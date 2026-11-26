using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeManagement.Client.Services;

namespace HomeManagement.Client.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly HubSettings _settings;
    private readonly IHomeManagementApi _api;
    private readonly AppLanguage _language;

    public SettingsViewModel(HubSettings settings, IHomeManagementApi api, AppLanguage language)
    {
        _settings = settings;
        _api = api;
        _language = language;
        Languages =
        [
            new(AppLanguage.SystemDefault, AppResources.LanguageSystem),
            new(AppLanguage.English, "English"),
            new(AppLanguage.Ukrainian, "Українська")
        ];
        SelectedLanguage = Languages.First(x => x.Code == language.Code);
    }

    /// <summary>
    /// The languages to pick from. Each language is named in itself.
    /// </summary>
    public LanguageOption[] Languages { get; }

    /// <summary>
    /// Changing it applies the language and rebuilds the pages.
    /// </summary>
    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? HubUrl { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? ApiKey { get; set; }

    [ObservableProperty]
    public partial string? ConnectionStatus { get; private set; }

    [ObservableProperty]
    public partial bool IsConnected { get; private set; }

    public string AppVersion => $"{AppInfo.Current.Name} {AppInfo.Current.VersionString}";

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await _settings.LoadAsync();
        HubUrl ??= _settings.HubUri?.ToString() ?? HubSettings.DefaultHubUrl;
        ApiKey ??= _settings.ApiKey;
    }

    partial void OnSelectedLanguageChanged(LanguageOption value) => _language.Set(value.Code);

    private bool CanSave() => HubSettings.TryParseHubUrl(HubUrl, out _) && !string.IsNullOrWhiteSpace(ApiKey);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!HubSettings.TryParseHubUrl(HubUrl, out var hubUri) || string.IsNullOrWhiteSpace(ApiKey))
        {
            return;
        }

        await _settings.SaveAsync(hubUri, ApiKey.Trim());
        HubUrl = hubUri.ToString();
        ConnectionStatus = AppResources.Connecting;
        IsConnected = false;
        try
        {
            var dashboard = await _api.GetDashboardAsync();
            IsConnected = true;
            ConnectionStatus = string.Format(CultureInfo.CurrentCulture, AppResources.ConnectedSummary, dashboard.Hub.MachineName, dashboard.TotalDevices, dashboard.TotalWorkflows);
        }
        catch (Exception e)
        {
            ConnectionStatus = ApiErrors.ToMessage(e);
        }
    }
}

/// <summary>
/// A language the app can be shown in. An empty code follows the system language.
/// </summary>
public sealed record LanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}
