using System.Text.Json;

namespace HomeManagement.Shared;

public static class DeviceActionResultFormatter
{
    private const int MaxLength = 1000;

    public static string Format(string message)
    {
        var text = IsJsonObjectOrArray(message) ? JsonFlattener.FlattenToString(message).TrimEnd() : message;
        return text.Length > MaxLength ? $"{text[..MaxLength]}..." : text;
    }

    private static bool IsJsonObjectOrArray(string message)
    {
        var trimmed = message.AsSpan().TrimStart();
        if (trimmed.IsEmpty || trimmed[0] is not ('{' or '['))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(message);
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
