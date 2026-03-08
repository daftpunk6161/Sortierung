# Audit-Log-Format

> ROM Cleanup erzeugt bei jeder Operation ein Audit-CSV in `audit-logs/`.

## Dateiname

```
audit-{Konsolenname}-{YYYYMMDD-HHMMSS}.csv
```

Beispiel: `audit-dedupe-20260306-120000.csv`

## Spalten

| Spalte | Typ | Beschreibung |
|--------|-----|-------------|
| `Time` | ISO 8601 Timestamp | Zeitpunkt der Aktion (UTC) |
| `Action` | String | Art der Aktion (siehe unten) |
| `Source` | Pfad | Ursprungspfad der Datei |
| `Dest` | Pfad | Zielpfad (bei Move) oder leer |
| `SizeBytes` | Integer / leer | Dateigroesse in Bytes |

## Action-Werte

| Action | Beschreibung |
|--------|-------------|
| `MOVE` | Datei wurde verschoben |
| `TRASH` | Datei wurde in den Papierkorb verschoben |
| `KEEP` | Datei wurde behalten (Winner) |
| `SKIP` | Datei wurde uebersprungen |
| `ERROR` | Fehler bei der Verarbeitung |
| `PERF-METRIC` | Performance-Metrik (Source = `metric:Name`, Dest = Wert) |

## Sidecar-Metadaten

Neben jeder Audit-CSV wird eine `.meta.json` erzeugt mit:

```json
{
  "auditFile": "audit-dedupe-20260306-120000.csv",
  "csvSha256": "abc123...",
  "rowCount": 150,
  "createdUtc": "2026-03-06T12:00:00Z"
}
```

## Maschinelle Auswertung

```powershell
# CSV laden
$audit = Import-Csv -Path 'audit-logs/audit-dedupe-20260306-120000.csv'

# Nur Moves filtern
$moves = $audit | Where-Object { $_.Action -eq 'MOVE' }

# Gesamtgroesse berechnen
$totalBytes = ($moves | Measure-Object -Property SizeBytes -Sum).Sum
```

## Sicherheit

- CSV-Werte werden gegen Excel-Injection-Angriffe bereinigt (keine Formeln mit `=`, `+`, `-`, `@` am Anfang)
- Pfade werden mit `ConvertTo-SafeCsvValue` escaped
