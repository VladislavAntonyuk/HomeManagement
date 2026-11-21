using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Database.Entities;
using HomeManagement.Shared.Api;
using Innovative.SolarCalculator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeManagement.Application.Workflows;

public class WorkflowTriggerService(
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    WorkflowRunner workflowRunner,
    DeviceActionExecutor deviceActionExecutor,
    DeviceStateStore deviceStateStore,
    IOptionsMonitor<WorkflowAutomationOptions> optionsMonitor,
    TimeProvider timeProvider,
    ILogger<WorkflowTriggerService> logger) : BackgroundService
{
    private static readonly TimeSpan MinAtTimeGracePeriod = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<int, Task> _runningWorkflows = new();

    // When the last event each trigger handled arrived, so every event runs the workflow once.
    // A trigger starts from the check before its first one, so the event that causes that first check counts,
    // e.g. the first detection after the hub started, while a workflow created later does not run for an older event.
    // Devices do not retain events, so the hub never receives an old one again after it restarts.
    private readonly ConcurrentDictionary<int, DateTimeOffset> _handledEventTimes = new();

    // Holds at most one pending change, so a burst of changes, e.g. the retained statuses after a reconnect, causes at most two checks.
    private readonly Channel<bool> _deviceChanges = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private TimeZoneInfo? _timeZone;

    // Start of the previous check: events that arrived after it are new for a trigger checked for the first time.
    private DateTimeOffset _previousCheckUtc;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _previousCheckUtc = timeProvider.GetUtcNow();
        deviceStateStore.StatusChanged += OnDeviceChanged;
        deviceStateStore.TelemetryChanged += OnDeviceChanged;
        try
        {
            await RunLoopAsync(stoppingToken);
        }
        finally
        {
            deviceStateStore.StatusChanged -= OnDeviceChanged;
            deviceStateStore.TelemetryChanged -= OnDeviceChanged;
        }
    }

    private void OnDeviceChanged() => _deviceChanges.Writer.TryWrite(true);

    /// <summary>
    /// Checks all workflows every <see cref="WorkflowAutomationOptions.CheckIntervalSeconds"/>, and the workflows with device conditions
    /// as soon as a device status or telemetry value changes, so short outages are not missed and events are handled right away.
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
                    await ProcessWorkflowsAsync(options, deviceChangesOnly: !isFullCheck, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Workflow trigger processing failed.");
                }
            }

            isFullCheck = !await WaitForDeviceChangeAsync(nextFullCheck - timeProvider.GetUtcNow(), stoppingToken);
        }
    }

    /// <returns>True when a device status or telemetry value changed before <paramref name="timeout"/> elapsed.</returns>
    private async Task<bool> WaitForDeviceChangeAsync(TimeSpan timeout, CancellationToken token)
    {
        if (timeout > TimeSpan.Zero)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await _deviceChanges.Reader.WaitToReadAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return false;
            }
        }

        return _deviceChanges.Reader.TryRead(out _);
    }

    private TimeZoneInfo GetTimeZone(string id)
    {
        if (_timeZone?.Id != id)
        {
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
        }

        return _timeZone;
    }

    private async Task ProcessWorkflowsAsync(WorkflowAutomationOptions options, bool deviceChangesOnly, CancellationToken token)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var timeZone = GetTimeZone(options.TimeZoneId);
        var nowUtc = timeProvider.GetUtcNow();
        var tick = new TriggerTick(
            TimeZoneInfo.ConvertTime(nowUtc, timeZone),
            timeZone,
            options,
            TimeSpan.FromTicks(Math.Max(MinAtTimeGracePeriod.Ticks, GetCheckInterval(options).Ticks * 3)),
            _previousCheckUtc);
        _previousCheckUtc = nowUtc;

        var query = dbContext.Workflows
            .AsTracking()
            .WithTriggers()
            .Where(x => x.IsEnabled && x.Triggers.Any());
        if (deviceChangesOnly)
        {
            query = query.Where(x => x.Triggers.Any(t => t is DeviceStatusTrigger || t is DeviceResultTrigger));
        }

        var workflows = await query.ToListAsync(token);

        List<(Workflow Workflow, IReadOnlyDictionary<string, string?> Placeholders)> runs = [];
        foreach (var workflow in workflows)
        {
            if (_runningWorkflows.ContainsKey(workflow.Id))
            {
                continue;
            }

            var (evaluation, hasEvent, placeholders) = await EvaluateTriggersAsync(workflow, tick, token);
            if (evaluation is not { } isMatched)
            {
                // A device status is not known yet, e.g. right after the hub started. Keep the previous state, so the workflow does not run again.
                continue;
            }

            var wasMatched = workflow.WasMatched == true;
            workflow.WasMatched = isMatched;

            // An event, e.g. a device going offline, runs the workflow even when the triggers also matched on the previous check.
            // Repeating while matched happens only on the regular check, not on every device change.
            var repeats = workflow.RepeatWhileMatched && !deviceChangesOnly;
            if (!isMatched || (wasMatched && !repeats && !hasEvent))
            {
                continue;
            }

            workflow.LastTriggeredAtUtc = nowUtc;
            runs.Add((workflow, placeholders));
        }

        await dbContext.SaveChangesAsync(token);

        foreach (var (workflow, placeholders) in runs)
        {
            StartRun(workflow, placeholders, token);
        }
    }

    private void StartRun(Workflow workflow, IReadOnlyDictionary<string, string?> placeholders, CancellationToken token)
    {
        var run = RunAsync(workflow, placeholders, token);
        _runningWorkflows[workflow.Id] = run;
        _ = run.ContinueWith(
            _ => _runningWorkflows.TryRemove(new KeyValuePair<int, Task>(workflow.Id, run)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunAsync(Workflow workflow, IReadOnlyDictionary<string, string?> placeholders, CancellationToken token)
    {
        try
        {
            var result = await workflowRunner.RunAsync(workflow, placeholders, token);
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
    /// Whether the triggers match, or null when the result depends on a trigger that cannot be evaluated yet,
    /// whether a matching trigger is an event that happened since the previous check, and the {Trigger.*} placeholders.
    /// </returns>
    private async Task<(bool? IsMatched, bool HasEvent, IReadOnlyDictionary<string, string?> Placeholders)> EvaluateTriggersAsync(
        Workflow workflow,
        TriggerTick tick,
        CancellationToken token)
    {
        // Case does not matter, so {Trigger.Result.Score} finds the "score" property.
        Dictionary<string, string?> placeholders = new(StringComparer.OrdinalIgnoreCase);
        var triggers = workflow.Triggers;
        if (triggers.Count == 0)
        {
            return (false, false, placeholders);
        }

        List<TriggerEvaluation> results = [with(capacity: triggers.Count)];
        foreach (var (index, trigger) in triggers.Index())
        {
            var evaluation = await EvaluateAsync(trigger, tick, token);
            results.Add(evaluation);
            AddPlaceholders(placeholders, $"Trigger[{index}]", trigger, evaluation);
        }

        // {Trigger.*} describes the trigger that ran the workflow: the event that just happened, e.g. a detection while the mode is away,
        // otherwise the first matching trigger, e.g. which status a device went to when either status runs the workflow.
        var triggerIndex = results.FindIndex(x => x is { IsMatched: true, IsEvent: true });
        if (triggerIndex < 0)
        {
            triggerIndex = Math.Max(0, results.FindIndex(x => x.IsMatched == true));
        }

        AddPlaceholders(placeholders, "Trigger", triggers[triggerIndex], results[triggerIndex]);

        // Three-valued logic: an unknown trigger decides the result only when the known triggers do not.
        var matches = results.Select(x => x.IsMatched).ToList();
        bool? isMatched = workflow.ConditionOperator is WorkflowConditionOperator.Or
            ? matches.Contains(true) ? true : matches.Contains(null) ? null : false
            : matches.Contains(false) ? false : matches.Contains(null) ? null : true;

        return (isMatched, results.Any(x => x is { IsMatched: true, IsEvent: true }), placeholders);
    }

    private static void AddPlaceholders(Dictionary<string, string?> placeholders, string prefix, WorkflowTrigger trigger, TriggerEvaluation evaluation)
    {
        placeholders[$"{prefix}.Type"] = trigger switch
        {
            DateTrigger => "Date",
            TimeTrigger => "Time",
            DeviceResultTrigger => "DeviceResult",
            DeviceStatusTrigger => "DeviceStatus"
        };
        placeholders[$"{prefix}.Device"] = trigger switch
        {
            DeviceStatusTrigger status => status.Device.Name,
            DeviceResultTrigger deviceResult => deviceResult.DeviceAction.Device.Name,
            DateTrigger or TimeTrigger => null
        };
        placeholders[$"{prefix}.Property"] = (trigger as DeviceResultTrigger)?.PropertyPath;
        placeholders[$"{prefix}.Expected"] = trigger switch
        {
            DeviceStatusTrigger status => status.ExpectedStatus.ToString(),
            DeviceResultTrigger deviceResult => deviceResult.ExpectedValue,
            DateTrigger or TimeTrigger => null
        };
        placeholders[$"{prefix}.Current"] = evaluation.CurrentValue;
        if (evaluation.Result is { } result)
        {
            // {Trigger.Result} is the whole value, {Trigger.Result.snapshotUrl} one of its properties.
            placeholders[$"{prefix}.Result"] = result;
            foreach (var property in JsonValueReader.GetProperties(result).Where(x => x.Path.StartsWith("$.", StringComparison.Ordinal)))
            {
                placeholders[$"{prefix}.Result.{property.Path[2..]}"] = property.SampleValue;
            }
        }
    }

    private async Task<TriggerEvaluation> EvaluateAsync(WorkflowTrigger trigger, TriggerTick tick, CancellationToken token) => trigger switch
    {
        DateTrigger date => EvaluateDate(date, tick.LocalNow),
        TimeTrigger time => EvaluateTime(time, tick),
        DeviceResultTrigger deviceResult => await EvaluateDeviceResultAsync(deviceResult, tick, token),
        DeviceStatusTrigger deviceStatus => EvaluateDeviceStatus(deviceStatus)
    };

    private static string FormatDate(DateTimeOffset localNow) => DateOnly.FromDateTime(localNow.DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatTime(DateTimeOffset localNow) => TimeOnly.FromDateTime(localNow.DateTime).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static TriggerEvaluation EvaluateDate(DateTrigger trigger, DateTimeOffset localNow)
    {
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var isMatched = trigger.Operator switch
        {
            WorkflowComparisonOperator.Between => trigger.DateTo is { } to && today >= trigger.Date && today <= to,
            WorkflowComparisonOperator.NotBetween => trigger.DateTo is { } to && (today < trigger.Date || today > to),
            var op => Compare(op, today, trigger.Date)
        };

        return new TriggerEvaluation(isMatched, FormatDate(localNow));
    }

    private static TriggerEvaluation EvaluateTime(TimeTrigger trigger, TriggerTick tick)
    {
        var localNow = tick.LocalNow;
        var now = TimeOnly.FromDateTime(localNow.DateTime);
        var currentValue = FormatTime(localNow);
        var from = Resolve(trigger.From, tick);

        switch (trigger.Operator)
        {
            case WorkflowComparisonOperator.Equal:
            {
                // "At": matches once per day, within the grace period after the time.
                var elapsed = now - from;
                var occurrenceDate = (localNow.DateTime - elapsed).Date;
                if (elapsed >= tick.AtTimeGracePeriod || trigger.LastMatchedDate == occurrenceDate)
                {
                    return new TriggerEvaluation(false, currentValue);
                }

                trigger.LastMatchedDate = occurrenceDate;
                return new TriggerEvaluation(true, currentValue);
            }

            case WorkflowComparisonOperator.Between or WorkflowComparisonOperator.NotBetween:
            {
                if (trigger.To is not { } toPoint)
                {
                    return new TriggerEvaluation(false, currentValue);
                }

                // The range may wrap around midnight, e.g. between sunset and sunrise.
                var to = Resolve(toPoint, tick);
                var isInRange = from <= to ? now >= from && now < to : now >= from || now < to;
                return new TriggerEvaluation(trigger.Operator is WorkflowComparisonOperator.Between ? isInRange : !isInRange, currentValue);
            }

            case var op and not WorkflowComparisonOperator.NotEqual:
                return new TriggerEvaluation(Compare(op, now, from), currentValue);

            default:
                return new TriggerEvaluation(false, currentValue);
        }
    }

    private static TimeOnly Resolve(TimePoint point, TriggerTick tick)
    {
        var time = point.Reference switch
        {
            WorkflowTimeReference.Sunrise or WorkflowTimeReference.Sunset => TimeOnly.FromDateTime(GetSunEventLocal(point.Reference, tick)),
            _ => point.FixedTime.GetValueOrDefault()
        };

        return time.AddMinutes(point.OffsetMinutes);
    }

    private static DateTime GetSunEventLocal(WorkflowTimeReference reference, TriggerTick tick)
    {
        var solarTimes = new SolarTimes(tick.LocalNow.Date, tick.Options.Latitude, tick.Options.Longitude);
        var sunEvent = reference is WorkflowTimeReference.Sunrise ? solarTimes.Sunrise : solarTimes.Sunset;

        return TimeZoneInfo.ConvertTimeFromUtc(sunEvent.ToUniversalTime(), tick.TimeZone);
    }

    private TriggerEvaluation EvaluateDeviceStatus(DeviceStatusTrigger trigger)
    {
        DeviceStatus? status = deviceStateStore.GetPresence(trigger.Device.DeviceId).Status switch
        {
            DevicePresenceStatus.Online => DeviceStatus.Online,
            DevicePresenceStatus.Offline or DevicePresenceStatus.NotResponding => DeviceStatus.Offline,
            _ => null
        };

        if (status is null)
        {
            return new TriggerEvaluation(null, null);
        }

        // The trigger is an event: it matches once, when the device changes to the expected status.
        // HadStatus is null before the first check, e.g. for a new workflow, which only records the status, so saving a workflow does not run it.
        var hasStatus = status == trigger.ExpectedStatus;
        var hadStatus = trigger.HadStatus;
        trigger.HadStatus = hasStatus;
        return new TriggerEvaluation(hasStatus && hadStatus == false, status.ToString(), IsEvent: true);
    }

    private async Task<TriggerEvaluation> EvaluateDeviceResultAsync(DeviceResultTrigger trigger, TriggerTick tick, CancellationToken token)
    {
        var action = trigger.DeviceAction;
        var isEvent = action.CommandType == Shared.CommandType.Event;
        if (isEvent)
        {
            // Before reading the value: the first event the device sends is also the first value there is.
            _handledEventTimes.TryAdd(trigger.Id, tick.EventsSinceUtc);
        }

        var execution = await ExecuteSourceActionAsync(action, tick, token);
        if (execution is not ActionSucceeded { Output: { } response }
            || string.IsNullOrWhiteSpace(response)
            || !JsonValueReader.TryReadProperty(response, trigger.PropertyPath, out var currentValue, out _))
        {
            trigger.WasMatched = false;
            return new TriggerEvaluation(false, null);
        }

        var isMatched = IsMatched(trigger.Operator, currentValue, trigger.ExpectedValue, trigger.ValueType);
        if (isEvent)
        {
            // An event matches only at the check right after it arrived, never for as long as it stays the last value.
            var receivedAtUtc = deviceStateStore.GetTelemetry(action.Device.DeviceId, action.Command)?.ReceivedAtUtc;
            isMatched = IsNewEvent(trigger.Id, receivedAtUtc) && isMatched;
        }

        trigger.WasMatched = isMatched;
        return new TriggerEvaluation(isMatched, currentValue, isEvent, response);
    }

    private bool IsNewEvent(int triggerId, DateTimeOffset? receivedAtUtc)
    {
        var handledAtUtc = _handledEventTimes[triggerId];
        if (receivedAtUtc is not { } received || received <= handledAtUtc)
        {
            return false;
        }

        _handledEventTimes[triggerId] = received;
        return true;
    }

    private async Task<DeviceActionResult> ExecuteSourceActionAsync(DeviceAction action, TriggerTick tick, CancellationToken token)
    {
        if (!tick.SourceActionResults.TryGetValue(action.Id, out var result))
        {
            result = await deviceActionExecutor.ExecuteAsync(action.Device, action, token);
            tick.SourceActionResults[action.Id] = result;
        }

        return result;
    }

    private static bool IsMatched(WorkflowComparisonOperator op, string? currentValue, string expectedValue, WorkflowTriggerValueType valueType) => valueType switch
    {
        WorkflowTriggerValueType.Number => TryParseNumber(currentValue, out var current) && TryParseNumber(expectedValue, out var expected) && op switch
        {
            // Numbers from JSON are compared with a small tolerance, e.g. 21.1 equals 21.1000001.
            WorkflowComparisonOperator.Equal => Math.Abs(current - expected) < 0.0001,
            WorkflowComparisonOperator.NotEqual => Math.Abs(current - expected) >= 0.0001,
            _ => Compare(op, current, expected)
        },
        WorkflowTriggerValueType.Boolean => bool.TryParse(currentValue, out var current) && bool.TryParse(expectedValue, out var expected)
            && op is WorkflowComparisonOperator.Equal or WorkflowComparisonOperator.NotEqual
            && Compare(op, current, expected),
        WorkflowTriggerValueType.Null => op switch
        {
            WorkflowComparisonOperator.Equal => string.IsNullOrWhiteSpace(currentValue),
            WorkflowComparisonOperator.NotEqual => !string.IsNullOrWhiteSpace(currentValue),
            _ => false
        },
        _ => op switch
        {
            WorkflowComparisonOperator.Contains => currentValue?.Contains(expectedValue, StringComparison.Ordinal) ?? false,
            WorkflowComparisonOperator.NotContains => !(currentValue?.Contains(expectedValue, StringComparison.Ordinal) ?? false),
            WorkflowComparisonOperator.Equal or WorkflowComparisonOperator.NotEqual => Compare(op, currentValue ?? string.Empty, expectedValue, StringComparer.Ordinal),
            _ => false
        }
    };

    private static bool TryParseNumber(string? value, out double number) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    /// <summary>
    /// The relational operators, shared by dates, times, numbers, booleans and text. Range and text operators do not match here.
    /// </summary>
    private static bool Compare<T>(WorkflowComparisonOperator op, T current, T expected, IComparer<T>? comparer = null)
    {
        var order = (comparer ?? Comparer<T>.Default).Compare(current, expected);
        return op switch
        {
            WorkflowComparisonOperator.Equal => order == 0,
            WorkflowComparisonOperator.NotEqual => order != 0,
            WorkflowComparisonOperator.GreaterThan => order > 0,
            WorkflowComparisonOperator.GreaterThanOrEqual => order >= 0,
            WorkflowComparisonOperator.LessThan => order < 0,
            WorkflowComparisonOperator.LessThanOrEqual => order <= 0,
            _ => false
        };
    }

    private static TimeSpan GetCheckInterval(WorkflowAutomationOptions options)
        => TimeSpan.FromSeconds(Math.Max(5, options.CheckIntervalSeconds));

    /// <param name="IsMatched">Whether the trigger matches, or null when it cannot be evaluated yet.</param>
    /// <param name="IsEvent">The trigger matches only at the check right after something happened, not for as long as a state lasts.</param>
    /// <param name="Result">The device value the condition read, e.g. the JSON of a detection.</param>
    private sealed record TriggerEvaluation(bool? IsMatched, string? CurrentValue, bool IsEvent = false, string? Result = null);

    private sealed record TriggerTick(
        DateTimeOffset LocalNow,
        TimeZoneInfo TimeZone,
        WorkflowAutomationOptions Options,
        TimeSpan AtTimeGracePeriod,
        DateTimeOffset EventsSinceUtc)
    {
        public Dictionary<int, DeviceActionResult> SourceActionResults { get; } = new();
    }
}
