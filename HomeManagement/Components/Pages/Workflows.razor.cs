using HomeManagement.Application;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Application.Workflows;
using HomeManagement.Components.Dialogs;
using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HomeManagement.Components.Pages;

public partial class Workflows(
    ISnackbar snackbar,
    IDialogService dialogService,
    WorkflowService workflowService,
    DeviceService deviceService) : ComponentBase
{
    private static readonly DialogOptions EditorOptions = new()
    {
        FullWidth = true,
        MaxWidth = MaxWidth.ExtraExtraLarge,
        Position = DialogPosition.Center,
        BackdropClick = false
    };

    private readonly HashSet<int> _runningWorkflows = [];
    private List<Workflow>? _workflows;

    protected override Task OnInitializedAsync() => ReloadAsync();

    private async Task ReloadAsync() => _workflows = await workflowService.GetWorkflowsAsync();

    private Task AddAsync() => EditAsync(null);

    private async Task EditAsync(Workflow? workflow)
    {
        var parameters = new DialogParameters<WorkflowEditDialog>
        {
            { x => x.Model, workflow is null ? new WorkflowEditModel() : WorkflowEditModel.From(workflow) },
            { x => x.Devices, await GetDeviceOptionsAsync() }
        };
        var dialog = await dialogService.ShowAsync<WorkflowEditDialog>(workflow is null ? "Add workflow" : $"Edit {workflow.Name}", parameters, EditorOptions);
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
        if (await dialogService.ConfirmAsync("Delete workflow", $"Are you sure you want to delete workflow '{workflow.Name}'?"))
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
