using System.Text.Json;
using HomeManagement.Application.DeviceManagement;
using HomeManagement.DeviceSdk;
using HomeManagement.DeviceSdk.Mqtt;
using HomeManagement.Shared;
using Microsoft.Extensions.Options;
using MQTTnet.Formatter;
using Silverback.Messaging.Configuration;
using Silverback.Messaging.Messages;

namespace HomeManagement.Infrastructure.Mqtt;

public class BrokerClientsConfigurator(IOptions<MqttOptions> mqttOptions, IConfiguration configuration) : IBrokerClientsConfigurator
{
    public void Configure(BrokerClientsConfigurationBuilder builder)
    {
        var settings = mqttOptions.Value;
        builder
            .AddMqttClients(clients =>
            {
                clients.ConnectViaConnectionString(configuration.GetConnectionString("mqtt"));

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