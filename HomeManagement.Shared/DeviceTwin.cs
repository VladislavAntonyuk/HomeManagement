namespace HomeManagement.Shared;

/// <summary>
/// Describes a device and the actions it supports. Published retained to <see cref="DeviceTopics.Twin"/>.
/// </summary>
public sealed record DeviceTwin
{
    public required string Name { get; init; }

    public IReadOnlyList<DeviceAction> Actions { get; init; } = [];

    /// <summary>
    /// How often the device publishes <see cref="DeviceTopics.InfoKey"/> telemetry, in seconds.
    /// The hub treats an online device as offline when it misses several heartbeats, e.g. after a power outage the broker did not notice.
    /// </summary>
    public int? HeartbeatIntervalSeconds { get; init; }
}
