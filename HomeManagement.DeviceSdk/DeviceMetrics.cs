using System.Globalization;

namespace HomeManagement.DeviceSdk;

public static class DeviceMetrics
{
    /// <summary>
    /// Uptime, and the CPU temperature when the machine reports it.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetDefault()
    {
        Dictionary<string, string> metrics = new()
        {
            ["UptimeSeconds"] = ((long)HostInfo.Uptime.TotalSeconds).ToString(CultureInfo.InvariantCulture)
        };
        if (HostInfo.CpuTemperature is { } temperature)
        {
            metrics["Temperature"] = temperature.ToString(CultureInfo.InvariantCulture);
        }

        return metrics;
    }
}
