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

$remote = '~/Projects/HomeManagement.Devices.ProcessDevice.new'
Invoke-Checked { ssh $Target "rm -rf $remote" }
Invoke-Checked { scp -r $publish "${Target}:$remote" }
# install.sh swaps the folders, (re)writes the systemd unit and removes the old home-management-device-api service.
Invoke-Checked { ssh -t $Target "bash $remote/install.sh" }

Read-Host 'Press Enter to exit'
