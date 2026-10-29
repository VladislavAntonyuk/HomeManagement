namespace HomeManagementDeviceApi.Mqtt;

public class MqttSettings
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1883;

    public string? Username { get; set; }

    public string? Password { get; set; }
}
