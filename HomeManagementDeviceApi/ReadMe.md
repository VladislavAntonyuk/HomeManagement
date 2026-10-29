sudo /home/vladislav/.dotnet/dotnet HomeManagementDeviceApi.dll

sudo nano /lib/systemd/system/home-management-device-api.service

[Unit]
Description=Home Management Device API
After=network.target

[Service]
Type=notify
WorkingDirectory=/home/vladislav/Projects/HomeManagementDeviceApi/
ExecStart=/home/vladislav/.dotnet/dotnet /home/vladislav/Projects/HomeManagementDeviceApi/HomeManagementDeviceApi.dll
Restart=on-failure

[Install]
WantedBy=default.target






sudo systemctl daemon-reload
sudo systemctl enable home-management-device-api.service
sudo systemctl start home-management-device-api.service
sudo systemctl status home-management-device-api.service
sudo systemctl stop home-management-device-api.service



ssh vladislav@raspberrypi-zero-2w.local
rm -r Projects/HomeManagementDeviceApi


scp -r "./" vladislav@raspberrypi-zero-2w.local:/home/vladislav/Projects/HomeManagementDeviceApi
scp -r "./" orangepi@192.168.50.117:/home/orangepi/Projects/HomeManagementDeviceApi


Deploy from the HomeManagementDeviceApi folder:
.\deploy.ps1
.\deploy.ps1 -Target vladislav@raspberrypi-zero-2w.local -Environment ''

It publishes, replaces the published appsettings.json with appsettings.<Environment>.json (an empty -Environment keeps appsettings.json), copies to ~/Projects/HomeManagementDeviceApi.new, then stops the service, swaps the folders and starts it again.
The defaults are -Target orangepi@192.168.50.117 -Environment LiveCamera.
The service must already be installed (see above).
