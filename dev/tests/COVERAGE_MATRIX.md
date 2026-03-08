# ROM Cleanup Test Coverage Matrix

## Übersicht

| Funktion/Feature | Risiken | Testcases | Mocking-Strategie | Mutationen |
|------------------|---------|-----------|-------------------|------------|
| `ConvertTo-GameKey` | Leerer Key → Datenverlust | Empty input, tag-only names, Multi-Disc | - | M3: Fallback entfernen |
| `Select-Winner` | Falscher Winner → Datenverlust | Priority order, Ties, Edge cases | - | M1, M2: Score Logik |
| `Get-RegionTag` | Sprache vs Region verwechselt | EU/US/JP, Language tags, Multi-region | - | M5: Priorität vertauschen |
| `Get-VersionScore` | Falsche Version bevorzugt | [!], Rev, Version, Language | - | M2: Ascending statt Descending |
| `Get-RegionScore` | Falsche Region bevorzugt | PreferOrder, WORLD, UNKNOWN | - | M1: IndexOf invertieren |
| `Move-ItemSafely` | Overwrite, Path Traversal | Collision, Reparse, Outside root | - | M4: Suffix entfernen |
| `Expand-ArchiveToTemp` | Zip Slip, Tool Failure | Traversal paths, ExitCode != 0 | Mock Start-Process | M11: tempDir cleanup |
| `Invoke-ConvertItem` | Partial output, Missing tracks | CUE/GDI missing, chdman fail | Mock Start-Process | M8, M9: ExitCode ignorieren |
| `Get-DatIndex` | Invalid XML crash, hashKey uninit | Non-XML, Corrupted, Empty | - | M10: catch entfernen |
| `ConvertTo-HtmlReport` | XSS, th/td mismatch | Script injection, Column count | - | M7: HtmlEncode entfernen |
| `ConvertTo-SafeCsvValue` | CSV Injection | Formulas, Tab, Whitespace | - | M12: Prefix entfernen |
| `Get-HashesFrom7z` | Zip Slip, Tool Failure | Traversal entries, 7z fail | Mock Start-Process | M11 |
| `Test-PathWithinRoot` | Path Traversal | ..\, UNC, Edge cases | - | M4 |
| `Get-FilesSafe` | ReparsePoint escape | Symlinks, Junctions | - | - |

## RED Tests (erwarten Failure vor Fixes)

Diese Tests dokumentieren bekannte Bugs/Risks die gefixt werden sollten:

1. **RED-CSV-Tab**: `\t` am Anfang wird nicht mit Apostroph prefixiert (BEHOBEN in Tests)
2. **RED-DAT-hashKey**: Variable nicht initialisiert in ConsoleMap-Branch (BEHOBEN)
3. **RED-HTML-Columns**: th/td Mismatch bei bestimmten Spalten (BEHOBEN)

## Mutationen (Pflicht zu killen)

| ID | Mutation | Erwarteter kaputter Test | Warum |
|----|----------|-------------------------|-------|
| M1 | `RegionScore`: `1000 + idx` statt `1000 - idx` | `RegionScore.PreferOrder` | Umgekehrte Priorität |
| M2 | `Select-Winner`: VersionScore ascending | `Select-Winner.Priority` | Niedrigere Version gewinnt |
| M3 | `ConvertTo-GameKey`: Fallback bei leerem Key entfernen | `GameKey.EmptyProtection` | Leerer Key → Kollision |
| M4 | `Move-ItemSafely`: `__DUP` Suffix entfernen | `MoveSafely.NoOverwrite` | Überschreibt existierende |
| M5 | `Get-RegionTag`: US vor EU in Ordered Array | `RegionTag.EUvsUS` | Falsche Region erkannt |
| M6 | `RX_LANG` Check in Get-RegionTag entfernen | `RegionTag.LanguageVsRegion` | Sprache als Region |
| M7 | `ConvertTo-HtmlSafe` durch Identity ersetzen | `Security.HtmlXSS` | XSS möglich |
| M8 | `chdman ExitCode != 0` trotzdem OK | `Conversion.ToolFailure` | Defekte Ausgabe behalten |
| M9 | Missing CUE tracks: SKIP → convert attempt | `CUE.MissingTracks` | Crash bei Konvertierung |
| M10 | DAT XML: catch entfernen bei [xml] cast | `DAT.InvalidXml` | Crash bei non-XML |
| M11 | `_TRASH` nicht in ExcludePaths | `Dedupe.ExcludeSelf` | Self-scan Recursion |
| M12 | `ConvertTo-SafeCsvValue`: Prefix entfernen | `Security.CSVInjection` | Formula injection |

## Test-Ausführung

```powershell
# Alle Tests
Invoke-Pester -Path .\dev\tests\*.Tests.ps1 -Output Detailed

# Nur Mutation-Testing
.\dev\tools\Invoke-MutationLight.ps1
```
