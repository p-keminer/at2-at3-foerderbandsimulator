# Process supervision only. This file has no Siemens references or API calls.
function ConvertTo-ProbeProcessArgument {
  param([AllowEmptyString()][string]$Value)
  if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
  $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
  $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
  return '"' + $escaped + '"'
}

function Invoke-BoundedProbeProcess {
  param(
    [Parameter(Mandatory = $true)][string]$FilePath,
    [string[]]$Arguments = @(),
    [Parameter(Mandatory = $true)][string]$WorkingDirectory,
    [Parameter(Mandatory = $true)][string]$StdoutPath,
    [Parameter(Mandatory = $true)][string]$StderrPath,
    # Minimal probes retain 30 s by default. Digital transport may explicitly use
    # up to 300 s work + 10 s shutdown margin; all children remain bounded.
    [ValidateRange(1, 310000)][int]$TimeoutMilliseconds = 30000
  )
  $process = New-Object System.Diagnostics.Process
  $info = New-Object System.Diagnostics.ProcessStartInfo
  $info.FileName = $FilePath
  $info.Arguments = (($Arguments | ForEach-Object { ConvertTo-ProbeProcessArgument $_ }) -join ' ')
  $info.WorkingDirectory = $WorkingDirectory
  $info.UseShellExecute = $false
  $info.CreateNoWindow = $true
  $info.RedirectStandardOutput = $true
  $info.RedirectStandardError = $true
  $info.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
  $info.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
  $process.StartInfo = $info
  try {
    # Process.Start retains our child's native handle even if it exits immediately.
    # Do not Refresh/reopen the process by PID before reading ExitCode (PS 5.1).
    if (!$process.Start()) { throw 'Probe child process could not be started.' }
    $childId = $process.Id
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $ended = $process.WaitForExit($TimeoutMilliseconds)
    $timedOut = !$ended
    $killError = $null
    if ($timedOut) {
      try { if (!$process.HasExited) { $process.Kill() } }
      catch { if (!$process.HasExited) { $killError = $_.Exception.ToString() } }
      $ended = $process.WaitForExit(2000)
    }
    $nativeCode = $null
    if ($ended) { $nativeCode = $process.ExitCode }
    $drained = $false
    $captureError = $null
    try { $drained = [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($stdoutTask, $stderrTask), 2000) }
    catch { $captureError = $_.Exception.ToString() }
    $stdout = '[stdout capture incomplete]'
    $stderr = '[stderr capture incomplete]'
    if ($stdoutTask.Status -eq [System.Threading.Tasks.TaskStatus]::RanToCompletion) { $stdout = $stdoutTask.Result }
    if ($stderrTask.Status -eq [System.Threading.Tasks.TaskStatus]::RanToCompletion) { $stderr = $stderrTask.Result }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($StdoutPath, $stdout, $utf8)
    [IO.File]::WriteAllText($StderrPath, $stderr, $utf8)
    return [pscustomobject]@{
      ProcessId = $childId; ProcessExited = $ended; NativeExitCode = $nativeCode;
      HardTimeout = $timedOut; OutputCaptured = ($drained -and $null -eq $captureError);
      KillError = $killError; CaptureError = $captureError
    }
  }
  finally { $process.Dispose() }
}
