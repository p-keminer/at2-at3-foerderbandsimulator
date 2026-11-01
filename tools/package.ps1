param([string]$Destination = 'output\AT2AT3-Offline')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$destinationPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $Destination))
if (Test-Path -LiteralPath $destinationPath) { throw 'Paketordner existiert bereits. Für jeden Build einen neuen Ordner wählen.' }
$binaryRoot = Join-Path $projectRoot 'src\Plant.Wpf\bin\Release\net48'
$required = @('AT2AT3.Simulator.exe','AT2AT3.Simulator.exe.config','Plant.Core.dll','Plant.Adapter.dll')
foreach ($file in $required) { if (!(Test-Path -LiteralPath (Join-Path $binaryRoot $file))) { throw "Builddatei fehlt: $file" } }
[IO.Directory]::CreateDirectory((Join-Path $destinationPath 'config')) | Out-Null
foreach ($file in $required) { Copy-Item -LiteralPath (Join-Path $binaryRoot $file) -Destination (Join-Path $destinationPath $file) }
foreach ($config in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'config') -Filter '*.json' -File | Where-Object { $_.Name -notlike '*.local.json' }) {
    Copy-Item -LiteralPath $config.FullName -Destination (Join-Path $destinationPath ('config\'+$config.Name))
}
foreach ($file in @('README.md','THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $destinationPath
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination (Join-Path $destinationPath 'docs') -Recurse
if (Test-Path -LiteralPath (Join-Path $projectRoot 'LICENSE')) { Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $destinationPath }
$licenseSource = Join-Path $projectRoot 'licenses'
if (Test-Path -LiteralPath $licenseSource) { Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $destinationPath 'licenses') -Recurse }
$zip = $destinationPath + '.zip'
if (Test-Path -LiteralPath $zip) { throw 'ZIP existiert bereits.' }
Compress-Archive -LiteralPath $destinationPath -DestinationPath $zip
$digest = Get-FileHash -LiteralPath $zip -Algorithm SHA256
($digest.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip)) | Set-Content -LiteralPath ($zip+'.sha256') -Encoding ascii
Write-Output "Offlinepaket: $zip"
