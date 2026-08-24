using HomeManagement.Infrastructure.IpCameras;
using Silverback.Configuration;
using Silverback.Messaging.Configuration;

namespace HomeManagement.Infrastructure.Extensions;

public static class ServiceBusExtensions
{
    public static void AddServiceBus(this WebApplicationBuilder builder)
    {
        builder.Services.AddSilverback()
            .WithConnectionToMessageBroker(options => options.AddMqtt().ConnectAfterStartup().RetryOnConnectionFailure())
            .AddBrokerClientsConfigurator<BrokerClientsConfigurator>()
            .AddDelegateSubscriber((FrigateEventMessage message, FrigateEventsSubscriber subscriber) => subscriber.OnFrigateEventAsync(message));
    }
}