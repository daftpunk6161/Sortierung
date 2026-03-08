function Export-FrontendCollectionXml {
  <# Export KEEP game entries to LaunchBox and EmulationStation XML formats. #>
  param(
    [Parameter(Mandatory=$true)][System.Collections.Generic.List[psobject]]$Report,
    [Parameter(Mandatory=$true)][string]$ReportDir,
    [Parameter(Mandatory=$true)][string]$Timestamp
  )

  $rows = @($Report | Where-Object { $_.Category -eq 'GAME' -and $_.Action -eq 'KEEP' })
  if (@($rows).Count -eq 0) {
    return [pscustomobject]@{ LaunchBoxPath = $null; EmulationStationPath = $null }
  }

  $lbPath = Join-Path $ReportDir ("rom-cleanup-launchbox-{0}.xml" -f $Timestamp)
  $esPath = Join-Path $ReportDir ("rom-cleanup-emulationstation-{0}.xml" -f $Timestamp)

  $lbSb = New-Object System.Text.StringBuilder
  [void]$lbSb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
  [void]$lbSb.AppendLine('<LaunchBox>')
  foreach ($row in $rows) {
    $name = [System.Security.SecurityElement]::Escape([IO.Path]::GetFileNameWithoutExtension([string]$row.MainPath))
    $appPath = [System.Security.SecurityElement]::Escape([string]$row.MainPath)
    [void]$lbSb.AppendLine('  <Game>')
    [void]$lbSb.AppendLine(('    <Title>{0}</Title>' -f $name))
    [void]$lbSb.AppendLine(('    <ApplicationPath>{0}</ApplicationPath>' -f $appPath))
    [void]$lbSb.AppendLine('  </Game>')
  }
  [void]$lbSb.AppendLine('</LaunchBox>')
  $lbSb.ToString() | Out-File -LiteralPath $lbPath -Encoding utf8 -Force

  $esSb = New-Object System.Text.StringBuilder
  [void]$esSb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
  [void]$esSb.AppendLine('<gameList>')
  foreach ($row in $rows) {
    $name = [System.Security.SecurityElement]::Escape([IO.Path]::GetFileNameWithoutExtension([string]$row.MainPath))
    $romPath = [System.Security.SecurityElement]::Escape([string]$row.MainPath)
    [void]$esSb.AppendLine('  <game>')
    [void]$esSb.AppendLine(('    <path>{0}</path>' -f $romPath))
    [void]$esSb.AppendLine(('    <name>{0}</name>' -f $name))
    [void]$esSb.AppendLine('  </game>')
  }
  [void]$esSb.AppendLine('</gameList>')
  $esSb.ToString() | Out-File -LiteralPath $esPath -Encoding utf8 -Force

  return [pscustomobject]@{ LaunchBoxPath = $lbPath; EmulationStationPath = $esPath }
}

function Write-Reports {
  <# Create CSV/HTML reports and optional DryRun JSON report; return artifact paths. #>
  param(
    [Parameter(Mandatory=$true)][System.Collections.Generic.List[psobject]]$Report,
    [int]$DupeGroups,
    [int]$TotalDupes,
    [long]$SavedBytes,
    [int]$JunkCount,
    [long]$JunkBytes,
    [int]$BiosCount,
    [int]$UniqueGames,
    [int]$TotalScanned,
    [string]$Mode,
    [bool]$UseDat,
    [hashtable]$DatIndex,
    [hashtable]$ConsoleSortUnknownReasons,
    [bool]$GenerateReports,
    [scriptblock]$Log
  )

  $reportSw = [System.Diagnostics.Stopwatch]::StartNew()
  $csvPath = $null
  $htmlPath = $null
  $jsonPath = $null
  $datCompletenessCsvPath = $null
  $launchBoxXmlPath = $null
  $emulationStationXmlPath = $null
  $datArchiveStats = $null
  if ($UseDat -and (Get-Command Get-DatArchiveStats -ErrorAction SilentlyContinue)) {
    $datArchiveStats = Get-DatArchiveStats
  }

  if ($GenerateReports) {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $reportDir = Join-Path (Get-Location) "reports"
    if (-not (Test-Path -LiteralPath $reportDir)) {
      try {
        [void](New-Item -ItemType Directory -Path $reportDir -Force)
      } catch {
        $fallbackBase = Join-Path $env:LOCALAPPDATA 'RomCleanupRegionDedupe'
        $reportDir = Join-Path $fallbackBase 'reports'
        if (-not (Test-Path -LiteralPath $reportDir)) {
          [void](New-Item -ItemType Directory -Path $reportDir -Force)
        }
        if ($Log) { & $Log ("WARN: Report-Ordner nicht schreibbar. Fallback: {0}" -f $reportDir) }
      }
    }
    $csvPath  = Join-Path $reportDir ("rom-cleanup-report-{0}.csv" -f $timestamp)
    $htmlPath = Join-Path $reportDir ("rom-cleanup-report-{0}.html" -f $timestamp)
    if ($UseDat) {
      $datCompletenessCsvPath = Join-Path $reportDir ("rom-cleanup-dat-completeness-{0}.csv" -f $timestamp)
    }
    if ($Mode -eq 'DryRun') {
      $jsonPath = Join-Path $reportDir ("rom-cleanup-report-{0}.json" -f $timestamp)
    }

    # Sanitize CSV values to prevent formula injection in Excel
    $Report | Sort-Object Category, GameKey, Action, Region |
      Select-Object @{N='Category';E={ConvertTo-SafeCsvValue $_.Category}},
                    @{N='GameKey';E={ConvertTo-SafeCsvValue $_.GameKey}},
                    @{N='Name';E={ConvertTo-SafeCsvValue ([IO.Path]::GetFileName($_.MainPath))}},
                    @{N='Region';E={ConvertTo-SafeCsvValue $_.Region}},
                    @{N='Action';E={ConvertTo-SafeCsvValue $_.Action}},
                    @{N='FullPath';E={ConvertTo-SafeCsvValue $_.MainPath}},
                    @{N='SizeBytes';E={$_.SizeBytes}} |
      Export-Csv -NoTypeInformation -Encoding UTF8 -Path $csvPath

    ConvertTo-HtmlReport -Report $Report -HtmlPath $htmlPath `
      -DupeGroups $DupeGroups -TotalDupes $TotalDupes `
      -SavedBytes $SavedBytes -JunkCount $JunkCount -JunkBytes $JunkBytes `
      -BiosCount $BiosCount -UniqueGames $UniqueGames `
      -TotalScanned $TotalScanned -Mode $Mode `
      -DatEnabled $UseDat `
      -DatIndex $DatIndex `
      -ConsoleSortUnknownReasons $ConsoleSortUnknownReasons `
      -DatArchiveStats $datArchiveStats

    if ($UseDat -and $DatIndex -and $DatIndex.Count -gt 0 -and $datCompletenessCsvPath) {
      $datCompleteness = Get-DatCompletenessReport -Report $Report -DatIndex $DatIndex
      if ($datCompleteness -and $datCompleteness.Rows -and @($datCompleteness.Rows).Count -gt 0) {
        # SEC-02 fix: sanitize Console field to prevent CSV formula injection
        @($datCompleteness.Rows) |
          Select-Object @{N='Console';E={ConvertTo-SafeCsvValue $_.Console}}, Matched, Expected, Missing, Coverage |
          Export-Csv -NoTypeInformation -Encoding UTF8 -Path $datCompletenessCsvPath
      } else {
        $datCompletenessCsvPath = $null
      }
    }

    if ($jsonPath) {
      $jsonRows = @($Report | Sort-Object Category, GameKey, Action, Region | ForEach-Object {
        $winnerRegion = if ($_.PSObject.Properties.Name -contains 'WinnerRegion') { [string]$_.WinnerRegion } else { '' }
        $winnerPath = if ($_.PSObject.Properties.Name -contains 'WinnerPath') { [string]$_.WinnerPath } else { '' }
        $type = if ($_.PSObject.Properties.Name -contains 'Type') { [string]$_.Type } else { '' }
        $datMatch = if ($_.PSObject.Properties.Name -contains 'DatMatch') { [bool]$_.DatMatch } else { $false }
        $versionScore = if ($_.PSObject.Properties.Name -contains 'VersionScore') { [long]$_.VersionScore } else { [long]0 }
        $formatScore = if ($_.PSObject.Properties.Name -contains 'FormatScore') { [int]$_.FormatScore } else { 0 }
        [pscustomobject]@{
          category = [string]$_.Category
          gameKey = [string]$_.GameKey
          action = [string]$_.Action
          region = [string]$_.Region
          winnerRegion = $winnerRegion
          type = $type
          datMatch = $datMatch
          versionScore = $versionScore
          formatScore = $formatScore
          sizeBytes = [long]$_.SizeBytes
          mainPath = [string]$_.MainPath
          winnerPath = $winnerPath
        }
      })

      $jsonDocument = [ordered]@{
        schemaVersion = 'dryrun-v1'
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        mode = [string]$Mode
        summary = [ordered]@{
          totalScanned = [int]$TotalScanned
          uniqueGames = [int]$UniqueGames
          dupeGroups = [int]$DupeGroups
          totalDupes = [int]$TotalDupes
          junkCount = [int]$JunkCount
          biosCount = [int]$BiosCount
          savedBytes = [long]$SavedBytes
        }
        items = $jsonRows
      }

      Write-JsonFile -Path $jsonPath -Data $jsonDocument -Depth 6
    }

    $frontendXml = Export-FrontendCollectionXml -Report $Report -ReportDir $reportDir -Timestamp $timestamp
    $launchBoxXmlPath = [string]$frontendXml.LaunchBoxPath
    $emulationStationXmlPath = [string]$frontendXml.EmulationStationPath

    # [F16] Invoke report plugins after built-in reports are complete
    if (Get-Command Invoke-ReportPlugins -ErrorAction SilentlyContinue) {
      $pluginSummary = @{
        TotalScanned = [int]$TotalScanned
        UniqueGames  = [int]$UniqueGames
        DupeGroups   = [int]$DupeGroups
        TotalDupes   = [int]$TotalDupes
        JunkCount    = [int]$JunkCount
        BiosCount    = [int]$BiosCount
        SavedBytes   = [long]$SavedBytes
        UseDat       = [bool]$UseDat
      }
      try {
        Invoke-ReportPlugins -Report $Report -ReportDir $reportDir -Timestamp $timestamp `
          -Mode $Mode -Summary $pluginSummary -Log $Log
      } catch {
        if ($Log) { & $Log ('WARN: Report-Plugin-Ausfuehrung fehlgeschlagen: {0}' -f $_.Exception.Message) }
      }
    }

    $reportSw.Stop()
    if ($Log) { & $Log ("{0:N1}s Report-Zeit" -f $reportSw.Elapsed.TotalSeconds) }

    if ($Log) {
      & $Log ""
      & $Log "Report CSV:  $csvPath"
      & $Log "Report HTML: $htmlPath"
      if ($datCompletenessCsvPath) { & $Log "Report DAT CSV: $datCompletenessCsvPath" }
      if ($jsonPath) { & $Log "Report JSON: $jsonPath" }
      if ($launchBoxXmlPath) { & $Log "Report LaunchBox XML: $launchBoxXmlPath" }
      if ($emulationStationXmlPath) { & $Log "Report EmulationStation XML: $emulationStationXmlPath" }
      if ($UseDat -and $datArchiveStats) {
        foreach ($entry in ($datArchiveStats.GetEnumerator() | Sort-Object Name)) {
          $count = [int]$entry.Value
          if ($count -gt 0) {
            & $Log ("DAT Archive Stat: {0}={1}" -f $entry.Key, $count)
          }
        }
      }
    }
  } else {
    $reportSw.Stop()
    if ($Log) {
      & $Log "0.0s Report-Zeit (deaktiviert)"
      & $Log ""
      & $Log "Report: deaktiviert (DryRun)"
    }
  }

  return [pscustomobject]@{
    CsvPath = $csvPath
    HtmlPath = $htmlPath
    JsonPath = $jsonPath
    DatCompletenessCsvPath = $datCompletenessCsvPath
    LaunchBoxXmlPath = $launchBoxXmlPath
    EmulationStationXmlPath = $emulationStationXmlPath
  }
}

# ================================================================
#  REPORT PLUGIN INTERFACE
# ================================================================

function Get-ReportPlugins {
  <#
  .SYNOPSIS
    Entdeckt Report-Plugins im plugins/reports/ Ordner.
  .DESCRIPTION
    Jedes Plugin ist ein .ps1-Skript das eine Funktion 'Invoke-ReportPlugin'
    exportiert mit Parametern: -Report, -ReportDir, -Timestamp, -Mode, -Summary.
    Rückgabe: Array mit Pfaden und Plugin-Metadaten.
  #>
  $pluginDir = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'plugins\reports'
  if (-not (Test-Path -LiteralPath $pluginDir -PathType Container)) { return @() }

  $plugins = @(Get-ChildItem -LiteralPath $pluginDir -Filter '*.ps1' -File -ErrorAction SilentlyContinue)
  $result = New-Object System.Collections.Generic.List[pscustomobject]
  foreach ($file in $plugins) {
    [void]$result.Add([pscustomobject]@{
      Name = [IO.Path]::GetFileNameWithoutExtension($file.Name)
      Path = $file.FullName
    })
  }
  return ,$result
}

function Invoke-ReportPlugins {
  <#
  .SYNOPSIS
    Führt alle Report-Plugins aus und gibt deren Ergebnisse zurück.
  .PARAMETER Report
    Die Report-Zeilen (List[psobject]).
  .PARAMETER ReportDir
    Zielverzeichnis für Reports.
  .PARAMETER Timestamp
    Zeitstempel für Dateinamen.
  .PARAMETER Mode
    DryRun oder Move.
  .PARAMETER Summary
    Zusammenfassungs-Hashtable (TotalScanned, UniqueGames, etc.).
  .PARAMETER Log
    Optionale Log-Scriptblock.
  #>
  param(
    [Parameter(Mandatory=$true)]$Report,
    [Parameter(Mandatory=$true)][string]$ReportDir,
    [Parameter(Mandatory=$true)][string]$Timestamp,
    [string]$Mode = 'DryRun',
    [hashtable]$Summary = @{},
    [scriptblock]$Log
  )

  $plugins = @(Get-ReportPlugins)
  if ($plugins.Count -eq 0) { return @() }

  $results = New-Object System.Collections.Generic.List[pscustomobject]
  foreach ($plugin in $plugins) {
    try {
      # Plugin laden (dot-source in eigenem Scope)
      $pluginFunc = $null
      $pluginBlock = [scriptblock]::Create((Get-Content -LiteralPath $plugin.Path -Raw -ErrorAction Stop))
      & $pluginBlock

      # Plugin aufrufen (erwartet Invoke-ReportPlugin Funktion)
      if (Get-Command Invoke-ReportPlugin -ErrorAction SilentlyContinue) {
        $pluginResult = Invoke-ReportPlugin -Report $Report -ReportDir $ReportDir -Timestamp $Timestamp -Mode $Mode -Summary $Summary
        [void]$results.Add([pscustomobject]@{
          Plugin = $plugin.Name
          Success = $true
          Result = $pluginResult
        })
        if ($Log) { & $Log ('Report-Plugin {0}: OK' -f $plugin.Name) }
      }
    } catch {
      [void]$results.Add([pscustomobject]@{
        Plugin = $plugin.Name
        Success = $false
        Result = $_.Exception.Message
      })
      if ($Log) { & $Log ('Report-Plugin {0}: FEHLER - {1}' -f $plugin.Name, $_.Exception.Message) }
    }
  }

  return @($results)
}
