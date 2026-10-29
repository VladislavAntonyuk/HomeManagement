using System.Collections.Concurrent;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Infrastructure.Database;
using Innovative.SolarCalculator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Application.Workflows;

public class WorkflowTriggerService(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    IWorkflowRunner workflowRunner,
    IDeviceActionExecutor deviceActionExecutor,
    IOptionsMonitor<WorkflowAutomationOptions> optionsMonitor,
    ILogger<WorkflowTriggerService> logger) : BackgroundService
{
    private static readonly TimeSpan MinAtTimeGracePeriod = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<int, Task> _runningWorkflows = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = optionsMonitor.CurrentValue;
            if (options.Enabled)
            {
                try
                {
                    await ProcessWorkflowsAsync(options, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Workflow trigger processing failed.");
                }
            }

            await Task.Delay(GetCheckInterval(options), stoppingToken);
        }
    }

    private async Task ProcessWorkflowsAsync(WorkflowAutomationOptions options, CancellationToken token)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        var nowUtc = DateTimeOffset.UtcNow;
        var tick = new TriggerTick(
            TimeZoneInfo.ConvertTime(nowUtc, timeZoneInfo),
            timeZoneInfo,
            options,
            TimeSpan.FromTicks(Math.Max(MinAtTimeGracePeriod.Ticks, GetCheckInterval(options).Ticks * 3)));

        var workflows = await dbContext.Workflows
            .AsTracking()
            .Include(x => x.TriggerConditions)
            .ThenInclude(x => x.TriggerSourceAction)
            .ThenInclude(x => x!.Device)
            .Where(x => x.IsEnabled)
            .ToListAsync(token);

        var runs = new List<(Workflow Workflow, WorkflowExecutionContext Context)>();
        foreach (var workflow in workflows)
        {
            if (_runningWorkflows.ContainsKey(workflow.Id))
            {
                continue;
            }

            var (isMatched, triggerContext) = await EvaluateTriggerAsync(workflow, tick, token);
            var wasMatched = workflow.LastConditionMatched == true;
            workflow.LastConditionMatched = isMatched;

            if (!isMatched || (wasMatched && !workflow.RepeatWhileMatched))
            {
                continue;
            }

            workflow.LastTriggeredAtUtc = nowUtc;
            runs.Add((workflow, triggerContext));
        }

        await dbContext.SaveChangesAsync(token);

        foreach (var (workflow, context) in runs)
        {
            StartRun(workflow, context, token);
        }
    }

    private void StartRun(Workflow workflow, WorkflowExecutionContext context, CancellationToken token)
    {
        var run = RunAsync(workflow, context, token);
        _runningWorkflows[workflow.Id] = run;
        _ = run.ContinueWith(
            _ => _runningWorkflows.TryRemove(new KeyValuePair<int, Task>(workflow.Id, run)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunAsync(Workflow workflow, WorkflowExecutionContext context, CancellationToken token)
    {
        try
        {
            var result = await workflowRunner.RunAsync(workflow, context, token);
            if (!result.IsSuccessful)
            {
                logger.LogWarning(
                    "Workflow {WorkflowName} execution completed with errors: {Errors}",
                    workflow.Name,
                    string.Join(" | ", result.Errors));
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            logger.LogError(ex, "Workflow {WorkflowName} execution failed.", workflow.Name);
        }
    }

    private async Task<(bool IsMatched, WorkflowExecutionContext Context)> EvaluateTriggerAsync(
        Workflow workflow,
        TriggerTick tick,
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
            var evaluation = await EvaluateConditionAsync(condition, tick, token);
            results.Add(evaluation.IsMatched);

            contextValues[$"Trigger[{i}].Type"] = condition.TriggerType.ToString();
            contextValues[$"Trigger[{i}].Device"] = condition.TriggerSourceAction?.Device.Name;
            contextValues[$"Trigger[{i}].Property"] = condition.TriggerPropertyPath;
            contextValues[$"Trigger[{i}].Expected"] = condition.TriggerExpectedValue;
            contextValues[$"Trigger[{i}].Current"] = evaluation.CurrentValue;

            if (i == 0)
            {
                contextValues["Trigger.Device"] = condition.TriggerSourceAction?.Device.Name;
                contextValues["Trigger.Property"] = condition.TriggerPropertyPath;
                contextValues["Trigger.Expected"] = condition.TriggerExpectedValue;
                contextValues["Trigger.Current"] = evaluation.CurrentValue;
            }
        }

        var isMatched = workflow.ConditionOperator is WorkflowConditionOperator.Or
            ? results.Any(x => x)
            : results.All(x => x);

        return (isMatched, new WorkflowExecutionContext(contextValues));
    }

    private async Task<ConditionEvaluationResult> EvaluateConditionAsync(
        WorkflowTriggerCondition condition,
        TriggerTick tick,
        CancellationToken token)
    {
        return condition.TriggerType switch
        {
            WorkflowTriggerType.Date => EvaluateDateCondition(condition, tick.LocalNow),
            WorkflowTriggerType.Time => EvaluateTimeCondition(condition, tick),
            WorkflowTriggerType.DeviceResult => await EvaluateDeviceResultConditionAsync(condition, tick, token),
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

    private static ConditionEvaluationResult EvaluateTimeCondition(WorkflowTriggerCondition condition, TriggerTick tick)
    {
        var localNow = tick.LocalNow;
        var now = TimeOnly.FromDateTime(localNow.DateTime);
        var currentValue = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var from = ResolveTime(condition.TriggerTimeReference, condition.TriggerTime, condition.TriggerTimeOffsetMinutes, tick);
        if (from is null || condition.TriggerOperator is not { } comparisonOperator)
        {
            return new ConditionEvaluationResult(false, currentValue);
        }

        if (comparisonOperator is WorkflowComparisonOperator.Equal)
        {
            var elapsed = now - from.Value;
            var occurrenceDate = (localNow.DateTime - elapsed).Date;
            if (elapsed >= tick.AtTimeGracePeriod || condition.LastTriggerDateLocal == occurrenceDate)
            {
                return new ConditionEvaluationResult(false, currentValue);
            }

            condition.LastTriggerDateLocal = occurrenceDate;
            return new ConditionEvaluationResult(true, currentValue);
        }

        if (comparisonOperator is WorkflowComparisonOperator.Between or WorkflowComparisonOperator.NotBetween)
        {
            var to = ResolveTime(condition.TriggerTimeToReference, condition.TriggerTimeTo, condition.TriggerTimeToOffsetMinutes, tick);
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
        TriggerTick tick)
    {
        var time = reference switch
        {
            WorkflowTimeReference.Fixed => fixedTime,
            WorkflowTimeReference.Sunrise or WorkflowTimeReference.Sunset
                => TimeOnly.FromDateTime(GetSunEventLocal(reference.Value, tick)),
            _ => null
        };

        return time?.AddMinutes(offsetMinutes ?? 0);
    }

    private static DateTime GetSunEventLocal(WorkflowTimeReference reference, TriggerTick tick)
    {
        var solarTimes = new SolarTimes(tick.LocalNow.Date, tick.Options.Latitude, tick.Options.Longitude);
        var sunEvent = reference is WorkflowTimeReference.Sunrise ? solarTimes.Sunrise : solarTimes.Sunset;

        return TimeZoneInfo.ConvertTimeFromUtc(sunEvent.ToUniversalTime(), tick.TimeZone);
    }

    private async Task<ConditionEvaluationResult> EvaluateDeviceResultConditionAsync(
        WorkflowTriggerCondition condition,
        TriggerTick tick,
        CancellationToken token)
    {
        if (condition.TriggerSourceAction is not { } sourceAction
            || string.IsNullOrWhiteSpace(condition.TriggerPropertyPath)
            || string.IsNullOrWhiteSpace(condition.TriggerExpectedValue)
            || condition.TriggerOperator is null)
        {
            condition.LastConditionMatched = false;
            return new ConditionEvaluationResult(false, null);
        }

        var execution = await ExecuteSourceActionAsync(sourceAction, tick, token);
        if (!execution.IsSuccess || string.IsNullOrWhiteSpace(execution.RawResponse))
        {
            condition.LastConditionMatched = false;
            return new ConditionEvaluationResult(false, null);
        }

        if (!JsonValueReader.TryReadProperty(execution.RawResponse, condition.TriggerPropertyPath, out var currentValue, out var currentValueType))
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

    private async Task<DeviceActionExecutionResult> ExecuteSourceActionAsync(
        DeviceAction action,
        TriggerTick tick,
        CancellationToken token)
    {
        if (tick.SourceActionResults.TryGetValue(action.Id, out var cached))
        {
            return cached;
        }

        var result = await deviceActionExecutor.ExecuteAsync(action.Device, action, token);
        tick.SourceActionResults[action.Id] = result;
        return result;
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

    private static TimeSpan GetCheckInterval(WorkflowAutomationOptions options)
        => TimeSpan.FromSeconds(Math.Max(5, options.CheckIntervalSeconds));

    private record ConditionEvaluationResult(bool IsMatched, string? CurrentValue);

    private sealed record TriggerTick(
        DateTimeOffset LocalNow,
        TimeZoneInfo TimeZone,
        WorkflowAutomationOptions Options,
        TimeSpan AtTimeGracePeriod)
    {
        public Dictionary<int, DeviceActionExecutionResult> SourceActionResults { get; } = new();
    }
}
