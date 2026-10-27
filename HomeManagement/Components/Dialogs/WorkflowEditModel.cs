using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Components.Dialogs;

public class WorkflowEditModel
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public WorkflowConditionOperator ConditionOperator { get; set; } = WorkflowConditionOperator.And;
    public bool RepeatWhileMatched { get; set; }
    public List<WorkflowTriggerConditionEditModel> TriggerConditions { get; set; } = [];
    public List<WorkflowStepEditModel> Steps { get; set; } = [];
}

public class WorkflowTriggerConditionEditModel
{
    public WorkflowTriggerType TriggerType { get; set; } = WorkflowTriggerType.Manual;
    public DateOnly? TriggerDate { get; set; }
    public DateOnly? TriggerDateTo { get; set; }
    public WorkflowTimeReference? TriggerTimeReference { get; set; }
    public TimeOnly? TriggerTime { get; set; }
    public int? TriggerTimeOffsetMinutes { get; set; }
    public WorkflowTimeReference? TriggerTimeToReference { get; set; }
    public TimeOnly? TriggerTimeTo { get; set; }
    public int? TriggerTimeToOffsetMinutes { get; set; }
    public string? TriggerDeviceName { get; set; }
    public string? TriggerSourceActionName { get; set; }
    public string? TriggerPropertyPath { get; set; }
    public string? TriggerExpectedValue { get; set; }
    public WorkflowTriggerValueType? TriggerValueType { get; set; }
    public WorkflowComparisonOperator? TriggerOperator { get; set; }
}

public class WorkflowStepEditModel
{
    public WorkflowStepType StepType { get; set; } = WorkflowStepType.DeviceAction;
    public string? DeviceName { get; set; }
    public string? ActionName { get; set; }
    public int? DelaySeconds { get; set; }
    public string? NotifyTitle { get; set; }
    public string? NotifyMessageTemplate { get; set; }
}