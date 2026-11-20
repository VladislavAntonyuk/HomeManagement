using Silverback.Messaging.Configuration.Mqtt;

namespace HomeManagement.DeviceSdk.Mqtt;

public static class MqttClientsConfigurationBuilderExtensions
{
    private const int DefaultPort = 1883;
    private const int DefaultTlsPort = 8883;

    extension(MqttClientsConfigurationBuilder clients)
    {
        /// <summary>
        /// Connects to the broker of a connection string like <c>mqtt://user:password@host:1883</c>, as Aspire passes it in ConnectionStrings:mqtt.
        /// <c>mqtts://</c> enables TLS. The port is optional, and the user and password are only sent when the string has them.
        /// </summary>
        public MqttClientsConfigurationBuilder ConnectViaConnectionString(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Connection string is null or empty.", nameof(connectionString));
            }

            var url = new Uri(connectionString);
            var useTls = url.Scheme.Equals("mqtts", StringComparison.OrdinalIgnoreCase);

            // Uri knows no default port for mqtt, so a missing port is -1.
            clients.ConnectViaTcp(url.Host, url.Port > 0 ? url.Port : useTls ? DefaultTlsPort : DefaultPort);
            if (url.UserInfo.Split(':', 2) is [{ Length: > 0 } user, var password])
            {
                clients.WithCredentials(Uri.UnescapeDataString(user), Uri.UnescapeDataString(password));
            }

            if (useTls)
            {
                clients.EnableTls(_ => { });
            }

            return clients;
        }
    }
}
