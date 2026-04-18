---
goal: Priorisierte Sanierung aller offenen Findings aus dem 8-Runden Deep-Dive-Audit (120 Findings, davon ~30 offen)
version: 1.0
date_created: 2026-04-12
last_updated: 2026-04-12
owner: Romulus Team
status: 'Planned'
tags: [refactor, security, stability, architecture, hygiene, audit-remediation]
---

# Audit-Sanierungsplan – Romulus Deep-Dive Runden 1–8

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Dieser Plan konsolidiert alle **offenen Findings** aus dem 8-Runden-Audit (120 Findings gesamt, 4 Kritisch, 28 Hoch, 58 Mittel, 30 Niedrig) und dem Full-Repo-Audit vom 2026-04-11. Bereits durch den Technical-Debt-Tracker (`refactor-technical-debt-tracker-2026-04-09-1.md`, Phasen 4–21, alle ✅) und das Full-Repo-Audit adressierte Items sind **nicht erneut enthalten**.

**Quellen:**
- Deep-Dive Runden 1–8 (Findings R1-xx bis R8-xx)
- Full-Repo-Audit `archive/full-repo-audit-2026-04-11.md` (Findings A-xx bis F-xx)
- Hard-Audit-Tracker `archive/hard-audit-findings-tracker.md` (39 Findings, alle ✅)
- Technical-Debt-Tracker Phasen 4–21 (TD-008 bis TD-060, alle ✅)

**Konsolidierungsergebnis:** Nach Abzug aller bereits erledigten Items verbleiben **28 offene Findings**, gruppiert in 6 Phasen.

## 1. Requirements & Constraints

- **REQ-001**: `dotnet build src/Romulus.sln` und `dotnet test src/Romulus.Tests/Romulus.Tests.csproj` müssen nach jeder Phase grün sein
- **REQ-002**: Preview/Execute/Report-Parität darf nicht verletzt werden
- **REQ-003**: Determinismus (GameKey, Region, Score, Winner Selection) muss gewahrt bleiben
- **REQ-004**: Kein Datenverlust – Move-to-Trash/Audit-Verhalten unverändert
- **REQ-005**: Jede Phase unabhängig deploy- und testbar (inkrementelle Delivery)
- **SEC-001**: API-Security-Findings (Exception Handler, Static Files vor Auth) sind Release-Blocker
- **SEC-002**: Tool-Hash-Bypass (ChdmanToolConverter ohne ToolRequirement) ist Security-relevant
- **SEC-003**: Unsigned Audit-Trail (RunService AuditCsvStore) ist Integritäts-Risiko
- **CON-001**: Dependency-Richtung Entry Points → Infrastructure → Core → Contracts einhalten
- **CON-002**: Keine neuen NuGet-Dependencies ohne Begründung
- **CON-003**: Keine Breaking Changes in CLI/API-Verhalten
- **CON-004**: Bestehende Architektur respektieren – keine großflächigen Umbauten in kritischen Phasen
- **GUD-001**: Fixes minimal halten – nur betroffene Stellen ändern
- **GUD-002**: Jeder Fix braucht mindestens einen Test, der den Fehler reproduziert hätte
- **PAT-001**: DI-Container nutzen statt `new` in Services; Constructor Injection bevorzugen
- **PAT-002**: Tool-Aufrufe über ToolRequirement mit Hash-Verifikation

## 2. Implementation Steps

### Phase 1 – Release-Blocker: UI-Thread-Deadlock + API-Security

- GOAL-001: Die 2 kritischsten Findings eliminieren: UI-Thread-Deadlock durch `.Result` und API-Stack-Trace-Leakage durch fehlenden Exception Handler.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | **R6-01 – Async-Deadlock eliminieren**: In `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs` Zeile ~126: `.AsTask().Result` ersetzen durch `await`-Aufruf. Die aufrufende Methode `RefreshRunConfigurationCatalogs()` auf `async Task` umstellen. Alle Aufrufer dieser Methode (Property-Setter, Event-Handler) auf `async void` mit try/catch umstellen. Async-Version `RefreshRunConfigurationCatalogsCoreAsync()` existiert bereits und soll genutzt werden. | | |
| TASK-002 | **R6-01 – Weitere `.GetAwaiter().GetResult()` in WPF prüfen**: `grep -rn 'GetAwaiter\(\).GetResult\(\)\|\.Result\b\|\.AsTask\(\).Result' src/Romulus.UI.Wpf/` ausführen. Jede Stelle die auf dem UI-Thread läuft entweder auf async umstellen oder mit `// SYNC-JUSTIFIED: [Grund]` dokumentieren. Aus Full-Repo-Audit bekannt: 19 Stellen in FeatureCommandService. | | |
| TASK-003 | **R6-08 – API Global Exception Handler**: In `src/Romulus.Api/Program.cs` nach `var app = builder.Build();` einfügen: `app.UseExceptionHandler(...)` mit generischem JSON-Error-Response `{ "error": "INTERNAL_ERROR", "message": "An unexpected error occurred" }`. Stack Traces nur bei `app.Environment.IsDevelopment()` inkludieren. Alternativ: Minimal-API-Exception-Filter via `IExceptionHandler` registrieren. | | |
| TASK-004 | **R6-08 – Tests**: Integration-Test der POST /runs mit provoziertem internen Fehler → Response ist JSON mit `error: "INTERNAL_ERROR"`, kein Stack Trace im Body. Test dass Development-Mode Stack Trace zeigt. | | |
| TASK-005 | **Verification Phase 1**: Build + Tests grün. `grep 'AsTask().Result' src/Romulus.UI.Wpf/` → Zero Treffer in neuen async Pfaden. API-Smoke-Test: ungültiger Request → keine Stack Traces in Response. | | |

### Phase 2 – Security-Hardening: Tool-Hash-Bypass + Unsigned Audit + API-Auth

- GOAL-002: Alle Security-relevanten Findings schließen: ChdmanToolConverter ohne Hash-Verifikation, AuditCsvStore ohne Signing Key, Static Files vor Auth.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-006 | **R6-05 – ChdmanToolConverter ToolRequirement**: In `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs` Zeile ~59: 3-Argument-Aufruf `_tools.InvokeProcess(toolPath, args, "chdman")` ersetzen durch 5-Argument-Variante mit `ToolRequirement` (analog `ChdmanInvoker`). `ToolRequirement` aus `tool-hashes.json` laden. Falls ChdmanToolConverter keinen Zugriff auf ToolRequirement hat: per Constructor Injection einführen. | | |
| TASK-007 | **R6-06 – Fehlende Tool-Hashes ergänzen**: In `data/tool-hashes.json` Hash-Einträge für `unecm.exe`, `flips.exe`, `xdelta3.exe` hinzufügen. SHA256-Hashes der aktuell im Projekt verwendeten Binaries berechnen und eintragen. Falls Binaries nicht im Repo vorhanden: Hashes als Platzhalter mit Kommentar `"hash": "PENDING-VERIFY"` und Dokumentation in welchem Release die Binaries verifiziert werden. | | |
| TASK-008 | **R6-03 – AuditCsvStore Signing Key**: In `src/Romulus.UI.Wpf/Services/RunService.cs` Zeile ~39: `new AuditCsvStore()` ersetzen durch DI-injizierte Instanz. `SharedServiceRegistration.cs` registriert bereits `AuditCsvStore` mit `AuditSecurityPaths.GetDefaultSigningKeyPath()`. RunService soll `IAuditCsvStore` (oder `AuditCsvStore`) per Constructor Injection empfangen statt `new`. | | |
| TASK-009 | **R7-08 – Static Files nach Auth**: In `src/Romulus.Api/Program.cs` Zeile ~124: `app.UseStaticFiles()` NACH `app.UseAuthentication()` und `app.UseAuthorization()` verschieben. Oder: Static Files auf einen expliziten unauthentifizierten Pfad (`/public/`) einschränken, falls Dashboard absichtlich öffentlich sein soll. Entscheidung dokumentieren. | | |
| TASK-010 | **Tests Phase 2**: Unit-Test für ChdmanToolConverter → ToolRequirement wird an InvokeProcess übergeben. Test dass RunService AuditCsvStore mit Signing Key erhält (DI-Resolution-Test). API-Test: GET /dashboard.html ohne API-Key → je nach Entscheidung 401 oder 200. | | |
| TASK-011 | **Verification Phase 2**: `grep -rn 'new AuditCsvStore()' src/` → Zero Treffer in Produktionscode. `grep -rn 'InvokeProcess.*"chdman"' src/` → Alle Aufrufe nutzen ToolRequirement. Build + Tests grün. | | |

### Phase 3 – DI-Hygiene + Daten-Korrektheit

- GOAL-003: DI-Bypass-Muster eliminieren und Daten-Inkonsistenzen in rules.json bereinigen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-012 | **R6-04 – DI-Bypass in MainViewModel.Productization.cs**: Zeile ~113: `runProfileService ?? new RunProfileService(new JsonRunProfileStore(), dataDir)` ersetzen. `IRunProfileService` per Constructor Injection an MainViewModel übergeben. Fallback-`new` entfernen. Falls MainViewModel bereits über DI erzeugt wird: Sicherstellen dass `IRunProfileService` in DI registriert ist (prüfe `SharedServiceRegistration.cs`). Falls MainViewModel manuell erzeugt wird: DI-Container nutzen oder Service als Parameter durchreichen. | | |
| TASK-013 | **R6-04 – Weitere DI-Bypass-Stellen prüfen**: `grep -rn 'new RunProfileService\|new JsonRunProfileStore\|new AuditCsvStore\|new SettingsLoader\b' src/Romulus.UI.Wpf/` → Jeder Treffer muss durch DI-Injection ersetzt oder als `// DI-BYPASS-JUSTIFIED: [Grund]` dokumentiert werden. | | |
| TASK-014 | **R6-10 – rules.json SCAN/EU-Widerspruch**: In `data/rules.json`: `regionTokenMap` ordnet `scandinavia → EU` zu (Zeile ~115+). Gleichzeitig existiert `SCAN` als eigenständige Region in `RegionOrdered` (Zeile ~38). **Entscheidung treffen und dokumentieren**: (a) SCAN bleibt eigenständig → `regionTokenMap`-Mapping `scandinavia → SCAN` ändern, ODER (b) SCAN ist Subset von EU → aus `RegionOrdered` entfernen und Scores anpassen. Entscheidung per Kommentar in rules.json dokumentieren. | | |
| TASK-015 | **R6-10 – Regression-Test**: Test der Region-Erkennung für nordische Releases (z.B. `"(Sweden)"`, `"(Scandinavia)"`) → erwartete Region-Zuordnung nach gewählter Auflösung. Deterministisch-Invariante: Gleicher Input → gleiche Region vor und nach Änderung (oder bewusster Breaking-Change-Test). | | |
| TASK-016 | **Verification Phase 3**: `grep -rn '?? new.*Service\|?? new.*Store' src/Romulus.UI.Wpf/` → Zero unjustifizierte Treffer. Build + Tests grün. Region-Detection-Tests alle grün. | | |

### Phase 4 – Robustheit + Resourcen-Sicherheit

- GOAL-004: Ressourcen-Leaks, fehlende Cache-Invalidierung und fehlerhafte Zustandsverwaltung beseitigen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-017 | **R6-07 – ArchiveHashService Cache-Invalidierung**: In `src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs`: Neben dem manuellen `ClearCache()` eine automatische Invalidierung einführen. Beim Cache-Lookup `lastWriteTimeUtc` und `fileSize` des gecachten Pfads prüfen. Wenn Datei geändert: Cache-Eintrag invalidieren und Hash neu berechnen. Minimale Implementierung: `ConcurrentDictionary<string, (string Hash, long Size, DateTime LastWrite)>`. | | |
| TASK-018 | **R6-09 – TrayService try/finally**: In `src/Romulus.UI.Wpf/Services/TrayService.cs` Zeile ~37: `_isCreating = true` in try/finally wrappen: `try { _isCreating = true; /* tray icon creation */ } finally { _isCreating = false; }`. Sicherstellen dass bei Exception im Icon-Erstellungscode der Flag nicht stecken bleibt. | | |
| TASK-019 | **R6-11 bis R6-16 (Medium/Low aus Runde 6)**: Sammlung der verbleibenden Medium-Findings aus Runde 6: (a) `DatCatalogViewModel` Thread-Safety bei `_cts` Zugriff → CancellationTokenSource-Erzeugung unter Lock oder Interlocked.Exchange, (b) `SetupViewModel` fehlende Validierung bei Pfadeingaben → Validierung analog MainViewModel nachziehen, (c) `FeatureCommandService` Error-Handling nicht einheitlich → einheitliches Pattern für Try-Catch-Logging etablieren. Pro Item: Datei identifizieren, Fix einzeilig, Test ergänzen. | | |
| TASK-020 | **Tests Phase 4**: ArchiveHashService-Test: Hash cachen → Datei ändern → erneut hashen → neuer Hash. TrayService-Test: Exception während Icon-Erstellung → `_isCreating == false` danach. DatCatalogViewModel: Concurrent Cancel-Aufrufe → kein ObjectDisposedException. | | |
| TASK-021 | **Verification Phase 4**: Build + Tests grün. Kein ArchiveHashService-Cache-Miss bei geänderten Dateien. TrayService: manueller WPF-Smoke-Test → Tray-Icon erscheint und verschwindet korrekt. | | |

### Phase 5 – Strukturelle Schulden (Post-Release, optional)

- GOAL-005: God-Class-Muster entschärfen und Orchestration-Monolith aufbrechen. Diese Phase ist **post-release** und wird nur bei klarem Bedarf aktiviert.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-022 | **C-01 – MainViewModel Aufspaltung (Analyse)**: Identifiziere die 3-4 fachlichen Domänen in MainViewModel (Configuration, RunState, Dashboard/KPI, AuditLog). Erstelle eine Mapping-Tabelle: Property/Method → Ziel-ViewModel. Schätze Aufwand und Risiko. **Nur Analyse, kein Code.** | ✅ | 2026-04-13 |
| TASK-023 | **C-01 – MainViewModel Phase 1 (ConfigViewModel)**: Extrahiere Configuration-Properties (Pfade, Regions, Extensions, Expert-Mode) in ein `ConfigurationViewModel`. MainViewModel delegiert an ConfigViewModel. Bestehende Bindings über Property-Forwarding oder DataContext-Wechsel in XAML erhalten. | ✅ | 2026-04-13 |
| TASK-024 | **C-01 – MainViewModel Phase 2 (DashboardViewModel)**: Extrahiere KPI-/Dashboard-Projektionsproperties in `DashboardViewModel`. RunResult-Subscription und Projection-Updates wandern mit. | ✅ | 2026-04-13 |
| TASK-025 | **C-02 – Orchestration Sub-Namespaces**: Erstelle Ordner `Orchestration/Phases/`, `Orchestration/Profiles/`, `Orchestration/Projections/`. Verschiebe Pipeline-Phase-Dateien, Profil-Dateien und Projection-Dateien. Namespaces anpassen. Keine funktionale Änderung. | DEFERRED | 2026-04-13 |
| TASK-026 | **C-03 – FeatureCommandService Domain-Handler**: Extrahiere `FeatureCommandService.Conversion.cs` in einen eigenständigen `ConversionCommandHandler`. Extrahiere `FeatureCommandService.Dat.cs` in `DatCommandHandler`. FeatureCommandService delegiert an Handler. Schrittweise, ein Handler pro Iteration. | ✅ | 2026-04-13 |
| TASK-027 | **Tests Phase 5**: Bestehende Tests müssen nach jedem Refactor-Schritt grün bleiben. Keine neuen Tests nötig wenn keine funktionale Änderung. Bei ViewModel-Split: bestehende GuiViewModelTests auf neues ViewModel umleiten. | ✅ | 2026-04-13 |

### Phase 6 – Test-Qualität + Restliche Hygiene

- GOAL-006: Verbleibende Test-Qualitäts- und Hygiene-Findings abarbeiten.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-028 | **F-05 (Full-Repo-Audit) – No-Crash-Only Tests verbessern**: In `src/Romulus.Tests/WpfCoverageBoostTests.cs` und weiteren Coverage-Boost-Dateien: Alle `Assert.True(result.Count > 0)` durch fachliche Assertions ersetzen (Struktur, Werte, Ordering). Priorität: Tests die Core-Logik berühren (GetDuplicateHeatmap, GetHealthScoreBreakdown, GetRegionDistribution). Mindestens 10 Tests upgraden. | ✅ | 2026-04-13 |
| TASK-029 | **R7-02 bis R7-06 (Medium aus Runde 7)**: Verbleibende Medium-Findings: (a) `RunManager` async void Event-Handler ohne try/catch → try/catch ergänzen, (b) `WatchFolderService` FileSystemWatcher.InternalBufferSize nicht konfiguriert → auf 64KB setzen und Settings-basiert konfigurierbar machen, (c) `ApiRunConfigurationMapper` Case-Sensitivity-Lücke (bereits in Repo-Memory dokumentiert) → berücksichtigen ob gefixt. | ✅ | 2026-04-13 |
| TASK-030 | **R7-07 bis R7-14 (Low aus Runde 7)**: (a) API OpenAPI-Docs: Fehlende Response-Codes dokumentieren (400, 401, 404, 409, 500), (b) CLI --version Flag hinzufügen wenn fehlend, (c) Stale TODO-Kommentare entfernen oder in Tracker überführen. Aggregiert abarbeiten. | ✅ | 2026-04-13 |
| TASK-031 | **Verification Phase 6**: Build + Tests grün. `grep -rn 'Assert.True(.*Count > 0)' src/Romulus.Tests/WpfCoverageBoostTests.cs` → Zero generische Count-Assertions in überarbeiteten Tests. | ✅ | 2026-04-13 |

## 3. Alternatives

- **ALT-001**: R6-01 könnte statt async-Umstellung per `Task.Run(() => ...).GetAwaiter().GetResult()` auf Hintergrund-Thread gelöst werden — abgelehnt, da dies gegen WPF-Best-Practices verstößt und das eigentliche Problem (blockierender UI-Thread) nur verschiebt.
- **ALT-002**: R6-08 (API Exception Handler) könnte per Middleware-Filter statt `UseExceptionHandler` gelöst werden — beides akzeptabel, `UseExceptionHandler` ist das ASP.NET-Standard-Pattern und testbar.
- **ALT-003**: R6-03 (AuditCsvStore) könnte per Factory-Pattern statt Direct-DI gelöst werden — abgelehnt, da DI-Injection einfacher und konsistent mit dem Rest des Projekts ist.
- **ALT-004**: Phase 5 (Structural Debt) könnte vor Phase 4 gezogen werden — abgelehnt, da strukturelle Schulden kein Release-Risiko sind und funktionale Fixes Vorrang haben.
- **ALT-005**: R6-07 (ArchiveHashService) könnte statt Metadaten-Prüfung per FileSystemWatcher gelöst werden — abgelehnt, da FileSystemWatcher selbst ein bekanntes Reliability-Problem im Projekt ist (Full-Repo-Audit F-06).
- **ALT-006**: R6-10 (SCAN/EU) könnte durch ein generelles Region-Hierarchy-System gelöst werden — abgeleht als Over-Engineering; einfache Mapping-Korrektur in rules.json reicht.

## 4. Dependencies

- **DEP-001**: Phase 1 (TASK-001/002) hat keine Vorbedingungen und kann sofort starten
- **DEP-002**: Phase 1 (TASK-003/004 API Exception Handler) hat keine Vorbedingungen
- **DEP-003**: Phase 2 (TASK-006 ChdmanToolConverter) setzt voraus, dass `tool-hashes.json` aktuell ist → TASK-007 zuerst
- **DEP-004**: Phase 2 (TASK-008 AuditCsvStore DI) setzt voraus, dass SharedServiceRegistration AuditCsvStore bereits registriert → bereits der Fall (verifiziert)
- **DEP-005**: Phase 3 (TASK-012 DI-Bypass) kann parallel zu Phase 2 laufen, hat aber keine harte Abhängigkeit
- **DEP-006**: Phase 3 (TASK-014 SCAN/EU) hat Abhängigkeit zu bestehenden Region-Tests → Regressions-Goldstandard aus TD-029 nutzen
- **DEP-007**: Phase 4 kann parallel zu Phase 3 laufen
- **DEP-008**: Phase 5 setzt Phasen 1–4 als abgeschlossen voraus (strukturelle Refactors nur auf stabilem Fundament)
- **DEP-009**: Phase 6 kann parallel zu Phase 5 laufen

```
Phase 1 ─────────────────┐
Phase 2 ────────────────┐ │
Phase 3 ──────────────┐ │ │
Phase 4 ────────────┐ │ │ │
                     ▼ ▼ ▼ ▼
                  Phase 5 (nach 1-4)
                  Phase 6 (parallel zu 5)
```

## 5. Files

### Phase 1
- **FILE-001**: `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs` – `.AsTask().Result` → async (R6-01)
- **FILE-002**: `src/Romulus.UI.Wpf/Services/FeatureCommandService*.cs` – 19x `.GetAwaiter().GetResult()` Audit (R6-01)
- **FILE-003**: `src/Romulus.Api/Program.cs` – `UseExceptionHandler` einfügen (R6-08)

### Phase 2
- **FILE-004**: `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs` – ToolRequirement einführen (R6-05)
- **FILE-005**: `data/tool-hashes.json` – fehlende Hashes ergänzen (R6-06)
- **FILE-006**: `src/Romulus.UI.Wpf/Services/RunService.cs` – AuditCsvStore DI statt `new` (R6-03)
- **FILE-007**: `src/Romulus.Api/Program.cs` – UseStaticFiles Reihenfolge (R7-08)

### Phase 3
- **FILE-008**: `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs` – DI-Bypass entfernen (R6-04)
- **FILE-009**: `data/rules.json` – SCAN/EU Mapping korrigieren (R6-10)
- **FILE-010**: `src/Romulus.Infrastructure/Configuration/SharedServiceRegistration.cs` – RunProfileService DI prüfen (R6-04)

### Phase 4
- **FILE-011**: `src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs` – Cache-Invalidierung (R6-07)
- **FILE-012**: `src/Romulus.UI.Wpf/Services/TrayService.cs` – try/finally (R6-09)
- **FILE-013**: `src/Romulus.UI.Wpf/ViewModels/DatCatalogViewModel.cs` – CTS Thread-Safety (R6-11)

### Phase 5 (Post-Release)
- **FILE-014**: `src/Romulus.UI.Wpf/ViewModels/MainViewModel*.cs` – ViewModel-Split (C-01)
- **FILE-015**: `src/Romulus.Infrastructure/Orchestration/*.cs` – Namespace-Reorganisation (C-02)
- **FILE-016**: `src/Romulus.UI.Wpf/Services/FeatureCommandService*.cs` – Domain-Handler (C-03)

### Phase 6
- **FILE-017**: `src/Romulus.Tests/WpfCoverageBoostTests.cs` – No-Crash-Only Tests upgraden (F-05)
- **FILE-018**: `src/Romulus.Api/RunManager.cs` – async void try/catch (R7-02)
- **FILE-019**: `src/Romulus.Infrastructure/Watch/WatchFolderService.cs` – Buffer-Size (R7-03)

## 6. Testing

### Phase 1 Tests
- **TEST-001**: `MainViewModel.Productization` – `RefreshRunConfigurationCatalogs` läuft async, kein `.Result` auf UI-Thread → kein SynchronizationContext-Deadlock
- **TEST-002**: API Integration – POST /runs mit Error-provozierendem Payload → JSON-Response `{ "error": "INTERNAL_ERROR" }`, kein Stack Trace
- **TEST-003**: API Integration – POST /runs im Development-Mode → Stack Trace in Response vorhanden (opt-in)

### Phase 2 Tests
- **TEST-004**: `ChdmanToolConverter` Unit-Test – `InvokeProcess` wird mit 5 Argumenten inkl. ToolRequirement aufgerufen (Mock/Verify)
- **TEST-005**: Tool-Hash-Resolution – `unecm.exe`, `flips.exe`, `xdelta3.exe` werden in `tool-hashes.json` gefunden
- **TEST-006**: `RunService` DI-Resolution – AuditCsvStore-Instanz hat Signing Key gesetzt (Properties-Assertion)
- **TEST-007**: API Static Files – GET /dashboard.html respektiert Auth-Middleware-Reihenfolge

### Phase 3 Tests
- **TEST-008**: `MainViewModel` DI – kein Fallback-`new` für RunProfileService; DI liefert konfigurierte Instanz
- **TEST-009**: Region-Determinismus – Region-Test-Suite nach SCAN/EU-Korrektur: `"(Sweden)"` → erwartete Region; `"(Scandinavia)"` → erwartete Region; bestehende Gold-Standards unverändert

### Phase 4 Tests
- **TEST-010**: `ArchiveHashService` – Hash cachen → Datei modifizieren (Size/LastWrite ändern) → erneut hashen → neuer Hash
- **TEST-011**: `TrayService` – Exception während Icon-Erstellung → `_isCreating == false`
- **TEST-012**: `DatCatalogViewModel` – Concurrent CancellationTokenSource-Nutzung → kein ObjectDisposedException

### Phase 5 Tests
- **TEST-013**: Bestehende `GuiViewModelTests` müssen nach ViewModel-Split unverändert grün sein (Regression-Gate)

### Phase 6 Tests
- **TEST-014**: Überarbeitete CoverageBoost-Tests haben fachliche Assertions (Struktur, Werte, Ordering) statt nur `Count > 0`

## 7. Risks & Assumptions

### Risks
- **RISK-001**: R6-01 (Async-Umstellung MainViewModel) kann Cascade-Effekte in Event-Handlern auslösen. `async void` Event-Handler brauchen sorgfältiges Exception-Handling, da ungefangene Exceptions den WPF-Prozess crashen. **Mitigation**: Jeder `async void` Handler bekommt try/catch mit Logging.
- **RISK-002**: R6-10 (SCAN/EU) ist eine fachliche Entscheidung, die Winner-Selection für nordische ROMs beeinflussen kann. **Mitigation**: Goldstandard-Testdaten aus bestehender Test-Suite als Regression-Gate nutzen. Breaking Change explizit dokumentieren falls gewollt.
- **RISK-003**: Phase 5 (ViewModel-Split) berührt WPF-Bindings. Fehlerhafte XAML-Bindings erzeugen stumme Fehler (kein Compile-Error). **Mitigation**: WPF-Smoke-Tests nach jedem Split-Schritt.
- **RISK-004**: R6-06 (Tool-Hashes) – Falls Binaries nicht im Repo vorhanden, können Hashes nicht sofort verifiziert werden. **Mitigation**: `PENDING-VERIFY` Marker + Verifikation im nächsten Build mit echten Binaries.
- **RISK-005**: Phase 6 (Test-Qualität) kann bestehende Coverage-Zahlen senken, wenn no-crash-only Tests durch weniger aber bessere Tests ersetzt werden. **Mitigation**: Coverage-Gate temporär adjustieren.

### Assumptions
- **ASSUMPTION-001**: `SharedServiceRegistration.cs` registriert bereits `AuditCsvStore` mit Signing Key korrekt (verifiziert 2026-04-12)
- **ASSUMPTION-002**: `MainViewModel` wird über DI-Container erzeugt oder erhält DI-Container-Zugriff für Child-Service-Resolution
- **ASSUMPTION-003**: Die bestehende Region-Test-Suite aus TD-029 kann als Goldstandard für SCAN/EU-Entscheidung dienen
- **ASSUMPTION-004**: ChdmanToolConverter hat Constructor Injection und kann ToolRequirement empfangen (oder kann erweitert werden)
- **ASSUMPTION-005**: Phasen 1–4 können innerhalb eines Release-Sprints abgeschlossen werden; Phase 5 ist multi-sprint

## 8. Related Specifications / Further Reading

- [Technical Debt Tracker (Phasen 4–21, alle ✅)](../archive/refactor-technical-debt-tracker-2026-04-09-1.md)
- [Full Repo Audit 2026-04-11](../archive/full-repo-audit-2026-04-11.md)
- [Hard Audit Findings Tracker (39 Findings, alle ✅)](../archive/hard-audit-findings-tracker.md)
- [Romulus Architekturregeln](../.claude/rules/architecture.instructions.md)
- [Romulus Projektregeln](../.claude/rules/project.instructions.md)
- [Romulus Testregeln](../.claude/rules/testing.instructions.md)

## Appendix A – Finding-zu-Task Mapping

| Finding | Schweregrad | Phase | Task(s) | Status-Quelle |
|---------|-------------|-------|---------|---------------|
| R6-01 | Kritisch | 1 | TASK-001, TASK-002 | Verifiziert 2026-04-12: OFFEN |
| R6-08 | Hoch | 1 | TASK-003, TASK-004 | Verifiziert 2026-04-12: OFFEN |
| R6-05 | Hoch | 2 | TASK-006 | Verifiziert 2026-04-12: OFFEN |
| R6-06 | Hoch | 2 | TASK-007 | Verifiziert 2026-04-12: OFFEN |
| R6-03 | Hoch | 2 | TASK-008 | Verifiziert 2026-04-12: OFFEN |
| R7-08 | Mittel | 2 | TASK-009 | Verifiziert 2026-04-12: OFFEN |
| R6-04 | Hoch | 3 | TASK-012, TASK-013 | Verifiziert 2026-04-12: OFFEN |
| R6-10 | Mittel | 3 | TASK-014, TASK-015 | Verifiziert 2026-04-12: OFFEN |
| R6-07 | Hoch | 4 | TASK-017 | Verifiziert 2026-04-12: TEILWEISE |
| R6-09 | Mittel | 4 | TASK-018 | Verifiziert 2026-04-12: OFFEN |
| R6-11+ | Mittel | 4 | TASK-019 | Aus R6-Runde |
| C-01 | P2 | 5 | TASK-022–024 | Full-Repo-Audit: OFFEN |
| C-02 | P2 | 5 | TASK-025 | Full-Repo-Audit: OFFEN |
| C-03 | P2 | 5 | TASK-026 | Full-Repo-Audit: OFFEN |
| F-05 | P2 | 6 | TASK-028 | Full-Repo-Audit: OFFEN |
| R7-02–06 | Mittel | 6 | TASK-029 | Aus R7-Runde |
| R7-07–14 | Niedrig | 6 | TASK-030 | Aus R7-Runde |

## Appendix B – Bereits erledigte Audit-Items (nicht in diesem Plan)

Folgende Finding-Gruppen sind bereits abgeschlossen und werden hier NICHT erneut adressiert:

| Quelle | Findings | Status |
|--------|----------|--------|
| Hard-Audit-Tracker F01–F39 | 39 | ✅ Alle erledigt |
| Technical-Debt-Tracker TD-008–TD-060 | 53 Tasks | ✅ Alle erledigt (Phasen 4–21) |
| Full-Repo-Audit A-01 bis A-04 | 4 Bugs | ✅ Alle gefixt |
| Full-Repo-Audit B-01 bis B-04 | 4 Security | ✅ Alle gefixt |
| Full-Repo-Audit D-01 bis D-03 | 3 Schattenlogik | ✅ Alle gefixt |
| Full-Repo-Audit E-01 bis E-04 | 4 Hygiene | ✅ Alle gefixt |
| Full-Repo-Audit F-01 bis F-06 | 6 Test-QA | ✅ Alle gefixt |
| R6-02 (RunService entkoppelt) | 1 | ✅ Gefixt |
| R7-01 (LiteDB Dispose) | 1 | ✅ Gefixt |
| R8-01 (DatCatalogState Comparer) | 1 | ✅ Gefixt |
| R5-02 (UNKNOWN ConsoleKey) | 1 | ✅ Gefixt |
| R5-03 (Non-Game Losers) | 1 | ✅ Gefixt |
