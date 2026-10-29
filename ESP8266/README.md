Installing ESP8266 Board in Arduino IDE
https://randomnerdtutorials.com/how-to-install-esp8266-board-arduino-ide/

Підключіть ESP8266 до реле:

- Підключіть один з цифрових пінів ESP8266 (наприклад, D1) до піну IN на реле.

- Підключіть пін GND ESP8266 до GND на реле.

- Пін VCC(V+)  реле підключіть до 3.3V ESP8266.

Підключіть реле до розетки:

- Один з проводів розетки підключіть до COM на реле.

- Другий провід підключіть до NO (нормально відкритий) або NC (нормально закритий) контакту реле в залежності від того, як ви хочете керувати розеткою.

- Замкніть ланцюг, підключивши провід від NO або NC до живлення.

## Connecting to HomeManagement

The sketches connect to the MQTT broker of HomeManagement with MQTT v5 and follow the device protocol in [DeviceProtocol.md](../HomeManagement/DeviceProtocol.md).
The shared code is in the `DeviceApi` library (`MqttDevice`), which every sketch uses directly; there is no HTTP server on the devices anymore.
Each device registers itself on the Devices page as soon as it connects.

Libraries to install in the Arduino sketchbook (`Documents/Arduino/libraries`):

- [H4](https://github.com/hamzahajeir/H4) 4.0.10, commit 9125640
- [H4Tools](https://github.com/hamzahajeir/H4Tools) 0.0.16, commit 396d0c5
- [H4AsyncTCP](https://github.com/hamzahajeir/H4AsyncTCP) 0.0.25, commit 90ec706
- [H4AsyncMQTT](https://github.com/hamzahajeir/H4AsyncMQTT) 1.0.0-rc12, commit 6fbf4cb
- ArduinoJson, from the Library Manager
- `libraries/DeviceApi` and `libraries/WifiHelper` from this folder, for example as junctions

`DeviceApi` and `WifiHelper` also build for the ESP32, see [ESP32/README.md](../ESP32/README.md).

The H4 scheduler owns `setup()` and `loop()`, so the sketches use `h4setup()` and H4 timers (`h4.every`, `h4.once`).

Every `Config.h` needs the broker settings next to the Wi-Fi settings (see `Config.h.example`).
The ESP8266 cannot resolve `.local` names, so `mqttHost` is the IP address of the hub.
