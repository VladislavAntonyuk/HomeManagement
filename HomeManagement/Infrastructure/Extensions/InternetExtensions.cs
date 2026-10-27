using HomeManagement.Application.Router;

namespace HomeManagement.Infrastructure.Extensions;

public static class InternetExtensions
{
    public static void AddInternetWatchdog(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<InternetWatchdogOptions>(builder.Configuration.GetSection("InternetWatchdog"));
        builder.Services.Configure<RouterOptions>(builder.Configuration.GetSection("Router"));
        builder.Services.AddSingleton<IRouterController, AsusRouterController>();
        builder.Services.AddHostedService<InternetWatchdogService>();
    }
}
