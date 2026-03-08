# User-Handbuch

## 1. Schnellstart
1. `simple_sort.ps1` starten.
2. Im Tab **Sortieren** ROM-Ordner hinzufügen (Drag & Drop oder Button).
3. Optional `_TRASH`-Pfad setzen.
4. Mit **Sortierung starten** ausführen.

## 2. Screenshots
![Hauptfenster](screenshots/main-window.svg)
![Konfiguration](screenshots/configuration.svg)
![Log & Dashboard](screenshots/log-dashboard.svg)

## 3. Wichtige Modi
- **DryRun**: Nur Vorschau/Analyse, keine Datei-Verschiebung.
- **Move**: Führt Verschiebungen aus (nur mit expliziter Bestätigung).

## 4. Konfiguration
- Externe Tools: `chdman`, `DolphinTool`, `7z`, `psxtract`, `ciso`
- DAT-Verifikation + Hash-Typ (`sha1`, `md5`, `crc32`)
- Profile speichern/laden/importieren/exportieren

## 5. Sicherheit
- API nur mit `X-Api-Key`
- Rate-Limiting aktiv
- CORS-Header aktiv
- Tool-Binary-Hashes über `data/tool-hashes.json`

## 6. Rollback
- Über **Rückgängig** (Rollback-Wizard)
- Audit-CSV auswählen, Vorschau prüfen, `ROLLBACK` bestätigen

## 7. Troubleshooting
- Fehlende Tools: Auto-Entdecken im Konfig-Tab nutzen
- DAT-Fehler: `DAT-Root` und Hash-Typ prüfen
- API-Fehler 401: `X-Api-Key` Header setzen

## 8. Format-Bewertung (Winner-Selection)

Bei der Deduplizierung wird das beste Format eines Spiels bevorzugt. Höhere Scores = besser:

| Format | Score | Bemerkung |
|--------|-------|-----------|
| CHD | 850 | Komprimiert, verifizierbar, disc-basiert |
| ISO | 700 | Unkomprimiert, universell |
| ZIP | 500 | Standard-Archiv |
| 7Z | 480 | Hohe Kompression |
| RAR | 400 | Proprietär |
| RVZ | 700 | GameCube/Wii-optimiert (DolphinTool) |
| CSO | 600 | PSP-komprimiert |

**Winner-Selection Reihenfolge:**
1. Regions-Score (bevorzugte Region = höchster Score)
2. DAT-Header-Score (verifizierte Dateien bevorzugt)
3. Versions-Score (Verified `[!]` = +500; höhere Revision bevorzugt)
4. Format-Score (siehe Tabelle)
5. Größen-Tiebreak (Disc → größer; Cartridge → kleiner)

## 9. Erkennungs-Pipeline

Die Konsolen-Erkennung durchläuft folgende Stufen in dieser Reihenfolge:

| Stufe | Methode | Konfidenz |
|-------|---------|-----------|
| 1 | DAT Hash Match (SHA1/MD5/CRC32) | 100% |
| 1b | ZIP-Inhaltsanalyse (PS1/PS2-typische Dateien) | 75% |
| 2 | Archiv-Inhalt (eindeutige Extensions im ZIP) | 70% |
| 3 | Archiv-Disc-Header (ISO in ZIP) | 95% |
| 4 | DolphinTool Disc-ID (GC/Wii) | 90% |
| 4b | Disc-ID im Dateinamen `[RZDE01]` | 85% |
| 5a | Ordner-Name-Erkennung | 50% |
| 5b | Disc-Header direkt (ISO/BIN) | 95% |
| 5c | Eindeutige Extension (.gba, .nes, .nds) | 60% |
| 5d | Dateiname-Regex | 30% |
| 5e | Mehrdeutige Extension (.cso = PSP) | 40% |
| 6 | UNKNOWN + Reason-Code | 0% |

Weitere Details: siehe `docs/UNKNOWN_FAQ.md`
