$ErrorActionPreference = 'Stop'

$target = 'orangepi@home-management.local'
$publish = Join-Path $PSScriptRoot 'bin/Release/net11.0/publish'

dotnet publish $PSScriptRoot -c Release -r linux-arm64 --self-contained false -o $publish
Copy-Item (Join-Path $PSScriptRoot 'appsettings.Development.json') (Join-Path $publish 'appsettings.json') -Force

ssh $target 'rm -rf ~/Projects/HomeManagement.new'
scp -r $publish "${target}:~/Projects/HomeManagement.new"
ssh -t $target 'sudo systemctl stop home-management && rm -rf ~/Projects/HomeManagement && mv ~/Projects/HomeManagement.new ~/Projects/HomeManagement && sudo systemctl start home-management && systemctl status home-management --no-pager'

Read-Host 'Press Enter to exit'
