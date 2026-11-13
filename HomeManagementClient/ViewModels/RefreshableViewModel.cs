using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeManagementClient.Services;

namespace HomeManagementClient.ViewModels;

public abstract partial class RefreshableViewModel(UserNotifier notifier, HubSettings settings) : ObservableObject
{
    private CancellationTokenSource? _visibleCts;
    private IDispatcherTimer? _timer;

    public UserNotifier Notifier { get; } = notifier;

#if GTK
    protected virtual TimeSpan? AutoRefreshInterval => null;
#else
    protected virtual TimeSpan? AutoRefreshInterval => TimeSpan.FromSeconds(10);
#endif

    [ObservableProperty]
    public partial int LoadVersion { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

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
        }
        catch (Exception e)
        {
            ErrorMessage = ApiErrors.ToMessage(e);
        }
        finally
        {
            IsLoading = false;
            LoadVersion++;
        }
    }
}
