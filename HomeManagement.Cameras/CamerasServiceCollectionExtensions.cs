using System.Net;
using HomeManagement.Cameras.Frigate;
using HomeManagement.Cameras.Streaming;
using HomeManagement.DeviceSdk;
using LiveStreamingServerNet;
using LiveStreamingServerNet.Flv.Installer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Silverback.Configuration;
using Silverback.Messaging.Messages;

namespace HomeManagement.Cameras;

public static class CamerasServiceCollectionExtensions
{
    /// <summary>
    /// Adds the camera module: the RTMP server cameras publish to, and Frigate exposed as a device over MQTT.
    /// It talks to the rest of the application only through the device protocol on the broker.
    /// The host must add Silverback with MQTT support.
    /// </summary>
    public static IServiceCollection AddCameras(this IServiceCollection services, Action<CamerasOptions> configure)
    {
        services.AddOptions<CamerasOptions>()
            .Configure(configure)
            .Validate(x => !string.IsNullOrEmpty(x.StreamKey), "Cameras:StreamKey is required.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddServiceDiscovery();
        services.AddHttpClient<FrigateApiClient>((sp, client) => client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<CamerasOptions>>().Value.FrigateApiUrl))
            .AddServiceDiscovery();

        services.AddSingleton<FrigateConfigProvider>();
        services.AddSingleton<FrigateStreamSync>();
        // The RTMP server and the device agent need their settings while the services are registered.
        var rtmpOptions = new CamerasOptions();
        configure(rtmpOptions);
        services.AddLiveStreamingServer(
            new IPEndPoint(IPAddress.Any, rtmpOptions.RtmpPort),
            options => options
                .AddAuthorizationHandler<StreamAuthorizationHandler>()
                .AddStreamEventHandler(sp => sp.GetRequiredService<FrigateStreamSync>())
                .AddFlv());

        services.AddDeviceAgent<FrigateDeviceHandler>(device =>
        {
            device.DeviceName = rtmpOptions.Device.DeviceName;
            device.DeviceId = rtmpOptions.Device.DeviceId;
            device.TelemetryIntervalSeconds = rtmpOptions.Device.TelemetryIntervalSeconds;
        });

        services.ConfigureSilverback()
            .AddBrokerClientsConfigurator<FrigateBrokerClientsConfigurator>()
            .AddDelegateSubscriber((FrigateEventMessage message, FrigateDeviceHandler handler) => handler.OnEventAsync(message))
            .AddDelegateSubscriber((FrigateStatsMessage message, FrigateDeviceHandler handler) => handler.OnStats(message))
            .AddDelegateSubscriber((StringMessage<FrigateAvailabilityPayload> message, FrigateDeviceHandler handler) => handler.OnAvailabilityAsync(message))
            .AddDelegateSubscriber((StringMessage<FrigateAvailabilityPayload> message, FrigateConfigProvider config) => config.OnFrigateAvailability(message))
            .AddDelegateSubscriber((FrigateConfigLoaded message, FrigateDeviceHandler handler) => handler.OnConfigLoaded(message.Config))
            .AddDelegateSubscriber((FrigateConfigLoaded _, FrigateStreamSync sync) => sync.OnConfigLoaded())
            .AddDelegateSubscriber((IInboundEnvelope<StringMessage<FrigateStatePayload>> envelope, FrigateDeviceHandler handler) => handler.OnStateAsync(envelope));

        return services;
    }

    /// <summary>
    /// Serves the camera streams over HTTP-FLV.
    /// </summary>
    public static IApplicationBuilder UseCameras(this IApplicationBuilder app) => app.UseHttpFlv();
}
