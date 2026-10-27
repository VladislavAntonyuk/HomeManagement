using HomeManagement.Shared;

namespace HomeManagement.Components.Dialogs;

public class DeviceActionEditModel
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public CommandType CommandType { get; set; } = CommandType.Get;
    public string? CommandArgs { get; set; } = null;
}

public class DeviceConfigurationEditModel
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}