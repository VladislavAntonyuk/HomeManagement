using HomeManagement.Application;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Workflows;
using HomeManagement.Components.Dialogs;
using HomeManagement.Components.Shared;
using HomeManagement.Infrastructure.Database.Entities;
using HomeManagement.Shared.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HomeManagement.Components.Pages;

public partial class Workflows(
    ISnackbar snackbar,
    IDialogService dialogService,
    WorkflowService workflowService,
    DeviceService deviceService,
    TimeProvider timeProvider) : ComponentBase
{
    private static readonly DialogOptions EditorOptions = new()
    {
        FullWidth = true,
        MaxWidth = MaxWidth.Medium,
        Position = DialogPosition.Center,
        BackdropClick = false
    };

    private readonly HashSet<int> _runningWorkflows = [];
    private List<Workflow>? _workflows;
    private string? _search;

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    protected override Task OnInitializedAsync() => ReloadAsync();

    private async Task ReloadAsync() => _workflows = await workflowService.GetWorkflowsAsync();

    private IEnumerable<Workflow> FilteredWorkflows()
    {
        var workflows = _workflows ?? [];
        return string.IsNullOrWhiteSpace(_search)
            ? workflows
            : workflows.Where(x => x.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
                                   || (x.Description?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private string Summary() => _workflows is null
        ? Strings.Common_Loading
        : string.Format(Strings.Workflows_Summary, _workflows.Count(x => x.IsEnabled), _workflows.Count);

    private static string TriggerIcon(WorkflowTrigger trigger) => trigger switch
    {
        DateTrigger => WorkflowIcons.DateTrigger,
        TimeTrigger => WorkflowIcons.TimeTrigger,
        DeviceResultTrigger => WorkflowIcons.DeviceValueTrigger,
        DeviceStatusTrigger => WorkflowIcons.DeviceStatusTrigger
    };

    private static string StepIcon(WorkflowStep step) => step switch
    {
        DeviceActionStep => WorkflowIcons.DeviceActionStep,
        DelayStep => WorkflowIcons.DelayStep,
        NotifyStep => WorkflowIcons.NotifyStep
    };

    private static string FormatStep(WorkflowStep step) => step switch
    {
        DeviceActionStep action => $"{action.DeviceAction.Device.Name}: {action.DeviceAction.Action}",
        DelayStep delay => string.Format(Strings.Workflows_StepWait, DisplayFormatter.FormatDuration(delay.Delay)),
        NotifyStep notify => string.Format(Strings.Workflows_StepNotify, notify.Title)
    };

    private async Task SetEnabledAsync(Workflow workflow, bool enabled)
        => await SaveAsync(() => workflowService.SetEnabledAsync(workflow.Id, enabled));

    private Task AddAsync() => EditAsync(null);

    private async Task EditAsync(Workflow? workflow)
    {
        var parameters = new DialogParameters<WorkflowEditDialog>
        {
            { x => x.Model, workflow is null ? new WorkflowEditModel() : WorkflowEditModel.From(workflow) },
            { x => x.Devices, await GetDeviceOptionsAsync() }
        };
        var dialog = await dialogService.ShowAsync<WorkflowEditDialog>(workflow is null ? Strings.Workflows_AddWorkflow : string.Format(Strings.Common_EditTitle, workflow.Name), parameters, EditorOptions);
        if (await dialog.Result is not { Canceled: false, Data: WorkflowEditModel model })
        {
            return;
        }

        await SaveAsync(() => workflow is null
            ? workflowService.AddAsync(model.ToEntity())
            : workflowService.UpdateAsync(workflow.Id, model.ToEntity()));
    }

    private async Task RunAsync(Workflow workflow)
    {
        if (!_runningWorkflows.Add(workflow.Id))
        {
            return;
        }

        try
        {
            var result = await workflowService.RunAsync(workflow.Id);
            snackbar.Add(result, Severity.Warning, requireInteraction: !result.IsSuccess);
        }
        finally
        {
            _runningWorkflows.Remove(workflow.Id);
            await ReloadAsync();
        }
    }

    private async Task DeleteAsync(Workflow workflow)
    {
        if (await dialogService.ConfirmAsync(Strings.Workflows_DeleteTitle, string.Format(Strings.Workflows_DeleteConfirm, workflow.Name)))
        {
            await SaveAsync(() => workflowService.DeleteAsync(workflow.Id));
        }
    }

    private async Task SaveAsync(Func<Task<OperationResult>> save)
    {
        snackbar.Add(await save());
        await ReloadAsync();
    }

    private async Task<IReadOnlyList<DeviceOption>> GetDeviceOptionsAsync()
        => [.. (await deviceService.ListAsync())
            .Select(x => new DeviceOption(
                x.Id,
                x.Name,
                [.. x.Actions.OrderBy(a => a.Action, StringComparer.CurrentCultureIgnoreCase).Select(a => new ActionOption(a.Id, a.Action))]))];
}
