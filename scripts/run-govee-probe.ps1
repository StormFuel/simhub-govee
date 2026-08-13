[CmdletBinding()]
param(
    [ValidateSet('inspect', 'discover', 'power', 'brightness', 'color', 'segments')]
    [string]$Command = 'inspect',
    [string]$Device,
    [ValidateSet('on', 'off')]
    [string]$State,
    [ValidateRange(1, 100)]
    [int]$Brightness,
    [string]$Rgb,
    [string]$Colors,
    [ValidateSet('on', 'off')]
    [string]$Gradient,
    [string]$GoveeDirectory = 'C:\Program Files\Govee\Govee Desktop\GoveeAPI',
    [switch]$ConfirmHardwareChange
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'tools\SimHub.Govee.Probe\SimHub.Govee.Probe.csproj'
$executable = Join-Path $projectRoot 'tools\SimHub.Govee.Probe\bin\Release\net48\SimHub.Govee.Probe.exe'

dotnet build $projectPath --configuration Release --nologo --configfile (Join-Path $projectRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) {
    throw "Probe build failed with exit code $LASTEXITCODE."
}

$arguments = @($Command, '--govee-dir', $GoveeDirectory)
if ($Device) { $arguments += @('--device', $Device) }
if ($State) { $arguments += @('--state', $State) }
if ($PSBoundParameters.ContainsKey('Brightness')) { $arguments += @('--value', $Brightness.ToString()) }
if ($Rgb) { $arguments += @('--rgb', $Rgb) }
if ($Colors) { $arguments += @('--colors', $Colors) }
if ($Gradient) { $arguments += @('--gradient', $Gradient) }
if ($ConfirmHardwareChange) { $arguments += '--confirm-hardware-change' }

& $executable @arguments
exit $LASTEXITCODE
