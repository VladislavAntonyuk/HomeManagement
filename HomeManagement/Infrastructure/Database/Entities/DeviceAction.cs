namespace HomeManagement.Infrastructure.Database.Entities;

public class DeviceAction
{
    public int Id { get; set; }
    public required string Action { get; set; }
    public Shared.CommandType CommandType { get; set; }
    public required string Command { get; set; }
    public string? CommandArgs { get; set; }
    public int DeviceId { get; set; }
}