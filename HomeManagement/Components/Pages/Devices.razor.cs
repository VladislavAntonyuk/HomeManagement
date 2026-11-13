using HomeManagement.Application.DeviceManagement;
using HomeManagement.Components.Dialogs;
using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HomeManagement.Components.Pages;

public partial class Devices(
    ISnackbar snackbar,
    IDialogService dialogService,
    DeviceService deviceService,
    DeviceStateStore deviceStateStore,
    TimeProvider timeProvider) : ComponentBase, IDisposable
{
    // Keeps "seen ... ago" and the device info current between status changes.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private MudTable<Device> _table = null!;
    private readonly HashSet<(string DeviceName, DeviceAction Action)> _runningActions = new();
    private readonly HashSet<string> _expandedDevices = new(StringComparer.Ordinal);
    private ITimer? _refreshTimer;

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    protected override void OnInitialized()
    {
        deviceStateStore.StatusChanged += OnStatusChanged;
        deviceStateStore.DevicesChanged += OnDevicesChanged;
        _refreshTimer = timeProvider.CreateTimer(_ => OnStatusChanged(), null, RefreshInterval, RefreshInterval);
    }

    private async Task<TableData<Device>> ServerReload(TableState state, CancellationToken token)
    {
        var devices = (await deviceService.GetDevicesAsync(token)).Select(x => x.Device);
        Func<Device, string> keySelector = state.SortLabel switch
        {
            nameof(Device.DeviceId) => x => x.DeviceId,
            _ => x => x.Name
        };
        var sorted = (state.SortDirection == SortDirection.Descending
                ? devices.OrderByDescending(keySelector, StringComparer.CurrentCultureIgnoreCase)
                : devices.OrderBy(keySelector, StringComparer.CurrentCultureIgnoreCase))
            .ToList();

        return new TableData<Device>
        {
            TotalItems = sorted.Count,
            Items = sorted.Skip(state.Page * state.PageSize).Take(state.PageSize).ToList()
        };
    }

    private bool? IsOnline(Device device) => deviceStateStore.IsOnline(device.DeviceId);

    private void ToggleDetails(Device device)
    {
        if (!_expandedDevices.Remove(device.DeviceId))
        {
            _expandedDevices.Add(device.DeviceId);
        }
    }

    private static Color GetStatusColor(DevicePresence? presence) => presence switch
    {
        { IsHeartbeatMissed: true } => Color.Warning,
        { IsOnline: true } => Color.Success,
        { IsOnline: false } => Color.Error,
        _ => Color.Default
    };

    private static string GetStatusTooltip(DevicePresence? presence)
    {
        if (presence?.LastSeenUtc is not { } lastSeen)
        {
            return "The device has not reported its status yet.";
        }

        var seen = lastSeen.ToLocalTime().ToString("G");
        return presence switch
        {
            { IsHeartbeatMissed: true } => $"The device reports online, but sent nothing since {seen}. It probably lost power or network.",
            { IsOnline: true } => $"Last message {seen}. Expected at least every {DeviceInfoFormatter.FormatDuration(presence.HeartbeatInterval)}.",
            { IsOnline: false } => $"The device went offline. Last message {seen}.",
            _ => $"Unknown status. Last message {seen}."
        };
    }

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
            var result = await deviceService.RunActionAsync(device.Id, action.Id);
            var message = Shared.DeviceActionResultFormatter.Format(result.Message);
            snackbar.Add(message,
                result.IsSuccess ? Severity.Success : Severity.Error,
                options => options.RequireInteraction = true);
        }
        finally
        {
            _runningActions.Remove(key);
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
            var updated = await deviceService.UpdateAsync(device.Id, model.Name, model.Description);
            snackbar.Add(updated.Message, updated.IsSuccess ? Severity.Success : Severity.Error);
            await _table.ReloadServerData();
        }
        catch (Exception ex)
        {
            snackbar.Add($"Failed to update device: {ex.InnerException?.Message ?? ex.Message}", Severity.Error);
        }
    }

    private async Task Delete(Device device)
    {
        var usages = await deviceService.GetWorkflowUsagesAsync(device.Id);
        if (usages.Count > 0)
        {
            snackbar.Add($"'{device.Name}' is used by workflows: {string.Join(", ", usages)}. Remove it from those workflows first.", Severity.Warning);
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
            var deleted = await deviceService.DeleteAsync(device.Id);
            snackbar.Add(deleted.Message, deleted.IsSuccess ? Severity.Success : Severity.Warning);
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
        _refreshTimer?.Dispose();
        deviceStateStore.StatusChanged -= OnStatusChanged;
        deviceStateStore.DevicesChanged -= OnDevicesChanged;
    }
}
