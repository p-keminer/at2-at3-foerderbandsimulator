param([switch]$Test, [switch]$UiSmoke, [switch]$SiemensCompile,
  [string]$EvidenceDirectory = 'artifacts\tests', [string]$DotNetPath)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
if ($DotNetPath) { $sdk = (Resolve-Path -LiteralPath $DotNetPath).ProviderPath }
elseif (Test-Path -LiteralPath (Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe')) { $sdk = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe' }
else { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
$pathMap = '-p:PathMap=' + $PSScriptRoot + '=/_/src'
& $sdk build AT2AT3.sln -c Release --nologo $pathMap
if ($LASTEXITCODE -ne 0) { throw 'Offlinebuild fehlgeschlagen. SDK-Version in global.json beachten.' }
if ($Test) {
  & $sdk build tools\ProcessFixture\ProcessFixture.csproj -c Release --nologo $pathMap
  if ($LASTEXITCODE -ne 0) { throw 'Siemensfreie Watchdog-Fixture konnte nicht gebaut werden.' }
  & .\tests\bin\Release\net48\Plant.Tests.exe $EvidenceDirectory
  if ($LASTEXITCODE -ne 0) { throw 'Offlinetests fehlgeschlagen.' }
}
if ($UiSmoke) {
  $uiEvidence = Join-Path ($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($EvidenceDirectory)) 'ui'
  [IO.Directory]::CreateDirectory($uiEvidence) | Out-Null
  . (Join-Path $PSScriptRoot 'tools\LiveProbe\Invoke-BoundedProcess.ps1')
  $run = Invoke-BoundedProbeProcess -FilePath (Join-Path $PSScriptRoot 'src\Plant.Wpf\bin\Release\net48\AT2AT3.Simulator.exe') `
    -WorkingDirectory $PSScriptRoot -Arguments @('--smoke', $uiEvidence) `
    -StdoutPath (Join-Path $uiEvidence 'stdout.txt') -StderrPath (Join-Path $uiEvidence 'stderr.txt') -TimeoutMilliseconds 120000
  $run | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $uiEvidence 'process.json') -Encoding utf8
  if ($run.HardTimeout -or !$run.ProcessExited -or !$run.OutputCaptured -or $null -eq $run.NativeExitCode -or $run.NativeExitCode -ne 0) { throw "WPF-Smokecheck fehlgeschlagen: $uiEvidence" }
}
if ($SiemensCompile) {
  foreach ($project in @('src\Plant.Siemens.V5\Plant.Siemens.V5.csproj','tools\LiveProbe\LiveProbe.csproj','tools\RestoreDefault\RestoreDefault.csproj')) {
    & $sdk build $project -c Release --nologo $pathMap
    if ($LASTEXITCODE -ne 0) { throw "Optionaler SDK-Compilecheck fehlgeschlagen: $project" }
  }
}
