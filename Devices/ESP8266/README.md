# ESP8266

Arduino sketches for ESP8266 boards that connect to HomeManagement as devices.

## Board setup

Install the ESP8266 board in the Arduino IDE: [how to install the ESP8266 board](https://randomnerdtutorials.com/how-to-install-esp8266-board-arduino-ide/).

## Wiring a relay

### ESP8266 to relay

| ESP8266 pin | Relay pin |
|---|---|
| A digital pin, for example D1 | IN |
| GND | GND |
| 3.3V | VCC (V+) |

### Relay to a socket

1. Connect one wire of the socket to COM on the relay.
2. Connect the other wire to NO (normally open) or NC (normally closed), depending on how you want to control the socket.
3. Close the circuit by connecting the wire from NO or NC to the power.

> [!WARNING]
> Mains voltage is dangerous.
> Turn off the power before wiring.

## Connecting to HomeManagement

The sketches connect to the MQTT broker of HomeManagement with MQTT v5 and follow the device protocol in [DeviceProtocol.md](../../HomeManagement/DeviceProtocol.md).
The shared code is in the `DeviceApi` library (`MqttDevice`), which every sketch uses directly.
There is no HTTP server on the devices.
Each device registers itself on the Devices page as soon as it connects.

### Libraries

Install them in the Arduino sketchbook (`Documents/Arduino/libraries`):

| Library | Version | Source |
|---|---|---|
| [H4](https://github.com/hamzahajeir/H4) | 4.0.10, commit 9125640 | GitHub |
| [H4Tools](https://github.com/hamzahajeir/H4Tools) | 0.0.16, commit 396d0c5 | GitHub |
| [H4AsyncTCP](https://github.com/hamzahajeir/H4AsyncTCP) | 0.0.25, commit 90ec706 | GitHub |
| [H4AsyncMQTT](https://github.com/hamzahajeir/H4AsyncMQTT) | 1.0.0-rc12, commit 6fbf4cb | GitHub |
| ArduinoJson | latest | Library Manager |
| [Preferences](https://github.com/vshymanskyy/Preferences) by Volodymyr Shymanskyy | 2.4.0 | Library Manager |
| `DeviceApi` and `WifiHelper` | | [`libraries`](libraries) in this folder, for example as junctions |

`Preferences` is used by `CODetector` and `Motor` to keep settings in flash.
`DeviceApi` and `WifiHelper` also build for the ESP32, see the [ESP32 README](../ESP32/README.md).

### Writing a sketch

The H4 scheduler owns `setup()` and `loop()`, so the sketches use `h4setup()` and H4 timers (`h4.every`, `h4.once`).

### Configuration

Copy `Config.h.example` to `Config.h` in the sketch folder and fill in the Wi-Fi and broker settings.
The ESP8266 cannot resolve `.local` names, so `mqttHost` is the IP address of the hub.
