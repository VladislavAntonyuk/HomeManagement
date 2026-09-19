namespace HomeManagement.Shared;

public class DeviceInfo
{
    public required string Name { get; init; }

    public required string Address { get; set; }

    public Dictionary<string, string> Metrics { get; set; } = new();
}