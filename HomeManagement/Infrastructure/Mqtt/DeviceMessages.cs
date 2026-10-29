using System.Text.Json.Serialization;

namespace HomeManagement.Infrastructure.Mqtt;

/// <summary>
/// Command request sent to a device. The device only uses the topic, the response topic and the correlation data, so the payload is empty JSON.
/// </summary>
public sealed record DeviceCommandRequest(
    [property: JsonIgnore] string DeviceId,
    [property: JsonIgnore] string Command,
    [property: JsonIgnore] string RequestId);

/// <summary>
/// Discriminator of the string payload of a device status topic.
/// </summary>
public sealed class DeviceStatusPayload
{
    private DeviceStatusPayload()
    {
    }
}

/// <summary>
/// Discriminator of the string payload of a device telemetry topic.
/// </summary>
public sealed class DeviceTelemetryPayload
{
    private DeviceTelemetryPayload()
    {
    }
}

/// <summary>
/// Discriminator of an empty retained message that removes the retained message of a topic.
/// </summary>
public sealed class RetainedMessageClear
{
    private RetainedMessageClear()
    {
    }
}
