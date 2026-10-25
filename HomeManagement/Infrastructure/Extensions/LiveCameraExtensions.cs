using System.Net;
using HomeManagement.Application.AwayMode;
using HomeManagement.Infrastructure.IpCameras;
using LiveStreamingServerNet;
using LiveStreamingServerNet.Flv.Installer;

namespace HomeManagement.Infrastructure.Extensions;

public static class LiveCameraExtensions
{
    public static void AddLiveCamera(this WebApplicationBuilder builder)
    {
        builder.Services.AddServiceDiscovery();

        builder.Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
            .AddServiceDiscoveryDestinationResolver();

        builder.Services.AddLiveStreamingServer(
            new IPEndPoint(IPAddress.Any, 1935),
            options => options
                .AddAuthorizationHandler<StreamAuthorizationHandler>().AddFlv()
        );

        builder.Services.AddScoped<FrigateEventsSubscriber>();
        builder.Services.AddScoped<AwayModeService>();
    }
}