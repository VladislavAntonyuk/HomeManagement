namespace HomeManagement.Client;

public partial class App : Application
{
    public App(AppLanguage language)
    {
        // Before any page is created, so every text comes in the picked language.
        language.Apply();
        language.Changed += (_, _) => Dispatcher.Dispatch(ReloadPages);
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(new AppShell()) { Title = AppResources.AppTitle };

    /// <summary>
    /// Rebuilds the shell, so all texts follow a language change without a restart.
    /// </summary>
    private void ReloadPages()
    {
        foreach (var window in Windows)
        {
            var shell = new AppShell();
            window.Page = shell;
            window.Title = AppResources.AppTitle;
            _ = shell.GoToAsync($"//{Routes.Settings}");
        }
    }
}
