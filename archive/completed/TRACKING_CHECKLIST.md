# Tracking Checklist (RomCleanup)

## Release-Blocker
- [x] HealthScore-Formel als Single Source of Truth zentralisiert
- [x] API/CLI/GUI auf gemeinsame RunProjection-KPIs ausgerichtet
- [x] Tote KPI-Logik (`RunResultSummary`) entfernt

## GUI/UX (WPF/XAML)
- [x] IsValidTransition-Duplikat durch gemeinsame RunStateMachine ersetzt
- [x] Dashboard-KPIs in `ApplyRunResult` auf RunProjection umgestellt
- [x] Wizard/Flow: Roots -> Optionen -> Preview -> Confirm -> Run -> Report/Undo

## Core/Engine
- [x] `HealthScorer` in Core eingeführt
- [x] `RunProjectionFactory` nutzt zentrale Scoring-Logik
- [x] Determinismus über neue RunProjection-Tests abgesichert

## IO/Safety
- [x] Report-Summary weiter aus zentraler Projektion gespeist
- [x] Vollständige OpenAPI-Dokumentation aller neuen API-KPI-Felder

## Performance
- [x] Keine zusätzlichen teuren Scans in GUI-KPI-Berechnung (Projection-Reuse)
- [x] Hotspot-Prüfung Scan/Hashing — FileHashService Cache-Key ohne Timestamp-Stat (eliminiert 100k+ Syscalls/Run)

## Tests (keine Alibi-Tests)
- [x] `RunProjectionFactoryTests` hinzugefügt
- [x] `RunReportWriterTests` hinzugefügt
- [x] API-Integrationstest für neue Ergebnisfelder hinzugefügt
- [x] Kompletter `dotnet test src/RomCleanup.sln` Lauf lokal ausführen
- [x] ADR-0007 Punkt 5: XXE-Payload-Test für DAT-Parser ergänzt
- [x] ADR-0007 Punkt 5: expliziter 3-Way Entry-Point Parity-Guard ergänzt
- [x] ADR-0007 Punkt 5: CompletenessScore-Test für unvollständige CUE-Sets ergänzt
- [x] ADR-0007 Punkt 5: NTFS-Reparse-Point-Blocking (Windows/Junction) testseitig abgesichert
- [x] ADR-0007 Punkt 5: DAT-Sidecar-Hash-Mismatch-Ablehnung testseitig ergänzt

## Backlog
- [x] Report/CLI/API/OpenAPI-Dokumentationsparität vollständig automatisiert prüfen
- [x] Weitere Channel-Parity-Tests (CLI vs API vs Report) für alle KPI-Felder