using System.Collections.Concurrent;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Infrastructure.Database;
using Innovative.SolarCalculator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Threading.Channels;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Application.Workflows;

public class WorkflowTriggerService(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    IWorkflowRunner workflowRunner,
    IDeviceActionExecutor deviceActionExecutor,
    DeviceStateStore deviceStateStore,
    IOptionsMonitor<WorkflowAutomationOptions> optionsMonitor,
    TimeProvider timeProvider,
    ILogger<WorkflowTriggerService> logger) : BackgroundService
{
    private static readonly TimeSpan MinAtTimeGracePeriod = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<int, Task> _runningWorkflows = new();

    // Holds at most one pending change, so a burst of changes, e.g. the retained statuses after a reconnect, causes at most two checks.
    private readonly Channel<bool> _statusChanges = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        deviceStateStore.StatusChanged += OnDeviceStatusChanged;
        try
        {
            await RunLoopAsync(stoppingToken);
        }
        finally
        {
            deviceStateStore.StatusChanged -= OnDeviceStatusChanged;
        }
    }

    private void OnDeviceStatusChanged() => _statusChanges.Writer.TryWrite(true);

    /// <summary>
    /// Checks all workflows every <see cref="WorkflowAutomationOptions.CheckIntervalSeconds"/>, and the workflows with
    /// <see cref="WorkflowTriggerType.DeviceStatus"/> conditions as soon as a device status changes, so short outages are not missed.
    /// </summary>
    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        var nextFullCheck = timeProvider.GetUtcNow();
        var isFullCheck = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = optionsMonitor.CurrentValue;
            if (isFullCheck)
            {
                nextFullCheck = timeProvider.GetUtcNow() + GetCheckInterval(options);
            }

            if (options.Enabled)
            {
                try
                {
                    await ProcessWorkflowsAsync(options, deviceStatusOnly: !isFullCheck, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Workflow trigger processing failed.");
                }
            }

            isFullCheck = !await WaitForDeviceStatusChangeAsync(nextFullCheck - timeProvider.GetUtcNow(), stoppingToken);
        }
    }

    /// <returns>True when a device status changed before <paramref name="timeout"/> elapsed.</returns>
    private async Task<bool> WaitForDeviceStatusChangeAsync(TimeSpan timeout, CancellationToken token)
    {
        if (timeout > TimeSpan.Zero)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await _statusChanges.Reader.WaitToReadAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return false;
            }
        }

        return _statusChanges.Reader.TryRead(out _);
    }

    private async Task ProcessWorkflowsAsync(WorkflowAutomationOptions options, bool deviceStatusOnly, CancellationToken token)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        var nowUtc = DateTimeOffset.UtcNow;
        var tick = new TriggerTick(
            TimeZoneInfo.ConvertTime(nowUtc, timeZoneInfo),
            timeZoneInfo,
            options,
            TimeSpan.FromTicks(Math.Max(MinAtTimeGracePeriod.Ticks, GetCheckInterval(options).Ticks * 3)));

        var query = dbContext.Workflows
            .AsTracking()
            .Include(x => x.TriggerConditions)
            .ThenInclude(x => x.TriggerSourceAction)
            .ThenInclude(x => x!.Device)
            .Include(x => x.TriggerConditions)
            .ThenInclude(x => x.TriggerDevice)
            .Where(x => x.IsEnabled);
        if (deviceStatusOnly)
        {
            query = query.Where(x => x.TriggerConditions.Any(c => c.TriggerType == WorkflowTriggerType.DeviceStatus));
        }

        var workflows = await query.ToListAsync(token);

        var runs = new List<(Workflow Workflow, WorkflowExecutionContext Context)>();
        foreach (var workflow in workflows)
        {
            if (_runningWorkflows.ContainsKey(workflow.Id))
            {
                continue;
            }

            var (evaluation, hasEvent, triggerContext) = await EvaluateTriggerAsync(workflow, tick, token);
            if (evaluation is not { } isMatched)
            {
                // A device status is not known yet, e.g. right after the hub started. Keep the previous state, so the workflow does not run again.
                continue;
            }

            var wasMatched = workflow.LastConditionMatched == true;
            workflow.LastConditionMatched = isMatched;

            // An event, e.g. a device going offline, runs the workflow even when the conditions also matched on the previous check.
            if (!isMatched || (wasMatched && !workflow.RepeatWhileMatched && !hasEvent))
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

    /// <returns>
    /// Whether the conditions match, or null when the result depends on a condition that cannot be evaluated yet,
    /// and whether a matching condition is an event that happened since the previous check.
    /// </returns>
    private async Task<(bool? IsMatched, bool HasEvent, WorkflowExecutionContext Context)> EvaluateTriggerAsync(
        Workflow workflow,
        TriggerTick tick,
        CancellationToken token)
    {
        var conditions = workflow.TriggerConditions;
        if (conditions.Count == 0)
        {
            return (false, false, WorkflowExecutionContext.Empty);
        }

        var contextValues = new Dictionary<string, string?>(StringComparer.Ordinal);
        var results = new List<ConditionEvaluationResult>(conditions.Count);

        for (var i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            var evaluation = await EvaluateConditionAsync(condition, tick, token);
            results.Add(evaluation);
            AddContextValues(contextValues, $"Trigger[{i}]", condition, evaluation);
        }

        // {Trigger.*} describes the first matching condition, e.g. which status a device went to when either status runs the workflow.
        var triggerIndex = Math.Max(0, results.FindIndex(x => x.IsMatched == true));
        AddContextValues(contextValues, "Trigger", conditions[triggerIndex], results[triggerIndex]);

        // Three-valued logic: an unknown condition decides the result only when the known conditions do not.
        var matches = results.Select(x => x.IsMatched).ToList();
        bool? isMatched = workflow.ConditionOperator is WorkflowConditionOperator.Or
            ? matches.Contains(true) ? true : matches.Contains(null) ? null : false
            : matches.Contains(false) ? false : matches.Contains(null) ? null : true;

        return (isMatched, results.Any(x => x is { IsMatched: true, IsEvent: true }), new WorkflowExecutionContext(contextValues));
    }

    private static void AddContextValues(
        Dictionary<string, string?> contextValues,
        string prefix,
        WorkflowTriggerCondition condition,
        ConditionEvaluationResult evaluation)
    {
        contextValues[$"{prefix}.Type"] = condition.TriggerType.ToString();
        contextValues[$"{prefix}.Device"] = condition.TriggerDevice?.Name ?? condition.TriggerSourceAction?.Device.Name;
        contextValues[$"{prefix}.Property"] = condition.TriggerPropertyPath;
        contextValues[$"{prefix}.Expected"] = condition.TriggerDeviceStatus?.ToString() ?? condition.TriggerExpectedValue;
        contextValues[$"{prefix}.Current"] = evaluation.CurrentValue;
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
            WorkflowTriggerType.DeviceStatus => EvaluateDeviceStatusCondition(condition),
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

    private ConditionEvaluationResult EvaluateDeviceStatusCondition(WorkflowTriggerCondition condition)
    {
        if (condition.TriggerDevice is not { } device || condition.TriggerDeviceStatus is not { } expectedStatus)
        {
            return new ConditionEvaluationResult(false, null);
        }

        DeviceStatus? status = deviceStateStore.IsOnline(device.DeviceId) switch
        {
            true => DeviceStatus.Online,
            false => DeviceStatus.Offline,
            null => null
        };

        if (status is null)
        {
            return new ConditionEvaluationResult(null, null);
        }

        // The condition is an event: it matches once, when the device changes to the expected status.
        // LastConditionMatched keeps whether the device had that status at the previous check. Null means it was never checked,
        // e.g. a new workflow, which only records the status, so saving a workflow does not run it.
        var hasStatus = status == expectedStatus;
        var hadStatus = condition.LastConditionMatched;
        condition.LastConditionMatched = hasStatus;
        return new ConditionEvaluationResult(hasStatus && hadStatus == false, status.ToString(), IsEvent: true);
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

    /// <param name="IsMatched">Whether the condition matches, or null when it cannot be evaluated yet.</param>
    /// <param name="IsEvent">The condition matches only at the check right after something happened, not for as long as a state lasts.</param>
    private record ConditionEvaluationResult(bool? IsMatched, string? CurrentValue, bool IsEvent = false);

    private sealed record TriggerTick(
        DateTimeOffset LocalNow,
        TimeZoneInfo TimeZone,
        WorkflowAutomationOptions Options,
        TimeSpan AtTimeGracePeriod)
    {
        public Dictionary<int, DeviceActionExecutionResult> SourceActionResults { get; } = new();
    }
}
