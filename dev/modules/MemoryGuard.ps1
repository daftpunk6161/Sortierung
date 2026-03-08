<#
.SYNOPSIS
  Runtime memory budget guard with backpressure support.
.DESCRIPTION
  Provides Test-MemoryBudget, Get-MemoryPressure, and Invoke-MemoryBackpressure
  for monitoring and reacting to memory pressure during long-running operations.
  Designed to be called periodically from hot paths (Dedupe, Convert, Scan).
#>

$script:MemoryBudgetConfig = $null

function Initialize-MemoryBudget {
  <#
  .SYNOPSIS  Sets up the memory budget guard with configurable thresholds.
  #>
  param(
    [int]$SoftLimitMB = 800,
    [int]$HardLimitMB = 1500,
    [int]$CheckIntervalMs = 5000
  )

  [System.GC]::Collect()
  [System.GC]::WaitForPendingFinalizers()

  $process = [System.Diagnostics.Process]::GetCurrentProcess()

  $script:MemoryBudgetConfig = @{
    SoftLimitMB       = $SoftLimitMB
    HardLimitMB       = $HardLimitMB
    CheckIntervalMs   = $CheckIntervalMs
    BaselineBytes     = [long]$process.WorkingSet64
    BaselineManagedMB = [Math]::Round([System.GC]::GetTotalMemory($false) / 1MB, 1)
    LastCheckAt       = [System.Diagnostics.Stopwatch]::StartNew()
    GcTriggered       = 0
    BackpressureCount = 0
    PeakWorkingSetMB  = [Math]::Round($process.WorkingSet64 / 1MB, 1)
  }

  return $script:MemoryBudgetConfig
}

function Get-MemoryPressure {
  <#
  .SYNOPSIS  Returns current memory usage relative to budget.
  .OUTPUTS   PSCustomObject with CurrentMB, DeltaMB, SoftLimitMB, HardLimitMB, Pressure (None/Soft/Hard).
  #>

  if (-not $script:MemoryBudgetConfig) {
    Initialize-MemoryBudget | Out-Null
  }

  $process = [System.Diagnostics.Process]::GetCurrentProcess()
  $currentBytes = [long]$process.WorkingSet64
  $currentMB = [Math]::Round($currentBytes / 1MB, 1)
  $managedMB = [Math]::Round([System.GC]::GetTotalMemory($false) / 1MB, 1)
  $deltaMB   = [Math]::Round(($currentBytes - $script:MemoryBudgetConfig.BaselineBytes) / 1MB, 1)

  if ($currentMB -gt $script:MemoryBudgetConfig.PeakWorkingSetMB) {
    $script:MemoryBudgetConfig.PeakWorkingSetMB = $currentMB
  }

  $pressure = 'None'
  if ($currentMB -ge $script:MemoryBudgetConfig.HardLimitMB) {
    $pressure = 'Hard'
  } elseif ($currentMB -ge $script:MemoryBudgetConfig.SoftLimitMB) {
    $pressure = 'Soft'
  }

  return [pscustomobject]@{
    CurrentMB   = $currentMB
    ManagedMB   = $managedMB
    DeltaMB     = $deltaMB
    BaselineMB  = [Math]::Round($script:MemoryBudgetConfig.BaselineBytes / 1MB, 1)
    PeakMB      = $script:MemoryBudgetConfig.PeakWorkingSetMB
    SoftLimitMB = $script:MemoryBudgetConfig.SoftLimitMB
    HardLimitMB = $script:MemoryBudgetConfig.HardLimitMB
    Pressure    = $pressure
    GcTriggered = $script:MemoryBudgetConfig.GcTriggered
    BackpressureCount = $script:MemoryBudgetConfig.BackpressureCount
  }
}

function Test-MemoryBudget {
  <#
  .SYNOPSIS  Quick check if memory is within budget. Returns $true if OK, $false if over soft limit.
  .DESCRIPTION
    Only performs actual measurement if CheckIntervalMs has elapsed since last check.
    Triggers GC on soft limit, returns $false on hard limit.
  #>
  param(
    [scriptblock]$Log = $null
  )

  if (-not $script:MemoryBudgetConfig) { return $true }

  # Throttle checks
  if ($script:MemoryBudgetConfig.LastCheckAt.ElapsedMilliseconds -lt $script:MemoryBudgetConfig.CheckIntervalMs) {
    return $true
  }
  $script:MemoryBudgetConfig.LastCheckAt.Restart()

  $pressure = Get-MemoryPressure

  if ($pressure.Pressure -eq 'Hard') {
    $script:MemoryBudgetConfig.GcTriggered++
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()

    if ($Log) {
      & $Log ("[MemoryGuard] HARD limit reached: {0:N0} MB / {1:N0} MB - GC triggered (#{2})" -f $pressure.CurrentMB, $pressure.HardLimitMB, $script:MemoryBudgetConfig.GcTriggered)
    }
    return $false
  }

  if ($pressure.Pressure -eq 'Soft') {
    $script:MemoryBudgetConfig.GcTriggered++
    [System.GC]::Collect()

    if ($Log) {
      & $Log ("[MemoryGuard] Soft limit: {0:N0} MB / {1:N0} MB - GC hint (#{2})" -f $pressure.CurrentMB, $pressure.SoftLimitMB, $script:MemoryBudgetConfig.GcTriggered)
    }
    return $true  # Soft = still OK but GC triggered
  }

  return $true
}

function Invoke-MemoryBackpressure {
  <#
  .SYNOPSIS  Applies backpressure when memory exceeds hard limit.
  .DESCRIPTION
    Triggers GC and optionally pauses (Start-Sleep) to allow memory to settle.
    Returns the pressure level after backpressure action.
  #>
  param(
    [int]$PauseMs = 500,
    [int]$MaxRetries = 3,
    [scriptblock]$Log = $null
  )

  if (-not $script:MemoryBudgetConfig) { return 'None' }

  $pressure = Get-MemoryPressure
  if ($pressure.Pressure -eq 'None') { return 'None' }

  $script:MemoryBudgetConfig.BackpressureCount++

  for ($retry = 0; $retry -lt $MaxRetries; $retry++) {
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
    [System.GC]::Collect()

    if ($PauseMs -gt 0) {
      Start-Sleep -Milliseconds $PauseMs
    }

    $pressure = Get-MemoryPressure
    if ($pressure.Pressure -eq 'None') {
      if ($Log) {
        & $Log ("[MemoryGuard] Backpressure resolved after {0} retries - {1:N0} MB" -f ($retry + 1), $pressure.CurrentMB)
      }
      return 'None'
    }
  }

  if ($Log) {
    & $Log ("[MemoryGuard] Backpressure persists after {0} retries - {1:N0} MB (hard: {2:N0} MB)" -f $MaxRetries, $pressure.CurrentMB, $pressure.HardLimitMB)
  }
  return $pressure.Pressure
}

function Get-MemoryBudgetSummary {
  <#
  .SYNOPSIS  Returns a summary of memory budget usage for reporting.
  #>

  if (-not $script:MemoryBudgetConfig) {
    return [pscustomobject]@{
      Initialized = $false
      Message     = 'MemoryBudget not initialized'
    }
  }

  $pressure = Get-MemoryPressure

  return [pscustomobject]@{
    Initialized       = $true
    CurrentMB         = $pressure.CurrentMB
    ManagedMB         = $pressure.ManagedMB
    PeakMB            = $pressure.PeakMB
    BaselineMB        = $pressure.BaselineMB
    DeltaMB           = $pressure.DeltaMB
    SoftLimitMB       = $pressure.SoftLimitMB
    HardLimitMB       = $pressure.HardLimitMB
    Pressure          = $pressure.Pressure
    GcTriggeredCount  = $script:MemoryBudgetConfig.GcTriggered
    BackpressureCount = $script:MemoryBudgetConfig.BackpressureCount
  }
}
