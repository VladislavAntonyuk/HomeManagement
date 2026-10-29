using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace HomeManagement.Shared;

/// <summary>
/// MQTT topics used between devices and the hub.
/// </summary>
public static class DeviceTopics
{
    public const string Root = "home/devices";

    public const string Online = "online";

    public const string Offline = "offline";

    public const string TwinSegment = "twin";

    public const string StatusSegment = "status";

    public const string TelemetrySegment = "telemetry";

    public const string CommandsSegment = "commands";

    public const string AllTwins = $"{Root}/+/{TwinSegment}";

    public const string AllStatuses = $"{Root}/+/{StatusSegment}";

    public const string AllTelemetry = $"{Root}/+/{TelemetrySegment}/#";

    /// <summary>
    /// Retained <see cref="DeviceTwin"/> JSON.
    /// </summary>
    public static string Twin(string deviceId) => $"{Root}/{deviceId}/{TwinSegment}";

    /// <summary>
    /// Retained <see cref="Online"/> or <see cref="Offline"/>. The device sets <see cref="Offline"/> as its last will.
    /// </summary>
    public static string Status(string deviceId) => $"{Root}/{deviceId}/{StatusSegment}";

    /// <summary>
    /// Retained latest value of a <see cref="CommandType.Get"/> action.
    /// </summary>
    public static string Telemetry(string deviceId, string key) => $"{Root}/{deviceId}/{TelemetrySegment}/{key}";

    /// <summary>
    /// Command request. MQTT v5 response topic and correlation data are used to return a <see cref="CommandResponse"/>.
    /// </summary>
    public static string Command(string deviceId, string key) => $"{Root}/{deviceId}/{CommandsSegment}/{key}";

    public static string AllCommands(string deviceId) => $"{Root}/{deviceId}/{CommandsSegment}/#";

    /// <summary>
    /// Topic the hub receives command responses on.
    /// </summary>
    public static string HubResponses(string hubId) => $"home/hub/{hubId}/responses";

    /// <summary>
    /// Splits a device topic into the device id, the kind (twin, status, telemetry, commands) and the optional key.
    /// </summary>
    public static bool TryParse(string topic, [NotNullWhen(true)] out string? deviceId, [NotNullWhen(true)] out string? kind, out string? key)
    {
        deviceId = null;
        kind = null;
        key = null;

        var prefix = $"{Root}/";
        if (!topic.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = topic[prefix.Length..].Split('/', 3);
        if (parts.Length < 2 || parts[0].Length == 0)
        {
            return false;
        }

        deviceId = parts[0];
        kind = parts[1];
        key = parts.Length == 3 ? parts[2] : null;
        return true;
    }

    /// <summary>
    /// Converts a device name to a topic-safe id, e.g. "Live Camera" to "live-camera".
    /// </summary>
    public static string ToDeviceId(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().TrimEnd('-');
    }
}
