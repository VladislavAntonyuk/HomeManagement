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

    private async Task<TableData<Workflow>> ServerReload(TableState state, CancellationToken token)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var query = dbContext.Workflows
            .AsNoTracking()
            .Include(w => w.TriggerConditions)
            .Include(w => w.Steps)
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

    private static string GetTriggerSummary(Workflow workflow)
    {
        var conditions = workflow.TriggerConditions.ToList();

        if (conditions.Count == 0)
        {
            return "No conditions";
        }

        var delimiter = workflow.ConditionOperator is WorkflowConditionOperator.Or ? " OR " : " AND ";
        return string.Join(delimiter, conditions.Select(FormatCondition));
    }

    private static string FormatCondition(WorkflowTriggerCondition condition)
    {
        return condition.TriggerType switch
        {
            WorkflowTriggerType.DateTime when condition.ScheduledAt is not null
                => condition.ScheduledAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            WorkflowTriggerType.Sunrise => "Sunrise",
            WorkflowTriggerType.Sunset => "Sunset",
            WorkflowTriggerType.DeviceResult
                => $"{condition.TriggerDeviceName}/{condition.TriggerSourceActionName}:{condition.TriggerPropertyPath} {condition.TriggerOperator} {condition.TriggerExpectedValue}",
            _ => condition.TriggerType.ToString()
        };
    }

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
                TriggerConditions = workflow.TriggerConditions
                    .Select(x => new WorkflowTriggerConditionEditModel
                    {
                        TriggerType = x.TriggerType,
                        ScheduledAtLocal = x.ScheduledAt?.ToLocalTime().DateTime,
                        TriggerDeviceName = x.TriggerDeviceName,
                        TriggerSourceActionName = x.TriggerSourceActionName,
                        TriggerPropertyPath = x.TriggerPropertyPath,
                        TriggerExpectedValue = x.TriggerExpectedValue,
                        TriggerValueType = x.TriggerValueType,
                        TriggerOperator = x.TriggerOperator
                    }).ToList(),
                Steps = workflow.Steps
                    .Select(x => new WorkflowStepEditModel
                    {
                        StepType = x.StepType,
                        DeviceName = x.DeviceName,
                        ActionName = x.ActionName,
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
            var existing = await dbContext.Workflows.FirstOrDefaultAsync(x => x.Id == workflow.Id);
            if (existing is null)
            {
                snackbar.Add("Workflow not found.", Severity.Error);
                return;
            }

            dbContext.Workflows.Remove(existing);
            dbContext.Workflows.Add(MapToEntity(model));
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

        return devices
            .OrderBy(d => d.Name)
            .ToDictionary(
                d => d.Name,
                d => d.Actions.Select(a => a.Action).Order().ToList(),
                StringComparer.Ordinal);
    }

    private static Workflow MapToEntity(WorkflowEditModel model)
    {
        var triggerConditions = model.TriggerConditions.Select(x =>
        {
            DateTimeOffset? scheduledAt = x.ScheduledAtLocal is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(x.ScheduledAtLocal.Value, DateTimeKind.Local)).ToUniversalTime();

            return new WorkflowTriggerCondition
            {
                TriggerType = x.TriggerType,
                ScheduledAt = x.TriggerType is WorkflowTriggerType.DateTime ? scheduledAt : null,
                TriggerDeviceName = x.TriggerType is WorkflowTriggerType.DeviceResult ? x.TriggerDeviceName : null,
                TriggerSourceActionName = x.TriggerType is WorkflowTriggerType.DeviceResult ? x.TriggerSourceActionName : null,
                TriggerPropertyPath = x.TriggerType is WorkflowTriggerType.DeviceResult ? x.TriggerPropertyPath : null,
                TriggerExpectedValue = x.TriggerType is WorkflowTriggerType.DeviceResult ? x.TriggerExpectedValue : null,
                TriggerValueType = x.TriggerType is WorkflowTriggerType.DeviceResult ? x.TriggerValueType : null,
                TriggerOperator = x.TriggerType is WorkflowTriggerType.DeviceResult ? x.TriggerOperator : null
            };
        }).ToList();

        return new Workflow
        {
            Name = model.Name,
            Description = model.Description,
            IsEnabled = model.IsEnabled,
            ConditionOperator = model.ConditionOperator,
            TriggerConditions = triggerConditions,
            Steps = model.Steps.Select(x => new WorkflowStep
            {
                StepType = x.StepType,
                DeviceName = x.DeviceName,
                ActionName = x.ActionName,
                DelaySeconds = x.DelaySeconds,
                NotifyTitle = x.NotifyTitle,
                NotifyMessageTemplate = x.NotifyMessageTemplate
            }).ToList()
        };
    }
}