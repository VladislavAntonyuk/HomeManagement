# IoTSamples

[![Buy Me A Coffee](https://ik.imagekit.io/VladislavAntonyuk/vladislavantonyuk/misc/bmc-button.png)](https://www.buymeacoffee.com/vlad.antonyuk)

[![Stand With Ukraine](https://img.shields.io/badge/made_in-ukraine-ffd700.svg?labelColor=0057b7)](https://stand-with-ukraine.pp.ua)

License: MIT.

## Projects

The folders on disk match the solution folders in `IoTSamples.slnx`.

- [HomeManagement](HomeManagement): the hub, with [HomeManagement.Cameras](HomeManagement.Cameras), [HomeManagement.Shared](HomeManagement.Shared) and the [HomeManagement.AppHost](HomeManagement.AppHost) Aspire host.
- [HomeManagement.Client](HomeManagement.Client): the MAUI app.
- [HomeManagement.DeviceSdk](HomeManagement.DeviceSdk): the .NET device library.
- [Devices](Devices): everything that connects to the hub as a device.
  - [ESP8266](Devices/ESP8266) and [ESP32](Devices/ESP32): Arduino sketches.
  - [HomeManagement.Devices.Fridge](Devices/HomeManagement.Devices.Fridge): nanoFramework.
  - [HomeManagement.Devices.ProcessDevice](Devices/HomeManagement.Devices.ProcessDevice): .NET agent for Linux boards.
  - [BluetoothGpioController](Devices/BluetoothGpioController) and [IPCamera](Devices/IPCamera).
