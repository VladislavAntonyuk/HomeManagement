namespace HomeManagement.Infrastructure.Database.Entities;

public class WorkflowStep
{
    public int Id { get; init; }

    public int Order { get; set; }

    public WorkflowStepType StepType { get; set; } = WorkflowStepType.DeviceAction;

    public int? DeviceActionId { get; set; }

    public DeviceAction? DeviceAction { get; set; }

    public int? DelaySeconds { get; set; }

    public string? NotifyTitle { get; set; }

    public string? NotifyMessageTemplate { get; set; }
    public int WorkflowId { get; set; }
}