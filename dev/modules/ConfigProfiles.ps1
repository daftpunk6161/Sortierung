# ================================================================
#  CONFIG PROFILES  (extracted from Settings.ps1)
# ================================================================

function Get-ConfigProfiles {
  <# Load named profile library from disk; returns hashtable name->settings. #>
  param([string]$StorePath = $script:PROFILES_PATH)

  if (-not (Test-Path -LiteralPath $StorePath -PathType Leaf)) {
    return [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  }

  try {
    $raw = Get-Content -LiteralPath $StorePath -Raw -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($raw)) {
      return [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    }

    $payload = $raw | ConvertFrom-Json -ErrorAction Stop
    $sourceProfiles = $null
    if ($payload -and $payload.schemaVersion -eq 'romcleanup-profiles-v1' -and $payload.profiles) {
      $sourceProfiles = $payload.profiles
    } elseif ($payload -is [hashtable] -or $payload -is [pscustomobject]) {
      $sourceProfiles = $payload
    }

    $profiles = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    if (-not $sourceProfiles) { return $profiles }
    if ($sourceProfiles -is [hashtable]) {
      foreach ($entry in $sourceProfiles.GetEnumerator()) {
        $name = [string]$entry.Key
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $profiles[$name] = Copy-ObjectDeep -Value $entry.Value
      }
    } else {
      foreach ($prop in $sourceProfiles.PSObject.Properties) {
        $name = [string]$prop.Name
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $profiles[$name] = Copy-ObjectDeep -Value $prop.Value
      }
    }
    return $profiles
  } catch {
    return [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  }
}

function Set-ConfigProfiles {
  <# Persist named profile library as schema romcleanup-profiles-v1. #>
  param(
    [hashtable]$Profiles,
    [string]$StorePath = $script:PROFILES_PATH
  )

  if (-not $Profiles) {
    $Profiles = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  }

  try {
    Assert-DirectoryExists -Path (Split-Path -Parent $StorePath)

    $clean = [ordered]@{}
    foreach ($entry in $Profiles.GetEnumerator()) {
      $name = [string]$entry.Key
      if ([string]::IsNullOrWhiteSpace($name)) { continue }
      $clean[$name] = Copy-ObjectDeep -Value $entry.Value
    }

    $wrapper = [ordered]@{
      schemaVersion = 'romcleanup-profiles-v1'
      generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
      profiles = $clean
    }

    Write-JsonFile -Path $StorePath -Data $wrapper -Depth 8
  } catch {
    # F-04 FIX: Profile save failure surfaced (same policy as Set-UserSettings).
    $errMsg = ('Profil-Speichern fehlgeschlagen [{0}]: {1}' -f $StorePath, $_.Exception.Message)
    Write-Warning $errMsg
    if (Get-Command Show-ToastNotification -ErrorAction SilentlyContinue) {
      try { Show-ToastNotification -Message $errMsg -Type 'Warning' } catch { }
    }
  }
}

function Set-ConfigProfile {
  <# Create/update one named profile from settings object. #>
  param(
    [Parameter(Mandatory=$true)][string]$Name,
    [Parameter(Mandatory=$true)]$Settings,
    [string]$StorePath = $script:PROFILES_PATH
  )

  $profileName = $Name.Trim()
  if ([string]::IsNullOrWhiteSpace($profileName)) {
    throw 'Profilname darf nicht leer sein.'
  }

  $profiles = Get-ConfigProfiles -StorePath $StorePath
  $profiles[$profileName] = Copy-ObjectDeep -Value $Settings
  Set-ConfigProfiles -Profiles $profiles -StorePath $StorePath
  return $profiles[$profileName]
}

function Remove-ConfigProfile {
  <# Remove one named profile from profile library. #>
  param(
    [Parameter(Mandatory=$true)][string]$Name,
    [string]$StorePath = $script:PROFILES_PATH
  )

  $profileName = $Name.Trim()
  if ([string]::IsNullOrWhiteSpace($profileName)) { return $false }

  $profiles = Get-ConfigProfiles -StorePath $StorePath
  if (-not $profiles.ContainsKey($profileName)) { return $false }
  [void]$profiles.Remove($profileName)
  Set-ConfigProfiles -Profiles $profiles -StorePath $StorePath
  return $true
}

function Export-ConfigProfile {
  <# Export selected profile as JSON file (romcleanup-profile-v1). #>
  param(
    [Parameter(Mandatory=$true)][string]$Name,
    [Parameter(Mandatory=$true)][string]$Path,
    [string]$StorePath = $script:PROFILES_PATH
  )

  $profiles = Get-ConfigProfiles -StorePath $StorePath
  if (-not $profiles.ContainsKey($Name)) {
    throw ('Profil nicht gefunden: {0}' -f $Name)
  }

  $wrapper = [ordered]@{
    schemaVersion = 'romcleanup-profile-v1'
    exportedUtc = (Get-Date).ToUniversalTime().ToString('o')
    name = [string]$Name
    settings = Copy-ObjectDeep -Value $profiles[$Name]
  }

  Write-JsonFile -Path $Path -Data $wrapper -Depth 8
}

function Import-ConfigProfile {
  <# Import one profile file or profile library file into local profile store. #>
  param(
    [Parameter(Mandatory=$true)][string]$Path,
    [string]$StorePath = $script:PROFILES_PATH,
    [switch]$ReplaceAll
  )

  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw ('Datei nicht gefunden: {0}' -f $Path)
  }

  $raw = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
  if ([string]::IsNullOrWhiteSpace($raw)) {
    throw 'Datei ist leer.'
  }

  $payload = $raw | ConvertFrom-Json -ErrorAction Stop
  $profiles = if ($ReplaceAll) { [hashtable]::new([StringComparer]::OrdinalIgnoreCase) } else { Get-ConfigProfiles -StorePath $StorePath }

  if ($payload.schemaVersion -eq 'romcleanup-profile-v1' -and $payload.settings) {
    if (Get-Command Get-RomCleanupSchema -ErrorAction SilentlyContinue) {
      $schemaSingle = Get-RomCleanupSchema -Name 'profile-v1'
      if ($schemaSingle -and (Get-Command Test-JsonPayloadSchema -ErrorAction SilentlyContinue)) {
        $checkSingle = Test-JsonPayloadSchema -Payload $payload -Schema $schemaSingle
        if (-not $checkSingle.IsValid) { throw ('Profilschema ungültig: {0}' -f ($checkSingle.Errors -join '; ')) }
      }
    }

    $name = [string]$payload.name
    if ([string]::IsNullOrWhiteSpace($name)) { throw 'Profilname fehlt in Datei.' }
    $profiles[$name] = Copy-ObjectDeep -Value $payload.settings
  } elseif ($payload.schemaVersion -eq 'romcleanup-profiles-v1' -and $payload.profiles) {
    if (Get-Command Get-RomCleanupSchema -ErrorAction SilentlyContinue) {
      $schemaMulti = Get-RomCleanupSchema -Name 'profiles-v1'
      if ($schemaMulti -and (Get-Command Test-JsonPayloadSchema -ErrorAction SilentlyContinue)) {
        $checkMulti = Test-JsonPayloadSchema -Payload $payload -Schema $schemaMulti
        if (-not $checkMulti.IsValid) { throw ('Profileschema ungültig: {0}' -f ($checkMulti.Errors -join '; ')) }
      }
    }

    if ($payload.profiles -is [hashtable]) {
      foreach ($entry in $payload.profiles.GetEnumerator()) {
        $name = [string]$entry.Key
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $profiles[$name] = Copy-ObjectDeep -Value $entry.Value
      }
    } else {
      foreach ($prop in $payload.profiles.PSObject.Properties) {
        $name = [string]$prop.Name
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $profiles[$name] = Copy-ObjectDeep -Value $prop.Value
      }
    }
  } else {
    throw 'Unbekanntes Profil-Format.'
  }

  Set-ConfigProfiles -Profiles $profiles -StorePath $StorePath
  return $profiles
}
