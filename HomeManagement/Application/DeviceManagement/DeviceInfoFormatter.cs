using System.Globalization;
using System.Text.RegularExpressions;

namespace HomeManagement.Application.DeviceManagement;

public sealed record FormattedMetric(string Name, string Value, string Label, string DisplayValue);

/// <summary>
/// Formats device presence and the metrics devices publish in their info telemetry for display.
/// </summary>
public static partial class DeviceInfoFormatter
{
    public static string FormatStatus(DevicePresence? presence) => presence switch
    {
        { IsHeartbeatMissed: true } => "Not responding",
        { IsOnline: true } => "Online",
        { IsOnline: false } => "Offline",
        _ => "Unknown"
    };

    public static string FormatAgo(DateTimeOffset value, DateTimeOffset now)
    {
        var elapsed = now - value;
        if (elapsed < TimeSpan.FromSeconds(10))
        {
            return "just now";
        }

        return elapsed switch
        {
            { TotalMinutes: < 1 } => $"{(int)elapsed.TotalSeconds} s ago",
            { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes} min ago",
            { TotalDays: < 1 } => $"{(int)elapsed.TotalHours} h ago",
            _ => $"{(int)elapsed.TotalDays} d ago"
        };
    }

    public static string FormatDuration(TimeSpan value)
    {
        if (value.TotalMinutes < 1)
        {
            return $"{(int)value.TotalSeconds} s";
        }

        var parts = new List<string>(3);
        if (value.Days > 0)
        {
            parts.Add($"{value.Days} d");
        }

        if (value.Hours > 0)
        {
            parts.Add($"{value.Hours} h");
        }

        if (value.Days == 0 && value.Minutes > 0)
        {
            parts.Add($"{value.Minutes} min");
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Turns a metric into a label and a value, e.g. "UptimeSeconds" "93784" into "Uptime" "1 d 2 h".
    /// Unknown metrics keep their value and only get a readable label.
    /// </summary>
    public static (string Label, string Value) FormatMetric(string name, string value)
    {
        var number = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : (double?)null;
        return (name, number) switch
        {
            ("UptimeSeconds", { } seconds) => ("Uptime", FormatDuration(TimeSpan.FromSeconds(seconds))),
            ("Rssi", { } rssi) => ("Wi-Fi signal", $"{rssi.ToString(CultureInfo.InvariantCulture)} dBm"),
            ("FreeHeap", { } bytes) => ("Free memory", FormatBytes(bytes)),
            ("Temperature", { } celsius) => ("Temperature", $"{celsius.ToString("0.#", CultureInfo.InvariantCulture)} °C"),
            _ => (ToLabel(name), value)
        };
    }

    public static IReadOnlyList<FormattedMetric> FormatMetrics(Shared.DeviceInfo info)
        => info.Metrics
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x =>
            {
                var (label, displayValue) = FormatMetric(x.Key, x.Value);
                return new FormattedMetric(x.Key, x.Value, label, displayValue);
            })
            .ToList();

    private static string FormatBytes(double bytes) => bytes switch
    {
        < 1024 => $"{bytes.ToString(CultureInfo.InvariantCulture)} B",
        < 1024 * 1024 => $"{(bytes / 1024).ToString("0.#", CultureInfo.InvariantCulture)} KB",
        _ => $"{(bytes / 1024 / 1024).ToString("0.#", CultureInfo.InvariantCulture)} MB"
    };

    private static string ToLabel(string name)
    {
        var words = WordBoundary().Replace(name.Replace('_', ' '), " ").Trim();
        return words.Length == 0 ? name : char.ToUpperInvariant(words[0]) + words[1..].ToLowerInvariant();
    }

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
