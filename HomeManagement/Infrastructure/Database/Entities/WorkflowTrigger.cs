namespace HomeManagement.Infrastructure.Database.Entities;

/// <summary>
/// A condition that runs a workflow. All kinds share one table, and the TriggerType column says which kind a row is.
/// The hierarchy is closed, so a switch over the triggers needs no default branch.
/// </summary>
public closed class WorkflowTrigger
{
    public int Id { get; init; }

    /// <summary>
    /// Position of the trigger in the workflow: {Trigger[Order].*}. {Trigger.*} is the matching event, otherwise the first matching trigger.
    /// </summary>
    public int Order { get; set; }

    public int WorkflowId { get; set; }
}

/// <summary>
/// Matches today's date against a date, or a range of dates.
/// </summary>
public sealed class DateTrigger : WorkflowTrigger
{
    public WorkflowComparisonOperator Operator { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>
    /// End of the range for <see cref="WorkflowComparisonOperator.Between"/> and <see cref="WorkflowComparisonOperator.NotBetween"/>.
    /// </summary>
    public DateOnly? DateTo { get; set; }
}

/// <summary>
/// Matches the time of day. <see cref="WorkflowComparisonOperator.Equal"/> means "at", which matches once per day.
/// </summary>
public sealed class TimeTrigger : WorkflowTrigger
{
    public WorkflowComparisonOperator Operator { get; set; }

    public required TimePoint From { get; set; }

    /// <summary>
    /// End of the range for <see cref="WorkflowComparisonOperator.Between"/> and <see cref="WorkflowComparisonOperator.NotBetween"/>.
    /// </summary>
    public TimePoint? To { get; set; }

    /// <summary>
    /// The day an "at" trigger last matched, so it matches once per day.
    /// </summary>
    public DateTime? LastMatchedDate { get; set; }
}

/// <summary>
/// Reads a property of the latest value of a device action and compares it.
/// </summary>
public sealed class DeviceResultTrigger : WorkflowTrigger
{
    public int DeviceActionId { get; set; }

    public DeviceAction DeviceAction { get; set; } = null!;

    /// <summary>
    /// JSON path of the property, e.g. "$.temperature".
    /// </summary>
    public required string PropertyPath { get; set; }

    public WorkflowComparisonOperator Operator { get; set; }

    public required string ExpectedValue { get; set; }

    public WorkflowTriggerValueType ValueType { get; set; }

    /// <summary>
    /// Whether the trigger matched at the previous check.
    /// </summary>
    public bool? WasMatched { get; set; }
}

/// <summary>
/// Matches once, when a device changes to the expected status.
/// </summary>
public sealed class DeviceStatusTrigger : WorkflowTrigger
{
    public int DeviceId { get; set; }

    public Device Device { get; set; } = null!;

    public DeviceStatus ExpectedStatus { get; set; }

    /// <summary>
    /// Whether the device had the expected status at the previous check, or null before the first check.
    /// </summary>
    public bool? HadStatus { get; set; }
}

/// <summary>
/// A point in the day: a fixed time, or sunrise or sunset moved by an offset. Stored as an EF Core complex type.
/// </summary>
public sealed record class TimePoint(WorkflowTimeReference Reference, TimeOnly? FixedTime, int OffsetMinutes);
