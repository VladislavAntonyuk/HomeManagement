using System.Text.Json;
using CommunityToolkit.Maui;
using HomeManagementClient.Pages;
using Microsoft.Extensions.Logging;
using Refit;

#if GTK
using Platform.Maui.Linux.Gtk4.Hosting;
using Platform.Maui.Linux.Gtk4.Essentials.Storage;
#endif

namespace HomeManagementClient;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
#if GTK
            .UseMauiAppLinuxGtk4<App>()
#else
            .UseMauiApp<App>()
            // The toolkit has no Linux GTK platform code, its XAML behaviors and converters work without it
            .UseMauiCommunityToolkit(options => options.SetShouldEnableSnackbarOnWindows(true))
#endif
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

#if GTK
        builder.Services.AddSingleton<IPreferences>(new LinuxPreferences());
        builder.Services.AddSingleton<ISecureStorage>(new LinuxSecureStorage());
#else
        builder.Services.AddSingleton(Preferences.Default);
        builder.Services.AddSingleton(SecureStorage.Default);
#endif
        builder.Services.AddSingleton<HubSettings>();
        builder.Services.AddSingleton<UserNotifier>();
        builder.Services.AddHubApi();

        // Tab pages get their routes from AppShell, pushed pages register theirs
        builder.Services.AddTransient<DashboardPage, DashboardViewModel>();
        builder.Services.AddTransient<DevicesPage, DevicesViewModel>();
        builder.Services.AddTransient<WorkflowsPage, WorkflowsViewModel>();
        builder.Services.AddTransient<CamerasPage, CamerasViewModel>();
        builder.Services.AddTransientWithShellRoute<CameraPage, CameraViewModel>(Routes.Camera);
        builder.Services.AddTransient<SettingsPage, SettingsViewModel>();

        return builder.Build();
    }

    private static void AddHubApi(this IServiceCollection services)
    {
        var refitSettings = new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = ApiJsonContext.Default
            })
        };

        // The real address is set per request by HubConnectionHandler from the settings
        var placeholderAddress = new Uri("http://home-management.local");

        services.AddTransient<HubConnectionHandler>();
        services.AddRefitGeneratedClient<IHomeManagementApi>(refitSettings)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = placeholderAddress;
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<HubConnectionHandler>();

        services.AddRefitGeneratedClient<ICameraStreamApi>(refitSettings)
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = placeholderAddress;
                // A live stream lasts until the page closes it
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<HubConnectionHandler>();
    }
}
