# Audit-Log-Format

> ROM Cleanup erzeugt bei jeder Operation ein Audit-CSV.
> Implementierung: `src/RomCleanup.Infrastructure/Audit/AuditCsvStore.cs`

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
| `SizeBytes` | Integer / leer | Dateigröße in Bytes |

## Action-Werte

| Action | Beschreibung |
|--------|-------------|
| `MOVE` | Datei wurde verschoben |
| `TRASH` | Datei wurde in den Papierkorb verschoben |
| `KEEP` | Datei wurde behalten (Winner) |
| `SKIP` | Datei wurde übersprungen |
| `ERROR` | Fehler bei der Verarbeitung |
| `JUNK_REMOVE` | Junk-Datei wurde entfernt (Move in Trash) |
| `CONVERT` | Datei wurde konvertiert (CHD/RVZ/ZIP) |
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

## Maschinelle Auswertung (C#)

```csharp
// Audit-CSV lesen
var lines = File.ReadAllLines("audit-dedupe-20260306-120000.csv");
var moves = lines.Skip(1) // Header überspringen
    .Select(l => l.Split(','))
    .Where(cols => cols.Length > 1 && cols[1] == "MOVE");
```

## Sicherheit

- CSV-Werte werden gegen CSV-Injection-Angriffe bereinigt (keine Formeln mit `=`, `+`, `-`, `@` am Anfang)
- Pfade werden NFC-normalisiert für konsistente Vergleiche

## Rollback-Trail

Bei einem Rollback (`AuditCsvStore.Rollback()`) wird zusätzlich eine `.rollback-trail.csv` geschrieben.
Diese enthält alle rückgängig gemachten Operationen mit TOCTOU-Schutz (try/catch + retry).

## Partial Sidecar

Bei Abbruch (Cancel) wird ein Sidecar mit `"Status": "partial"` geschrieben, um unvollständige Läufe zu kennzeichnen.
- Implementiert in `AuditCsvStore.SanitizeCsvField()`
- SHA256-Signierung via `AuditSigningService`
