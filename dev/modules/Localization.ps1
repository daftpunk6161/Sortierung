# ================================================================
#  LOCALIZATION - i18n string table loader
#  Provides Get-UIString for all UI text lookups.
# ================================================================

$script:UI_STRINGS = $null
$script:UI_LOCALE  = 'de'

function Initialize-Localization {
  <#
  .SYNOPSIS
    Loads the UI string table for the given locale.
    Falls back to 'de' if the requested locale file is missing.
  #>
  param([string]$Locale = 'de')

  $script:UI_LOCALE = $Locale

  # Resolve i18n directory relative to module location or workspace root
  $i18nDir = $null
  $moduleDir = if ($PSScriptRoot) { $PSScriptRoot } else { '.' }
  $candidate = Join-Path (Split-Path $moduleDir) 'data\i18n'
  if (Test-Path $candidate) { $i18nDir = $candidate }

  if (-not $i18nDir) {
    $rootGuess = (Get-Location).Path
    $candidate2 = Join-Path $rootGuess 'data\i18n'
    if (Test-Path $candidate2) { $i18nDir = $candidate2 }
  }

  if (-not $i18nDir) {
    $script:UI_STRINGS = @{}
    return
  }

  $localePath = Join-Path $i18nDir "$Locale.json"
  if (-not (Test-Path -LiteralPath $localePath -PathType Leaf)) {
    $localePath = Join-Path $i18nDir 'de.json'
  }

  if (Test-Path -LiteralPath $localePath -PathType Leaf) {
    try {
      $raw = Get-Content -LiteralPath $localePath -Raw -Encoding UTF8 -ErrorAction Stop
      $script:UI_STRINGS = @{}
      $parsed = $raw | ConvertFrom-Json -ErrorAction Stop
      foreach ($prop in $parsed.PSObject.Properties) {
        if ($prop.Name -ne '_meta') {
          $script:UI_STRINGS[$prop.Name] = [string]$prop.Value
        }
      }
    } catch {
      $script:UI_STRINGS = @{}
    }
  } else {
    $script:UI_STRINGS = @{}
  }
}

function Get-UIString {
  <#
  .SYNOPSIS
    Returns a localized UI string by key.
    Supports optional format arguments: Get-UIString 'Dat.AutoDownloadStart' 5
  .PARAMETER Key
    The i18n key, e.g. 'Start.RunButton'.
  .PARAMETER FormatArgs
    Optional arguments for string formatting via -f operator.
  #>
  param(
    [Parameter(Mandatory=$true)][string]$Key,
    [Parameter(ValueFromRemainingArguments=$true)][object[]]$FormatArgs
  )

  if ($null -eq $script:UI_STRINGS) { Initialize-Localization }

  $template = if ($script:UI_STRINGS.ContainsKey($Key)) {
    $script:UI_STRINGS[$Key]
  } else {
    # Fallback: return key itself so missing translations are visible
    $Key
  }

  if ($FormatArgs -and $FormatArgs.Count -gt 0) {
    try { return ($template -f $FormatArgs) } catch { return $template }
  }
  return $template
}

