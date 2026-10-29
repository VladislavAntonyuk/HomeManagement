using System.Text.Json.Serialization;

namespace HomeManagement.Shared;

[JsonConverter(typeof(JsonStringEnumConverter<CommandType>))]
public enum CommandType
{
    Get,
    Post
}
