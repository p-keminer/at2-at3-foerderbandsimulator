param(
  [Parameter(Mandatory = $true)][string]$Report,
  [switch]$FreigabeRuntime,
  [switch]$FreigabeDefaultFortlauf
)
$ErrorActionPreference = 'Stop'
if (!$FreigabeRuntime -or !$FreigabeDefaultFortlauf) { throw 'GESPERRT: eigene Freigabe fuer API und moeglichen fortlaufenden CPU-Betrieb erforderlich.' }
. (Join-Path $PSScriptRoot '..\LiveProbe\Invoke-BoundedProcess.ps1')
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).ProviderPath
$exe = Join-Path $PSScriptRoot 'bin\Release\net48\AT2AT3.V5.RestoreDefault.exe'
$reportPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Report)
if ([IO.Path]::GetExtension($reportPath) -ne '.json') { throw 'JSON-Bericht erforderlich.' }
foreach ($path in @($reportPath, ($reportPath + '.stdout.txt'), ($reportPath + '.stderr.txt'), ($reportPath + '.watchdog.json'))) {
  if (Test-Path -LiteralPath $path) { throw "Bericht/Log existiert bereits: $path" }
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportPath)) | Out-Null
Write-Output 'Freigegebene Aktion: nur AT2_Kopplung -> Default. Die CPU kann danach fortlaufend arbeiten.'
$run = Invoke-BoundedProbeProcess -FilePath $exe -WorkingDirectory $root `
  -Arguments @('--freigabe-runtime', '--freigabe-default-fortlauf', '--report', $reportPath) `
  -StdoutPath ($reportPath + '.stdout.txt') -StderrPath ($reportPath + '.stderr.txt') -TimeoutMilliseconds 10000
$run | ConvertTo-Json | Set-Content -LiteralPath ($reportPath + '.watchdog.json') -Encoding UTF8
Get-Content -LiteralPath ($reportPath + '.stdout.txt') -Encoding UTF8
Get-Content -LiteralPath ($reportPath + '.stderr.txt') -Encoding UTF8
if ($run.HardTimeout -or !$run.ProcessExited -or !$run.OutputCaptured -or $null -eq $run.NativeExitCode -or $run.NativeExitCode -ne 0) {
  throw "Rueckstellung nicht bestaetigt. Kein automatischer Wiederholungsversuch. Bericht: $reportPath"
}
