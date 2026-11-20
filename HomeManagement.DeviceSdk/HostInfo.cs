using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HomeManagement.DeviceSdk;

/// <summary>
/// Facts about the machine a device, or the hub, runs on.
/// </summary>
public static class HostInfo
{
    private const string ThermalZone = "/sys/class/thermal/thermal_zone0/temp";

    /// <summary>
    /// Time since the machine started.
    /// </summary>
    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>
    /// CPU temperature in °C, or null when the machine does not report it. Linux boards report it in their thermal zone.
    /// </summary>
    public static double? CpuTemperature
    {
        get
        {
            if (!OperatingSystem.IsLinux() || !File.Exists(ThermalZone))
            {
                return null;
            }

            try
            {
                return double.Parse(File.ReadAllText(ThermalZone), CultureInfo.InvariantCulture) / 1000.0;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                return null;
            }
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
}
