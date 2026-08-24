namespace HomeManagement.Shared;

public class Device
{
    public required string Name { get; init; }

    public required string Address { get; set; }

    public string? Description { get; set; }

    public IList<DeviceAction> Actions { get; init; } = [];
}