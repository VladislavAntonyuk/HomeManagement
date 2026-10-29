using HomeManagement.Shared;

namespace HomeManagementDeviceApi;

public class DeviceSettings
{
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// Topic-safe id of the device. Derived from <see cref="DeviceName"/> when not set.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// How often Get commands and device info are published.
    /// </summary>
    public int TelemetryIntervalSeconds { get; set; } = 60;

    public string GetDeviceId() => string.IsNullOrWhiteSpace(DeviceId) ? DeviceTopics.ToDeviceId(DeviceName) : DeviceId;
}
