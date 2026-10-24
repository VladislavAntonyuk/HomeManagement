using HomeManagement.Application.DeviceManagement;
using HomeManagement.Infrastructure.Database;
using Innovative.SolarCalculator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Application.Workflows;

public class WorkflowTriggerService(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    IWorkflowRunner workflowRunner,
    IDeviceActionExecutor deviceActionExecutor,
    IOptionsMonitor<WorkflowAutomationOptions> optionsMonitor,
    ILogger<WorkflowTriggerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = optionsMonitor.CurrentValue;
            if (!options.Enabled)
            {
                await DelayAsync(options, stoppingToken);
                continue;
            }

            try
            {
                await ProcessWorkflowsAsync(options, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "Workflow trigger processing failed due to invalid configuration.");
            }
            catch (DbUpdateException ex)
            {
                logger.LogError(ex, "Workflow trigger processing failed when saving runtime state.");
            }

            await DelayAsync(options, stoppingToken);
        }
    }

    private async Task ProcessWorkflowsAsync(WorkflowAutomationOptions options, CancellationToken token)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var nowUtc = DateTimeOffset.UtcNow;
        var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZoneInfo);

        var workflows = await dbContext.Workflows
            .AsTracking()
            .Include(x => x.TriggerConditions)
            .Include(x => x.Steps)
            .Where(x => x.IsEnabled)
            .ToListAsync(token);

        foreach (var workflow in workflows)
        {
            var (shouldRun, triggerContext) = await ShouldRunByTriggerAsync(workflow, localNow, options, dbContext, token);
            if (!shouldRun)
            {
                continue;
            }

            var result = await workflowRunner.RunAsync(workflow, triggerContext, token);
            workflow.LastTriggeredAtUtc = nowUtc;

            if (!result.IsSuccessful)
            {
                logger.LogWarning(
                    "Workflow {WorkflowName} execution completed with errors: {Errors}",
                    workflow.Name,
                    string.Join(" | ", result.Errors));
            }
        }

        await dbContext.SaveChangesAsync(token);
    }

    private async Task<(bool ShouldRun, WorkflowExecutionContext Context)> ShouldRunByTriggerAsync(
        Workflow workflow,
        DateTimeOffset localNow,
        WorkflowAutomationOptions options,
        HomeManagementDbContext dbContext,
        CancellationToken token)
    {
        var conditions = workflow.TriggerConditions;
        if (conditions.Count == 0)
        {
            return (false, WorkflowExecutionContext.Empty);
        }

        var contextValues = new Dictionary<string, string?>(StringComparer.Ordinal);
        var results = new List<bool>(conditions.Count);

        for (var i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            var evaluation = await EvaluateConditionAsync(condition, localNow, options, dbContext, token);
            results.Add(evaluation.IsMatched);

            contextValues[$"Trigger[{i}].Type"] = condition.TriggerType.ToString();
            contextValues[$"Trigger[{i}].Device"] = condition.TriggerDeviceName;
            contextValues[$"Trigger[{i}].Property"] = condition.TriggerPropertyPath;
            contextValues[$"Trigger[{i}].Expected"] = condition.TriggerExpectedValue;
            contextValues[$"Trigger[{i}].Current"] = evaluation.CurrentValue;

            if (i == 0)
            {
                contextValues["Trigger.Device"] = condition.TriggerDeviceName;
                contextValues["Trigger.Property"] = condition.TriggerPropertyPath;
                contextValues["Trigger.Expected"] = condition.TriggerExpectedValue;
                contextValues["Trigger.Current"] = evaluation.CurrentValue;
            }

        }

        var shouldRun = workflow.ConditionOperator is WorkflowConditionOperator.Or
            ? results.Any(x => x)
            : results.All(x => x);

        workflow.LastConditionMatched = shouldRun;
        return (shouldRun, new WorkflowExecutionContext(contextValues));
    }

    private async Task<ConditionEvaluationResult> EvaluateConditionAsync(
        WorkflowTriggerCondition condition,
        DateTimeOffset localNow,
        WorkflowAutomationOptions options,
        HomeManagementDbContext dbContext,
        CancellationToken token)
    {
        return condition.TriggerType switch
        {
            WorkflowTriggerType.Date => EvaluateDateCondition(condition, localNow),
            WorkflowTriggerType.Time => EvaluateTimeCondition(condition, localNow, options),
            WorkflowTriggerType.DeviceResult => await EvaluateDeviceResultConditionAsync(condition, dbContext, deviceActionExecutor, token),
            _ => new ConditionEvaluationResult(false, null)
        };
    }

    private static ConditionEvaluationResult EvaluateDateCondition(WorkflowTriggerCondition condition, DateTimeOffset localNow)
    {
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var currentValue = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (condition.TriggerDate is not { } date || condition.TriggerOperator is not { } comparisonOperator)
        {
            return new ConditionEvaluationResult(false, currentValue);
        }

        var isMatched = comparisonOperator switch
        {
            WorkflowComparisonOperator.Equal => today == date,
            WorkflowComparisonOperator.NotEqual => today != date,
            WorkflowComparisonOperator.GreaterThan => today > date,
            WorkflowComparisonOperator.GreaterThanOrEqual => today >= date,
            WorkflowComparisonOperator.LessThan => today < date,
            WorkflowComparisonOperator.LessThanOrEqual => today <= date,
            WorkflowComparisonOperator.Between => condition.TriggerDateTo is { } dateTo && today >= date && today <= dateTo,
            WorkflowComparisonOperator.NotBetween => condition.TriggerDateTo is { } dateTo && (today < date || today > dateTo),
            _ => false
        };

        return new ConditionEvaluationResult(isMatched, currentValue);
    }

    private static ConditionEvaluationResult EvaluateTimeCondition(
        WorkflowTriggerCondition condition,
        DateTimeOffset localNow,
        WorkflowAutomationOptions options)
    {
        var now = TimeOnly.FromDateTime(localNow.DateTime);
        var currentValue = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var from = ResolveTime(condition.TriggerTimeReference, condition.TriggerTime, condition.TriggerTimeOffsetMinutes, localNow, options);
        if (from is null || condition.TriggerOperator is not { } comparisonOperator)
        {
            return new ConditionEvaluationResult(false, currentValue);
        }

        if (comparisonOperator is WorkflowComparisonOperator.Equal)
        {
            // "At" fires once per day as soon as the time is reached.
            if (now < from.Value || condition.LastTriggerDateLocal == localNow.Date)
            {
                return new ConditionEvaluationResult(false, currentValue);
            }

            condition.LastTriggerDateLocal = localNow.Date;
            return new ConditionEvaluationResult(true, currentValue);
        }

        if (comparisonOperator is WorkflowComparisonOperator.Between or WorkflowComparisonOperator.NotBetween)
        {
            var to = ResolveTime(condition.TriggerTimeToReference, condition.TriggerTimeTo, condition.TriggerTimeToOffsetMinutes, localNow, options);
            if (to is null)
            {
                return new ConditionEvaluationResult(false, currentValue);
            }

            // The range may wrap around midnight, e.g. between sunset and sunrise.
            var isInRange = from.Value <= to.Value
                ? now >= from.Value && now < to.Value
                : now >= from.Value || now < to.Value;

            return new ConditionEvaluationResult(
                comparisonOperator is WorkflowComparisonOperator.Between ? isInRange : !isInRange,
                currentValue);
        }

        var isMatched = comparisonOperator switch
        {
            WorkflowComparisonOperator.GreaterThan => now > from.Value,
            WorkflowComparisonOperator.GreaterThanOrEqual => now >= from.Value,
            WorkflowComparisonOperator.LessThan => now < from.Value,
            WorkflowComparisonOperator.LessThanOrEqual => now <= from.Value,
            _ => false
        };

        return new ConditionEvaluationResult(isMatched, currentValue);
    }

    private static TimeOnly? ResolveTime(
        WorkflowTimeReference? reference,
        TimeOnly? fixedTime,
        int? offsetMinutes,
        DateTimeOffset localNow,
        WorkflowAutomationOptions options)
    {
        var time = reference switch
        {
            WorkflowTimeReference.Fixed => fixedTime,
            WorkflowTimeReference.Sunrise or WorkflowTimeReference.Sunset
                => TimeOnly.FromDateTime(GetSunEventLocal(reference.Value, localNow, options)),
            _ => null
        };

        return time?.AddMinutes(offsetMinutes ?? 0);
    }

    private static DateTime GetSunEventLocal(WorkflowTimeReference reference, DateTimeOffset localNow, WorkflowAutomationOptions options)
    {
        var solarTimes = new SolarTimes(localNow.Date, options.Latitude, options.Longitude);
        var sunEvent = reference is WorkflowTimeReference.Sunrise ? solarTimes.Sunrise : solarTimes.Sunset;

        var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        return TimeZoneInfo.ConvertTimeFromUtc(sunEvent.ToUniversalTime(), timeZoneInfo);
    }

    private static async Task<ConditionEvaluationResult> EvaluateDeviceResultConditionAsync(
        WorkflowTriggerCondition condition,
        HomeManagementDbContext dbContext,
        IDeviceActionExecutor deviceActionExecutor,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(condition.TriggerDeviceName)
            || string.IsNullOrWhiteSpace(condition.TriggerSourceActionName)
            || string.IsNullOrWhiteSpace(condition.TriggerPropertyPath)
            || string.IsNullOrWhiteSpace(condition.TriggerExpectedValue)
            || condition.TriggerOperator is null)
        {
            condition.LastConditionMatched = false;
            return new ConditionEvaluationResult(false, null);
        }

        var device = await dbContext.Devices
            .AsNoTracking()
            .Include(x => x.Actions)
            .Include(x => x.Configurations)
            .FirstOrDefaultAsync(x => x.Name == condition.TriggerDeviceName, token);
        if (device is null)
        {
            condition.LastConditionMatched = false;
            return new ConditionEvaluationResult(false, null);
        }

        var action = device.Actions.FirstOrDefault(x => string.Equals(x.Action, condition.TriggerSourceActionName, StringComparison.Ordinal));
        if (action is null)
        {
            condition.LastConditionMatched = false;
            return new ConditionEvaluationResult(false, null);
        }

        var execution = await deviceActionExecutor.ExecuteAsync(device, action, token);
        if (!execution.IsSuccess || string.IsNullOrWhiteSpace(execution.RawResponse))
        {
            condition.LastConditionMatched = false;
            return new ConditionEvaluationResult(false, null);
        }

        if (!TryReadProperty(execution.RawResponse, condition.TriggerPropertyPath, out var currentValue, out var currentValueType))
        {
            condition.LastConditionMatched = false;
            return new ConditionEvaluationResult(false, null);
        }

        var expectedType = condition.TriggerValueType ?? currentValueType;
        var isMatched = IsMatched(
            condition.TriggerOperator.Value,
            currentValue,
            condition.TriggerExpectedValue,
            expectedType);

        condition.LastConditionMatched = isMatched;
        return new ConditionEvaluationResult(isMatched, currentValue);
    }

    private static bool TryReadProperty(string rawResponse, string propertyPath, out string? value, out WorkflowTriggerValueType valueType)
    {
        value = null;
        valueType = WorkflowTriggerValueType.Text;

        try
        {
            using var document = JsonDocument.Parse(rawResponse);
            var element = document.RootElement;

            if (propertyPath == "$")
            {
                value = element.GetRawText();
                valueType = MapValueType(element);
                return true;
            }

            var normalized = propertyPath.StartsWith("$.", StringComparison.Ordinal) ? propertyPath[2..] : propertyPath;
            foreach (var segment in normalized.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryResolveSegment(ref element, segment))
                {
                    return false;
                }
            }

            value = ToComparableString(element);
            valueType = MapValueType(element);
            return true;
        }
        catch (JsonException)
        {
            if (propertyPath == "$")
            {
                value = rawResponse;
                valueType = WorkflowTriggerValueType.Text;
                return true;
            }

            return false;
        }
    }

    private static bool TryResolveSegment(ref JsonElement element, string segment)
    {
        var currentSegment = segment;
        while (true)
        {
            var bracketIndex = currentSegment.IndexOf('[', StringComparison.Ordinal);
            if (bracketIndex < 0)
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(currentSegment, out element))
                {
                    return false;
                }

                return true;
            }

            var propertyName = currentSegment[..bracketIndex];
            if (!string.IsNullOrWhiteSpace(propertyName))
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out element))
                {
                    return false;
                }
            }

            var closeBracket = currentSegment.IndexOf(']', bracketIndex);
            if (closeBracket < 0)
            {
                return false;
            }

            var indexText = currentSegment[(bracketIndex + 1)..closeBracket];
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                || element.ValueKind != JsonValueKind.Array
                || index < 0
                || index >= element.GetArrayLength())
            {
                return false;
            }

            element = element[index];

            if (closeBracket == currentSegment.Length - 1)
            {
                return true;
            }

            currentSegment = currentSegment[(closeBracket + 1)..];
        }
    }

    private static bool IsMatched(WorkflowComparisonOperator comparisonOperator, string? currentValue, string expectedValue, WorkflowTriggerValueType valueType)
    {
        return valueType switch
        {
            WorkflowTriggerValueType.Number => IsNumberMatched(comparisonOperator, currentValue, expectedValue),
            WorkflowTriggerValueType.Boolean => IsBooleanMatched(comparisonOperator, currentValue, expectedValue),
            WorkflowTriggerValueType.Null => IsNullMatched(comparisonOperator, currentValue),
            _ => IsTextMatched(comparisonOperator, currentValue, expectedValue)
        };
    }

    private static bool IsNumberMatched(WorkflowComparisonOperator comparisonOperator, string? currentValue, string expectedValue)
    {
        if (!double.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var currentNumber)
            || !double.TryParse(expectedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber))
        {
            return false;
        }

        return comparisonOperator switch
        {
            WorkflowComparisonOperator.GreaterThan => currentNumber > expectedNumber,
            WorkflowComparisonOperator.GreaterThanOrEqual => currentNumber >= expectedNumber,
            WorkflowComparisonOperator.LessThan => currentNumber < expectedNumber,
            WorkflowComparisonOperator.LessThanOrEqual => currentNumber <= expectedNumber,
            WorkflowComparisonOperator.Equal => Math.Abs(currentNumber - expectedNumber) < 0.0001,
            WorkflowComparisonOperator.NotEqual => Math.Abs(currentNumber - expectedNumber) >= 0.0001,
            _ => false
        };
    }

    private static bool IsBooleanMatched(WorkflowComparisonOperator comparisonOperator, string? currentValue, string expectedValue)
    {
        if (!bool.TryParse(currentValue, out var currentBoolean) || !bool.TryParse(expectedValue, out var expectedBoolean))
        {
            return false;
        }

        return comparisonOperator switch
        {
            WorkflowComparisonOperator.Equal => currentBoolean == expectedBoolean,
            WorkflowComparisonOperator.NotEqual => currentBoolean != expectedBoolean,
            _ => false
        };
    }

    private static bool IsNullMatched(WorkflowComparisonOperator comparisonOperator, string? currentValue)
    {
        return comparisonOperator switch
        {
            WorkflowComparisonOperator.Equal => string.IsNullOrWhiteSpace(currentValue),
            WorkflowComparisonOperator.NotEqual => !string.IsNullOrWhiteSpace(currentValue),
            _ => false
        };
    }

    private static bool IsTextMatched(WorkflowComparisonOperator comparisonOperator, string? currentValue, string expectedValue)
    {
        return comparisonOperator switch
        {
            WorkflowComparisonOperator.Equal => string.Equals(currentValue, expectedValue, StringComparison.Ordinal),
            WorkflowComparisonOperator.NotEqual => !string.Equals(currentValue, expectedValue, StringComparison.Ordinal),
            WorkflowComparisonOperator.Contains => currentValue?.Contains(expectedValue, StringComparison.Ordinal) ?? false,
            WorkflowComparisonOperator.NotContains => !(currentValue?.Contains(expectedValue, StringComparison.Ordinal) ?? false),
            _ => false
        };
    }

    private static WorkflowTriggerValueType MapValueType(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => WorkflowTriggerValueType.Number,
            JsonValueKind.True or JsonValueKind.False => WorkflowTriggerValueType.Boolean,
            JsonValueKind.String => WorkflowTriggerValueType.Text,
            JsonValueKind.Null or JsonValueKind.Undefined => WorkflowTriggerValueType.Null,
            _ => WorkflowTriggerValueType.Json
        };
    }

    private static string? ToComparableString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static Task DelayAsync(WorkflowAutomationOptions options, CancellationToken token)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.CheckIntervalSeconds));
        return Task.Delay(interval, token);
    }

    private record ConditionEvaluationResult(bool IsMatched, string? CurrentValue);
}