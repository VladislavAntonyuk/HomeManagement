using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeManagement.Shared.Resources;

namespace HomeManagement.Shared.Api;

/// <summary>
/// Text for people, shared by the hub web UI, the MCP tools and the app, so all of them show the same words.
/// Words and numbers follow <see cref="CultureInfo.CurrentUICulture"/> and <see cref="CultureInfo.CurrentCulture"/>.
/// </summary>
public static partial class DisplayFormatter
{
    private const int MaxResultLength = 1000;

    public static string FormatAgo(DateTimeOffset value, DateTimeOffset now) => (now - value) switch
    {
        { TotalSeconds: < 10 } => SharedStrings.JustNow,
        { TotalMinutes: < 1 } elapsed => Format(SharedStrings.SecondsAgo, (int)elapsed.TotalSeconds),
        { TotalHours: < 1 } elapsed => Format(SharedStrings.MinutesAgo, (int)elapsed.TotalMinutes),
        { TotalDays: < 1 } elapsed => Format(SharedStrings.HoursAgo, (int)elapsed.TotalHours),
        var elapsed => Format(SharedStrings.DaysAgo, (int)elapsed.TotalDays)
    };

    /// <summary>
    /// Like <see cref="FormatAgo(DateTimeOffset, DateTimeOffset)"/>, or <paramref name="never"/>, "never" by default, when there is no date.
    /// </summary>
    public static string FormatAgo(DateTimeOffset? value, DateTimeOffset now, string? never = null)
        => value is { } date ? FormatAgo(date, now) : never ?? SharedStrings.Never;

    public static string FormatDuration(TimeSpan value)
    {
        if (value.TotalMinutes < 1)
        {
            return Format(SharedStrings.DurationSeconds, (int)value.TotalSeconds);
        }

        List<string> parts = [with(capacity: 2)];
        if (value.Days > 0)
        {
            parts.Add(Format(SharedStrings.DurationDays, value.Days));
        }

        if (value.Hours > 0)
        {
            parts.Add(Format(SharedStrings.DurationHours, value.Hours));
        }

        if (value.Days == 0 && value.Minutes > 0)
        {
            parts.Add(Format(SharedStrings.DurationMinutes, value.Minutes));
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Turns a metric into a label and a value, e.g. "UptimeSeconds" "93784" into "Uptime" "1 d 2 h".
    /// Unknown metrics keep their value and only get a readable label.
    /// </summary>
    public static (string Label, string Value) FormatMetric(string name, string value)
    {
        double? number = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        return (name, number) switch
        {
            ("UptimeSeconds", { } seconds) => (SharedStrings.MetricUptime, FormatDuration(TimeSpan.FromSeconds(seconds))),
            ("Rssi", { } rssi) => (SharedStrings.MetricWifiSignal, $"{rssi.ToString(CultureInfo.CurrentCulture)} dBm"),
            ("FreeHeap", { } bytes) => (SharedStrings.MetricFreeMemory, FormatBytes((long)bytes)),
            ("Temperature", { } celsius) => (SharedStrings.MetricTemperature, FormatTemperature(celsius)),
            _ => (ToLabel(name), value)
        };
    }

    public static IReadOnlyList<DeviceMetricDto> FormatMetrics(DeviceInfo info)
        => [.. info.Metrics
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x =>
            {
                var (label, displayValue) = FormatMetric(x.Key, x.Value);
                return new DeviceMetricDto(x.Key, x.Value, label, displayValue);
            })];

    /// <summary>
    /// Shows the result of a device action: JSON becomes one "key: value" line per leaf, other text stays as it is.
    /// </summary>
    public static string FormatActionResult(string message)
    {
        var text = TryFlatten(message) ?? message;
        return text.Length > MaxResultLength ? $"{text[..MaxResultLength]}..." : text;
    }

    /// <summary>
    /// Shows the value of a dashboard widget: a single value gets its unit, e.g. "21.5 °C", anything longer is shown as <see cref="FormatActionResult"/>.
    /// </summary>
    public static string FormatWidgetValue(string value, string? unit)
    {
        var text = FormatActionResult(value);
        return string.IsNullOrWhiteSpace(unit) || text.Contains('\n') ? text : $"{text} {unit.Trim()}";
    }

    /// <summary>
    /// The label of a value a widget shows: the one the user gave it, or one made from the last part of its path, e.g. "$.battery.levelPercent" as "Level percent".
    /// </summary>
    public static string FormatPropertyLabel(string path, string? label)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        var name = path[(path.LastIndexOf('.') + 1)..];
        return name.StartsWith('$') ? SharedStrings.Value : ToLabel(name);
    }

    /// <summary>
    /// Devices name actions as they like, e.g. "start clip garden" or "RESTART"; shows them all as a sentence.
    /// </summary>
    public static string FormatActionName(string action)
    {
        if (string.IsNullOrEmpty(action))
        {
            return action;
        }

        if (!action.Any(char.IsLower))
        {
            action = action.ToLowerInvariant();
        }

        return char.ToUpperInvariant(action[0]) + action[1..];
    }

    private static string? TryFlatten(string message)
    {
        if (message.AsSpan().TrimStart() is not [ '{' or '[', ..])
        {
            return null;
        }

        List<(string Leaf, string Path, string Value)> leaves = [];
        try
        {
            using var document = JsonDocument.Parse(message);
            AddLeaves(document.RootElement, string.Empty, string.Empty, leaves);
        }
        catch (JsonException)
        {
            return null;
        }

        // A leaf name is enough unless several leaves share it, e.g. "power" of two solar inputs.
        var repeated = leaves.CountBy(x => x.Leaf).Where(x => x.Value > 1).Select(x => x.Key).ToHashSet();
        var builder = new StringBuilder();
        foreach (var (leaf, path, value) in leaves)
        {
            var key = repeated.Contains(leaf) ? path : leaf;
            var shown = key.Contains("uptime", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var seconds)
                ? FormatDuration(TimeSpan.FromSeconds(seconds))
                : value;
            builder.Append(key).Append(": ").AppendLine(shown);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AddLeaves(JsonElement element, string path, string leaf, List<(string Leaf, string Path, string Value)> leaves)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddLeaves(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}", property.Name, leaves);
                }

                break;
            case JsonValueKind.Array:
                foreach (var (index, item) in element.EnumerateArray().Index())
                {
                    AddLeaves(item, $"{path}[{index}]", leaf, leaves);
                }

                break;
            default:
                leaves.Add((leaf, path, element.ValueKind is JsonValueKind.Null ? "null" : element.ToString()));
                break;
        }
    }

    /// <summary>
    /// Shows a size in the biggest unit it has at least one of, e.g. "1.5 GB".
    /// </summary>
    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => Format(SharedStrings.SizeBytes, bytes),
        < 1024L * 1024 => Format(SharedStrings.SizeKilobytes, (bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture)),
        < 1024L * 1024 * 1024 => Format(SharedStrings.SizeMegabytes, (bytes / 1024.0 / 1024).ToString("0.#", CultureInfo.CurrentCulture)),
        < 1024L * 1024 * 1024 * 1024 => Format(SharedStrings.SizeGigabytes, (bytes / 1024.0 / 1024 / 1024).ToString("0.#", CultureInfo.CurrentCulture)),
        _ => Format(SharedStrings.SizeTerabytes, (bytes / 1024.0 / 1024 / 1024 / 1024).ToString("0.##", CultureInfo.CurrentCulture))
    };

    public static string FormatTemperature(double celsius) => $"{celsius.ToString("0.#", CultureInfo.CurrentCulture)} °C";

    private static string Format(string format, object value) => string.Format(CultureInfo.CurrentCulture, format, value);

    private static string ToLabel(string name)
    {
        var words = WordBoundary().Replace(name.Replace('_', ' '), " ").Trim();
        return words.Length == 0 ? name : char.ToUpperInvariant(words[0]) + words[1..].ToLowerInvariant();
    }

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
