namespace HomeManagementDeviceApi.Mqtt;

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
/// Discriminator of the payload of a command request. The device only uses its topic, response topic and correlation data.
/// </summary>
public sealed class DeviceCommandPayload
{
    private DeviceCommandPayload()
    {
    }
}
