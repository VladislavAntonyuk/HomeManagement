using MudBlazor;

namespace HomeManagement.Components.Shared;

/// <summary>
/// One icon per kind of trigger and step, so the workflow list and the editor look the same.
/// </summary>
public static class WorkflowIcons
{
    public const string Workflow = Icons.Material.Rounded.AccountTree;
    public const string Manual = Icons.Material.Rounded.TouchApp;
    public const string DateTrigger = Icons.Material.Rounded.Event;
    public const string TimeTrigger = Icons.Material.Rounded.Schedule;
    public const string DeviceValueTrigger = Icons.Material.Rounded.Sensors;
    public const string DeviceStatusTrigger = Icons.Material.Rounded.Power;
    public const string DeviceActionStep = Icons.Material.Rounded.PlayCircle;
    public const string DelayStep = Icons.Material.Rounded.HourglassEmpty;
    public const string NotifyStep = Icons.Material.Rounded.Notifications;
}
