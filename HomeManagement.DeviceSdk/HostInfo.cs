using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HomeManagement.DeviceSdk;

/// <summary>
/// Facts about the machine a device, or the hub, runs on.
/// </summary>
public static class HostInfo
{
    private const string ThermalRoot = "/sys/class/thermal";
    private const string ThermalZonePrefix = "thermal_zone";

    /// <summary>
    /// Time since the machine started.
    /// </summary>
    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>
    /// CPU temperature in °C, or null when the machine does not report it. Linux boards report it in their first thermal zone.
    /// </summary>
    public static double? CpuTemperature => ReadThermalZone(Path.Combine(ThermalRoot, ThermalZonePrefix + "0"))?.Celsius;

    /// <summary>
    /// Every temperature sensor of the machine, e.g. CPU, GPU and DDR on an Orange Pi. Empty when there are none.
    /// </summary>
    public static IReadOnlyList<HostTemperature> Temperatures
    {
        get
        {
            if (!OperatingSystem.IsLinux() || !Directory.Exists(ThermalRoot))
            {
                return [];
            }

            return [.. Directory.EnumerateDirectories(ThermalRoot, ThermalZonePrefix + "*")
                .OrderBy(x => int.TryParse(Path.GetFileName(x).AsSpan(ThermalZonePrefix.Length), out var number) ? number : int.MaxValue)
                .Select(ReadThermalZone)
                .OfType<HostTemperature>()];
        }
    }

    /// <summary>
    /// The IPv4 address of the first network interface that is up, or null without a network.
    /// </summary>
    public static string? LocalIpAddress => NetworkInterface.GetAllNetworkInterfaces()
        .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(x => x.GetIPProperties().UnicastAddresses)
        .FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
        ?.Address.ToString();

    private static HostTemperature? ReadThermalZone(string zone)
    {
        try
        {
            var milliCelsius = double.Parse(File.ReadAllText(Path.Combine(zone, "temp")), CultureInfo.InvariantCulture);
            var typeFile = Path.Combine(zone, "type");
            var sensor = File.Exists(typeFile) ? File.ReadAllText(typeFile).Trim() : Path.GetFileName(zone);
            return new HostTemperature(sensor, milliCelsius / 1000.0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }
}

/// <param name="Sensor">Name the machine gives the sensor, e.g. cpu-thermal.</param>
public sealed record HostTemperature(string Sensor, double Celsius);
