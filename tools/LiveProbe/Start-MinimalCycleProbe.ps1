param(
  [Parameter(Mandatory = $true)][string]$Report,
  [string]$Map = (Join-Path $PSScriptRoot '..\..\config\io-map.AT2_Kopplung.partial.json'),
  [switch]$FreigabeRuntime,
  [switch]$FreigabeCpuSchritte,
  [switch]$TiaMinimalBestaetigt,
  [switch]$Ob1AutomatikBestaetigt
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-BoundedProcess.ps1')
if (!$FreigabeRuntime -or !$FreigabeCpuSchritte -or !$TiaMinimalBestaetigt -or !$Ob1AutomatikBestaetigt) {
  throw 'GESPERRT: eigene Livefreigabe, TIA-Minimalstand und automatische OB1-E/A-Aktualisierung bestaetigen.'
}
$probeExe = Join-Path $PSScriptRoot 'bin\Release\net48\AT2AT3.V5.LiveProbe.exe'
if (!(Test-Path -LiteralPath $probeExe -PathType Leaf)) { throw 'Zuerst build.ps1 -SiemensCompile ausfuehren.' }
$probeMap = (Resolve-Path -LiteralPath $Map).ProviderPath
$probeReport = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Report)
if ([IO.Path]::GetExtension($probeReport) -ne '.json') { throw 'JSON-Berichtsdatei erforderlich.' }
$stdoutPath = $probeReport + '.stdout.txt'
$stderrPath = $probeReport + '.stderr.txt'
$watchdogPath = $probeReport + '.watchdog.json'
foreach ($probePath in @($probeReport, $stdoutPath, $stderrPath, $watchdogPath)) {
  if (Test-Path -LiteralPath $probePath) { throw "Datei existiert bereits; neuen Berichtsnamen verwenden: $probePath" }
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($probeReport)) | Out-Null
$probeArguments = @('--freigabe-runtime', '--freigabe-cpu-schritte', '--minimal-two-signal', '--confirm-tia-minimal',
  '--cycle-control-point', '--confirm-ob1-automatic', '--map', $probeMap, '--report', $probeReport)
Write-Output 'Expliziter Zweibittest: SingleStep_C, beobachtete V5-U2-Ereignis-ID 0; kein automatischer Modusfallback oder Default-Reset.'
$run = Invoke-BoundedProbeProcess -FilePath $probeExe -Arguments $probeArguments `
  -WorkingDirectory (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).ProviderPath `
  -StdoutPath $stdoutPath -StderrPath $stderrPath
$code = if ($run.HardTimeout) { 3 } elseif (!$run.ProcessExited -or $null -eq $run.NativeExitCode -or !$run.OutputCaptured) { 4 } else { [int]$run.NativeExitCode }
@{ utc = [DateTime]::UtcNow.ToString('o'); mode = 'SingleStep_C'; expectedSyncPointId = 0;
  processId = $run.ProcessId; processExited = $run.ProcessExited; exitCode = $code; nativeExitCode = $run.NativeExitCode;
  hardTimeout = $run.HardTimeout; hardTimeoutSeconds = 30; outputCaptured = $run.OutputCaptured;
  captureError = $run.CaptureError; killError = $run.KillError; cleanupConfirmedByWatchdog = $false;
  reportExists = (Test-Path -LiteralPath $probeReport); note = 'CPU state after process end unverified. No automatic resume.'
} | ConvertTo-Json | Set-Content -LiteralPath $watchdogPath -Encoding UTF8
Get-Content -LiteralPath $stdoutPath -Encoding UTF8
if ((Get-Item -LiteralPath $stderrPath).Length -gt 0) { Get-Content -LiteralPath $stderrPath -Encoding UTF8 }
if ($code -ne 0) { throw "C-Minimaltest fehlgeschlagen oder begrenzt (Exitcode $code). Bericht: $probeReport" }
Write-Output "C-Minimaltest beendet. Ergebnis: $probeReport. CPU-Zustand nach Prozessende separat pruefen."
