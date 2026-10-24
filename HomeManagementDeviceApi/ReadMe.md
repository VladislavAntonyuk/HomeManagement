sudo /home/vladislav/.dotnet/dotnet HomeManagementDeviceApi.dll --urls http://+:80

sudo nano /lib/systemd/system/home-management-device-api.service

[Unit]
Description=Home Management Device API
After=network.target

[Service]
Type=idle
WorkingDirectory=/home/vladislav/Projects/HomeManagementDeviceApi/
ExecStart=/home/vladislav/.dotnet/dotnet /home/vladislav/Projects/HomeManagementDeviceApi/HomeManagementDeviceApi.dll --urls http://+:80
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