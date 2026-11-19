#!/usr/bin/env bash
# Installs the process device from ~/Projects/HomeManagement.Devices.ProcessDevice.new as a systemd service.
# Run on the device by deploy.ps1. It is idempotent and also removes the service of the old HomeManagementDeviceApi project.
set -euo pipefail

name=HomeManagement.Devices.ProcessDevice
service=home-management-process-device
old_service=home-management-device-api
target="$HOME/Projects/$name"
staging="$target.new"

dotnet_path="$HOME/.dotnet/dotnet"
if [ ! -x "$dotnet_path" ]; then
    dotnet_path="$(command -v dotnet)"
fi

if systemctl list-unit-files "$old_service.service" --no-legend | grep -q "$old_service"; then
    echo "Removing the old $old_service service"
    sudo systemctl disable --now "$old_service"
    sudo rm -f "/lib/systemd/system/$old_service.service" "/etc/systemd/system/$old_service.service"
    rm -rf "$HOME/Projects/HomeManagementDeviceApi"
fi

sudo tee "/etc/systemd/system/$service.service" > /dev/null <<EOF
[Unit]
Description=Home Management process device
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
WorkingDirectory=$target/
ExecStart=$dotnet_path $target/$name.dll
Restart=on-failure

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl stop "$service" 2>/dev/null || true
rm -rf "$target"
mv "$staging" "$target"
sudo systemctl enable --now "$service"
systemctl status "$service" --no-pager
