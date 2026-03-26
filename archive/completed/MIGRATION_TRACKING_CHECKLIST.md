# Tracking Checklist (RomCleanup)

## Release-Blocker
- [x] Category-aware Winner Selection in Deduplication umgesetzt
- [x] Unknown-Category wird nicht mehr implizit als Game behandelt
- [x] Rollback meldet nur reale Restores (außer DryRun)
- [x] LoserCount bleibt geplante Dedupe-Menge (kein Move-Overwrite)

## GUI/UX (WPF/XAML)
- [x] IA/Navigation klar
- [x] Spacing/Overlaps/Resize sauber
- [x] Wizard/Flow: Roots -> Optionen -> Preview -> Confirm -> Run -> Report/Undo
- [x] Phase/Progress/Cancel sauber
- [x] Retro-modern Theme lesbar (ResourceDictionary)

## Core/Engine
- [x] CandidateFactory aus Scan-Logik extrahiert
- [x] BIOS-GameKey-Isolation via Prefix eingeführt
- [x] Dedupe-Determinismus beibehalten
- [x] RomCandidate auf FileCategory-Enum migrieren (breaking)
- [x] RunResult immutable + Builder einführen

## IO/Safety
- [x] Rollback-Actions für CONSOLE_SORT/CONVERT erweitert
- [x] Reparse-Checks im Rollback intakt
- [x] Audit-Trail für alle Action-Phasen vollständig vereinheitlichen

## Performance
- [x] Enumeration/Hashing/Regex Hotspots geprüft
- [x] UI bleibt responsiv

## Tests (keine Alibi-Tests)
- [x] Core-Invariant-Fehler in Tests kompilierbar gemacht
- [x] Vollständigen Testlauf über gesamte Solution durchführen
- [x] Regressionen für RunProjection in CLI/API/WPF ergänzen

## Backlog
- [x] RunProjection + RunProjectionFactory eingeführt
- [x] CLI auf RunProjection umgestellt
- [x] API auf RunProjection umgestellt
- [x] ReportGenerator vollständig auf RunProjection umstellen
- [x] Phase-Handler vollständig extrahieren (Scan/Enrichment/Dedupe/Junk/Move/ConvertOnly/WinnerConvert)