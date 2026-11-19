using System.Text.Json.Serialization;

namespace HomeManagement.Shared;

[JsonConverter(typeof(JsonStringEnumConverter<CommandType>))]
public enum CommandType
{
    /// <summary>
    /// State the device publishes as retained telemetry, e.g. a temperature. Workflows match it for as long as it lasts.
    /// </summary>
    Get,
    /// <summary>
    /// A command the hub sends to the device.
    /// </summary>
    Post,
    /// <summary>
    /// Telemetry that reports something that happened, e.g. a detected person. Every new value is a separate event, so workflows run once per value.
    /// </summary>
    Event
}
