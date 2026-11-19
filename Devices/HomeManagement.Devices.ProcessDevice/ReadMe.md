# HomeManagement.Devices.ProcessDevice

A .NET device agent for Linux boards such as the Orange Pi camera or a Raspberry Pi.
It connects to the MQTT broker through `HomeManagement.DeviceSdk` and exposes shutdown, restart and the processes configured in `CommandsSettings` as device actions.
It has no HTTP endpoints.

## Deploy

Run from this folder:

```powershell
.\deploy.ps1
.\deploy.ps1 -Target vladislav@raspberrypi-zero-2w.local -Environment ''
```

The defaults are `-Target orangepi@192.168.50.117 -Environment LiveCamera`.
The script publishes the project and replaces the published `appsettings.json` with `appsettings.<Environment>.json`.
An empty `-Environment` keeps `appsettings.json`.
It copies the output to `~/Projects/HomeManagement.Devices.ProcessDevice.new` on the device and runs `install.sh` there.

`install.sh` swaps the folders and writes the `home-management-process-device` systemd unit.
It also enables and starts the service.
On the first run it disables and removes the old `home-management-device-api` service and the `~/Projects/HomeManagementDeviceApi` folder.

## Service

```bash
systemctl status home-management-process-device
sudo systemctl restart home-management-process-device
journalctl -u home-management-process-device -f
```
