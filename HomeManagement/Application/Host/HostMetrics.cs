using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HomeManagement.Application.Host;

/// <summary>
/// Live load of the machine the hub runs on, read from .NET and the Linux /proc files instead of parsing command output.
/// </summary>
public static class HostMetrics
{
    // Mounts that are not storage a person cares about: containers, snaps and per-session runtime folders.
    private static readonly string[] HiddenFileSystems = ["overlay", "squashfs", "tmpfs", "devtmpfs", "ramfs", "nsfs"];
    private static readonly string[] HiddenMountPrefixes = ["/run/", "/snap/", "/var/lib/docker/", "/var/snap/"];

    // Virtual links that docker creates for every container and bridge.
    private static readonly string[] HiddenInterfacePrefixes = ["lo", "veth", "br-", "docker", "virbr"];

    private static readonly Lazy<Task<CpuModel>> Model = new(ReadCpuModelAsync);

    public static IReadOnlyList<DiskUsage> Disks()
    {
        List<DiskUsage> disks = [];
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady
                    // .NET reports pseudo file systems, e.g. proc or cgroup, as RAM drives.
                    || drive.DriveType is DriveType.Ram or DriveType.NoRootDirectory or DriveType.CDRom
                    || HiddenFileSystems.Contains(drive.DriveFormat, StringComparer.OrdinalIgnoreCase)
                    || HiddenMountPrefixes.Any(x => drive.Name.StartsWith(x, StringComparison.Ordinal))
                    || drive.TotalSize == 0)
                {
                    continue;
                }

                // Like df: the space reserved for root is neither used nor available to people.
                var used = drive.TotalSize - drive.TotalFreeSpace;
                disks.Add(new DiskUsage(drive.Name, drive.DriveFormat, drive.TotalSize, used, drive.AvailableFreeSpace));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A disk that disappeared or is not readable is not shown.
            }
        }

        return [.. disks.OrderBy(x => x.MountPoint.Length).ThenBy(x => x.MountPoint, StringComparer.Ordinal)];
    }

    public static IReadOnlyList<NetworkLink> Network()
        => [.. NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && !HiddenInterfacePrefixes.Any(prefix => x.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(ToLink)
            // A disconnected adapter without an address, e.g. an unused Wi-Fi, says nothing.
            .Where(x => x.IsUp || x.Addresses.Count > 0)
            .OrderByDescending(x => x.IsUp)
            .ThenBy(x => x.Kind)
            .ThenBy(x => x.Name, StringComparer.Ordinal)];

    public static MemoryUsage? Memory()
    {
        var values = ReadProcFile("/proc/meminfo")?
            .Select(line => line.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => ParseKilobytes(parts[1]), StringComparer.Ordinal);

        if (values is null || !values.TryGetValue("MemTotal", out var total) || !values.TryGetValue("MemAvailable", out var available))
        {
            return null;
        }

        return new MemoryUsage(
            total,
            available,
            values.GetValueOrDefault("Cached") + values.GetValueOrDefault("Buffers"),
            values.GetValueOrDefault("SwapTotal"),
            values.GetValueOrDefault("SwapFree"));
    }

    /// <summary>
    /// CPU time counters; the usage is the busy share between two samples.
    /// </summary>
    public static CpuSample? SampleCpu()
    {
        // cpu  user nice system idle iowait irq softirq steal guest guest_nice
        var line = ReadProcFile("/proc/stat")?.FirstOrDefault(x => x.StartsWith("cpu ", StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        var counters = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).Select(long.Parse).ToArray();
        var idle = counters[3] + counters[4];
        return new CpuSample(counters.Sum() - idle, counters.Sum());
    }

    public static double? CpuUsagePercent(CpuSample? previous, CpuSample? current)
    {
        if (previous is null || current is null || current.Total <= previous.Total)
        {
            return null;
        }

        return 100.0 * (current.Busy - previous.Busy) / (current.Total - previous.Total);
    }

    /// <summary>
    /// Average number of runnable processes over the last 1, 5 and 15 minutes.
    /// </summary>
    public static IReadOnlyList<double> LoadAverage()
    {
        var parts = ReadProcFile("/proc/loadavg")?.FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is null ? [] : [.. parts.Take(3).Select(x => double.Parse(x, CultureInfo.InvariantCulture))];
    }

    public static Task<CpuModel> CpuModelAsync() => Model.Value;

    private static NetworkLink ToLink(NetworkInterface adapter)
    {
        var addresses = adapter.GetIPProperties().UnicastAddresses
            .Select(x => x.Address)
            .Where(x => x.AddressFamily == AddressFamily.InterNetwork
                        || x is { AddressFamily: AddressFamily.InterNetworkV6, IsIPv6LinkLocal: false })
            .OrderBy(x => x.AddressFamily)
            .Select(x => x.ToString())
            .ToArray();

        var statistics = adapter.GetIPStatistics();

        // A VPN tunnel has no carrier, so Linux reports it as UNKNOWN while it carries traffic.
        var isUp = adapter.OperationalStatus == OperationalStatus.Up
                   || (adapter.OperationalStatus == OperationalStatus.Unknown && addresses.Length > 0);

        return new NetworkLink(
            adapter.Name,
            KindOf(adapter),
            isUp,
            addresses,
            statistics.BytesReceived,
            statistics.BytesSent,
            isUp && adapter.Speed > 0 ? adapter.Speed / 1_000_000 : null);
    }

    private static NetworkKind KindOf(NetworkInterface adapter) => adapter.Name switch
    {
        _ when adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 => NetworkKind.WiFi,
        ['w', 'l', ..] => NetworkKind.WiFi,
        ['e', 'n', ..] or ['e', 't', 'h', ..] => NetworkKind.Ethernet,
        ['t', 'a', 'i', 'l', ..] or ['w', 'g', ..] or ['t', 'u', 'n', ..] => NetworkKind.Vpn,
        _ when adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet => NetworkKind.Ethernet,
        _ => NetworkKind.Other
    };

    private static async Task<CpuModel> ReadCpuModelAsync()
    {
        var architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        var logical = Environment.ProcessorCount;
        if (!OperatingSystem.IsLinux())
        {
            return new CpuModel([], architecture, logical);
        }

        string output;
        try
        {
            var result = await Process.RunAndCaptureTextAsync(new ProcessStartInfo("lscpu") { RedirectStandardOutput = true, RedirectStandardError = true });
            output = result.StandardOutput;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new CpuModel([], architecture, logical);
        }

        // A big.LITTLE board, e.g. RK3588, lists one "Model name" section per core cluster, each with its own core count.
        List<CpuCluster> clusters = [];
        string? name = null;
        int cores = 0, sockets = 1, threads = 1;

        void Flush()
        {
            if (name is not null)
            {
                clusters.Add(new CpuCluster(name, cores > 0 ? cores * sockets * threads : null));
            }
        }

        foreach (var line in output.Split('\n'))
        {
            var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            var value = parts[1];
            switch (parts[0])
            {
                case "Architecture":
                    architecture = value;
                    break;
                case "Model name":
                    Flush();
                    (name, cores, sockets, threads) = (value, 0, 1, 1);
                    break;
                case "Core(s) per socket" or "Core(s) per cluster":
                    cores = int.TryParse(value, out var c) ? c : 0;
                    break;
                case "Socket(s)" or "Cluster(s)":
                    sockets = int.TryParse(value, out var s) ? s : sockets;
                    break;
                case "Thread(s) per core":
                    threads = int.TryParse(value, out var t) ? t : 1;
                    break;
            }
        }

        Flush();
        return new CpuModel(clusters, architecture, logical);
    }

    private static string[]? ReadProcFile(string path)
    {
        try
        {
            return OperatingSystem.IsLinux() ? File.ReadAllLines(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // /proc/meminfo values look like "3975536 kB".
    private static long ParseKilobytes(string value)
        => long.TryParse(value.Split(' ', 2)[0], CultureInfo.InvariantCulture, out var kilobytes) ? kilobytes * 1024 : 0;
}

public sealed record DiskUsage(string MountPoint, string FileSystem, long TotalBytes, long UsedBytes, long AvailableBytes)
{
    public double UsedPercent => UsedBytes + AvailableBytes == 0 ? 0 : 100.0 * UsedBytes / (UsedBytes + AvailableBytes);
}

public enum NetworkKind
{
    Ethernet,
    WiFi,
    Vpn,
    Other
}

public sealed record NetworkLink(string Name, NetworkKind Kind, bool IsUp, IReadOnlyList<string> Addresses, long ReceivedBytes, long SentBytes, long? SpeedMbps);

/// <param name="AvailableBytes">Memory programs can still get, including the cache the kernel gives back on demand.</param>
public sealed record MemoryUsage(long TotalBytes, long AvailableBytes, long CacheBytes, long SwapTotalBytes, long SwapFreeBytes)
{
    public long UsedBytes => TotalBytes - AvailableBytes;
    public double UsedPercent => TotalBytes == 0 ? 0 : 100.0 * UsedBytes / TotalBytes;
    public long SwapUsedBytes => SwapTotalBytes - SwapFreeBytes;
    public double SwapUsedPercent => SwapTotalBytes == 0 ? 0 : 100.0 * SwapUsedBytes / SwapTotalBytes;
}

public sealed record CpuSample(long Busy, long Total);

/// <param name="Cores">Number of cores of this model, or null when lscpu does not report it.</param>
public sealed record CpuCluster(string Name, int? Cores);

public sealed record CpuModel(IReadOnlyList<CpuCluster> Clusters, string Architecture, int LogicalCpus);
