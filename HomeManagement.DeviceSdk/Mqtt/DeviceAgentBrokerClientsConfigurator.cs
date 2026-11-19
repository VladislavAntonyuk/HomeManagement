using HomeManagement.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MQTTnet.Formatter;
using Silverback.Messaging.Configuration;
using Silverback.Messaging.Messages;
using Silverback.Messaging.Serialization;
using System.Text.Json;

namespace HomeManagement.DeviceSdk.Mqtt;

public sealed class DeviceAgentBrokerClientsConfigurator(IOptions<DeviceAgentOptions> options, IConfiguration configuration) : IBrokerClientsConfigurator
{
    public void Configure(BrokerClientsConfigurationBuilder builder)
    {
        var settings = options.Value;
        var deviceId = settings.GetDeviceId();

        builder.AddMqttClients(clients =>
        {
            clients.ConnectViaConnectionString(configuration.GetConnectionString("mqtt"));

            clients.AddClient(client => client
                .WithClientId(deviceId)
                .UseProtocolVersion(MqttProtocolVersion.V500)
                .RequestCleanStart()
                .SendLastWillMessage<StringMessage<DeviceStatusPayload>>(lastWill => lastWill
                    .SendMessage(new StringMessage<DeviceStatusPayload>(DeviceTopics.Offline))
                    .ProduceTo(DeviceTopics.Status(deviceId))
                    .SerializeUsing(new StringMessageSerializer())
                    .Retain()
                    .WithAtLeastOnceQoS())
                .Consume(endpoint => endpoint
                    .ConsumeFrom(DeviceTopics.AllCommands(deviceId))
                    .ConsumeStrings(serializer => serializer.UseDiscriminator<DeviceCommandPayload>())
                    .WithAtLeastOnceQoS()
                    .OnError(policy => policy.Skip()))
                .Produce<DeviceTwin>(endpoint => endpoint
                    .ProduceTo(DeviceTopics.Twin(deviceId))
                    .Retain()
                    .SerializeAsJson(serializer => serializer.Configure(ConfigureJson))
                    .WithAtLeastOnceQoS())
                .Produce<StringMessage<DeviceStatusPayload>>(endpoint => endpoint
                    .ProduceTo(DeviceTopics.Status(deviceId))
                    .Retain()
                    .ProduceStrings()
                    .WithAtLeastOnceQoS())
                .Produce<StringMessage<DeviceTelemetryPayload>>(endpoint => endpoint
                    .ProduceToDynamicTopic()
                    .Retain()
                    .ProduceStrings()
                    .WithAtLeastOnceQoS())
                // Events are not retained, so the broker never replays an old event, e.g. to the hub after it restarts.
                .Produce<StringMessage<DeviceEventPayload>>(endpoint => endpoint
                    .ProduceToDynamicTopic()
                    .ProduceStrings()
                    .WithAtLeastOnceQoS())
                .Produce<DeviceCommandResponse>(endpoint => endpoint
                    .ProduceToDynamicTopic()
                    .SerializeAsJson(serializer => serializer.Configure(ConfigureJson))
                    .WithAtLeastOnceQoS()));
        });
    }

    private static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    }
}
