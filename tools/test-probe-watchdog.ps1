param([string]$Evidence = (Join-Path $PSScriptRoot '..\docs\evidence\watchdog-regression.json'))
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$fixture = Join-Path $PSScriptRoot 'ProcessFixture\bin\Release\net48\SiemensFree.ProcessFixture.exe'
if (!(Test-Path -LiteralPath $fixture)) { throw 'Build tools/ProcessFixture/ProcessFixture.csproj first.' }
. (Join-Path $PSScriptRoot 'LiveProbe\Invoke-BoundedProcess.ps1')
$caseRoot = Join-Path $projectRoot ('artifacts\watchdog regression ' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($caseRoot) | Out-Null
$cases = New-Object 'System.Collections.Generic.List[object]'
$script:checkCount = 0
function Check([bool]$ok, [string]$message) { $script:checkCount++; if (!$ok) { throw $message } }
function Case([string]$name, [scriptblock]$body) {
  $errorText = $null
  try { & $body } catch { $errorText = $_.Exception.ToString() }
  $cases.Add([pscustomobject]@{name=$name; passed=($null -eq $errorText); error=$errorText})
  Write-Output ((@('FAIL','PASS')[[int]($null -eq $errorText)]) + ' ' + $name)
}
function RunFixture([string]$name, [string[]]$childArguments, [int]$timeout = 3000) {
  Invoke-BoundedProbeProcess -FilePath $fixture -Arguments $childArguments -WorkingDirectory $caseRoot `
    -StdoutPath (Join-Path $caseRoot ($name + '.stdout')) -StderrPath (Join-Path $caseRoot ($name + '.stderr')) -TimeoutMilliseconds $timeout
}
Case 'W01 immediate exit codes remain numeric (0, 1, 7, 23; repeated)' {
  foreach ($number in @(0, 1, 7, 23, 0, 7, 1, 0)) {
    $r = RunFixture ('exit' + [Guid]::NewGuid().ToString('N')) @('--exit-code', $number.ToString())
    Check ($r.ProcessExited -and !$r.HardTimeout -and $null -ne $r.NativeExitCode -and $r.NativeExitCode -eq $number) 'Lost/incorrect child exit code'
    Check $r.OutputCaptured 'Output not drained'
  }
}
Case 'W02 argument boundaries survive spaces, quotes, empty strings, trailing backslashes' {
  $expected = @('--echo', 'two words', 'x"y', '', 'C:\path with spaces\', 'a\"b')
  $r = RunFixture 'arguments' $expected
  $lines = @(Get-Content -LiteralPath (Join-Path $caseRoot 'arguments.stdout') -Encoding UTF8 | Where-Object {$_ -match '^ARG\d+:'})
  Check ($lines.Count -eq $expected.Count) 'Argument count changed'
  for ($i=0; $i -lt $expected.Count; $i++) {
    $actual = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($lines[$i] -split ':', 2)[1]))
    Check ($actual -ceq $expected[$i]) ('Argument changed at ' + $i)
  }
  Check ($r.NativeExitCode -eq 0) 'Fixture failed'
}
Case 'W03 both redirected pipes drain without deadlock; UTF-8 preserved' {
  $r = RunFixture 'pipes' @('--bulk-lines', '1000')
  Check ($r.NativeExitCode -eq 0 -and $r.OutputCaptured -and !$r.HardTimeout) 'Pipe deadlock/incomplete capture'
  Check (([IO.File]::ReadAllText((Join-Path $caseRoot 'pipes.stdout'))).Contains('Prüfung äöü')) 'stdout encoding changed'
  Check (([IO.File]::ReadAllText((Join-Path $caseRoot 'pipes.stderr'))).Contains('Fehlerprüfung äöü')) 'stderr encoding changed'
}
Case 'W04 timeout terminates only the deliberately sleeping fixture child' {
  $clock = [Diagnostics.Stopwatch]::StartNew()
  $r = RunFixture 'timeout' @('--delay-ms', '5000') 100
  Check ($r.HardTimeout -and $r.ProcessExited) 'Timeout did not terminate fixture'
  Check ($clock.ElapsedMilliseconds -lt 4500) 'Fixture was not bounded'
}
Case 'W05 real wrapper with fake binary handles exit 0/1 and PS/.NET cwd divergence' {
  # Isolated copy: the genuine LiveProbe binary is NEVER used or executed.
  $wrapper = Join-Path $caseRoot 'tools\LiveProbe'
  $bin = Join-Path $wrapper 'bin\Release\net48'
  [IO.Directory]::CreateDirectory($bin) | Out-Null
  [IO.Directory]::CreateDirectory((Join-Path $caseRoot 'config')) | Out-Null
  Copy-Item -LiteralPath $fixture -Destination (Join-Path $bin 'AT2AT3.V5.LiveProbe.exe')
  foreach ($name in @('Start-MinimalProbe.ps1', 'Invoke-BoundedProcess.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('LiveProbe\' + $name)) -Destination (Join-Path $wrapper $name)
  }
  $previousLocation = Get-Location
  $previousDirectory = [Environment]::CurrentDirectory
  try {
    Set-Location -LiteralPath $caseRoot
    [Environment]::CurrentDirectory = [IO.Path]::GetPathRoot($caseRoot)
    foreach ($name in @('pass', 'fail')) {
      [IO.File]::WriteAllText((Join-Path $caseRoot ('config\' + $name + '.json')), '{}')
      $report = '.\reports\' + $name + ' one.json'
      $exception = $null
      try { & (Join-Path $wrapper 'Start-MinimalProbe.ps1') -Partition 0 -Map ('.\config\' + $name + '.json') `
        -Report $report -FreigabeRuntime -FreigabeCpuSchritte -TiaMinimalBestaetigt | Out-Null }
      catch { $exception = $_.Exception.Message }
      $expectedCode = if ($name -eq 'pass') { 0 } else { 1 }
      $path = Join-Path $caseRoot ('reports\' + $name + ' one.json')
      $result = [IO.File]::ReadAllText($path) | ConvertFrom-Json
      $watchdog = [IO.File]::ReadAllText($path + '.watchdog.json') | ConvertFrom-Json
      Check ($result.fixture -and $result.cwd -eq $caseRoot) 'Wrong binary or child cwd'
      Check ($watchdog.exitCode -eq $expectedCode -and $watchdog.nativeExitCode -eq $expectedCode) 'Wrapper lost exit code'
      Check ($watchdog.outputCaptured -and !$watchdog.hardTimeout) 'Wrapper capture failed'
      if ($name -eq 'pass') { Check ($null -eq $exception) 'Wrapper rejected real exit 0' }
      else { Check ($exception -match 'Exitcode 1') 'Wrapper failure lacks concrete exit code 1' }
    }
  }
  finally { Set-Location $previousLocation; [Environment]::CurrentDirectory = $previousDirectory }
}
Case 'W06 explicit cycle wrapper uses only fake binary; guards, flags, exit codes and relative paths' {
  $cycleRoot = Join-Path $caseRoot 'cycle isolated'
  $wrapper = Join-Path $cycleRoot 'tools\LiveProbe'
  $bin = Join-Path $wrapper 'bin\Release\net48'
  [IO.Directory]::CreateDirectory($bin) | Out-Null
  [IO.Directory]::CreateDirectory((Join-Path $cycleRoot 'config')) | Out-Null
  Copy-Item -LiteralPath $fixture -Destination (Join-Path $bin 'AT2AT3.V5.LiveProbe.exe')
  foreach ($name in @('Start-MinimalCycleProbe.ps1', 'Invoke-BoundedProcess.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('LiveProbe\' + $name)) -Destination (Join-Path $wrapper $name)
  }
  $previousLocation = Get-Location
  $previousDirectory = [Environment]::CurrentDirectory
  try {
    Set-Location -LiteralPath $cycleRoot
    [Environment]::CurrentDirectory = [IO.Path]::GetPathRoot($cycleRoot)
    [IO.File]::WriteAllText((Join-Path $cycleRoot 'config\pass.json'), '{}')
    $blocked = $false
    try { & (Join-Path $wrapper 'Start-MinimalCycleProbe.ps1') -Report '.\blocked.json' -Map '.\config\pass.json' `
      -FreigabeRuntime -FreigabeCpuSchritte -TiaMinimalBestaetigt | Out-Null }
    catch { $blocked = $_.Exception.Message -like 'GESPERRT:*' }
    Check ($blocked -and !(Test-Path -LiteralPath '.\blocked.json')) 'Missing OB1 confirmation started child'
    foreach ($name in @('pass', 'fail')) {
      [IO.File]::WriteAllText((Join-Path $cycleRoot ('config\' + $name + '.json')), '{}')
      $exception = $null
      try { & (Join-Path $wrapper 'Start-MinimalCycleProbe.ps1') -Map ('.\config\' + $name + '.json') `
        -Report ('.\reports\' + $name + ' c.json') -FreigabeRuntime -FreigabeCpuSchritte -TiaMinimalBestaetigt -Ob1AutomatikBestaetigt | Out-Null }
      catch { $exception = $_.Exception.Message }
      $path = Join-Path $cycleRoot ('reports\' + $name + ' c.json')
      $result = [IO.File]::ReadAllText($path) | ConvertFrom-Json
      $watchdog = [IO.File]::ReadAllText($path + '.watchdog.json') | ConvertFrom-Json
      $expectedCode = if ($name -eq 'pass') { 0 } else { 1 }
      $arguments = @(Get-Content -LiteralPath ($path + '.stdout.txt') -Encoding UTF8 | Where-Object { $_ -match '^ARG\d+:' } | ForEach-Object {
        [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($_ -split ':', 2)[1]))
      })
      Check ($result.fixture -and $result.cwd -eq $cycleRoot) 'Cycle wrapper used wrong child/cwd'
      Check ($watchdog.exitCode -eq $expectedCode -and $watchdog.nativeExitCode -eq $expectedCode) 'Cycle wrapper lost exit code'
      Check ($watchdog.mode -eq 'SingleStep_C' -and $watchdog.expectedSyncPointId -eq 0) 'Observed V5 U2 cycle marker not recorded'
      Check ($watchdog.outputCaptured -and !$watchdog.hardTimeout) 'Cycle output incomplete'
      Check ($arguments -contains '--cycle-control-point' -and $arguments -contains '--confirm-ob1-automatic' -and $arguments -notcontains '--partition') 'Cycle flags/partition incorrect'
      if ($name -eq 'pass') { Check ($null -eq $exception) 'Cycle wrapper rejected exit 0' }
      else { Check ($exception -match 'Exitcode 1') 'Cycle wrapper swallowed failure' }
    }
  }
  finally { Set-Location $previousLocation; [Environment]::CurrentDirectory = $previousDirectory }
}
Case 'W07 digital wrapper: harmless binary, strict guards, C-only arguments and bounded 120/300-second settings' {
  # Isolate every executable path: this regression can never load the installed Siemens SDK.
  $digitalRoot = Join-Path $caseRoot 'digital isolated'
  $wrapper = Join-Path $digitalRoot 'tools\LiveProbe'
  $starter = Join-Path $wrapper 'Start-DigitalProbe.ps1'
  $bin = Join-Path $wrapper 'bin\Release\net48'
  [IO.Directory]::CreateDirectory($bin) | Out-Null
  [IO.Directory]::CreateDirectory((Join-Path $digitalRoot 'config')) | Out-Null
  Copy-Item -LiteralPath $fixture -Destination (Join-Path $bin 'AT2AT3.V5.LiveProbe.exe')
  foreach ($name in @('Start-DigitalProbe.ps1', 'Invoke-BoundedProcess.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('LiveProbe\' + $name)) -Destination (Join-Path $wrapper $name)
  }
  foreach ($name in @('pass map', 'fail map', 'digital profile', 'plant model', 'plant.digital-test')) {
    [IO.File]::WriteAllText((Join-Path $digitalRoot ('config\' + $name + '.json')), '{}')
  }
  $previousLocation = Get-Location
  $previousDirectory = [Environment]::CurrentDirectory
  $previousCulture = [Threading.Thread]::CurrentThread.CurrentCulture
  try {
    Set-Location -LiteralPath $digitalRoot
    [Environment]::CurrentDirectory = [IO.Path]::GetPathRoot($digitalRoot)
    [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('de-DE')
    $approved = @{ Map='.\config\pass map.json'; Profile='.\config\digital profile.json'; Plant='.\config\plant model.json';
      FreigabeRuntime=$true; FreigabeCpuSchritte=$true; TiaDigitalBestaetigt=$true }
    foreach ($missing in @('FreigabeRuntime', 'FreigabeCpuSchritte', 'TiaDigitalBestaetigt', 'BewegungFreigegeben')) {
      $call = $approved.Clone()
      $call.Report = '.\guard ' + $missing + '.json'
      if ($missing -eq 'BewegungFreigegeben') { $call.VollstaendigerDurchlauf = $true }
      else { $call.Remove($missing) }
      $blocked = $false
      try { & $starter @call | Out-Null } catch { $blocked = $true }
      Check ($blocked -and !(Test-Path -LiteralPath $call.Report) -and !(Test-Path -LiteralPath ($call.Report+'.watchdog.json'))) ('Missing approval started child: ' + $missing)
    }
    foreach ($bound in @(
      @{name='Steps'; value=0}, @{name='Steps'; value=50001},
      @{name='WallSeconds'; value=4}, @{name='WallSeconds'; value=301},
      @{name='MaxVirtualSeconds'; value=0.0009}, @{name='MaxVirtualSeconds'; value=60.001})) {
      $call = $approved.Clone(); $call[$bound.name] = $bound.value
      $call.Report = '.\bound ' + [Guid]::NewGuid().ToString('N') + '.json'
      $blocked = $false
      try { & $starter @call | Out-Null } catch { $blocked = $true }
      Check ($blocked -and !(Test-Path -LiteralPath $call.Report)) ('Out-of-range setting reached child: ' + $bound.name)
    }
    foreach ($scenario in @(
      @{name='stationary'; wall=15; steps=12; seconds=0.25; move=$false; transport=$false; code=0},
      @{name='moving 120s'; wall=120; steps=15000; seconds=15.0; move=$true; transport=$false; code=0},
      @{name='transport 300s'; wall=300; steps=50000; seconds=60.0; move=$true; transport=$true; code=0},
      @{name='failure'; wall=120; steps=12; seconds=0.25; move=$false; transport=$false; code=1})) {
      $call = $approved.Clone()
      $call.Report = '.\reports\' + $scenario.name + '.json'
      $call.Steps = $scenario.steps; $call.MaxVirtualSeconds = $scenario.seconds; $call.WallSeconds = $scenario.wall
      $call.BewegungFreigegeben = $scenario.move; $call.VollstaendigerDurchlauf = $scenario.transport
      if ($scenario.code -ne 0) { $call.Map = '.\config\fail map.json' }
      $exception = $null
      try { & $starter @call | Out-Null } catch { $exception = $_.Exception.Message }
      $path = Join-Path $digitalRoot ('reports\' + $scenario.name + '.json')
      $result = [IO.File]::ReadAllText($path) | ConvertFrom-Json
      $watchdog = [IO.File]::ReadAllText($path+'.watchdog.json') | ConvertFrom-Json
      $childArgs = @(Get-Content -LiteralPath ($path+'.stdout.txt') -Encoding UTF8 | Where-Object { $_ -match '^ARG\d+:' } | ForEach-Object {
        [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($_ -split ':', 2)[1]))
      })
      Check ($result.fixture -and $result.cwd -eq $digitalRoot) 'Digital wrapper used wrong binary/cwd'
      Check ($watchdog.ProcessExited -and $null -ne $watchdog.NativeExitCode -and $watchdog.NativeExitCode -eq $scenario.code) 'Digital wrapper lost numeric exit status'
      Check ($watchdog.OutputCaptured -and !$watchdog.HardTimeout) 'Digital output incomplete or unexpectedly timed out'
      foreach ($flag in @('--freigabe-runtime', '--freigabe-cpu-schritte', '--digital-plant', '--confirm-tia-digital', '--cycle-control-point', '--confirm-ob1-automatic')) {
        Check ($childArgs -contains $flag) ('Missing digital argument: ' + $flag)
      }
      Check ($childArgs -notcontains '--partition' -and $childArgs -notcontains '--minimal-two-signal' -and $childArgs -notcontains '--confirm-tia-minimal') 'Digital wrapper selected a partition/minimal profile'
      Check (($childArgs -contains '--allow-movement') -eq $scenario.move -and ($childArgs -contains '--require-transport') -eq $scenario.transport) 'Digital motion/transport approval changed'
      foreach ($pair in @(
        @{flag='--map'; value=(Resolve-Path -LiteralPath $call.Map).ProviderPath},
        @{flag='--profile'; value=(Resolve-Path -LiteralPath $call.Profile).ProviderPath},
        @{flag='--plant'; value=(Resolve-Path -LiteralPath $call.Plant).ProviderPath},
        @{flag='--report'; value=$path},
        @{flag='--steps'; value=$scenario.steps.ToString()},
        @{flag='--wall-seconds'; value=$scenario.wall.ToString()},
        @{flag='--max-virtual-seconds'; value=$scenario.seconds.ToString([Globalization.CultureInfo]::InvariantCulture)})) {
        $at = [Array]::IndexOf($childArgs, $pair.flag)
        Check ($at -ge 0 -and $childArgs[$at+1] -ceq $pair.value) ('Digital argument value changed: ' + $pair.flag)
      }
      if ($scenario.code -eq 0) { Check ($null -eq $exception) ('Bounded digital success rejected: ' + $scenario.name + '; ' + $exception) }
      else { Check ($null -ne $exception) 'Digital wrapper swallowed child failure' }
    }
    $defaultPlant = $approved.Clone(); $defaultPlant.Remove('Plant'); $defaultPlant.Report = '.\reports\default plant.json'
    & $starter @defaultPlant | Out-Null
    $defaultArgs = @(Get-Content -LiteralPath '.\reports\default plant.json.stdout.txt' -Encoding UTF8 | Where-Object { $_ -match '^ARG\d+:' } | ForEach-Object {
      [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($_ -split ':', 2)[1]))
    })
    $plantIndex = [Array]::IndexOf($defaultArgs, '--plant')
    Check ($plantIndex -ge 0 -and $defaultArgs[$plantIndex+1] -ceq (Join-Path $digitalRoot 'config\plant.digital-test.json')) 'Digital default reverted to an unmatched offline plant profile'
    $collision = $approved.Clone(); $collision.Report = '.\existing.json'
    [IO.File]::WriteAllText((Join-Path $digitalRoot 'existing.json.stdout.txt'), 'preserve this evidence')
    $blocked = $false
    try { & $starter @collision | Out-Null } catch { $blocked = $true }
    Check ($blocked -and !(Test-Path -LiteralPath $collision.Report) -and [IO.File]::ReadAllText((Join-Path $digitalRoot 'existing.json.stdout.txt')) -ceq 'preserve this evidence') 'Existing evidence was overwritten or child started'
  }
  finally {
    Set-Location $previousLocation
    [Environment]::CurrentDirectory = $previousDirectory
    [Threading.Thread]::CurrentThread.CurrentCulture = $previousCulture
  }
}
$failed = @($cases | Where-Object {!$_.passed}).Count
@{date='2026-11-01'; powershell=$PSVersionTable.PSVersion.ToString(); passed=($failed -eq 0);
  cases=@($cases.ToArray()); assertions=$script:checkCount; failed=$failed; fixture=$fixture; artifacts=$caseRoot;
  siemensLiveProbeExecuted=$false; note='Only our Siemens-free console fixture; isolated wrapper copy. No CPU actions.'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $Evidence -Encoding UTF8
if ($failed) { throw "$failed watchdog regression case(s) failed. Evidence: $Evidence" }
Write-Output "$($cases.Count) watchdog groups; $script:checkCount assertions passed. No Siemens code executed."
