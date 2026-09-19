using HomeManagement.Shared;

namespace HomeManagement.Components.Dialogs;

public class SelectableDevice
{
    public required DeviceConfig Device { get; init; }
    public bool Selected { get; set; }
}