namespace HomeManagement.Infrastructure.Database.Entities;

public class DeviceConfiguration
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Value { get; set; }
    public int DeviceId { get; set; }
}