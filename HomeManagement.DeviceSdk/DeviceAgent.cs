using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using HomeManagement.DeviceSdk.Mqtt;
using HomeManagement.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Silverback.Messaging.Broker.Callbacks;
using Silverback.Messaging.Configuration.Mqtt;
using Silverback.Messaging.Messages;
using Silverback.Messaging.Publishing;

namespace HomeManagement.DeviceSdk;

/// <summary>
/// Device side of the protocol described by <see cref="DeviceTopics"/>: publishes the twin, status and telemetry, and runs commands through <see cref="IDeviceHandler"/>.
/// The device only knows the broker, never the hub.
/// </summary>
public sealed class DeviceAgent(
    IOptions<DeviceAgentOptions> options,
    IDeviceHandler handler,
    DeviceTelemetryPublisher telemetryPublisher,
    IServiceScopeFactory serviceScopeFactory,
    TimeProvider timeProvider,
    ILogger<DeviceAgent> logger) : BackgroundService, IMqttClientConnectedCallback, IMqttClientDisconnectingCallback
{
    private static readonly TimeSpan HandledRequestRetention = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _handledRequests = new();

    // Holds at most one wake-up, so overlapping reconnects cannot overflow it.
    private readonly Channel<bool> _publishTelemetrySignal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly SemaphoreSlim _twinLock = new(1, 1);
    private readonly string _deviceId = options.Value.GetDeviceId();

    public Task OnClientConnectedAsync(MqttClientConfiguration configuration)
    {
        // The callback runs for every MQTT client of the process, e.g. the hub's own client when the device is hosted in the hub.
        if (!IsOwnClient(configuration))
        {
            return Task.CompletedTask;
        }

        telemetryPublisher.IsConnected = true;
        logger.LogInformation("Connected to the MQTT broker as '{DeviceId}'.", _deviceId);

        // Not awaited: the client must finish connecting before it can deliver these messages.
        _ = AnnounceAsync();
        return Task.CompletedTask;
    }

    public async Task OnClientDisconnectingAsync(MqttClientConfiguration configuration)
    {
        if (!IsOwnClient(configuration))
        {
            return;
        }

        telemetryPublisher.IsConnected = false;

        // The last will is only sent on an unexpected disconnect.
        await PublishAsync(new StringMessage<DeviceStatusPayload>(DeviceTopics.Offline));
    }

    public async Task OnCommandAsync(IInboundEnvelope<StringMessage<DeviceCommandPayload>> envelope)
    {
        if (envelope.Endpoint is not MqttConsumerEndpoint endpoint
            || !DeviceTopics.TryParse(endpoint.Topic, out var deviceId, out _, out var key)
            || deviceId != _deviceId
            || string.IsNullOrEmpty(key))
        {
            return;
        }

        var correlationData = envelope.GetMqttCorrelationData();
        if (!TryMarkHandled(correlationData))
        {
            logger.LogInformation("Skipping duplicate command '{Key}'.", key);
            return;
        }

        var response = await ExecuteCommandAsync(key);
        logger.LogInformation("Command '{Key}' executed. Success: {IsSuccess}.", key, response.IsSuccess);

        if (envelope.GetMqttResponseTopic() is not { Length: > 0 } responseTopic)
        {
            return;
        }

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPublisher>().WrapAndPublishAsync(
            new DeviceCommandResponse(response.IsSuccess, response.Output),
            outbound =>
            {
                outbound.SetMqttDestinationTopic(responseTopic);
                if (correlationData is not null)
                {
                    outbound.SetMqttCorrelationData(correlationData);
                }
            });
    }

    private TimeSpan TelemetryInterval => TimeSpan.FromSeconds(Math.Max(1, options.Value.TelemetryIntervalSeconds));

    // The heartbeat runs on its own, so a slow Get action cannot make the hub think the device is offline.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        handler.ActionsChanged += OnActionsChanged;
        try
        {
            await Task.WhenAll(PublishHeartbeatsAsync(stoppingToken), PublishTelemetryAsync(stoppingToken));
        }
        finally
        {
            handler.ActionsChanged -= OnActionsChanged;
        }
    }

    private bool IsOwnClient(MqttClientConfiguration configuration) => configuration.ClientId == _deviceId;

    private void OnActionsChanged()
    {
        if (telemetryPublisher.IsConnected)
        {
            _ = PublishTwinAsync();
        }
    }

    private async Task PublishHeartbeatsAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TelemetryInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryPublishInfoAsync(stoppingToken);
        }
    }

    private async Task PublishTelemetryAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Wakes up early when the device (re)connects, so telemetry is published right away.
            await WaitForTelemetrySignalAsync(stoppingToken);
            if (!telemetryPublisher.IsConnected)
            {
                continue;
            }

            foreach (var action in handler.GetActions().Where(x => x.CommandType == CommandType.Get))
            {
                try
                {
                    // A hung read must not stop the telemetry of the other actions.
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(DeviceTopics.CommandTimeout);
                    if (await handler.ReadAsync(action.Command, timeout.Token) is { } value)
                    {
                        await telemetryPublisher.TryPublishAsync(action.Command, value, stoppingToken);
                    }
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(e, "Failed to read '{Action}'.", action.Action);
                }
            }
        }
    }

    private async Task AnnounceAsync()
    {
        try
        {
            await WaitUntilHandlerReadyAsync();
            await PublishTwinAsync();
            await PublishAsync(new StringMessage<DeviceStatusPayload>(DeviceTopics.Online));
            await TryPublishInfoAsync(CancellationToken.None);
            _publishTelemetrySignal.Writer.TryWrite(true);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to publish the twin and status.");
        }
    }

    private async Task WaitForTelemetrySignalAsync(CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TelemetryInterval);
        try
        {
            await _publishTelemetrySignal.Reader.WaitToReadAsync(timeout.Token);
            _publishTelemetrySignal.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // The regular interval elapsed.
        }
    }

    private async Task WaitUntilHandlerReadyAsync()
    {
        using var timeout = new CancellationTokenSource(ReadyTimeout);
        try
        {
            await handler.WaitUntilReadyAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            // The twin is published again when the actions are known.
            logger.LogWarning("The device was not ready within {Timeout}; announcing the actions known so far.", ReadyTimeout);
        }
    }

    private async Task PublishTwinAsync()
    {
        await _twinLock.WaitAsync();
        try
        {
            await PublishAsync(new DeviceTwin
            {
                Name = options.Value.DeviceName,
                Actions = [new("INFO", CommandType.Get, DeviceTopics.InfoKey), .. handler.GetActions()],
                HeartbeatIntervalSeconds = (int)TelemetryInterval.TotalSeconds
            });
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to publish the twin.");
        }
        finally
        {
            _twinLock.Release();
        }
    }

    private Task TryPublishInfoAsync(CancellationToken token)
        => telemetryPublisher.TryPublishAsync(DeviceTopics.InfoKey, JsonSerializer.Serialize(GetDeviceInfo(), JsonSerializerOptions.Web), token);

    private bool TryMarkHandled(byte[]? correlationData)
    {
        if (correlationData is not { Length: > 0 })
        {
            return true;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var (requestId, handledUtc) in _handledRequests)
        {
            if (now - handledUtc > HandledRequestRetention)
            {
                _handledRequests.TryRemove(requestId, out _);
            }
        }

        return _handledRequests.TryAdd(Convert.ToHexString(correlationData), now);
    }

    private async Task<CommandResponse> ExecuteCommandAsync(string key)
    {
        if (!handler.GetActions().Any(x => x.CommandType == CommandType.Post && x.Command == key))
        {
            return new CommandResponse(false, $"Unknown command '{key}'.");
        }

        // The hub stops waiting after the command timeout, so the device stops too.
        using var timeout = new CancellationTokenSource(DeviceTopics.CommandTimeout);
        try
        {
            return await handler.ExecuteAsync(key, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return new CommandResponse(false, $"'{key}' did not finish within {DeviceTopics.CommandTimeout.TotalSeconds} seconds.");
        }
        catch (Exception e)
        {
            return new CommandResponse(false, e.Message);
        }
    }

    private async Task PublishAsync<TMessage>(TMessage message)
        where TMessage : class
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPublisher>().PublishAsync(message);
    }

    private DeviceInfo GetDeviceInfo() => new()
    {
        Name = options.Value.DeviceName,
        Address = HostInfo.LocalIpAddress,
        Metrics = handler.GetMetrics()
    };
}
