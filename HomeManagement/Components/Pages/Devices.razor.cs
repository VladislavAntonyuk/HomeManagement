using HomeManagement.Application.Dashboard;
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
    DashboardWidgetService widgetService,
    TimeProvider timeProvider) : ComponentBase, IDisposable
{
    // Keeps "seen ... ago" and the device info current between status changes.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private static readonly DialogOptions EditorOptions = new()
    {
        BackdropClick = false,
        FullWidth = true,
        MaxWidth = MaxWidth.Small,
        Position = DialogPosition.Center
    };

    private readonly HashSet<int> _runningActions = [];
    // Devices such as Frigate register many actions; only the first ones are shown until the user asks for all.
    private const int CollapsedActionCount = 6;

    // Actions most devices share come first, so they sit in the same place on every card.
    private static readonly string[] CommonActions = ["info", "restart", "shutdown"];

    private readonly HashSet<int> _expandedDevices = [];
    private readonly HashSet<int> _expandedActions = [];
    private List<Device>? _devices;
    private string? _search;
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

    private IEnumerable<Device> FilteredDevices()
    {
        var devices = _devices ?? [];
        var filtered = string.IsNullOrWhiteSpace(_search)
            ? devices
            : devices.Where(x => x.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
                                 || x.DeviceId.Contains(_search, StringComparison.OrdinalIgnoreCase));
        return filtered.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private string Summary()
    {
        if (_devices is null)
        {
            return Strings.Common_Loading;
        }

        var online = _devices.Count(x => deviceStateStore.GetPresence(x.DeviceId).IsOnline);
        return string.Format(Strings.Common_OnlineOf, online, _devices.Count);
    }

    private static List<DeviceAction> OrderActions(IEnumerable<DeviceAction> actions) =>
    [
        .. actions
            .OrderBy(x => CommonActionRank(x.Action))
            .ThenBy(x => x.Action, StringComparer.CurrentCultureIgnoreCase)
    ];

    /// <summary>
    /// The position of a common action, or a rank after all of them for any other action.
    /// </summary>
    private static int CommonActionRank(string action)
    {
        var index = Array.FindIndex(CommonActions, x => string.Equals(x, action.Trim(), StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : CommonActions.Length;
    }

    private static void Toggle(HashSet<int> expanded, Device device)
    {
        if (!expanded.Remove(device.Id))
        {
            expanded.Add(device.Id);
        }
    }

    private static string GetStatusCssColor(DevicePresence presence) => presence.Status switch
    {
        DevicePresenceStatus.NotResponding => "var(--mud-palette-warning)",
        DevicePresenceStatus.Online => "var(--mud-palette-success)",
        DevicePresenceStatus.Offline => "var(--mud-palette-error)",
        _ => "var(--mud-palette-text-disabled)"
    };

    private static string GetStatusTooltip(DevicePresence presence)
    {
        if (presence.LastSeenUtc is not { } lastSeen)
        {
            return Strings.Devices_StatusNotReported;
        }

        var seen = lastSeen.ToLocalTime().ToString("G");
        return presence.Status switch
        {
            DevicePresenceStatus.NotResponding => string.Format(Strings.Devices_StatusNotResponding, seen),
            DevicePresenceStatus.Online => string.Format(Strings.Devices_StatusOnline, seen, DisplayFormatter.FormatDuration(presence.HeartbeatInterval)),
            DevicePresenceStatus.Offline => string.Format(Strings.Devices_StatusOffline, seen),
            _ => string.Format(Strings.Devices_StatusUnknown, seen)
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
        var dialog = await dialogService.ShowAsync<DeviceEditDialog>(string.Format(Strings.Common_EditTitle, device.Name), parameters, EditorOptions);
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
            snackbar.Add(string.Format(Strings.Device_UsedByWorkflows, device.Name, string.Join(", ", usages)), Severity.Warning);
            return;
        }

        var message = deviceStateStore.IsOnline(device.DeviceId)
            ? string.Format(Strings.Devices_DeleteOnline, device.Name)
            : string.Format(Strings.Devices_DeleteConfirm, device.Name);
        if (await widgetService.CountForDeviceAsync(device.Id) is var widgets and > 0)
        {
            message += widgets == 1 ? Strings.Devices_DeleteWidget : string.Format(Strings.Devices_DeleteWidgets, widgets);
        }

        if (await dialogService.ConfirmAsync(Strings.Devices_DeleteTitle, message))
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
