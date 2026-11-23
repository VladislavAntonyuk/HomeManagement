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
            return "Loading...";
        }

        var online = _devices.Count(x => deviceStateStore.GetPresence(x.DeviceId).IsOnline);
        return $"{online} of {_devices.Count} online";
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

    /// <summary>
    /// Devices name actions as they like, e.g. "start clip garden" or "RESTART"; show them all as a sentence.
    /// </summary>
    private static string FormatActionName(string action)
    {
        if (string.IsNullOrEmpty(action))
        {
            return action;
        }

        if (!action.Any(char.IsLower))
        {
            action = action.ToLowerInvariant();
        }

        return char.ToUpperInvariant(action[0]) + action[1..];
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
