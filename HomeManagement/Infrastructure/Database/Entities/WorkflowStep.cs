namespace HomeManagement.Infrastructure.Database.Entities;

public class WorkflowStep
{
    public int Id { get; init; }

    public WorkflowStepType StepType { get; set; } = WorkflowStepType.DeviceAction;

    public string? DeviceName { get; set; }

    public string? ActionName { get; set; }

    public int? DelaySeconds { get; set; }

    public string? NotifyTitle { get; set; }

    public string? NotifyMessageTemplate { get; set; }
    public int WorkflowId { get; set; }
}