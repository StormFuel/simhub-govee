[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet restore (Join-Path $root 'SimHub.Govee.sln') --configfile (Join-Path $root 'NuGet.Config')
dotnet build (Join-Path $root 'SimHub.Govee.sln') -c $Configuration --no-restore
& (Join-Path $root "tests\SimHub.Govee.Tests\bin\$Configuration\net48\SimHub.Govee.Tests.exe")
if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE." }
