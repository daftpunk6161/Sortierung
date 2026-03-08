function Stop-RunspaceWorkerJobsShared {
  param([System.Collections.IEnumerable]$Jobs)

  foreach ($job in @($Jobs)) {
    if (-not $job -or -not $job.PS) { continue }
    try {
      if (-not $job.Done -and $job.Handle) {
        try { $job.PS.Stop() } catch { }
      }
    } catch { }
    try { $job.PS.Dispose() } catch { }
  }
}

function Remove-RunspacePoolResourcesShared {
  param(
    [AllowNull()]$Pool,
    [AllowNull()][System.Threading.ManualResetEventSlim]$CancelEvent
  )

  if ($Pool) {
    try { $Pool.Close() } catch { }
    try { $Pool.Dispose() } catch { }
  }
  if ($CancelEvent) {
    try { $CancelEvent.Dispose() } catch { }
  }
}

function New-RunspacePoolShared {
  param(
    [Parameter(Mandatory)][int]$MinRunspaces,
    [Parameter(Mandatory)][int]$MaxRunspaces,
    [Parameter(Mandatory)]$InitialSessionState,
    $HostObject = $null
  )

  $pool = $null
  if ($HostObject) {
    $pool = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspacePool($MinRunspaces, $MaxRunspaces, $InitialSessionState, $HostObject)
  } else {
    $pool = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspacePool($MinRunspaces, $MaxRunspaces, $InitialSessionState)
  }
  $pool.Open()
  return $pool
}

function Get-WaitProcessResponsiveDefinition {
  <#
  .SYNOPSIS
    Returns the HereString definition of Wait-ProcessResponsive for injection
    into runspace InitialSessionState. Canonical single source of truth -
    replaces inline duplicates in Dedupe.ps1 and Convert.ps1.
  #>
  return @'
param([System.Diagnostics.Process]$Process, [int]$PollMs = 200)
$maxWaitMs = 300000
$timer = [System.Diagnostics.Stopwatch]::StartNew()
try {
  while (-not $Process.WaitForExit($PollMs)) {
    if ($script:_CancelEvent -and $script:_CancelEvent.IsSet) {
      try { $Process.Kill() } catch { }
      try { Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue } catch { }
      throw [System.OperationCanceledException]::new("Abbruch durch Benutzer")
    }
    if ($maxWaitMs -gt 0 -and $timer.ElapsedMilliseconds -ge $maxWaitMs) {
      try { $Process.Kill() } catch { }
      try { Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue } catch { }
      throw [System.TimeoutException]::new("Tool-Timeout nach $maxWaitMs ms")
    }
  }
} catch [System.OperationCanceledException] { throw }
catch [System.TimeoutException] { throw }
catch {
  if (-not $Process.HasExited) { try { $Process.Kill() } catch { } }
  throw
}
'@
}
