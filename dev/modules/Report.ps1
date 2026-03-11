function New-ReportRow {
  <#
    Creates a standardized report row object.
    Used for GAME, JUNK, and BIOS entries.
  #>
  param(
    [Parameter(Mandatory=$true)][psobject]$Item,
    [string]$GameKey,
    [string]$Action,
    [string]$WinnerRegion = '',
    [string]$WinnerPath = '',
    [bool]$DatMatch = $false,
    [bool]$IsCorrupt = $false
  )

  return [pscustomobject]@{
    GameKey      = $GameKey
    Action       = $Action
    Category     = $Item.Category
    Region       = $Item.Region
    WinnerRegion = $WinnerRegion
    VersionScore = $Item.VersionScore
    FormatScore  = $Item.FormatScore
    Type         = $Item.Type
    DatMatch     = $DatMatch
    IsCorrupt    = $IsCorrupt
    MainPath     = $Item.MainPath
    Root         = $Item.Root
    SizeBytes    = $Item.SizeBytes
    Winner       = $WinnerPath
  }
}

function ConvertTo-SafeOutputValue {
  param(
    [string]$Value,
    [ValidateSet('Csv','Html')][string]$Mode = 'Csv'
  )

  switch ($Mode) {
    'Html' {
      if ($null -eq $Value) { return '' }
      return [System.Net.WebUtility]::HtmlEncode([string]$Value)
    }
    default {
      if ([string]::IsNullOrEmpty($Value)) { return $Value }
      # REPORT-006 FIX: Strip embedded control characters (tabs, newlines, null bytes)
      # to prevent CSV-injection bypass via embedded newlines/tabs (BUG-008)
      $Value = $Value -replace "[\t\r\n\x00]", ''
      $trimmed = $Value.TrimStart(' ')
      if ($trimmed -match '^[=+\-@\|]') {
        return "'" + $Value
      }
      return $Value
    }
  }
}

function ConvertTo-HtmlAttributeSafe {
  param([string]$Value)

  if ($null -eq $Value) { return '' }
  $encoded = [System.Net.WebUtility]::HtmlEncode([string]$Value)
  # Escape single quotes for safe use in single-quoted HTML attributes
  return $encoded.Replace("'", '&#39;')
}

function ConvertTo-SafeCsvValue {
  param([string]$Value)
  return ConvertTo-SafeOutputValue -Value $Value -Mode 'Csv'
}

function ConvertTo-HtmlSafe {
  param([string]$Value)
  return ConvertTo-SafeOutputValue -Value $Value -Mode 'Html'
}

function Get-DatCompletenessReport {
  param(
    [System.Collections.Generic.List[psobject]]$Report,
    [hashtable]$DatIndex = $null
  )

  $rows = New-Object System.Collections.Generic.List[psobject]
  $missingTotal = 0
  $expectedTotal = 0

  if (-not $Report -or $Report.Count -eq 0 -or -not $DatIndex -or $DatIndex.Count -eq 0) {
    return [pscustomobject]@{
      Rows = @($rows)
      MissingTotal = 0
      ExpectedTotal = 0
    }
  }

  $matchedGameKeysByConsole = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($r in $Report) {
    if ($r.Category -ne 'GAME' -or -not $r.DatMatch) { continue }
    $ext = [IO.Path]::GetExtension($r.MainPath)
    $console = Get-ConsoleType -RootPath $r.Root -FilePath $r.MainPath -Extension $ext
    if ([string]::IsNullOrWhiteSpace([string]$console)) { continue }
    if (-not $matchedGameKeysByConsole.ContainsKey($console)) {
      $matchedGameKeysByConsole[$console] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$r.GameKey)) {
      [void]$matchedGameKeysByConsole[$console].Add(([string]$r.GameKey).Trim())
    }
  }

  foreach ($consoleKey in ($DatIndex.Keys | Sort-Object)) {
    $consoleIndex = $DatIndex[$consoleKey]
    if (-not $consoleIndex) { continue }

    $expectedGameSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($gameName in $consoleIndex.Values) {
      if ([string]::IsNullOrWhiteSpace([string]$gameName)) { continue }
      [void]$expectedGameSet.Add(([string]$gameName).Trim())
    }

    $expected = $expectedGameSet.Count
    $matched = 0
    if ($matchedGameKeysByConsole.ContainsKey($consoleKey)) {
      $matched = $matchedGameKeysByConsole[$consoleKey].Count
    }
    if ($matched -gt $expected) { $matched = $expected }
    $missing = [math]::Max(0, ($expected - $matched))
    $coverage = if ($expected -gt 0) { [math]::Round(($matched / $expected) * 100, 1) } else { 0 }

    $expectedTotal += $expected
    $missingTotal += $missing
    [void]$rows.Add([pscustomobject]@{
      Console  = [string]$consoleKey
      Matched  = [int]$matched
      Expected = [int]$expected
      Missing  = [int]$missing
      Coverage = [double]$coverage
    })
  }

  return [pscustomobject]@{
    Rows = @($rows)
    MissingTotal = [int]$missingTotal
    ExpectedTotal = [int]$expectedTotal
  }
}

function ConvertTo-HtmlReport {
  param(
    [Parameter(Mandatory=$true)][System.Collections.Generic.List[psobject]]$Report,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$HtmlPath,
    [int]$DupeGroups,
    [int]$TotalDupes,
    [long]$SavedBytes,
    [int]$JunkCount,
    [long]$JunkBytes,
    [int]$BiosCount,
    [int]$UniqueGames,
    [int]$TotalScanned,
    [string]$Mode,
    [bool]$DatEnabled,
    [hashtable]$DatIndex = $null,
    [hashtable]$ConsoleSortUnknownReasons = $null,
    [hashtable]$DatArchiveStats = $null
  )

  # REPORT-002 FIX: Path traversal validation — ensure HtmlPath stays within the working directory
  $resolvedHtmlPath = [System.IO.Path]::GetFullPath($HtmlPath)
  $expectedBase = [System.IO.Path]::GetFullPath((Get-Location).Path)
  if (-not $resolvedHtmlPath.StartsWith($expectedBase, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "REPORT-002: HtmlPath '$HtmlPath' resolves to '$resolvedHtmlPath' which is outside the working directory '$expectedBase'. Possible path traversal."
  }

  $savedMB  = [math]::Round($SavedBytes / 1MB, 1)
  $junkMB   = [math]::Round($JunkBytes  / 1MB, 1)
  $sortedReport = $Report | Sort-Object Category, GameKey, Action, Region
  $datMatchCount = 0
  $consoleStats = $null
  $datCompletenessRows = New-Object System.Collections.Generic.List[psobject]
  $datMissingTotal = 0
  $datExpectedTotal = 0
  if ($Report -and $Report.Count -gt 0) {
    $consoleStats = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    $matchedGameKeysByConsole = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($r in $Report) {
      if ($DatEnabled -and $r.DatMatch) { $datMatchCount++ }
      if ($r.Category -ne 'GAME') { continue }
      $ext = [IO.Path]::GetExtension($r.MainPath)
      $console = Get-ConsoleType -RootPath $r.Root -FilePath $r.MainPath -Extension $ext
      if (-not $consoleStats.ContainsKey($console)) {
        $consoleStats[$console] = @{ total = 0; match = 0 }
      }
      $consoleStats[$console].total++
      if ($DatEnabled -and $r.DatMatch) {
        $consoleStats[$console].match++
        if (-not $matchedGameKeysByConsole.ContainsKey($console)) {
          $matchedGameKeysByConsole[$console] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$r.GameKey)) {
          [void]$matchedGameKeysByConsole[$console].Add(([string]$r.GameKey).Trim())
        }
      }
    }

    if ($DatEnabled -and $DatIndex -and $DatIndex.Count -gt 0) {
      $datCompleteness = Get-DatCompletenessReport -Report $Report -DatIndex $DatIndex
      foreach ($entry in @($datCompleteness.Rows)) {
        [void]$datCompletenessRows.Add($entry)
      }
      $datExpectedTotal = [int]$datCompleteness.ExpectedTotal
      $datMissingTotal = [int]$datCompleteness.MissingTotal
    }

    if ($consoleStats.Count -eq 0) { $consoleStats = $null }
  }

  $catIcons = @{ 'GAME' = '&#x1F3AE;'; 'JUNK' = '&#x1F5D1;'; 'BIOS' = '&#x1F4BE;' }
  $htmlCache = [hashtable]::new([StringComparer]::Ordinal)
  $htmlSafe = {
    param([string]$Value)
    if ($null -eq $Value) { return '' }
    if ($htmlCache.ContainsKey($Value)) { return $htmlCache[$Value] }
    $encoded = ConvertTo-HtmlSafe $Value
    $htmlCache[$Value] = $encoded
    return $encoded
  }

  $sb = New-Object System.Text.StringBuilder 8192
  $streamingThresholdRows = 100000
  $flushEveryRows = 2000
  if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
    $cfgStreamingThreshold = Get-AppStateValue -Key 'HtmlReportStreamingThresholdRows' -Default $streamingThresholdRows
    if ($cfgStreamingThreshold -is [int] -or $cfgStreamingThreshold -is [long]) {
      $streamingThresholdRows = [int][math]::Max(10000, [int64]$cfgStreamingThreshold)
    }

    $cfgFlushEveryRows = Get-AppStateValue -Key 'HtmlReportFlushEveryRows' -Default $flushEveryRows
    if ($cfgFlushEveryRows -is [int] -or $cfgFlushEveryRows -is [long]) {
      $flushEveryRows = [int][math]::Max(250, [int64]$cfgFlushEveryRows)
    }
  }

  $useStreamingWrite = ($sortedReport.Count -ge $streamingThresholdRows)
  if ($useStreamingWrite -and (Test-Path -LiteralPath $HtmlPath -PathType Leaf)) {
    # REPORT-008 FIX: Fail early if the file is locked instead of producing corrupt HTML
    try {
      Remove-Item -LiteralPath $HtmlPath -Force -ErrorAction Stop
    } catch {
      throw "REPORT-008: Cannot write HTML report — file is locked or in use: $HtmlPath. Close any application using this file and retry. Error: $($_.Exception.Message)"
    }
  }
  $cspTag = ''
  $cspNonce = ''
  if (Get-Command Get-HtmlReportSecurityHeaders -ErrorAction SilentlyContinue) {
    $cspResult = Get-HtmlReportSecurityHeaders
    if ($cspResult -is [string]) {
      $cspTag = $cspResult
    } else {
      $cspTag = $cspResult.CspTag
      $cspNonce = $cspResult.Nonce
    }
  } else {
    Write-Warning 'SEC-03: SecurityEventStream nicht geladen — HTML-Report hat keinen CSP-Header.'
    # REPORT-003 FIX: Fallback basic CSP meta tag when SecurityEventStream module isn't loaded
    $cspTag = '<meta http-equiv="Content-Security-Policy" content="default-src ''none''; style-src ''self''; script-src ''none''; img-src data:;">'
  }
  $header = @"
<!-- saved from url=(0016)http://localhost -->
<!DOCTYPE html><html lang="de"><head><meta charset="utf-8"><meta http-equiv="X-UA-Compatible" content="IE=edge">
${cspTag}
<title>ROM Cleanup Report</title><style>
*{box-sizing:border-box;margin:0;padding:0}
body{font-family:Segoe UI,system-ui,sans-serif;background:#1e1e2e;color:#cdd6f4;padding:24px}
h1{font-size:1.6em;margin-bottom:6px;color:#89b4fa}
.meta{color:#a6adc8;margin-bottom:18px;font-size:.9em}
.cards{display:flex;gap:12px;flex-wrap:wrap;margin-bottom:20px}
.card{background:#313244;border-radius:10px;padding:14px 20px;min-width:150px}
.card .val{font-size:1.8em;font-weight:700;color:#cba6f7}
.card .lbl{font-size:.85em;color:#a6adc8}
.card.green .val{color:#a6e3a1} .card.red .val{color:#f38ba8} .card.blue .val{color:#89b4fa} .card.yellow .val{color:#f9e2af}
table{border-collapse:collapse;width:100%;font-size:.82em;margin-top:8px}
th{background:#313244;color:#89b4fa;padding:8px 10px;text-align:left;position:sticky;top:0;cursor:pointer;user-select:none}
th:hover{color:#cba6f7}
td{padding:6px 10px;border-bottom:1px solid #45475a;white-space:nowrap}
tr:hover td{background:#45475a}
.act{display:inline-block;padding:2px 8px;border-radius:4px;font-weight:600;font-size:.85em}
.act-KEEP{background:#a6e3a1;color:#1e1e2e} .act-MOVE,.act-DRYRUN{background:#f9e2af;color:#1e1e2e}
.act-JUNK,.act-DRYRUN-JUNK{background:#f38ba8;color:#1e1e2e}
.act-BIOS-MOVE,.act-DRYRUN-BIOS{background:#89b4fa;color:#1e1e2e}
.sz{text-align:right;font-variant-numeric:tabular-nums}
.filter-bar{margin-bottom:12px;display:flex;gap:8px;align-items:center;flex-wrap:wrap}
.filter-bar input{background:#313244;border:1px solid #585b70;color:#cdd6f4;padding:6px 12px;border-radius:6px;font-size:.9em;width:300px}
.filter-bar .btn{padding:5px 14px;border-radius:6px;border:1px solid #585b70;background:#313244;color:#cdd6f4;cursor:pointer;font-size:.85em}
.filter-bar .btn.active{background:#89b4fa;color:#1e1e2e;border-color:#89b4fa}
</style></head><body>
"@
  [void]$sb.AppendLine($header)

  # Header
  $modeEsc = & $htmlSafe $Mode
  [void]$sb.AppendLine(('<h1>&#x1F4CB; ROM Cleanup Report</h1>'))
  [void]$sb.AppendLine(('<p class="meta">Modus: <strong>{0}</strong> &bull; {1}</p>' -f $modeEsc, (Get-Date -Format "dd.MM.yyyy HH:mm:ss")))

  # Summary Cards
  [void]$sb.AppendLine('<div class="cards">')
  [void]$sb.AppendLine(('<div class="card green"><div class="val">{0}</div><div class="lbl">Spiele (KEEP)</div></div>' -f $UniqueGames))
  if ($DupeGroups -gt 0) {
    [void]$sb.AppendLine(('<div class="card yellow"><div class="val">{0}</div><div class="lbl">Duplikate</div></div>' -f $TotalDupes))
  }
  if ($JunkCount -gt 0) {
    [void]$sb.AppendLine(('<div class="card red"><div class="val">{0}</div><div class="lbl">Junk ({1} MB)</div></div>' -f $JunkCount, $junkMB))
  }
  if ($BiosCount -gt 0) {
    [void]$sb.AppendLine(('<div class="card blue"><div class="val">{0}</div><div class="lbl">BIOS/Firmware</div></div>' -f $BiosCount))
  }
  if ($datMatchCount -gt 0) {
    [void]$sb.AppendLine(('<div class="card"><div class="val">{0}</div><div class="lbl">DAT Matches</div></div>' -f $datMatchCount))
  }
  if ($DatEnabled -and $datExpectedTotal -gt 0) {
    [void]$sb.AppendLine(('<div class="card red"><div class="val">{0}</div><div class="lbl">DAT Fehlend</div></div>' -f $datMissingTotal))
  }
  [void]$sb.AppendLine(('<div class="card"><div class="val">{0} MB</div><div class="lbl">Gespart</div></div>' -f $savedMB))
  [void]$sb.AppendLine(('<div class="card"><div class="val">{0}</div><div class="lbl">Gescannt</div></div>' -f $TotalScanned))
  [void]$sb.AppendLine('</div>')

  $keepCount = @($sortedReport | Where-Object { $_.Category -eq 'GAME' -and $_.Action -eq 'KEEP' }).Count
  $moveCount = @($sortedReport | Where-Object { $_.Category -eq 'GAME' -and $_.Action -in @('MOVE','SKIP_DRYRUN') }).Count
  $junkActionCount = @($sortedReport | Where-Object { $_.Action -in @('JUNK','DRYRUN-JUNK') }).Count
  $biosActionCount = @($sortedReport | Where-Object { $_.Action -in @('BIOS-MOVE','DRYRUN-BIOS') }).Count

  [void]$sb.AppendLine('<div style="margin:6px 0 16px 0">')
  [void]$sb.AppendLine('<h3>Collection-Statistik-Dashboard</h3>')

  # SVG Pie Chart (Action distribution)
  $pieData = @(
    @{ Label='Keep';  Value=$keepCount;      Color='#a6e3a1' },
    @{ Label='Move';  Value=$moveCount;      Color='#f9e2af' },
    @{ Label='Junk';  Value=$junkActionCount; Color='#f38ba8' },
    @{ Label='BIOS';  Value=$biosActionCount; Color='#89b4fa' }
  ) | Where-Object { $_.Value -gt 0 }
  $pieTotal = 0; foreach ($pd in $pieData) { $pieTotal += $pd.Value }
  if ($pieTotal -gt 0) {
    [void]$sb.AppendLine('<div style="display:flex;gap:32px;align-items:center;flex-wrap:wrap">')
    [void]$sb.AppendLine('<svg viewBox="-1.1 -1.1 2.2 2.2" width="180" height="180" style="transform:rotate(-90deg)">')
    $cumulative = 0.0
    foreach ($slice in $pieData) {
      $pct = $slice.Value / $pieTotal
      if ($pieData.Count -eq 1) {
        [void]$sb.AppendLine(('<circle cx="0" cy="0" r="1" fill="{0}"/>' -f $slice.Color))
      } else {
        $startAngle = $cumulative * 2 * [math]::PI
        $endAngle   = ($cumulative + $pct) * 2 * [math]::PI
        $largeArc   = if ($pct -gt 0.5) { 1 } else { 0 }
        $x1 = [math]::Round([math]::Cos($startAngle), 6).ToString([System.Globalization.CultureInfo]::InvariantCulture)
        $y1 = [math]::Round([math]::Sin($startAngle), 6).ToString([System.Globalization.CultureInfo]::InvariantCulture)
        $x2 = [math]::Round([math]::Cos($endAngle), 6).ToString([System.Globalization.CultureInfo]::InvariantCulture)
        $y2 = [math]::Round([math]::Sin($endAngle), 6).ToString([System.Globalization.CultureInfo]::InvariantCulture)
        [void]$sb.AppendLine(('<path d="M {0} {1} A 1 1 0 {2} 1 {3} {4} L 0 0 Z" fill="{5}"/>' -f $x1, $y1, $largeArc, $x2, $y2, $slice.Color))
      }
      $cumulative += $pct
    }
    [void]$sb.AppendLine('</svg>')
    # Legend
    [void]$sb.AppendLine('<div>')
    foreach ($slice in $pieData) {
      $pctText = [math]::Round(($slice.Value / $pieTotal) * 100, 1).ToString([System.Globalization.CultureInfo]::InvariantCulture)
      [void]$sb.AppendLine(('<div style="margin:4px 0"><span style="display:inline-block;width:12px;height:12px;background:{0};border-radius:2px;margin-right:6px"></span>{1}: {2} ({3}%)</div>' -f $slice.Color, $slice.Label, $slice.Value, $pctText))
    }
    [void]$sb.AppendLine('</div></div>')
  }

  # SVG Horizontal Bar Chart
  [void]$sb.AppendLine('<svg width="100%" viewBox="0 0 500 120" style="margin-top:12px;max-width:600px">')
  $barItems = @(
    @{ Label='Keep';      Value=$keepCount;      Color='#a6e3a1' },
    @{ Label='Move';      Value=$moveCount;      Color='#f9e2af' },
    @{ Label='Junk';      Value=$junkActionCount; Color='#f38ba8' },
    @{ Label='BIOS';      Value=$biosActionCount; Color='#89b4fa' }
  )
  $barMax = 1; foreach ($bi in $barItems) { if ([int]$bi.Value -gt $barMax) { $barMax = [int]$bi.Value } }
  $barY = 0
  foreach ($bar in $barItems) {
    $barW = [int][math]::Round(($bar.Value / $barMax) * 380)
    [void]$sb.AppendLine(('<text x="0" y="{0}" fill="#cdd6f4" font-size="13" dominant-baseline="middle">{1}</text>' -f ($barY + 14), $bar.Label))
    [void]$sb.AppendLine(('<rect x="60" y="{0}" width="{1}" height="20" rx="3" fill="{2}"/>' -f ($barY + 4), $barW, $bar.Color))
    $txtX = [math]::Max(65, $barW + 65)
    [void]$sb.AppendLine(('<text x="{0}" y="{1}" fill="#cdd6f4" font-size="11" dominant-baseline="middle">{2}</text>' -f $txtX, ($barY + 14), $bar.Value))
    $barY += 28
  }
  [void]$sb.AppendLine('</svg>')
  [void]$sb.AppendLine('</div>')

  if ($DatEnabled -and $datCompletenessRows.Count -gt 0) {
    [void]$sb.AppendLine('<div style="margin:6px 0 16px 0">')
    [void]$sb.AppendLine('<h3>DAT Vollstaendigkeit pro Konsole</h3>')
    [void]$sb.AppendLine('<table><thead><tr><th>Konsole</th><th>Gefunden</th><th>Erwartet (DAT)</th><th>Fehlend</th><th>Quote</th></tr></thead><tbody>')
    foreach ($entry in $datCompletenessRows) {
      $consoleEsc = & $htmlSafe $entry.Console
      [void]$sb.AppendLine(('<tr><td>{0}</td><td class="sz">{1}</td><td class="sz">{2}</td><td class="sz">{3}</td><td class="sz">{4}%</td></tr>' -f $consoleEsc, $entry.Matched, $entry.Expected, $entry.Missing, $entry.Coverage))
    }
    [void]$sb.AppendLine('</tbody></table></div>')

    [void]$sb.AppendLine('<div style="margin:6px 0 16px 0">')
    [void]$sb.AppendLine('<h3>DAT Coverage Heatmap (Top 20)</h3>')
    [void]$sb.AppendLine('<table><thead><tr><th>Konsole</th><th>Coverage</th><th>Heat</th><th>Fehlend/Erwartet</th></tr></thead><tbody>')
    foreach ($entry in @($datCompletenessRows | Sort-Object Coverage -Descending | Select-Object -First 20)) {
      $coverage = [double]$entry.Coverage
      $heat = if ($coverage -ge 90) { '#####' } elseif ($coverage -ge 75) { '####-' } elseif ($coverage -ge 50) { '###--' } elseif ($coverage -ge 25) { '##---' } else { '#----' }
      $consoleEsc = & $htmlSafe $entry.Console
      [void]$sb.AppendLine(('<tr><td>{0}</td><td class="sz">{1}%</td><td>{2}</td><td class="sz">{3}/{4}</td></tr>' -f $consoleEsc, $coverage.ToString('0.0'), $heat, [int]$entry.Missing, [int]$entry.Expected))
    }
    [void]$sb.AppendLine('</tbody></table></div>')
  }

  try {
    # REPORT-009 FIX: Use the report output directory instead of non-deterministic Get-Location
    $deltaPath = Join-Path (Split-Path -Parent $resolvedHtmlPath) 'dryrun-delta-latest.json'
    if (Test-Path -LiteralPath $deltaPath -PathType Leaf) {
      $delta = Get-Content -LiteralPath $deltaPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
      if ($delta) {
        $added = if ($delta.PSObject.Properties.Name -contains 'AddedCount') { [int]$delta.AddedCount } else { 0 }
        $resolved = if ($delta.PSObject.Properties.Name -contains 'ResolvedCount') { [int]$delta.ResolvedCount } else { 0 }
        $unchanged = if ($delta.PSObject.Properties.Name -contains 'UnchangedCount') { [int]$delta.UnchangedCount } else { 0 }
        $current = if ($delta.PSObject.Properties.Name -contains 'CurrentCount') { [int]$delta.CurrentCount } else { 0 }
        $previous = if ($delta.PSObject.Properties.Name -contains 'PreviousCount') { [int]$delta.PreviousCount } else { 0 }

        [void]$sb.AppendLine('<div style="margin:6px 0 16px 0">')
        [void]$sb.AppendLine('<h3>Incremental DryRun Delta</h3>')
        [void]$sb.AppendLine('<table><thead><tr><th>Aktuell</th><th>Vorher</th><th>Neu</th><th>Aufgeloest</th><th>Unveraendert</th></tr></thead><tbody>')
        [void]$sb.AppendLine(('<tr><td class="sz">{0}</td><td class="sz">{1}</td><td class="sz">{2}</td><td class="sz">{3}</td><td class="sz">{4}</td></tr>' -f $current, $previous, $added, $resolved, $unchanged))
        [void]$sb.AppendLine('</tbody></table></div>')
      }
    }
  } catch {
    if (Get-Command Write-CatchGuardLog -ErrorAction SilentlyContinue) {
      [void](Write-CatchGuardLog -Module 'Report' -Action 'Write-HtmlReport/DeltaSection' -Exception $_.Exception -Level 'Warning')
    }
  }
  if ($consoleStats -and $consoleStats.Count -gt 0) {    [void]$sb.AppendLine('<div style="margin:6px 0 16px 0">')
    [void]$sb.AppendLine('<h3>DAT Match Quote pro Konsole</h3>')
    [void]$sb.AppendLine('<table><thead><tr><th>Konsole</th><th>Match</th><th>Total</th><th>Quote</th></tr></thead><tbody>')
    foreach ($k in $consoleStats.Keys | Sort-Object) {
      $t = [int]$consoleStats[$k].total
      $m = [int]$consoleStats[$k].match
      $pct = if ($t -gt 0) { [math]::Round(($m / $t) * 100, 1) } else { 0 }
      $kEsc = & $htmlSafe $k
      [void]$sb.AppendLine(('<tr><td>{0}</td><td class="sz">{1}</td><td class="sz">{2}</td><td class="sz">{3}%</td></tr>' -f $kEsc, $m, $t, $pct))
    }
    [void]$sb.AppendLine('</tbody></table></div>')
  }

  if ($ConsoleSortUnknownReasons -and $ConsoleSortUnknownReasons.Count -gt 0) {
    $getUnknownReasonLabel = {
      param([string]$Code)
      switch ($Code) {
        'ARCHIVE_AMBIGUOUS_EXT' { 'Archive: keine eindeutigen Extensions' }
        'ARCHIVE_TOOL_MISSING' { 'Archive: 7z nicht verfuegbar' }
        'ARCHIVE_DISC_HEADER_MISSING' { 'Archive: kein Disc-Header gefunden' }
        'DOLPHIN_DISC_ID_MISSING' { 'DolphinTool: keine Disc-ID' }
        'HEURISTIC_NO_MATCH' { 'Heuristik: kein Match' }
        'DISC_HEADER_IO_ERROR' { 'Disc-Header: Datei nicht lesbar (IO)' }
        default {
          if ([string]::IsNullOrWhiteSpace($Code)) { 'Unbekannt' } else { $Code }
        }
      }
    }

    [void]$sb.AppendLine('<div style="margin:6px 0 16px 0">')
    [void]$sb.AppendLine('<h3>Konsolen-Sortierung: UNKNOWN Gruende</h3>')
    [void]$sb.AppendLine('<table><thead><tr><th>Reason Code</th><th>Grund</th><th>Anzahl</th></tr></thead><tbody>')
    foreach ($k in ($ConsoleSortUnknownReasons.Keys | Sort-Object)) {
      $kEsc = & $htmlSafe ([string]$k)
      $labelEsc = & $htmlSafe (& $getUnknownReasonLabel $k)
      $v = [int]$ConsoleSortUnknownReasons[$k]
      [void]$sb.AppendLine(('<tr><td>{0}</td><td>{1}</td><td class="sz">{2}</td></tr>' -f $kEsc, $labelEsc, $v))
    }
    [void]$sb.AppendLine('</tbody></table>')

    # DOC-01: UNKNOWN-Diagnose Hinweise fuer Endbenutzer
    [void]$sb.AppendLine('<details style="margin:8px 0"><summary style="cursor:pointer;color:#00F5FF;font-weight:bold">Warum werden Dateien als UNBEKANNT eingestuft?</summary>')
    [void]$sb.AppendLine('<div style="padding:8px;background:#13133A;border-radius:6px;margin-top:4px;font-size:12px;line-height:1.6">')
    [void]$sb.AppendLine('<p><strong>Haeufige Gruende:</strong></p><ul>')
    [void]$sb.AppendLine('<li><strong>Keine DAT-Dateien:</strong> Ohne DAT-Dateien kann kein Hash-Abgleich stattfinden. Laden Sie DAT-Dateien fuer Ihre Konsolen herunter (z.B. von No-Intro oder Redump).</li>')
    [void]$sb.AppendLine('<li><strong>Mehrdeutige Dateiendung:</strong> Endungen wie .iso, .bin, .cue, .chd werden von mehreren Konsolen genutzt. Verschieben Sie die Datei in einen Ordner mit dem Konsolennamen (z.B. <code>PS1/</code>, <code>DC/</code>).</li>')
    [void]$sb.AppendLine('<li><strong>Disc-Header nicht lesbar:</strong> Die Datei konnte nicht geoeffnet werden (Berechtigung, gesperrt, beschaedigt). Pruefen Sie die Dateiberechtigungen.</li>')
    [void]$sb.AppendLine('<li><strong>7-Zip nicht verfuegbar:</strong> Archive (.zip/.7z) koennen ohne 7z-Tool nicht analysiert werden. Installieren Sie 7-Zip und konfigurieren Sie den Pfad in den Einstellungen.</li>')
    [void]$sb.AppendLine('<li><strong>DolphinTool fehlt:</strong> GameCube/Wii-Formate (.rvz, .gcz) benoetigen DolphinTool zur Erkennung. Installieren Sie Dolphin Emulator.</li>')
    [void]$sb.AppendLine('</ul>')
    [void]$sb.AppendLine('<p><strong>Empfehlung:</strong> Verschieben Sie unbekannte Dateien in Ordner mit dem Konsolennamen (z.B. <code>PS1/</code>, <code>dreamcast/</code>, <code>saturn/</code>). Die Ordner-Erkennung ordnet sie dann automatisch korrekt zu.</p>')
    [void]$sb.AppendLine('</div></details>')

    [void]$sb.AppendLine('</div>')
  }

  if ($DatEnabled -and $DatArchiveStats -and $DatArchiveStats.Count -gt 0) {
    [void]$sb.AppendLine('<div style="margin:6px 0 16px 0">')
    [void]$sb.AppendLine('<h3>DAT Archive Hashing: uebersprungen/Fehler</h3>')
    [void]$sb.AppendLine('<table><thead><tr><th>Grund</th><th>Anzahl</th></tr></thead><tbody>')
    foreach ($k in ($DatArchiveStats.Keys | Sort-Object)) {
      $kEsc = & $htmlSafe $k
      $v = [int]$DatArchiveStats[$k]
      [void]$sb.AppendLine(('<tr><td>{0}</td><td class="sz">{1}</td></tr>' -f $kEsc, $v))
    }
    [void]$sb.AppendLine('</tbody></table></div>')
  }

  # Filter bar (BUG REPORT-010: inline handlers removed; event delegation in script block)
  [void]$sb.AppendLine('<div class="filter-bar">')
  [void]$sb.AppendLine('<input type="text" id="search" placeholder="Suchen... (Name, Pfad, Region)">')
  [void]$sb.AppendLine('<span>')
  foreach ($a in @('ALL','KEEP','MOVE','JUNK','BIOS')) {
    $cls = if ($a -eq 'ALL') { 'btn active' } else { 'btn' }
    [void]$sb.AppendLine(('<button class="{0}" data-filter="{1}">{1}</button>' -f $cls, $a))
  }
  [void]$sb.AppendLine('</span>')
  [void]$sb.AppendLine('<span>')
  foreach ($a in @('DAT-ALL','DAT-YES','DAT-NO')) {
    $cls = if ($a -eq 'DAT-ALL') { 'btn active' } else { 'btn' }
    [void]$sb.AppendLine(('<button class="{0}" data-dat="{1}">{1}</button>' -f $cls, $a))
  }
  [void]$sb.AppendLine('</span></div>')

  # Table
  [void]$sb.AppendLine('<table id="reportTable"><thead><tr>')
  foreach ($h in @('#','GameKey','Action','Category','Region','WinnerRegion','VersionScore','FormatScore','Type','DatMatch','Size','MainPath')) {
    [void]$sb.AppendLine(('<th>{0}</th>' -f $h))
  }
  [void]$sb.AppendLine('</tr></thead><tbody>')

  $rowIdx = 0
  foreach ($r in $sortedReport) {
    $rowIdx++
    $sizeMB = [math]::Round($r.SizeBytes / 1MB, 2)
    $actToken = ($r.Action -replace '[^A-Za-z0-9_-]', '')
    if ([string]::IsNullOrWhiteSpace($actToken)) { $actToken = 'OTHER' }
    $actClass = "act act-$actToken"
    $catIcon = if ($catIcons.ContainsKey($r.Category)) { $catIcons[$r.Category] } else { '' }
    $escaped = & $htmlSafe $r.MainPath
    $gameEsc = & $htmlSafe $r.GameKey
    $actEsc = & $htmlSafe $r.Action
    $catEsc = & $htmlSafe $r.Category
    $regionEsc = & $htmlSafe $r.Region
    $winnerEsc = & $htmlSafe $r.WinnerRegion
    $typeEsc = & $htmlSafe $r.Type

    $datFlag = if ($r.DatMatch) { 'yes' } else { 'no' }
    $actAttrEsc = ConvertTo-HtmlAttributeSafe $r.Action
    $datAttrEsc = ConvertTo-HtmlAttributeSafe $datFlag
    [void]$sb.Append('<tr')
    [void]$sb.Append((' data-action="{0}"' -f $actAttrEsc))
    [void]$sb.Append((' data-dat="{0}"' -f $datAttrEsc))
    [void]$sb.AppendLine('>')
    [void]$sb.AppendLine(('<td>{0}</td>' -f $rowIdx))
    [void]$sb.AppendLine(('<td>{0}</td>' -f $gameEsc))
    [void]$sb.AppendLine(('<td><span class="{0}">{1}</span></td>' -f $actClass, $actEsc))
    [void]$sb.AppendLine(('<td>{0} {1}</td>' -f $catIcon, $catEsc))
    [void]$sb.AppendLine(('<td>{0}</td>' -f $regionEsc))
    [void]$sb.AppendLine(('<td>{0}</td>' -f $winnerEsc))
    # REPORT-004 FIX: HTML-encode score values for defense-in-depth
    [void]$sb.AppendLine(('<td class="sz">{0}</td>' -f (& $htmlSafe ([string]$r.VersionScore))))
    [void]$sb.AppendLine(('<td class="sz">{0}</td>' -f (& $htmlSafe ([string]$r.FormatScore))))
    [void]$sb.AppendLine(('<td>{0}</td>' -f $typeEsc))
    [void]$sb.AppendLine(('<td>{0}</td>' -f $datFlag))
    [void]$sb.AppendLine(('<td class="sz">{0} MB</td>' -f $sizeMB))
    [void]$sb.AppendLine(('<td title="{0}">{1}</td>' -f $escaped, $escaped))
    [void]$sb.AppendLine('</tr>')

    if ($useStreamingWrite -and ($rowIdx % $flushEveryRows -eq 0) -and $sb.Length -gt 0) {
      try {
        $sb.ToString() | Out-File -LiteralPath $HtmlPath -Encoding utf8 -Append
      } catch {
        throw "REPORT-008: Failed to write HTML report chunk (row $rowIdx). File may be locked: $HtmlPath. Error: $($_.Exception.Message)"
      }
      [void]$sb.Clear()
    }
  }

  [void]$sb.AppendLine('</tbody></table>')

  # Scripts (BUG REPORT-010 FIX: nonce-based CSP; event delegation replaces inline handlers)
  $nonceAttr = if ($cspNonce) { ' nonce="{0}"' -f $cspNonce } else { '' }
  $scriptBlock = @"
<script${nonceAttr}>
var activeFilter="ALL";var datFilter="DAT-ALL";
function removeActive(selector){var buttons=document.querySelectorAll(selector);for(var i=0;i<buttons.length;i++){buttons[i].classList.remove("active");}}
function toggleFilter(el){removeActive(".filter-bar .btn[data-filter]");el.classList.add("active");activeFilter=el.getAttribute("data-filter");applyFilters();}
function toggleDatFilter(el){removeActive(".filter-bar .btn[data-dat]");el.classList.add("active");datFilter=el.getAttribute("data-dat");applyFilters();}
function applyFilters(){var searchEl=document.getElementById("search");var q=(searchEl&&searchEl.value?searchEl.value:"").toLowerCase();var rows=document.querySelectorAll("#reportTable tbody tr");for(var i=0;i<rows.length;i++){var r=rows[i];var act=r.getAttribute("data-action");var dat=r.getAttribute("data-dat");var txt=(r.textContent||r.innerText||"").toLowerCase();var matchAct=activeFilter==="ALL"||act===activeFilter||(activeFilter==="MOVE"&&(act==="MOVE"||act==="SKIP_DRYRUN"))||(activeFilter==="JUNK"&&(act==="JUNK"||act==="DRYRUN-JUNK"))||(activeFilter==="BIOS"&&(act==="BIOS-MOVE"||act==="DRYRUN-BIOS"));var matchDat=datFilter==="DAT-ALL"||dat===(datFilter==="DAT-YES"?"yes":"no");var matchTxt=!q||txt.indexOf(q)!==-1;r.style.display=matchAct&&matchDat&&matchTxt?"":"none";}}
var sortDir={};function sortTable(th){var idx=0;var ths=th.parentNode.children;for(var i=0;i<ths.length;i++){if(ths[i]===th){idx=i;break;}}var tb=document.querySelector("#reportTable tbody");if(!tb){return;}var rows=[];for(var r=0;r<tb.rows.length;r++){rows.push(tb.rows[r]);}sortDir[idx]=!sortDir[idx];rows.sort(function(a,b){var va=(a.cells[idx].textContent||a.cells[idx].innerText||"").trim();var vb=(b.cells[idx].textContent||b.cells[idx].innerText||"").trim();var na=parseFloat(va),nb=parseFloat(vb);if(!isNaN(na)&&!isNaN(nb)){va=na;vb=nb;}if(va<vb){return sortDir[idx]?-1:1;}if(va>vb){return sortDir[idx]?1:-1;}return 0;});for(var j=0;j<rows.length;j++){tb.appendChild(rows[j]);}}
function addEvt(el,evt,fn){if(el.addEventListener){el.addEventListener(evt,fn,false);}else if(el.attachEvent){el.attachEvent("on"+evt,fn);}else{el["on"+evt]=fn;}}function findClosest(el,id){var n=el;while(n){if(n.id===id){return n;}n=n.parentNode;}return null;}addEvt(document,"click",function(e){e=e||window.event;var t=e.target||e.srcElement;if(t.getAttribute&&t.getAttribute("data-filter")){toggleFilter(t);}else if(t.getAttribute&&t.getAttribute("data-dat")){toggleDatFilter(t);}else if(t.tagName==="TH"&&findClosest(t,"reportTable")){sortTable(t);}});var se=document.getElementById("search");if(se){addEvt(se,"input",function(){applyFilters();});addEvt(se,"propertychange",function(){applyFilters();});}
</script>
</body></html>
"@
  [void]$sb.AppendLine($scriptBlock)

  if ($useStreamingWrite) {
    if ($sb.Length -gt 0) {
      try {
        $sb.ToString() | Out-File -LiteralPath $HtmlPath -Encoding utf8 -Append
      } catch {
        throw "REPORT-008: Failed to write final HTML report chunk. File may be locked: $HtmlPath. Error: $($_.Exception.Message)"
      }
    }
  } else {
    # REPORT-001 FIX: Use -LiteralPath to handle paths with brackets (e.g. [USA])
    try {
      $sb.ToString() | Out-File -LiteralPath $HtmlPath -Encoding utf8 -Force
    } catch {
      throw "REPORT-008: Failed to write HTML report. File may be locked: $HtmlPath. Error: $($_.Exception.Message)"
    }
  }
}

function Add-AuditLinksToHtml {
  param(
    [string]$HtmlPath,
    [string[]]$AuditPaths
  )

  if (-not (Test-Path -LiteralPath $HtmlPath)) { return }
  if (-not $AuditPaths -or $AuditPaths.Count -eq 0) { return }

  $links = New-Object System.Text.StringBuilder 1024
  [void]$links.AppendLine('<div style="margin-top:18px">')
  [void]$links.AppendLine('<h3>Audit Logs</h3>')
  [void]$links.AppendLine('<ul>')
  foreach ($p in $AuditPaths) {
    if (-not (Test-Path -LiteralPath $p)) { continue }
    # REPORT-005 FIX: HTML-entity-encode the URI for safe use in href attribute
    $uri = [System.Net.WebUtility]::HtmlEncode((New-Object System.Uri($p)).AbsoluteUri)
    $name = [System.Net.WebUtility]::HtmlEncode([IO.Path]::GetFileName($p))
    [void]$links.AppendLine(('<li><a href="{0}">{1}</a></li>' -f $uri, $name))
  }
  [void]$links.AppendLine('</ul></div>')

  $html = Get-Content -LiteralPath $HtmlPath -Raw -ErrorAction SilentlyContinue
  if (-not $html) { return }
  if ($html -match '</body>') {
    # REPORT-007 FIX: Use String.Replace() to avoid $N backreference corruption in -replace
    $html = $html.Replace('</body>', ($links.ToString() + '</body>'))
  } else {
    $html += $links.ToString()
  }
  $html | Out-File -LiteralPath $HtmlPath -Encoding utf8 -Force
}
