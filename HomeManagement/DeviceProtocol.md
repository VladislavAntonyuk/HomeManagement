# Device protocol

Devices talk to Home Management only through the MQTT broker, like devices of Azure IoT Hub.
The hub and a device never know about each other: a device only knows the broker and its own topics, and the hub only knows what devices publish there.
Devices do not expose HTTP endpoints, and the hub never connects to a device directly.
Any device that follows the topics below appears on the Devices page, in workflows and in the MCP tools.
`HomeManagement.DeviceSdk` implements the protocol for .NET devices, and the topic names are in `HomeManagement.Shared/DeviceTopics.cs`.

## Topics

`{deviceId}` is a topic-safe id: lowercase letters, digits, `-` and `_`.
Use the same value as the MQTT client id.

| Topic | Direction | Retained | Payload |
|---|---|---|---|
| `home/devices/{deviceId}/twin` | device to hub | yes | Device name and actions as JSON |
| `home/devices/{deviceId}/status` | device to hub | yes | `online` or `offline` |
| `home/devices/{deviceId}/telemetry/{key}` | device to hub | `Get`: yes, `Event`: no | Value of a `Get` action, or an occurrence of an `Event` action, any text or JSON |
| `home/devices/{deviceId}/commands/{key}` | hub to device | no | Command request |
| `home/hub/{hubId}/responses` | device to hub | no | Command response as JSON |

All messages use QoS 1.

## Connecting

1. Connect with MQTT v5, client id `{deviceId}`, and a last will of `offline` on the status topic with the retain flag.
2. Subscribe to `home/devices/{deviceId}/commands/#` if the device has `Post` actions.
3. Publish the twin, then `online` to the status topic, both retained.
4. Publish the `info` telemetry right away and then at the heartbeat interval announced in the twin.
5. Publish the values of `Get` actions, retained, whenever a value changes and at a regular interval.
   Publish `Event` actions, not retained, when they happen.
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
`Get` actions are state, read from the latest telemetry value, so the hub never waits for the device to answer them.
A workflow condition on a `Get` action matches for as long as the value does.
`Event` actions are telemetry that reports something that happened, e.g. a detected person.
Publish events without the retain flag, so the broker never sends an old event again, e.g. to the hub after it restarts.
A workflow condition on an `Event` action matches once for every event, right when it arrives; include an id, so the hub sees two equal events as a change right away.
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

- `HomeManagement.DeviceSdk`: the .NET device library, with Silverback. A device implements `IDeviceHandler` (its actions, commands and readings) and calls `AddDeviceAgent<THandler>`.
- `HomeManagement.Devices.ProcessDevice`: .NET devices such as the Orange Pi camera, whose actions run processes configured in `CommandsSettings`.
- `HomeManagement.Cameras`: the camera module of the hub, which exposes Frigate as a device (see below).
- `Devices/ESP8266/libraries/DeviceApi`: ESP8266 and ESP32 sketches, with H4AsyncMQTT (see `Devices/ESP8266/README.md` and `Devices/ESP32/README.md`).

Every device connects to the broker itself with MQTT v5; no device relays messages for another one.

## Telemetry-only devices

A sensor that has nothing to execute only needs the twin with `Get` actions, the status with its last will, and the telemetry topics.
It does not subscribe to anything.

## Cameras module and Frigate

Frigate is part of the hub, but the hub core does not know about it.
The `HomeManagement.Cameras` module runs in the hub process and talks to the hub core only through the broker, as the device `frigate-module`, the same way an Azure IoT Edge module runs on its gateway.
It has its own MQTT clients: the device client `frigate-module`, and `home-management-frigate` for the Frigate topics.
Neither may be `frigate`, which is the client id Frigate itself uses by default: the broker allows one connection per client id.

The `Frigate` device has these actions, built from the cameras and profiles in the Frigate configuration (`/api/config`):

| Action | Type | Value or effect |
|---|---|---|
| `State` | Get | `{ "available": true, "profile": "away" }`, from `frigate/available` and `frigate/profile/state` |
| `Detection` | Event | `{ "id", "camera", "label", "score", "snapshotUrl" }` for every new object Frigate tracks, from `frigate/events` |
| `{camera} State` | Get | `{ "enabled": true, "recording": false, "clip": false, "clipEventId": null }`, from `frigate/{camera}/enabled/state`, `frigate/{camera}/recordings/state` and the clips in progress |
| `Start Recording {camera}`, `Stop Recording {camera}` | Post | `ON` or `OFF` to `frigate/{camera}/recordings/set`: switches recording by the camera's `record` configuration, e.g. continuous or only around detections |
| `Start Clip {camera}`, `Stop Clip {camera}` | Post | Records on demand: creates a Frigate manual event with the label `manual` (`POST /api/events/{camera}/manual/create`) and ends it (`PUT /api/events/{id}/end`) |
| `Profile {name}`, `Profile None` | Post | the profile name or `none` to `frigate/profile/set` |

The actions are read again every time Frigate reports `online`, so a new camera or profile shows up without restarting the hub.
Frigate only turns recording on over MQTT for cameras that have `record.enabled: true` in its configuration.
A clip ends by itself after `Cameras:ClipMaxSeconds` (one hour by default), so a clip whose Stop Clip never comes, e.g. because the hub restarted, does not record forever.
Frigate keeps a clip like an alert, by the `record.alerts.retain` setting of the camera.
Activating a profile clears the recording switches, so switch the profile before you start or stop recording.

The module also runs the RTMP server cameras publish to, on port 1935, with the stream key from `Auth:StreamKey`.
It enables a Frigate camera while the RTMP stream it reads is published, and disables it when the stream stops.
It finds the stream of each camera in the Frigate configuration, either as an `rtmp://` input or as a go2rtc restream of one.
The sync runs on every stream start and stop, and every time Frigate reports `online` on `frigate/available`.
Cameras that do not read from the hub are not changed.

The module connects with the hub's `ConnectionStrings:mqtt`, as the device client `frigate-module` and the Frigate client `home-management-frigate`.

## Away mode with workflows

There is no away mode switch: create workflows for it on the Workflows page, so you choose what happens.

- **Leave home**, without triggers, run from the Workflows page or the app: `Frigate` › `Profile Away`, then `LiveCamera` › `Start LiveCamera`, then `Frigate` › `Start Recording {camera}`.
- **Arrive home**, without triggers: `Frigate` › `Stop Recording {camera}`, `LiveCamera` › `Stop LiveCamera`, `Frigate` › `Profile None`.
- **Person alert**, with both triggers: `Frigate` › `State` › `profile` equals `away`, and `Frigate` › `Detection` › `label` equals `person`.
  Add a Notify step with a message such as `Person on {Trigger.Result.camera}, score {Trigger.Result.score}: {Trigger.Result.snapshotUrl}`.

A Notify message can use every property of the value a device trigger read, as `{Trigger.Result.<property>}` or `{Trigger[0].Result.<property>}` for a given trigger.
`{Trigger.*}` is the trigger that ran the workflow: the event that just happened, such as the detection, otherwise the first matching trigger.
Placeholder names are not case sensitive, and a placeholder that matches nothing stays in the message as written.

## Broker

The hub and the .NET devices connect to the broker in `ConnectionStrings:mqtt` of their `appsettings.json`:

```json
"ConnectionStrings": {
  "mqtt": "mqtt://home-management:password@localhost:1883"
}
```

The port defaults to `1883`, or `8883` with `mqtts://`, which also enables TLS.
The user and password are optional, and special characters in them must be URL-encoded.
Aspire sets the connection string itself when it starts the hub or a device.
Devices point the host to the broker, for example `home-management.local`.

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

# Only needed when the cameras module has its own login instead of the hub's one.
user frigate-module
topic readwrite frigate/#
topic readwrite home/devices/frigate-module/#
pattern write home/hub/+/responses

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
