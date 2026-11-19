using System.Globalization;
using HomeManagement.Shared;

namespace HomeManagement.DeviceSdk;

public static class DeviceMetrics
{
    public static IReadOnlyDictionary<string, string> GetDefault() => new Dictionary<string, string>
    {
        { "UptimeSeconds", DeviceManager.GetUptime().ToString(CultureInfo.InvariantCulture) },
        { "Temperature", DeviceManager.GetTemperature().ToString(CultureInfo.InvariantCulture) }
    };
}
