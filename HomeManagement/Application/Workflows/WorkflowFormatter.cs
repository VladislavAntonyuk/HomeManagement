using System.Globalization;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Application.Workflows;

/// <summary>
/// What an operator compares, which changes how it reads, e.g. "at" for a time and "=" for a value.
/// </summary>
public enum OperatorSubject
{
    Date,
    Time,
    Value
}

/// <summary>
/// Describes triggers for people, e.g. "Time between Sunset and 23:00".
/// </summary>
public static class WorkflowFormatter
{
    /// <summary>
    /// Describes a workflow without triggers, which runs only when someone runs it.
    /// </summary>
    public static string ManualTrigger => Strings.WorkflowFormat_Manual;

    public static readonly IReadOnlyList<WorkflowComparisonOperator> DateOperators =
    [
        WorkflowComparisonOperator.Equal,
        WorkflowComparisonOperator.NotEqual,
        WorkflowComparisonOperator.GreaterThan,
        WorkflowComparisonOperator.GreaterThanOrEqual,
        WorkflowComparisonOperator.LessThan,
        WorkflowComparisonOperator.LessThanOrEqual,
        WorkflowComparisonOperator.Between,
        WorkflowComparisonOperator.NotBetween
    ];

    public static readonly IReadOnlyList<WorkflowComparisonOperator> TimeOperators =
    [
        WorkflowComparisonOperator.Equal,
        WorkflowComparisonOperator.GreaterThan,
        WorkflowComparisonOperator.GreaterThanOrEqual,
        WorkflowComparisonOperator.LessThan,
        WorkflowComparisonOperator.LessThanOrEqual,
        WorkflowComparisonOperator.Between,
        WorkflowComparisonOperator.NotBetween
    ];

    /// <summary>
    /// The operators that make sense for a device value of the given type.
    /// </summary>
    public static IReadOnlyList<WorkflowComparisonOperator> GetValueOperators(WorkflowTriggerValueType valueType) => valueType switch
    {
        WorkflowTriggerValueType.Number =>
        [
            WorkflowComparisonOperator.Equal,
            WorkflowComparisonOperator.NotEqual,
            WorkflowComparisonOperator.GreaterThan,
            WorkflowComparisonOperator.GreaterThanOrEqual,
            WorkflowComparisonOperator.LessThan,
            WorkflowComparisonOperator.LessThanOrEqual
        ],
        WorkflowTriggerValueType.Text or WorkflowTriggerValueType.Json =>
        [
            WorkflowComparisonOperator.Equal,
            WorkflowComparisonOperator.NotEqual,
            WorkflowComparisonOperator.Contains,
            WorkflowComparisonOperator.NotContains
        ],
        _ => [WorkflowComparisonOperator.Equal, WorkflowComparisonOperator.NotEqual]
    };

    public static string FormatOperator(OperatorSubject subject, WorkflowComparisonOperator op) => (subject, op) switch
    {
        (OperatorSubject.Time, WorkflowComparisonOperator.Equal) => Strings.Operator_TimeAt,
        (OperatorSubject.Date, WorkflowComparisonOperator.Equal) => Strings.Operator_DateOn,
        (OperatorSubject.Date, WorkflowComparisonOperator.NotEqual) => Strings.Operator_DateNotOn,
        (OperatorSubject.Time, WorkflowComparisonOperator.GreaterThanOrEqual) => Strings.Operator_TimeAtOrAfter,
        (OperatorSubject.Time, WorkflowComparisonOperator.LessThanOrEqual) => Strings.Operator_TimeAtOrBefore,
        (OperatorSubject.Date, WorkflowComparisonOperator.GreaterThanOrEqual) => Strings.Operator_DateOnOrAfter,
        (OperatorSubject.Date, WorkflowComparisonOperator.LessThanOrEqual) => Strings.Operator_DateOnOrBefore,
        (OperatorSubject.Date or OperatorSubject.Time, WorkflowComparisonOperator.GreaterThan) => Strings.Operator_After,
        (OperatorSubject.Date or OperatorSubject.Time, WorkflowComparisonOperator.LessThan) => Strings.Operator_Before,
        (_, WorkflowComparisonOperator.Equal) => "=",
        (_, WorkflowComparisonOperator.NotEqual) => "!=",
        (_, WorkflowComparisonOperator.GreaterThan) => ">",
        (_, WorkflowComparisonOperator.GreaterThanOrEqual) => ">=",
        (_, WorkflowComparisonOperator.LessThan) => "<",
        (_, WorkflowComparisonOperator.LessThanOrEqual) => "<=",
        (_, WorkflowComparisonOperator.Between) => Strings.Operator_Between,
        (_, WorkflowComparisonOperator.NotBetween) => Strings.Operator_NotBetween,
        (_, WorkflowComparisonOperator.Contains) => Strings.Operator_Contains,
        (_, WorkflowComparisonOperator.NotContains) => Strings.Operator_NotContains,
        _ => op.ToString()
    };

    /// <summary>
    /// How triggers combine, e.g. "AND".
    /// </summary>
    public static string FormatConditionOperator(WorkflowConditionOperator op)
        => op is WorkflowConditionOperator.Or ? Strings.Common_Or : Strings.Common_And;

    public static string FormatTimeReference(WorkflowTimeReference reference) => reference switch
    {
        WorkflowTimeReference.Sunrise => Strings.TimeReference_Sunrise,
        WorkflowTimeReference.Sunset => Strings.TimeReference_Sunset,
        _ => Strings.TimeReference_Fixed
    };

    public static string FormatDeviceStatus(DeviceStatus status)
        => status is DeviceStatus.Offline ? Strings.DeviceStatus_Offline : Strings.DeviceStatus_Online;

    public static bool IsRange(WorkflowComparisonOperator? op)
        => op is WorkflowComparisonOperator.Between or WorkflowComparisonOperator.NotBetween;

    public static string FormatTrigger(Workflow workflow)
        => workflow.Triggers.Count == 0
            ? ManualTrigger
            : string.Join($" {FormatConditionOperator(workflow.ConditionOperator)} ", workflow.Triggers.Select(Format));

    public static string Format(WorkflowTrigger trigger) => trigger switch
    {
        DateTrigger date => FormatRange(Strings.WorkflowFormat_Date, OperatorSubject.Date, date.Operator, FormatDate(date.Date), date.DateTo is { } to ? FormatDate(to) : "?"),
        TimeTrigger time => FormatRange(Strings.WorkflowFormat_Time, OperatorSubject.Time, time.Operator, FormatTime(time.From), time.To is { } to ? FormatTime(to) : "?"),
        DeviceResultTrigger result
            => $"{result.DeviceAction.Device.Name}/{result.DeviceAction.Action}:{result.PropertyPath} {FormatOperator(OperatorSubject.Value, result.Operator)} {result.ExpectedValue}",
        DeviceStatusTrigger status => string.Format(
            status.ExpectedStatus is DeviceStatus.Offline ? Strings.WorkflowFormat_GoesOffline : Strings.WorkflowFormat_GoesOnline,
            status.Device.Name)
    };

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatTime(TimePoint point)
    {
        var offset = point.OffsetMinutes switch
        {
            0 => string.Empty,
            > 0 => " " + string.Format(Strings.WorkflowFormat_OffsetMinutes, $"+{point.OffsetMinutes}"),
            _ => " " + string.Format(Strings.WorkflowFormat_OffsetMinutes, point.OffsetMinutes)
        };

        return point.Reference is WorkflowTimeReference.Fixed
            ? point.FixedTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "?"
            : FormatTimeReference(point.Reference) + offset;
    }

    private static string FormatRange(string subject, OperatorSubject operatorSubject, WorkflowComparisonOperator op, string from, string to)
        => IsRange(op)
            ? string.Format(Strings.WorkflowFormat_Range, subject, FormatOperator(operatorSubject, op), from, to)
            : string.Format(Strings.WorkflowFormat_Single, subject, FormatOperator(operatorSubject, op), from);
}
