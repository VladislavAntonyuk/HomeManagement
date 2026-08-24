using HomeManagement.Application.Workflows;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using System.Globalization;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Components.Dialogs;

public partial class WorkflowEditDialog
{
    [Inject] private IWorkflowTriggerPreviewService WorkflowTriggerPreviewService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    [CascadingParameter] private IMudDialogInstance DialogReference { get; set; } = default!;
    [Parameter] public WorkflowEditModel Model { get; set; } = new();
    [Parameter] public IReadOnlyDictionary<string, List<string>> AvailableActions { get; set; } = new Dictionary<string, List<string>>();

    private MudForm _form = default!;
    private bool _saving;
    private readonly Dictionary<int, DateTime?> _conditionDates = new();
    private readonly Dictionary<int, TimeSpan?> _conditionTimes = new();
    private readonly Dictionary<int, string?> _previewErrors = new();
    private readonly Dictionary<int, string?> _previewRawResponses = new();
    private readonly Dictionary<int, IReadOnlyList<WorkflowTriggerPropertyOption>> _previewProperties = new();
    private readonly HashSet<int> _previewLoadingConditions = [];
    private readonly Dictionary<int, double?> _conditionNumberExpectedValues = new();

    protected override void OnParametersSet()
    {
        if (Model.TriggerConditions.Count == 0)
        {
            Model.TriggerConditions.Add(new WorkflowTriggerConditionEditModel { TriggerType = WorkflowTriggerType.DateTime });
        }

        if (Model.Steps.Count == 0)
        {
            Model.Steps.Add(new WorkflowStepEditModel { StepType = WorkflowStepType.DeviceAction });
        }

        for (var i = 0; i < Model.TriggerConditions.Count; i++)
        {
            var condition = Model.TriggerConditions[i];
            _conditionDates[i] = condition.ScheduledAtLocal?.Date;
            _conditionTimes[i] = condition.ScheduledAtLocal?.TimeOfDay;

            if (condition.TriggerValueType is WorkflowTriggerValueType.Number
                && double.TryParse(condition.TriggerExpectedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                _conditionNumberExpectedValues[i] = number;
            }
        }
    }

    private async Task Save()
    {
        _saving = true;
        await _form.ValidateAsync();
        if (!_form.IsValid)
        {
            _saving = false;
            return;
        }

        if (Model.TriggerConditions.Count == 0)
        {
            _saving = false;
            return;
        }

        if (Model.Steps.Count == 0)
        {
            _saving = false;
            return;
        }

        for (var i = 0; i < Model.TriggerConditions.Count; i++)
        {
            var condition = Model.TriggerConditions[i];

            if (condition.TriggerType is WorkflowTriggerType.DateTime)
            {
                var date = GetConditionDate(i);
                var time = GetConditionTime(i);
                if (date is null || time is null)
                {
                    _saving = false;
                    return;
                }

                condition.ScheduledAtLocal = date.Value.Date + time.Value;
            }
            else
            {
                condition.ScheduledAtLocal = null;
            }

            if (condition.TriggerType is WorkflowTriggerType.DeviceResult
                && (string.IsNullOrWhiteSpace(condition.TriggerDeviceName)
                    || string.IsNullOrWhiteSpace(condition.TriggerSourceActionName)
                    || string.IsNullOrWhiteSpace(condition.TriggerPropertyPath)
                    || string.IsNullOrWhiteSpace(condition.TriggerExpectedValue)
                    || condition.TriggerOperator is null
                    || condition.TriggerValueType is null))
            {
                _previewErrors[i] = "Complete DeviceResult setup using Preview Response before saving.";
                _saving = false;
                return;
            }

            if (condition.TriggerType is WorkflowTriggerType.DeviceResult && condition.TriggerValueType is WorkflowTriggerValueType.Number)
            {
                if (!double.TryParse(condition.TriggerExpectedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedNumber))
                {
                    _previewErrors[i] = "Expected value must be a valid number.";
                    _saving = false;
                    return;
                }
            }
        }

        DialogReference.Close(DialogResult.Ok(Model));
    }

    private void Cancel() => DialogReference.Close(DialogResult.Cancel());

    private void AddCondition()
    {
        Model.TriggerConditions.Add(new WorkflowTriggerConditionEditModel { TriggerType = WorkflowTriggerType.DateTime });
    }

    private async Task RemoveCondition(int index)
    {
        if (index < 0 || index >= Model.TriggerConditions.Count)
        {
            return;
        }

        if (!await ConfirmDeleteAsync("condition"))
        {
            return;
        }

        Model.TriggerConditions.RemoveAt(index);
        ClearConditionState(index);
    }

    private void OnConditionTypeChanged(int index, WorkflowTriggerType value)
    {
        if (!TryGetCondition(index, out var condition))
        {
            return;
        }

        condition.TriggerType = value;
        condition.ScheduledAtLocal = null;
        condition.TriggerDeviceName = null;
        condition.TriggerSourceActionName = null;
        condition.TriggerPropertyPath = null;
        condition.TriggerExpectedValue = null;
        condition.TriggerValueType = null;
        condition.TriggerOperator = null;

        _conditionDates[index] = null;
        _conditionTimes[index] = null;
        ClearConditionPreview(index);
    }

    private DateTime? GetConditionDate(int index) => _conditionDates.TryGetValue(index, out var value) ? value : null;

    private TimeSpan? GetConditionTime(int index) => _conditionTimes.TryGetValue(index, out var value) ? value : null;

    private void OnConditionDateChanged(int index, DateTime? date)
    {
        _conditionDates[index] = date;
    }

    private void OnConditionTimeChanged(int index, TimeSpan? time)
    {
        _conditionTimes[index] = time;
    }

    private void OnConditionDeviceChanged(int index, string value)
    {
        if (!TryGetCondition(index, out var condition))
        {
            return;
        }

        condition.TriggerDeviceName = value;
        condition.TriggerSourceActionName = GetActionsForDevice(value).FirstOrDefault();
        ClearConditionPreview(index);
    }

    private void OnConditionActionChanged(int index, string value)
    {
        if (!TryGetCondition(index, out var condition))
        {
            return;
        }

        condition.TriggerSourceActionName = value;
        ClearConditionPreview(index);
    }

    private bool CanPreviewCondition(int index)
    {
        return TryGetCondition(index, out var condition)
            && !string.IsNullOrWhiteSpace(condition.TriggerDeviceName)
            && !string.IsNullOrWhiteSpace(condition.TriggerSourceActionName);
    }

    private bool IsConditionPreviewLoading(int index) => _previewLoadingConditions.Contains(index);

    private string? GetConditionPreviewError(int index) => _previewErrors.TryGetValue(index, out var value) ? value : null;

    private string? GetConditionPreviewRawResponse(int index) => _previewRawResponses.TryGetValue(index, out var value) ? value : null;

    private IReadOnlyList<WorkflowTriggerPropertyOption> GetConditionPreviewProperties(int index)
        => _previewProperties.TryGetValue(index, out var value) ? value : [];

    private async Task PreviewConditionResponse(int index)
    {
        if (!TryGetCondition(index, out var condition) || !CanPreviewCondition(index))
        {
            return;
        }

        _previewErrors[index] = null;
        _previewRawResponses[index] = null;
        _previewProperties[index] = [];
        _previewLoadingConditions.Add(index);

        try
        {
            var preview = await WorkflowTriggerPreviewService.PreviewAsync(condition.TriggerDeviceName!, condition.TriggerSourceActionName!);
            if (!preview.IsSuccess)
            {
                _previewErrors[index] = preview.Error;
                return;
            }

            _previewRawResponses[index] = preview.RawResponse;
            _previewProperties[index] = preview.Properties;
            if (preview.Properties.Count == 0)
            {
                _previewErrors[index] = "No comparable values found in response.";
                return;
            }

            if (!preview.Properties.Any(x => string.Equals(x.Path, condition.TriggerPropertyPath, StringComparison.Ordinal)))
            {
                OnConditionPropertyChanged(index, preview.Properties[0].Path);
            }
        }
        finally
        {
            _previewLoadingConditions.Remove(index);
        }
    }

    private void OnConditionPropertyChanged(int index, string value)
    {
        if (!TryGetCondition(index, out var condition))
        {
            return;
        }

        condition.TriggerPropertyPath = value;
        var selected = GetConditionPreviewProperties(index).FirstOrDefault(x => string.Equals(x.Path, value, StringComparison.Ordinal));
        if (selected is null)
        {
            return;
        }

        condition.TriggerValueType = selected.ValueType;
        condition.TriggerExpectedValue = selected.SampleValue;

        if (selected.ValueType is WorkflowTriggerValueType.Number
            && double.TryParse(selected.SampleValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            _conditionNumberExpectedValues[index] = number;
        }
        else
        {
            _conditionNumberExpectedValues[index] = null;
        }

        if (condition.TriggerOperator is not WorkflowComparisonOperator.Equal and not WorkflowComparisonOperator.NotEqual
            && selected.ValueType is not WorkflowTriggerValueType.Number)
        {
            condition.TriggerOperator = WorkflowComparisonOperator.Equal;
        }
    }

    private void OnConditionOperatorChanged(int index, WorkflowComparisonOperator? value)
    {
        if (TryGetCondition(index, out var condition))
        {
            condition.TriggerOperator = value;
        }
    }

    private void OnConditionBooleanExpectedValueChanged(int index, string value)
    {
        if (TryGetCondition(index, out var condition))
        {
            condition.TriggerExpectedValue = value;
        }
    }

    private double? GetConditionNumberExpectedValue(int index)
        => _conditionNumberExpectedValues.TryGetValue(index, out var value) ? value : null;

    private void OnConditionNumberExpectedValueChanged(int index, double? value)
    {
        _conditionNumberExpectedValues[index] = value;
        if (TryGetCondition(index, out var condition))
        {
            condition.TriggerExpectedValue = value?.ToString(CultureInfo.InvariantCulture);
        }
    }

    private void OnConditionExpectedValueChanged(int index, string value)
    {
        if (TryGetCondition(index, out var condition))
        {
            condition.TriggerExpectedValue = value;
        }
    }

    private void AddStep()
    {
        Model.Steps.Add(new WorkflowStepEditModel { StepType = WorkflowStepType.DeviceAction });
    }

    private async Task RemoveStep(int index)
    {
        if (index < 0 || index >= Model.Steps.Count)
        {
            return;
        }

        if (!await ConfirmDeleteAsync("step"))
        {
            return;
        }

        Model.Steps.RemoveAt(index);
    }

    private async Task<bool> ConfirmDeleteAsync(string itemName)
    {
        var parameters = new DialogParameters
        {
            ["Message"] = $"Are you sure you want to delete this {itemName}?"
        };
        var dialog = await DialogService.ShowAsync<ConfirmationDialog>("Confirm delete", parameters);
        var result = await dialog.Result;
        return result is not null && !result.Canceled;
    }

    private void OnStepTypeChanged(int index, WorkflowStepType value)
    {
        if (!TryGetStep(index, out var step))
        {
            return;
        }

        step.StepType = value;
        step.DeviceName = null;
        step.ActionName = null;
        step.DelaySeconds = null;
        step.NotifyTitle = null;
        step.NotifyMessageTemplate = null;
    }

    private void OnStepDeviceChanged(int index, string value)
    {
        if (!TryGetStep(index, out var step))
        {
            return;
        }

        step.DeviceName = value;
        step.ActionName = GetActionsForDevice(value).FirstOrDefault();
    }

    private void OnStepActionChanged(int index, string value)
    {
        if (TryGetStep(index, out var step))
        {
            step.ActionName = value;
        }
    }

    private void OnStepDelayChanged(int index, int? value)
    {
        if (TryGetStep(index, out var step))
        {
            step.DelaySeconds = value;
        }
    }

    private void OnStepNotifyTitleChanged(int index, string value)
    {
        if (TryGetStep(index, out var step))
        {
            step.NotifyTitle = value;
        }
    }

    private void OnStepNotifyMessageChanged(int index, string value)
    {
        if (TryGetStep(index, out var step))
        {
            step.NotifyMessageTemplate = value;
        }
    }

    private IEnumerable<string> GetActionsForDevice(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return [];
        }

        return AvailableActions.TryGetValue(deviceName, out var actions)
            ? actions
            : [];
    }

    private static IEnumerable<WorkflowComparisonOperator> GetAllowedOperators(WorkflowTriggerValueType? valueType)
    {
        if (valueType is WorkflowTriggerValueType.Number)
        {
            return Enum.GetValues<WorkflowComparisonOperator>();
        }

        return [WorkflowComparisonOperator.Equal, WorkflowComparisonOperator.NotEqual];
    }

    private bool TryGetCondition(int index, out WorkflowTriggerConditionEditModel condition)
    {
        if (index < 0 || index >= Model.TriggerConditions.Count)
        {
            condition = null!;
            return false;
        }

        condition = Model.TriggerConditions[index];
        return true;
    }

    private bool TryGetStep(int index, out WorkflowStepEditModel step)
    {
        if (index < 0 || index >= Model.Steps.Count)
        {
            step = null!;
            return false;
        }

        step = Model.Steps[index];
        return true;
    }

    private void ClearConditionPreview(int index)
    {
        _previewErrors[index] = null;
        _previewRawResponses[index] = null;
        _previewProperties[index] = [];

        if (TryGetCondition(index, out var condition))
        {
            condition.TriggerPropertyPath = null;
            condition.TriggerExpectedValue = null;
            condition.TriggerValueType = null;
            condition.TriggerOperator = null;
        }

        _conditionNumberExpectedValues[index] = null;
    }

    private void ClearConditionState(int index)
    {
        _conditionDates.Remove(index);
        _conditionTimes.Remove(index);
        _previewErrors.Remove(index);
        _previewRawResponses.Remove(index);
        _previewProperties.Remove(index);
        _previewLoadingConditions.Remove(index);
        _conditionNumberExpectedValues.Remove(index);
    }
}