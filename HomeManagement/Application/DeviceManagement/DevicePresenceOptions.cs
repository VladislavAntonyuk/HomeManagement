namespace HomeManagement.Application.DeviceManagement;

/// <summary>
/// Detects devices that report online but stopped sending messages. Configured in the <c>DevicePresence</c> section.
/// </summary>
public class DevicePresenceOptions
{
    /// <summary>
    /// Heartbeat interval of a device whose twin does not announce one.
    /// </summary>
    public TimeSpan DefaultHeartbeatInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How many heartbeats in a row a device may miss before it is shown as offline.
    /// </summary>
    public int MissedHeartbeats { get; set; } = 3;

    /// <summary>
    /// How often the hub looks for devices that missed their heartbeats.
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(10);
}
