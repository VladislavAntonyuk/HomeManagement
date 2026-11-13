using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Workflows;
using HomeManagement.Components.Dialogs;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Components.Pages;

public partial class Workflows(
    ISnackbar snackbar,
    IDialogService dialogService,
    WorkflowService workflowService,
    DeviceService deviceService) : ComponentBase
{
    private MudTable<Workflow> _table = null!;
    private readonly HashSet<string> _runningWorkflows = new(StringComparer.Ordinal);

    private Dictionary<(string DeviceName, string ActionName), int> _actionIds = new();

    private Dictionary<string, int> _deviceIds = new(StringComparer.Ordinal);

    private async Task<TableData<Workflow>> ServerReload(TableState state, CancellationToken token)
    {
        var workflows = await workflowService.GetWorkflowsAsync(token);
        IEnumerable<Workflow> sorted = state.SortLabel switch
        {
            nameof(Workflow.ConditionOperator) => state.SortDirection == SortDirection.Descending
                ? workflows.OrderByDescending(x => x.ConditionOperator)
                : workflows.OrderBy(x => x.ConditionOperator),
            _ => state.SortDirection == SortDirection.Descending
                ? workflows.OrderByDescending(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                : workflows.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        return new TableData<Workflow>
        {
            TotalItems = workflows.Count,
            Items = sorted.Skip(state.Page * state.PageSize).Take(state.PageSize).ToList()
        };
    }

    private static string GetTriggerSummary(Workflow workflow) => WorkflowConditionFormatter.FormatTrigger(workflow);

    private async Task Add()
    {
        var availableActions = await BuildAvailableActionsAsync();
        var parameters = new DialogParameters
        {
            ["Model"] = new WorkflowEditModel(),
            ["AvailableActions"] = availableActions
        };
        var options = new DialogOptions()
        {
            FullWidth = true,
            MaxWidth = MaxWidth.ExtraExtraLarge,
            Position = DialogPosition.Center,
            BackdropClick = false
        };
        var dialog = await dialogService.ShowAsync<WorkflowEditDialog>("Add workflow", parameters, options);
        var result = await dialog.Result;
        if (result is null || result.Canceled)
        {
            return;
        }

        var model = (WorkflowEditModel)result.Data!;

        try
        {
            var added = await workflowService.AddAsync(MapToEntity(model));
            snackbar.Add(added.Message, Severity.Success);
            await _table.ReloadServerData();
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to add workflow: {ex.Message}", Severity.Error);
        }
    }

    private async Task Edit(Workflow workflow)
    {
        var availableActions = await BuildAvailableActionsAsync();
        var parameters = new DialogParameters
        {
            ["Model"] = new WorkflowEditModel
            {
                Name = workflow.Name,
                Description = workflow.Description,
                IsEnabled = workflow.IsEnabled,
                ConditionOperator = workflow.ConditionOperator,
                RepeatWhileMatched = workflow.RepeatWhileMatched,
                TriggerConditions = workflow.TriggerConditions
                    .Select(x => new WorkflowTriggerConditionEditModel
                    {
                        TriggerType = x.TriggerType,
                        TriggerDate = x.TriggerDate,
                        TriggerDateTo = x.TriggerDateTo,
                        TriggerTimeReference = x.TriggerTimeReference,
                        TriggerTime = x.TriggerTime,
                        TriggerTimeOffsetMinutes = x.TriggerTimeOffsetMinutes,
                        TriggerTimeToReference = x.TriggerTimeToReference,
                        TriggerTimeTo = x.TriggerTimeTo,
                        TriggerTimeToOffsetMinutes = x.TriggerTimeToOffsetMinutes,
                        TriggerDeviceName = x.TriggerDevice?.Name ?? x.TriggerSourceAction?.Device.Name,
                        TriggerSourceActionName = x.TriggerSourceAction?.Action,
                        TriggerDeviceStatus = x.TriggerDeviceStatus,
                        TriggerPropertyPath = x.TriggerPropertyPath,
                        TriggerExpectedValue = x.TriggerExpectedValue,
                        TriggerValueType = x.TriggerValueType,
                        TriggerOperator = x.TriggerOperator
                    }).ToList(),
                Steps = workflow.Steps
                    .Select(x => new WorkflowStepEditModel
                    {
                        StepType = x.StepType,
                        DeviceName = x.DeviceAction?.Device.Name,
                        ActionName = x.DeviceAction?.Action,
                        DelaySeconds = x.DelaySeconds,
                        NotifyTitle = x.NotifyTitle,
                        NotifyMessageTemplate = x.NotifyMessageTemplate
                    }).ToList()
            },
            ["AvailableActions"] = availableActions
        };
        var options = new DialogOptions()
        {
            FullWidth = true,
            MaxWidth = MaxWidth.ExtraExtraLarge,
            Position = DialogPosition.Center,
            BackdropClick = false
        };
        var dialog = await dialogService.ShowAsync<WorkflowEditDialog>($"Edit {workflow.Name}", parameters, options);
        var result = await dialog.Result;
        if (result is null || result.Canceled)
        {
            return;
        }

        var model = (WorkflowEditModel)result.Data!;

        try
        {
            var updated = await workflowService.UpdateAsync(workflow.Id, MapToEntity(model));
            snackbar.Add(updated.Message, updated.IsSuccess ? Severity.Success : Severity.Error);
            await _table.ReloadServerData();
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to update workflow: {ex.Message}", Severity.Error);
        }
    }

    private async Task Run(Workflow workflow)
    {
        if (!_runningWorkflows.Add(workflow.Name))
        {
            return;
        }

        try
        {
            var result = await workflowService.RunAsync(workflow.Id);
            if (result.IsSuccess)
            {
                snackbar.Add(result.Message, Severity.Success);
            }
            else
            {
                snackbar.Add(result.Message, Severity.Warning, options => options.RequireInteraction = true);
            }
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to run workflow: {ex.Message}", Severity.Error);
        }
        finally
        {
            _runningWorkflows.Remove(workflow.Name);
            await _table.ReloadServerData();
        }
    }

    private async Task Delete(Workflow workflow)
    {
        if (!await ConfirmDeleteWorkflowAsync(workflow.Name))
        {
            return;
        }

        try
        {
            var deleted = await workflowService.DeleteAsync(workflow.Id);
            snackbar.Add(deleted.Message, deleted.IsSuccess ? Severity.Success : Severity.Warning);
            await _table.ReloadServerData();
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to delete workflow: {ex.Message}", Severity.Error);
        }
    }

    private async Task<bool> ConfirmDeleteWorkflowAsync(string workflowName)
    {
        var parameters = new DialogParameters
        {
            ["Message"] = $"Are you sure you want to delete workflow '{workflowName}'?"
        };
        var dialog = await dialogService.ShowAsync<ConfirmationDialog>("Delete workflow", parameters);
        var result = await dialog.Result;
        return result is not null && !result.Canceled;
    }

    private async Task<Dictionary<string, List<string>>> BuildAvailableActionsAsync()
    {
        var devices = (await deviceService.GetDevicesAsync()).Select(x => x.Device).ToList();

        _deviceIds = devices.ToDictionary(d => d.Name, d => d.Id, StringComparer.Ordinal);
        _actionIds = devices
            .SelectMany(d => d.Actions.Select(a => (Key: (d.Name, a.Action), a.Id)))
            .DistinctBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Id);

        return devices
            .OrderBy(d => d.Name)
            .ToDictionary(
                d => d.Name,
                d => d.Actions.Select(a => a.Action).Order().ToList(),
                StringComparer.Ordinal);
    }

    private int? ResolveActionId(string? deviceName, string? actionName)
    {
        if (string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(actionName))
        {
            return null;
        }

        return _actionIds.TryGetValue((deviceName, actionName), out var id)
            ? id
            : throw new InvalidOperationException($"Action '{actionName}' no longer exists on device '{deviceName}'.");
    }

    private int? ResolveDeviceId(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return null;
        }

        return _deviceIds.TryGetValue(deviceName, out var id)
            ? id
            : throw new InvalidOperationException($"Device '{deviceName}' no longer exists.");
    }

    private Workflow MapToEntity(WorkflowEditModel model)
    {
        var triggerConditions = model.TriggerConditions.Select(x =>
        {
            var isDate = x.TriggerType is WorkflowTriggerType.Date;
            var isTime = x.TriggerType is WorkflowTriggerType.Time;
            var isDeviceResult = x.TriggerType is WorkflowTriggerType.DeviceResult;
            var isDeviceStatus = x.TriggerType is WorkflowTriggerType.DeviceStatus;
            var isRange = WorkflowConditionFormatter.IsRange(x.TriggerOperator);

            return new WorkflowTriggerCondition
            {
                TriggerType = x.TriggerType,
                TriggerOperator = isDate || isTime || isDeviceResult ? x.TriggerOperator : null,
                TriggerDate = isDate ? x.TriggerDate : null,
                TriggerDateTo = isDate && isRange ? x.TriggerDateTo : null,
                TriggerTimeReference = isTime ? x.TriggerTimeReference : null,
                TriggerTime = isTime && x.TriggerTimeReference is WorkflowTimeReference.Fixed ? x.TriggerTime : null,
                TriggerTimeOffsetMinutes = isTime && x.TriggerTimeReference is not WorkflowTimeReference.Fixed ? x.TriggerTimeOffsetMinutes : null,
                TriggerTimeToReference = isTime && isRange ? x.TriggerTimeToReference : null,
                TriggerTimeTo = isTime && isRange && x.TriggerTimeToReference is WorkflowTimeReference.Fixed ? x.TriggerTimeTo : null,
                TriggerTimeToOffsetMinutes = isTime && isRange && x.TriggerTimeToReference is not WorkflowTimeReference.Fixed ? x.TriggerTimeToOffsetMinutes : null,
                TriggerSourceActionId = isDeviceResult ? ResolveActionId(x.TriggerDeviceName, x.TriggerSourceActionName) : null,
                TriggerDeviceId = isDeviceStatus ? ResolveDeviceId(x.TriggerDeviceName) : null,
                TriggerDeviceStatus = isDeviceStatus ? x.TriggerDeviceStatus : null,
                TriggerPropertyPath = isDeviceResult ? x.TriggerPropertyPath : null,
                TriggerExpectedValue = isDeviceResult ? x.TriggerExpectedValue : null,
                TriggerValueType = isDeviceResult ? x.TriggerValueType : null
            };
        }).ToList();

        return new Workflow
        {
            Name = model.Name,
            Description = model.Description,
            IsEnabled = model.IsEnabled,
            ConditionOperator = model.ConditionOperator,
            RepeatWhileMatched = model.RepeatWhileMatched,
            TriggerConditions = triggerConditions,
            Steps = model.Steps.Select((x, index) => new WorkflowStep
            {
                Order = index,
                StepType = x.StepType,
                DeviceActionId = x.StepType is WorkflowStepType.DeviceAction ? ResolveActionId(x.DeviceName, x.ActionName) : null,
                DelaySeconds = x.DelaySeconds,
                NotifyTitle = x.NotifyTitle,
                NotifyMessageTemplate = x.NotifyMessageTemplate
            }).ToList()
        };
    }
}