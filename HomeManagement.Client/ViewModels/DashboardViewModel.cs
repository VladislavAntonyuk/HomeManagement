using CommunityToolkit.Mvvm.ComponentModel;
using HomeManagement.Shared.Api;
using HomeManagement.Client.Services;

namespace HomeManagement.Client.ViewModels;

public sealed partial class DashboardViewModel(IHomeManagementApi api, UserNotifier notifier, HubSettings settings)
    : RefreshableViewModel(notifier, settings)
{
    [ObservableProperty]
    public partial DashboardDto? Dashboard { get; set; }

    public string HubUptime => Dashboard is null ? "-" : DisplayFormatter.FormatDuration(TimeSpan.FromSeconds(Dashboard.Hub.UptimeSeconds));

    public string LastWorkflowRun => DisplayFormatter.FormatAgo(Dashboard?.LastWorkflowRunAtUtc, DateTimeOffset.UtcNow);

    public double OnlineShare => Dashboard is { TotalDevices: > 0 } d ? (double)d.OnlineDevices / d.TotalDevices : 0;

    protected override async Task LoadAsync(CancellationToken token)
    {
        var dashboard = await api.GetDashboardAsync(token);
        Dashboard = dashboard;
    }

    partial void OnDashboardChanged(DashboardDto? value)
    {
        OnPropertyChanged(nameof(HubUptime));
        OnPropertyChanged(nameof(LastWorkflowRun));
        OnPropertyChanged(nameof(OnlineShare));
    }
}
