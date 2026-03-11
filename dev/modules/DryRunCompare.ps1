# ================================================================
#  DRYRUN COMPARE – Side-by-Side DryRun-Vergleich (MF-21)
#  Dependencies: ReportBuilder.ps1
# ================================================================

function Compare-DryRunResults {
  <#
  .SYNOPSIS
    Vergleicht zwei DryRun-Ergebnisse Side-by-Side.
  .PARAMETER ResultA
    Ergebnis des ersten DryRuns (Array von Items).
  .PARAMETER ResultB
    Ergebnis des zweiten DryRuns (Array von Items).
  .PARAMETER KeyField
    Feld fuer den Abgleich (z.B. 'SourcePath' oder 'OldPath').
  #>
  param(
    [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$ResultA,
    [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$ResultB,
    [string]$KeyField = 'OldPath'
  )

  $itemsA = @{}
  $itemsB = @{}

  if ($ResultA) {
    foreach ($item in $ResultA) {
      $key = if ($item -is [hashtable] -and $item.ContainsKey($KeyField)) { $item[$KeyField] } else { $null }
      if ($key) { $itemsA[$key] = $item }
    }
  }

  if ($ResultB) {
    foreach ($item in $ResultB) {
      $key = if ($item -is [hashtable] -and $item.ContainsKey($KeyField)) { $item[$KeyField] } else { $null }
      if ($key) { $itemsB[$key] = $item }
    }
  }

  $allKeys = @(@($itemsA.Keys) + @($itemsB.Keys) | Sort-Object -Unique)

  $onlyA = @()
  $onlyB = @()
  $different = @()
  $identical = @()

  foreach ($key in $allKeys) {
    $inA = $itemsA.ContainsKey($key)
    $inB = $itemsB.ContainsKey($key)

    if ($inA -and -not $inB) {
      $onlyA += $itemsA[$key]
    } elseif ($inB -and -not $inA) {
      $onlyB += $itemsB[$key]
    } else {
      # Beide vorhanden - vergleiche Zielverhalten
      $targetA = if ($itemsA[$key].ContainsKey('NewPath')) { $itemsA[$key].NewPath } else { '' }
      $targetB = if ($itemsB[$key].ContainsKey('NewPath')) { $itemsB[$key].NewPath } else { '' }
      $actionA = if ($itemsA[$key].ContainsKey('Action')) { $itemsA[$key].Action } else { '' }
      $actionB = if ($itemsB[$key].ContainsKey('Action')) { $itemsB[$key].Action } else { '' }

      if ($targetA -eq $targetB -and $actionA -eq $actionB) {
        $identical += @{ Key = $key; ItemA = $itemsA[$key]; ItemB = $itemsB[$key] }
      } else {
        $different += @{
          Key    = $key
          ItemA  = $itemsA[$key]
          ItemB  = $itemsB[$key]
          DiffTarget = ($targetA -ne $targetB)
          DiffAction = ($actionA -ne $actionB)
        }
      }
    }
  }

  return @{
    OnlyInA   = $onlyA
    OnlyInB   = $onlyB
    Different = $different
    Identical = $identical
    Summary   = @{
      TotalKeys  = $allKeys.Count
      OnlyA      = $onlyA.Count
      OnlyB      = $onlyB.Count
      Different  = $different.Count
      Identical  = $identical.Count
    }
  }
}

function Get-DryRunComparisonSummary {
  <#
  .SYNOPSIS
    Erstellt eine menschenlesbare Zusammenfassung eines DryRun-Vergleichs.
  .PARAMETER Comparison
    Ergebnis von Compare-DryRunResults.
  .PARAMETER LabelA
    Bezeichnung fuer DryRun A.
  .PARAMETER LabelB
    Bezeichnung fuer DryRun B.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Comparison,
    [string]$LabelA = 'DryRun A',
    [string]$LabelB = 'DryRun B'
  )

  $s = $Comparison.Summary

  $lines = @()
  $lines += "Vergleich: $LabelA vs. $LabelB"
  $lines += "  Identisch:       $($s.Identical)"
  $lines += "  Unterschiedlich: $($s.Different)"
  $lines += "  Nur in $($LabelA): $($s.OnlyA)"
  $lines += "  Nur in $($LabelB): $($s.OnlyB)"

  return @{
    Text       = ($lines -join "`n")
    HasChanges = ($s.Different -gt 0 -or $s.OnlyA -gt 0 -or $s.OnlyB -gt 0)
  }
}

function Export-DryRunComparisonHtml {
  <#
  .SYNOPSIS
    Generiert einen HTML-Diff-Report aus einem DryRun-Vergleich.
  .PARAMETER ComparisonResult
    Ergebnis von Compare-DryRunResults.
  .PARAMETER LabelA
    Bezeichnung fuer Run A.
  .PARAMETER LabelB
    Bezeichnung fuer Run B.
  .PARAMETER OutputPath
    Ziel-HTML-Datei (optional, gibt sonst String zurueck).
  #>
  param(
    [Parameter(Mandatory)][hashtable]$ComparisonResult,
    [string]$LabelA = 'Run A',
    [string]$LabelB = 'Run B',
    [string]$OutputPath = ''
  )

  Add-Type -AssemblyName System.Web -ErrorAction SilentlyContinue
  $enc = [System.Web.HttpUtility]

  $identical = $ComparisonResult.Identical
  $different = $ComparisonResult.Different
  $onlyA     = $ComparisonResult.OnlyInA
  $onlyB     = $ComparisonResult.OnlyInB
  $summary   = $ComparisonResult.Summary

  $safeA = $enc::HtmlEncode($LabelA)
  $safeB = $enc::HtmlEncode($LabelB)

  $html = [System.Text.StringBuilder]::new(8192)
  [void]$html.AppendLine('<!-- saved from url=(0016)http://localhost -->')
  [void]$html.AppendLine('<!DOCTYPE html><html lang="de"><head><meta charset="utf-8">')
  [void]$html.AppendLine("<title>DryRun-Vergleich: $safeA vs $safeB</title>")
  [void]$html.AppendLine('<style>')
  [void]$html.AppendLine('body{font-family:"Segoe UI",sans-serif;background:#1a1a2e;color:#e0e0e0;margin:2em}')
  [void]$html.AppendLine('h1{color:#0ff}h2{color:#0f0;border-bottom:1px solid #333;padding-bottom:.3em}')
  [void]$html.AppendLine('table{border-collapse:collapse;width:100%;margin-bottom:2em}')
  [void]$html.AppendLine('th{background:#16213e;color:#0ff;text-align:left;padding:8px 12px}')
  [void]$html.AppendLine('td{padding:6px 12px;border-bottom:1px solid #222}')
  [void]$html.AppendLine('tr:hover{background:#16213e}')
  [void]$html.AppendLine('.stat{display:inline-block;padding:8px 20px;margin:4px;border-radius:6px;font-size:1.1em}')
  [void]$html.AppendLine('.identical{background:#0a3d0a;color:#0f0}.different{background:#3d3d0a;color:#ff0}')
  [void]$html.AppendLine('.only-a{background:#3d0a0a;color:#f66}.only-b{background:#0a0a3d;color:#66f}')
  [void]$html.AppendLine('</style></head><body>')

  [void]$html.AppendLine('<h1>DryRun-Vergleich</h1>')
  [void]$html.AppendLine("<p><strong>$safeA</strong> vs <strong>$safeB</strong></p>")

  # Summary-Kacheln
  [void]$html.AppendLine('<div>')
  [void]$html.AppendLine("<span class='stat identical'>Identisch: $($summary.Identical)</span>")
  [void]$html.AppendLine("<span class='stat different'>Unterschiedlich: $($summary.Different)</span>")
  [void]$html.AppendLine("<span class='stat only-a'>Nur $safeA`: $($summary.OnlyA)</span>")
  [void]$html.AppendLine("<span class='stat only-b'>Nur $safeB`: $($summary.OnlyB)</span>")
  [void]$html.AppendLine('</div>')

  # Unterschiedliche Eintraege
  if ($different.Count -gt 0) {
    [void]$html.AppendLine('<h2>Unterschiedliche Zuordnungen</h2>')
    [void]$html.AppendLine("<table><tr><th>Datei</th><th>$safeA Aktion</th><th>$safeA Ziel</th><th>$safeB Aktion</th><th>$safeB Ziel</th></tr>")
    foreach ($d in $different) {
      $key = $enc::HtmlEncode($d.Key)
      $actA = $enc::HtmlEncode($(if ($d.ItemA.ContainsKey('Action')) { $d.ItemA.Action } else { '' }))
      $tgtA = $enc::HtmlEncode($(if ($d.ItemA.ContainsKey('NewPath')) { $d.ItemA.NewPath } else { '' }))
      $actB = $enc::HtmlEncode($(if ($d.ItemB.ContainsKey('Action')) { $d.ItemB.Action } else { '' }))
      $tgtB = $enc::HtmlEncode($(if ($d.ItemB.ContainsKey('NewPath')) { $d.ItemB.NewPath } else { '' }))
      [void]$html.AppendLine("<tr><td>$key</td><td>$actA</td><td>$tgtA</td><td>$actB</td><td>$tgtB</td></tr>")
    }
    [void]$html.AppendLine('</table>')
  }

  # Nur in A
  if ($onlyA.Count -gt 0) {
    [void]$html.AppendLine("<h2>Nur in $safeA</h2>")
    [void]$html.AppendLine('<table><tr><th>Datei</th><th>Aktion</th><th>Ziel</th></tr>')
    foreach ($a in $onlyA) {
      $path   = $enc::HtmlEncode($(if ($a.ContainsKey('OldPath')) { $a.OldPath } else { '' }))
      $action = $enc::HtmlEncode($(if ($a.ContainsKey('Action'))  { $a.Action }  else { '' }))
      $target = $enc::HtmlEncode($(if ($a.ContainsKey('NewPath')) { $a.NewPath } else { '' }))
      [void]$html.AppendLine("<tr><td>$path</td><td>$action</td><td>$target</td></tr>")
    }
    [void]$html.AppendLine('</table>')
  }

  # Nur in B
  if ($onlyB.Count -gt 0) {
    [void]$html.AppendLine("<h2>Nur in $safeB</h2>")
    [void]$html.AppendLine('<table><tr><th>Datei</th><th>Aktion</th><th>Ziel</th></tr>')
    foreach ($b in $onlyB) {
      $path   = $enc::HtmlEncode($(if ($b.ContainsKey('OldPath')) { $b.OldPath } else { '' }))
      $action = $enc::HtmlEncode($(if ($b.ContainsKey('Action'))  { $b.Action }  else { '' }))
      $target = $enc::HtmlEncode($(if ($b.ContainsKey('NewPath')) { $b.NewPath } else { '' }))
      [void]$html.AppendLine("<tr><td>$path</td><td>$action</td><td>$target</td></tr>")
    }
    [void]$html.AppendLine('</table>')
  }

  # Identische (eingeklappt)
  if ($identical.Count -gt 0) {
    [void]$html.AppendLine('<h2>Identische Zuordnungen</h2>')
    [void]$html.AppendLine("<details><summary>Alle anzeigen ($($identical.Count))</summary>")
    [void]$html.AppendLine('<table><tr><th>Datei</th><th>Aktion</th><th>Ziel</th></tr>')
    foreach ($i in $identical) {
      $path   = $enc::HtmlEncode($i.Key)
      $action = $enc::HtmlEncode($(if ($i.ItemA.ContainsKey('Action'))  { $i.ItemA.Action }  else { '' }))
      $target = $enc::HtmlEncode($(if ($i.ItemA.ContainsKey('NewPath')) { $i.ItemA.NewPath } else { '' }))
      [void]$html.AppendLine("<tr><td>$path</td><td>$action</td><td>$target</td></tr>")
    }
    [void]$html.AppendLine('</table></details>')
  }

  [void]$html.AppendLine("<p style='color:#666;font-size:.8em'>Generiert: $([datetime]::Now.ToString('yyyy-MM-dd HH:mm:ss'))</p>")
  [void]$html.AppendLine('</body></html>')

  $result = $html.ToString()

  if ($OutputPath) {
    $dir = [System.IO.Path]::GetDirectoryName($OutputPath)
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { [void](New-Item -Path $dir -ItemType Directory -Force) }
    [System.IO.File]::WriteAllText($OutputPath, $result, [System.Text.Encoding]::UTF8)
    return @{ Success = $true; OutputPath = $OutputPath; Size = $result.Length }
  }

  return $result
}
