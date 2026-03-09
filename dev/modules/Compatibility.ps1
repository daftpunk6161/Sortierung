$script:_UiPumpDeprecationWarned = $false

function Get-RomCleanupVersion {
  <# Returns the project version from a single source of truth. #>
  return '1.0.0'
}

function ConvertFrom-CSVList {
  param([Parameter(ValueFromPipeline = $true)]$InputObject)

  if ($null -eq $InputObject) { return @() }

  $parts = [string]$InputObject -split '(?:,|;|\r|\n|`r|`n)+'
  return @(
    $parts |
      ForEach-Object { [string]$_ } |
      ForEach-Object { $_.Trim() } |
      Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
  )
}

function Update-QuickStatus {
  param(
    [string]$Phase,
    [string]$Tone
  )

  if (-not [string]::IsNullOrWhiteSpace($Tone)) {
    Set-QuickTone -Tone $Tone
  }
  if (-not [string]::IsNullOrWhiteSpace($Phase)) {
    Set-QuickPhase -Phase $Phase
  }
}

function Invoke-UiPump {
  # BUG-050: DEPRECATED — implements forbidden DoEvents pattern (copilot-instructions.md).
  # Reentrance risk: UI events may fire during Dispatcher.Invoke pump.
  # Replace call sites with proper Dispatcher.BeginInvoke or Timer-based patterns.
  # TODO: Remove in v2.0 migration.
  if (-not $script:_UiPumpDeprecationWarned) {
    $script:_UiPumpDeprecationWarned = $true
    Write-Warning '[Compatibility] Invoke-UiPump ist veraltet (DoEvents-Pattern). Bitte durch Timer/Dispatcher ersetzen.'
  }
  try {
    if ([System.Threading.Thread]::CurrentThread.ApartmentState -eq [System.Threading.ApartmentState]::STA) {
      try {
        $dispatcher = [System.Windows.Threading.Dispatcher]::CurrentDispatcher
        if ($dispatcher) {
          [void]$dispatcher.Invoke([System.Windows.Threading.DispatcherPriority]::Background, [Action]{})
        }
      } catch { }
    }
  } catch { }

  Start-Sleep -Milliseconds 1
}

function Set-QuickTone {
  param(
    [Parameter(Mandatory)][string]$Tone,
    [string]$Phase
  )

  $normalized = [string]$Tone
  if ([string]::IsNullOrWhiteSpace($normalized)) { $normalized = 'idle' }
  $normalized = $normalized.Trim().ToLowerInvariant()
  if ($normalized -notin @('idle','success','warning','error')) { $normalized = 'idle' }

  if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
    [void](Set-AppStateValue -Key 'QuickTone' -Value $normalized)
  }

  if (-not [string]::IsNullOrWhiteSpace($Phase)) {
    Set-QuickPhase -Phase $Phase
  }
}

function Set-QuickPhase {
  param([Parameter(Mandatory)][string]$Phase)

  $phaseText = [string]$Phase
  if ([string]::IsNullOrWhiteSpace($phaseText)) { $phaseText = 'Bereit' }

  if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
    [void](Set-AppStateValue -Key 'CurrentPhase' -Value $phaseText)
  }

  $mode = 'DryRun'
  try {
    if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
      $appMode = [string](Get-AppStateValue -Key 'OperationMode' -Default '')
      if ($appMode -match '^(DryRun|Move)$') {
        $mode = $appMode
      }
    }
  } catch { }

  if ($mode -ne 'Move') {
    try {
      if (Get-Variable -Name UIControls -Scope Script -ErrorAction SilentlyContinue) {
        $ui = Get-Variable -Name UIControls -Scope Script -ValueOnly -ErrorAction SilentlyContinue
        if ($ui -and $ui.PSObject.Properties['RbMove'] -and $ui.RbMove -and $ui.RbMove.PSObject.Properties['Checked'] -and $ui.RbMove.Checked) {
          $mode = 'Move'
        }
      }
    } catch { }
  }

  $tone = 'idle'
  try {
    if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
      $tone = [string](Get-AppStateValue -Key 'QuickTone' -Default 'idle')
      if ([string]::IsNullOrWhiteSpace($tone)) { $tone = 'idle' }
    }
  } catch { }

  try {
    $reportsDir = Join-Path (Get-Location).Path 'reports'
    Assert-DirectoryExists -Path $reportsDir

    $payload = [ordered]@{
      schemaVersion = 'session-checkpoint-v1'
      timestampUtc  = (Get-Date).ToUniversalTime().ToString('o')
      phase         = $phaseText
      tone          = $tone
      mode          = $mode
    }

    $path = Join-Path $reportsDir 'session-checkpoint-latest.json'
    Write-JsonFile -Path $path -Data $payload -Depth 4
  } catch { }
}

function Invoke-PreflightRun {
  param(
    [string[]]$Roots,
    [string[]]$Exts,
    [bool]$UseDat = $false,
    [string]$DatRoot,
    [hashtable]$DatMap,
    [bool]$DoConvert = $false,
    [hashtable]$ToolOverrides,
    [string]$AuditRoot,
    [scriptblock]$Log
  )

  $validRoots = @($Roots | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) -and (Test-Path -LiteralPath $_ -PathType Container) })
  if ($validRoots.Count -eq 0) {
    if ($Log) { & $Log 'Preflight blockiert: Kein gültiger Root-Pfad gefunden.' }
    return $false
  }

  $moveBlocklist = @()
  if (Get-Command Get-MovePathBlocklist -ErrorAction SilentlyContinue) {
    try { $moveBlocklist = @(Get-MovePathBlocklist) } catch { $moveBlocklist = @() }
  }

  foreach ($root in $validRoots) {
    $rootPath = [string]$root
    if ([string]::IsNullOrWhiteSpace($rootPath)) { continue }

    $normalized = $rootPath
    try {
      if (Get-Command Resolve-RootPath -ErrorAction SilentlyContinue) {
        $normalized = Resolve-RootPath -Path $rootPath
      } else {
        $normalized = [System.IO.Path]::GetFullPath($rootPath).TrimEnd('\\','/')
      }
    } catch {
      $normalized = $rootPath
    }

    if ($normalized -match '^[a-zA-Z]:\\?$') {
      if ($Log) { & $Log ("Preflight blockiert: Root darf nicht auf Laufwerks-Root zeigen: {0}" -f $normalized) }
      return $false
    }

    if ($moveBlocklist.Count -gt 0 -and (Get-Command Test-PathBlockedByBlocklist -ErrorAction SilentlyContinue)) {
      try {
        if (Test-PathBlockedByBlocklist -Path $normalized -Blocklist $moveBlocklist) {
          if ($Log) { & $Log ("Preflight blockiert: Root liegt in geschütztem Pfadbereich: {0}" -f $normalized) }
          return $false
        }
      } catch { }
    }
  }

  if ($UseDat) {
    $hasDatRoot = (-not [string]::IsNullOrWhiteSpace($DatRoot)) -and (Test-Path -LiteralPath $DatRoot -PathType Container)
    $hasDatMap = $DatMap -and ($DatMap.Count -gt 0)
    if (-not $hasDatRoot -and -not $hasDatMap) {
      if ($Log) { & $Log 'Preflight blockiert: DAT aktiviert, aber kein gültiger DAT-Pfad oder DatMap vorhanden.' }
      return $false
    }
  }

  return $true
}