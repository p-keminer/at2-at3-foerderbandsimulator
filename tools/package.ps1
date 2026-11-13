param([string]$Destination = 'output\AT2AT3-Simulator', [switch]$IncludeSiemensAdapter)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$destinationPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $Destination))
if (Test-Path -LiteralPath $destinationPath) { throw 'Paketordner existiert bereits. Für jeden Build einen neuen Ordner wählen.' }
$binaryRoot = Join-Path $projectRoot 'src\Plant.Wpf\bin\Release\net48'
$required = @('AT2AT3.Simulator.exe','AT2AT3.Simulator.exe.config','Plant.Core.dll','Plant.Adapter.dll')
foreach ($file in $required) { if (!(Test-Path -LiteralPath (Join-Path $binaryRoot $file))) { throw "Builddatei fehlt: $file" } }
$documents = @('README.md','ANLEITUNG.md','LICENSE','THIRD_PARTY_LICENSES.md')
$adapterFiles = @{
    'live-probe' = @{ Source='tools\LiveProbe\bin\Release\net48'; Files=@('AT2AT3.V5.LiveProbe.exe','AT2AT3.V5.LiveProbe.exe.config','Plant.Core.dll','Plant.Adapter.dll','Plant.Siemens.V5.dll') }
    'restore-default' = @{ Source='tools\RestoreDefault\bin\Release\net48'; Files=@('AT2AT3.V5.RestoreDefault.exe','AT2AT3.V5.RestoreDefault.exe.config','Plant.Core.dll') }
}
if ($IncludeSiemensAdapter) {
    foreach ($part in $adapterFiles.Values) { foreach ($file in $part.Files) {
        if (!(Test-Path -LiteralPath (Join-Path $projectRoot (Join-Path $part.Source $file)))) { throw "Adapterbuild fehlt: $file. build.ps1 -SiemensCompile ausführen." }
    } }
}
foreach ($file in ($documents + @('docs\images\offline.png'))) {
    if (!(Test-Path -LiteralPath (Join-Path $projectRoot $file) -PathType Leaf)) { throw "Paketinhalt fehlt: $file" }
}
[IO.Directory]::CreateDirectory((Join-Path $destinationPath 'config')) | Out-Null
foreach ($file in $required) { Copy-Item -LiteralPath (Join-Path $binaryRoot $file) -Destination (Join-Path $destinationPath $file) }
# Built-in profiles travel with the application; personal profiles are not published.
Copy-Item -LiteralPath (Join-Path $projectRoot 'config\profiles') -Destination (Join-Path $destinationPath 'config\profiles') -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'tia') -Destination (Join-Path $destinationPath 'tia') -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'extensions') -Destination (Join-Path $destinationPath 'extensions') -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'Start_AT1B.cmd') -Destination $destinationPath
foreach ($config in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'config') -Filter '*.json' -File | Where-Object { $_.Name -notlike '*.local.json' }) {
    Copy-Item -LiteralPath $config.FullName -Destination (Join-Path $destinationPath ('config\'+$config.Name))
}
foreach ($file in $documents) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $destinationPath
}
$imageDestination = Join-Path $destinationPath 'docs\images'
[IO.Directory]::CreateDirectory($imageDestination) | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\images\offline.png') -Destination $imageDestination
if ($IncludeSiemensAdapter) {
    foreach ($name in $adapterFiles.Keys) {
        $part = $adapterFiles[$name]; $partDestination = Join-Path $destinationPath $name
        [IO.Directory]::CreateDirectory($partDestination) | Out-Null
        foreach ($file in $part.Files) { Copy-Item -LiteralPath (Join-Path $projectRoot (Join-Path $part.Source $file)) -Destination $partDestination }
    }
}
$zip = $destinationPath + '.zip'
if (Test-Path -LiteralPath $zip) { throw 'ZIP existiert bereits.' }
Compress-Archive -LiteralPath $destinationPath -DestinationPath $zip
$digest = Get-FileHash -LiteralPath $zip -Algorithm SHA256
($digest.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip)) | Set-Content -LiteralPath ($zip+'.sha256') -Encoding ascii
Write-Output "Simulatorpaket: $zip"
