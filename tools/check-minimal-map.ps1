param(
  [string]$Map = (Join-Path $PSScriptRoot '..\config\io-map.AT2_Kopplung.partial.json'),
  [string]$Report = (Join-Path $PSScriptRoot '..\docs\evidence\minimal-map-check.json')
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Mit Windows PowerShell (.NET Framework) aufrufen: powershell.exe -NoProfile -File tools/check-minimal-map.ps1' }
$root = Split-Path -Parent $PSScriptRoot
# ONLY our managed libraries. No reference to or loading of any Siemens assembly.
Add-Type -Path (Join-Path $root 'src\Plant.Core\bin\Release\net48\Plant.Core.dll')
Add-Type -Path (Join-Path $root 'src\Plant.Adapter\bin\Release\net48\Plant.Adapter.dll')
$sourcePath = (Resolve-Path -LiteralPath $Map).ProviderPath
# Invoke the already compiled, public generic JSON reader in OUR Core library.
$reader = [Plant.Core.Json].GetMethod('Read').MakeGenericMethod([Plant.Adapter.SignalMap])
$source = $reader.Invoke($null, @([IO.File]::ReadAllText($sourcePath)))
$minimal = [Plant.Adapter.MinimalProbeMap]::FromConfirmedSignals($source)
$fullErrors = $source.Validate($true)
$siemensLoaded = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -like 'Siemens*' }).Count -ne 0
if ($siemensLoaded) { throw 'Unerwartete Siemensassembly im reinen Map-Pruefprozess.' }
@{ source = $sourcePath; sha256 = (Get-FileHash -LiteralPath $sourcePath).Hash;
  minimalValid = $true; instanceName = $minimal.InstanceName;
  sensorLeftHigh = $minimal.SensorLeftHigh; lampGreen = $minimal.LampGreen;
  fullProfileValid = ($fullErrors.Count -eq 0); fullProfileErrors = @($fullErrors);
  siemensAssemblyLoaded = $false; note = 'Echte Mapvalidierung mit eigenen kompilierten Klassen; kein Siemens-Livetest.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Report -Encoding UTF8
Write-Output "Minimalmap gueltig: $($minimal.InstanceName), I0.0 / Q0.0. Vollprofilfehler: $($fullErrors.Count). Kein Siemens geladen. Bericht: $Report"
