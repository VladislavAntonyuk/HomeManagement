using HomeManagement.Application.DeviceManagement;
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
            .AddDelegateSubscriber((IInboundEnvelope<DeviceTwin> envelope, DeviceHub hub) => hub.OnTwinAsync(envelope))
            .AddDelegateSubscriber((IInboundEnvelope<StringMessage<InboundStatusPayload>> envelope, DeviceHub hub) => hub.OnStatus(envelope))
            .AddDelegateSubscriber((IInboundEnvelope<StringMessage<InboundTelemetryPayload>> envelope, DeviceHub hub) => hub.OnTelemetry(envelope))
            .AddDelegateSubscriber((IInboundEnvelope<CommandResponse> envelope, DeviceHub hub) => hub.OnResponse(envelope));
    }
}
