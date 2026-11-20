using HomeManagement.Cameras;
using HomeManagement.DeviceSdk;

namespace HomeManagement.Infrastructure.Extensions;

public static class LiveCameraExtensions
{
    public static void AddLiveCamera(this WebApplicationBuilder builder)
    {
        builder.Services.AddServiceDiscovery();

        builder.Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
            .AddServiceDiscoveryDestinationResolver();

        // The camera module is a device on the broker: the hub reaches Frigate only through it, like any other device.
        builder.Services.AddCameras(options =>
        {
            options.StreamKey = builder.Configuration["Auth:StreamKey"] ?? string.Empty;
            options.FrigatePublicUrl = $"http://{AppHosts.CamerasHost}";
            builder.Configuration.GetSection("Cameras").Bind(options);
        });
    }
}
