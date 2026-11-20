using HomeManagement.Shared.Api;

namespace HomeManagement.Application.DeviceManagement;

/// <summary>
/// What the hub knows about whether a device is reachable.
/// </summary>
/// <param name="LastSeenUtc">When the hub last received any message of the device.</param>
/// <param name="HeartbeatInterval">How often the device promised to publish its info.</param>
public sealed record DevicePresence(DevicePresenceStatus Status, DateTimeOffset? LastSeenUtc, TimeSpan HeartbeatInterval)
{
    /// <summary>
    /// Whether the device can run actions now.
    /// </summary>
    public bool IsOnline => Status is DevicePresenceStatus.Online;
}
