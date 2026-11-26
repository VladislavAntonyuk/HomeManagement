#!/usr/bin/env bash
# Installs the Tuya plug device from ~/Projects/HomeManagement.Devices.TuyaPlug.new as a systemd service.
# Run on the target by deploy.ps1. It is idempotent.
set -euo pipefail

name=HomeManagement.Devices.TuyaPlug
service=home-management-tuya-plug
target="$HOME/Projects/$name"
staging="$target.new"

dotnet_path="$HOME/.dotnet/dotnet"
if [ ! -x "$dotnet_path" ]; then
    dotnet_path="$(command -v dotnet)"
fi

# The Tuya Access Secret is readable only by the service user.
chmod 600 "$staging/appsettings.json"

sudo tee "/etc/systemd/system/$service.service" > /dev/null <<EOF
[Unit]
Description=Home Management Tuya plug device
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
User=$(id -un)
WorkingDirectory=$target/
ExecStart=$dotnet_path $target/$name.dll
Restart=on-failure
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl stop "$service" 2>/dev/null || true
rm -rf "$target"
mv "$staging" "$target"
sudo systemctl enable --now "$service"
systemctl status "$service" --no-pager
