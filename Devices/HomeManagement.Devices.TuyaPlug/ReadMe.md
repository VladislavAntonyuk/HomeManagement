# HomeManagement.Devices.TuyaPlug

A .NET device agent for a Tuya smart plug, e.g. the MOES 16A Wi-Fi plug with power monitoring.
It connects to the MQTT broker through `HomeManagement.DeviceSdk` and controls the plug through the Tuya Cloud OpenAPI.
It has no HTTP endpoints.

| Action | Type | Key | Value |
| --- | --- | --- | --- |
| ON | Post | `power/on` | `{"status":"on"}` |
| OFF | Post | `power/off` | `{"status":"off"}` |
| STATUS | Get | `power` | `{"status":"on"}`, `{"status":"off"}` or `{"status":"offline"}` |

STATUS is read every `TelemetryIntervalSeconds` (120 by default).
ON and OFF publish the new status right away.
Switching the plug with its button or in the Tuya app shows up at the next read.

## Tuya Cloud project

The plug must be added in the Smart Life or Tuya Smart app, not only in Apple Home or Google Home through Matter.

1. Sign in at [platform.tuya.com](https://platform.tuya.com) and open **Cloud > Development > Create Cloud Project**.
2. Choose the **Smart Home** development method and the data center of the app account, e.g. **Central Europe**.
   Keep the default API services, which include **IoT Core**.
3. Open the project, go to **Devices > Link App Account > Add App Account** and scan the QR code in the app (**Me**, then the scan icon).
   The plug appears in the device list with its **Device ID**.
4. Copy the **Access ID** and **Access Secret** from the project **Overview**.

The free IoT Core trial has a monthly API call quota and has to be extended on the platform from time to time.
Every read is one call, so keep `TelemetryIntervalSeconds` high enough for the quota.

## Settings

Copy `appsettings.json` to `appsettings.Development.json` and add the Tuya keys.
It is in `.gitignore`, so it is never committed, and `deploy.ps1` deploys it as `appsettings.json`:

```json
"Tuya": {
  "BaseUrl": "https://openapi.tuyaeu.com",
  "SwitchCode": "switch_1",
  "AccessId": "<Access ID>",
  "AccessSecret": "<Access Secret>",
  "DeviceId": "<Device ID>"
}
```

The settings:

- `Tuya:BaseUrl`: OpenAPI endpoint of the data center, `https://openapi.tuyaeu.com` for Central Europe, `https://openapi-weaz.tuyaeu.com` for Western Europe, `https://openapi.tuyaus.com` for Western America.
- `Tuya:SwitchCode`: data point that switches the outlet, `switch_1` on single-outlet plugs.
  When it is wrong, the STATUS error lists the data points the plug has.
- `DeviceName`, `DeviceId` and `TelemetryIntervalSeconds`: the device on the broker.
  Run one instance per plug, each with its own `DeviceId`.

## Deploy

Run from this folder:

```powershell
.\deploy.ps1
```

The default target is the hub, `orangepi@home-management.local`, which also runs the broker at `mqtt://localhost:1883`.
The script publishes the project, replaces the published `appsettings.json` with `appsettings.Development.json` and copies it to `~/Projects/HomeManagement.Devices.TuyaPlug.new`.
`install.sh` makes the settings readable only by the user, swaps the folders and writes the `home-management-tuya-plug` systemd unit.

```bash
systemctl status home-management-tuya-plug
sudo systemctl restart home-management-tuya-plug
journalctl -u home-management-tuya-plug -f
```
