using System.Globalization;
using HomeManagement.Application.Workflows;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Components.Dialogs;

/// <summary>
/// A device and its actions, offered in the device and action selects of the editor.
/// </summary>
public sealed record DeviceOption(int Id, string Name, IReadOnlyList<ActionOption> Actions);

public sealed record ActionOption(int Id, string Name);

public enum TriggerKind
{
    Date,
    Time,
    DeviceResult,
    DeviceStatus
}

public enum StepKind
{
    DeviceAction,
    Delay,
    Notify
}

/// <summary>
/// The form of the workflow editor. It holds every field of every kind, so switching the kind of a row keeps the form simple,
/// and turns into the entity of the chosen kind when it is saved.
/// </summary>
public sealed class WorkflowEditModel
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsEnabled { get; set; } = true;

    public WorkflowConditionOperator ConditionOperator { get; set; } = WorkflowConditionOperator.And;

    public bool RepeatWhileMatched { get; set; }

    public List<TriggerEditModel> Triggers { get; set; } = [];

    public List<StepEditModel> Steps { get; set; } = [new()];

    public static WorkflowEditModel From(Workflow workflow) => new()
    {
        Name = workflow.Name,
        Description = workflow.Description,
        IsEnabled = workflow.IsEnabled,
        ConditionOperator = workflow.ConditionOperator,
        RepeatWhileMatched = workflow.RepeatWhileMatched,
        Triggers = [.. workflow.Triggers.Select(TriggerEditModel.From)],
        Steps = [.. workflow.Steps.Select(StepEditModel.From)]
    };

    public Workflow ToEntity() => new()
    {
        Name = Name.Trim(),
        Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
        IsEnabled = IsEnabled,
        ConditionOperator = ConditionOperator,
        RepeatWhileMatched = RepeatWhileMatched,
        Triggers = [.. Triggers.Select((x, index) => x.ToEntity(index))],
        Steps = [.. Steps.Select((x, index) => x.ToEntity(index))]
    };
}

public sealed class TriggerEditModel
{
    public TriggerKind Kind { get; private set; } = TriggerKind.Time;

    public WorkflowComparisonOperator Operator { get; set; } = WorkflowComparisonOperator.GreaterThan;

    public DateOnly? Date { get; set; }

    public DateOnly? DateTo { get; set; }

    public WorkflowTimeReference FromReference { get; set; } = WorkflowTimeReference.Fixed;

    public TimeOnly? FromTime { get; set; }

    public int FromOffsetMinutes { get; set; }

    public WorkflowTimeReference ToReference { get; set; } = WorkflowTimeReference.Fixed;

    public TimeOnly? ToTime { get; set; }

    public int ToOffsetMinutes { get; set; }

    public int? DeviceId { get; set; }

    public int? ActionId { get; set; }

    public DeviceStatus ExpectedStatus { get; set; } = DeviceStatus.Offline;

    public string? PropertyPath { get; set; }

    public string? ExpectedValue { get; set; }

    public WorkflowTriggerValueType? ValueType { get; set; }

    /// <summary>
    /// <see cref="ExpectedValue"/> as a number, for the numeric field of a number property.
    /// </summary>
    public double? ExpectedNumber
    {
        get => double.TryParse(ExpectedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
        set => ExpectedValue = value?.ToString(CultureInfo.InvariantCulture);
    }

    // Editor state, not saved.

    public string? Error { get; set; }

    public WorkflowTriggerPreview? Preview { get; set; }

    public bool IsPreviewLoading { get; set; }

    public bool IsRange => WorkflowFormatter.IsRange(Operator);

    /// <summary>
    /// The JSON properties to choose from: the ones the preview found, or the saved one before a preview ran.
    /// </summary>
    public IReadOnlyList<WorkflowTriggerPropertyOption> Properties => Preview?.Properties is { Count: > 0 } properties
        ? properties
        : PropertyPath is { } path && ValueType is { } valueType ? [new(path, valueType, ExpectedValue)] : [];

    /// <summary>
    /// Changes the kind and resets the fields to the defaults of that kind.
    /// </summary>
    public void SetKind(TriggerKind kind)
    {
        Kind = kind;
        Operator = kind switch
        {
            TriggerKind.Date => WorkflowComparisonOperator.Equal,
            TriggerKind.Time => WorkflowComparisonOperator.GreaterThan,
            _ => WorkflowComparisonOperator.Equal
        };
        Date = kind is TriggerKind.Date ? DateOnly.FromDateTime(DateTime.Today) : null;
        DateTo = null;
        (FromReference, FromTime, FromOffsetMinutes) = (WorkflowTimeReference.Fixed, null, 0);
        (ToReference, ToTime, ToOffsetMinutes) = (WorkflowTimeReference.Fixed, null, 0);
        (DeviceId, ActionId, ExpectedStatus) = (null, null, DeviceStatus.Offline);
        ClearProperty();
        Error = null;
    }

    /// <summary>
    /// Forgets the chosen property, e.g. when another action is chosen.
    /// </summary>
    public void ClearProperty()
    {
        (PropertyPath, ExpectedValue, ValueType, Preview) = (null, null, null, null);
    }

    /// <summary>
    /// Chooses a property and takes its current value as the expected value.
    /// </summary>
    public void SetProperty(string path)
    {
        PropertyPath = path;
        if (Properties.FirstOrDefault(x => x.Path == path) is not { } property)
        {
            return;
        }

        ValueType = property.ValueType;
        ExpectedValue = property.SampleValue;
        if (!WorkflowFormatter.GetValueOperators(property.ValueType).Contains(Operator))
        {
            Operator = WorkflowComparisonOperator.Equal;
        }
    }

    /// <returns>What is missing, or null when the trigger is complete.</returns>
    public string? Validate() => Kind switch
    {
        TriggerKind.Date when Date is null => Strings.Validation_SelectDate,
        TriggerKind.Date when IsRange && DateTo is null => Strings.Validation_SelectEndDate,
        TriggerKind.Date when IsRange && DateTo < Date => Strings.Validation_EndBeforeStart,
        TriggerKind.Time when FromReference is WorkflowTimeReference.Fixed && FromTime is null => Strings.Validation_SelectTime,
        TriggerKind.Time when IsRange && ToReference is WorkflowTimeReference.Fixed && ToTime is null => Strings.Validation_SelectEndTime,
        TriggerKind.DeviceStatus when DeviceId is null => Strings.Validation_SelectDevice,
        TriggerKind.DeviceResult when ActionId is null => Strings.Validation_SelectDeviceAndAction,
        TriggerKind.DeviceResult when string.IsNullOrWhiteSpace(PropertyPath) || ValueType is null => Strings.Validation_PreviewAndSelect,
        TriggerKind.DeviceResult when string.IsNullOrWhiteSpace(ExpectedValue) => Strings.Validation_EnterExpectedValue,
        TriggerKind.DeviceResult when ValueType is WorkflowTriggerValueType.Number && ExpectedNumber is null => Strings.Validation_ExpectedNumber,
        _ => null
    };

    public static TriggerEditModel From(WorkflowTrigger trigger)
    {
        var model = new TriggerEditModel();
        switch (trigger)
        {
            case DateTrigger date:
                (model.Kind, model.Operator, model.Date, model.DateTo) = (TriggerKind.Date, date.Operator, date.Date, date.DateTo);
                break;
            case TimeTrigger time:
                (model.Kind, model.Operator) = (TriggerKind.Time, time.Operator);
                (model.FromReference, model.FromTime, model.FromOffsetMinutes) = (time.From.Reference, time.From.FixedTime, time.From.OffsetMinutes);
                if (time.To is { } to)
                {
                    (model.ToReference, model.ToTime, model.ToOffsetMinutes) = (to.Reference, to.FixedTime, to.OffsetMinutes);
                }

                break;
            case DeviceResultTrigger result:
                (model.Kind, model.Operator, model.DeviceId, model.ActionId) = (TriggerKind.DeviceResult, result.Operator, result.DeviceAction.DeviceId, result.DeviceActionId);
                (model.PropertyPath, model.ExpectedValue, model.ValueType) = (result.PropertyPath, result.ExpectedValue, result.ValueType);
                break;
            case DeviceStatusTrigger status:
                (model.Kind, model.DeviceId, model.ExpectedStatus) = (TriggerKind.DeviceStatus, status.DeviceId, status.ExpectedStatus);
                break;
        }

        return model;
    }

    /// <summary>
    /// The entity of the chosen kind. Call it only when <see cref="Validate"/> returns null.
    /// </summary>
    public WorkflowTrigger ToEntity(int order) => Kind switch
    {
        TriggerKind.Date => new DateTrigger { Order = order, Operator = Operator, Date = Date!.Value, DateTo = IsRange ? DateTo : null },
        TriggerKind.Time => new TimeTrigger
        {
            Order = order,
            Operator = Operator,
            From = ToTimePoint(FromReference, FromTime, FromOffsetMinutes),
            To = IsRange ? ToTimePoint(ToReference, ToTime, ToOffsetMinutes) : null
        },
        TriggerKind.DeviceResult => new DeviceResultTrigger
        {
            Order = order,
            DeviceActionId = ActionId!.Value,
            PropertyPath = PropertyPath!,
            Operator = Operator,
            ExpectedValue = ExpectedValue!,
            ValueType = ValueType!.Value
        },
        TriggerKind.DeviceStatus => new DeviceStatusTrigger { Order = order, DeviceId = DeviceId!.Value, ExpectedStatus = ExpectedStatus },
        _ => throw new InvalidOperationException($"Unknown trigger kind '{Kind}'.")
    };

    // A fixed time has no offset, sunrise and sunset have no fixed time.
    private static TimePoint ToTimePoint(WorkflowTimeReference reference, TimeOnly? time, int offsetMinutes)
        => reference is WorkflowTimeReference.Fixed ? new(reference, time, 0) : new(reference, null, offsetMinutes);
}

public sealed class StepEditModel
{
    public StepKind Kind { get; private set; } = StepKind.DeviceAction;

    public int? DeviceId { get; set; }

    public int? ActionId { get; set; }

    public int? DelaySeconds { get; set; }

    public string? Title { get; set; }

    public string? MessageTemplate { get; set; }

    // Editor state, not saved.

    public string? Error { get; set; }

    public void SetKind(StepKind kind)
    {
        Kind = kind;
        (DeviceId, ActionId, DelaySeconds, Title, MessageTemplate, Error) = (null, null, null, null, null, null);
        if (kind is StepKind.Delay)
        {
            DelaySeconds = 10;
        }
    }

    /// <returns>What is missing, or null when the step is complete.</returns>
    public string? Validate() => Kind switch
    {
        StepKind.DeviceAction when ActionId is null => Strings.Validation_SelectDeviceAndAction,
        StepKind.Delay when DelaySeconds is null or <= 0 => Strings.Validation_DelayPositive,
        StepKind.Notify when string.IsNullOrWhiteSpace(Title) => Strings.Validation_EnterTitle,
        StepKind.Notify when string.IsNullOrWhiteSpace(MessageTemplate) => Strings.Validation_EnterMessage,
        _ => null
    };

    public static StepEditModel From(WorkflowStep step)
    {
        var model = new StepEditModel();
        switch (step)
        {
            case DeviceActionStep action:
                (model.Kind, model.DeviceId, model.ActionId) = (StepKind.DeviceAction, action.DeviceAction.DeviceId, action.DeviceActionId);
                break;
            case DelayStep delay:
                (model.Kind, model.DelaySeconds) = (StepKind.Delay, (int)delay.Delay.TotalSeconds);
                break;
            case NotifyStep notify:
                (model.Kind, model.Title, model.MessageTemplate) = (StepKind.Notify, notify.Title, notify.MessageTemplate);
                break;
        }

        return model;
    }

    /// <summary>
    /// The entity of the chosen kind. Call it only when <see cref="Validate"/> returns null.
    /// </summary>
    public WorkflowStep ToEntity(int order) => Kind switch
    {
        StepKind.DeviceAction => new DeviceActionStep { Order = order, DeviceActionId = ActionId!.Value },
        StepKind.Delay => new DelayStep { Order = order, Delay = TimeSpan.FromSeconds(DelaySeconds!.Value) },
        StepKind.Notify => new NotifyStep { Order = order, Title = Title!.Trim(), MessageTemplate = MessageTemplate! },
        _ => throw new InvalidOperationException($"Unknown step kind '{Kind}'.")
    };
}
