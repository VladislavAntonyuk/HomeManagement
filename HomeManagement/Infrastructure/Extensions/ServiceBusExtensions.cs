using HomeManagement.Application.DeviceManagement;
using HomeManagement.Infrastructure.IpCameras;
using HomeManagement.Infrastructure.Mqtt;
using HomeManagement.Shared;
using Silverback.Configuration;
using Silverback.Messaging.Configuration;
using Silverback.Messaging.Messages;

namespace HomeManagement.Infrastructure.Extensions;

public static class ServiceBusExtensions
{
    public static void AddServiceBus(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection("Mqtt"));

        builder.Services.AddSilverback()
            .WithConnectionToMessageBroker(options => options.AddMqtt().ConnectAfterStartup().RetryOnConnectionFailure())
            .AddBrokerClientsConfigurator<BrokerClientsConfigurator>()
            .AddDelegateSubscriber((FrigateEventMessage message, FrigateEventsSubscriber subscriber) => subscriber.OnFrigateEventAsync(message))
            .AddDelegateSubscriber((StringMessage<FrigateAvailability> message, FrigateStreamSync sync) => sync.OnFrigateAvailability(message))
            .AddDelegateSubscriber((IInboundEnvelope<DeviceTwin> envelope, DeviceHub hub) => hub.OnTwinAsync(envelope))
            .AddDelegateSubscriber((IInboundEnvelope<StringMessage<DeviceStatusPayload>> envelope, DeviceHub hub) => hub.OnStatus(envelope))
            .AddDelegateSubscriber((IInboundEnvelope<StringMessage<DeviceTelemetryPayload>> envelope, DeviceHub hub) => hub.OnTelemetry(envelope))
            .AddDelegateSubscriber((IInboundEnvelope<CommandResponse> envelope, DeviceHub hub) => hub.OnResponse(envelope));
    }
}
