param(
  [Parameter(Mandatory=$true)][string]$Report,
  [string]$Map = (Join-Path $PSScriptRoot '..\..\config\io-map.AT2_Kopplung.partial.json'),
  [string]$Profile = (Join-Path $PSScriptRoot '..\..\config\digital-cycle.test.json'),
  [string]$Plant = (Join-Path $PSScriptRoot '..\..\config\plant.digital-test.json'),
  [ValidateRange(1,50000)][int]$Steps = 12,
  [ValidateRange(0.001,60)][double]$MaxVirtualSeconds = 1,
  [ValidateRange(5,300)][int]$WallSeconds = 15,
  [switch]$FreigabeRuntime, [switch]$FreigabeCpuSchritte, [switch]$TiaDigitalBestaetigt,
  [switch]$BewegungFreigegeben, [switch]$VollstaendigerDurchlauf
)
$ErrorActionPreference = 'Stop'
if (!$FreigabeRuntime -or !$FreigabeCpuSchritte -or !$TiaDigitalBestaetigt) { throw 'Runtime, CPU-Schritte und TIA-Digitalstand müssen ausdrücklich freigegeben sein.' }
if ($VollstaendigerDurchlauf -and !$BewegungFreigegeben) { throw 'Vollständiger Durchlauf benötigt Bewegungsfreigabe.' }
. (Join-Path $PSScriptRoot 'Invoke-BoundedProcess.ps1')
$reportPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Report)
if ([IO.Path]::GetExtension($reportPath) -ne '.json') { throw 'JSON-Bericht erforderlich.' }
foreach ($suffix in @('', '.stdout.txt', '.stderr.txt', '.watchdog.json')) { if (Test-Path -LiteralPath ($reportPath+$suffix)) { throw 'Bericht existiert; neuen Namen wählen.' } }
$probeArgs = @('--freigabe-runtime','--freigabe-cpu-schritte','--digital-plant','--confirm-tia-digital','--cycle-control-point','--confirm-ob1-automatic',
  '--map',(Resolve-Path -LiteralPath $Map).ProviderPath,'--profile',(Resolve-Path -LiteralPath $Profile).ProviderPath,
  '--plant',(Resolve-Path -LiteralPath $Plant).ProviderPath,'--report',$reportPath,'--steps',$Steps.ToString(),
  '--max-virtual-seconds',$MaxVirtualSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),'--wall-seconds',$WallSeconds.ToString())
if ($BewegungFreigegeben) { $probeArgs += '--allow-movement' }
if ($VollstaendigerDurchlauf) { $probeArgs += '--require-transport' }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportPath)) | Out-Null
$run = Invoke-BoundedProbeProcess -FilePath (Join-Path $PSScriptRoot 'bin\Release\net48\AT2AT3.V5.LiveProbe.exe') `
  -WorkingDirectory (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).ProviderPath -Arguments $probeArgs `
  -StdoutPath ($reportPath+'.stdout.txt') -StderrPath ($reportPath+'.stderr.txt') -TimeoutMilliseconds (($WallSeconds+10)*1000)
$run | ConvertTo-Json | Set-Content -LiteralPath ($reportPath+'.watchdog.json') -Encoding utf8
Get-Content -LiteralPath ($reportPath+'.stdout.txt') -Encoding utf8
Get-Content -LiteralPath ($reportPath+'.stderr.txt') -Encoding utf8
if ($run.HardTimeout -or !$run.ProcessExited -or !$run.OutputCaptured -or $null -eq $run.NativeExitCode -or $run.NativeExitCode -ne 0) {
  throw "Digitaltest fehlgeschlagen/unbestätigt. CPU-Modus separat prüfen; kein automatischer Reset. Bericht: $reportPath"
}
