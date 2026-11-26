using HomeManagement.Application.Workflows;
using HomeManagement.Components.Shared;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HomeManagement.Components.Dialogs;

public partial class WorkflowEditDialog(WorkflowTriggerPreviewService previewService, IDialogService dialogService)
{
    [CascadingParameter] private IMudDialogInstance DialogReference { get; set; } = default!;

    [Parameter] public WorkflowEditModel Model { get; set; } = new();

    [Parameter] public IReadOnlyList<DeviceOption> Devices { get; set; } = [];

    private MudForm _form = default!;
    private string? _saveError;

    private async Task SaveAsync()
    {
        _saveError = null;
        await _form.ValidateAsync();
        foreach (var trigger in Model.Triggers)
        {
            trigger.Error = trigger.Validate();
        }

        foreach (var step in Model.Steps)
        {
            step.Error = step.Validate();
        }

        if (Model.Steps.Count == 0)
        {
            _saveError = Strings.WorkflowEdit_AddOneStep;
        }
        else if (!_form.IsValid || Model.Triggers.Any(x => x.Error is not null) || Model.Steps.Any(x => x.Error is not null))
        {
            _saveError = Strings.WorkflowEdit_CompleteFields;
        }

        if (_saveError is null)
        {
            DialogReference.Close(DialogResult.Ok(Model));
        }
    }

    private void Cancel() => DialogReference.Close(DialogResult.Cancel());

    private void AddTrigger() => Model.Triggers.Add(new TriggerEditModel());

    private void AddStep() => Model.Steps.Add(new StepEditModel());

    private async Task RemoveAsync<T>(List<T> items, T item, string question)
    {
        if (await dialogService.ConfirmAsync(Strings.Common_ConfirmDelete, question))
        {
            items.Remove(item);
        }
    }

    private static void Move<T>(List<T> items, T item, int offset)
    {
        var index = items.IndexOf(item);
        var target = index + offset;
        if (index >= 0 && target >= 0 && target < items.Count)
        {
            (items[index], items[target]) = (items[target], items[index]);
        }
    }

    private static void OnTriggerDeviceChanged(TriggerEditModel trigger, int? deviceId)
    {
        trigger.DeviceId = deviceId;
        trigger.ActionId = null;
        trigger.ClearProperty();
    }

    private static void OnTriggerActionChanged(TriggerEditModel trigger, int? actionId)
    {
        trigger.ActionId = actionId;
        trigger.ClearProperty();
    }

    private static void OnStepDeviceChanged(StepEditModel step, int? deviceId)
    {
        step.DeviceId = deviceId;
        step.ActionId = null;
    }

    private async Task PreviewAsync(TriggerEditModel trigger)
    {
        if (trigger.ActionId is not { } actionId)
        {
            return;
        }

        trigger.IsPreviewLoading = true;
        try
        {
            trigger.Preview = await previewService.PreviewAsync(actionId);
            if (trigger.Preview.IsSuccess && !trigger.Properties.Any(x => x.Path == trigger.PropertyPath))
            {
                trigger.SetProperty(trigger.Properties[0].Path);
            }
        }
        finally
        {
            trigger.IsPreviewLoading = false;
        }
    }

    private static string TriggerKindName(TriggerKind kind) => kind switch
    {
        TriggerKind.Date => Strings.TriggerKind_Date,
        TriggerKind.Time => Strings.TriggerKind_Time,
        TriggerKind.DeviceResult => Strings.TriggerKind_DeviceResult,
        TriggerKind.DeviceStatus => Strings.TriggerKind_DeviceStatus,
        _ => kind.ToString()
    };

    private static string TriggerKindIcon(TriggerKind kind) => kind switch
    {
        TriggerKind.Date => WorkflowIcons.DateTrigger,
        TriggerKind.Time => WorkflowIcons.TimeTrigger,
        TriggerKind.DeviceResult => WorkflowIcons.DeviceValueTrigger,
        TriggerKind.DeviceStatus => WorkflowIcons.DeviceStatusTrigger,
        _ => WorkflowIcons.Workflow
    };

    private static string StepKindName(StepKind kind) => kind switch
    {
        StepKind.DeviceAction => Strings.StepKind_DeviceAction,
        StepKind.Delay => Strings.StepKind_Delay,
        StepKind.Notify => Strings.StepKind_Notify,
        _ => kind.ToString()
    };

    private static string StepKindIcon(StepKind kind) => kind switch
    {
        StepKind.DeviceAction => WorkflowIcons.DeviceActionStep,
        StepKind.Delay => WorkflowIcons.DelayStep,
        StepKind.Notify => WorkflowIcons.NotifyStep,
        _ => WorkflowIcons.Workflow
    };

    private IReadOnlyList<ActionOption> ActionsOf(int? deviceId) => Devices.FirstOrDefault(x => x.Id == deviceId)?.Actions ?? [];

    private string DeviceName(int? deviceId) => Devices.FirstOrDefault(x => x.Id == deviceId)?.Name ?? string.Empty;

    private string ActionName(int? actionId) => Devices.SelectMany(x => x.Actions).FirstOrDefault(x => x.Id == actionId)?.Name ?? string.Empty;

    private static DateTime? ToDateTime(DateOnly? date) => date?.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? ToDateOnly(DateTime? date) => date is null ? null : DateOnly.FromDateTime(date.Value);

    private static TimeOnly? ToTimeOnly(TimeSpan? time) => time is null ? null : TimeOnly.FromTimeSpan(time.Value);
}
