using HomeManagement.Shared;

namespace HomeManagement.DeviceSdk;

public class DeviceAgentOptions
{
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// Topic-safe id of the device, also its MQTT client id. Derived from <see cref="DeviceName"/> when not set.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// How often Get actions and device info are published. Device info is also the heartbeat the hub uses to detect a device that lost power.
    /// </summary>
    public int TelemetryIntervalSeconds { get; set; } = 60;

    public string GetDeviceId() => string.IsNullOrWhiteSpace(DeviceId) ? DeviceTopics.ToDeviceId(DeviceName) : DeviceId;
}