using HomeManagement.Shared;

namespace HomeManagement.Infrastructure.Database.Entities;

public class WorkflowTriggerCondition
{
    public int Id { get; init; }

    public WorkflowTriggerType TriggerType { get; set; } = WorkflowTriggerType.Manual;

    public DateOnly? TriggerDate { get; set; }

    public DateOnly? TriggerDateTo { get; set; }

    public WorkflowTimeReference? TriggerTimeReference { get; set; }

    public TimeOnly? TriggerTime { get; set; }

    public int? TriggerTimeOffsetMinutes { get; set; }

    public WorkflowTimeReference? TriggerTimeToReference { get; set; }

    public TimeOnly? TriggerTimeTo { get; set; }

    public int? TriggerTimeToOffsetMinutes { get; set; }

    public int? TriggerSourceActionId { get; set; }

    public DeviceAction? TriggerSourceAction { get; set; }

    public int? TriggerDeviceId { get; set; }

    /// <summary>
    /// Device whose status a <see cref="WorkflowTriggerType.DeviceStatus"/> condition watches.
    /// </summary>
    public Device? TriggerDevice { get; set; }

    public DeviceStatus? TriggerDeviceStatus { get; set; }

    public string? TriggerPropertyPath { get; set; }

    public string? TriggerExpectedValue { get; set; }

    public WorkflowTriggerValueType? TriggerValueType { get; set; }

    public WorkflowComparisonOperator? TriggerOperator { get; set; }

    public DateTime? LastTriggerDateLocal { get; set; }

    public bool? LastConditionMatched { get; set; }
    public int WorkflowId { get; set; }
}
