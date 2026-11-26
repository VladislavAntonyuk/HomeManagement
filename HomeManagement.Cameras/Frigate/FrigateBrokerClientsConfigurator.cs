using System.Text.Json;
using HomeManagement.DeviceSdk.Mqtt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MQTTnet.Formatter;
using Silverback.Messaging.Configuration;
using Silverback.Messaging.Messages;

namespace HomeManagement.Cameras.Frigate;

public sealed class FrigateBrokerClientsConfigurator(IOptions<CamerasOptions> options, IConfiguration configuration) : IBrokerClientsConfigurator
{
    public void Configure(BrokerClientsConfigurationBuilder builder)
    {
        var settings = options.Value;
        builder.AddMqttClients(clients =>
        {
            clients.ConnectViaConnectionString(configuration.GetConnectionString("mqtt"));

            clients.AddClient(client => client
                .WithClientId(settings.FrigateClientId)
                .UseProtocolVersion(MqttProtocolVersion.V500)
                .Consume(endpoint => endpoint
                    .ConsumeFrom(FrigateTopics.Events)
                    .DeserializeJson(serializer => serializer.Configure(o =>
                    {
                        o.PropertyNameCaseInsensitive = true;
                        o.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                    }).UseModel<FrigateEventMessage>().IgnoreMessageTypeHeader())
                    .WithAtLeastOnceQoS()
                    .OnError(policy => policy.Retry(3).ThenSkip()))
                .Consume(endpoint => endpoint
                    .ConsumeFrom(FrigateTopics.Stats)
                    .DeserializeJson(serializer => serializer.Configure(o =>
                    {
                        o.PropertyNameCaseInsensitive = true;
                        o.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                    }).UseModel<FrigateStatsMessage>().IgnoreMessageTypeHeader())
                    // The next stats come within a minute, so a broken one is only skipped.
                    .OnError(policy => policy.Skip()))
                .Consume(endpoint => endpoint
                    .ConsumeFrom(FrigateTopics.Available)
                    .ConsumeStrings(serializer => serializer.UseDiscriminator<FrigateAvailabilityPayload>())
                    .WithAtLeastOnceQoS())
                .Consume(endpoint => endpoint
                    .ConsumeFrom(FrigateTopics.ProfileState, FrigateTopics.AllRecordingStates, FrigateTopics.AllEnabledStates)
                    .ConsumeStrings(serializer => serializer.UseDiscriminator<FrigateStatePayload>())
                    .WithAtLeastOnceQoS())
                .Produce<StringMessage<FrigateCommandPayload>>(endpoint => endpoint
                    .ProduceToDynamicTopic()
                    .ProduceStrings()
                    .WithAtLeastOnceQoS()));
        });
    }
}
