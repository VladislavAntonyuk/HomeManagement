using System.Globalization;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Application.Workflows;

public static class WorkflowConditionFormatter
{
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

    public static string FormatOperator(WorkflowTriggerType triggerType, WorkflowComparisonOperator? comparisonOperator)
    {
        return (triggerType, comparisonOperator) switch
        {
            (WorkflowTriggerType.Time, WorkflowComparisonOperator.Equal) => "at",
            (WorkflowTriggerType.Date, WorkflowComparisonOperator.Equal) => "on",
            (WorkflowTriggerType.Date, WorkflowComparisonOperator.NotEqual) => "not on",
            (WorkflowTriggerType.Time, WorkflowComparisonOperator.GreaterThanOrEqual) => "at or after",
            (WorkflowTriggerType.Time, WorkflowComparisonOperator.LessThanOrEqual) => "at or before",
            (WorkflowTriggerType.Date, WorkflowComparisonOperator.GreaterThanOrEqual) => "on or after",
            (WorkflowTriggerType.Date, WorkflowComparisonOperator.LessThanOrEqual) => "on or before",
            (WorkflowTriggerType.Date or WorkflowTriggerType.Time, WorkflowComparisonOperator.GreaterThan) => "after",
            (WorkflowTriggerType.Date or WorkflowTriggerType.Time, WorkflowComparisonOperator.LessThan) => "before",
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
            _ => "?"
        };
    }

    public static string FormatDate(DateOnly? date)
        => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "?";

    public static string FormatTime(WorkflowTimeReference? reference, TimeOnly? time, int? offsetMinutes)
    {
        var offset = offsetMinutes switch
        {
            null or 0 => string.Empty,
            > 0 => $" +{offsetMinutes}m",
            _ => $" {offsetMinutes}m"
        };

        return reference switch
        {
            WorkflowTimeReference.Fixed => time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "?",
            WorkflowTimeReference.Sunrise => $"Sunrise{offset}",
            WorkflowTimeReference.Sunset => $"Sunset{offset}",
            _ => "?"
        };
    }

    public static bool IsRange(WorkflowComparisonOperator? comparisonOperator)
        => comparisonOperator is WorkflowComparisonOperator.Between or WorkflowComparisonOperator.NotBetween;

    public static string Format(WorkflowTriggerCondition condition)
    {
        return condition.TriggerType switch
        {
            WorkflowTriggerType.Date => FormatRange(
                "Date",
                condition.TriggerType,
                condition.TriggerOperator,
                FormatDate(condition.TriggerDate),
                FormatDate(condition.TriggerDateTo)),
            WorkflowTriggerType.Time => FormatRange(
                "Time",
                condition.TriggerType,
                condition.TriggerOperator,
                FormatTime(condition.TriggerTimeReference, condition.TriggerTime, condition.TriggerTimeOffsetMinutes),
                FormatTime(condition.TriggerTimeToReference, condition.TriggerTimeTo, condition.TriggerTimeToOffsetMinutes)),
            WorkflowTriggerType.DeviceResult
                => $"{condition.TriggerDeviceName}/{condition.TriggerSourceActionName}:{condition.TriggerPropertyPath} {FormatOperator(condition.TriggerType, condition.TriggerOperator)} {condition.TriggerExpectedValue}",
            _ => condition.TriggerType.ToString()
        };
    }

    private static string FormatRange(
        string subject,
        WorkflowTriggerType triggerType,
        WorkflowComparisonOperator? comparisonOperator,
        string from,
        string to)
    {
        var operatorText = FormatOperator(triggerType, comparisonOperator);
        return IsRange(comparisonOperator)
            ? $"{subject} {operatorText} {from} and {to}"
            : $"{subject} {operatorText} {from}";
    }
}