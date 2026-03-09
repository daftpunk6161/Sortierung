# ================================================================
#  CONFIG MERGE  (extracted from Settings.ps1)
#  Config-Precedence: Default → User Settings → Profile → Environment
# ================================================================

$script:ENV_OVERRIDE_MAP = @{
  'ROM_CLEANUP_DAT_ROOT'       = 'datRoot'
  'ROM_CLEANUP_TRASH_ROOT'     = 'trashRoot'
  'ROM_CLEANUP_AUDIT_ROOT'     = 'auditRoot'
  'ROM_CLEANUP_MODE'           = 'mode'
  'ROM_CLEANUP_EXTENSIONS'     = 'extensions'
  'ROM_CLEANUP_LOG_LEVEL'      = 'logLevel'
  'ROM_CLEANUP_SORT_CONSOLE'   = 'sortConsole'
  'ROM_CLEANUP_AGGRESSIVE'     = 'aggressiveJunk'
  'ROM_CLEANUP_DRY_RUN'        = 'dryRun'
  'ROM_CLEANUP_PREFER'         = 'prefer'
  'ROM_CLEANUP_PARALLEL_HASH'  = 'parallelHashThreshold'
  'ROM_CLEANUP_PARALLEL_SCAN'  = 'parallelClassifyThreshold'
  'ROM_CLEANUP_EXPERT_MODE'    = 'expertMode'
  'ROM_CLEANUP_THEME'          = 'theme'
}

# Keys whose env values are cast to boolean (true/1/yes → $true)
$script:ENV_BOOL_KEYS = @('sortConsole','aggressiveJunk','dryRun','expertMode')
# Keys whose env values are cast to integer
$script:ENV_INT_KEYS  = @('parallelHashThreshold','parallelClassifyThreshold')

function Get-EnvironmentOverrides {
  <#
  .SYNOPSIS
    Liest Konfigurationsüberschreibungen aus Umgebungsvariablen.
  .DESCRIPTION
    Mappt ROM_CLEANUP_* Environment-Variablen auf Settings-Schlüssel.
    Wird in der Config-Precedence-Chain als höchste Ebene (vor CLI) genutzt.
  #>
  $overrides = [ordered]@{}
  foreach ($entry in $script:ENV_OVERRIDE_MAP.GetEnumerator()) {
    $envVal = [System.Environment]::GetEnvironmentVariable($entry.Key)
    if (-not [string]::IsNullOrWhiteSpace($envVal)) {
      $settingsKey = [string]$entry.Value
      if ($script:ENV_BOOL_KEYS -contains $settingsKey) {
        $overrides[$settingsKey] = ($envVal -in @('true','1','yes'))
      } elseif ($script:ENV_INT_KEYS -contains $settingsKey) {
        $parsed = 0
        if ([int]::TryParse($envVal, [ref]$parsed)) {
          $overrides[$settingsKey] = $parsed
        }
      } else {
        $overrides[$settingsKey] = $envVal
      }
    }
  }
  return $overrides
}

# ================================================================
#  CONFIG PRECEDENCE - Default → User Settings → Profile → Environment
# ================================================================

function Get-ConfigurationBaselineDefaults {
  <# Returns the hardcoded baseline defaults hashtable used by merge and diff. #>
  return [ordered]@{
    mode       = 'DryRun'
    datRoot    = ''
    trashRoot  = ''
    auditRoot  = ''
    extensions = '.zip,.7z,.rar,.chd,.iso,.rvz,.cso,.gcz,.wbfs,.nsp,.xci,.3ds,.cia'
    logLevel   = 'info'
  }
}

function script:Test-ObjHasKey {
  <# Helper: checks whether a key exists on a hashtable or PSCustomObject. #>
  param($Obj, [string]$Key)
  if ($Obj -is [System.Collections.IDictionary]) {
    return $Obj.ContainsKey($Key)
  }
  return [bool]($Obj.PSObject.Properties[$Key])
}

function Get-MergedConfiguration {
  <#
  .SYNOPSIS
    Liefert die finale Konfiguration durch Merge aller Quellen.
  .DESCRIPTION
    Precedence (aufsteigend, spätere überschreiben frühere):
      1. Defaults (hardcoded)
      2. User Settings (%APPDATA%/settings.json)
      3. Active Profile (falls gewählt)
      4. Environment Overrides (ROM_CLEANUP_* Vars)
  #>
  param(
    [string]$ProfileName,
    [hashtable]$CliOverrides
  )

  # 1) Defaults
  $merged = Get-ConfigurationBaselineDefaults

  # 1b) Optional defaults file
  $defaultsFromFile = Get-ConfigurationDefaults
  foreach ($entry in $defaultsFromFile.GetEnumerator()) {
    $merged[[string]$entry.Key] = $entry.Value
  }

  # 2) User Settings
  $userSettings = Get-UserSettings
  if ($userSettings) {
    if ((script:Test-ObjHasKey $userSettings 'general') -and $userSettings['general']) {
      $gen = $userSettings['general']
      if ($gen -is [System.Collections.IDictionary]) {
        foreach ($entry in $gen.GetEnumerator()) {
          if ($merged.Contains($entry.Key)) {
            $merged[$entry.Key] = $entry.Value
          }
        }
      } else {
        foreach ($prop in $gen.PSObject.Properties) {
          if ($merged.Contains($prop.Name)) {
            $merged[$prop.Name] = $prop.Value
          }
        }
      }
    }
    if ((script:Test-ObjHasKey $userSettings 'dat') -and $userSettings['dat']) {
      $dat = $userSettings['dat']
      if ((script:Test-ObjHasKey $dat 'datRoot') -and -not [string]::IsNullOrWhiteSpace([string]$dat['datRoot'])) {
        $merged['datRoot'] = [string]$dat['datRoot']
      } elseif ((script:Test-ObjHasKey $dat 'root') -and -not [string]::IsNullOrWhiteSpace([string]$dat['root'])) {
        $merged['datRoot'] = [string]$dat['root']
      }
    }
  }

  # 3) Profile
  if (-not [string]::IsNullOrWhiteSpace($ProfileName)) {
    $profiles = Get-ConfigProfiles
    if ($profiles -and $profiles.ContainsKey($ProfileName)) {
      $profile = $profiles[$ProfileName]
      if ($profile -is [hashtable]) {
        foreach ($entry in $profile.GetEnumerator()) {
          if ($merged.Contains($entry.Key)) { $merged[$entry.Key] = $entry.Value }
        }
      } elseif ($profile -is [pscustomobject]) {
        foreach ($prop in $profile.PSObject.Properties) {
          if ($merged.Contains($prop.Name)) { $merged[$prop.Name] = $prop.Value }
        }
      }
    }
  }

  # 4) Environment Overrides (höchste Priorität)
  $envOverrides = Get-EnvironmentOverrides
  foreach ($entry in $envOverrides.GetEnumerator()) {
    if ($merged.Contains($entry.Key)) { $merged[$entry.Key] = $entry.Value }
  }

  # 5) CLI Overrides (höchste Priorität)
  if ($CliOverrides) {
    foreach ($entry in $CliOverrides.GetEnumerator()) {
      $key = [string]$entry.Key
      if ([string]::IsNullOrWhiteSpace($key)) { continue }
      if ($merged.Contains($key)) { $merged[$key] = $entry.Value }
    }
  }

  return [pscustomobject]$merged
}

function Get-ConfigurationDefaults {
  <# Reads optional defaults.json and returns key/value defaults. #>
  $defaults = [ordered]@{}
  if (-not (Test-Path -LiteralPath $script:DEFAULTS_PATH -PathType Leaf)) { return $defaults }

  try {
    $raw = Get-Content -LiteralPath $script:DEFAULTS_PATH -Raw -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($raw)) { return $defaults }
    $payload = $raw | ConvertFrom-Json -ErrorAction Stop
    if (-not ($payload -is [pscustomobject] -or $payload -is [hashtable])) { return $defaults }
    foreach ($prop in $payload.PSObject.Properties) {
      $name = [string]$prop.Name
      if ([string]::IsNullOrWhiteSpace($name)) { continue }
      $defaults[$name] = $prop.Value
    }
  } catch { }

  return $defaults
}

function Get-ConfigurationDiff {
  <# Returns differences between effective config and defaults. #>
  param(
    [string]$ProfileName,
    [hashtable]$CliOverrides
  )

  $baseline = Get-ConfigurationBaselineDefaults
  foreach ($entry in (Get-ConfigurationDefaults).GetEnumerator()) {
    $baseline[[string]$entry.Key] = $entry.Value
  }

  $effective = Get-MergedConfiguration -ProfileName $ProfileName -CliOverrides $CliOverrides
  $rows = New-Object System.Collections.Generic.List[psobject]
  foreach ($prop in $effective.PSObject.Properties) {
    $key = [string]$prop.Name
    $effectiveValue = $prop.Value
    $baselineValue = if ($baseline.Contains($key)) { $baseline[$key] } else { $null }
    if (([string]$effectiveValue) -ne ([string]$baselineValue)) {
      [void]$rows.Add([pscustomobject]@{
        Key = $key
        DefaultValue = $baselineValue
        EffectiveValue = $effectiveValue
      })
    }
  }
  return @($rows)
}
