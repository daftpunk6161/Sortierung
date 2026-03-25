# Bekannte Detektionslimits

## .bin ohne Header-Magic (Systemic UNKNOWN)

`.bin`-Dateien ohne erkennbare Header-Magic-Bytes koennen nicht allein durch `DiscHeaderDetector` oder `CartridgeHeaderDetector` identifiziert werden. Die Erkennung stuetzt sich in diesen Faellen auf Fallback-Strategien:

1. **FolderName** – Ordnerstruktur (`nes/`, `snes/`, `arcade/` etc.)
2. **FilenameSerial** / **FilenameKeyword** – Seriennummern oder Muster im Dateinamen

Wenn keiner dieser Fallbacks greift (z.B. `.bin` in einem unsortierten Verzeichnis ohne Namensmuster), bleibt das Ergebnis `UNKNOWN`.

### Betroffene Systeme

Besonders betroffen sind Systeme mit generischem `.bin`-Format ohne standardisiertes Header-Layout:

- ATARI2600, ATARI5200, ATARI7800
- COLECO, CHANNELF, INTELLIVISION, VECTREX, ODYSSEY2
- ARCADE (MAME .bin-Dumps)
- CPC, MSX, VIC20, C64 (Teilweise)

### Testabsicherung

- `Phase3DetectionRecallTests.DiscHeaderDetector_BinWithoutMagic_ReturnsNull` – verifiziert, dass DiscHeaderDetector bei leerem `.bin`-Stub korrekt `null` zurueckgibt
- Ground-Truth-Eintraege mit `.bin`-Extension und `directory`-Kontext werden ueber FolderName-Detection korrekt erkannt
- Benchmark-Gate: Missed-Entries ≤ 12 (TASK-045) schliesst diese erwarteten UNKNOWN-Faelle ein

### Empfehlung

Dieses Limit ist architektonisch begruendet und kein Bug. Verbesserungen sind moeglich durch:
- DAT-basierte Verifizierung (erkennt `.bin`-Dateien anhand von Hash-Matching)
- Erweiterte CartridgeHeader-Erkennung fuer spezifische Systeme
