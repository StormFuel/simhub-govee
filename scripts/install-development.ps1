[CmdletBinding()]
param([string]$SimHubPath = 'C:\Program Files (x86)\SimHub')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'src\SimHub.Govee\bin\Release\net48\SimHub.Govee.dll'
if (-not (Test-Path -LiteralPath $source)) { throw 'Release DLL not found. Run scripts\build.ps1 first.' }
if (-not (Test-Path -LiteralPath (Join-Path $SimHubPath 'SimHubWPF.exe'))) { throw "SimHub was not found at $SimHubPath." }
if (Get-Process SimHubWPF -ErrorAction SilentlyContinue) { throw 'Close SimHub before installing the development DLL.' }
Copy-Item -LiteralPath $source -Destination (Join-Path $SimHubPath 'SimHub.Govee.dll') -Force
Write-Host 'Installed SimHub.Govee.dll. Start SimHub and enable Govee Controller Plugin for SimHub under Settings > Plugins.'
