namespace HomeManagement.Infrastructure.Database.Entities;

public class Device
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Address { get; set; }

    public string? Description { get; set; }

    public IList<DeviceAction> Actions { get; set; } = [];
    public IList<DeviceConfiguration> Configurations { get; set; } = [];
}