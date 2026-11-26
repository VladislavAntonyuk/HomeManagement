param(
    # SSH target of the machine that runs the plug device, by default the hub, which also runs the MQTT broker.
    [string] $Target = 'orangepi@home-management.local',
    [string] $Runtime = 'linux-arm64'
)

$ErrorActionPreference = 'Stop'

function Invoke-Checked([scriptblock] $command) {
    & $command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $command"
    }
}

# appsettings.Development.json holds the settings of the deployed device, including the Tuya keys, and is not committed.
$settings = Join-Path $PSScriptRoot 'appsettings.Development.json'
if (-not (Test-Path $settings)) {
    throw "Settings file '$settings' not found. See ReadMe.md."
}

$publish = Join-Path $PSScriptRoot 'bin/Release/net11.0/publish'
if (Test-Path $publish) {
    Remove-Item $publish -Recurse -Force
}

Invoke-Checked { dotnet publish $PSScriptRoot -c Release -r $Runtime --self-contained false -o $publish }
Copy-Item $settings (Join-Path $publish 'appsettings.json') -Force

$remote = '~/Projects/HomeManagement.Devices.TuyaPlug.new'
Invoke-Checked { ssh $Target "rm -rf $remote" }
Invoke-Checked { scp -r $publish "${Target}:$remote" }
# install.sh swaps the folders and (re)writes the systemd unit.
Invoke-Checked { ssh -t $Target "bash $remote/install.sh" }

Read-Host 'Press Enter to exit'
