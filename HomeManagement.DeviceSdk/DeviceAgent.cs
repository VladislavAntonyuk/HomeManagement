using System.Collections.Concurrent;
using System.Text.Json;
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
    ILogger<DeviceAgent> logger) : BackgroundService, IMqttClientConnectedCallback, IMqttClientDisconnectingCallback
{
    private static readonly TimeSpan HandledRequestRetention = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, DateTime> _handledRequests = new();
    private readonly SemaphoreSlim _publishTelemetrySignal = new(0, 1);
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
            await _publishTelemetrySignal.WaitAsync(TelemetryInterval, stoppingToken);
            if (!telemetryPublisher.IsConnected)
            {
                continue;
            }

            foreach (var action in handler.GetActions().Where(x => x.CommandType == CommandType.Get))
            {
                try
                {
                    if (await handler.ReadAsync(action.Command, stoppingToken) is { } value)
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
            if (_publishTelemetrySignal.CurrentCount == 0)
            {
                _publishTelemetrySignal.Release();
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to publish the twin and status.");
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
            var actions = new List<DeviceAction> { new("INFO", CommandType.Get, DeviceTopics.InfoKey) };
            actions.AddRange(handler.GetActions());
            await PublishAsync(new DeviceTwin
            {
                Name = options.Value.DeviceName,
                Actions = actions,
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
        => telemetryPublisher.TryPublishAsync(DeviceTopics.InfoKey, JsonSerializer.Serialize(GetDeviceInfo(), JsonOptions), token);

    private bool TryMarkHandled(byte[]? correlationData)
    {
        if (correlationData is not { Length: > 0 })
        {
            return true;
        }

        var now = DateTime.UtcNow;
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

        try
        {
            return await handler.ExecuteAsync(key, CancellationToken.None);
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
        Address = NetworkManager.GetLocalIp(),
        Metrics = new Dictionary<string, string>(handler.GetMetrics())
    };
}
