$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $projectRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
# Explicit developer/CI action only; never invoked by the application.
& (Join-Path $PSScriptRoot 'dotnet-install.ps1') -Version $version -Architecture x64 -InstallDir (Join-Path $projectRoot '.tools\dotnet') -NoPath
if (!(Test-Path -LiteralPath (Join-Path $projectRoot '.tools\dotnet\dotnet.exe'))) { throw 'SDK-Installation nicht bestätigt.' }
