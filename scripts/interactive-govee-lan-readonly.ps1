[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$firewallScript = Join-Path $PSScriptRoot 'manage-govee-firewall.ps1'
$probe = Join-Path $projectRoot 'tools\SimHub.Govee.LanProbe\bin\Release\net48\SimHub.Govee.LanProbe.exe'
$resultDirectory = Join-Path $projectRoot '.probe-results'
$resultPath = Join-Path $resultDirectory 'lan-readonly.txt'
$ruleName = 'SimHub Govee LAN Probe (UDP 4002)'

New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
& $firewallScript -Action Enable -ProgramPath $probe -RuleName $ruleName

Write-Host ''
Write-Host 'Running read-only Govee LAN discovery and state query...' -ForegroundColor Cyan
$ErrorActionPreference = 'Continue'
& $probe --sku H6046 --timeout 10 --scan-local-subnet 2>&1 | Tee-Object -FilePath $resultPath
$probeExitCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'

Add-Content -LiteralPath $resultPath -Value "Probe exit code: $probeExitCode"

Write-Host ''
Write-Host "Probe exit code: $probeExitCode"
Write-Host "Results: $resultPath"
Write-Host "Temporary firewall rule remains enabled as '$ruleName' for the power test."
Read-Host 'Press Enter to close this window'
exit $probeExitCode
