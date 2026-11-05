# Device protocol

Devices talk to Home Management only through the MQTT broker.
Devices do not expose HTTP endpoints, and the hub never connects to a device directly.
Any device that follows the topics below appears on the Devices page, in workflows and in the MCP tools.
`HomeManagementDeviceApi` is the reference implementation, and the topic names are in `HomeManagement.Shared/DeviceTopics.cs`.

## Topics

`{deviceId}` is a topic-safe id: lowercase letters, digits, `-` and `_`.
Use the same value as the MQTT client id.

| Topic | Direction | Retained | Payload |
|---|---|---|---|
| `home/devices/{deviceId}/twin` | device to hub | yes | Device name and actions as JSON |
| `home/devices/{deviceId}/status` | device to hub | yes | `online` or `offline` |
| `home/devices/{deviceId}/telemetry/{key}` | device to hub | yes | Latest value of a `Get` action, any text or JSON |
| `home/devices/{deviceId}/commands/{key}` | hub to device | no | Command request |
| `home/hub/{hubId}/responses` | device to hub | no | Command response as JSON |

All messages use QoS 1.

## Connecting

1. Connect with MQTT v5, client id `{deviceId}`, and a last will of `offline` on the status topic with the retain flag.
2. Subscribe to `home/devices/{deviceId}/commands/#` if the device has `Post` actions.
3. Publish the twin, then `online` to the status topic, both retained.
4. Publish the `info` telemetry right away and then at the heartbeat interval announced in the twin.
5. Publish other telemetry, retained, whenever a value changes and at a regular interval.
6. On a clean shutdown, publish `offline` to the status topic before disconnecting, because the broker only sends the last will on an unexpected disconnect.

## Twin

```json
{
  "name": "LiveCamera",
  "heartbeatIntervalSeconds": 60,
  "actions": [
    { "action": "Get Temperature", "commandType": "Get", "command": "temperature" },
    { "action": "Stop LiveCamera", "commandType": "Post", "command": "live-camera/stop" }
  ]
}
```

`command` is the key used in the telemetry or command topic.
`heartbeatIntervalSeconds` is optional and says how often the device publishes its `info` telemetry; the hub assumes 60 seconds when it is missing.
`Get` actions are read from the latest telemetry value, so the hub never waits for the device to answer them.
`Post` actions are sent to the device as commands.

The hub adds a device when its twin first arrives and updates the actions every time the twin changes.
The device name is only used for the first registration and can be changed on the Devices page afterwards.
An action the device stops reporting is removed, unless a workflow still uses it.

## Device info and heartbeat

The `info` telemetry (`home/devices/{deviceId}/telemetry/info`) describes the device and doubles as its heartbeat:

```json
{
  "name": "Temperature And Humidity",
  "address": "192.168.1.42",
  "metrics": { "UptimeSeconds": "93784", "FreeHeap": "23456", "Rssi": "-67" }
}
```

`metrics` is a free list of text values.
The hub shows every metric on the Devices page, and formats the ones it knows: `UptimeSeconds`, `Rssi` (dBm), `FreeHeap` (bytes) and `Temperature` (°C).

The last will alone cannot always tell the real status.
When the broker loses power together with a device, or restarts before it saved the last will, the retained `online` stays forever.
So the hub also tracks when it last received any message of a device.
A device that reports `online` but sent nothing for 3 heartbeat intervals is shown as "Not responding" and treated as offline: its actions are disabled and commands to it fail.
The first message it sends again makes it online.
The number of missed heartbeats, the default interval and how often the hub checks are set in the `DevicePresence` section of the hub's `appsettings.json` (`MissedHeartbeats`, `DefaultHeartbeatInterval`, `CheckInterval`).

## Commands

The hub publishes a request with the payload `{}` to `home/devices/{deviceId}/commands/{key}`.
Ignore the payload and any user properties, and use these MQTT v5 properties:

- Response topic: where to publish the response.
- Correlation data: copy it to the response unchanged.
- Message expiry interval of 30 seconds, so a command the device could not receive in time is dropped by the broker instead of running later.

The device runs the command and publishes the response to the response topic with the same correlation data:

```json
{ "isSuccess": true, "output": "live-camera stopped" }
```

The hub waits up to 30 seconds for the response.
A command to a device whose status is not `online` fails immediately.
QoS 1 can deliver a request twice, so ignore a correlation data value that was already handled.

## Implementations

- `HomeManagementDeviceApi`: .NET devices such as the Orange Pi camera, with Silverback.
- `ESP8266/libraries/DeviceApi`: ESP8266 and ESP32 sketches, with H4AsyncMQTT (see `ESP8266/README.md` and `ESP32/README.md`).

Every device connects to the broker itself with MQTT v5; no device relays messages for another one.

## Telemetry-only devices

A sensor that has nothing to execute only needs the twin with `Get` actions, the status with its last will, and the telemetry topics.
It does not subscribe to anything.

## Frigate cameras

Frigate cameras are not devices.
The hub enables a Frigate camera while the RTMP stream it reads is published to the hub, and disables it when the stream stops.
It finds the stream of each camera in the Frigate configuration (`/api/config`), either as an `rtmp://` input or as a go2rtc restream of one.
The sync runs on every stream start and stop, and every time Frigate reports `online` on `frigate/available`.
Cameras that do not read from the hub are not changed.

## Broker

The hub connects to the broker configured in the `Mqtt` section of `appsettings.json`:

```json
"Mqtt": {
  "Host": "localhost",
  "Port": 1883,
  "Username": "home-management",
  "Password": "..."
}
```

`Host` and `Port` default to `localhost` and `1883`, and `Username` and `Password` are optional.
Devices use the same `Mqtt` section in their own `appsettings.json`, with `Host` pointing to the hub, for example `home-management.local`.

Devices now connect to the broker over the network, so give every client a login and limit devices to their own topics.
Example for Mosquitto, in `/etc/mosquitto/conf.d/home-management.conf`:

```
listener 1883
allow_anonymous false
password_file /etc/mosquitto/passwd
acl_file /etc/mosquitto/acl
```

`/etc/mosquitto/acl`:

```
user home-management
topic readwrite #

user frigate
topic readwrite frigate/#

# Every device may only use its own topics. %c is the client id, which must be the device id.
pattern readwrite home/devices/%c/#
pattern write home/hub/+/responses
```

Create the logins with `sudo mosquitto_passwd /etc/mosquitto/passwd <user>`, and add the Frigate login to the `mqtt` section of the Frigate configuration.

Keep retained messages and sessions over a broker restart, and save them often, so a power outage does not bring back an old status:

```
persistence true
persistence_location /var/lib/mosquitto/
autosave_interval 60
```

The hub does not rely on this: a device that stopped sending heartbeats is shown as not responding either way.
