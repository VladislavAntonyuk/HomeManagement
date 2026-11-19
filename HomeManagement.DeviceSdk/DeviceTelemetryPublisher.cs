using HomeManagement.DeviceSdk.Mqtt;
using HomeManagement.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Silverback.Messaging.Messages;
using Silverback.Messaging.Publishing;

namespace HomeManagement.DeviceSdk;

/// <summary>
/// Publishes telemetry of the device without waiting for the next telemetry interval: state that changed, or an event.
/// </summary>
public sealed class DeviceTelemetryPublisher(
    IServiceScopeFactory serviceScopeFactory,
    IOptions<DeviceAgentOptions> options,
    ILogger<DeviceTelemetryPublisher> logger)
{
    private readonly string _deviceId = options.Value.GetDeviceId();

    /// <summary>
    /// Whether the device is connected to the broker. Telemetry published while disconnected is dropped.
    /// </summary>
    public bool IsConnected { get; internal set; }

    /// <summary>
    /// Publishes the value of a <see cref="CommandType.Get"/> action, retained.
    /// </summary>
    /// <returns>False when the device is not connected or publishing failed.</returns>
    public Task<bool> TryPublishAsync(string key, string value, CancellationToken token = default)
        => TryPublishAsync(new StringMessage<DeviceTelemetryPayload>(value), key, token);

    /// <summary>
    /// Publishes an occurrence of a <see cref="CommandType.Event"/> action, not retained.
    /// </summary>
    /// <returns>False when the device is not connected or publishing failed.</returns>
    public Task<bool> TryPublishEventAsync(string key, string value, CancellationToken token = default)
        => TryPublishAsync(new StringMessage<DeviceEventPayload>(value), key, token);

    private async Task<bool> TryPublishAsync<TMessage>(TMessage message, string key, CancellationToken token)
        where TMessage : class
    {
        if (!IsConnected)
        {
            return false;
        }

        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IPublisher>().WrapAndPublishAsync(
                message,
                envelope => envelope.SetMqttDestinationTopic(DeviceTopics.Telemetry(_deviceId, key)),
                token);
            return true;
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            logger.LogError(e, "Failed to publish telemetry '{Key}'.", key);
            return false;
        }
    }
}
