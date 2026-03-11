# ================================================================
#  APP STATE  (extracted from Settings.ps1 - Sprint 2)
# ================================================================

$script:AppState = $null
$script:_AppStatePublishing = $false

function Initialize-AppState {
  if ($script:AppState -is [hashtable]) { return }

  $defaults = [ordered]@{
    RunState               = 'Idle'
    CancelRequested        = $false
    OperationInProgress    = $false
    HeartbeatFrame         = 0
    CurrentPhase           = 'Bereit'
    QuickTone              = 'idle'
    lastReport             = $null
    OperationJsonlPath     = $null
    LogNeedsFullRefresh    = $false
    UiPumpTick             = 0
    SessionCheckpointState = $null
    UseAggressiveJunk      = $false
    ModuleLoadError        = $null
    LogMaxLines            = 2000
    LogTrimTo              = 1500
    LogTrimBatchSize       = 20
    LogDrainBatchSize      = 60
    LOG_BUFFER_MAX         = 5000
    OperationJsonlMaxBytes = 5242880
    OperationJsonlKeepFiles = 10
    UiTelemetryMaxBytes    = 5242880
    UiTelemetryKeepFiles   = 7
    UiPumpUseTimerPolling  = $true
    UiPumpPollIntervalMs   = 50
    DatStreamThresholdBytes = 20971520
    DatIndexCacheEnabled = $true
    GameKeyCacheMaxEntries = 50000
    ScanSqliteThreshold = 10000
    ParallelClassifyThreshold = 2000
    ParallelHashThreshold = 200
    HtmlReportStreamingThresholdRows = 100000
    HtmlReportFlushEveryRows = 2000
    DatAutoDownloadOnTabVisit = $false
    UILocale = 'de'
    ConversionBackupEnabled = $true
    ConversionBackupRetentionDays = 7
    DatXmlMaxCharactersInDocument = 524288000
  }

  $script:AppState = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in $defaults.GetEnumerator()) {
    $script:AppState[$entry.Key] = $entry.Value
    Set-Variable -Scope Script -Name $entry.Key -Value $entry.Value -Force
  }

  if (-not (Get-Variable -Scope Script -Name AppStoreHistory -ErrorAction SilentlyContinue)) {
    $script:AppStoreHistory = New-Object System.Collections.Generic.List[object]
  }
  if (-not (Get-Variable -Scope Script -Name AppStoreRedo -ErrorAction SilentlyContinue)) {
    $script:AppStoreRedo = New-Object System.Collections.Generic.List[object]
  }
}

function Get-AppStateValue {
  param(
    [Parameter(Mandatory=$true)][string]$Key,
    [object]$Default = $null
  )

  Initialize-AppState
  if ($script:AppState.ContainsKey($Key)) { return $script:AppState[$Key] }
  $script:AppState[$Key] = $Default
  Set-Variable -Scope Script -Name $Key -Value $Default -Force
  return $Default
}

function Set-AppStateValue {
  param(
    [Parameter(Mandatory=$true)][string]$Key,
    [Parameter()][AllowNull()]$Value,
    [string]$Reason = '',
    [switch]$SyncLegacy
  )

  if (-not $script:AppState) { Initialize-AppState }

  # TD-007: Type validation via AppStateSchema (if loaded)
  if (Get-Command Assert-AppStateValue -ErrorAction SilentlyContinue) {
    try { $Value = Assert-AppStateValue -Key $Key -Value $Value } catch {
      Write-Warning ('[AppState] Type validation failed for key ''{0}'': {1}' -f $Key, $_.Exception.Message)
    }
  }

  $blockedKeys = @('AppState','AppStoreHistory','ErrorActionPreference','SETTINGS_PATH','_AppStatePublishing')
  if ($blockedKeys -contains $Key) {
    Write-Warning ('[AppState] Blocked key rejected: {0}' -f $Key)
    return
  }

  $script:AppState[$Key] = $Value
  Set-Variable -Scope Script -Name $Key -Value $Value -Force

  # Observer-Pattern: Publish AppState-Änderung über EventBus
  if (-not $script:_AppStatePublishing) {
    $script:_AppStatePublishing = $true
    try {
      if (Get-Command Publish-RomEvent -ErrorAction SilentlyContinue) {
        try {
          Publish-RomEvent -Topic 'AppState.Changed' -Data @{ Key = $Key; Value = $Value; Reason = $Reason } -Source 'AppState' -ContinueOnError
        } catch {
          Write-Verbose ('[AppState] EventBus publish failed for key ''{0}'': {1}' -f $Key, $_.Exception.Message)
        }
      }
    } finally {
      $script:_AppStatePublishing = $false
    }
  }

  return $Value
}

function Get-AppStore {
  <# Returns an immutable snapshot copy of current AppState. #>
  Initialize-AppState
  return (Copy-ObjectDeep -Value $script:AppState)
}

function Set-AppStore {
  <# Applies key/value patch to AppState and records delta history. #>
  param(
    [Parameter(Mandatory=$true)][hashtable]$Patch,
    [string]$Reason = 'update'
  )

  Initialize-AppState

  # Build delta: only store changed keys with old/new values
  $delta = [ordered]@{}
  foreach ($entry in $Patch.GetEnumerator()) {
    $key = [string]$entry.Key
    $oldVal = $script:AppState[$key]
    $newVal = $entry.Value
    $delta[$key] = [pscustomobject]@{ Old = $oldVal; New = $newVal }
  }

  # Apply patch
  foreach ($entry in $Patch.GetEnumerator()) {
    [void](Set-AppStateValue -Key ([string]$entry.Key) -Value $entry.Value)
  }

  if (-not $script:AppStoreHistory) {
    $script:AppStoreHistory = New-Object System.Collections.Generic.List[object]
  }
  [void]$script:AppStoreHistory.Add([pscustomobject]@{
    TimeUtc = (Get-Date).ToUniversalTime().ToString('o')
    Reason = [string]$Reason
    Delta = $delta
  })
  if ($script:AppStoreHistory.Count -gt 100) {
    $script:AppStoreHistory.RemoveAt(0)
  }

  if ($script:AppStoreRedo) { $script:AppStoreRedo.Clear() }

  return (Get-AppStore)
}

function Watch-AppStoreChange {
  <# Subscribes to AppState changes via EventBus and returns subscription id. #>
  param([Parameter(Mandatory=$true)][scriptblock]$Handler)

  if (-not (Get-Command Register-RomEventSubscriber -ErrorAction SilentlyContinue)) {
    throw 'EventBus nicht verfügbar: Register-RomEventSubscriber fehlt.'
  }

  return (Register-RomEventSubscriber -Topic 'AppState.Changed' -Handler $Handler)
}

function Invoke-AppStoreHistoryStep {
  <# Shared implementation for delta-based undo/redo history traversal. #>
  param(
    [Parameter(Mandatory=$true)][ValidateSet('Undo','Redo')][string]$Direction
  )

  Initialize-AppState

  if ($Direction -eq 'Undo') {
    if (-not $script:AppStoreHistory -or $script:AppStoreHistory.Count -eq 0) { return $false }
    $entry = $script:AppStoreHistory[$script:AppStoreHistory.Count - 1]
    $script:AppStoreHistory.RemoveAt($script:AppStoreHistory.Count - 1)
    if (-not $script:AppStoreRedo) { $script:AppStoreRedo = New-Object System.Collections.Generic.List[object] }
    [void]$script:AppStoreRedo.Add($entry)

    # Delta-based: restore Old value for each changed key
    if ($entry.PSObject.Properties.Name -contains 'Delta' -and $entry.Delta) {
      foreach ($k in @($entry.Delta.Keys)) {
        [void](Set-AppStateValue -Key ([string]$k) -Value $entry.Delta[$k].Old)
      }
    } elseif ($entry.PSObject.Properties.Name -contains 'Before' -and $entry.Before) {
      # Legacy full-snapshot fallback
      foreach ($k in @($entry.Before.Keys)) {
        [void](Set-AppStateValue -Key ([string]$k) -Value $entry.Before[$k])
      }
    }
    return $true
  }

  # Redo branch
  if (-not $script:AppStoreRedo -or $script:AppStoreRedo.Count -eq 0) { return $false }
  $entry = $script:AppStoreRedo[$script:AppStoreRedo.Count - 1]
  $script:AppStoreRedo.RemoveAt($script:AppStoreRedo.Count - 1)
  if (-not $script:AppStoreHistory) { $script:AppStoreHistory = New-Object System.Collections.Generic.List[object] }
  [void]$script:AppStoreHistory.Add($entry)

  # Delta-based: apply New value for each changed key
  if ($entry.PSObject.Properties.Name -contains 'Delta' -and $entry.Delta) {
    foreach ($k in @($entry.Delta.Keys)) {
      [void](Set-AppStateValue -Key ([string]$k) -Value $entry.Delta[$k].New)
    }
  } elseif ($entry.PSObject.Properties.Name -contains 'After' -and $entry.After) {
    # Legacy full-snapshot fallback
    foreach ($k in @($entry.After.Keys)) {
      [void](Set-AppStateValue -Key ([string]$k) -Value $entry.After[$k])
    }
  }
  return $true
}

function Undo-AppStore {
  <# Restores previous AppStore snapshot, if available. #>
  return (Invoke-AppStoreHistoryStep -Direction 'Undo')
}

function Redo-AppStore {
  <# Re-applies last undone AppStore snapshot, if available. #>
  return (Invoke-AppStoreHistoryStep -Direction 'Redo')
}

function Save-AppStoreRecovery {
  <# Persists current AppStore snapshot for crash recovery. #>
  param([string]$Path = $script:APPSTORE_RECOVERY_PATH)

  Initialize-AppState
  Assert-DirectoryExists -Path (Split-Path -Parent $Path)

  $payload = [ordered]@{
    schemaVersion = 'appstore-recovery-v1'
    savedUtc = (Get-Date).ToUniversalTime().ToString('o')
    state = Get-AppStore
  }

  $tmpPath = $Path + '.tmp'
  Write-JsonFile -Path $tmpPath -Data $payload -Depth 12
  Move-Item -LiteralPath $tmpPath -Destination $Path -Force
  return $Path
}

function Restore-AppStoreRecovery {
  <# Restores AppStore snapshot from recovery file if present. #>
  param([string]$Path = $script:APPSTORE_RECOVERY_PATH)

  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
  try {
    $raw = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($raw)) { return $false }
    $payload = $raw | ConvertFrom-Json -ErrorAction Stop
    if (-not $payload -or -not $payload.state) { return $false }

    $state = if ($payload.state -is [hashtable]) {
      $payload.state
    } else {
      $tmp = [ordered]@{}
      foreach ($prop in $payload.state.PSObject.Properties) { $tmp[[string]$prop.Name] = $prop.Value }
      $tmp
    }

    [void](Set-AppStore -Patch $state -Reason 'recovery')
    return $true
  } catch {
    return $false
  }
}

function Test-AppRunStateTransition {
  param(
    [Parameter(Mandatory=$true)][string]$From,
    [Parameter(Mandatory=$true)][string]$To
  )

  $fromState = [string]$From
  $toState = [string]$To

  if ($fromState -eq $toState) { return $true }

  $allowed = @{
    'Idle'      = @('Starting')
    'Starting'  = @('Running','Failed','Canceled')
    'Running'   = @('Canceling','Completed','Failed','Canceled')
    'Canceling' = @('Canceled','Failed')
    'Completed' = @('Idle')
    'Failed'    = @('Idle')
    'Canceled'  = @('Idle')
  }

  if (-not $allowed.ContainsKey($fromState)) { return $false }
  return ($allowed[$fromState] -contains $toState)
}

function Get-AppRunState {
  return [string](Get-AppStateValue -Key 'RunState' -Default 'Idle')
}

function Set-AppRunState {
  param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('Idle','Starting','Running','Canceling','Completed','Failed','Canceled')]
    [string]$State,
    [switch]$Force
  )

  $current = [string](Get-AppRunState)
  if ($Force -or (Test-AppRunStateTransition -From $current -To $State)) {
    [void](Set-AppStateValue -Key 'RunState' -Value $State)
    return $true
  }

  throw ("Ungültiger RunState-Übergang: {0} -> {1}" -f $current, $State)
}

# --- Background operation support ---
$script:_BackgroundCancelEvent = $null
$script:_BackgroundLogQueue    = $null

function Set-BackgroundCancelEvent {
  <# Sets a ManualResetEventSlim for background-runspace cancellation.
     Test-CancelRequested checks this before AppState. #>
  param([System.Threading.ManualResetEventSlim]$Event)
  $script:_BackgroundCancelEvent = $Event
}

function Set-BackgroundLogQueue {
  <# Sets a ConcurrentQueue[string] for background-runspace log forwarding. #>
  param($Queue)
  $script:_BackgroundLogQueue = $Queue
}

function Invoke-OperationHeartbeat {
  <# Separated UI heartbeat logic from cancel-checking.
     Call this periodically during long-running operations to keep the
     UI responsive without coupling cancel-detection to UI concerns. #>

  if (-not (Get-Variable -Name UiPumpTick -Scope Script -ErrorAction SilentlyContinue)) { $script:UiPumpTick = 0 }

  [void]($script:UiPumpTick++)
  $isOperationInProgress = [bool](Get-AppStateValue -Key 'OperationInProgress' -Default $false)
  if (-not $isOperationInProgress) { return }

  if ($script:UiPumpTick % 250 -eq 0) {
    if (Get-Command Invoke-UiPump -ErrorAction SilentlyContinue) {
      Invoke-UiPump
    }
  }
  if ($script:UiPumpTick % 500 -eq 0) {
    $frame = [int](Get-AppStateValue -Key 'HeartbeatFrame' -Default 0)
    [void](Set-AppStateValue -Key 'HeartbeatFrame' -Value (($frame + 1) % 3))
    if (Get-Command Update-QuickStatus -ErrorAction SilentlyContinue) { Update-QuickStatus }
  }
}

function Test-CancelRequested {
  <# Pure cancel-detection: checks background event and AppState flag.
     Calls Invoke-OperationHeartbeat for UI responsiveness but the two
     concerns are now independently testable and mockable. #>

  # Background cancel event (shared ManualResetEventSlim from background runspace)
  if ($script:_BackgroundCancelEvent -and $script:_BackgroundCancelEvent.IsSet) {
    throw [System.OperationCanceledException]::new("Abgebrochen")
  }

  Initialize-AppState

  # Keep UI responsive during operations (decoupled heartbeat)
  Invoke-OperationHeartbeat

  $stateCancelRequested = [bool](Get-AppStateValue -Key 'CancelRequested' -Default $false)

  if ($stateCancelRequested) {
    throw [System.OperationCanceledException]::new("Abgebrochen")
  }
}
