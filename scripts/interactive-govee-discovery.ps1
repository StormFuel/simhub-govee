[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'tools\SimHub.Govee.Probe\bin\Release\net48\SimHub.Govee.Probe.exe'
$resultDirectory = Join-Path $projectRoot '.probe-results'
$resultPath = Join-Path $resultDirectory 'discovery.txt'

New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
Set-Content -LiteralPath $resultPath -Value @(
    "Discovery launcher started: $([DateTime]::Now.ToString('s'))",
    "Launcher elevated: $((New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"
)

function Write-ProbeStage {
    param([string]$Message)
    Add-Content -LiteralPath $resultPath -Value $Message
}

Write-Host 'SimHub Govee discovery probe' -ForegroundColor Cyan
Write-Host 'Copy the GUID directly from Govee Desktop Settings > API.'
Write-Host 'This operation discovers devices and does not change their state.'
Write-Host ''

$secureGuid = Read-Host 'Paste the Govee Desktop API GUID, then press Enter (input is masked)' -AsSecureString
$guidPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureGuid)
try {
    $plainGuid = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($guidPointer).Trim()
    $parsedGuid = [Guid]::Empty
    if (-not [Guid]::TryParse($plainGuid, [ref]$parsedGuid)) {
        throw 'The pasted value is not a valid GUID. Nothing was sent to Govee.'
    }

    $normalizedGuid = $parsedGuid.ToString('D')
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $digest = $sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($normalizedGuid))
    }
    finally {
        $sha256.Dispose()
    }

    $fingerprint = ([BitConverter]::ToString($digest, 0, 4)).Replace('-', '').ToLowerInvariant()
    $lastFour = $normalizedGuid.Substring($normalizedGuid.Length - 4)

    Write-Host ''
    Write-Host 'Paste confirmation (the full credential remains hidden):' -ForegroundColor Yellow
    Write-Host "  Normalized length: $($normalizedGuid.Length) characters (expected 36)"
    Write-Host "  Last four:        $lastFour"
    Write-Host "  Fingerprint:      $fingerprint"
    Write-ProbeStage "Credential confirmation: length=$($normalizedGuid.Length), last-four=$lastFour, fingerprint=$fingerprint"
    $confirmation = Read-Host 'Does this match the GUID shown in Govee Desktop? Type YES to continue'
    if ($confirmation -cne 'YES') {
        throw 'Credential confirmation was not accepted. Nothing was sent to Govee.'
    }

    Write-ProbeStage 'Credential confirmation accepted; starting probe.'
    $previousCredential = [Environment]::GetEnvironmentVariable('GOVEE_DESKTOP_API_GUID', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('GOVEE_DESKTOP_API_GUID', $normalizedGuid, 'Process')
        $probeOutput = & $executable discover 2>&1
        $probeOutput | ForEach-Object { Write-Host $_ }
        $probeOutput | Add-Content -LiteralPath $resultPath -Encoding UTF8
        $probeExitCode = $LASTEXITCODE
        Write-ProbeStage "Native probe exit code: $probeExitCode"
    }
    finally {
        [Environment]::SetEnvironmentVariable('GOVEE_DESKTOP_API_GUID', $previousCredential, 'Process')
    }
}
catch {
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-ProbeStage "Launcher failure: $($_.Exception.GetType().FullName): $($_.Exception.Message)"
    $probeExitCode = 2
}
finally {
    if ($guidPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($guidPointer)
    }
    $plainGuid = $null
    $normalizedGuid = $null
    $secureGuid = $null
}

Write-Host ''
Write-Host "Probe exit code: $probeExitCode"
Write-Host "Sanitized results: $resultPath"
Write-ProbeStage "Launcher exit code: $probeExitCode"
Read-Host 'Press Enter to close this window'
exit $probeExitCode
