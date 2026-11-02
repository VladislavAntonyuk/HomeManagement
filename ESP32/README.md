# ESP32

## BluettiBridge

Reads a Bluetti power station over Bluetooth LE and connects it to HomeManagement as the `Bluetti` device.
The ESP32 is the device: it publishes the power station's status as telemetry and switches its outputs, following [DeviceProtocol.md](../HomeManagement/DeviceProtocol.md).

| Action | Type | Command |
|---|---|---|
| STATUS | Get | `status` |
| POLLING ON, POLLING OFF | Post | `polling/on`, `polling/off` |
| AC ON, AC OFF | Post | `ac/on`, `ac/off` |
| DC ON, DC OFF | Post | `dc/on`, `dc/off` |

The AC and DC actions are only in the twin when the model can switch that output, so an AP300 only has STATUS and the polling actions.
The ESP32 remembers them across restarts, so they stay on the Devices page while the power station is out of reach.

The status is JSON, for example from an AP300:

```json
{
  "polling": true,
  "connected": true,
  "model": "AP300",
  "serialNumber": "2340000123456",
  "protocol": 2,
  "encrypted": true,
  "batteryPercent": 87,
  "acOutputPower": 96,
  "dcInputPower": 412,
  "acOutputVoltage": 230.1,
  "acOutputFrequency": 50.0,
  "timeRemaining": 18.4,
  "solar1": { "power": 262, "voltage": 38.2, "current": 6.8 },
  "solar2": { "power": 150, "voltage": 36.9, "current": 4.0 }
}
```

Values the model does not report are left out, and `error` explains why the power station is not connected.
The power station is polled every 10 seconds, and the status is published as soon as it changes and every minute.

POLLING OFF disconnects from the power station, for example at night when no updates are needed, and frees it for the Bluetti app.
The status then shows `"polling": false` with the model and serial number, but without readings, which would be stale.
POLLING ON connects again, and the setting is kept across restarts of the ESP32.
After a disconnect, including POLLING OFF and restarting or uploading to the ESP32 while it is connected, the AP300 refuses new connections for about 2 minutes.
The ESP32 keeps retrying, so the readings come back by themselves after that.

### Supported power stations

The sketch uses the Modbus protocol of the Bluetti app, ported from [bluetti_mqtt](https://github.com/warhammerkid/bluetti_mqtt) and [bluetti-bt-lib](https://github.com/Patrick762/bluetti-bt-lib).
It finds the protocol version from the device type registers after connecting.

- Protocol 1: AC200M, AC300, AC500, EP500, EP500P and EB3A.
  Battery, powers, generated energy and the output states are read from registers 10 to 49.
  Outputs are switched with registers 3007 (AC) and 3008 (DC).
- Protocol 2: AP300, AC180, AC180P, AC60, AC60P, AC70, AC2P, EL30V2, EP600 and the other newer models.
  Battery and powers are read from registers 100 to 161.
  The AP300 also reports the time remaining (104), both solar inputs (1212 to 1222) and the AC output frequency and voltage (1500, 1511).
  Outputs are switched with registers 2011 (AC) and 2012 (DC) on the models that support it; the AP300 has no switchable outputs.

Newer Bluetti firmware (IoT module 9046 and later) encrypts the connection.
The power station then sends a challenge right after the connection, and the ESP32 answers it with the key exchange of the Bluetti app: an AES-128 key from the challenge, signed P-256 public keys, and an AES-256 key from ECDH for the Modbus frames ([BluettiEncryption.cpp](BluettiBridge/BluettiEncryption.cpp)).
Older firmware sends no challenge, and the ESP32 talks to it without encryption.

The power station accepts only one Bluetooth connection, so close the Bluetti app while the ESP32 is connected.

### Setup

1. Install the ESP32 boards (`esp32` by Espressif, 3.x) in the Arduino IDE.
2. Install the libraries listed in the [ESP8266 README](../ESP8266/README.md): H4, H4Tools, H4AsyncTCP, H4AsyncMQTT, ArduinoJson, `DeviceApi` and `WifiHelper`.
   `DeviceApi` and `WifiHelper` support both the ESP8266 and the ESP32.
3. Copy `BluettiBridge/Config.h.example` to `BluettiBridge/Config.h` and fill in Wi-Fi, the broker, and optionally the Bluetooth address of the power station.
4. Select the board, e.g. `ESP32C6 Dev Module` with `Flash Size: 16MB` and `Partition Scheme: 16M Flash (3MB APP/9.9MB FATFS)` and `USB CDC On Boot: Enabled` so the log reaches the Serial Monitor over USB, or `ESP32 Dev Module` with `Huge APP (3MB No OTA/1MB SPIFFS)`, because Wi-Fi and Bluetooth do not fit the default 1.2 MB app partition.
5. Upload, and the `Bluetti` device appears on the Devices page.
