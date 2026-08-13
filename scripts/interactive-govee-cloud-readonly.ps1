[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultDirectory = Join-Path $projectRoot '.probe-results'
$resultPath = Join-Path $resultDirectory 'cloud-readonly.txt'
$apiRoot = 'https://openapi.api.govee.com/router/api/v1'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Mask-DeviceId {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return 'unknown' }
    if ($Value.Length -le 5) { return '***' }
    return '***' + $Value.Substring($Value.Length - 5)
}

function Format-StateValue {
    param($Value)
    if ($null -eq $Value) { return 'not queryable' }
    if ($Value -is [string] -or $Value -is [ValueType]) { return [string]$Value }
    return ($Value | ConvertTo-Json -Compress -Depth 8)
}

New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue

Write-Host 'Govee Cloud API read-only probe' -ForegroundColor Cyan
Write-Host 'Enter a Govee Developer API key, not the Govee Desktop API GUID.'
Write-Host 'The key is kept only in this process and is never printed or saved.'
Write-Host ''

$secureKey = Read-Host 'Developer API key' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
try {
    $apiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer).Trim()
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
}

if ([string]::IsNullOrWhiteSpace($apiKey)) {
    throw 'No API key was entered.'
}

$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $hash = $sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($apiKey))
    $fingerprint = -join ($hash[0..3] | ForEach-Object { $_.ToString('x2') })
}
finally {
    $sha256.Dispose()
}

$suffix = if ($apiKey.Length -ge 4) { $apiKey.Substring($apiKey.Length - 4) } else { '(short)' }
Write-Host "Captured $($apiKey.Length) characters; suffix $suffix; fingerprint $fingerprint."
$confirmation = Read-Host 'Type YES to call the read-only device and state endpoints'
if ($confirmation -cne 'YES') {
    throw 'Cancelled before making an API request.'
}

$headers = @{ 'Govee-API-Key' = $apiKey }
try {
    $deviceResponse = Invoke-RestMethod -Method Get -Uri "$apiRoot/user/devices" -Headers $headers -ContentType 'application/json'
    if ($deviceResponse.code -ne 200) {
        throw "Device discovery returned API code $($deviceResponse.code): $($deviceResponse.message)"
    }

    $devices = @($deviceResponse.data)
    $lines = New-Object 'System.Collections.Generic.List[string]'
    $lines.Add("Cloud discovery succeeded. Devices: $($devices.Count)")
    foreach ($device in $devices) {
        $capabilities = @($device.capabilities | ForEach-Object { "$($_.type)/$($_.instance)" })
        $lines.Add("Device: SKU=$($device.sku), ID=$(Mask-DeviceId $device.device)")
        $lines.Add("  Capabilities: $($capabilities -join ', ')")
    }

    $targets = @($devices | Where-Object { $_.sku -eq 'H6046' })
    if ($targets.Count -eq 0) {
        $lines.Add('No H6046 was returned for this Developer API key.')
        $exitCode = 3
    }
    else {
        foreach ($target in $targets) {
            $request = @{
                requestId = [Guid]::NewGuid().ToString()
                payload = @{ sku = $target.sku; device = $target.device }
            } | ConvertTo-Json -Compress -Depth 6
            $stateResponse = Invoke-RestMethod -Method Post -Uri "$apiRoot/device/state" -Headers $headers -ContentType 'application/json' -Body $request
            if ($stateResponse.code -ne 200) {
                $lines.Add("H6046 state query failed with API code $($stateResponse.code): $($stateResponse.msg)")
                $exitCode = 4
                continue
            }

            $lines.Add("H6046 state: ID=$(Mask-DeviceId $target.device)")
            foreach ($capability in @($stateResponse.payload.capabilities)) {
                $lines.Add("  $($capability.type)/$($capability.instance) = $(Format-StateValue $capability.state.value)")
            }
            if ($null -eq $exitCode) { $exitCode = 0 }
        }
    }

    $lines | Set-Content -LiteralPath $resultPath -Encoding UTF8
    $lines | ForEach-Object { Write-Host $_ }
    Write-Host ''
    Write-Host "Sanitized results: $resultPath"
}
catch {
    $status = $_.Exception.Response.StatusCode.value__
    if ($status -eq 401) {
        Write-Error 'The Developer API rejected the key (HTTP 401). This must be a Govee Developer API key, not the Desktop API GUID.'
    }
    elseif ($status -eq 429) {
        Write-Error 'The Govee API rate limit was reached (HTTP 429). Wait for the reset window before retrying.'
    }
    else {
        Write-Error "Cloud probe failed: $($_.Exception.Message)"
    }
    $exitCode = 10
}
finally {
    $headers.Clear()
    $apiKey = $null
}

Read-Host 'Press Enter to close this window'
exit $exitCode
