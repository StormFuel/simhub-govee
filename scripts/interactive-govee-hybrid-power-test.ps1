[CmdletBinding()]
param(
    [string]$DeviceIp = '192.0.2.1',
    [string]$Sku = 'H6046'
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$projectRoot = Split-Path -Parent $PSScriptRoot
$resultDirectory = Join-Path $projectRoot '.probe-results'
$resultPath = Join-Path $resultDirectory 'hybrid-power-test.txt'
$apiRoot = 'https://openapi.api.govee.com/router/api/v1'
$headers = $null
$device = $null
$initialPower = $null
$restoreRequired = $false
$testPassed = $false
$lines = New-Object 'System.Collections.Generic.List[string]'

function Mask-DeviceId {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return 'unknown' }
    if ($Value.Length -le 5) { return '***' }
    return '***' + $Value.Substring($Value.Length - 5)
}

function Get-CloudPower {
    param([Parameter(Mandatory = $true)]$Target)
    $body = @{
        requestId = [Guid]::NewGuid().ToString()
        payload = @{ sku = $Target.sku; device = $Target.device }
    } | ConvertTo-Json -Compress -Depth 6
    $response = Invoke-RestMethod -Method Post -Uri "$apiRoot/device/state" -Headers $headers -ContentType 'application/json' -Body $body
    if ($response.code -ne 200) {
        throw "State query returned API code $($response.code): $($response.msg)"
    }
    $online = @($response.payload.capabilities | Where-Object { $_.instance -eq 'online' })[0].state.value
    if ($online -ne $true) { throw 'Govee cloud reports the H6046 offline; no local command will be sent.' }
    $power = @($response.payload.capabilities | Where-Object { $_.instance -eq 'powerSwitch' })[0].state.value
    if ($null -eq $power -or [int]$power -notin 0,1) { throw 'Cloud state did not contain a valid powerSwitch value.' }
    return [int]$power
}

function Send-LocalPower {
    param([Parameter(Mandatory = $true)][int]$Value)
    $json = @{ msg = @{ cmd = 'turn'; data = @{ value = $Value } } } | ConvertTo-Json -Compress -Depth 4
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $udp = New-Object Net.Sockets.UdpClient
    try {
        [void]$udp.Send($bytes, $bytes.Length, $DeviceIp, 4003)
    }
    finally {
        $udp.Dispose()
    }
}

function Wait-ForCloudPower {
    param(
        [Parameter(Mandatory = $true)]$Target,
        [Parameter(Mandatory = $true)][int]$Expected,
        [int]$Attempts = 6
    )
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        Start-Sleep -Seconds 2
        $observed = Get-CloudPower -Target $Target
        $lines.Add("Cloud verification attempt ${attempt}: power=$observed")
        if ($observed -eq $Expected) { return $true }
    }
    return $false
}

New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue

Write-Host 'Govee hybrid local-command/cloud-verification power test' -ForegroundColor Cyan
Write-Host "Target IP: $DeviceIp; SKU: $Sku"
Write-Host 'The Developer API key is retained only in this process and is never printed or saved.'
$secureKey = Read-Host 'Developer API key' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
try {
    $apiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer).Trim()
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
}
if ([string]::IsNullOrWhiteSpace($apiKey)) { throw 'No API key was entered.' }
$headers = @{ 'Govee-API-Key' = $apiKey }

try {
    $discovery = Invoke-RestMethod -Method Get -Uri "$apiRoot/user/devices" -Headers $headers -ContentType 'application/json'
    if ($discovery.code -ne 200) { throw "Discovery returned API code $($discovery.code): $($discovery.message)" }
    $targets = @($discovery.data | Where-Object { $_.sku -eq $Sku })
    if ($targets.Count -ne 1) { throw "Expected exactly one $Sku but cloud discovery returned $($targets.Count)." }
    $device = $targets[0]
    $initialPower = Get-CloudPower -Target $device
    $temporaryPower = if ($initialPower -eq 1) { 0 } else { 1 }

    Write-Host "Cloud snapshot: device $(Mask-DeviceId $device.device) is $(if($initialPower){'ON'}else{'OFF'})."
    Write-Host "The test will set it $(if($temporaryPower){'ON'}else{'OFF'}) locally, verify through cloud, then restore $(if($initialPower){'ON'}else{'OFF'})." -ForegroundColor Yellow
    $confirmation = Read-Host 'Type TEST AND RESTORE to continue'
    if ($confirmation -cne 'TEST AND RESTORE') { throw 'Cancelled before sending a hardware command.' }

    $lines.Add("Target: SKU=$Sku, ID=$(Mask-DeviceId $device.device), IP=$DeviceIp")
    $lines.Add("Initial cloud power: $initialPower")
    $restoreRequired = $true
    Send-LocalPower -Value $temporaryPower
    $lines.Add("Sent local UDP 4003 temporary power: $temporaryPower")
    if (-not (Wait-ForCloudPower -Target $device -Expected $temporaryPower)) {
        throw 'Cloud did not verify the temporary local power state.'
    }
    $lines.Add('Temporary state verified.')
    $testPassed = $true
}
catch {
    $lines.Add("Test error: $($_.Exception.Message)")
}
finally {
    if ($restoreRequired -and $null -ne $initialPower) {
        try {
            Send-LocalPower -Value $initialPower
            $lines.Add("Sent local UDP 4003 restore power: $initialPower")
            if (Wait-ForCloudPower -Target $device -Expected $initialPower) {
                $lines.Add('RESTORE VERIFIED.')
            }
            else {
                $lines.Add('RESTORE VERIFICATION FAILED. Inspect the light immediately.')
                $testPassed = $false
            }
        }
        catch {
            $lines.Add("RESTORE ERROR: $($_.Exception.Message). Inspect the light immediately.")
            $testPassed = $false
        }
    }
    if ($null -ne $headers) { $headers.Clear() }
    $apiKey = $null
    $lines | Set-Content -LiteralPath $resultPath -Encoding UTF8
}

$lines | ForEach-Object { Write-Host $_ }
Write-Host "Sanitized results: $resultPath"
Read-Host 'Press Enter to close this window'
if ($testPassed) { exit 0 }
exit 10
