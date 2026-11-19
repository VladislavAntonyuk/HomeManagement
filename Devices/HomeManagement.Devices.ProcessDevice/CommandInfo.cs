using HomeManagement.Shared;

namespace HomeManagement.Devices.ProcessDevice;

public class CommandInfo
{
    public required string Name { get; init; }

    public required string Endpoint { get; init; }

    public CommandType Method { get; init; } = CommandType.Post;

    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];
}
