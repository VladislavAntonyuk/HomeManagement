using System.Text.Json;
using HomeManagement.Shared;
using Microsoft.Extensions.Options;
using MQTTnet.Formatter;
using Silverback.Messaging.Configuration;
using Silverback.Messaging.Messages;
using Silverback.Messaging.Serialization;

namespace HomeManagementDeviceApi.Mqtt;

public class AgentBrokerClientsConfigurator(
    IOptions<MqttSettings> mqttOptions,
    IOptions<DeviceSettings> deviceOptions) : IBrokerClientsConfigurator
{
    public void Configure(BrokerClientsConfigurationBuilder builder)
    {
        var settings = mqttOptions.Value;
        var deviceId = deviceOptions.Value.GetDeviceId();

        builder.AddMqttClients(clients =>
        {
            clients.ConnectViaTcp(settings.Host, settings.Port);
            if (!string.IsNullOrEmpty(settings.Username))
            {
                clients.WithCredentials(settings.Username, settings.Password);
            }

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
                .Produce<CommandResponse>(endpoint => endpoint
                    .ProduceToDynamicTopic()
                    .SerializeAsJson(serializer => serializer.Configure(ConfigureJson))
                    .WithAtLeastOnceQoS()));
        });
    }

    public static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    }
}
