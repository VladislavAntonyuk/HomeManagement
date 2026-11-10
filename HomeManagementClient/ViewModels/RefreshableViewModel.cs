using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeManagementClient.Services;

namespace HomeManagementClient.ViewModels;

/// <summary>
/// Loads its data when the page appears and keeps it fresh while the page is visible.
/// </summary>
public abstract partial class RefreshableViewModel(UserNotifier notifier, HubSettings settings) : ObservableObject
{
    private CancellationTokenSource? _visibleCts;
    private IDispatcherTimer? _timer;

    protected UserNotifier Notifier { get; } = notifier;

    /// <summary>
    /// How often the data is reloaded while visible, or null to load only on appear and pull to refresh.
    /// </summary>
    protected virtual TimeSpan? AutoRefreshInterval => TimeSpan.FromSeconds(10);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    /// <summary>
    /// True until the first load finished, to show a loading indicator instead of empty content.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial bool IsConfigured { get; set; } = true;

    [RelayCommand]
    private async Task AppearingAsync()
    {
        _visibleCts?.Cancel();
        _visibleCts = new CancellationTokenSource();
        await LoadSafeAsync(_visibleCts.Token);

        if (AutoRefreshInterval is { } interval && _timer is null)
        {
            _timer = Application.Current!.Dispatcher.CreateTimer();
            _timer.Interval = interval;
            _timer.Tick += async (_, _) =>
            {
                if (_visibleCts is { IsCancellationRequested: false } cts && !IsRefreshing)
                {
                    await LoadSafeAsync(cts.Token);
                }
            };
        }

        _timer?.Start();
    }

    [RelayCommand]
    private void Disappearing()
    {
        _timer?.Stop();
        _visibleCts?.Cancel();
        _visibleCts = null;
        OnHidden();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await LoadSafeAsync(_visibleCts?.Token ?? CancellationToken.None);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private Task OpenSettingsAsync() => GoToAsync($"//{Routes.Settings}");

    [RelayCommand]
    private Task GoToAsync(string route) => Shell.Current.GoToAsync(route);

    protected abstract Task LoadAsync(CancellationToken token);

    protected virtual void OnHidden()
    {
    }

    private async Task LoadSafeAsync(CancellationToken token)
    {
        try
        {
            await settings.LoadAsync();
            IsConfigured = settings.IsConfigured;
            if (!IsConfigured)
            {
                return;
            }

            await LoadAsync(token);
            ErrorMessage = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Page was hidden
        }
        catch (Exception e)
        {
            // A failed refresh keeps the last data and shows the error above it
            ErrorMessage = ApiErrors.ToMessage(e);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
