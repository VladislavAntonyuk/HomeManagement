using HomeManagement.Application.Workflows;
using HomeManagement.Components.Dialogs;
using HomeManagement.Infrastructure.Database;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using System.Linq.Expressions;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Components.Pages;

public partial class Workflows(
    ISnackbar snackbar,
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    IDialogService dialogService,
    IWorkflowRunner workflowRunner) : ComponentBase
{
    private MudTable<Workflow> _table = null!;
    private readonly HashSet<string> _runningWorkflows = new(StringComparer.Ordinal);

    private Dictionary<(string DeviceName, string ActionName), int> _actionIds = new();

    private async Task<TableData<Workflow>> ServerReload(TableState state, CancellationToken token)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var query = dbContext.Workflows
            .AsNoTracking()
            .Include(w => w.TriggerConditions)
            .ThenInclude(c => c.TriggerSourceAction)
            .ThenInclude(a => a!.Device)
            .Include(w => w.Steps.OrderBy(s => s.Order))
            .ThenInclude(s => s.DeviceAction)
            .ThenInclude(a => a!.Device)
            .AsQueryable();

        var totalItems = await query.CountAsync(token);
        query = ApplySorting(query, state.SortLabel, state.SortDirection);

        var workflows = await query.Skip(state.Page * state.PageSize).Take(state.PageSize).ToListAsync(token);

        return new TableData<Workflow> { TotalItems = totalItems, Items = workflows };
    }

    private static IQueryable<Workflow> ApplySorting(IQueryable<Workflow> query, string? sortLabel, SortDirection direction)
    {
        Expression<Func<Workflow, object?>> keySelector = sortLabel switch
        {
            nameof(Workflow.ConditionOperator) => x => x.ConditionOperator,
            nameof(Workflow.Name) => x => x.Name,
            _ => x => x.Name
        };

        return direction == SortDirection.Descending
            ? query.OrderByDescending(keySelector)
            : query.OrderBy(keySelector);
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
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            dbContext.Workflows.Add(MapToEntity(model));
            await dbContext.SaveChangesAsync();

            snackbar.Add($"Workflow '{model.Name}' added.", Severity.Success);
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
                        TriggerDeviceName = x.TriggerSourceAction?.Device.Name,
                        TriggerSourceActionName = x.TriggerSourceAction?.Action,
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
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var existing = await dbContext.Workflows
                .AsTracking()
                .Include(x => x.TriggerConditions)
                .Include(x => x.Steps)
                .FirstOrDefaultAsync(x => x.Id == workflow.Id);
            if (existing is null)
            {
                snackbar.Add("Workflow not found.", Severity.Error);
                return;
            }

            var updated = MapToEntity(model);
            existing.Name = updated.Name;
            existing.Description = updated.Description;
            existing.IsEnabled = updated.IsEnabled;
            existing.ConditionOperator = updated.ConditionOperator;
            existing.RepeatWhileMatched = updated.RepeatWhileMatched;
            existing.TriggerConditions = updated.TriggerConditions;
            existing.Steps = updated.Steps;
            existing.LastConditionMatched = null;
            await dbContext.SaveChangesAsync();

            snackbar.Add("Workflow updated.", Severity.Success);
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
            var result = await workflowRunner.RunAsync(workflow);

            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var existing = await dbContext.Workflows.FirstOrDefaultAsync(x => x.Id == workflow.Id);
            if (existing is not null)
            {
                existing.LastTriggeredAtUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync();
            }

            if (result.IsSuccessful)
            {
                snackbar.Add($"Workflow '{workflow.Name}' executed. Actions run: {result.ExecutedActions}.", Severity.Success);
            }
            else
            {
                snackbar.Add($"Workflow '{workflow.Name}' executed with errors: {string.Join(" | ", result.Errors)}", Severity.Warning, options => options.RequireInteraction = true);
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
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var existing = await dbContext.Workflows.FirstOrDefaultAsync(x => x.Id == workflow.Id);
            if (existing is null)
            {
                snackbar.Add("Workflow not found in DB.", Severity.Warning);
                return;
            }

            dbContext.Workflows.Remove(existing);
            await dbContext.SaveChangesAsync();

            snackbar.Add($"Deleted workflow '{workflow.Name}'.", Severity.Success);
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
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var devices = await dbContext.Devices.AsNoTracking().Include(d => d.Actions).ToListAsync();

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

    private Workflow MapToEntity(WorkflowEditModel model)
    {
        var triggerConditions = model.TriggerConditions.Select(x =>
        {
            var isDate = x.TriggerType is WorkflowTriggerType.Date;
            var isTime = x.TriggerType is WorkflowTriggerType.Time;
            var isDeviceResult = x.TriggerType is WorkflowTriggerType.DeviceResult;
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