# HomeManagement

The hub: the web UI, the REST API for the [HomeManagement.Client](../HomeManagement.Client) app and the MCP server.
It talks to devices only through the MQTT broker.
The device protocol is in [DeviceProtocol.md](DeviceProtocol.md).

It runs on an Orange Pi as the `home-management` systemd service and is reachable at `http://home-management.local`.
The IP camera UI (Frigate) is served through the hub at `http://cameras.home-management.local`.

## Deploy

Run from this folder:

```powershell
.\deploy.ps1
```

The script:

1. Publishes the hub for `linux-arm64`.
2. Replaces the published `appsettings.json` with `appsettings.Development.json`.
3. Copies the output to `~/Projects/HomeManagement.new` on `orangepi@home-management.local`.
4. Stops the service, swaps the folders and starts the service again.

The database is in `~/.local/share/HomeManagement/home_management.db`, so it is kept between deploys.

## Service setup

Do this once on the Orange Pi.

Create the unit:

```bash
sudo tee /lib/systemd/system/home-management.service > /dev/null << 'EOF'
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
EOF
```

Enable and start it:

```bash
sudo systemctl daemon-reload
sudo systemctl enable home-management.service
sudo systemctl start home-management.service
sudo systemctl status home-management.service
```

To run the hub by hand instead:

```bash
sudo /home/orangepi/.dotnet/dotnet HomeManagement.dll --urls http://+:80
```

## Cameras subdomain

The hub's own name, `home-management.local`, is published by Avahi automatically.
The `cameras.home-management.local` subdomain is not, so each kind of client needs a step below.

### On the Orange Pi

Publish `cameras.home-management.local` over mDNS with the hub's LAN address.

> [!IMPORTANT]
> Do not take the address from `hostname -I`.
> When Frigate runs in Docker, its first address is the Docker bridge `172.17.0.1`, and iOS and Linux clients get an address they cannot reach.

The script below publishes the address of the default route.
It exits when that address changes, and systemd starts it again with the new one.

```bash
sudo apt update && sudo apt install -y avahi-utils

sudo tee /usr/local/bin/publish-cameras-mdns.sh > /dev/null << 'EOF'
#!/bin/bash
name=cameras.home-management.local
lan_ip() { ip -4 route get 1.1.1.1 2>/dev/null | sed -n 's/.* src \([0-9.]*\).*/\1/p'; }
ip=$(lan_ip)
until [ -n "$ip" ]; do sleep 2; ip=$(lan_ip); done
avahi-publish-address -R "$name" "$ip" &
pid=$!
trap 'kill $pid 2>/dev/null' EXIT
while kill -0 "$pid" 2>/dev/null; do
  sleep 30
  [ "$(lan_ip)" = "$ip" ] || exit 0
done
exit 1
EOF
sudo chmod +x /usr/local/bin/publish-cameras-mdns.sh

sudo tee /etc/systemd/system/avahi-cameras.service > /dev/null << 'EOF'
[Unit]
Description=Publish cameras.home-management.local mDNS record
After=avahi-daemon.service network-online.target
Wants=network-online.target
Requires=avahi-daemon.service

[Service]
Type=simple
ExecStart=/usr/local/bin/publish-cameras-mdns.sh
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl restart avahi-cameras.service
sudo systemctl enable avahi-cameras.service
```

Check it.
The address must be the hub's LAN address, not `172.17.0.1`:

```bash
avahi-resolve -n cameras.home-management.local
```

### On iOS and Android

Nothing to do once the Orange Pi publishes the right address.
If the page still does not load, turn Wi-Fi off and on to drop the cached address.

### On Ubuntu

By default, `nss-mdns` (`mdns4_minimal`) does not resolve `.local` names with more than two labels, such as `cameras.home-management.local`.
Allow them and switch to the full `mdns4` module:

```bash
printf '.local.\n.local\n' | sudo tee /etc/mdns.allow
sudo sed -i 's/mdns4_minimal \[NOTFOUND=return\]/mdns4/' /etc/nsswitch.conf
getent hosts cameras.home-management.local
```

> [!NOTE]
> In a virtual machine, set the network adapter to **Bridged** (in VMware: VM → Settings → Network Adapter → Bridged, with "Replicate physical network connection state").
> With the default NAT network, mDNS multicast does not reach the LAN, so `avahi-resolve` times out and no `.local` name resolves over mDNS.
> After switching, restart the network and Avahi, then check the name:
>
> ```bash
> sudo systemctl restart NetworkManager avahi-daemon
> avahi-resolve -n cameras.home-management.local
> ```
>
> Remove any `home-management.local` line from `/etc/hosts` in the VM, so the names come from mDNS.

### On Windows

Add the subdomain to the hosts file from an elevated PowerShell, with the hub's LAN address:

```powershell
Add-Content -Path "$env:windir\System32\drivers\etc\hosts" -Value "`n192.168.50.151  cameras.home-management.local"
```

## REST API

The API is used by the [HomeManagement.Client](../HomeManagement.Client) app.
All endpoints are under `/api`.
They need the `X-API-Key` header with the `Auth:Key` value (the key you sign in with), or the sign-in cookie.
The routes are in [ApiContracts.cs](../HomeManagement.Shared/Api/ApiContracts.cs), which the app also uses.

| Method | Route | Description |
|---|---|---|
| GET | `/api/dashboard` | Dashboard tiles |
| GET | `/api/devices` | Devices |
| POST | `/api/devices/{deviceId}/actions/{actionId}` | Runs a device action |
| GET | `/api/workflows` | Workflows |
| POST | `/api/workflows/{workflowId}/run` | Runs a workflow |
| PUT | `/api/workflows/{workflowId}/enabled` | Enables or disables a workflow |
| GET | `/api/cameras` | Cameras |
| GET | `/api/cameras/{camera}/snapshot?height=` | Camera snapshot |
| GET | `/api/cameras/{camera}/stream?height=&fps=` | MJPEG stream, proxied from Frigate |
