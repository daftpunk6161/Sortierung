# ================================================================
#  JUNK REPORT – Detaillierter Junk-Klassifikations-Report (QW-05)
#  Dependencies: Classification.ps1, rules.json
# ================================================================

function Get-JunkClassificationReason {
  <#
  .SYNOPSIS
    Ermittelt den Grund der Junk-Klassifikation fuer einen Dateinamen.
    Gibt zurueck welche Regel/Pattern zum Treffer fuehrte.
  .PARAMETER BaseName
    Dateiname ohne Extension.
  .PARAMETER AggressiveJunk
    Ob aggressive Junk-Erkennung aktiv ist.
  .PARAMETER JunkPatterns
    Optionale benutzerdefinierte Junk-Patterns (Hashtable mit Tags/Words Arrays).
  #>
  param(
    [Parameter(Mandatory)][string]$BaseName,
    [bool]$AggressiveJunk = $false,
    [hashtable]$JunkPatterns
  )

  $result = @{
    IsJunk     = $false
    Category   = 'GAME'
    JunkReason = $null
    JunkRule   = $null
    MatchedTag = $null
  }

  if ([string]::IsNullOrWhiteSpace($BaseName)) {
    return $result
  }

  # Standard-Junk-Tags (aus gaengigen DAT-Konventionen)
  $junkTags = @(
    '\((?:Beta|Beta\s*\d*)\)'
    '\((?:Proto|Prototype)\)'
    '\((?:Sample|Preview)\)'
    '\((?:Demo|Kiosk(?:\s*Demo)?)\)'
    '\((?:Homebrew|Aftermarket)\)'
    '\((?:Hack|Pirate|Bootleg)\)'
    '\((?:Unl|Unlicensed)\)'
    '\((?:Virtual Console)\)'
    '\((?:Program|Test Program)\)'
    '\((?:Competition Cart|Promo)\)'
  )

  $junkWords = @(
    '\[b\d*\]'           # Bad dump marker
    '\[h\d*\]'           # Hack marker
    '\[o\d*\]'           # Overdump
    '\[t\d*\]'           # Trainer
    '\[f\d*\]'           # Fixed
    '\[p\d*\]'           # Pirate
  )

  $aggressiveTags = @(
    '\((?:Alt|Alternate)\)'
    '\((?:Bonus Disc)\)'
  )

  $aggressiveWords = @(
    '\[!\]'  # Verified good (in aggressiv: nur beste behalten)
  )

  # Benutzerdefinierte Patterns uebernehmen
  if ($JunkPatterns) {
    if ($JunkPatterns.Tags) { $junkTags = @($JunkPatterns.Tags) }
    if ($JunkPatterns.Words) { $junkWords = @($JunkPatterns.Words) }
    if ($JunkPatterns.AggressiveTags) { $aggressiveTags = @($JunkPatterns.AggressiveTags) }
    if ($JunkPatterns.AggressiveWords) { $aggressiveWords = @($JunkPatterns.AggressiveWords) }
  }

  # Standard-Junk-Tags pruefen
  foreach ($pattern in $junkTags) {
    if ($BaseName -match $pattern) {
      $result.IsJunk = $true
      $result.Category = 'JUNK'
      $result.MatchedTag = $Matches[0]
      $result.JunkReason = "Tag: $($Matches[0])"
      $result.JunkRule = $pattern
      return $result
    }
  }

  # Standard-Junk-Words pruefen
  foreach ($pattern in $junkWords) {
    if ($BaseName -match $pattern) {
      $result.IsJunk = $true
      $result.Category = 'JUNK'
      $result.MatchedTag = $Matches[0]
      $result.JunkReason = "Marker: $($Matches[0])"
      $result.JunkRule = $pattern
      return $result
    }
  }

  # Aggressive Junk-Erkennung
  if ($AggressiveJunk) {
    foreach ($pattern in $aggressiveTags) {
      if ($BaseName -match $pattern) {
        $result.IsJunk = $true
        $result.Category = 'JUNK'
        $result.MatchedTag = $Matches[0]
        $result.JunkReason = "Aggressive-Tag: $($Matches[0])"
        $result.JunkRule = $pattern
        return $result
      }
    }
    foreach ($pattern in $aggressiveWords) {
      if ($BaseName -match $pattern) {
        $result.IsJunk = $true
        $result.Category = 'JUNK'
        $result.MatchedTag = $Matches[0]
        $result.JunkReason = "Aggressive-Marker: $($Matches[0])"
        $result.JunkRule = $pattern
        return $result
      }
    }
  }

  return $result
}

function Get-JunkReport {
  <#
  .SYNOPSIS
    Erstellt einen detaillierten Junk-Report fuer eine Liste von Dateinamen.
  .PARAMETER FileNames
    Array von Dateinamen (ohne Extension).
  .PARAMETER AggressiveJunk
    Ob aggressive Junk-Erkennung aktiv ist.
  #>
  param(
    [Parameter(Mandatory)][string[]]$FileNames,
    [bool]$AggressiveJunk = $false,
    [hashtable]$JunkPatterns
  )

  $report = [System.Collections.Generic.List[hashtable]]::new()

  foreach ($name in $FileNames) {
    $classification = Get-JunkClassificationReason -BaseName $name -AggressiveJunk $AggressiveJunk -JunkPatterns $JunkPatterns
    [void]$report.Add(@{
      FileName   = $name
      IsJunk     = $classification.IsJunk
      Category   = $classification.Category
      JunkReason = $classification.JunkReason
      JunkRule   = $classification.JunkRule
      MatchedTag = $classification.MatchedTag
    })
  }

  $summary = @{
    Total     = $report.Count
    JunkCount = @($report | Where-Object { $_.IsJunk }).Count
    GameCount = @($report | Where-Object { -not $_.IsJunk }).Count
    Entries   = $report
  }

  return $summary
}
