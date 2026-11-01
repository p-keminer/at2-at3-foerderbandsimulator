param(
  [Parameter(Mandatory = $true)][uint32]$Partition,
  [Parameter(Mandatory = $true)][string]$Report,
  [string]$Map = (Join-Path $PSScriptRoot '..\..\config\io-map.AT2_Kopplung.partial.json'),
  [switch]$FreigabeRuntime,
  [switch]$FreigabeCpuSchritte,
  [switch]$TiaMinimalBestaetigt
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Invoke-BoundedProcess.ps1')
if (!$FreigabeRuntime -or !$FreigabeCpuSchritte -or !$TiaMinimalBestaetigt) {
  throw 'GESPERRT: separate menschliche Livefreigabe und geprüfter TIA-Minimalstand erforderlich. Alle drei Bestätigungsschalter müssen bewusst angegeben werden.'
}
if ($Partition -ge 65535) { throw 'Bestätigte P-Partition erforderlich; kein Zykluskontrollpunkt.' }
$probeExe = Join-Path $PSScriptRoot 'bin\Release\net48\AT2AT3.V5.LiveProbe.exe'
if (!(Test-Path -LiteralPath $probeExe -PathType Leaf)) { throw 'Zuerst build.ps1 -SiemensCompile ausführen.' }
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
  '--map', $probeMap, '--partition', $Partition.ToString(), '--report', $probeReport)
# Dedicated child process with a retained handle. No CPU process is supervised.
$run = Invoke-BoundedProbeProcess -FilePath $probeExe -Arguments $probeArguments `
  -WorkingDirectory (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).ProviderPath `
  -StdoutPath $stdoutPath -StderrPath $stderrPath
$code = if ($run.HardTimeout) { 3 } elseif (!$run.ProcessExited -or $null -eq $run.NativeExitCode -or !$run.OutputCaptured) { 4 } else { [int]$run.NativeExitCode }
@{ utc = [DateTime]::UtcNow.ToString('o'); processId = $run.ProcessId; processExited = $run.ProcessExited;
  exitCode = $code; nativeExitCode = $run.NativeExitCode; hardTimeout = $run.HardTimeout; hardTimeoutSeconds = 30;
  outputCaptured = $run.OutputCaptured; captureError = $run.CaptureError; killError = $run.KillError;
  cleanupConfirmedByWatchdog = $false; reportExists = (Test-Path -LiteralPath $probeReport);
  note = 'CPU state unverified. Hard timeout: native cleanup unconfirmed. No automatic CPU resume. Functional result/cleanup only from child report.'
} | ConvertTo-Json | Set-Content -LiteralPath $watchdogPath -Encoding UTF8
Get-Content -LiteralPath $stdoutPath
if ((Get-Item -LiteralPath $stderrPath).Length -gt 0) { Get-Content -LiteralPath $stderrPath }
if ($code -ne 0) { throw "Minimaltest fehlgeschlagen oder hart begrenzt (Exitcode $code). Bericht/Logs: $probeReport" }
Write-Output "Minimaltest beendet. Ergebnis: $probeReport; Watchdog: $watchdogPath. CPU-Zustand separat prüfen."
