# ================================================================
#  Example Report Plugin — Markdown-Report
#  Dateiname: example.report-plugin.ps1
#  Ordner: plugins/reports/
#
#  Jedes Report-Plugin muss eine Funktion 'Invoke-ReportPlugin' definieren.
#  Parameter:
#    -Report      : Array von Report-Zeilen (Category, GameKey, Action, Region, MainPath, SizeBytes)
#    -ReportDir   : Zielverzeichnis für Reports
#    -Timestamp   : Zeitstempel (yyyyMMdd-HHmmss)
#    -Mode        : 'DryRun' oder 'Move'
#    -Summary     : Hashtable mit TotalScanned, UniqueGames, DupeGroups, TotalDupes, SavedBytes
#  Rückgabe:
#    [pscustomobject] mit mindestens FilePath-Property
# ================================================================

function Invoke-ReportPlugin {
  param(
    $Report,
    [string]$ReportDir,
    [string]$Timestamp,
    [string]$Mode = 'DryRun',
    [hashtable]$Summary = @{}
  )

  $mdPath = Join-Path $ReportDir ("rom-cleanup-report-{0}.md" -f $Timestamp)

  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('# ROM Cleanup Report')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine(('**Modus:** {0}' -f $Mode))
  [void]$sb.AppendLine(('**Erstellt:** {0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
  [void]$sb.AppendLine('')

  if ($Summary -and $Summary.Count -gt 0) {
    [void]$sb.AppendLine('## Zusammenfassung')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| Metrik | Wert |')
    [void]$sb.AppendLine('|--------|------|')
    foreach ($entry in $Summary.GetEnumerator()) {
      [void]$sb.AppendLine(('| {0} | {1} |' -f $entry.Key, $entry.Value))
    }
    [void]$sb.AppendLine('')
  }

  $keeps = @($Report | Where-Object { $_.Action -eq 'KEEP' })
  $moves = @($Report | Where-Object { $_.Action -eq 'MOVE' })

  [void]$sb.AppendLine('## Ergebnisse')
  [void]$sb.AppendLine('')
  [void]$sb.AppendLine(('- **Behalten:** {0} Dateien' -f $keeps.Count))
  [void]$sb.AppendLine(('- **Verschoben/Dedupliziert:** {0} Dateien' -f $moves.Count))

  $sb.ToString() | Out-File -LiteralPath $mdPath -Encoding utf8 -Force

  return [pscustomobject]@{
    FilePath = $mdPath
    Format = 'markdown'
  }
}
