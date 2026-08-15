[CmdletBinding()]
param(
    [string]$Sku = 'H6046',
    [int]$TestBrightness = 35
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultDirectory = Join-Path $projectRoot '.probe-results'
$resultPath = Join-Path $resultDirectory 'bar-separation-0-4-5-9.txt'
$apiRoot = 'https://openapi.api.govee.com/router/api/v1'
$lines = New-Object 'System.Collections.Generic.List[string]'
$apiKey = $null
$headers = $null
$device = $null
$snapshot = $null
$mutationStarted = $false
$restorePassed = $false
$exitCode = 10

function Mask-DeviceId {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return 'unknown' }
    if ($Value.Length -le 5) { return '***' }
    return '***' + $Value.Substring($Value.Length - 5)
}

function Get-State {
    param([Parameter(Mandatory = $true)]$Target)
    $body = @{
        requestId = [Guid]::NewGuid().ToString()
        payload = @{ sku = $Target.sku; device = $Target.device }
    } | ConvertTo-Json -Compress -Depth 8
    $response = Invoke-RestMethod -Method Post -Uri "$apiRoot/device/state" -Headers $headers -ContentType 'application/json' -Body $body
    if ($response.code -ne 200) { throw "State query returned API code $($response.code): $($response.msg)" }
    $values = @{}
    foreach ($capability in @($response.payload.capabilities)) { $values[$capability.instance] = $capability.state.value }
    if ($values.online -ne $true) { throw "$Sku is offline according to Govee Cloud." }
    return [pscustomobject]@{
        Power = [int]$values.powerSwitch
        Brightness = [int]$values.brightness
        Rgb = [int]$values.colorRgb
    }
}

function Send-Control {
    param(
        [Parameter(Mandatory = $true)][string]$Type,
        [Parameter(Mandatory = $true)][string]$Instance,
        [Parameter(Mandatory = $true)]$Value
    )
    $body = @{
        requestId = [Guid]::NewGuid().ToString()
        payload = @{
            sku = $device.sku
            device = $device.device
            capability = @{ type = $Type; instance = $Instance; value = $Value }
        }
    } | ConvertTo-Json -Compress -Depth 10
    $response = Invoke-RestMethod -Method Post -Uri "$apiRoot/device/control" -Headers $headers -ContentType 'application/json' -Body $body
    if ($response.code -ne 200) { throw "$Instance returned API code $($response.code): $($response.msg)" }
    Start-Sleep -Milliseconds 600
}

function Send-Power {
    param([int]$Value)
    Send-Control -Type 'devices.capabilities.on_off' -Instance 'powerSwitch' -Value $Value
}

function Send-Brightness {
    param([int]$Value)
    Send-Control -Type 'devices.capabilities.range' -Instance 'brightness' -Value $Value
}

function Send-WholeColor {
    param([int]$Rgb)
    Send-Control -Type 'devices.capabilities.color_setting' -Instance 'colorRgb' -Value $Rgb
}

function Send-SegmentBrightness {
    param([int[]]$Segments, [int]$Brightness)
    Send-Control -Type 'devices.capabilities.segment_color_setting' -Instance 'segmentedBrightness' -Value @{ segment = $Segments; brightness = $Brightness }
}

function Send-SegmentColor {
    param([int[]]$Segments, [int]$Rgb)
    Send-Control -Type 'devices.capabilities.segment_color_setting' -Instance 'segmentedColorRgb' -Value @{ segment = $Segments; rgb = $Rgb }
}

function Restore-PrimitiveState {
    if (-not $mutationStarted -or $null -eq $snapshot -or $null -eq $device) { return }
    Write-Host 'Restoring the captured whole-device color, brightness, and power...' -ForegroundColor Yellow
    Send-WholeColor -Rgb $snapshot.Rgb
    Send-Brightness -Value $snapshot.Brightness
    Send-Power -Value $snapshot.Power
    $restored = $null
    $restorePassed = $false
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        Start-Sleep -Seconds 2
        $restored = Get-State -Target $device
        $lines.Add("Restore verification attempt ${attempt}: power=$($restored.Power), brightness=$($restored.Brightness), rgb=$($restored.Rgb)")
        $restorePassed = $restored.Power -eq $snapshot.Power -and $restored.Brightness -eq $snapshot.Brightness -and $restored.Rgb -eq $snapshot.Rgb
        if ($restorePassed) { break }
        if ($attempt -eq 2 -and $restored.Brightness -ne $snapshot.Brightness) {
            $lines.Add('Brightness had not restored after two checks; resending the captured brightness.')
            Send-Brightness -Value $snapshot.Brightness
        }
    }
    $script:restorePassed = $restorePassed
    $lines.Add($(if ($restorePassed) { 'RESTORE VERIFIED for queryable primitive state.' } else { 'RESTORE VERIFICATION FAILED for queryable primitive state.' }))
}

New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue

try {
    Write-Host 'Govee H6046 0-4 versus 5-9 bar-separation test' -ForegroundColor Cyan
    Write-Host 'This test sets both bars GREEN, then sets segments 0-4 RED and segments 5-9 BLUE.'
    Write-Host 'It can restore power, whole-device RGB, and brightness, but it cannot recover an unknown segmented pattern.' -ForegroundColor Yellow
    Write-Host 'Enter the Govee Developer cloud API key saved in Step 1. The key is never printed or saved.'
    $secureKey = Read-Host 'Developer API key' -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
    try { $apiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer).Trim() }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    if ([string]::IsNullOrWhiteSpace($apiKey)) { throw 'No API key was entered.' }
    $headers = @{ 'Govee-API-Key' = $apiKey }

    $discovery = Invoke-RestMethod -Method Get -Uri "$apiRoot/user/devices" -Headers $headers -ContentType 'application/json'
    if ($discovery.code -ne 200) { throw "Discovery returned API code $($discovery.code): $($discovery.message)" }
    $targets = @($discovery.data | Where-Object { $_.sku -eq $Sku })
    if ($targets.Count -ne 1) { throw "Expected exactly one $Sku but discovery returned $($targets.Count)." }
    $device = $targets[0]
    $segmentCapability = @($device.capabilities | Where-Object { $_.instance -eq 'segmentedColorRgb' })[0]
    $segmentField = @($segmentCapability.parameters.fields | Where-Object { $_.fieldName -eq 'segment' })[0]
    if ($null -ne $segmentField.options) {
        $indices = @($segmentField.options | ForEach-Object { [int]$_.value } | Sort-Object -Unique)
    }
    elseif ($null -ne $segmentField.elementRange) {
        $minimumSegment = [int]$segmentField.elementRange.min
        $maximumSegment = [int]$segmentField.elementRange.max
        $indices = if ($maximumSegment -ge $minimumSegment) { @($minimumSegment..$maximumSegment) } else { @() }
    }
    else {
        $indices = @()
    }
    if (($indices -join ',') -ne '0,1,2,3,4,5,6,7,8,9,10,11,12,13,14') { throw "Unexpected segment topology: $($indices -join ',')." }

    $snapshot = Get-State -Target $device
    $hex = '#{0:X6}' -f $snapshot.Rgb
    Write-Host "Target: $Sku $(Mask-DeviceId $device.device); snapshot power=$($snapshot.Power), brightness=$($snapshot.Brightness), color=$hex."
    Write-Host 'Before continuing, make sure replacing any current segmented effect with that whole-device snapshot is acceptable.' -ForegroundColor Yellow
    $confirmation = Read-Host 'Type TEST BAR SEPARATION AND RESTORE to continue'
    if ($confirmation.Trim() -ine 'TEST BAR SEPARATION AND RESTORE') { throw 'Cancelled before changing the light.' }

    $lines.Add("Target: SKU=$Sku, ID=$(Mask-DeviceId $device.device), segments=0-14")
    $lines.Add("Snapshot: power=$($snapshot.Power), brightness=$($snapshot.Brightness), rgb=$($snapshot.Rgb)")
    $mutationStarted = $true
    if ($snapshot.Power -ne 1) { Send-Power -Value 1 }
    Send-Brightness -Value $TestBrightness
    Send-WholeColor -Rgb 0x00FF00
    Send-SegmentColor -Segments @(0,1,2,3,4) -Rgb 0xFF0000
    Send-SegmentColor -Segments @(5,6,7,8,9) -Rgb 0x0000FF
    Write-Host ''
    Write-Host 'TEST ACTIVE: segments 0-4 RED; segments 5-9 BLUE.' -ForegroundColor Cyan
    $observation = Read-Host 'Describe the final color/pattern shown by the left bar and the right bar'
    $lines.Add("Observation: $($observation.Trim())")
    $exitCode = 0
}
catch {
    $message = $_.Exception.Message
    if (-not [string]::IsNullOrWhiteSpace($apiKey)) { $message = $message.Replace($apiKey, '[redacted]') }
    $lines.Add("Test error: $message")
    Write-Host "Test error: $message" -ForegroundColor Red
    $exitCode = 10
}
finally {
    try { Restore-PrimitiveState }
    catch {
        $message = $_.Exception.Message
        if (-not [string]::IsNullOrWhiteSpace($apiKey)) { $message = $message.Replace($apiKey, '[redacted]') }
        $lines.Add("RESTORE ERROR: $message. Inspect the light immediately.")
        Write-Host "RESTORE ERROR: $message. Inspect the light immediately." -ForegroundColor Red
        $exitCode = 10
    }
    if ($mutationStarted -and -not $restorePassed) { $exitCode = 10 }
    if ($null -ne $headers) { $headers.Clear() }
    $apiKey = $null
    if ($lines.Count -eq 0) { $lines.Add('Test ended before a result was produced.') }
    $lines | Set-Content -LiteralPath $resultPath -Encoding UTF8
}

$lines | ForEach-Object { Write-Host $_ }
Write-Host "Sanitized results: $resultPath"
Read-Host 'Press Enter to close this window'
exit $exitCode
