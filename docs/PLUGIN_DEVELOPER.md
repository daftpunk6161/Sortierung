# Plugin-Entwickler-Dokumentation

## Übersicht
ROM Cleanup unterstützt drei Plugin-Arten:
- `plugins/consoles/*.json` für Konsolen-/Regex-Mappings
- `plugins/operations/*.ps1` für Operation-Hooks (`Invoke-RomCleanupOperationPlugin`)
- `plugins/reports/*.ps1` für Report-Generatoren (`Invoke-ReportPlugin`)

## 1) Console-Plugins
Datei: `plugins/consoles/<name>.json`

Beispiel:
```json
{
  "folderMap": {
    "PSX": "Sony - PlayStation"
  },
  "extMap": {
    ".zso": "PSP"
  },
  "regexMap": {
    "(?i)playstation\\s*2": "PS2"
  }
}
```

## 2) Operation-Plugins
Datei: `plugins/operations/<name>.ps1`

Pflichtfunktion:
```powershell
function Invoke-RomCleanupOperationPlugin {
  param(
    [string]$Phase,
    [hashtable]$Context
  )

  if ($Phase -eq 'post-run') {
    return [pscustomobject]@{ status = 'ok'; note = 'post-run handled' }
  }

  return $null
}
```

## 3) Report-Plugins
Datei: `plugins/reports/<name>.ps1`

Pflichtfunktion:
```powershell
function Invoke-ReportPlugin {
  param(
    $Report,
    [string]$ReportDir,
    [string]$Timestamp,
    [string]$Mode,
    [hashtable]$Summary
  )

  $outPath = Join-Path $ReportDir ("custom-report-{0}.txt" -f $Timestamp)
  "Custom report" | Out-File -LiteralPath $outPath -Encoding utf8 -Force
  return [pscustomobject]@{ path = $outPath }
}
```

## Fehlerbehandlung
- Plugins sollen Exceptions selbst abfangen und strukturierte Ergebnisse zurückgeben.
- Das Host-System protokolliert Fehler je Plugin, fährt aber weiter.

## Sicherheit
- Keine destruktiven Dateioperationen außerhalb der vom Host übergebenen Pfade.
- Keine Secrets im Klartext loggen.
- Externe Netzwerkaufrufe optional und timeout-geschützt.
