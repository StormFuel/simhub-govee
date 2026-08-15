[CmdletBinding()]
param(
    [string]$DeviceIp = '192.0.2.1'
)

$ErrorActionPreference = 'Continue'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultDirectory = Join-Path $projectRoot '.probe-results'
$etlPath = Join-Path $resultDirectory 'govee-lan.etl'
$textPath = Join-Path $resultDirectory 'govee-lan.txt'
$logPath = Join-Path $resultDirectory 'govee-lan-capture.log'
$probe = Join-Path $projectRoot 'tools\SimHub.Govee.LanProbe\bin\Release\net48\SimHub.Govee.LanProbe.exe'

New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
Remove-Item -LiteralPath $etlPath,$textPath,$logPath -Force -ErrorAction SilentlyContinue
Set-Location -LiteralPath $resultDirectory

function Invoke-PktMon {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    "pktmon $($Arguments -join ' ')" | Add-Content -LiteralPath $logPath
    & pktmon @Arguments 2>&1 | Tee-Object -FilePath $logPath -Append
    $code = $LASTEXITCODE
    "Exit code: $code" | Add-Content -LiteralPath $logPath
    if ($code -ne 0 -and -not $AllowFailure) {
        throw "pktmon failed with exit code $code. See $logPath"
    }
}

try {
    Invoke-PktMon -Arguments @('stop') -AllowFailure
    Invoke-PktMon -Arguments @('filter','remove')
    Invoke-PktMon -Arguments @('filter','add','GoveeLan','-i',$DeviceIp,'-t','UDP')
    Invoke-PktMon -Arguments @('start','--capture','--comp','all','--pkt-size','0','--file-name',$etlPath,'--file-size','16')

    & $probe --ip $DeviceIp --scan-ip $DeviceIp --status-scan --timeout 5
    $probeExitCode = $LASTEXITCODE
}
finally {
    Invoke-PktMon -Arguments @('stop') -AllowFailure
    if (Test-Path -LiteralPath $etlPath) {
        Invoke-PktMon -Arguments @('etl2txt',$etlPath,'--out',$textPath,'--brief','--hex') -AllowFailure
    }
    Invoke-PktMon -Arguments @('filter','remove') -AllowFailure
}

Write-Host "Probe exit code: $probeExitCode"
Write-Host "Packet trace: $textPath"
if (Test-Path -LiteralPath $textPath) {
    Get-Content -LiteralPath $textPath
}
else {
    Get-Content -LiteralPath $logPath
}
Read-Host 'Press Enter to close this window'
