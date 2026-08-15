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

function Get-SegmentMetadata {
    param([Parameter(Mandatory = $true)]$Capability)

    $segmentField = @($Capability.parameters.fields | Where-Object { $_.fieldName -eq 'segment' })[0]
    if ($null -eq $segmentField) {
        return [pscustomobject]@{ Indices = @(); MinimumPerCommand = $null; MaximumPerCommand = $null }
    }

    $indices = @()
    if ($null -ne $segmentField.options) {
        $indices = @($segmentField.options | ForEach-Object { [int]$_.value } | Sort-Object -Unique)
    }
    elseif ($null -ne $segmentField.elementRange) {
        $minimum = [int]$segmentField.elementRange.min
        $maximum = [int]$segmentField.elementRange.max
        if ($maximum -ge $minimum) { $indices = @($minimum..$maximum) }
    }

    return [pscustomobject]@{
        Indices = $indices
        MinimumPerCommand = if ($null -ne $segmentField.size) { [int]$segmentField.size.min } else { $null }
        MaximumPerCommand = if ($null -ne $segmentField.size) { [int]$segmentField.size.max } else { $null }
    }
}

function Format-OptionalNumber {
    param($Value)
    if ($null -eq $Value) { return 'not specified' }
    return [string]$Value
}

New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
$lines = New-Object 'System.Collections.Generic.List[string]'
$apiKey = $null
$headers = $null
$exitCode = 10

trap {
    $message = $_.Exception.Message
    if (-not [string]::IsNullOrWhiteSpace($apiKey)) { $message = $message.Replace($apiKey, '[redacted]') }
    $failure = "Cloud probe stopped unexpectedly: $message"
    if (-not $lines.Contains($failure)) { $lines.Add($failure) }
    $lines | Set-Content -LiteralPath $resultPath -Encoding UTF8
    Write-Host $failure -ForegroundColor Red
    Write-Host "Sanitized results: $resultPath"
    Read-Host 'Press Enter to close this window'
    exit 10
}

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
if ($confirmation.Trim() -ine 'YES') {
    throw 'Cancelled before making an API request.'
}

$headers = @{ 'Govee-API-Key' = $apiKey }
try {
    $deviceResponse = Invoke-RestMethod -Method Get -Uri "$apiRoot/user/devices" -Headers $headers -ContentType 'application/json'
    if ($deviceResponse.code -ne 200) {
        throw "Device discovery returned API code $($deviceResponse.code): $($deviceResponse.message)"
    }

    $devices = @($deviceResponse.data)
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
            $segmentedRgb = @($target.capabilities | Where-Object { $_.type -eq 'devices.capabilities.segment_color_setting' -and $_.instance -eq 'segmentedColorRgb' })[0]
            $segmentedBrightness = @($target.capabilities | Where-Object { $_.type -eq 'devices.capabilities.segment_color_setting' -and $_.instance -eq 'segmentedBrightness' })[0]
            $gradient = @($target.capabilities | Where-Object { $_.instance -eq 'gradientToggle' })[0]

            if ($null -ne $segmentedRgb) {
                $metadata = Get-SegmentMetadata -Capability $segmentedRgb
                $indexText = if ($metadata.Indices.Count -gt 0) { $metadata.Indices -join ',' } else { 'not reported' }
                $lines.Add("H6046 segmented RGB indices ($($metadata.Indices.Count)): $indexText")
                $lines.Add("  Segments per command: min=$(Format-OptionalNumber $metadata.MinimumPerCommand), max=$(Format-OptionalNumber $metadata.MaximumPerCommand)")
            }
            else {
                $lines.Add('H6046 does not advertise segmented RGB control.')
            }
            $lines.Add("  Segmented brightness: $(if ($null -ne $segmentedBrightness) { 'supported' } else { 'not advertised' })")
            if ($null -ne $gradient) {
                $gradientOptions = @($gradient.parameters.options | ForEach-Object { "$($_.name)=$($_.value)" })
                $lines.Add("  Gradient toggle: $(if ($gradientOptions.Count -gt 0) { $gradientOptions -join ', ' } else { 'advertised; options not reported' })")
            }
            else {
                $lines.Add('  Gradient toggle: not advertised')
            }

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

}
catch {
    $status = $_.Exception.Response.StatusCode.value__
    if ($status -eq 401) {
        $failure = 'The Developer API rejected the key (HTTP 401). This must be a Govee Developer API key, not the Desktop API GUID.'
    }
    elseif ($status -eq 429) {
        $failure = 'The Govee API rate limit was reached (HTTP 429). Wait for the reset window before retrying.'
    }
    else {
        $failure = "Cloud probe failed: $($_.Exception.Message)"
    }
    $lines.Add($failure)
    Write-Host $failure -ForegroundColor Red
    $exitCode = 10
}
finally {
    if ($null -ne $headers) { $headers.Clear() }
    $apiKey = $null
    if ($lines.Count -eq 0) { $lines.Add('Cloud probe ended before a result was produced.') }
    $lines | Set-Content -LiteralPath $resultPath -Encoding UTF8
}

$lines | ForEach-Object { Write-Host $_ }
Write-Host ''
Write-Host "Sanitized results: $resultPath"
Read-Host 'Press Enter to close this window'
exit $exitCode
