using HomeManagement.Infrastructure.IpCameras;
using LiveStreamingServerNet;
using LiveStreamingServerNet.Flv.Installer;
using System.Net;
using System.Threading.Channels;

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
            options => FlvInstaller.AddFlv(options
                    .AddAuthorizationHandler<StreamAuthorizationHandler>())
        );

        builder.Services.AddSingleton<FrigateEventsSubscriber>();

        builder.Services.AddSingleton(_ => Channel.CreateBounded<string>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        }));
    }
}