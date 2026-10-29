namespace HomeManagement.Infrastructure.Database.Entities;

public class Device
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Id the device uses in its MQTT topics.
    /// </summary>
    public required string DeviceId { get; set; }

    public string? Description { get; set; }

    public IList<DeviceAction> Actions { get; set; } = [];
}
