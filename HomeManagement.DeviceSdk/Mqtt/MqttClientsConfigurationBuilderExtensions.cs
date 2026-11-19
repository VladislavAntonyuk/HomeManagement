using Silverback.Messaging.Configuration.Mqtt;

namespace HomeManagement.DeviceSdk.Mqtt;

public static class MqttClientsConfigurationBuilderExtensions
{
    extension(MqttClientsConfigurationBuilder clients)
    {
        public MqttClientsConfigurationBuilder ConnectViaConnectionString(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string is null or empty.", nameof(connectionString));
            }

            var url = new Uri(connectionString);
            clients.ConnectViaTcp(url.Host, url.Port);
            return clients;
        }
    }
}
