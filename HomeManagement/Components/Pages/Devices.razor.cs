using HomeManagement.Application.DeviceManagement;
using HomeManagement.Components.Dialogs;
using HomeManagement.Infrastructure;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using System.Linq.Expressions;
using System.Net.NetworkInformation;

namespace HomeManagement.Components.Pages;

public partial class Devices(
    ISnackbar snackbar,
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    IDialogService dialogService,
    IDeviceActionExecutor deviceActionExecutor,
    IDeviceDiscoveryService deviceDiscoveryService) : ComponentBase, IAsyncDisposable
{
    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(10);

    private MudTable<Device> _table = null!;
    private readonly Dictionary<string, DeviceStatus> _statuses = new();
    private readonly HashSet<(string DeviceName, DeviceAction Action)> _runningActions = new();
    private readonly CancellationTokenSource _statusRefreshCts = new();
    private Task? _statusRefreshTask;

    private async Task<TableData<Device>> ServerReload(TableState state, CancellationToken token)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(token);
        var query = dbContext.Devices.AsNoTracking()
            .Include(d => d.Actions)
            .Include(d => d.Configurations)
            .AsQueryable();

        var totalItems = await query.CountAsync(token);
        query = ApplySorting(query, state.SortLabel, state.SortDirection);
        var devices = await query.Skip(state.Page * state.PageSize).Take(state.PageSize).ToListAsync(token);

        // Start/refresh status fetch for current page devices
        _ = UpdateStatusesAsync(devices, token);

        return new TableData<Device>() { TotalItems = totalItems, Items = devices };
    }

    private static IQueryable<Device> ApplySorting(IQueryable<Device> query, string? sortLabel, SortDirection stateSortDirection)
    {
        Expression<Func<Device, object?>> keySelector = sortLabel switch
        {
            nameof(Device.Address) => c => c.Address,
            nameof(Device.Name) => c => c.Name,
            _ => c => c.Address
        };

        return stateSortDirection == SortDirection.Descending
            ? query.OrderByDescending(keySelector)
            : query.OrderBy(keySelector);
    }

    private async Task UpdateStatusesAsync(IEnumerable<Device> devices, CancellationToken token)
    {
        var tasks = new List<Task>();
        foreach (var device in devices)
        {
            if (!_statuses.TryGetValue(device.Name, out var st))
            {
                st = new DeviceStatus();
                _statuses[device.Name] = st;
            }

            if (st.IsChecking)
            {
                continue;
            }

            st.IsChecking = true;
            tasks.Add(UpdateDeviceStatusAsync(device, st, token));
        }

        await foreach (var _ in Task.WhenEach(tasks).WithCancellation(token))
        {
            StateHasChanged();
        }
    }

    private async Task UpdateDeviceStatusAsync(Device device, DeviceStatus status, CancellationToken token)
    {
        try
        {
            if (DeviceAddress.IsPhysicalAddress(device.Address))
            {
                status.Online = null;
                return;
            }

            var info = await deviceDiscoveryService.GetDeviceConfigAsync(device.Address, token);
            if (info is not null)
            {
                status.Online = true;
                return;
            }

            using var ping = new Ping();
            var reply = await ping.SendPingAsync(new Uri(device.Address).Host, 100);
            status.Online = reply.Status == IPStatus.Success;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            status.Online = false;
        }
        finally
        {
            status.Loading = false;
            status.IsChecking = false;
        }
    }

    private DeviceStatus? GetStatus(string name) => _statuses.TryGetValue(name, out var st) ? st : null;

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _statusRefreshTask = RefreshStatusesPeriodicallyAsync(_statusRefreshCts.Token);
        }

        return Task.CompletedTask;
    }

    private async Task RefreshStatusesPeriodicallyAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(StatusRefreshInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                if (_table.FilteredItems is not null)
                {
                    await InvokeAsync(() => UpdateStatusesAsync(_table.FilteredItems, token));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the component is disposed.
        }
    }

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

    private async Task ScanNetworkDevices()
    {
        var parameters = new DialogParameters
        {
            [nameof(ScanDevicesDialog.ExistingDevices)] = await GetExistingDeviceKeysAsync()
        };
        var dialog = await dialogService.ShowAsync<ScanDevicesDialog>("Scan Network", parameters);
        var result = await dialog.Result;
        if (result is null || result.Canceled)
        {
            return;
        }

        var devices = (List<Shared.DeviceConfig>)result.Data!;
        if (devices.Count == 0)
        {
            return;
        }

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            foreach (var device in devices)
            {
                dbContext.Devices.Add(new Device
                {
                    Name = device.Name,
                    Address = device.Address,
                    Actions = device.Actions.Select(a => new DeviceAction
                    {
                        Action = a.Action,
                        Command = a.Command,
                        CommandArgs = a.CommandArgs,
                        CommandType = a.CommandType
                    }).ToList()
                });
            }

            await dbContext.SaveChangesAsync();
            snackbar.Add($"Added {devices.Count} device(s).", Severity.Success);
            await _table.ReloadServerData();
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to add scanned devices: {ex.InnerException?.Message ?? ex.Message}", Severity.Error);
        }
    }

    private async Task Add()
    {
        var dialog = await dialogService.ShowAsync<AddressDialog>("Add Device");
        var result = await dialog.Result;
        if (result is null || result.Canceled)
        {
            return;
        }

        var device = (Shared.DeviceConfig)result.Data!;
        var entity = new Device
        {
            Name = device.Name,
            Address = device.Address,
            Actions = device.Actions.Select(a => new DeviceAction
            {
                Action = a.Action,
                Command = a.Command,
                CommandArgs = a.CommandArgs,
                CommandType = a.CommandType
            }).ToList()
        };

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            dbContext.Devices.Add(entity);
            await dbContext.SaveChangesAsync();

            snackbar.Add($"Device '{device.Name}' added.", Severity.Success);
            await _table.ReloadServerData();
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to add device: {ex.InnerException?.Message ?? ex.Message}", Severity.Error);
            return;
        }

        if (entity.Actions.Count == 0)
        {
            await Edit(entity);
        }
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

    private async Task<List<string>> GetExistingDeviceKeysAsync()
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var devices = await dbContext.Devices.Select(d => new { d.Name, d.Address }).ToListAsync();
        return devices.SelectMany(d => new[] { d.Name, d.Address }).ToList();
    }

    private async Task Edit(Device device)
    {
        var parameters = new DialogParameters
        {
            ["Model"] = new DeviceEditModel
            {
                Id = device.Id,
                Name = device.Name,
                Address = device.Address,
                Description = device.Description,
                Actions = device.Actions.Select(a => new DeviceActionEditModel
                {
                    Id = a.Id,
                    Action = a.Action,
                    Command = a.Command,
                    CommandArgs = a.CommandArgs,
                    CommandType = a.CommandType
                }).ToList(),
                Configurations = device.Configurations.Select(c => new DeviceConfigurationEditModel
                {
                    Name = c.Name,
                    Value = c.Value
                }).ToList()
            },
            [nameof(DeviceEditDialog.ActionUsages)] = await GetActionUsagesAsync(device.Actions.Select(a => a.Id).ToList())
        };
        var options = new DialogOptions()
        {
            BackdropClick = false,
            FullWidth = true,
            MaxWidth = MaxWidth.ExtraExtraLarge,
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
            var existing = await dbContext.Devices.AsTracking()
                .Include(x => x.Actions)
                .Include(x => x.Configurations)
                .FirstOrDefaultAsync(d => d.Id == device.Id);
            if (existing is null)
            {
                snackbar.Add("Device not found.", Severity.Error);
                return;
            }

            existing.Name = model.Name;
            existing.Address = model.Address;
            existing.Description = model.Description;

            var keptActionIds = model.Actions.Where(a => a.Id != 0).Select(a => a.Id).ToHashSet();
            var removedActions = existing.Actions.Where(a => !keptActionIds.Contains(a.Id)).ToList();
            var usages = await GetActionUsagesAsync(removedActions.Select(a => a.Id).ToList());
            if (usages.Count > 0)
            {
                snackbar.Add($"Cannot remove actions used by workflows: {string.Join(", ", usages.Values.SelectMany(x => x).Distinct())}.", Severity.Error);
                return;
            }

            foreach (var removedAction in removedActions)
            {
                existing.Actions.Remove(removedAction);
            }

            foreach (var action in model.Actions)
            {
                var target = existing.Actions.FirstOrDefault(a => action.Id != 0 && a.Id == action.Id);
                if (target is null)
                {
                    target = new DeviceAction { Action = action.Action, Command = action.Command };
                    existing.Actions.Add(target);
                }

                target.Action = action.Action;
                target.Command = action.Command;
                target.CommandArgs = action.CommandArgs;
                target.CommandType = action.CommandType;
            }

            existing.Configurations = model.Configurations.Select(c => new DeviceConfiguration
            {
                Name = c.Name,
                Value = c.Value
            }).ToList();
            await dbContext.SaveChangesAsync();

            snackbar.Add("Device updated.", Severity.Success);
            await _table.ReloadServerData();
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to update device: {ex.Message}", Severity.Error);
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

        if (!await ConfirmDeleteDeviceAsync(device.Name))
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
            snackbar.Add($"Deleted {device.Name}.", Severity.Success);
            await _table.ReloadServerData();
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to delete device: {ex.Message}", Severity.Error);
        }
    }

    private async Task<bool> ConfirmDeleteDeviceAsync(string deviceName)
    {
        var parameters = new DialogParameters
        {
            ["Message"] = $"Are you sure you want to delete '{deviceName}'?"
        };
        var dialog = await dialogService.ShowAsync<ConfirmationDialog>("Delete device", parameters);
        var result = await dialog.Result;
        return result is not null && !result.Canceled;
    }

    public async ValueTask DisposeAsync()
    {
        await _statusRefreshCts.CancelAsync();

        if (_statusRefreshTask is not null)
        {
            await _statusRefreshTask;
        }

        _statusRefreshCts.Dispose();
    }

    private class DeviceStatus
    {
        public bool? Online { get; set; }
        public bool Loading { get; set; } = true;
        public bool IsChecking { get; set; }
    }
}