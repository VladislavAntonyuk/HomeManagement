using System.Text.Json;

namespace HomeManagement.Shared;

/// <summary>
/// JSON of the device protocol: twins, device info and command responses use camelCase names and are read case-insensitively.
/// </summary>
public static class DeviceProtocolJson
{
    /// <summary>
    /// Configures the serializer of an MQTT endpoint.
    /// </summary>
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    }
}
