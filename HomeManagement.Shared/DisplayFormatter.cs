using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HomeManagement.Shared.Api;

/// <summary>
/// Text for people, shared by the hub web UI, the MCP tools and the app, so all of them show the same words.
/// </summary>
public static partial class DisplayFormatter
{
    private const int MaxResultLength = 1000;

    public static string FormatAgo(DateTimeOffset value, DateTimeOffset now) => (now - value) switch
    {
        { TotalSeconds: < 10 } => "just now",
        { TotalMinutes: < 1 } elapsed => $"{(int)elapsed.TotalSeconds} s ago",
        { TotalHours: < 1 } elapsed => $"{(int)elapsed.TotalMinutes} min ago",
        { TotalDays: < 1 } elapsed => $"{(int)elapsed.TotalHours} h ago",
        var elapsed => $"{(int)elapsed.TotalDays} d ago"
    };

    public static string FormatAgo(DateTimeOffset? value, DateTimeOffset now, string never = "never")
        => value is { } date ? FormatAgo(date, now) : never;

    public static string FormatDuration(TimeSpan value)
    {
        if (value.TotalMinutes < 1)
        {
            return $"{(int)value.TotalSeconds} s";
        }

        List<string> parts = [with(capacity: 2)];
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
        double? number = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        return (name, number) switch
        {
            ("UptimeSeconds", { } seconds) => ("Uptime", FormatDuration(TimeSpan.FromSeconds(seconds))),
            ("Rssi", { } rssi) => ("Wi-Fi signal", $"{rssi.ToString(CultureInfo.InvariantCulture)} dBm"),
            ("FreeHeap", { } bytes) => ("Free memory", FormatBytes(bytes)),
            ("Temperature", { } celsius) => ("Temperature", $"{celsius.ToString("0.#", CultureInfo.InvariantCulture)} °C"),
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
