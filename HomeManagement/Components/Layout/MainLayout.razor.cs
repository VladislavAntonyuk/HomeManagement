using HomeManagement.Infrastructure.Localization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;

namespace HomeManagement.Components.Layout;

public partial class MainLayout
{
    private bool _drawerOpen = true;
    private bool _isDarkMode = true;
    private bool _rightToLeft;
    private ErrorBoundary? _errorBoundary;
    private MudThemeProvider? _themeProvider;

    protected override void OnInitialized()
    {
        NavigationManager.LocationChanged += OnLocationChanged;
    }

    // MudThemeProvider only reports changes of the system color scheme. A change while the page was in the background
    // reached a paused or disconnected circuit and was lost, so the page kept the old theme, e.g. light on a dark phone.
    // A resumed circuit renders a new layout, so reading the current scheme here also catches up after the background.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _themeProvider is null)
        {
            return;
        }

        try
        {
            var isDarkMode = await _themeProvider.GetSystemDarkModeAsync();
            if (isDarkMode != _isDarkMode)
            {
                _isDarkMode = isDarkMode;
                StateHasChanged();
            }
        }
        catch (Exception e) when (e is JSDisconnectedException or TaskCanceledException)
        {
            // The connection dropped or the circuit paused; the next circuit reads the scheme again.
        }
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => _errorBoundary?.Recover();
    public void Dispose() => NavigationManager.LocationChanged -= OnLocationChanged;

    // The language is read once per connection, so the page reloads to show the new one.
    private void SetCulture(AppCulture culture)
    {
        var page = "/" + NavigationManager.ToBaseRelativePath(NavigationManager.Uri);
        NavigationManager.NavigateTo(
            $"culture/set?culture={Uri.EscapeDataString(culture.Culture.Name)}&redirectUri={Uri.EscapeDataString(page)}",
            forceLoad: true);
    }

    private void DrawerToggle()
    {
        _drawerOpen = !_drawerOpen;
    }
}