namespace HomeManagement.DeviceSdk.Mqtt;

// These discriminators belong to this library, so a device hosted in the hub process never receives the hub's own messages.

/// <summary>
/// Discriminator of the string payload of the device status topic.
/// </summary>
public sealed class DeviceStatusPayload
{
    private DeviceStatusPayload()
    {
    }
}

/// <summary>
/// Discriminator of the string payload of a telemetry topic.
/// </summary>
public sealed class DeviceTelemetryPayload
{
    private DeviceTelemetryPayload()
    {
    }
}

/// <summary>
/// Discriminator of the string payload of an event, published to a telemetry topic without the retain flag.
/// </summary>
public sealed class DeviceEventPayload
{
    private DeviceEventPayload()
    {
    }
}

/// <summary>
/// Discriminator of the payload of a command request. The device only uses its topic, response topic and correlation data.
/// </summary>
public sealed class DeviceCommandPayload
{
    private DeviceCommandPayload()
    {
    }
}

/// <summary>
/// The response a device sends to a command. Wraps <see cref="HomeManagement.Shared.CommandResponse"/> in a type of this library.
/// </summary>
public sealed record DeviceCommandResponse(bool IsSuccess, string Output);
