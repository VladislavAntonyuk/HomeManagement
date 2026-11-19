using HomeManagement.DeviceSdk.Mqtt;
using Microsoft.Extensions.DependencyInjection;
using Silverback.Configuration;
using Silverback.Messaging.Messages;

namespace HomeManagement.DeviceSdk;

public static class DeviceAgentServiceCollectionExtensions
{
    /// <summary>
    /// Runs a device that speaks the Home Management device protocol over MQTT.
    /// The host must add Silverback with MQTT support, e.g. <c>services.AddSilverback().WithConnectionToMessageBroker(o => o.AddMqtt())</c>.
    /// One device agent can run per process.
    /// </summary>
    public static IServiceCollection AddDeviceAgent<THandler>(this IServiceCollection services, Action<DeviceAgentOptions> configure)
        where THandler : class, IDeviceHandler
    {
        services.AddOptions<DeviceAgentOptions>()
            .Configure(configure)
            .Validate(x => !string.IsNullOrEmpty(x.GetDeviceId()), "DeviceName or DeviceId is required.")
            .ValidateOnStart();

        services.AddSingleton<THandler>();
        services.AddSingleton<IDeviceHandler>(sp => sp.GetRequiredService<THandler>());
        services.AddSingleton<DeviceTelemetryPublisher>();
        services.AddSingleton<DeviceAgent>();
        services.AddHostedService(sp => sp.GetRequiredService<DeviceAgent>());

        services.ConfigureSilverback()
            .AddBrokerClientsConfigurator<DeviceAgentBrokerClientsConfigurator>()
            .AddSingletonBrokerClientCallback(sp => sp.GetRequiredService<DeviceAgent>())
            .AddDelegateSubscriber((IInboundEnvelope<StringMessage<DeviceCommandPayload>> envelope, DeviceAgent agent) => agent.OnCommandAsync(envelope));

        return services;
    }
}
