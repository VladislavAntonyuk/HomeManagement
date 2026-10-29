using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using HomeManagement.Shared;
using HomeManagementDeviceApi.Mqtt;
using Microsoft.Extensions.Options;
using Silverback.Messaging.Broker.Callbacks;
using Silverback.Messaging.Configuration.Mqtt;
using Silverback.Messaging.Messages;
using Silverback.Messaging.Publishing;

namespace HomeManagementDeviceApi;

/// <summary>
/// Device side of the protocol described by <see cref="DeviceTopics"/>: publishes the twin, status and telemetry, and executes commands.
/// </summary>
public sealed class DeviceAgent(
    IOptions<DeviceSettings> deviceOptions,
    IOptions<CommandsSettings> commandsOptions,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<DeviceAgent> logger) : BackgroundService, IMqttClientConnectedCallback, IMqttClientDisconnectingCallback
{
    private const string InfoKey = "info";
    private const string ShutdownKey = "shutdown";
    private const string RestartKey = "restart";

    private static readonly TimeSpan HandledRequestRetention = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, DateTime> _handledRequests = new();
    private readonly SemaphoreSlim _publishTelemetrySignal = new(0, 1);
    private readonly string _deviceId = deviceOptions.Value.GetDeviceId();
    private volatile bool _isConnected;

    public Task OnClientConnectedAsync(MqttClientConfiguration configuration)
    {
        _isConnected = true;
        logger.LogInformation("Connected to the MQTT broker as '{DeviceId}'.", _deviceId);

        // Not awaited: the client must finish connecting before it can deliver these messages.
        _ = AnnounceAsync();
        return Task.CompletedTask;
    }

    public async Task OnClientDisconnectingAsync(MqttClientConfiguration configuration)
    {
        _isConnected = false;

        // The last will is only sent on an unexpected disconnect.
        await PublishAsync(new StringMessage<DeviceStatusPayload>(DeviceTopics.Offline));
    }

    public async Task OnCommandAsync(IInboundEnvelope<StringMessage<DeviceCommandPayload>> envelope)
    {
        if (envelope.Endpoint is not MqttConsumerEndpoint endpoint
            || !DeviceTopics.TryParse(endpoint.Topic, out _, out _, out var key)
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
        await scope.ServiceProvider.GetRequiredService<IPublisher>().WrapAndPublishAsync(response, outbound =>
        {
            outbound.SetMqttDestinationTopic(responseTopic);
            if (correlationData is not null)
            {
                outbound.SetMqttCorrelationData(correlationData);
            }
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var telemetryInterval = TimeSpan.FromSeconds(Math.Max(1, deviceOptions.Value.TelemetryIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            // Wakes up early when the device (re)connects, so telemetry is published right away.
            await _publishTelemetrySignal.WaitAsync(telemetryInterval, stoppingToken);
            if (!_isConnected)
            {
                continue;
            }

            await TryPublishTelemetryAsync(InfoKey, () => Task.FromResult<string?>(JsonSerializer.Serialize(GetDeviceInfo(), JsonOptions)), stoppingToken);
            foreach (var command in commandsOptions.Value.Commands.Where(x => x.Method == CommandType.Get))
            {
                await TryPublishTelemetryAsync(command.Endpoint, async () =>
                {
                    var result = await CommandRunner.RunAsync(command, stoppingToken);
                    if (!result.IsSuccess)
                    {
                        logger.LogWarning("Command '{Command}' failed: {Error}", command.Name, result.Output);
                        return null;
                    }

                    return result.Output;
                }, stoppingToken);
            }
        }
    }

    private async Task AnnounceAsync()
    {
        try
        {
            await PublishAsync(BuildTwin());
            await PublishAsync(new StringMessage<DeviceStatusPayload>(DeviceTopics.Online));
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

    private DeviceTwin BuildTwin()
    {
        var actions = new List<DeviceAction>
        {
            new("INFO", CommandType.Get, InfoKey),
            new("SHUTDOWN", CommandType.Post, ShutdownKey),
            new("RESTART", CommandType.Post, RestartKey)
        };
        actions.AddRange(commandsOptions.Value.Commands.Select(x => new DeviceAction(x.Name, x.Method, x.Endpoint)));

        return new DeviceTwin
        {
            Name = deviceOptions.Value.DeviceName,
            Actions = actions
        };
    }

    private async Task TryPublishTelemetryAsync(string key, Func<Task<string?>> readValue, CancellationToken token)
    {
        try
        {
            if (await readValue() is not { } value)
            {
                return;
            }

            await using var scope = serviceScopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IPublisher>().WrapAndPublishAsync(
                new StringMessage<DeviceTelemetryPayload>(value),
                envelope => envelope.SetMqttDestinationTopic(DeviceTopics.Telemetry(_deviceId, key)),
                token);
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            logger.LogError(e, "Failed to publish telemetry '{Key}'.", key);
        }
    }

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
        switch (key)
        {
            case ShutdownKey:
                Process.StartAndForget("poweroff");
                return new CommandResponse(true, "Shutting down");
            case RestartKey:
                Process.StartAndForget("reboot");
                return new CommandResponse(true, "Restarting");
        }

        var command = commandsOptions.Value.Commands.FirstOrDefault(x => x.Endpoint == key);
        if (command is null)
        {
            return new CommandResponse(false, $"Unknown command '{key}'.");
        }

        try
        {
            return await CommandRunner.RunAsync(command, CancellationToken.None);
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
        Name = deviceOptions.Value.DeviceName,
        Address = NetworkManager.GetAddress(),
        Metrics = new Dictionary<string, string>
        {
            { "UptimeSeconds", DeviceManager.GetUptime().ToString(CultureInfo.InvariantCulture) },
            { "Temperature", DeviceManager.GetTemperature().ToString(CultureInfo.InvariantCulture) }
        }
    };
}
