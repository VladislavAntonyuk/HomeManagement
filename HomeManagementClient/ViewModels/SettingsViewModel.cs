using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeManagementClient.Services;

namespace HomeManagementClient.ViewModels;

public sealed partial class SettingsViewModel(HubSettings settings, IHomeManagementApi api) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? HubUrl { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? ApiKey { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionStatus))]
    public partial string? ConnectionStatus { get; private set; }

    [ObservableProperty]
    public partial bool IsConnected { get; private set; }

    public bool HasConnectionStatus => ConnectionStatus is not null;

    public string AppVersion => $"{AppInfo.Current.Name} {AppInfo.Current.VersionString}";

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await settings.LoadAsync();
        HubUrl ??= settings.HubUri?.ToString() ?? HubSettings.DefaultHubUrl;
        ApiKey ??= settings.ApiKey;
    }

    private bool CanSave() => HubSettings.TryParseHubUrl(HubUrl, out _) && !string.IsNullOrWhiteSpace(ApiKey);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!HubSettings.TryParseHubUrl(HubUrl, out var hubUri) || string.IsNullOrWhiteSpace(ApiKey))
        {
            return;
        }

        await settings.SaveAsync(hubUri, ApiKey.Trim());
        HubUrl = hubUri.ToString();
        ConnectionStatus = "Connecting...";
        IsConnected = false;
        try
        {
            var dashboard = await api.GetDashboardAsync();
            IsConnected = true;
            ConnectionStatus = $"Saved. Connected to {dashboard.Hub.MachineName}: {dashboard.TotalDevices} devices, {dashboard.TotalWorkflows} workflows.";
        }
        catch (Exception e)
        {
            ConnectionStatus = ApiErrors.ToMessage(e);
        }
    }
}
