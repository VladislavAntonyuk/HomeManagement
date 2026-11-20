using HomeManagement.Application.DeviceManagement;
using HomeManagement.Components.Dialogs;
using HomeManagement.Infrastructure.Database.Entities;
using HomeManagement.Shared.Api;
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

    private static readonly DialogOptions EditorOptions = new()
    {
        BackdropClick = false,
        FullWidth = true,
        MaxWidth = MaxWidth.Medium,
        Position = DialogPosition.Center
    };

    private readonly HashSet<int> _runningActions = [];
    private readonly HashSet<int> _expandedDevices = [];
    private List<Device>? _devices;
    private ITimer? _refreshTimer;

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    protected override async Task OnInitializedAsync()
    {
        deviceStateStore.StatusChanged += OnStateChanged;
        deviceStateStore.TelemetryChanged += OnStateChanged;
        deviceStateStore.DevicesChanged += OnDevicesChanged;
        _refreshTimer = timeProvider.CreateTimer(_ => OnStateChanged(), null, RefreshInterval, RefreshInterval);
        await ReloadAsync();
    }

    private async Task ReloadAsync() => _devices = await deviceService.ListAsync();

    private void ToggleDetails(Device device)
    {
        if (!_expandedDevices.Remove(device.Id))
        {
            _expandedDevices.Add(device.Id);
        }
    }

    private static Color GetStatusColor(DevicePresence presence) => presence.Status switch
    {
        DevicePresenceStatus.NotResponding => Color.Warning,
        DevicePresenceStatus.Online => Color.Success,
        DevicePresenceStatus.Offline => Color.Error,
        _ => Color.Default
    };

    private static string GetStatusTooltip(DevicePresence presence)
    {
        if (presence.LastSeenUtc is not { } lastSeen)
        {
            return "The device has not reported its status yet.";
        }

        var seen = lastSeen.ToLocalTime().ToString("G");
        return presence.Status switch
        {
            DevicePresenceStatus.NotResponding => $"The device reports online, but sent nothing since {seen}. It probably lost power or network.",
            DevicePresenceStatus.Online => $"Last message {seen}. Expected at least every {DisplayFormatter.FormatDuration(presence.HeartbeatInterval)}.",
            DevicePresenceStatus.Offline => $"The device went offline. Last message {seen}.",
            _ => $"Unknown status. Last message {seen}."
        };
    }

    private void OnStateChanged() => _ = InvokeAsync(StateHasChanged);

    private void OnDevicesChanged() => _ = InvokeAsync(async () =>
    {
        await ReloadAsync();
        StateHasChanged();
    });

    private async Task RunActionAsync(Device device, DeviceAction action)
    {
        if (!_runningActions.Add(action.Id))
        {
            return;
        }

        try
        {
            var result = await deviceService.RunActionAsync(device.Id, action.Id);
            snackbar.Add(result with { Message = DisplayFormatter.FormatActionResult(result.Message) }, requireInteraction: true);
        }
        finally
        {
            _runningActions.Remove(action.Id);
        }
    }

    private async Task EditAsync(Device device)
    {
        var parameters = new DialogParameters<DeviceEditDialog>
        {
            { x => x.Model, new DeviceEditModel { Name = device.Name, Description = device.Description } }
        };
        var dialog = await dialogService.ShowAsync<DeviceEditDialog>($"Edit {device.Name}", parameters, EditorOptions);
        if (await dialog.Result is not { Canceled: false, Data: DeviceEditModel model })
        {
            return;
        }

        snackbar.Add(await deviceService.UpdateAsync(device.Id, model.Name, model.Description));
        await ReloadAsync();
    }

    private async Task DeleteAsync(Device device)
    {
        var usages = await deviceService.GetWorkflowUsagesAsync(device.Id);
        if (usages.Count > 0)
        {
            snackbar.Add($"'{device.Name}' is used by workflows: {string.Join(", ", usages)}. Remove it from those workflows first.", Severity.Warning);
            return;
        }

        var message = deviceStateStore.IsOnline(device.DeviceId)
            ? $"'{device.Name}' is online, so it registers again the next time it connects. Delete it anyway?"
            : $"Are you sure you want to delete '{device.Name}'?";
        if (await dialogService.ConfirmAsync("Delete device", message))
        {
            snackbar.Add(await deviceService.DeleteAsync(device.Id), Severity.Warning);
            await ReloadAsync();
        }
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
        deviceStateStore.StatusChanged -= OnStateChanged;
        deviceStateStore.TelemetryChanged -= OnStateChanged;
        deviceStateStore.DevicesChanged -= OnDevicesChanged;
    }
}
