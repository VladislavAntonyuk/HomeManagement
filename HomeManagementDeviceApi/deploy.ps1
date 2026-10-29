param(
    # SSH target of the device.
    [string] $Target = 'orangepi@192.168.50.117',
    # Settings of the device: appsettings.<Environment>.json is deployed as appsettings.json. Empty keeps appsettings.json.
    [string] $Environment = 'LiveCamera',
    [string] $Runtime = 'linux-arm64'
)

$ErrorActionPreference = 'Stop'

function Invoke-Checked([scriptblock] $command) {
    & $command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $command"
    }
}

$publish = Join-Path $PSScriptRoot 'bin/Release/net11.0/publish'
$settings = if ($Environment) { Join-Path $PSScriptRoot "appsettings.$Environment.json" }
if ($settings -and -not (Test-Path $settings)) {
    throw "Settings file '$settings' not found."
}

if (Test-Path $publish) {
    Remove-Item $publish -Recurse -Force
}

Invoke-Checked { dotnet publish $PSScriptRoot -c Release -r $Runtime --self-contained false -o $publish }
if ($settings) {
    Copy-Item $settings (Join-Path $publish 'appsettings.json') -Force
}

Invoke-Checked { ssh $Target 'rm -rf ~/Projects/HomeManagementDeviceApi.new' }
Invoke-Checked { scp -r $publish "${Target}:~/Projects/HomeManagementDeviceApi.new" }
Invoke-Checked { ssh -t $Target 'sudo systemctl stop home-management-device-api && rm -rf ~/Projects/HomeManagementDeviceApi && mv ~/Projects/HomeManagementDeviceApi.new ~/Projects/HomeManagementDeviceApi && sudo systemctl start home-management-device-api && systemctl status home-management-device-api --no-pager' }

Read-Host 'Press Enter to exit'
