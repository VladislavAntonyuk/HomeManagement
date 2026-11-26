using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeManagement.Shared;
using HomeManagement.Shared.Api;
using HomeManagement.Client.Services;

namespace HomeManagement.Client.ViewModels;

public sealed partial class DashboardViewModel(IHomeManagementApi api, UserNotifier notifier, HubSettings settings)
    : RefreshableViewModel(notifier, settings)
{
    [ObservableProperty]
    public partial DashboardDto? Dashboard { get; set; }

    /// <summary>
    /// Device action outputs pinned to the home screen on the hub web page.
    /// </summary>
    public ObservableCollection<DashboardWidgetItemViewModel> Widgets { get; } = [];

    public string HubUptime => Dashboard is null ? "-" : DisplayFormatter.FormatDuration(TimeSpan.FromSeconds(Dashboard.Hub.UptimeSeconds));

    public string LastWorkflowRun => Format(AppResources.DashboardLastRun, DisplayFormatter.FormatAgo(Dashboard?.LastWorkflowRunAtUtc, DateTimeOffset.UtcNow));

    public string DevicesOnlineOf => Format(AppResources.DevicesOnlineOf, Dashboard?.TotalDevices);

    public string DevicesOffline => Format(AppResources.DevicesOfflineCount, Dashboard?.OfflineDevices);

    public string DevicesNotResponding => Format(AppResources.DevicesNotRespondingCount, Dashboard?.NotRespondingDevices);

    public string WorkflowsEnabledOf => Format(AppResources.WorkflowsEnabledOf, Dashboard?.TotalWorkflows);

    public string HubTemperature => Dashboard?.Hub.TemperatureCelsius is { } celsius ? DisplayFormatter.FormatTemperature(celsius) : "-";

    public double OnlineShare => Dashboard is { TotalDevices: > 0 } d ? (double)d.OnlineDevices / d.TotalDevices : 0;

    protected override async Task LoadAsync(CancellationToken token)
    {
        var dashboard = api.GetDashboardAsync(token);
        var widgets = api.GetDashboardWidgetsAsync(token);
        Dashboard = await dashboard;
        UpdateWidgets(await widgets);
    }

    /// <summary>
    /// Updates the widgets in place, so a refresh does not rebuild the cards.
    /// </summary>
    private void UpdateWidgets(List<DashboardWidgetDto> widgets)
    {
        for (var i = Widgets.Count - 1; i >= 0; i--)
        {
            if (widgets.All(x => x.Id != Widgets[i].Id))
            {
                Widgets.RemoveAt(i);
            }
        }

        foreach (var (index, widget) in widgets.Index())
        {
            var existing = Widgets.FirstOrDefault(x => x.Id == widget.Id);
            if (existing is null)
            {
                Widgets.Insert(index, new DashboardWidgetItemViewModel(widget, api, Notifier, RefreshCommand));
                continue;
            }

            existing.Update(widget);
            var current = Widgets.IndexOf(existing);
            if (current != index)
            {
                Widgets.Move(current, index);
            }
        }
    }

    partial void OnDashboardChanged(DashboardDto? value)
    {
        OnPropertyChanged(nameof(HubUptime));
        OnPropertyChanged(nameof(LastWorkflowRun));
        OnPropertyChanged(nameof(OnlineShare));
        OnPropertyChanged(nameof(DevicesOnlineOf));
        OnPropertyChanged(nameof(DevicesOffline));
        OnPropertyChanged(nameof(DevicesNotResponding));
        OnPropertyChanged(nameof(WorkflowsEnabledOf));
        OnPropertyChanged(nameof(HubTemperature));
    }

    private static string Format(string format, object? value) => string.Format(CultureInfo.CurrentCulture, format, value);
}

public sealed partial class DashboardWidgetItemViewModel : ObservableObject
{
    private readonly IHomeManagementApi _api;
    private readonly UserNotifier _notifier;
    private readonly IAsyncRelayCommand _refresh;
    private DashboardWidgetDto _widget;

    public DashboardWidgetItemViewModel(DashboardWidgetDto widget, IHomeManagementApi api, UserNotifier notifier, IAsyncRelayCommand refresh)
    {
        _api = api;
        _notifier = notifier;
        _refresh = refresh;
        _widget = widget;
        Update(widget);
    }

    public int Id => _widget.Id;

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Device { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial DevicePresenceStatus Status { get; private set; }

    /// <summary>
    /// The formatted value, or why there is none.
    /// </summary>
    [ObservableProperty]
    public partial string Text { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasValue { get; private set; }

    [ObservableProperty]
    public partial bool IsSingleValue { get; private set; }

    /// <summary>
    /// A JSON output shown as "key: value" lines, or an error.
    /// </summary>
    [ObservableProperty]
    public partial bool IsMultiLine { get; private set; }

    [ObservableProperty]
    public partial bool IsError { get; private set; }

    [ObservableProperty]
    public partial string? Updated { get; private set; }

    [ObservableProperty]
    public partial bool CanRun { get; private set; }

    public bool IsOnline => Status == DevicePresenceStatus.Online;

    public void Update(DashboardWidgetDto widget)
    {
        _widget = widget;
        Title = widget.Title;
        Status = widget.Status;
        Device = widget.Status == DevicePresenceStatus.Online ? widget.DeviceName : $"{widget.DeviceName} · {widget.Status.DisplayName.ToLower(CultureInfo.CurrentCulture)}";
        HasValue = widget.Message is null;
        Text = widget switch
        {
            { Message: { } message } => message,
            { Properties: [var single] } => FormatValue(single),
            { Properties.Count: > 0 } => string.Join('\n', widget.Properties.Select(x => $"{DisplayFormatter.FormatPropertyLabel(x.Path, x.Label)}: {FormatValue(x)}")),
            { IsError: true } => widget.Output ?? string.Empty,
            _ => DisplayFormatter.FormatActionResult(widget.Output ?? string.Empty)
        };
        IsSingleValue = HasValue && !widget.IsError && !Text.Contains('\n');
        IsMultiLine = HasValue && !IsSingleValue;
        IsError = widget.IsError;
        Updated = widget.UpdatedAtUtc is { } updatedAt ? string.Format(CultureInfo.CurrentCulture, AppResources.WidgetUpdated, DisplayFormatter.FormatAgo(updatedAt, DateTimeOffset.UtcNow)) : null;
        CanRun = widget.CommandType == CommandType.Post;
        OnPropertyChanged(nameof(IsOnline));
    }

    private static string FormatValue(DashboardWidgetPropertyDto property)
        => property.Value is { } value ? DisplayFormatter.FormatWidgetValue(value, property.Unit) : "–";

    [RelayCommand]
    private async Task RunAsync()
    {
        try
        {
            // The output shows in the widget after the refresh.
            await _api.RunDeviceActionAsync(_widget.DeviceId, _widget.ActionId);
        }
        catch (Exception e)
        {
            // The hub answers a failed action with a problem, e.g. 503 when the device is offline.
            await _notifier.ShowErrorAsync(e);
        }

        await _refresh.ExecuteAsync(null);
    }
}
