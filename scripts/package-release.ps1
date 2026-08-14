[CmdletBinding()]
param([string]$Version = '0.2.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release
if ($LASTEXITCODE -ne 0) { throw "Release validation failed with exit code $LASTEXITCODE." }
$staging = Join-Path $root "artifacts\Govee-Controller-Plugin-for-SimHub-$Version"
$zip = "$staging.zip"
if (Test-Path $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item (Join-Path $root 'src\SimHub.Govee\bin\Release\net48\SimHub.Govee.dll') $staging
Copy-Item (Join-Path $root 'README.md') $staging
Copy-Item (Join-Path $root 'LICENSE') $staging
Compress-Archive -LiteralPath $staging -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $(Split-Path -Leaf $zip)" -Encoding ASCII
Write-Host "Created $zip"
Write-Host "SHA-256: $hash"
