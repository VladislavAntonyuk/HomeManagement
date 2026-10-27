using System.Globalization;
using System.Text.Json;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Application.Workflows;

public static class JsonValueReader
{
    public const string RootPath = "$";

    public static IReadOnlyList<WorkflowTriggerPropertyOption> GetProperties(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(response);
            var result = new List<WorkflowTriggerPropertyOption>();
            AddProperties(document.RootElement, RootPath, result);
            return result;
        }
        catch (JsonException)
        {
            return [new WorkflowTriggerPropertyOption(RootPath, WorkflowTriggerValueType.Text, response)];
        }
    }

    public static bool TryReadProperty(string rawResponse, string propertyPath, out string? value, out WorkflowTriggerValueType valueType)
    {
        value = null;
        valueType = WorkflowTriggerValueType.Text;

        try
        {
            using var document = JsonDocument.Parse(rawResponse);
            var element = document.RootElement;

            if (propertyPath == RootPath)
            {
                value = element.GetRawText();
                valueType = MapValueType(element);
                return true;
            }

            var normalized = propertyPath.StartsWith("$.", StringComparison.Ordinal) ? propertyPath[2..] : propertyPath;
            foreach (var segment in normalized.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryResolveSegment(ref element, segment))
                {
                    return false;
                }
            }

            value = ToComparableString(element);
            valueType = MapValueType(element);
            return true;
        }
        catch (JsonException)
        {
            if (propertyPath == RootPath)
            {
                value = rawResponse;
                valueType = WorkflowTriggerValueType.Text;
                return true;
            }

            return false;
        }
    }

    private static void AddProperties(JsonElement element, string path, ICollection<WorkflowTriggerPropertyOption> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddProperties(property.Value, $"{path}.{property.Name}", result);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    AddProperties(item, $"{path}[{index}]", result);
                    index++;
                }
                break;

            default:
                result.Add(new WorkflowTriggerPropertyOption(path, MapValueType(element), ToComparableString(element)));
                break;
        }
    }

    private static bool TryResolveSegment(ref JsonElement element, string segment)
    {
        var currentSegment = segment;
        while (true)
        {
            var bracketIndex = currentSegment.IndexOf('[', StringComparison.Ordinal);
            if (bracketIndex < 0)
            {
                return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(currentSegment, out element);
            }

            var propertyName = currentSegment[..bracketIndex];
            if (!string.IsNullOrWhiteSpace(propertyName))
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out element))
                {
                    return false;
                }
            }

            var closeBracket = currentSegment.IndexOf(']', bracketIndex);
            if (closeBracket < 0)
            {
                return false;
            }

            var indexText = currentSegment[(bracketIndex + 1)..closeBracket];
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                || element.ValueKind != JsonValueKind.Array
                || index < 0
                || index >= element.GetArrayLength())
            {
                return false;
            }

            element = element[index];

            if (closeBracket == currentSegment.Length - 1)
            {
                return true;
            }

            currentSegment = currentSegment[(closeBracket + 1)..];
        }
    }

    private static WorkflowTriggerValueType MapValueType(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => WorkflowTriggerValueType.Number,
            JsonValueKind.True or JsonValueKind.False => WorkflowTriggerValueType.Boolean,
            JsonValueKind.String => WorkflowTriggerValueType.Text,
            JsonValueKind.Null or JsonValueKind.Undefined => WorkflowTriggerValueType.Null,
            _ => WorkflowTriggerValueType.Json
        };
    }

    private static string? ToComparableString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
