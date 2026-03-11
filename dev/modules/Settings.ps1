function Resolve-RomCleanupUserDataRoot {
  $candidates = @(
    [string]$env:APPDATA,
    [string]$env:LOCALAPPDATA,
    [string][System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::ApplicationData),
    [string][System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::LocalApplicationData),
    [string][System.IO.Path]::GetTempPath()
  )

  foreach ($candidate in $candidates) {
    if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
    $trimmed = $candidate.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }
    if ($trimmed -eq '?') { continue }

    try {
      if (Test-Path -LiteralPath $trimmed -PathType Container) {
        return $trimmed
      }
    } catch {
      continue
    }
  }

  return [System.IO.Path]::GetTempPath()
}

$script:USERDATA_ROOT = Join-Path (Resolve-RomCleanupUserDataRoot) 'RomCleanupRegionDedupe'
$script:SETTINGS_PATH = Join-Path $script:USERDATA_ROOT 'settings.json'
$script:PROFILES_PATH = Join-Path $script:USERDATA_ROOT 'profiles.json'
$script:DEFAULTS_PATH = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'data\defaults.json'
$script:APPSTORE_RECOVERY_PATH = Join-Path $script:USERDATA_ROOT 'appstate-recovery.json'

function Copy-ObjectDeep {
  <# Creates a deep copy of any PS value (hashtable, pscustomobject, array, scalar).
     Canonical deep-copy helper - replaces the former Copy-ObjectDeep
     and ConvertTo-PlainObjectDeep duplicates. #>
  param([object]$Value, [int]$Depth = 0)

  if ($Depth -gt 20) { Write-Warning '[Settings] Copy-ObjectDeep: max depth exceeded'; return $Value }
  if ($null -eq $Value) { return $null }
  if ($Value -is [System.Collections.IDictionary]) {
    $copy = @{}
    foreach ($entry in ([System.Collections.IDictionary]$Value).GetEnumerator()) {
      $copy[[string]$entry.Key] = Copy-ObjectDeep -Value $entry.Value -Depth ($Depth + 1)
    }
    return $copy
  }
  # BUG SET-002: In PS 7, [pscustomobject] is an alias for [PSObject] — scalars
  # like [int], [bool], [string] also match.  Check for scalars FIRST.
  if ($Value -is [System.ValueType] -or $Value -is [string]) {
    return $Value
  }
  if ($Value -is [pscustomobject]) {
    $copy = @{}
    foreach ($prop in $Value.PSObject.Properties) {
      $copy[[string]$prop.Name] = Copy-ObjectDeep -Value $prop.Value -Depth ($Depth + 1)
    }
    return $copy
  }
  if ($Value -is [System.Collections.IEnumerable] -and -not ($Value -is [string])) {
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($item in $Value) {
      [void]$rows.Add((Copy-ObjectDeep -Value $item -Depth ($Depth + 1)))
    }
    return $rows.ToArray()
  }
  return $Value
}

function Write-SettingsWarning {
  <# Emit a warning via Write-Warning, Write-Log (if available), and toast. #>
  param([string]$Message)
  Write-Warning $Message
  if (Get-Command Write-Log -ErrorAction SilentlyContinue) {
    try { [void](Write-Log -Level Error -Message ('[F-09] Settings: {0}' -f $Message)) } catch { }
  }
  if (Get-Command Show-ToastNotification -ErrorAction SilentlyContinue) {
    try { Show-ToastNotification -Message $Message -Type 'Warning' } catch { }
  }
}

function Get-UserSettings {
  param(
    # Optional path override for testing; defaults to the module-level settings path.
    [string]$SettingsPath = $script:SETTINGS_PATH
  )
  if (-not (Test-Path -LiteralPath $SettingsPath)) { return $null }
  try {
    $raw = Get-Content -LiteralPath $SettingsPath -Raw -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($raw)) {
      # Empty file is treated as missing (no warning)
      return $null
    }
    $s = $raw | ConvertFrom-Json -ErrorAction Stop
    if (Get-Command Invoke-SettingsMigration -ErrorAction SilentlyContinue) {
      # BUG-029 FIX: Log migration errors instead of silent catch
      try { $s = Invoke-SettingsMigration -Settings $s } catch {
        Write-Warning ('Settings-Migration fehlgeschlagen: {0}' -f $_.Exception.Message)
      }
    }
    if (-not ($s -is [pscustomobject] -or $s -is [hashtable] -or $s -is [System.Collections.IDictionary])) {
      $warnMsg = ('Settings-Datei ungueltig (kein Objekt): {0}' -f $SettingsPath)
      Write-SettingsWarning $warnMsg
      return $null
    }
    try {
      if ($s -is [System.Collections.IDictionary]) {
        # After migration $s is an OrderedDictionary — sub-objects are also IDictionary, skip PSObject checks
      } else {
        if ($s.PSObject.Properties['toolPaths'] -and
            -not ($s.toolPaths -is [pscustomobject] -or $s.toolPaths -is [hashtable] -or $s.toolPaths -is [System.Collections.IDictionary])) {
          $s.toolPaths = $null
        }
        if ($s.PSObject.Properties['dat'] -and
            -not ($s.dat -is [pscustomobject] -or $s.dat -is [hashtable] -or $s.dat -is [System.Collections.IDictionary])) {
          $s.dat = $null
        }
        if ($s.PSObject.Properties['general'] -and
            -not ($s.general -is [pscustomobject] -or $s.general -is [hashtable] -or $s.general -is [System.Collections.IDictionary])) {
          $s.general = $null
        }
      }
    } catch {
      return $null
    }

    if (Get-Command Get-RomCleanupSchema -ErrorAction SilentlyContinue) {
      $schema = Get-RomCleanupSchema -Name 'settings-v1'
      if ($schema -and (Get-Command Test-JsonPayloadSchema -ErrorAction SilentlyContinue)) {
        $validation = Test-JsonPayloadSchema -Payload $s -Schema $schema
        if (-not $validation.IsValid) {
          $warnMsg = ('Settings-Datei Schema-Fehler: {0}' -f ($validation.Errors -join '; '))
          Write-SettingsWarning $warnMsg
          return $null
        }
      }
    }

    return $s
  } catch {
    # File exists but could not be read or parsed - surfaces the corruption
    $warnMsg = ('Settings-Datei korrupt oder nicht lesbar ({0}): {1}' -f $SettingsPath, $_.Exception.Message)
    Write-SettingsWarning $warnMsg
    return $null
  }
}

function Set-UserSettings {
  param([hashtable]$Settings)

  try {
    if (Get-Command Invoke-SettingsMigration -ErrorAction SilentlyContinue) {
      # BUG-029 FIX: Log migration errors in Set-UserSettings too
      try { $Settings = Invoke-SettingsMigration -Settings $Settings } catch {
        Write-Warning ('Settings-Migration fehlgeschlagen (Save): {0}' -f $_.Exception.Message)
      }
    }
    Assert-DirectoryExists -Path (Split-Path -Parent $script:SETTINGS_PATH)
    Write-JsonFile -Path $script:SETTINGS_PATH -Data $Settings -Depth 10
  } catch {
    # F-04 FIX: Settings save failure is no longer silently ignored.
    # Surface via Write-Warning so the host (CLI or test runner) sees it.
    # In GUI mode, Show-ToastNotification is called if available.
    $errMsg = ('Settings-Speichern fehlgeschlagen: {0}' -f $_.Exception.Message)
    Write-Warning $errMsg
    if (Get-Command Show-ToastNotification -ErrorAction SilentlyContinue) {
      try { Show-ToastNotification -Message $errMsg -Type 'Warning' } catch { }
    }
  }
}

function ConvertTo-SafeFileName {
  param([string]$Name)
  if ([string]::IsNullOrWhiteSpace($Name)) { return 'root' }
  $invalid = [IO.Path]::GetInvalidFileNameChars()
  $safe = $Name
  foreach ($c in $invalid) { $safe = $safe.Replace($c, '_') }
  return $safe
}
function Invoke-SettingsMigration {
  <# Applies lightweight compatible migrations to settings payloads. #>
  param([Parameter(Mandatory=$true)]$Settings)

  if (-not $Settings) { return $Settings }

  # Guard: only migrate dictionary-like objects — arrays, scalars etc. pass through unchanged
  if (-not ($Settings -is [hashtable] -or $Settings -is [System.Collections.IDictionary] -or $Settings -is [pscustomobject])) {
    return $Settings
  }
  # Extra guard: in PS 7, arrays also match [pscustomobject]; reject IEnumerable that isn't a dict
  if ($Settings -is [System.Collections.IEnumerable] -and -not ($Settings -is [System.Collections.IDictionary]) -and -not ($Settings -is [string])) {
    return $Settings
  }

  $migrated = if ($Settings -is [hashtable] -or $Settings -is [System.Collections.IDictionary]) {
    Copy-ObjectDeep -Value $Settings
  } else {
    $tmp = @{}
    foreach ($prop in $Settings.PSObject.Properties) { $tmp[[string]$prop.Name] = $prop.Value }
    $tmp
  }

  if (-not $migrated.ContainsKey('schemaVersion') -or [string]::IsNullOrWhiteSpace([string]$migrated.schemaVersion)) {
    $migrated['schemaVersion'] = 'settings-v1'
  }

  if ($migrated.ContainsKey('toolPaths') -and $migrated.toolPaths -and -not ($migrated.toolPaths -is [hashtable])) {
    $tpObj = @{}
    foreach ($prop in $migrated.toolPaths.PSObject.Properties) { $tpObj[[string]$prop.Name] = $prop.Value }
    $migrated.toolPaths = $tpObj
  }
  if ($migrated.ContainsKey('dat') -and $migrated.dat -and -not ($migrated.dat -is [hashtable])) {
    $datObj = @{}
    foreach ($prop in $migrated.dat.PSObject.Properties) { $datObj[[string]$prop.Name] = $prop.Value }
    $migrated.dat = $datObj
  }
  if ($migrated.ContainsKey('general') -and $migrated.general -and -not ($migrated.general -is [hashtable])) {
    $generalObj = @{}
    foreach ($prop in $migrated.general.PSObject.Properties) { $generalObj[[string]$prop.Name] = $prop.Value }
    $migrated.general = $generalObj
  }

  return $migrated
}

