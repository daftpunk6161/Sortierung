# ================================================================
#  QUARANTINE – ROM-Quarantaene fuer verdaechtige Dateien (MF-26)
#  Dependencies: FileOps.ps1, Classification.ps1
# ================================================================

function Test-QuarantineCandidate {
  <#
  .SYNOPSIS
    Prueft ob eine Datei ein Quarantaene-Kandidat ist.
  .PARAMETER Item
    Datei-Info als Hashtable (Format, Console, DatStatus, Category etc.).
  .PARAMETER Rules
    Optionale Quarantaene-Regeln.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Item,
    [hashtable[]]$Rules
  )

  $reasons = @()

  # Standard-Kriterien
  $console = if ($Item.ContainsKey('Console')) { $Item.Console } else { '' }
  $format = if ($Item.ContainsKey('Format')) { $Item.Format } else { '' }
  $datStatus = if ($Item.ContainsKey('DatStatus')) { $Item.DatStatus } else { '' }
  $category = if ($Item.ContainsKey('Category')) { $Item.Category } else { '' }
  $headerStatus = if ($Item.ContainsKey('HeaderStatus')) { $Item.HeaderStatus } else { '' }

  # Unbekanntes Format + unbekannte Konsole
  if ((-not $console -or $console -eq 'Unknown') -and (-not $format -or $format -eq 'Unknown')) {
    $reasons += 'UnbekannteKonsoleUndFormat'
  }

  # Kein DAT-Match + verdaechtiger Name
  if ($datStatus -eq 'NoMatch' -and $category -ne 'GAME') {
    $reasons += 'KeinDatMatchUndKeinGame'
  }

  # Header-Anomalien
  if ($headerStatus -eq 'Anomaly' -or $headerStatus -eq 'Corrupted') {
    $reasons += 'HeaderAnomalie'
  }

  # Custom Rules evaluieren
  if ($Rules) {
    foreach ($rule in $Rules) {
      if ($rule.ContainsKey('Field') -and $rule.ContainsKey('Value')) {
        $itemVal = if ($Item.ContainsKey($rule.Field)) { $Item[$rule.Field] } else { '' }
        if ("$itemVal" -eq "$($rule.Value)") {
          $reasons += "CustomRule:$($rule.Field)=$($rule.Value)"
        }
      }
    }
  }

  return @{
    IsCandidate = ($reasons.Count -gt 0)
    Reasons     = $reasons
    Item        = $Item
  }
}

function New-QuarantineAction {
  <#
  .SYNOPSIS
    Erstellt eine Quarantaene-Verschiebeaktion.
  .PARAMETER SourcePath
    Original-Pfad der Datei.
  .PARAMETER QuarantineRoot
    Quarantaene-Basisverzeichnis.
  .PARAMETER Reasons
    Gruende fuer die Quarantaene.
  .PARAMETER Mode
    DryRun oder Move.
  #>
  param(
    [Parameter(Mandatory)][string]$SourcePath,
    [Parameter(Mandatory)][string]$QuarantineRoot,
    [string[]]$Reasons = @(),
    [ValidateSet('DryRun','Move')][string]$Mode = 'DryRun'
  )

  $fileName = [System.IO.Path]::GetFileName($SourcePath)
  $timestamp = (Get-Date).ToString('yyyyMMdd')
  $targetDir = Join-Path $QuarantineRoot $timestamp
  $targetPath = Join-Path $targetDir $fileName

  return @{
    SourcePath     = $SourcePath
    TargetPath     = $targetPath
    QuarantineDir  = $targetDir
    Reasons        = $Reasons
    Mode           = $Mode
    Status         = 'Pending'
    Timestamp      = (Get-Date).ToString('o')
  }
}

function Invoke-Quarantine {
  <#
  .SYNOPSIS
    Fuehrt Quarantaene-Aktionen aus.
  .PARAMETER Actions
    Array von Quarantaene-Aktionen.
  .PARAMETER Mode
    DryRun oder Move.
  #>
  param(
    [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Actions,
    [ValidateSet('DryRun','Move')][string]$Mode = 'DryRun'
  )

  if (-not $Actions -or $Actions.Count -eq 0) {
    return @{ Processed = 0; Moved = 0; Errors = 0; Results = @() }
  }

  $results = @()
  $moved = 0
  $errors = 0

  foreach ($action in $Actions) {
    if ($Mode -eq 'DryRun') {
      $action.Status = 'DryRun'
      $results += $action
      continue
    }

    try {
      $dir = $action.QuarantineDir
      if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
      }

      if (Test-Path -LiteralPath $action.SourcePath) {
        Move-ItemSafely -Source $action.SourcePath -Dest $action.TargetPath
        $action.Status = 'Moved'
        $moved++
      } else {
        $action.Status = 'SourceMissing'
        $errors++
      }
    } catch {
      $action.Status = 'Error'
      $action['Error'] = $_.Exception.Message
      $errors++
    }

    $results += $action
  }

  return @{
    Processed = $Actions.Count
    Moved     = $moved
    Errors    = $errors
    Results   = $results
  }
}

function Get-QuarantineContents {
  <#
  .SYNOPSIS
    Listet den Inhalt des Quarantaene-Verzeichnisses.
  .PARAMETER QuarantineRoot
    Quarantaene-Basisverzeichnis.
  #>
  param(
    [Parameter(Mandatory)][string]$QuarantineRoot
  )

  if (-not (Test-Path $QuarantineRoot)) {
    return @{ Files = @(); TotalSize = 0; DateGroups = @{} }
  }

  $files = Get-ChildItem -Path $QuarantineRoot -Recurse -File
  $totalSize = 0
  $dateGroups = @{}

  foreach ($file in $files) {
    $totalSize += $file.Length
    $dateDir = $file.Directory.Name
    if (-not $dateGroups.ContainsKey($dateDir)) {
      $dateGroups[$dateDir] = @()
    }
    $dateGroups[$dateDir] += @{
      Name = $file.Name
      Path = $file.FullName
      Size = $file.Length
    }
  }

  return @{
    Files      = @($files | ForEach-Object { @{ Name = $_.Name; Path = $_.FullName; Size = $_.Length } })
    TotalSize  = $totalSize
    TotalSizeMB = [math]::Round($totalSize / 1MB, 2)
    DateGroups = $dateGroups
  }
}

function Restore-FromQuarantine {
  <#
  .SYNOPSIS
    Stellt eine Datei aus der Quarantaene wieder her.
  .PARAMETER QuarantinePath
    Pfad der Datei in der Quarantaene.
  .PARAMETER OriginalPath
    Original-Pfad wohin die Datei zurueck soll.
  .PARAMETER Mode
    DryRun oder Move.
  #>
  param(
    [Parameter(Mandatory)][string]$QuarantinePath,
    [Parameter(Mandatory)][string]$OriginalPath,
    [ValidateSet('DryRun','Move')][string]$Mode = 'DryRun'
  )

  if (-not (Test-Path -LiteralPath $QuarantinePath)) {
    return @{ Status = 'Error'; Reason = 'QuarantineFileNotFound' }
  }

  if ($Mode -eq 'DryRun') {
    return @{ Status = 'DryRun'; From = $QuarantinePath; To = $OriginalPath }
  }

  $dir = [System.IO.Path]::GetDirectoryName($OriginalPath)
  if ($dir -and -not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
  }

  Move-ItemSafely -Source $QuarantinePath -Dest $OriginalPath
  return @{ Status = 'Restored'; From = $QuarantinePath; To = $OriginalPath }
}
