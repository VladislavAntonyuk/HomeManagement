using CommunityToolkit.Mvvm.ComponentModel;
using HomeManagement.Shared.Api;
using HomeManagementClient.Services;

namespace HomeManagementClient.ViewModels;

public enum DashboardTileKind
{
    Devices,
    Workflows,
    Hub
}

public sealed record DashboardTile(DashboardTileKind Kind, DashboardViewModel Dashboard);

public sealed partial class DashboardViewModel(IHomeManagementApi api, UserNotifier notifier, HubSettings settings)
    : RefreshableViewModel(notifier, settings)
{
    private bool _isApplyingServerState;

    [ObservableProperty]
    public partial IReadOnlyList<DashboardTile> Tiles { get; private set; } = [];

    [ObservableProperty]
    public partial DashboardDto? Dashboard { get; set; }

    [ObservableProperty]
    public partial bool AwayModeEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsUpdatingAwayMode { get; set; }

    public string HubUptime => Dashboard is null ? "-" : DisplayFormatter.FormatDuration(TimeSpan.FromSeconds(Dashboard.Hub.UptimeSeconds));

    public string HubTemperature => Dashboard?.Hub.TemperatureCelsius is { } celsius ? $"{celsius:0.#} °C" : "-";

    public string LastWorkflowRun => DisplayFormatter.FormatAgo(Dashboard?.LastWorkflowRunAtUtc);

    public double OnlineShare => Dashboard is { TotalDevices: > 0 } d ? (double)d.OnlineDevices / d.TotalDevices : 0;

    protected override async Task LoadAsync(CancellationToken token)
    {
        var dashboard = await api.GetDashboardAsync(token);
        Dashboard = dashboard;
        if (Tiles.Count == 0)
        {
            Tiles = Enum.GetValues<DashboardTileKind>().Select(x => new DashboardTile(x, this)).ToList();
        }

        if (!IsUpdatingAwayMode)
        {
            _isApplyingServerState = true;
            AwayModeEnabled = dashboard.AwayModeEnabled;
            _isApplyingServerState = false;
        }
    }

    partial void OnDashboardChanged(DashboardDto? value)
    {
        OnPropertyChanged(nameof(HubUptime));
        OnPropertyChanged(nameof(HubTemperature));
        OnPropertyChanged(nameof(LastWorkflowRun));
        OnPropertyChanged(nameof(OnlineShare));
    }

    async partial void OnAwayModeEnabledChanged(bool value)
    {
        if (_isApplyingServerState)
        {
            return;
        }

        IsUpdatingAwayMode = true;
        try
        {
            var result = await api.SetAwayModeAsync(new SetEnabledRequest(value));
            await Notifier.ShowAsync(result.Message, !result.IsSuccess);
        }
        catch (Exception e)
        {
            _isApplyingServerState = true;
            AwayModeEnabled = !value;
            _isApplyingServerState = false;
            await Notifier.ShowErrorAsync(e);
        }
        finally
        {
            IsUpdatingAwayMode = false;
        }
    }
}
