namespace HomeManagement.Infrastructure.Mqtt;

public class MqttOptions
{
    /// <summary>
    /// MQTT client id of the hub, also used in the topic devices send command responses to.
    /// </summary>
    public string ClientId { get; set; } = "home-management-hub";
}
