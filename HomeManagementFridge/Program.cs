using nanoFramework.Json;
using nanoFramework.M2Mqtt;
using nanoFramework.M2Mqtt.Messages;
using nanoFramework.Networking;
using nanoFramework.Runtime.Native;
using System;
using System.Collections;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;

namespace HomeManagementFridge;

/// <summary>
/// Telemetry-only device of HomeManagement/DeviceProtocol.md: publishes its twin, status (with an "offline" last will) and telemetry over MQTT.
/// The temperature is read from the Liebherr SmartDevice Home API at every telemetry interval, and the hub answers Get actions from the latest value.
/// M2Mqtt cannot set correlation data on a publish, so the device has no Post actions.
/// </summary>
public class Program
{
    private const string DeviceName = "Fridge";

    // Topic-safe id, also used as the MQTT client id.
    private const string DeviceId = "fridge";

    private const string InfoKey = "info";
    private const string TemperatureKey = "temperature";

    private const string Online = "online";
    private const string Offline = "offline";

    private const int TelemetryIntervalMs = 60000;
    private const int ReconnectDelayMs = 10000;

    // M2Mqtt only pings after a whole keep alive period without sending anything, so it has to be shorter than the telemetry
    // interval. Otherwise every publish postpones the ping, and a connection that died silently (e.g. a router restart) is never noticed.
    private const ushort KeepAliveSeconds = 30;

    // Restarting recovers from everything the device cannot fix itself, e.g. lost Wi-Fi or low memory.
    private const int MaxDisconnectedMs = 10 * 60 * 1000;

    private static readonly string StatusTopic = Topic("status");

    // Ticks instead of DateTime.UtcNow, because the clock is only set once Wi-Fi syncs the time.
    private static long _bootTicks;

    public static void Main()
    {
        _bootTicks = Environment.TickCount64;

        Debug.WriteLine("Connecting to Wi-Fi...");
        using (var cts = new CancellationTokenSource(60000))
        {
            if (!WifiNetworkHelper.ConnectDhcp(Config.WifiSsid, Config.WifiPassword, requiresDateTime: true, token: cts.Token))
            {
                Debug.WriteLine($"Failed to connect to Wi-Fi: {WifiNetworkHelper.Status}. Restarting.");
                Thread.Sleep(ReconnectDelayMs);
                Power.RebootDevice();
            }
        }

        Debug.WriteLine("Connected to Wi-Fi.");

        var fridge = new LiebherrFridge();
        MqttClient client = null;
        var lastConnectedTicks = Environment.TickCount64;

        while (true)
        {
            if (client is not { IsConnected: true })
            {
                Close(client);
                client = TryConnect();
                if (client is null)
                {
                    if (Environment.TickCount64 - lastConnectedTicks >= MaxDisconnectedMs)
                    {
                        Debug.WriteLine("No connection to the MQTT broker for 10 minutes. Restarting.");
                        Power.RebootDevice();
                    }

                    Thread.Sleep(ReconnectDelayMs);
                    continue;
                }
            }

            lastConnectedTicks = Environment.TickCount64;

            var isPublished = TryPublish(client, Topic("telemetry/" + InfoKey), BuildInfoJson());
            if (isPublished && fridge.ReadTemperatureJson() is { } temperature)
            {
                isPublished = TryPublish(client, Topic("telemetry/" + TemperatureKey), temperature);
            }

            if (!isPublished)
            {
                // Reconnect right away instead of waiting for the keep alive to notice.
                Close(client);
                client = null;
                continue;
            }

            Thread.Sleep(TelemetryIntervalMs);
        }
    }

    /// <summary>
    /// Connects a new client every time: a client whose connection was lost is not reused.
    /// </summary>
    private static MqttClient TryConnect()
    {
        MqttClient client = null;
        try
        {
            client = new MqttClient(Config.MqttHost, Config.MqttPort, false, null, null, MqttSslProtocols.None)
            {
                ProtocolVersion = MqttProtocolVersion.Version_5
            };

            var result = client.Connect(
                DeviceId,
                Config.MqttUsername.Length > 0 ? Config.MqttUsername : null,
                Config.MqttPassword.Length > 0 ? Config.MqttPassword : null,
                willRetain: true,
                willQosLevel: MqttQoSLevel.AtLeastOnce,
                willFlag: true,
                willTopic: StatusTopic,
                willMessage: Offline,
                cleanSession: true,
                keepAlivePeriod: KeepAliveSeconds);

            if (result != MqttReasonCode.Success)
            {
                Debug.WriteLine($"MQTT broker refused the connection: {result}.");
                Close(client);
                return null;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Failed to connect to the MQTT broker: {e.Message}");
            Close(client);
            return null;
        }

        Debug.WriteLine($"Connected to the MQTT broker as '{DeviceId}'.");
        if (TryPublish(client, Topic("twin"), BuildTwinJson()) && TryPublish(client, StatusTopic, Online))
        {
            return client;
        }

        Close(client);
        return null;
    }

    private static void Close(MqttClient client)
    {
        if (client is null)
        {
            return;
        }

        try
        {
            client.Close();
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Failed to close the MQTT client: {e.Message}");
        }
    }

    private static bool TryPublish(MqttClient client, string topic, string payload)
    {
        try
        {
            client.Publish(topic, Encoding.UTF8.GetBytes(payload), null, null, MqttQoSLevel.AtLeastOnce, retain: true);
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Failed to publish '{topic}': {e.Message}");
            return false;
        }
    }

    private static string Topic(string kind) => "home/devices/" + DeviceId + "/" + kind;

    private static string BuildTwinJson()
    {
        var actions = new ArrayList
        {
            Action("INFO", "Get", InfoKey),
            Action("Temperature", "Get", TemperatureKey)
        };

        var twin = new Hashtable
        {
            { "name", DeviceName },
            { "actions", actions }
        };

        return JsonConvert.SerializeObject(twin);
    }

    private static Hashtable Action(string action, string commandType, string command) => new Hashtable
    {
        { "action", action },
        { "commandType", commandType },
        { "command", command }
    };

    private static string BuildInfoJson()
    {
        var metrics = new Hashtable
        {
            { "UptimeSeconds", ((Environment.TickCount64 - _bootTicks) / 1000).ToString() },
            { "FreeMemory", nanoFramework.Runtime.Native.GC.Run(false).ToString() }
        };

        var info = new Hashtable
        {
            { "name", DeviceName },
            { "address", GetAddress() },
            { "metrics", metrics }
        };

        return JsonConvert.SerializeObject(info);
    }

    private static string GetAddress()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.IPv4Address is { Length: > 0 } address && address != "0.0.0.0")
            {
                return address;
            }
        }

        return string.Empty;
    }
}
