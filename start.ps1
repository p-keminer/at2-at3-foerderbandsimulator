$ErrorActionPreference = 'Stop'
$app = Join-Path $PSScriptRoot 'src\Plant.Wpf\bin\Release\net48\AT2AT3.Simulator.exe'
if (!(Test-Path -LiteralPath $app)) { & (Join-Path $PSScriptRoot 'build.ps1') }
& $app
