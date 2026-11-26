using HomeManagement.Application.Dashboard;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Components.Dialogs;
using HomeManagement.Infrastructure.Database.Entities;
using HomeManagement.Shared.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using CommandType = HomeManagement.Shared.CommandType;

namespace HomeManagement.Components.Shared;

/// <summary>
/// Device action outputs pinned to the home screen. Values update as devices report them.
/// </summary>
public partial class DeviceWidgets(
    DashboardWidgetService widgetService,
    DeviceService deviceService,
    DeviceStateStore deviceStateStore,
    IDialogService dialogService,
    ISnackbar snackbar,
    TimeProvider timeProvider) : ComponentBase, IDisposable
{
    // Keeps "updated ... ago" current between device reports.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private static readonly DialogOptions EditorOptions = new()
    {
        BackdropClick = false,
        FullWidth = true,
        MaxWidth = MaxWidth.Small,
        Position = DialogPosition.Center
    };

    private readonly HashSet<int> _runningWidgets = [];
    private List<DashboardWidget>? _widgets;
    private ITimer? _refreshTimer;

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    protected override async Task OnInitializedAsync()
    {
        deviceStateStore.StatusChanged += OnStateChanged;
        deviceStateStore.TelemetryChanged += OnStateChanged;
        deviceStateStore.CommandOutputChanged += OnStateChanged;
        deviceStateStore.DevicesChanged += OnWidgetsChanged;
        widgetService.WidgetsChanged += OnWidgetsChanged;
        _refreshTimer = timeProvider.CreateTimer(_ => OnStateChanged(), null, RefreshInterval, RefreshInterval);
        await ReloadAsync();
    }

    private async Task ReloadAsync() => _widgets = await widgetService.ListAsync();

    private async Task AddAsync()
    {
        var devices = await deviceService.ListAsync();
        if (devices.Count == 0)
        {
            snackbar.Add(Strings.Widgets_NoDevices, Severity.Info);
            return;
        }

        if (await ShowEditorAsync(Strings.Widgets_Add, new DashboardWidgetEditModel(), devices) is { } model)
        {
            snackbar.Add(await widgetService.AddAsync(model.ToEntity()));
        }
    }

    private async Task EditAsync(DashboardWidget widget)
    {
        var devices = await deviceService.ListAsync();
        if (await ShowEditorAsync(string.Format(Strings.Common_EditTitle, DashboardWidgetService.GetTitle(widget)), DashboardWidgetEditModel.From(widget), devices) is { } model)
        {
            snackbar.Add(await widgetService.UpdateAsync(widget.Id, model.ToEntity()));
        }
    }

    private async Task<DashboardWidgetEditModel?> ShowEditorAsync(string title, DashboardWidgetEditModel model, List<Device> devices)
    {
        var parameters = new DialogParameters<DashboardWidgetEditDialog>
        {
            { x => x.Model, model },
            { x => x.Devices, devices }
        };
        var dialog = await dialogService.ShowAsync<DashboardWidgetEditDialog>(title, parameters, EditorOptions);
        return await dialog.Result is { Canceled: false, Data: DashboardWidgetEditModel result } ? result : null;
    }

    private async Task DeleteAsync(DashboardWidget widget)
    {
        if (await dialogService.ConfirmAsync(Strings.Widgets_RemoveTitle, string.Format(Strings.Widgets_RemoveConfirm, DashboardWidgetService.GetTitle(widget)), Strings.Common_Remove))
        {
            snackbar.Add(await widgetService.DeleteAsync(widget.Id), Severity.Warning);
        }
    }

    private async Task MoveAsync(DashboardWidget widget, int offset)
    {
        var result = await widgetService.MoveAsync(widget.Id, offset);
        if (!result.IsSuccess)
        {
            snackbar.Add(result);
        }
    }

    private async Task RunAsync(DashboardWidget widget)
    {
        if (!_runningWidgets.Add(widget.Id))
        {
            return;
        }

        try
        {
            // The output shows in the widget, so only a failure needs a message.
            var result = await deviceService.RunActionAsync(widget.DeviceAction.DeviceId, widget.DeviceActionId);
            if (!result.IsSuccess)
            {
                snackbar.Add(result);
            }
        }
        finally
        {
            _runningWidgets.Remove(widget.Id);
        }
    }

    private sealed record WidgetLine(string? Label, string Value, bool IsMissing = false);

    /// <summary>
    /// What the widget body shows: one value without a label, or labelled values.
    /// A single chosen value is shown large; several are shown as a list, like a JSON output.
    /// </summary>
    private static List<WidgetLine> Lines(DashboardWidgetValue value)
    {
        if (value.Properties is [var single])
        {
            return [new WidgetLine(null, single.Value is { } raw ? DisplayFormatter.FormatWidgetValue(raw, single.Property.Unit) : "–")];
        }

        if (value.Properties.Count > 0)
        {
            return
            [
                .. value.Properties.Select(x => new WidgetLine(
                    DisplayFormatter.FormatPropertyLabel(x.Property.Path, x.Property.Label),
                    x.Value is { } raw ? DisplayFormatter.FormatWidgetValue(raw, x.Property.Unit) : "–",
                    x.Value is null))
            ];
        }

        if (value.Output is not { } output)
        {
            return [];
        }

        return
        [
            .. DisplayFormatter.FormatActionResult(output).Split('\n').Select(line =>
            {
                var separator = line.IndexOf(": ", StringComparison.Ordinal);
                return separator > 0 ? new WidgetLine(line[..separator], line[(separator + 2)..]) : new WidgetLine(null, line);
            })
        ];
    }

    private static string CommandTypeIcon(CommandType type) => type switch
    {
        CommandType.Get => Icons.Material.Rounded.Sensors,
        CommandType.Event => Icons.Material.Rounded.NotificationsActive,
        _ => Icons.Material.Rounded.Terminal
    };

    private static Color CommandTypeAccent(CommandType type) => type switch
    {
        CommandType.Get => Color.Info,
        CommandType.Event => Color.Warning,
        _ => Color.Primary
    };

    private static string StatusCssColor(DevicePresenceStatus status) => status switch
    {
        DevicePresenceStatus.NotResponding => "var(--mud-palette-warning)",
        DevicePresenceStatus.Online => "var(--mud-palette-success)",
        DevicePresenceStatus.Offline => "var(--mud-palette-error)",
        _ => "var(--mud-palette-text-disabled)"
    };

    private void OnStateChanged() => _ = InvokeAsync(StateHasChanged);

    private void OnWidgetsChanged() => _ = InvokeAsync(async () =>
    {
        await ReloadAsync();
        StateHasChanged();
    });

    public void Dispose()
    {
        _refreshTimer?.Dispose();
        deviceStateStore.StatusChanged -= OnStateChanged;
        deviceStateStore.TelemetryChanged -= OnStateChanged;
        deviceStateStore.CommandOutputChanged -= OnStateChanged;
        deviceStateStore.DevicesChanged -= OnWidgetsChanged;
        widgetService.WidgetsChanged -= OnWidgetsChanged;
    }
}
