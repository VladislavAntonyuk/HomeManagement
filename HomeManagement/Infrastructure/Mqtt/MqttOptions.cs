namespace HomeManagement.Infrastructure.Mqtt;

public class MqttOptions
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1883;

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>
    /// MQTT client id of the hub, also used in the topic devices send command responses to.
    /// </summary>
    public string ClientId { get; set; } = "home-management-hub";
}
