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
    public const string ManualTrigger = "Run manually";

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
        (OperatorSubject.Time, WorkflowComparisonOperator.Equal) => "at",
        (OperatorSubject.Date, WorkflowComparisonOperator.Equal) => "on",
        (OperatorSubject.Date, WorkflowComparisonOperator.NotEqual) => "not on",
        (OperatorSubject.Time, WorkflowComparisonOperator.GreaterThanOrEqual) => "at or after",
        (OperatorSubject.Time, WorkflowComparisonOperator.LessThanOrEqual) => "at or before",
        (OperatorSubject.Date, WorkflowComparisonOperator.GreaterThanOrEqual) => "on or after",
        (OperatorSubject.Date, WorkflowComparisonOperator.LessThanOrEqual) => "on or before",
        (OperatorSubject.Date or OperatorSubject.Time, WorkflowComparisonOperator.GreaterThan) => "after",
        (OperatorSubject.Date or OperatorSubject.Time, WorkflowComparisonOperator.LessThan) => "before",
        (_, WorkflowComparisonOperator.Equal) => "=",
        (_, WorkflowComparisonOperator.NotEqual) => "!=",
        (_, WorkflowComparisonOperator.GreaterThan) => ">",
        (_, WorkflowComparisonOperator.GreaterThanOrEqual) => ">=",
        (_, WorkflowComparisonOperator.LessThan) => "<",
        (_, WorkflowComparisonOperator.LessThanOrEqual) => "<=",
        (_, WorkflowComparisonOperator.Between) => "between",
        (_, WorkflowComparisonOperator.NotBetween) => "not between",
        (_, WorkflowComparisonOperator.Contains) => "contains",
        (_, WorkflowComparisonOperator.NotContains) => "not contains",
        _ => op.ToString()
    };

    public static bool IsRange(WorkflowComparisonOperator? op)
        => op is WorkflowComparisonOperator.Between or WorkflowComparisonOperator.NotBetween;

    public static string FormatTrigger(Workflow workflow)
        => workflow.Triggers.Count == 0
            ? ManualTrigger
            : string.Join(workflow.ConditionOperator is WorkflowConditionOperator.Or ? " OR " : " AND ", workflow.Triggers.Select(Format));

    public static string Format(WorkflowTrigger trigger) => trigger switch
    {
        DateTrigger date => FormatRange("Date", OperatorSubject.Date, date.Operator, FormatDate(date.Date), date.DateTo is { } to ? FormatDate(to) : "?"),
        TimeTrigger time => FormatRange("Time", OperatorSubject.Time, time.Operator, FormatTime(time.From), time.To is { } to ? FormatTime(to) : "?"),
        DeviceResultTrigger result
            => $"{result.DeviceAction.Device.Name}/{result.DeviceAction.Action}:{result.PropertyPath} {FormatOperator(OperatorSubject.Value, result.Operator)} {result.ExpectedValue}",
        DeviceStatusTrigger status => $"{status.Device.Name} goes {status.ExpectedStatus.ToString().ToLowerInvariant()}"
    };

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatTime(TimePoint point)
    {
        var offset = point.OffsetMinutes switch
        {
            0 => string.Empty,
            > 0 => $" +{point.OffsetMinutes}m",
            _ => $" {point.OffsetMinutes}m"
        };

        return point.Reference switch
        {
            WorkflowTimeReference.Sunrise => $"Sunrise{offset}",
            WorkflowTimeReference.Sunset => $"Sunset{offset}",
            _ => point.FixedTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "?"
        };
    }

    private static string FormatRange(string subject, OperatorSubject operatorSubject, WorkflowComparisonOperator op, string from, string to)
        => IsRange(op)
            ? $"{subject} {FormatOperator(operatorSubject, op)} {from} and {to}"
            : $"{subject} {FormatOperator(operatorSubject, op)} {from}";
}
