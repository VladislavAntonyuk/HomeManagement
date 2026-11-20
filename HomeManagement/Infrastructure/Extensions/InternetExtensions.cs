using HomeManagement.Application.Router;

namespace HomeManagement.Infrastructure.Extensions;

public static class InternetExtensions
{
    public static void AddInternetWatchdog(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<InternetWatchdogOptions>(builder.Configuration.GetSection("InternetWatchdog"));
        builder.Services.Configure<RouterOptions>(builder.Configuration.GetSection("Router"));
        builder.Services.AddHttpClient<IRouterController, AsusRouterController>((sp, client) =>
        {
            // The router is optional: without an address its actions report that it is not configured.
            if (Uri.TryCreate(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RouterOptions>>().Value.IpAddress, UriKind.Absolute, out var address))
            {
                client.BaseAddress = address;
            }
            client.DefaultRequestHeaders.UserAgent.ParseAdd(AsusRouterController.UserAgent);
        });
        builder.Services.AddHostedService<InternetWatchdogService>();
    }
}
