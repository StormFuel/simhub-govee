[CmdletBinding()]
param(
    [switch]$TogglePower,
    [switch]$ConfirmHardwareChange,
    [string]$IpAddress,
    [string]$Sku = 'H6046'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'tools\SimHub.Govee.LanProbe\SimHub.Govee.LanProbe.csproj'
$executable = Join-Path $projectRoot 'tools\SimHub.Govee.LanProbe\bin\Release\net48\SimHub.Govee.LanProbe.exe'

dotnet build $projectPath --configuration Release --nologo --configfile (Join-Path $projectRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw "LAN probe build failed with exit code $LASTEXITCODE." }

$arguments = @()
if ($IpAddress) { $arguments += @('--ip', $IpAddress) } else { $arguments += @('--sku', $Sku) }
if ($TogglePower) { $arguments += '--toggle-power' }
if ($ConfirmHardwareChange) { $arguments += '--confirm-hardware-change' }

& $executable @arguments
exit $LASTEXITCODE
