[CmdletBinding()]
param(
    [ValidateSet('Enable', 'Disable', 'Status', 'Remove')]
    [string]$Action = 'Status',
    [string]$ProgramPath,
    [string]$RuleName = 'SimHub Govee LAN (UDP 4002)'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $ProgramPath) {
    $ProgramPath = Join-Path $projectRoot 'tools\SimHub.Govee.LanProbe\bin\Release\net48\SimHub.Govee.LanProbe.exe'
}
$ProgramPath = [IO.Path]::GetFullPath($ProgramPath)

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$rule = Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue

if ($Action -eq 'Status') {
    if (-not $rule) {
        Write-Host "Firewall rule '$RuleName' is not installed."
        exit 1
    }

    $port = $rule | Get-NetFirewallPortFilter
    $application = $rule | Get-NetFirewallApplicationFilter
    $rule | Select-Object DisplayName, Enabled, Direction, Action, Profile
    $port | Select-Object Protocol, LocalPort
    $application | Select-Object Program
    exit 0
}

if (-not $isAdministrator) {
    throw "Action $Action requires administrator privileges. Re-run PowerShell as Administrator."
}

switch ($Action) {
    'Enable' {
        if (-not (Test-Path -LiteralPath $ProgramPath -PathType Leaf)) {
            throw "Program was not found: $ProgramPath"
        }

        if ($rule) {
            $rule | Remove-NetFirewallRule
        }

        New-NetFirewallRule `
            -DisplayName $RuleName `
            -Description 'Allows only the SimHub Govee LAN process to receive device replies on Govee UDP port 4002.' `
            -Direction Inbound `
            -Action Allow `
            -Program $ProgramPath `
            -Protocol UDP `
            -LocalPort 4002 `
            -Profile Public,Private | Out-Null
        Write-Host "Enabled '$RuleName' for UDP 4002 and program: $ProgramPath"
    }
    'Disable' {
        if ($rule) {
            $rule | Disable-NetFirewallRule
            Write-Host "Disabled '$RuleName'."
        }
        else {
            Write-Host "Firewall rule '$RuleName' is not installed."
        }
    }
    'Remove' {
        if ($rule) {
            $rule | Remove-NetFirewallRule
            Write-Host "Removed '$RuleName'."
        }
        else {
            Write-Host "Firewall rule '$RuleName' is not installed."
        }
    }
}
