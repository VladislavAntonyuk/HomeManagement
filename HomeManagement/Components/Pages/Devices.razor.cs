using HomeManagement.Application.DeviceManagement;
using HomeManagement.Components.Dialogs;
using HomeManagement.Infrastructure.Database;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using System.Linq.Expressions;
using System.Net;
using System.Net.NetworkInformation;
using HomeManagement.Infrastructure.Database.Entities;
using HomeManagement.Shared;
using Device = HomeManagement.Infrastructure.Database.Entities.Device;
using DeviceAction = HomeManagement.Infrastructure.Database.Entities.DeviceAction;
using NetworkManager = HomeManagement.Application.DeviceManagement.NetworkManager;

namespace HomeManagement.Components.Pages;

public partial class Devices(
    ISnackbar snackbar,
    IDbContextFactory<HomeManagementDbContext> dbContextFactory,
    IDialogService dialogService,
    IDeviceActionExecutor deviceActionExecutor) : ComponentBase, IAsyncDisposable
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

            st.Loading = true;
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
            var info = await NetworkManager.GetDeviceInfoAsync(device.Address, token);
            if (info is not null)
            {
                status.Online = true;
                status.UptimeSeconds = info.UptimeSeconds;
                status.Temperature = info.Temperature;
            }
            else
            {
                if (PhysicalAddress.TryParse(device.Address, out _))
                {
                    status.Online = true;
                    return;
                }

                if (Uri.TryCreate(device.Address, UriKind.Absolute, out var address) || IPAddress.TryParse(device.Address, out _))
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(address is null ? device.Address : address.Host, 100);
                    status.Online = reply.Status == IPStatus.Success;
                    status.UptimeSeconds = 0;
                    status.Temperature = 0;
                }
                else
                {
                    status.Online = false;
                    status.UptimeSeconds = 0;
                }
            }
        }
        catch
        {
            status.Online = false;
            status.UptimeSeconds = 0;
        }
        finally
        {
            status.Loading = false;
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
            snackbar.Add(result.Message,
                result.IsSuccess ? Severity.Success : Severity.Error,
                options => options.RequireInteraction = true);
        }
        finally
        {
            _runningActions.Remove(key);
        }
    }

    private static string FormatDuration(int? seconds)
    {
        if (seconds is null)
        {
            return string.Empty;
        }

        var ts = TimeSpan.FromSeconds(seconds.Value);
        var parts = new List<string>();
        if (ts.Days > 0)
        {
            parts.Add($"{ts.Days}d");
        }

        if (ts.Hours > 0 || parts.Count > 0)
        {
            parts.Add($"{ts.Hours}h");
        }

        if (ts.Minutes > 0 || parts.Count > 0)
        {
            parts.Add($"{ts.Minutes}m");
        }

        parts.Add($"{ts.Seconds}s");
        return string.Join(" ", parts);
    }

    private async Task ScanNetworkDevices()
    {
        var dialog = await dialogService.ShowAsync<ScanDevicesDialog>("Scan Network");
        var result = await dialog.Result;
        if (result is null || result.Canceled)
        {
            return;
        }

        var devices = (List<NetworkDevice>)result.Data!;
        if (devices.Count == 0)
        {
            return;
        }

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            foreach (var device in devices)
            {
                if (!device.Address.StartsWith("http"))
                {
                    device.Address = "http://" + device.Address;
                }

                dbContext.Devices.Add(new Device
                {
                    Name = device.Name,
                    Address = device.Address,
                    Description = device.Description,
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
        var dialog = await dialogService.ShowAsync<AddressDialog>("Enter Address");
        var result = await dialog.Result;
        if (result is null || result.Canceled)
        {
            return;
        }

        var device = (Shared.Device)result.Data!;
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            if (device is NetworkDevice && !device.Address.StartsWith("http"))
            {
                device.Address = "http://" + device.Address;
            }

            dbContext.Devices.Add(new Device()
            {
                Name = device.Name,
                Address = device.Address,
                Description = device.Description,
                Actions = device.Actions.Select(a => new DeviceAction
                {
                    Action = a.Action,
                    Command = a.Command,
                    CommandArgs = a.CommandArgs,
                    CommandType = a.CommandType
                }).ToList()
            });
            await dbContext.SaveChangesAsync();

            snackbar.Add($"Device '{device.Name}' added.", Severity.Success);
            await _table.ReloadServerData();
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to add device: {ex.Message}", Severity.Error);
        }
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
            }
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

            existing.Id = model.Id;
            existing.Name = model.Name;
            existing.Address = model.Address;
            existing.Description = model.Description;
            existing.Actions = model.Actions.Select(a => new DeviceAction
            {
                Action = a.Action,
                Command = a.Command,
                CommandArgs = a.CommandArgs,
                CommandType = a.CommandType
            })
                .ToList();
            existing.Configurations = model.Configurations.Select(c => new DeviceConfiguration
            {
                Name = c.Name,
                Value = c.Value
            })
                .ToList()
        ;
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
        public bool Online { get; set; }
        public int UptimeSeconds { get; set; }
        public double Temperature { get; set; }
        public bool Loading { get; set; } = true;
    }
}