namespace HomeManagement.Shared;

/// <summary>
/// Describes a device and doubles as its heartbeat. Published as the <see cref="DeviceTopics.InfoKey"/> telemetry.
/// </summary>
public sealed record DeviceInfo
{
    public required string Name { get; init; }

    public string? Address { get; init; }

    /// <summary>
    /// Free list of text values, e.g. UptimeSeconds, Rssi, FreeHeap or Temperature.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metrics { get; init; } = new Dictionary<string, string>();
}
