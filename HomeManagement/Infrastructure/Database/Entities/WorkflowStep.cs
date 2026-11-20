namespace HomeManagement.Infrastructure.Database.Entities;

/// <summary>
/// A step of a workflow. All kinds share one table, and the StepType column says which kind a row is.
/// The hierarchy is closed, so a switch over the steps needs no default branch.
/// </summary>
public closed class WorkflowStep
{
    public int Id { get; init; }

    public int Order { get; set; }

    public int WorkflowId { get; set; }
}

/// <summary>
/// Runs an action of a device.
/// </summary>
public sealed class DeviceActionStep : WorkflowStep
{
    public int DeviceActionId { get; set; }

    public DeviceAction DeviceAction { get; set; } = null!;
}

/// <summary>
/// Waits before the next step.
/// </summary>
public sealed class DelayStep : WorkflowStep
{
    public TimeSpan Delay { get; set; }
}

/// <summary>
/// Sends a notification. The template can use the {Trigger.*} and {LastStep.*} placeholders.
/// </summary>
public sealed class NotifyStep : WorkflowStep
{
    public required string Title { get; set; }

    public required string MessageTemplate { get; set; }
}
