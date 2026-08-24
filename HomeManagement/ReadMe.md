sudo /home/orangepi/.dotnet/dotnet HomeManagement.dll --urls http://+:80

sudo nano /lib/systemd/system/home-management.service

[Unit]
Description=Home Management
Requires=media-orangepi-MyPassport.mount
After=network.target media-orangepi-MyPassport.mount

[Service]
User=orangepi
Group=orangepi
AmbientCapabilities=CAP_NET_BIND_SERVICE
Type=idle
WorkingDirectory=/home/orangepi/Projects/HomeManagement/
ExecStart=/home/orangepi/.dotnet/dotnet /home/orangepi/Projects/HomeManagement/HomeManagement.dll --urls http://+:80
Restart=on-failure

[Install]
WantedBy=default.target






sudo systemctl daemon-reload
sudo systemctl enable home-management.service
sudo systemctl start home-management.service
sudo systemctl status home-management.service




ssh orangepi@home-management.local
rm -r Projects/HomeManagement


scp -r "./" orangepi@home-management.local:/home/orangepi/Projects/HomeManagement




On Windows only:
Add-Content -Path "$env:windir\System32\drivers\etc\hosts" -Value "`n192.168.50.151  cameras.home-management.local"


on orangepi
sudo apt update && sudo apt install -y avahi-utils
sudo tee /etc/systemd/system/avahi-cameras.service > /dev/null << 'EOF'
[Unit]
Description=Publish cameras.home-management.local mDNS record
After=avahi-daemon.service
Requires=avahi-daemon.service

[Service]
Type=simple
ExecStart=/bin/bash -c 'exec avahi-publish-address -R cameras.home-management.local $(hostname -I | awk "{print \$1}")'
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl restart avahi-cameras.service
sudo systemctl enable avahi-cameras.service