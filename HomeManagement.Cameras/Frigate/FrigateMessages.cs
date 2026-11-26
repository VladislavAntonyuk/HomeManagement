using System.Text.Json.Serialization;

namespace HomeManagement.Cameras.Frigate;

/// <summary>
/// Discriminator of the string payload of <see cref="FrigateTopics.Available"/>.
/// </summary>
public sealed class FrigateAvailabilityPayload
{
    private FrigateAvailabilityPayload()
    {
    }
}

/// <summary>
/// Discriminator of the string payload of the Frigate state topics, e.g. <see cref="FrigateTopics.ProfileState"/>.
/// </summary>
public sealed class FrigateStatePayload
{
    private FrigateStatePayload()
    {
    }
}

/// <summary>
/// Discriminator of the string payload sent to Frigate set topics, e.g. <see cref="FrigateTopics.ProfileSet"/>.
/// </summary>
public sealed class FrigateCommandPayload
{
    private FrigateCommandPayload()
    {
    }
}

public sealed class FrigateEventMessage
{
    /// <summary>
    /// new, update or end.
    /// </summary>
    public string? Type { get; set; }

    public FrigateTrackedObject? After { get; set; }
}

/// <summary>
/// The parts of <see cref="FrigateTopics.Stats"/> the device reports, e.g.
/// <c>{ "service": { "uptime": 290678, "version": "0.18.2-cfa08c5" }, "gpu_usages": { "rockchip": { "temp": 33.3 } } }</c>.
/// </summary>
public sealed class FrigateStatsMessage
{
    public FrigateServiceStats? Service { get; set; }

    public Dictionary<string, FrigateHardwareUsage>? GpuUsages { get; set; }

    public Dictionary<string, FrigateHardwareUsage>? NpuUsages { get; set; }

    /// <summary>
    /// Every temperature Frigate reports in °C: of the Coral TPUs, GPUs and NPUs.
    /// </summary>
    public IEnumerable<double> GetTemperatures()
    {
        IEnumerable<double> coral = Service?.Temperatures?.Values ?? Enumerable.Empty<double>();
        IEnumerable<FrigateHardwareUsage> gpus = GpuUsages?.Values ?? Enumerable.Empty<FrigateHardwareUsage>();
        IEnumerable<FrigateHardwareUsage> npus = NpuUsages?.Values ?? Enumerable.Empty<FrigateHardwareUsage>();
        return coral.Concat(gpus.Concat(npus).Select(x => x.Temp).OfType<double>());
    }
}

public sealed class FrigateServiceStats
{
    /// <summary>
    /// Seconds since Frigate started.
    /// </summary>
    public long Uptime { get; set; }

    public string? Version { get; set; }

    /// <summary>
    /// Temperatures of the Coral TPUs in °C, by device, e.g. apex_0. Missing without a Coral.
    /// </summary>
    public Dictionary<string, double>? Temperatures { get; set; }
}

public sealed class FrigateHardwareUsage
{
    /// <summary>
    /// Temperature in °C, reported only by some vendors, e.g. Rockchip.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public double? Temp { get; set; }
}

public sealed class FrigateTrackedObject
{
    public required string Id { get; set; }

    public required string Camera { get; set; }

    public string? Label { get; set; }

    public float TopScore { get; set; }

    public bool HasSnapshot { get; set; }
}
