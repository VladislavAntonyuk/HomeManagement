using System.Text.Json;
using HomeManagement.Infrastructure.IpCameras;
using MQTTnet.Formatter;
using Silverback.Messaging.Configuration;

namespace HomeManagement.Infrastructure;

public class BrokerClientsConfigurator : IBrokerClientsConfigurator
{
    public void Configure(BrokerClientsConfigurationBuilder builder)
    {
        builder
            .AddMqttClients(clients => clients
                .ConnectViaTcp("localhost", 1883)
                .AddClient(client => client
                    .WithClientId("frigate-client")
                    .UseProtocolVersion(MqttProtocolVersion.V500)
                    .Consume(endpoint => endpoint
                        .ConsumeFrom("frigate/events")
                        .DeserializeJson(serializer => serializer.Configure(o =>
                        {
                            o.PropertyNameCaseInsensitive = true;
                            o.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                        }).UseModel<FrigateEventMessage>().IgnoreMessageTypeHeader())
                        .WithAtLeastOnceQoS()
                        .OnError(policy => policy.Retry(3).ThenSkip()))));
    }
}