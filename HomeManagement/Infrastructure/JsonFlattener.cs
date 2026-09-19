using System.Text;
using System.Text.Json;

namespace HomeManagement.Infrastructure;

public static class JsonFlattener
{
    public static string FlattenToString(string json)
    {
        var dictionary = FlattenToDictionary(json);
        var sb = new StringBuilder();
        foreach (var kvp in dictionary)
        {
            sb.AppendLine($"{kvp.Key}: {FormatValue(kvp)}");
        }

        return sb.ToString();

        string FormatValue(KeyValuePair<string, string> kv)
        {
            if (kv.Key.Contains("uptime", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(kv.Value, out var seconds))
                {
                    return FormatDuration(seconds);
                }
            }

            return kv.Value;

            static string FormatDuration(int? seconds)
            {
                if (seconds is null)
                {
                    return string.Empty;
                }

                var ts = TimeSpan.FromSeconds(seconds.Value);
                var parts = new List<string>();
                if (ts.Days > 0)
                {
                    parts.Add($"{ts.Days}d");
                }

                if (ts.Hours > 0 || parts.Count > 0)
                {
                    parts.Add($"{ts.Hours}h");
                }

                if (ts.Minutes > 0 || parts.Count > 0)
                {
                    parts.Add($"{ts.Minutes}m");
                }

                parts.Add($"{ts.Seconds}s");
                return string.Join(" ", parts);
            }

        }
    }

    public static Dictionary<string, string> FlattenToDictionary(string json)
    {
        var results = new List<(string LeafName, string FullPath, string Value)>();
        using var doc = JsonDocument.Parse(json);
        Traverse(doc.RootElement, string.Empty, string.Empty, results);

        var duplicates = results
            .GroupBy(r => r.LeafName)
            .ToDictionary(g => g.Key, g => g.Count() > 1);

        var finalDict = new Dictionary<string, string>();
        foreach (var item in results)
        {
            string key = duplicates[item.LeafName] ? item.FullPath : item.LeafName;

            if (!finalDict.TryAdd(key, item.Value))
            {
                int counter = 1;
                while (!finalDict.TryAdd($"{key}_{counter}", item.Value))
                {
                    counter++;
                }
            }
        }

        return finalDict;
    }

    private static void Traverse(JsonElement element, string path, string leafName, List<(string LeafName, string FullPath, string Value)> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    string newPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
                    Traverse(prop.Value, newPath, prop.Name, results);
                }
                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    string newPath = string.IsNullOrEmpty(path) ? $"[{index}]" : $"{path}[{index}]";
                    Traverse(item, newPath, leafName, results);
                    index++;
                }
                break;
            case JsonValueKind.Null:
                results.Add((leafName, path, "null"));
                break;
            default:
                results.Add((leafName, path, element.ToString()));
                break;
        }
    }
}