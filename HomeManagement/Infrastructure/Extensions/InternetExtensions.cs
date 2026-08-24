using HomeManagement.Application.Router;

namespace HomeManagement.Infrastructure.Extensions;

public static class InternetExtensions
{
    public static void AddInternetWatchdog(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<InternetWatchdogOptions>(builder.Configuration.GetSection("InternetWatchdog"));

        builder.Services.AddSingleton<IRouterController, AsusRouterController>((sp) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("AsusRouter");
            httpClient.BaseAddress = new Uri(configuration.GetValue("Router:IpAddress", string.Empty));
            var username = configuration.GetValue("Router:Username", string.Empty);
            var password = configuration.GetValue("Router:Password", string.Empty);
            return new AsusRouterController(httpClient, username, password);
        });

        builder.Services.AddHostedService<InternetWatchdogService>();
    }
}