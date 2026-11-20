using System.Collections.Concurrent;
using HomeManagement.Infrastructure.Mqtt;
using HomeManagement.Shared;
using Silverback.Messaging.Configuration.Mqtt;
using Silverback.Messaging.Messages;
using Silverback.Messaging.Publishing;

namespace HomeManagement.Application.DeviceManagement;

/// <summary>
/// Hub side of the device protocol described by <see cref="DeviceTopics"/>.
/// </summary>
public sealed class DeviceHub(
    IServiceScopeFactory serviceScopeFactory,
    DeviceStateStore stateStore,
    DeviceRegistry registry,
    ILogger<DeviceHub> logger)
{
    public static TimeSpan CommandTimeout => DeviceTopics.CommandTimeout;

    private readonly ConcurrentDictionary<string, PendingCommand> _pendingCommands = new(StringComparer.Ordinal);

    /// <summary>
    /// Sends a command and waits for the response. The caller checks that the device is online.
    /// </summary>
    public async Task<CommandResponse> SendCommandAsync(string deviceId, string command, CancellationToken token = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var pending = new PendingCommand(deviceId, new TaskCompletionSource<CommandResponse>(TaskCreationOptions.RunContinuationsAsynchronously));
        _pendingCommands[requestId] = pending;
        try
        {
            await using (var scope = serviceScopeFactory.CreateAsyncScope())
            {
                var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
                await publisher.PublishAsync(new DeviceCommandRequest(deviceId, command, requestId), token);
            }

            return await pending.Completion.Task.WaitAsync(CommandTimeout, token);
        }
        catch (TimeoutException)
        {
            return new CommandResponse(false, $"Device '{deviceId}' did not respond within {CommandTimeout.TotalSeconds} seconds.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Failed to send command '{Command}' to device '{DeviceId}'.", command, deviceId);
            return new CommandResponse(false, e.Message);
        }
        finally
        {
            _pendingCommands.TryRemove(requestId, out _);
        }
    }

    public async Task ForgetDeviceAsync(string deviceId, CancellationToken token = default)
    {
        var topics = stateStore.GetTelemetryKeys(deviceId)
            .Select(key => DeviceTopics.Telemetry(deviceId, key))
            .Append(DeviceTopics.Status(deviceId))
            .Append(DeviceTopics.Twin(deviceId));

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
        foreach (var topic in topics)
        {
            // An empty retained message removes the retained message of the topic.
            await publisher.WrapAndPublishAsync(
                new StringMessage<RetainedMessageClear>(string.Empty),
                envelope => envelope.SetMqttDestinationTopic(topic),
                token);
        }

        stateStore.Remove(deviceId);
    }

    public async Task OnTwinAsync(IInboundEnvelope<DeviceTwin> envelope)
    {
        if (!TryGetDeviceTopic(envelope, out var deviceId, out _))
        {
            return;
        }

        var twin = envelope.Message;
        if (twin is null || string.IsNullOrWhiteSpace(twin.Name))
        {
            logger.LogWarning("Ignoring twin of device '{DeviceId}' without a name.", deviceId);
            return;
        }

        stateStore.SetHeartbeatInterval(deviceId, twin.HeartbeatIntervalSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);
        await registry.RegisterAsync(deviceId, twin);
        stateStore.NotifyDevicesChanged();
    }

    public void OnStatus(IInboundEnvelope<StringMessage<InboundStatusPayload>> envelope)
    {
        if (!TryGetDeviceTopic(envelope, out var deviceId, out _))
        {
            return;
        }

        bool? isOnline = envelope.Message?.Content switch
        {
            DeviceTopics.Online => true,
            DeviceTopics.Offline => false,
            _ => null
        };
        stateStore.SetStatus(deviceId, isOnline);
        logger.LogInformation("Device '{DeviceId}' is {Status}.", deviceId, isOnline switch { true => "online", false => "offline", null => "unknown" });

        if (isOnline != true)
        {
            FailPendingCommands(deviceId, $"Device '{deviceId}' went offline.");
        }
    }

    /// <summary>
    /// Completes the commands still waiting for a response of the device, because it will not send one.
    /// </summary>
    public void FailPendingCommands(string deviceId, string reason)
    {
        foreach (var pending in _pendingCommands.Values.Where(x => x.DeviceId == deviceId))
        {
            pending.Completion.TrySetResult(new CommandResponse(false, reason));
        }
    }

    public void OnTelemetry(IInboundEnvelope<StringMessage<InboundTelemetryPayload>> envelope)
    {
        if (TryGetDeviceTopic(envelope, out var deviceId, out var key) && !string.IsNullOrEmpty(key))
        {
            stateStore.SetTelemetry(deviceId, key, envelope.Message?.Content);
        }
    }

    public void OnResponse(IInboundEnvelope<CommandResponse> envelope)
    {
        if (envelope.GetMqttCorrelationDataAsString() is { Length: > 0 } requestId
            && _pendingCommands.TryGetValue(requestId, out var pending))
        {
            pending.Completion.TrySetResult(envelope.Message ?? new CommandResponse(false, "Empty response."));
        }
    }

    private static bool TryGetDeviceTopic(IRawInboundEnvelope envelope, out string deviceId, out string? key)
    {
        deviceId = string.Empty;
        key = null;
        if (envelope.Endpoint is not MqttConsumerEndpoint endpoint
            || !DeviceTopics.TryParse(endpoint.Topic, out var parsedDeviceId, out _, out key))
        {
            return false;
        }

        deviceId = parsedDeviceId;
        return true;
    }

    private sealed record PendingCommand(string DeviceId, TaskCompletionSource<CommandResponse> Completion);
}
