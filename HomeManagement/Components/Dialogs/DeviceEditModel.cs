namespace HomeManagement.Components.Dialogs;

public class DeviceEditModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Address { get; set; } = string.Empty;
    public List<DeviceActionEditModel> Actions { get; set; } = new();
    public List<DeviceConfigurationEditModel> Configurations { get; set; } = new();
}