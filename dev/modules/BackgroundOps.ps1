# ================================================================
#  BackgroundOps.ps1 - Background runspace helpers (PERF-01/02)
#  Runs long-running operations in a background PowerShell runspace
#  while keeping the GUI responsive via event-driven wait + Invoke-UiPump.
# ================================================================

# --- Generic infrastructure ---

function Start-BackgroundRunspace {
  <# Creates a background PowerShell runspace with the RomCleanup module
     and shared communication objects (ConcurrentQueue, ManualResetEvent). #>
  param([string]$ModulePath)

  if (-not $ModulePath) {
    $ModulePath = Join-Path $script:_RomCleanupModuleRoot 'RomCleanup.psm1'
  }

  $logQueue    = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
  $logSignal   = [System.Threading.AutoResetEvent]::new($false)
  $cancelEvent = [System.Threading.ManualResetEventSlim]::new($false)
  $maxQueuedLogEntries = 50000

  $ps = [System.Management.Automation.PowerShell]::Create()
  $runspace = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace()
  $runspace.Open()
  $ps.Runspace = $runspace

  $runspace.SessionStateProxy.SetVariable('_BgLogQueue',    $logQueue)
  $runspace.SessionStateProxy.SetVariable('_BgLogSignal',   $logSignal)
  $runspace.SessionStateProxy.SetVariable('_BgCancelEvent', $cancelEvent)
  $runspace.SessionStateProxy.SetVariable('_BgModulePath',  $ModulePath)
  $runspace.SessionStateProxy.SetVariable('_BgMaxLogEntries', $maxQueuedLogEntries)

  # Set working directory to project root so reports land beside the project
  $projectRoot = Split-Path -Parent (Split-Path -Parent $ModulePath)
  if ($projectRoot -and (Test-Path -LiteralPath $projectRoot -PathType Container)) {
    $runspace.SessionStateProxy.SetVariable('_BgProjectRoot', $projectRoot)
  }

  if (Get-Command Publish-RomEvent -ErrorAction SilentlyContinue) {
    try {
      Publish-RomEvent -Topic 'background.runspace.started' -Source 'BackgroundOps' -Data @{
        modulePath = [string]$ModulePath
      } -ContinueOnError | Out-Null
    } catch { }
  }

  return @{
    PS          = $ps
    Runspace    = $runspace
    LogQueue    = $logQueue
    LogSignal   = $logSignal
    CancelEvent = $cancelEvent
    ModulePath  = $ModulePath
  }
}


# --- Dedupe ---

function Start-BackgroundDedupe {
  <# Launches Invoke-RegionDedupe in a background PowerShell runspace.
     Returns a job handle (hashtable with Runspace + PowerShell instance). #>
  param(
    [Parameter(Mandatory=$true)][hashtable]$DedupeParams,
    [string]$ModulePath
  )

  $bg = Start-BackgroundRunspace -ModulePath $ModulePath
  $bg.Runspace.SessionStateProxy.SetVariable('_BgDedupeParams', $DedupeParams)

  $scriptBody = @'
# Load full module in background runspace
Import-Module $_BgModulePath -Force -DisableNameChecking

# Set working directory to project root (so reports land in project/reports/)
if ($_BgProjectRoot) { Set-Location -LiteralPath $_BgProjectRoot }

# Wire cancel event into module (Set-BackgroundCancelEvent is in Settings.ps1)
Set-BackgroundCancelEvent -Event $_BgCancelEvent

# Wire log queue into module
Set-BackgroundLogQueue -Queue $_BgLogQueue

# Create log callback that enqueues timestamped messages
$bgLog = {
  param([string]$msg)
  $drop = $null
  while ($_BgLogQueue.Count -ge $_BgMaxLogEntries) {
    [void]$_BgLogQueue.TryDequeue([ref]$drop)
  }
  $_BgLogQueue.Enqueue($msg)
  if ($_BgLogSignal) { try { [void]$_BgLogSignal.Set() } catch { } }
}

# Build parameter splat from the hashtable
$p = $_BgDedupeParams

$preferOrder = @('EU','US','WORLD','JP')
if ($p.ContainsKey('PreferOrder') -and $p.PreferOrder) {
  $preferOrder = @($p.PreferOrder)
} elseif ($p.ContainsKey('Prefer') -and $p.Prefer) {
  $preferOrder = @($p.Prefer)
}

$includeExtensions = $null
if ($p.ContainsKey('IncludeExtensions') -and $p.IncludeExtensions) {
  $includeExtensions = @($p.IncludeExtensions)
} elseif ($p.ContainsKey('Extensions') -and $p.Extensions) {
  $includeExtensions = @($p.Extensions)
}

$removeJunk = $true
if ($p.ContainsKey('RemoveJunk')) {
  $removeJunk = [bool]$p.RemoveJunk
}

$aliasEditionKeying = $false
if ($p.ContainsKey('AliasEditionKeying')) {
  $aliasEditionKeying = [bool]$p.AliasEditionKeying
} elseif ($p.ContainsKey('AliasKeying')) {
  $aliasEditionKeying = [bool]$p.AliasKeying
}

$generateReportsInDryRun = $true
if ($p.ContainsKey('GenerateReportsInDryRun')) {
  $generateReportsInDryRun = [bool]$p.GenerateReportsInDryRun
}

$convertChecked = $false
if ($p.ContainsKey('ConvertChecked')) {
  $convertChecked = [bool]$p.ConvertChecked
}

$toolOverrides = $null
if ($p.ContainsKey('ToolOverrides') -and $p.ToolOverrides) {
  $toolOverrides = $p.ToolOverrides
} else {
  $toolOverrides = @{}
  if ($p.ContainsKey('ToolChdman') -and -not [string]::IsNullOrWhiteSpace([string]$p.ToolChdman)) { $toolOverrides['chdman'] = [string]$p.ToolChdman }
  if ($p.ContainsKey('ToolDolphin') -and -not [string]::IsNullOrWhiteSpace([string]$p.ToolDolphin)) { $toolOverrides['dolphintool'] = [string]$p.ToolDolphin }
  if ($p.ContainsKey('Tool7z') -and -not [string]::IsNullOrWhiteSpace([string]$p.Tool7z)) { $toolOverrides['7z'] = [string]$p.Tool7z }
  if ($p.ContainsKey('ToolPsxtract') -and -not [string]::IsNullOrWhiteSpace([string]$p.ToolPsxtract)) { $toolOverrides['psxtract'] = [string]$p.ToolPsxtract }
  if ($p.ContainsKey('ToolCiso') -and -not [string]::IsNullOrWhiteSpace([string]$p.ToolCiso)) { $toolOverrides['ciso'] = [string]$p.ToolCiso }
  if ($toolOverrides.Count -eq 0) { $toolOverrides = $null }
}

$rootsCount = @($p.Roots).Count
& $bgLog ("BG: Dedupe-Start (Mode={0}, Roots={1}, DAT={2})" -f [string]$p.Mode, $rootsCount, [bool]$p.UseDat)

$dedupeArgs = @{
  Roots                      = $p.Roots
  Mode                       = $p.Mode
  PreferOrder                = $preferOrder
  TrashRoot                  = $p.TrashRoot
  AuditRoot                  = $p.AuditRoot
  IncludeExtensions          = $includeExtensions
  RemoveJunk                 = $removeJunk
  AggressiveJunk             = [bool]($p.ContainsKey('AggressiveJunk')        -and $p.AggressiveJunk)
  AliasEditionKeying         = $aliasEditionKeying
  ConsoleFilter              = $p.ConsoleFilter
  JpOnlyForSelectedConsoles  = [bool]($p.ContainsKey('JpOnlyForSelectedConsoles') -and $p.JpOnlyForSelectedConsoles)
  JpKeepConsoles             = $p.JpKeepConsoles
  SeparateBios               = [bool]($p.ContainsKey('SeparateBios')          -and $p.SeparateBios)
  GenerateReportsInDryRun    = $generateReportsInDryRun
  UseDat                     = [bool]($p.ContainsKey('UseDat')                -and $p.UseDat)
  DatRoot                    = [string]$(if ($p.ContainsKey('DatRoot'))        { $p.DatRoot }    else { '' })
  DatHashType                = [string]$(if ($p.ContainsKey('DatHashType'))    { $p.DatHashType } else { '' })
  CrcVerifyScan              = [bool]($p.ContainsKey('CrcVerifyScan')         -and $p.CrcVerifyScan)
  DatFallback                = [bool]($p.ContainsKey('DatFallback')           -and $p.DatFallback)
  DatMap                     = $p.DatMap
  ToolOverrides              = $toolOverrides
  ConsoleSortUnknownReasons  = if ($p.ContainsKey('ConsoleSortUnknownReasons') -and $p.ConsoleSortUnknownReasons -is [hashtable]) { $p.ConsoleSortUnknownReasons } else { $null }
  Log                        = $bgLog
  RequireConfirmMove         = $false
  ConvertChecked             = $convertChecked
  Use1G1R                    = [bool]($p.ContainsKey('Use1G1R')               -and $p.Use1G1R)
  ManualWinnerOverrides      = if ($p.ContainsKey('ManualWinnerOverrides') -and $p.ManualWinnerOverrides -is [hashtable]) { $p.ManualWinnerOverrides } else { $null }
  ExcludedCandidatePaths     = if ($p.ContainsKey('ExcludedCandidatePaths') -and $p.ExcludedCandidatePaths) { @($p.ExcludedCandidatePaths) } else { @() }
}

# Apply custom alias map if provided
if ($p.CustomAliasMapText) {
  Set-CustomAliasMapText -Text $p.CustomAliasMapText | Out-Null
}

# Apply region rule overrides if provided
if ($p.RegionRulesOverride) {
  $ro = $p.RegionRulesOverride
  Set-RegionRulesOverride -OrderedText $ro.OrderedText -TwoLetterText $ro.TwoLetterText -LangPattern $ro.LangPattern | Out-Null
}

# OnDatHash progress → enqueue as log messages
if ($p.UseDat) {
  $dedupeArgs['OnDatHash'] = {
    param([int]$current, [int]$total, [string]$path)
    if ($current % 50 -eq 0 -or $current -eq $total) {
      $drop = $null
      while ($_BgLogQueue.Count -ge $_BgMaxLogEntries) {
        [void]$_BgLogQueue.TryDequeue([ref]$drop)
      }
      $_BgLogQueue.Enqueue("__DATHASH__:$current/$total")
      if ($_BgLogSignal) { try { [void]$_BgLogSignal.Set() } catch { } }
    }
  }
}

# ── Console Sort (pre-dedupe, Move-only) ──
$sortConsole = [bool]($p.ContainsKey('SortConsole') -and $p.SortConsole)
if ($sortConsole -and [string]$p.Mode -eq 'Move') {
  & $bgLog 'BG: Konsolen-Sortierung gestartet...'
  try {
    $sortResult = Invoke-RunSortService `
      -Enabled $true `
      -Mode 'Move' `
      -Roots $p.Roots `
      -Extensions $(if ($includeExtensions) { $includeExtensions } else { @('.chd','.iso','.gcm','.gcz','.rvz','.wbfs','.zip','.7z','.img','.bin','.cue','.gdi','.cso','.rvz','.nsp','.xci') }) `
      -UseDat ([bool]($p.ContainsKey('UseDat') -and $p.UseDat)) `
      -DatRoot ([string]$(if ($p.ContainsKey('DatRoot')) { $p.DatRoot } else { '' })) `
      -DatHashType ([string]$(if ($p.ContainsKey('DatHashType')) { $p.DatHashType } else { 'SHA1' })) `
      -DatMap $(if ($p.ContainsKey('DatMap')) { $p.DatMap } else { $null }) `
      -ToolOverrides $toolOverrides `
      -Log $bgLog
    if ($sortResult -and $sortResult.Value) {
      $dedupeArgs['ConsoleSortUnknownReasons'] = $sortResult.Value
    }
    & $bgLog 'BG: Konsolen-Sortierung abgeschlossen.'
  } catch {
    & $bgLog ("BG: Konsolen-Sortierung Fehler: {0}" -f $_.Exception.Message)
  }
}

$result = Invoke-RunDedupeService -Parameters $dedupeArgs

# ── Auto folder-level dedupe (PS3/DOS/AMIGA etc.) ──
try {
  $folderDedupeResult = Invoke-RunFolderDedupeService `
    -Roots $p.Roots `
    -Mode ([string]$p.Mode) `
    -DupeRoot $(if ($p.ContainsKey('Ps3DupesRoot') -and -not [string]::IsNullOrWhiteSpace([string]$p.Ps3DupesRoot)) { [string]$p.Ps3DupesRoot } else { '' }) `
    -Log $bgLog

  if ($result -and $folderDedupeResult) {
    $result | Add-Member -NotePropertyName FolderDedupeResult -NotePropertyValue $folderDedupeResult -Force
  }
} catch {
  & $bgLog ("BG: Auto folder-dedupe error: {0}" -f $_.Exception.Message)
}

& $bgLog "BG: Dedupe-Ende"
return $result
'@

  [void]$bg.PS.AddScript($scriptBody)
  $async = $bg.PS.BeginInvoke()

  return [pscustomobject]@{
    PS          = $bg.PS
    Runspace    = $bg.Runspace
    Handle      = $async
    LogQueue    = $bg.LogQueue
    LogSignal   = $bg.LogSignal
    CancelEvent = $bg.CancelEvent
    Completed   = $false
    Result      = $null
    Error       = $null
  }
}
