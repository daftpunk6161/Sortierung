# ================================================================
#  WpfSlice.ReportPreview.ps1  –  Slice 5: Report, preview & export handlers
#  Extracted from WpfEventHandlers.ps1 (TD-001 Slice 5/6)
# ================================================================

function Update-WpfErrorSummaryFromLog {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  if (-not $Ctx.ContainsKey('listErrorSummary') -or -not $Ctx['listErrorSummary']) { return }
  if (-not $Ctx.ContainsKey('listLog') -or -not $Ctx['listLog']) { return }

  $summaryList = $Ctx['listErrorSummary']
  $summaryList.Items.Clear()

  $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @($Ctx['listLog'].Items)) {
    $line = if ($entry -is [System.Windows.Controls.ListBoxItem]) { [string]$entry.Content } else { [string]$entry }
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -match '^\s*(ERROR:|WARN:|Fehler|WARNUNG)') {
      if ($seen.Add($line)) {
        [void]$summaryList.Items.Add($line)
      }
    }
  }

  if ($summaryList.Items.Count -eq 0) {
    [void]$summaryList.Items.Add('Keine Fehler oder Warnungen im aktuellen Lauf.')
  }
}

function Update-WpfHealthScore {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [int]$Winners = 0,
    [int]$Dupes = 0,
    [int]$Junk = 0
  )

  $total = [Math]::Max(1, ($Winners + $Dupes + $Junk))
  $penaltyDupes = [Math]::Min(60, [Math]::Round((100.0 * $Dupes / $total), 0))
  $penaltyJunk = [Math]::Min(30, [Math]::Round((100.0 * $Junk / $total), 0))
  $score = [Math]::Max(0, 100 - $penaltyDupes - $penaltyJunk)

  if ($Ctx.ContainsKey('lblHealthScore') -and $Ctx['lblHealthScore']) {
    $Ctx['lblHealthScore'].Text = ('{0}/100' -f $score)
    $brushKey = if ($score -ge 80) { 'BrushSuccess' } elseif ($score -ge 50) { 'BrushWarning' } else { 'BrushDanger' }
    $fallback = if ($score -ge 80) { '#00FF88' } elseif ($score -ge 50) { '#FFB700' } else { '#FF0044' }
    $Ctx['lblHealthScore'].Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey $brushKey -Fallback $fallback
    $Ctx['lblHealthScore'].ToolTip = ('Formel: 100 - min(60, Dupes-Anteil%) - min(30, Junk-Anteil%) = {0}' -f $score)
  }

  return [int]$score
}

function Invoke-WpfQuickPreview {
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx
  )

  $params = Get-WpfRunParameters -Ctx $Ctx
  if (-not (Get-Command Get-StandaloneConversionPreview -ErrorAction SilentlyContinue)) {
    throw 'Get-StandaloneConversionPreview ist nicht verfügbar.'
  }

  $preview = Get-StandaloneConversionPreview -Roots @($params.Roots) -AllowedRoots @($params.Roots) -PreviewLimit 20
  $grouped = @($preview.PreviewItems |
    Group-Object -Property { if ($_.Console) { [string]$_.Console } else { 'UNBEKANNT' } } |
    Sort-Object Count -Descending)
  $sample = @($grouped | Select-Object -First 6 | ForEach-Object { ('• {0}: {1}' -f [string]$_.Name, [int]$_.Count) }) -join "`n"
  if ([string]::IsNullOrWhiteSpace($sample)) { $sample = 'Keine Kandidaten gefunden.' }

  [System.Windows.MessageBox]::Show(
    ("Quick-Preview`n`nKandidaten: {0}`n`n{1}" -f [int]$preview.CandidateCount, $sample),
    'Quick-Preview',
    [System.Windows.MessageBoxButton]::OK,
    [System.Windows.MessageBoxImage]::Information) | Out-Null

  Set-WpfAdvancedStatus -Ctx $Ctx -Text ('Quick-Preview: {0} Kandidaten' -f [int]$preview.CandidateCount)
}

function Invoke-WpfDuplicateInspector {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  $params = Get-WpfRunParameters -Ctx $Ctx
  $auditRoot = [string]$params.AuditRoot
  if ([string]::IsNullOrWhiteSpace($auditRoot) -or -not (Test-Path -LiteralPath $auditRoot -PathType Container)) {
    throw 'Audit-Verzeichnis nicht gefunden. Bitte zuerst ein gültiges Audit-Root konfigurieren.'
  }

  $latestAudit = @(Get-ChildItem -LiteralPath $auditRoot -Filter '*.csv' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1)
  if ($latestAudit.Count -eq 0) {
    throw 'Keine Audit-CSV gefunden. Bitte zuerst einen Lauf ausführen.'
  }

  $rows = @(Import-Csv -LiteralPath $latestAudit[0].FullName -Encoding UTF8)
  if ($rows.Count -eq 0) {
    throw 'Audit-CSV ist leer.'
  }

  $dupeRows = @($rows | Where-Object {
    $action = ([string]$_.Action).Trim().ToUpperInvariant()
    ($_.Category -eq 'GAME') -and ($action -in @('MOVE','SKIP_DRYRUN'))
  })

  if ($dupeRows.Count -eq 0) {
    Add-WpfLogLine -Ctx $Ctx -Line 'Duplikat-Inspektor: keine Duplikat-Zeilen im letzten Audit gefunden.' -Level 'INFO'
    [System.Windows.MessageBox]::Show(
      'Im letzten Audit wurden keine GAME-Duplikate gefunden.',
      'Duplikat-Inspektor',
      [System.Windows.MessageBoxButton]::OK,
      [System.Windows.MessageBoxImage]::Information) | Out-Null
    return
  }

  $bySourceDir = @($dupeRows |
    Group-Object -Property { try { [System.IO.Path]::GetDirectoryName([string]$_.Source) } catch { '' } } |
    Sort-Object Count -Descending |
    Select-Object -First 8)

  $lines = New-Object System.Collections.Generic.List[string]
  [void]$lines.Add(('Audit: {0}' -f $latestAudit[0].Name))
  [void]$lines.Add(('Duplikat-Zeilen: {0}' -f $dupeRows.Count))
  [void]$lines.Add('')
  [void]$lines.Add('Top-Quellordner:')
  foreach ($entry in $bySourceDir) {
    $name = if ([string]::IsNullOrWhiteSpace([string]$entry.Name)) { '<unbekannt>' } else { [string]$entry.Name }
    [void]$lines.Add((' - {0} ({1})' -f $name, $entry.Count))
  }

  $message = $lines -join [Environment]::NewLine
  Add-WpfLogLine -Ctx $Ctx -Line ('Duplikat-Inspektor: {0} Duplikate analysiert ({1}).' -f $dupeRows.Count, $latestAudit[0].Name) -Level 'INFO'
  Set-WpfAdvancedStatus -Ctx $Ctx -Text ('Duplikat-Inspektor: {0} Duplikate' -f $dupeRows.Count)
  [System.Windows.MessageBox]::Show(
    $message,
    'Duplikat-Inspektor',
    [System.Windows.MessageBoxButton]::OK,
    [System.Windows.MessageBoxImage]::Information) | Out-Null
}

function Export-WpfDuplicateInspector {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  $params = Get-WpfRunParameters -Ctx $Ctx
  $auditRoot = [string]$params.AuditRoot
  if ([string]::IsNullOrWhiteSpace($auditRoot) -or -not (Test-Path -LiteralPath $auditRoot -PathType Container)) {
    throw 'Audit-Verzeichnis nicht gefunden. Bitte zuerst ein gültiges Audit-Root konfigurieren.'
  }

  $latestAudit = @(Get-ChildItem -LiteralPath $auditRoot -Filter '*.csv' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1)
  if ($latestAudit.Count -eq 0) {
    throw 'Keine Audit-CSV gefunden. Bitte zuerst einen Lauf ausführen.'
  }

  $rows = @(Import-Csv -LiteralPath $latestAudit[0].FullName -Encoding UTF8)
  $dupeRows = @($rows | Where-Object {
    $action = ([string]$_.Action).Trim().ToUpperInvariant()
    ($_.Category -eq 'GAME') -and ($action -in @('MOVE','SKIP_DRYRUN'))
  })

  if ($dupeRows.Count -eq 0) {
    Add-WpfLogLine -Ctx $Ctx -Line 'Duplikat-Export: keine Duplikate im letzten Audit gefunden.' -Level 'INFO'
    return $null
  }

  $target = Join-Path $auditRoot ('duplicate-inspector-export-{0}.csv' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
  # SEC-01 fix: sanitize all string fields to prevent CSV formula injection
  $safeDupeRows = @($dupeRows | ForEach-Object {
    $row = [ordered]@{}
    foreach ($prop in $_.PSObject.Properties) {
      $row[$prop.Name] = if ($prop.Value -is [string]) { ConvertTo-SafeCsvValue $prop.Value } else { $prop.Value }
    }
    [pscustomobject]$row
  })
  $safeDupeRows | Export-Csv -LiteralPath $target -NoTypeInformation -Encoding UTF8
  Add-WpfLogLine -Ctx $Ctx -Line ('Duplikat-Export erstellt: {0}' -f $target) -Level 'INFO'
  Set-WpfAdvancedStatus -Ctx $Ctx -Text ('Duplikat-Export: {0} Zeilen' -f $dupeRows.Count)
  return $target
}

function Export-WpfSummaryData {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][string]$Path,
    [switch]$ExcelFormat
  )

  $data = [ordered]@{
    GeneratedAt = (Get-Date).ToString('o')
    Mode = if ($Ctx.ContainsKey('lblDashMode')) { [string]$Ctx['lblDashMode'].Text } else { '–' }
    Winners = if ($Ctx.ContainsKey('lblDashWinners')) { [string]$Ctx['lblDashWinners'].Text } else { '0' }
    Dupes = if ($Ctx.ContainsKey('lblDashDupes')) { [string]$Ctx['lblDashDupes'].Text } else { '0' }
    Junk = if ($Ctx.ContainsKey('lblDashJunk')) { [string]$Ctx['lblDashJunk'].Text } else { '0' }
    Duration = if ($Ctx.ContainsKey('lblDashDuration')) { [string]$Ctx['lblDashDuration'].Text } else { '00:00' }
    Health = if ($Ctx.ContainsKey('lblHealthScore')) { [string]$Ctx['lblHealthScore'].Text } else { '–' }
  }

  if ($ExcelFormat) {
    $xml = @(
      '<?xml version="1.0"?>',
      '<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">',
      '  <Worksheet ss:Name="Summary">',
      '    <Table>'
    )
    foreach ($entry in $data.GetEnumerator()) {
      $xml += ('      <Row><Cell><Data ss:Type="String">{0}</Data></Cell><Cell><Data ss:Type="String">{1}</Data></Cell></Row>' -f $entry.Key, [System.Security.SecurityElement]::Escape([string]$entry.Value))
    }
    $xml += '    </Table>'
    $xml += '  </Worksheet>'
    $xml += '</Workbook>'
    $xml | Set-Content -LiteralPath $Path -Encoding UTF8 -Force
  } else {
    @([pscustomobject]$data) | Export-Csv -LiteralPath $Path -NoTypeInformation -Encoding UTF8
  }
}

function Format-WpfDuration {
  param([Parameter(Mandatory)][TimeSpan]$Span)

  if ($Span.TotalHours -ge 1) {
    return ('{0:D2}:{1:D2}:{2:D2}' -f [int]$Span.TotalHours, $Span.Minutes, $Span.Seconds)
  }
  return ('{0:D2}:{1:D2}' -f $Span.Minutes, $Span.Seconds)
}

function Update-WpfResultDashboard {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [string]$Mode = '–',
    [int]$Winners = 0,
    [int]$Dupes = 0,
    [int]$Junk = 0,
    [string]$Duration = '00:00'
  )

  try {
    if ($Ctx.ContainsKey('lblDashMode'))     { $Ctx['lblDashMode'].Text     = $Mode }
    if ($Ctx.ContainsKey('lblDashWinners'))  { $Ctx['lblDashWinners'].Text  = [string]$Winners }
    if ($Ctx.ContainsKey('lblDashDupes'))    { $Ctx['lblDashDupes'].Text    = [string]$Dupes }
    if ($Ctx.ContainsKey('lblDashJunk'))     { $Ctx['lblDashJunk'].Text     = [string]$Junk }
    if ($Ctx.ContainsKey('lblDashDuration')) { $Ctx['lblDashDuration'].Text = $Duration }

    if ($Ctx.ContainsKey('lblDashWinners')) {
      $Ctx['lblDashWinners'].Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey $(if ($Winners -gt 0) { 'BrushSuccess' } else { 'BrushTextMuted' }) -Fallback $(if ($Winners -gt 0) { '#00FF88' } else { '#9999CC' })
    }
    if ($Ctx.ContainsKey('lblDashDupes')) {
      $Ctx['lblDashDupes'].Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey $(if ($Dupes -gt 0) { 'BrushWarning' } else { 'BrushTextMuted' }) -Fallback $(if ($Dupes -gt 0) { '#FFB700' } else { '#9999CC' })
    }
    if ($Ctx.ContainsKey('lblDashJunk')) {
      $Ctx['lblDashJunk'].Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey $(if ($Junk -gt 0) { 'BrushDanger' } else { 'BrushTextMuted' }) -Fallback $(if ($Junk -gt 0) { '#FF0044' } else { '#9999CC' })
    }

    if (Get-Command Update-WpfHealthScore -ErrorAction SilentlyContinue) {
      [void](Update-WpfHealthScore -Ctx $Ctx -Winners $Winners -Dupes $Dupes -Junk $Junk)
    }
  } catch { } # intentionally keep dashboard updates non-fatal
}

function Update-WpfPerfDashboard {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [double]$Percent = 0,
    [string]$Throughput = '– grp/s',
    [string]$Eta = 'ETA –',
    [string]$Cache = 'Cache: –',
    [string]$Parallel = 'Parallel: –',
    [double]$ParallelUtilPercent = -1,
    [string]$Phase = 'Phase: –',
    [string]$File = 'Datei: –'
  )

  try {
    if ($Ctx.ContainsKey('lblPerfProgress'))   { $Ctx['lblPerfProgress'].Text   = ('{0}%' -f [Math]::Round($Percent, 1)) }
    if ($Ctx.ContainsKey('lblPerfThroughput')) { $Ctx['lblPerfThroughput'].Text = $Throughput }
    if ($Ctx.ContainsKey('lblPerfEta'))        { $Ctx['lblPerfEta'].Text        = $Eta }
    if ($Ctx.ContainsKey('lblPerfCache'))      { $Ctx['lblPerfCache'].Text      = $Cache }
    if ($Ctx.ContainsKey('lblPerfParallel')) {
      $Ctx['lblPerfParallel'].Text = $Parallel
      $parallelBrushKey = 'BrushTextMuted'
      $parallelFallback = '#9999CC'
      if ($ParallelUtilPercent -ge 0) {
        if ($ParallelUtilPercent -lt 30) {
          $parallelBrushKey = 'BrushDanger'; $parallelFallback = '#FF0044'
        } elseif ($ParallelUtilPercent -lt 70) {
          $parallelBrushKey = 'BrushWarning'; $parallelFallback = '#FFB700'
        } else {
          $parallelBrushKey = 'BrushSuccess'; $parallelFallback = '#00FF88'
        }
      }
      $Ctx['lblPerfParallel'].Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey $parallelBrushKey -Fallback $parallelFallback
    }
    if ($Ctx.ContainsKey('lblPerfPhase'))      { $Ctx['lblPerfPhase'].Text      = ('Phase: {0}' -f $Phase) }
    if ($Ctx.ContainsKey('lblPerfFile'))       { $Ctx['lblPerfFile'].Text       = ('Datei: {0}' -f $File) }
  } catch { } # intentionally keep perf dashboard updates non-fatal
}

# DUP-03: Zentraler Helper fuer das Dashboard-Update am Ende eines Runs.
function Update-WpfDashboardComplete {
  <# Updates result + perf dashboards, sets AppRunState, and stores WpfOperationState.
     Consolidates 6+ identische Bloecke in WpfEventHandlers.ps1. #>
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][string]$Status,  # Canceled|Completed|Failed
    [Parameter(Mandatory)][string]$Phase,
    [string]$Mode = '–',
    [double]$Percent = 0,
    [string]$BusyHint = '',
    [string]$Duration = '00:00',
    [int]$Winners = 0,
    [int]$Dupes = 0,
    [int]$Junk = 0,
    [string]$Throughput = '– grp/s',
    [string]$Eta = 'ETA –',
    [string]$Cache = '',
    [string]$Parallel = '',
    [double]$ParallelUtilPercent = -1,
    [string]$File = '–'
  )

  if (Get-Command Set-AppRunState -ErrorAction SilentlyContinue) {
    try { [void](Set-AppRunState -State $(if ($Status -eq 'Completed') { 'Completed' } elseif ($Status -eq 'Canceled') { 'Canceled' } else { 'Failed' })) } catch { }
  }
  if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
    try {
      [void](Set-AppStateValue -Key 'WpfOperationState' -Value ([pscustomobject]@{
        Status = $Status
        Phase = $Phase
        ProgressPercent = [double]$Percent
        CompletedAtUtc = [DateTime]::UtcNow
      }))
    } catch { }
  }
  if (-not [string]::IsNullOrWhiteSpace($BusyHint) -and $Ctx.ContainsKey('lblBusyHint')) {
    $Ctx['lblBusyHint'].Text = $BusyHint
  }
  Update-WpfResultDashboard -Ctx $Ctx -Mode $Mode -Winners $Winners -Dupes $Dupes -Junk $Junk -Duration $Duration
  Update-WpfPerfDashboard -Ctx $Ctx -Percent $Percent -Throughput $Throughput -Eta $Eta -Cache $Cache -Parallel $Parallel -ParallelUtilPercent $ParallelUtilPercent -Phase $Phase -File $File
}

# DUP-04: Zentraler Helper fuer Runspace/Process Dispose.
function Dispose-WpfBackgroundJob {
  <# Safely disposes a background job's PowerShell and Runspace objects. #>
  param($Job)
  if (-not $Job) { return }
  if ($Job.PSObject.Properties.Name -contains 'Completed') { $Job.Completed = $true }
  try { if ($Job.PSObject.Properties['PS'] -and $Job.PS) { $Job.PS.Dispose() } } catch { }
  try { if ($Job.PSObject.Properties['Runspace'] -and $Job.Runspace) { $Job.Runspace.Dispose() } } catch { }
}

function Register-WpfReportPreviewHandlers {
  <#
  .SYNOPSIS
    Registers report, preview and export event handlers (Slice 5).
  .PARAMETER Window
    The WPF Window object.
  .PARAMETER Ctx
    The named-element hashtable from Get-WpfNamedElements.
  .PARAMETER SaveFile
    Scriptblock for save-file dialog.
  .PARAMETER ResolveLatestHtmlReport
    Scriptblock to resolve the latest HTML report file.
  .PARAMETER RefreshReportPreview
    Scriptblock to reload report preview WebBrowser.
  #>
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][scriptblock]$SaveFile,
    [Parameter(Mandatory)][scriptblock]$ResolveLatestHtmlReport,
    [Parameter(Mandatory)][scriptblock]$RefreshReportPreview
  )

  # ── Quick Preview ──────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnQuickPreview') -and $Ctx['btnQuickPreview']) {
    $Ctx['btnQuickPreview'].add_Click({
      try { Invoke-WpfQuickPreview -Window $Window -Ctx $Ctx } catch { Add-WpfLogLine -Ctx $Ctx -Line ("Quick-Preview fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR' }
    }.GetNewClosure())
  }

  # ── Health Score ───────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnHealthScore') -and $Ctx['btnHealthScore']) {
    $Ctx['btnHealthScore'].add_Click({
      try {
        $w = if ($Ctx.ContainsKey('lblDashWinners')) { [int]([string]$Ctx['lblDashWinners'].Text) } else { 0 }
        $d = if ($Ctx.ContainsKey('lblDashDupes')) { [int]([string]$Ctx['lblDashDupes'].Text) } else { 0 }
        $j = if ($Ctx.ContainsKey('lblDashJunk')) { [int]([string]$Ctx['lblDashJunk'].Text) } else { 0 }
        $score = Update-WpfHealthScore -Ctx $Ctx -Winners $w -Dupes $d -Junk $j
        Add-WpfLogLine -Ctx $Ctx -Line ("Health-Score: {0}/100" -f $score) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Health-Score fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Duplicate Inspector ────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnDuplicateInspector') -and $Ctx['btnDuplicateInspector']) {
    $Ctx['btnDuplicateInspector'].add_Click({
      try {
        Invoke-WpfDuplicateInspector -Ctx $Ctx
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Duplikat-Inspektor fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Duplicate Export ───────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnDuplicateExport') -and $Ctx['btnDuplicateExport']) {
    $Ctx['btnDuplicateExport'].add_Click({
      try {
        $exportPath = Export-WpfDuplicateInspector -Ctx $Ctx
        if ($exportPath) {
          try { Start-Process $exportPath | Out-Null } catch { }
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Duplikat-Export fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── CSV Export ─────────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnExportCsv') -and $Ctx['btnExportCsv']) {
    $Ctx['btnExportCsv'].add_Click({
      try {
        $targetPath = & $SaveFile 'CSV Export' 'CSV (*.csv)|*.csv|Alle Dateien (*.*)|*.*' ('romcleanup-summary-{0}.csv' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        if (-not [string]::IsNullOrWhiteSpace($targetPath)) {
          Export-WpfSummaryData -Ctx $Ctx -Path $targetPath
          Add-WpfLogLine -Ctx $Ctx -Line ("CSV exportiert: {0}" -f $targetPath) -Level 'INFO'
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("CSV Export fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Excel Export ───────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnExportExcel') -and $Ctx['btnExportExcel']) {
    $Ctx['btnExportExcel'].add_Click({
      try {
        $targetPath = & $SaveFile 'Excel Export' 'Excel XML (*.xml)|*.xml|Alle Dateien (*.*)|*.*' ('romcleanup-summary-{0}.xml' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        if (-not [string]::IsNullOrWhiteSpace($targetPath)) {
          Export-WpfSummaryData -Ctx $Ctx -Path $targetPath -ExcelFormat
          Add-WpfLogLine -Ctx $Ctx -Line ("Excel exportiert: {0}" -f $targetPath) -Level 'INFO'
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Excel Export fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Log controls ───────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnClearLog') -and $Ctx['btnClearLog']) {
    $Ctx['btnClearLog'].add_Click({
      $Ctx['listLog'].Items.Clear()
    }.GetNewClosure())
  }

  if ($Ctx.ContainsKey('btnExportLog') -and $Ctx['btnExportLog']) {
    $Ctx['btnExportLog'].add_Click({
      try {
        $targetPath = & $SaveFile 'Log exportieren' 'Text-Datei (*.txt)|*.txt|Alle Dateien (*.*)|*.*' ("romcleanup-log-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt")
        if (-not [string]::IsNullOrWhiteSpace($targetPath)) {
          $lines = @($Ctx['listLog'].Items | ForEach-Object {
            if ($_ -is [System.Windows.Controls.ListBoxItem]) { [string]$_.Content } else { [string]$_ }
          })
          $lines | Set-Content -LiteralPath $targetPath -Encoding UTF8
          Add-WpfLogLine -Ctx $Ctx -Line "Log exportiert nach: $targetPath" -Level 'INFO'
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line "Log-Export fehlgeschlagen: $($_.Exception.Message)" -Level 'ERROR'
      }
    }.GetNewClosure())
  }
}
