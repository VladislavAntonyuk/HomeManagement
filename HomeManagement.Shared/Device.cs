namespace HomeManagement.Shared;

public class DeviceConfig
{
    public required string Name { get; init; }

    public required string Address { get; set; }

    public IList<DeviceAction> Actions { get; init; } = [];
}