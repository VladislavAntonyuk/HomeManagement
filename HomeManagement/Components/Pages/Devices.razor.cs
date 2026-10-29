using HomeManagement.Application.DeviceManagement;
using HomeManagement.Components.Dialogs;
using HomeManagement.Infrastructure;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using System.Linq.Expressions;

namespace HomeManagement.Components.Pages;

public partial class Devices(
    ISnackbar snackbar,
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    IDialogService dialogService,
    IDeviceActionExecutor deviceActionExecutor,
    IDeviceHub deviceHub,
    DeviceStateStore deviceStateStore) : ComponentBase, IDisposable
{
    private MudTable<Device> _table = null!;
    private readonly HashSet<(string DeviceName, DeviceAction Action)> _runningActions = new();

    protected override void OnInitialized()
    {
        deviceStateStore.StatusChanged += OnStatusChanged;
        deviceStateStore.DevicesChanged += OnDevicesChanged;
    }

    private async Task<TableData<Device>> ServerReload(TableState state, CancellationToken token)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var query = dbContext.Devices.AsNoTracking()
            .Include(d => d.Actions)
            .AsQueryable();

        var totalItems = await query.CountAsync(token);
        query = ApplySorting(query, state.SortLabel, state.SortDirection);
        var devices = await query.Skip(state.Page * state.PageSize).Take(state.PageSize).ToListAsync(token);

        return new TableData<Device>() { TotalItems = totalItems, Items = devices };
    }

    private static IQueryable<Device> ApplySorting(IQueryable<Device> query, string? sortLabel, SortDirection stateSortDirection)
    {
        Expression<Func<Device, object?>> keySelector = sortLabel switch
        {
            nameof(Device.DeviceId) => c => c.DeviceId,
            nameof(Device.Name) => c => c.Name,
            _ => c => c.Name
        };

        return stateSortDirection == SortDirection.Descending
            ? query.OrderByDescending(keySelector)
            : query.OrderBy(keySelector);
    }

    private bool? IsOnline(Device device) => deviceStateStore.IsOnline(device.DeviceId);

    private void OnStatusChanged() => _ = InvokeAsync(StateHasChanged);

    private void OnDevicesChanged() => _ = InvokeAsync(_table.ReloadServerData);

    private async Task RunAction(Device device, DeviceAction action)
    {
        var key = (device.Name, action);
        if (!_runningActions.Add(key))
        {
            return;
        }

        try
        {
            var result = await deviceActionExecutor.ExecuteAsync(device, action);
            var message = FormatMessage(result.Message);
            snackbar.Add(message,
                result.IsSuccess ? Severity.Success : Severity.Error,
                options => options.RequireInteraction = true);
        }
        finally
        {
            _runningActions.Remove(key);
        }
    }

    private string FormatMessage(string resultMessage)
    {
        const int maxLength = 1000;
        var message = resultMessage;
        try
        {
            message = JsonFlattener.FlattenToString(resultMessage);
        }
        catch
        {
            // Ignore any exceptions and use the original message
        }
        if (message.Length > maxLength)
        {
            message = $"{message[..maxLength]}...";
        }

        return message;
    }

    private async Task<Dictionary<int, List<string>>> GetActionUsagesAsync(IReadOnlyCollection<int> actionIds)
    {
        if (actionIds.Count == 0)
        {
            return [];
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var stepUsages = await dbContext.Set<WorkflowStep>()
            .Where(s => s.DeviceActionId != null && actionIds.Contains(s.DeviceActionId.Value))
            .Join(dbContext.Workflows, s => s.WorkflowId, w => w.Id, (s, w) => new { ActionId = s.DeviceActionId!.Value, w.Name })
            .ToListAsync();
        var conditionUsages = await dbContext.Set<WorkflowTriggerCondition>()
            .Where(c => c.TriggerSourceActionId != null && actionIds.Contains(c.TriggerSourceActionId.Value))
            .Join(dbContext.Workflows, c => c.WorkflowId, w => w.Id, (c, w) => new { ActionId = c.TriggerSourceActionId!.Value, w.Name })
            .ToListAsync();

        return stepUsages.Concat(conditionUsages)
            .GroupBy(x => x.ActionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).Distinct().Order().ToList());
    }

    private async Task Edit(Device device)
    {
        var parameters = new DialogParameters
        {
            ["Model"] = new DeviceEditModel
            {
                Id = device.Id,
                Name = device.Name,
                Description = device.Description
            }
        };
        var options = new DialogOptions()
        {
            BackdropClick = false,
            FullWidth = true,
            MaxWidth = MaxWidth.Medium,
            Position = DialogPosition.Center
        };
        var dialog = await dialogService.ShowAsync<DeviceEditDialog>($"Edit {device.Name}", parameters, options);
        var result = await dialog.Result;
        if (result is null || result.Canceled)
        {
            return;
        }

        var model = (DeviceEditModel)result.Data!;
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var existing = await dbContext.Devices.AsTracking().FirstOrDefaultAsync(d => d.Id == device.Id);
            if (existing is null)
            {
                snackbar.Add("Device not found.", Severity.Error);
                return;
            }

            existing.Name = model.Name.Trim();
            existing.Description = model.Description;
            await dbContext.SaveChangesAsync();

            snackbar.Add("Device updated.", Severity.Success);
            await _table.ReloadServerData();
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to update device: {ex.InnerException?.Message ?? ex.Message}", Severity.Error);
        }
    }

    private async Task Delete(Device device)
    {
        var usages = await GetActionUsagesAsync(device.Actions.Select(a => a.Id).ToList());
        if (usages.Count > 0)
        {
            snackbar.Add($"'{device.Name}' is used by workflows: {string.Join(", ", usages.Values.SelectMany(x => x).Distinct())}. Remove it from those workflows first.", Severity.Warning);
            return;
        }

        var message = IsOnline(device) == true
            ? $"'{device.Name}' is online, so it registers again the next time it connects. Delete it anyway?"
            : $"Are you sure you want to delete '{device.Name}'?";
        if (!await ConfirmDeleteDeviceAsync(message))
        {
            return;
        }

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var existing = await dbContext.Devices.AsTracking().FirstOrDefaultAsync(d => d.Id == device.Id);
            if (existing is null)
            {
                snackbar.Add("Device not found in DB.", Severity.Warning);
                return;
            }

            dbContext.Devices.Remove(existing);
            await dbContext.SaveChangesAsync();
            await deviceHub.ForgetDeviceAsync(device.DeviceId);
            snackbar.Add($"Deleted {device.Name}.", Severity.Success);
            await _table.ReloadServerData();
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to delete device: {ex.Message}", Severity.Error);
        }
    }

    private async Task<bool> ConfirmDeleteDeviceAsync(string message)
    {
        var parameters = new DialogParameters
        {
            ["Message"] = message
        };
        var dialog = await dialogService.ShowAsync<ConfirmationDialog>("Delete device", parameters);
        var result = await dialog.Result;
        return result is not null && !result.Canceled;
    }

    public void Dispose()
    {
        deviceStateStore.StatusChanged -= OnStatusChanged;
        deviceStateStore.DevicesChanged -= OnDevicesChanged;
    }
}
