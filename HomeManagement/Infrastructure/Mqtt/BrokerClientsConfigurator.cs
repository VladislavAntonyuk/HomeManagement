using System.Text.Json;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.Infrastructure.IpCameras;
using HomeManagement.Shared;
using Microsoft.Extensions.Options;
using MQTTnet.Formatter;
using Silverback.Messaging.Configuration;
using Silverback.Messaging.Messages;

namespace HomeManagement.Infrastructure.Mqtt;

public class BrokerClientsConfigurator(IOptions<MqttOptions> mqttOptions) : IBrokerClientsConfigurator
{
    public void Configure(BrokerClientsConfigurationBuilder builder)
    {
        var settings = mqttOptions.Value;
        builder
            .AddMqttClients(clients =>
            {
                clients.ConnectViaTcp(settings.Host, settings.Port);
                if (!string.IsNullOrEmpty(settings.Username))
                {
                    clients.WithCredentials(settings.Username, settings.Password);
                }

                clients.AddClient(client => client
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
                        .OnError(policy => policy.Retry(3).ThenSkip()))
                    .Consume(endpoint => endpoint
                        .ConsumeFrom(FrigateAvailability.Topic)
                        .ConsumeStrings(serializer => serializer.UseDiscriminator<FrigateAvailability>())
                        .WithAtLeastOnceQoS())
                    .Produce<StringMessage<FrigateProfile>>(endpoint => endpoint
                        .ProduceTo(FrigateProfile.SetTopic)
                        .ProduceStrings()
                        .WithAtLeastOnceQoS())
                    .Produce<StringMessage<FrigateCameraEnabled>>(endpoint => endpoint
                        .ProduceToDynamicTopic()
                        .ProduceStrings()
                        .WithAtLeastOnceQoS()));

                clients.AddClient(client => client
                    .WithClientId(settings.ClientId)
                    .UseProtocolVersion(MqttProtocolVersion.V500)
                    .Consume(endpoint => endpoint
                        .ConsumeFrom(DeviceTopics.AllTwins)
                        .DeserializeJson(serializer => serializer.Configure(ConfigureJson).UseModel<DeviceTwin>().IgnoreMessageTypeHeader())
                        .WithAtLeastOnceQoS()
                        .OnError(policy => policy.Retry(3).ThenSkip()))
                    .Consume(endpoint => endpoint
                        .ConsumeFrom(DeviceTopics.AllStatuses)
                        .ConsumeStrings(serializer => serializer.UseDiscriminator<DeviceStatusPayload>())
                        .WithAtLeastOnceQoS())
                    .Consume(endpoint => endpoint
                        .ConsumeFrom(DeviceTopics.AllTelemetry)
                        .ConsumeStrings(serializer => serializer.UseDiscriminator<DeviceTelemetryPayload>())
                        .WithAtLeastOnceQoS())
                    .Consume(endpoint => endpoint
                        .ConsumeFrom(DeviceTopics.HubResponses(settings.ClientId))
                        .DeserializeJson(serializer => serializer.Configure(ConfigureJson).UseModel<CommandResponse>().IgnoreMessageTypeHeader())
                        .WithAtLeastOnceQoS()
                        .OnError(policy => policy.Skip()))
                    .Produce<DeviceCommandRequest>(endpoint => endpoint
                        .ProduceTo(request => DeviceTopics.Command(request!.DeviceId, request.Command))
                        .SetResponseTopic(DeviceTopics.HubResponses(settings.ClientId))
                        .SetCorrelationData(request => request!.RequestId)
                        // A command the device could not receive in time must not run later, e.g. a restart after a reconnect.
                        .WithMessageExpiration(DeviceHub.CommandTimeout)
                        .SerializeAsJson(serializer => serializer.Configure(ConfigureJson))
                        .WithAtLeastOnceQoS())
                    .Produce<StringMessage<RetainedMessageClear>>(endpoint => endpoint
                        .ProduceToDynamicTopic()
                        .Retain()
                        .ProduceStrings()
                        .WithAtLeastOnceQoS()));
            });
    }

    private static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    }
}
