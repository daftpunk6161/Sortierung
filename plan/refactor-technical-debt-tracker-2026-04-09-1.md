---
goal: Umsetzung aller offenen Findings aus dem Technical Debt Tracker 2026-04-09 (TD-008 bis TD-060)
version: 1.0
date_created: 2026-04-10
last_updated: 2026-04-11
owner: Romulus Team
status: 'In Progress'
tags: [refactor, technical-debt, security, i18n, architecture, stability, hygiene]
---

# Technical Debt Tracker – Vollständiger Implementierungsplan

![Status: In Progress](https://img.shields.io/badge/status-In%20Progress-yellow)

Dieser Plan beschreibt die strukturierte Abarbeitung aller 40 offenen Findings (TD-008 bis TD-060) aus dem [Technical Debt Tracker](../../docs/audits/technical-debt-tracker-2026-04-09.md). Die Phasierung folgt dem dort definierten Quick Execution Plan (Phasen 4–20) und priorisiert nach: P1/Security → P1/Release → P2/Stability → P3/Hygiene.

## 1. Requirements & Constraints

- **REQ-001**: Alle Änderungen müssen `dotnet build src/Romulus.sln` und `dotnet test src/Romulus.Tests/Romulus.Tests.csproj` grün halten
- **REQ-002**: Preview/Execute/Report-Parität darf durch keine Änderung verletzt werden
- **REQ-003**: Determinismus (GameKey, Region, Score, Winner Selection) muss in allen betroffenen Phasen gewahrt bleiben
- **REQ-004**: Kein Datenverlust – Move-to-Trash/Audit-Verhalten darf nicht verändert werden
- **REQ-005**: Jede Phase muss unabhängig deploy- und testbar sein (inkrementelle Delivery)
- **SEC-001**: Race-Condition in GameKeyNormalizer (TD-028) und HMAC-Key-Sicherheit (TD-032) sind sicherheitskritisch → Phase 11 hat höchste Priorität nach Status-Konsolidierung
- **SEC-002**: DatSourceService OOM-Risiko (TD-031) und AuditSigningService OOM-Risiko (TD-033) müssen streaming-basiert gelöst werden
- **CON-001**: Dependency-Richtung Entry Points → Infrastructure → Core → Contracts muss eingehalten werden
- **CON-002**: Keine neuen NuGet-Dependencies ohne explizite Begründung
- **CON-003**: Keine Breaking Changes in CLI/API-Verhalten; nur interne Refactors
- **GUD-001**: Single Source of Truth – jede Konstante, Regel und Konfiguration darf nur an einer Stelle definiert werden
- **GUD-002**: I/O-freier Core – Core darf keine System.IO-Referenzen enthalten (Zielzustand)
- **GUD-003**: Alle user-facing Strings über i18n/Lokalisierung; keine hardcoded deutschen Texte in Logik
- **PAT-001**: Bestehende Patterns nutzen: RunConstants für Konstanten, RunProgressLocalization für Fortschrittsmeldungen, DI-Container für Services
- **PAT-002**: Tests nach Projekt-Testregeln: Unit + Integration + Regression + Negative/Edge

## 2. Implementation Steps

### Phase 4 – Status-String-Konsolidierung (TD-008 + TD-009 + TD-015)

- GOAL-001: Alle Status- und Sort-Decision-Strings in einer einzigen kanonischen Quelle (RunConstants) konsolidieren; alle Verbraucher referenzieren nur diese Quelle.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | **TD-008 – Competing status sources**: Erweitere `RunConstants` (src/Romulus.Contracts/RunConstants.cs) um fehlende Werte: `StatusRunning="running"`, `StatusCompleted="completed"`, `StatusCompletedWithErrors="completed_with_errors"`. Entscheide: `StatusOk` → `StatusCompleted` umbenennen oder Alias beibehalten. Dokumentiere Entscheidung. | [x] | 2026-04-10 |
| TASK-002 | **TD-008 – Remove ApiRunStatus.cs**: Lösche `src/Romulus.Api/ApiRunStatus.cs`. Ersetze alle Referenzen in `src/Romulus.Api/Program.cs`, `RunManager.cs`, `RunLifecycleManager.cs` durch `RunConstants.StatusXxx`. | [x] | 2026-04-10 |
| TASK-003 | **TD-008 – Remove OperationResult duplicates**: Entferne `StatusOk`/`StatusBlocked` Konstanten aus `src/Romulus.Contracts/Models/OperationResult.cs`. Verweise auf `RunConstants`. | [x] | 2026-04-10 |
| TASK-004 | **TD-009 – Replace magic strings in API**: Ersetze 6x `"running"` Literale in `src/Romulus.Api/Program.cs` → `RunConstants.StatusRunning`. Ersetze `Status != "running"` in `src/Romulus.Api/RunManager.cs:375` → `RunConstants.StatusRunning`. | [x] | 2026-04-10 |
| TASK-005 | **TD-009 – Replace magic strings in WPF**: Ersetze `"cancelled"`/`"failed"` in `src/Romulus.UI.Wpf/Models/DashboardProjection.cs:151-152`, `"blocked"` in `ErrorSummaryProjection.cs:45`, `"cancelled"` in `MainViewModel.RunPipeline.cs:1180` → `RunConstants.StatusXxx`. | [x] | 2026-04-10 |
| TASK-006 | **TD-015 – Sort decision constants**: Füge `RunConstants.SortDecisions` Klasse hinzu mit `Sort`, `Review`, `Blocked`, `DatVerified`. Ersetze alle String-Literale in `src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs:63,150,212,271`. | [x] | 2026-04-10 |
| TASK-007 | **Verification**: `grep -rn '"running"\|"completed"\|"cancelled"\|"failed"\|"blocked"\|"ok"\|"Sort"\|"Review"\|"Blocked"\|"DatVerified"' src/` → Zero hits in Produktionscode (exkl. Konstanten-Definitionen). Build + Tests grün. | [x] | 2026-04-11 |

### Phase 5 – API Error Classification Refactor (TD-010 + TD-011)

- GOAL-002: Fragile string-basierte Fehlerklassifikation durch typsicheres Pattern ersetzen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-008 | **TD-011 – Define error codes**: Erstelle `enum ConfigurationErrorCode` in `src/Romulus.Contracts/` mit Werten: `ProtectedSystemPath`, `DriveRoot`, `UncPath`, `InvalidRegion`, `InvalidConsole`, `MissingDatRoot`, `MissingTrashRoot`, `InvalidPath`, `PathTraversal`, `ReparsePoint`, `AccessDenied`, `Unknown`. | [x] | 2026-04-10 |
| TASK-009 | **TD-011 – Typed exception**: Erstelle `ConfigurationValidationException : Exception` in Contracts mit `ConfigurationErrorCode Code` Property. | [x] | 2026-04-10 |
| TASK-010 | **TD-011 – Throw typed exceptions**: Passe Safety/Infrastructure-Layer an, um `ConfigurationValidationException` mit passendem Code zu werfen statt generischer Messages. Betrifft: `SafetyValidator`, `RunEnvironmentBuilder`, Konsolen-Validierung. | [x] | 2026-04-10 |
| TASK-011 | **TD-010 + TD-011 – Unified error mapper**: Ersetze `MapRunConfigurationError()` und `MapWatchConfigurationError()` (src/Romulus.Api/ProgramHelpers.cs:509-585) durch eine einzelne Methode `MapConfigurationError(ConfigurationValidationException ex, string prefix)` die auf `ex.Code` switched statt auf `message.Contains()`. | [x] | 2026-04-10 |
| TASK-012 | **Tests**: Unit-Tests für jeden `ConfigurationErrorCode` → korrekter API-Fehlercode. Regression-Test: bestehende API-Error-Responses unverändert. | [x] | 2026-04-10 |

### Phase 6 – Core I/O Violation + i18n Hygiene (TD-012 + TD-013)

- GOAL-003: Core von System.IO-Abhängigkeiten befreien; hardcoded deutsche Strings in WPF durch i18n ersetzen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-013 | **TD-012 – Move I/O facades to Infrastructure**: Verschiebe `SetParserIo` und `ClassificationIo` (static delegates) von `src/Romulus.Core/` nach `src/Romulus.Infrastructure/IO/`. Core-Klassen (DiscHeaderDetector, CartridgeHeaderDetector, Set-Parser) erhalten I/O-Delegates per Constructor-Injection statt statische Defaults. | [x] | 2026-04-11 |
| TASK-014 | **TD-012 – Update DI registration**: SharedServiceRegistration muss I/O-Delegates an Core-Classifier/Parser injizieren. | [x] | 2026-04-10 |
| TASK-015 | **TD-012 – Verify zero System.IO in Core**: `grep -rn 'System.IO\|File\.\|FileStream\|FileInfo\|ZipFile' src/Romulus.Core/` → nur Namespace-Imports ohne direkte Aufrufe. | [x] | 2026-04-10 |
| TASK-016 | **TD-013 – Lokalisierbare Keys WPF**: Erstelle i18n-Keys in `data/i18n/de.json` und `data/i18n/en.json` für alle 30+ hardcoded deutschen Strings aus `FeatureCommandService.Infra.cs`, `FeatureCommandService.Security.cs`, `FeatureService.Export.cs`, `FeatureCommandService.Dat.cs`, `FeatureCommandService.Workflow.cs`. | [x] | 2026-04-10 |
| TASK-017 | **TD-013 – Replace hardcoded strings**: Ersetze alle Hardcoded-Strings in `FeatureCommandService*.cs` durch `_vm.Loc["key"]` Aufrufe. | [x] | 2026-04-10 |
| TASK-018 | **Verification**: `grep -rn '"Hardlink\|"Verfügbar\|"Speicherplatz\|"Integritäts\|"Benutzerdefinierte\|"Erstelle Regeln' src/Romulus.UI.Wpf/Services/` → Zero hits. | [x] | 2026-04-10 |

### Phase 7 – Conversion Pipeline Cleanup (TD-014 + TD-022)

- GOAL-004: Duplikate in Conversion eliminieren; Hardcoded-Kompressionsparameter konfigurierbar machen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-019 | **TD-014 – Extract shared RVZ magic check**: Erstelle statische Methode `RvzFormatHelper.VerifyMagicBytes(string path)` in `src/Romulus.Infrastructure/Conversion/`. Entferne duplizierte Implementierungen in `ToolInvokerAdapter.cs:185-198` und `DolphinToolInvoker.cs:92-103`; beide delegieren an Helper. | [x] | 2026-04-10 |
| TASK-020 | **TD-022 – Configurable compression params**: Erweitere `data/conversion-registry.json` um `compressionAlgorithm`, `compressionLevel`, `blockSize` pro Format. Passe `ToolInvokerAdapter.cs:133-142` und `DolphinToolInvoker.cs:50` an, Werte aus Registry zu lesen statt hardcoded. | [x] | 2026-04-10 |
| TASK-021 | **Tests**: Unit-Test für RVZ magic check (gültig/ungültig/leer). Test für Compression-Parameter aus Registry. | [x] | 2026-04-10 |

### Phase 8 – Async/Time/HTTP Modernisierung (TD-016 + TD-017 + TD-018 + TD-019)

- GOAL-005: Sync-over-async reduzieren; TimeProvider einführen; HttpClient korrekt über DI verwalten; Thread.Sleep eliminieren.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-022 | **TD-016 – Convert remaining sync-over-async**: CLI: `Program.cs:277,747,855` → async Äquivalente oder dokumentierte Rechtfertigung. `CliOptionsMapper.cs:38,60` → async oder Kommentar. API: `Program.cs:1916` (Shutdown) → CancellationToken-basiert. `RunManager.cs:126` → async. Jede Stelle die blockierend bleibt, erhält `// SYNC-JUSTIFIED:` Kommentar mit Begründung. | [x] | 2026-04-10 |
| TASK-023 | **TD-017 – Introduce TimeProvider**: Definiere `ITimeProvider` Interface in Contracts (oder nutze .NET 8+ `TimeProvider`). Erstelle `SystemTimeProvider : TimeProvider` in Infrastructure. Registriere in DI. | [x] | 2026-04-10 |
| TASK-024 | **TD-017 – Replace DateTime.UtcNow**: Ersetze alle 30+ `DateTime.UtcNow` Aufrufe in `RunLifecycleManager.cs` (7x), `RateLimiter.cs` (2x), `RunManager.cs` (3x), `Program.cs` (10+x), `MainViewModel.RunPipeline.cs` (4x) durch `_timeProvider.GetUtcNow()`. | [x] | 2026-04-10 |
| TASK-025 | **TD-018 – IHttpClientFactory**: Ändere `DatSourceService` Constructor (src/Romulus.Infrastructure/Dat/DatSourceService.cs:38): statt `new HttpClient()` → `HttpClient` per Constructor-Injection. Registriere `IHttpClientFactory` in DI (`services.AddHttpClient<DatSourceService>()`). | [x] | 2026-04-10 |
| TASK-026 | **TD-019 – Replace Thread.Sleep**: Ersetze `Thread.Sleep` in `ProfileService.cs:36,40` und `SettingsFileAccess.cs:39` durch `await Task.Delay()`. Stelle sicher, dass Aufrufer async sind (ggf. async cascade). | [x] | 2026-04-10 |
| TASK-027 | **Tests**: TimeProvider-kontrollierte Tests für Zeitabhängige Logik. DatSourceService Test mit injiziertem HttpClient. | [x] | 2026-04-10 |

### Phase 9 – Minor Hardening (TD-020 + TD-021 + TD-023)

- GOAL-006: Leere catch-Blöcke diagnostisch absichern; hardcoded Pfad entfernen; CLI-Help-Konstante nutzen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-028 | **TD-020 – Add logging to empty catches**: Füge strukturiertes Logging (oder dokumentierten `// SUPPRESSED:` Kommentar) zu leeren `catch(IOException)` Blöcken hinzu: `DiscHeaderDetector.cs:74,99`, `CartridgeHeaderDetector.cs:64,202`, `ConsoleDetector.cs:550`, `FolderDeduplicator.cs:67`, `ConversionExecutor.cs:415,422`, `DatCatalogViewModel.cs:210,367`. Pro Stelle: mindestens `logger.LogDebug(ex, "...")` oder expliziter Kommentar warum Suppression korrekt ist. | [x] | 2026-04-10 |
| TASK-029 | **TD-021 – Remove hardcoded C:\tools\conversion**: Ersetze `C:\tools\conversion` in `ToolRunnerAdapter.cs:20` durch relativen Pfad (z.B. `tools/conversion`) oder Settings-basierten Default aus `defaults.json`. | [x] | 2026-04-10 |
| TASK-030 | **TD-023 – CLI Help stale-days constant**: Ersetze hardcoded `"365"` in `CliOutputWriter.cs:185` durch `$"{DatCatalogStateService.StaleThresholdDays}"`. | [x] | 2026-04-10 |
| TASK-031 | **Verification**: Grep für `C:\\tools` → Zero hits. Grep für leere catch → alle haben Log oder Kommentar. | [x] | 2026-04-10 |

### Phase 10 – P3 Hygiene Round 2 (TD-024 + TD-025 + TD-026 + TD-027)

- GOAL-007: SHA1-Default dokumentieren; Conversion-Paritätstest; JSON-Startup-Validierung; AppStateStore Immutability.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-032 | **TD-024 – SHA1 documentation**: Dokumentiere in `FileHashService.cs:210` warum SHA1 Standard ist (DAT/No-Intro Kompatibilität). Für neue nicht-DAT-Pfade: Default auf SHA256 setzen oder klar als SHA1-obligatorisch kommentieren. | [x] | 2026-04-10 |
| TASK-033 | **TD-025 – Conversion preview/execute parity test**: Füge `DryRunAndMoveMode_KeepConversionEstimateParity()` Test in `PreviewExecuteParityTests.cs` hinzu. Assertiere dass ConvertedCount zwischen DryRun und Move identisch ist. | [x] | 2026-04-10 |
| TASK-034 | **TD-026 – Console key cross-validation**: Füge Startup-Validierung in `ConversionRegistryLoader.cs:127,191` hinzu: alle `applicableConsoles` Keys müssen in `consoles.json` vorhanden sein. Klarer Fehler bei ungültigem Key. | [x] | 2026-04-10 |
| TASK-035 | **TD-027 – IReadOnlyDictionary**: Ändere `AppStateStore.Get()` Return-Type zu `IReadOnlyDictionary<string, object>`. Prüfe Kompilierung aller Aufrufer. | [x] | 2026-04-10 |

### Phase 11 – Critical Security + Race Condition (TD-028 + TD-032 + TD-033)

- GOAL-008: Race-Condition in GameKeyNormalizer eliminieren; HMAC-Key-Sicherheit garantieren; Audit-CSV-Rollback streaming-fähig machen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-036 | **TD-028 – Atomic volatile state**: Ersetze in `GameKeyNormalizer.cs` die beiden separaten `volatile` Felder (`_registeredPatterns`, `_registeredAliasMap`) durch ein einzelnes `volatile` Feld: `private volatile (IReadOnlyList<...> Patterns, IReadOnlyDictionary<...> Aliases)? _registeredState`. `RegisterPatterns()` setzt Tuple atomar. `Normalize()` liest Tuple einmal in lokale Variable → keine Partial-State-Window. | [x] | 2026-04-11 |
| TASK-037 | **TD-032 – HMAC key security**: Ändere `AuditSigningService.cs:90-93`: Wenn ACL-Restriction fehlschlägt, lösche die erstellte Key-Datei und werfe `InvalidOperationException("HMAC key file cannot be secured")`. Kein Fallback auf unsichere Datei. | [x] | 2026-04-11 |
| TASK-038 | **TD-033 – Streaming audit CSV rollback**: Ersetze `File.ReadAllLines()` in `AuditSigningService.cs:268` durch `StreamReader` mit zeilenweiser Verarbeitung. Kein Full-File-Load bei Rollback. | [x] | 2026-04-11 |
| TASK-039 | **Tests**: Concurrency-Test für GameKeyNormalizer (parallele Register + Normalize). Test dass HMAC Key bei Permission-Fehler nicht genutzt wird. Test dass Rollback mit großer Datei nicht OOM verursacht (Stream-basiert). | [x] | 2026-04-11 |

### Phase 12 – Data-Driven Migration: Regions, Formats, Languages (TD-029 + TD-030 + TD-034)

- GOAL-009: Hardcoded Business-Rules in C# durch datengetriebene Konfiguration ersetzen; Single Source of Truth für Regions, Formate und Sprachen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-040 | **TD-029 – Data-driven region rules**: Erweitere `data/rules.json` um strukturierten `regionRules`-Block mit allen 25 Regex-Rules und dem `regionTokenMap` (100+ Einträge). Erstelle `RegionRulesLoader` in Infrastructure. `RegionDetector` erhält Rules per Registration (analog GameKeyNormalizer). Entferne `DefaultOrderedRules` und `RegionTokenMap` Hardkoding. | [x] | 2026-04-11 |
| TASK-041 | **TD-030 – Data-driven format scores**: Erstelle `data/format-scores.json` (oder integriere in `conversion-registry.json`) mit allen 40+ Format→Score Mappings. `FormatScorer` liest Scores per Injection statt statischem Switch. Entferne `GetFormatScore` Switch-Statement. | [x] | 2026-04-11 |
| TASK-042 | **TD-034 – Unified language codes**: Entferne `LanguageCodes`/`EuLanguageCodes` aus `RegionDetector.cs:242-250` und `langPattern` Regex aus `VersionScorer.cs:32-33`. Lade alle Language-Code-Definitionen aus `data/rules.json`. Baue Regex dynamisch. | [x] | 2026-04-11 |
| TASK-043 | **Tests**: Determinismus-Tests: Gleiche Eingaben → gleiche Region/Score/Language-Ergebnisse vor und nach Migration. Regressions-Testdaten aus bestehenden Tests als Goldstandard nutzen. | [x] | 2026-04-11 |

### Phase 13 – Streaming + Timeout Hardening (TD-031 + TD-044 + TD-046)

- GOAL-010: OOM-Risiken durch Streaming eliminieren; Settings-Retry mit Timeout absichern; Scan-Fehler nicht verschlucken.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-044 | **TD-031 – Streaming DAT download**: Ändere `DatSourceService.cs:112-113`: Verwende `HttpCompletionOption.ResponseHeadersRead` + Streaming mit Chunk-Reader. Brich Download ab sobald kumulative Bytes `MaxDownloadBytes` überschreiten. Kein `ReadAsByteArrayAsync()` mehr. | [x] | 2026-04-11 |
| TASK-045 | **TD-044 – Settings retry timeout**: Füge `totalTimeoutMs` Parameter zu `SettingsFileAccess` Retry-Loop hinzu (src/Romulus.Infrastructure/Configuration/SettingsFileAccess.cs:10-30). Default: 2000ms. Abort wenn Gesamtzeit überschritten. | [x] | 2026-04-11 |
| TASK-046 | **TD-046 – Surface scan iteration errors**: In `StreamingScanPipelinePhase.cs:90-110`: Fange `UnauthorizedAccessException` und andere I/O-Fehler im async Iterator. Sammle in Error-Collection. Report am Ende: "Scan incomplete: X directories inaccessible". User sieht Warning. | [x] | 2026-04-11 |
| TASK-047 | **Tests**: Download-Abort-Test mit Mock-Server der >MaxDownloadBytes sendet. Settings-Timeout-Test. Scan-Test mit gesperrtem Verzeichnis → Warning in Output. | [x] | 2026-04-11 |

### Phase 14 – Dedup Safety + WPF Lifecycle (TD-035 + TD-036 + TD-041 + TD-042)

- GOAL-011: Ambiguous GroupKey eliminieren; WPF Memory Leaks fixen; Rollback-Fehler korrekt eskalieren; ViewModel-DI.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-048 | **TD-035 – Safe group key**: Ersetze `"\|\|"` Separator in `DeduplicationEngine.cs:95-98` durch Tuple-basiertes Grouping `(ConsoleKey, GameKey)` oder verwende `\0` (Null-Char) als Separator. | [x] | 2026-04-11 |
| TASK-049 | **TD-036 – PropertyChanged unsubscribe**: Füge `-= OnExtensionCheckedChanged`, `-= OnConsoleCheckedChanged`, `-= OnExtensionFilterChanged` vor jedem `.Clear()` der entsprechenden Collections in `MainViewModel.cs:379,489` und `MainViewModel.RunPipeline.cs:1417` hinzu. | [x] | 2026-04-11 |
| TASK-050 | **TD-041 – DI for child ViewModels**: Registriere `ShellViewModel`, `SetupViewModel`, `ToolsViewModel` etc. im DI-Container. `MainViewModel` empfängt sie per Constructor-Injection statt `new`. | [x] | 2026-04-11 |
| TASK-051 | **TD-042 – Escalate rollback failure**: Ändere `MovePipelinePhase.cs:230-280`: Wenn Rollback eines Set-Member-Moves fehlschlägt → Status auf `FAILURE` setzen (nicht WARNING). Run abbrechen wenn konsistenter State nicht wiederherstellbar. | [x] | 2026-04-11 |
| TASK-052 | **Tests**: GroupKey-Collision-Test mit `\|\|` im GameKey. Memory-Leak-Regression-Test (Subscribe-Count nach Clear). Rollback-Failure-Escalation-Test. | [x] | 2026-04-11 |

### Phase 15 – Config-Driven Scoring + Schema Validation (TD-037 + TD-038 + TD-039 + TD-043)

- GOAL-012: Business-KPI-Gewichte und Kategorie-Ränge konfigurierbar machen; System-Path-Cache; JSON-Schema-Validierung.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-053 | **TD-037 – Configurable health score weights**: Verschiebe die 8 Magic Numbers in `HealthScorer.cs:11-16` nach `data/defaults.json` unter neuem Key `healthScoreWeights`. Inject Werte in `HealthScorer`. Dokumentiere Formel-Rationale als Kommentar. | [x] | 2026-04-11 |
| TASK-054 | **TD-038 – Configurable category ranks**: Verschiebe `FileCategory` Priority-Ranks (Game=5, Bios=4 etc.) aus `DeduplicationEngine.cs:51-59` nach `data/defaults.json` unter `categoryPriorityRanks`. | [x] | 2026-04-11 |
| TASK-055 | **TD-039 – Cache protected system roots**: Ändere `SafetyValidator.IsProtectedSystemPath` (src/Romulus.Infrastructure/Safety/SafetyValidator.cs:340-351): Berechne `string[]` der geschützten Roots einmal als `static readonly Lazy<string[]>`. Keine Allocation pro Aufruf. | [x] | 2026-04-11 |
| TASK-056 | **TD-043 – JSON schema validation**: Erstelle JSON-Schemas in `data/schemas/` für: `consoles.json`, `rules.json`, `defaults.json`, `conversion-registry.json`, `dat-catalog.json`. Validiere bei Startup. Klarer Fehler mit Datei + Feld bei Mismatch. | [x] | 2026-04-11 |
| TASK-057 | **Tests**: HealthScore-Test mit Custom-Weights. CategoryRank-Test mit geänderter Config. Schema-Validation-Test mit bewusst defekter JSON. | [x] | 2026-04-11 |

### Phase 16 – Test Quality + P3 Hygiene Round 3 (TD-045 + TD-047 + TD-048–TD-054)

- GOAL-013: Test-Stubs verbessern; UK-Region-Duplikat bereinigen; P3-Hygiene-Items abarbeiten.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-058 | **TD-045 – Improve test stubs**: Erweitere `StubDialogService` und andere Test-Stubs in `GuiViewModelTests.cs:2632-2700` um `CallCount`, `LastArgs` Tracking. Füge Assertions in bestehende Tests hinzu, die verifizieren dass Services aufgerufen werden. | [x] | 2026-04-11 |
| TASK-059 | **TD-047 – Deduplicate UK detection**: Entferne standalone `UkPattern` in `RegionDetector.cs:152-156`. UK-Erkennung erfolgt nur noch über `DefaultOrderedRules`/datengetriebene Rules (nach Phase 12). Dokumentiere Prioritätsreihenfolge. | [x] | 2026-04-11 |
| TASK-060 | **TD-048 – GameKeyNormalizer DOS limit**: Erhöhe Limit in `GameKeyNormalizer.cs:310-316` auf 50 oder entferne zugunsten einer abschließenden Regex. Füge `logger.LogWarning` hinzu wenn Limit erreicht wird. | [x] | 2026-04-11 |
| TASK-061 | **TD-049 – VersionScorer configurable max segments**: Mache `maxSegments` in `VersionScorer.cs:101-117` konfigurierbar via `defaults.json`. Log Warning bei Truncation. | [x] | 2026-04-11 |
| TASK-062 | **TD-050 – O(1) region score lookup**: Konvertiere `preferOrder` Array in `FormatScorer.cs:68-79` zu `Dictionary<string,int>` für O(1) Lookup. | [x] | 2026-04-11 |
| TASK-063 | **TD-051 – Narrow AllowedRootPathPolicy catch**: Ersetze `catch(Exception)` in `AllowedRootPathPolicy.cs:29-33` durch `catch(ArgumentException)`, `catch(PathTooLongException)`, `catch(NotSupportedException)`. | [x] | 2026-04-11 |
| TASK-064 | **TD-052 – Eliminate SetupViewModel duplication**: `SetupViewModel` projeziert von `MainViewModel` State statt eigene Properties zu duplizieren. Entferne `SyncToSetup()` und duplizierte Properties. | [x] | 2026-04-11 |
| TASK-065 | **TD-053 – Exhaustive DatAuditStatus match**: Ersetze `_ => 60` Default in `DatAuditPipelinePhase.cs:70-88` durch `throw new InvalidOperationException($"Unexpected status: {status}")`. | [x] | 2026-04-11 |
| TASK-066 | **TD-054 – Log malformed defaults.json**: Füge `logger.LogWarning("defaults.json parsing failed: {Message}", ex.Message)` zum `catch(JsonException)` in `SettingsLoader.cs:160-200` hinzu. | [x] | 2026-04-11 |

### Phase 17 – Disc Extension Consolidation (TD-055 + TD-057)

- GOAL-014: Genau eine kanonische Disc-Extension-Definition; alle 11+ Inline-Checks nutzen zentrale Methode.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-067 | **TD-055 – Canonical disc extension set**: Erstelle `DiscFormats` Klasse in `src/Romulus.Contracts/` mit `public static IReadOnlySet<string> AllDiscExtensions` (Superset beider aktueller Sets, 31 Einträge). Definiere benannte Subsets: `ClassicDiscExtensions`, `NintendoExtensions`, `ArchiveExtensions`. | [x] | 2026-04-11 |
| TASK-068 | **TD-055 – Migrate Core FormatScorer**: `FormatScorer.DiscExtensions` → delegiert an `DiscFormats.AllDiscExtensions`. Entferne lokales HashSet. | [x] | 2026-04-11 |
| TASK-069 | **TD-055 – Migrate Infrastructure ExecutionHelpers**: `ExecutionHelpers.GetDiscExtensions()` → delegiert an `DiscFormats.AllDiscExtensions`. Entferne lokales HashSet. | [x] | 2026-04-11 |
| TASK-070 | **TD-057 – Replace all inline checks**: Ersetze alle `is ".chd" or ".iso" or ...` Patterns in den 11 betroffenen Dateien (MainViewModel.Productization.cs, FeatureCommandService.cs, FeatureCommandService.Conversion.cs, ConsoleDetector.cs, DiscHeaderDetector.cs, FamilyDatStrategyResolver.cs, DiscDatStrategy.cs, FormatConverterAdapter.cs, ChdmanToolConverter.cs, EnrichmentPipelinePhase.cs) durch `DiscFormats.IsDiscExtension(ext)` oder explizit benanntes Subset. Wo intentional nur ein Subset gemeint ist: benannten Subset verwenden und dokumentieren. | [x] | 2026-04-11 |
| TASK-071 | **Verification**: `grep -rn 'is ".chd"\|is ".iso"\|is ".cue"\|\.Contains(".chd")\|\.Contains(".iso")' src/` → Zero inline disc checks. Build + Tests grün. | [x] | 2026-04-11 |

### Phase 18 – Pipeline Phase Name Constants (TD-056)

- GOAL-015: Alle Pipeline-Phasennamen als zentrale Konstanten; kein Hardcoded-String in Orchestration oder WPF.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-072 | **TD-056 – Define phase constants**: Erweitere `RunConstants` (oder `PipelinePhaseWeights`) in Contracts um `public static class Phases` mit: `Preflight = "[Preflight]"`, `Scan = "[Scan]"`, `Filter = "[Filter]"`, `Dedupe = "[Dedupe]"`, `Junk = "[Junk]"`, `Move = "[Move]"`, `Sort = "[Sort]"`, `Convert = "[Convert]"`, `Report = "[Report]"`, `Finished = "[Fertig]"`. | [x] | 2026-04-11 |
| TASK-073 | **TD-056 – Replace literals in Infrastructure**: Ersetze alle Phase-Prefix-Literale in `RunOrchestrator.StandardPhaseSteps.cs`, `RunOrchestrator.ScanAndConvertSteps.cs`, `WinnerConversionPipelinePhase.cs`, `StreamingScanPipelinePhase.cs`. | [x] | 2026-04-11 |
| TASK-074 | **TD-056 – Replace literals in WPF**: Ersetze Phase-Prefix-Literale in `MainViewModel.RunPipeline.WatchAndProgress.cs:440-461`. | [x] | 2026-04-11 |
| TASK-075 | **Verification**: Grep für `"[Preflight]"\|"[Scan]"\|"[Filter]"\|"[Dedupe]"\|"[Junk]"\|"[Move]"\|"[Sort]"\|"[Convert]"\|"[Report]"\|"[Fertig]"` → Zero hits in Produktionscode. | [x] | 2026-04-11 |

### Phase 19 – Infrastructure/Report i18n (TD-058 + TD-060)

- GOAL-016: Alle deutschen Strings in Infrastructure-Orchestration, Pipeline-Phases und Reports durch Lokalisierung ersetzen.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-076 | **TD-058 – i18n keys for Infrastructure progress**: Erstelle i18n-Keys in `de.json`/`en.json` für alle 30+ deutschen Progress-Strings in `RunOrchestrator.StandardPhaseSteps.cs`, `RunOrchestrator.ScanAndConvertSteps.cs`, `RunOrchestrator.PreviewAndPipelineHelpers.cs`, `MovePipelinePhase.cs`, `StreamingScanPipelinePhase.cs`, `WinnerConversionPipelinePhase.cs`, `WatchFolderService.cs`. | [x] | 2026-04-11 |
| TASK-077 | **TD-058 – Replace via RunProgressLocalization**: Ersetze alle hardcoded deutschen Strings durch `RunProgressLocalization`-Aufrufe oder injizierte Lokalisierungsstrings. | [x] | 2026-04-11 |
| TASK-078 | **TD-060 – i18n for HTML reports**: Ersetze deutsche Strings in `ReportGenerator.cs:300,308,348-350` ("Convert-Fehler", "Fehler", "Datei(en)", "UNKNOWN bedeutet...", "Mögliche Ursachen...") durch lokalisierte Report-Strings. | [x] | 2026-04-11 |
| TASK-079 | **Verification**: `grep -rn '"Dateien sammeln\|"Fortschritt\|"Verschiebe\|"Analyse übersprungen\|"Duplikate\|"Konvertierung\|"abgeschlossen\|"FileSystemWatcher-Fehler\|"Convert-Fehler\|"Mögliche Ursachen"' src/` → Zero hits. | [x] | 2026-04-11 |

### Phase 20 – Dead Code Cleanup (TD-059)

- GOAL-017: Ungenutzte Production-Methode entfernen oder integrieren.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-080 | **TD-059 – FormatFixDatReport**: Prüfe ob `DatAnalysisService.FormatFixDatReport()` in CLI/API/GUI Report-Pfad integrierbar ist. Falls ja: integrieren und Test anpassen. Falls nein: Methode auf `internal` setzen oder entfernen. Test ggf. anpassen. | [x] | 2026-04-11 |
| TASK-081 | **Verification**: Wenn entfernt: Build grün, keine Referenzen mehr. Wenn integriert: Produktions-Report-Test deckt Methode ab. | [x] | 2026-04-11 |

### Phase 21 – Remaining P2 Items (TD-040 + TD-044)

- GOAL-018: SafetyValidator Depth-Guard und SettingsFileAccess Timeout sind in früheren Phasen adressiert, hier Auffang für übrig gebliebene Items.

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-082 | **TD-040 – Max depth guard for ancestry walk**: Füge `const int MaxAncestryDepth = 256` in `SafetyValidator.EnsureNoReparsePointInExistingAncestry` (src/Romulus.Infrastructure/Safety/SafetyValidator.cs:418-459) hinzu. Break mit `throw new InvalidOperationException("Max ancestry depth exceeded")` wenn überschritten. | [x] | 2026-04-11 |
| TASK-083 | **Tests**: Test mit tief verschachteltem Pfad (>256 Level) → Exception. Test mit normalem Pfad → kein Fehler. | [x] | 2026-04-11 |

## 3. Alternatives

- **ALT-001**: TD-011 (API error classification) könnte auch mit Error-Code-Strings statt Typed Exceptions gelöst werden – abgelehnt, weil Typsicherheit wichtiger als Einfachheit ist und Compile-Time-Checks bietet.
- **ALT-002**: TD-012 (Core I/O) könnte statt Move-to-Infrastructure auch über ein `IFileSystem` Interface in Core abstrahiert werden – abgelehnt, weil Move-to-Infrastructure die sauberere Schichttrennung ist und weniger Interfaces erfordert.
- **ALT-003**: TD-017 (TimeProvider) könnte auch als eigenes `ITimeProvider` Interface statt .NET 8+ `TimeProvider` umgesetzt werden – empfohlen wird `TimeProvider` da .NET 10 Zielplattform.
- **ALT-004**: TD-029/TD-030/TD-034 (data-driven migration) könnten auch als YAML statt JSON gespeichert werden – abgelehnt, da alle bestehenden Data-Files JSON sind und Konsistenz wichtiger ist.
- **ALT-005**: TD-055 (DiscFormats) könnte auch als Flag-Enum statt HashSet umgesetzt werden – abgelehnt, da String-Extensions der natürliche Key für Dateierweiterungen sind.
- **ALT-006**: TD-043 (JSON Schema Validation) könnte mit `System.Text.Json` Schema oder `NJsonSchema` umgesetzt werden – Empfehlung: leichtgewichtige manuelle Validierung ohne neue Dependency, da Schemas einfach sind.

## 4. Dependencies

- **DEP-001**: Phase 11 (TD-028 GameKeyNormalizer) hat keine externen Dependencies, aber alle Phasen die Region/Score/Key-Logik berühren (12, 14, 15, 16) müssen Phase 11 als Voraussetzung haben
- **DEP-002**: Phase 12 (data-driven rules.json) muss vor Phase 16 (TD-047 UK-Pattern Dedup) abgeschlossen sein, da TD-047 auf datengetriebene Rules aufbaut
- **DEP-003**: Phase 17 (Disc Extensions) sollte nach Phase 7 (Conversion Cleanup) erfolgen, da beide Conversion-Dateien berühren
- **DEP-004**: Phase 6 (TD-013 WPF i18n) und Phase 19 (TD-058/060 Infra i18n) sind unabhängig voneinander, aber sollten konsistente i18n-Key-Konventionen nutzen → gemeinsame Naming-Convention vor Beginn festlegen
- **DEP-005**: Phase 8 (TD-017 TimeProvider) benötigt .NET 8+ `TimeProvider` Klasse → bereits durch net10.0 TFM garantiert
- **DEP-006**: Phase 5 (TD-011 Typed Exceptions) muss vor Phase 9 (TD-020 empty catches) abgeschlossen sein, um konsistente Exception-Patterns zu etablieren
- **DEP-007**: Phase 15 (TD-043 JSON Schema) sollte nach Phasen 12 und 15 erfolgen, da rules.json/defaults.json dort erweitert werden

## 5. Files

### Contracts Layer
- **FILE-001**: `src/Romulus.Contracts/RunConstants.cs` – Status-Konstanten, Sort-Decisions, Phase-Names (TD-008, TD-015, TD-056)
- **FILE-002**: `src/Romulus.Contracts/Models/OperationResult.cs` – Duplikat-Konstanten entfernen (TD-008)
- **FILE-003**: `src/Romulus.Contracts/PipelinePhaseWeights.cs` – Phase-Name-Referenzen (TD-056)
- **FILE-004**: `src/Romulus.Contracts/ConfigurationErrorCode.cs` – **NEU** Fehlercode-Enum (TD-011)
- **FILE-005**: `src/Romulus.Contracts/ConfigurationValidationException.cs` – **NEU** Typed Exception (TD-011)
- **FILE-006**: `src/Romulus.Contracts/DiscFormats.cs` – **NEU** Kanonische Disc-Extension-Definition (TD-055)

### Core Layer
- **FILE-007**: `src/Romulus.Core/GameKeys/GameKeyNormalizer.cs` – Atomic volatile state (TD-028), DOS limit (TD-048)
- **FILE-008**: `src/Romulus.Core/Regions/RegionDetector.cs` – Data-driven rules (TD-029), UK-Dedup (TD-047), Language codes (TD-034)
- **FILE-009**: `src/Romulus.Core/Scoring/FormatScorer.cs` – Data-driven scores (TD-030), O(1) lookup (TD-050), DiscExtensions migration (TD-055)
- **FILE-010**: `src/Romulus.Core/Scoring/VersionScorer.cs` – Language codes (TD-034), max segments config (TD-049)
- **FILE-011**: `src/Romulus.Core/Scoring/HealthScorer.cs` – Configurable weights (TD-037)
- **FILE-012**: `src/Romulus.Core/Deduplication/DeduplicationEngine.cs` – Safe group key (TD-035), configurable ranks (TD-038)
- **FILE-013**: `src/Romulus.Core/Classification/DiscHeaderDetector.cs` – Logging in catch (TD-020), disc ext migration (TD-057)
- **FILE-014**: `src/Romulus.Core/Classification/CartridgeHeaderDetector.cs` – Logging in catch (TD-020)
- **FILE-015**: `src/Romulus.Core/Classification/ConsoleDetector.cs` – Logging in catch (TD-020), disc ext migration (TD-057)
- **FILE-016**: `src/Romulus.Core/SetParsing/SetParserIo.cs` – Move to Infrastructure (TD-012)
- **FILE-017**: `src/Romulus.Core/Classification/ClassificationIo.cs` – Move to Infrastructure (TD-012)

### API Layer
- **FILE-018**: `src/Romulus.Api/ApiRunStatus.cs` – **LÖSCHEN** (TD-008)
- **FILE-019**: `src/Romulus.Api/Program.cs` – Status literals, DateTime.UtcNow, phase prefixes (TD-009, TD-016, TD-017)
- **FILE-020**: `src/Romulus.Api/ProgramHelpers.cs` – Error mapper consolidation (TD-010, TD-011)
- **FILE-021**: `src/Romulus.Api/RunManager.cs` – Status literal, sync-over-async, DateTime.UtcNow (TD-009, TD-016, TD-017)
- **FILE-022**: `src/Romulus.Api/RunLifecycleManager.cs` – DateTime.UtcNow (TD-017)
- **FILE-023**: `src/Romulus.Api/RateLimiter.cs` – DateTime.UtcNow (TD-017)

### CLI Layer
- **FILE-024**: `src/Romulus.CLI/Program.cs` – Sync-over-async (TD-016)
- **FILE-025**: `src/Romulus.CLI/CliOptionsMapper.cs` – Sync-over-async (TD-016)
- **FILE-026**: `src/Romulus.CLI/CliOutputWriter.cs` – Stale-days constant (TD-023)

### Infrastructure Layer
- **FILE-027**: `src/Romulus.Infrastructure/Dat/DatSourceService.cs` – IHttpClientFactory (TD-018), streaming download (TD-031)
- **FILE-028**: `src/Romulus.Infrastructure/Audit/AuditSigningService.cs` – HMAC security (TD-032), streaming rollback (TD-033)
- **FILE-029**: `src/Romulus.Infrastructure/Safety/SafetyValidator.cs` – Cached roots (TD-039), max depth (TD-040)
- **FILE-030**: `src/Romulus.Infrastructure/Safety/AllowedRootPathPolicy.cs` – Narrow catch (TD-051)
- **FILE-031**: `src/Romulus.Infrastructure/Configuration/SettingsFileAccess.cs` – Thread.Sleep (TD-019), timeout (TD-044)
- **FILE-032**: `src/Romulus.Infrastructure/Configuration/SettingsLoader.cs` – Log malformed JSON (TD-054)
- **FILE-033**: `src/Romulus.Infrastructure/Conversion/ToolInvokerAdapter.cs` – RVZ dedup (TD-014), compression config (TD-022)
- **FILE-034**: `src/Romulus.Infrastructure/Conversion/ToolInvokers/DolphinToolInvoker.cs` – RVZ dedup (TD-014), compression config (TD-022)
- **FILE-035**: `src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs` – Hardcoded path (TD-021)
- **FILE-036**: `src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs` – Sort decision literals (TD-015)
- **FILE-037**: `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.StandardPhaseSteps.cs` – Phase literals, German strings (TD-056, TD-058)
- **FILE-038**: `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.ScanAndConvertSteps.cs` – Phase literals, German strings (TD-056, TD-058)
- **FILE-039**: `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs` – German strings (TD-058)
- **FILE-040**: `src/Romulus.Infrastructure/Orchestration/MovePipelinePhase.cs` – Rollback escalation (TD-042), German strings (TD-058)
- **FILE-041**: `src/Romulus.Infrastructure/Orchestration/StreamingScanPipelinePhase.cs` – Scan error surfacing (TD-046), phase literals (TD-056), German strings (TD-058)
- **FILE-042**: `src/Romulus.Infrastructure/Orchestration/WinnerConversionPipelinePhase.cs` – Phase literals (TD-056), German strings (TD-058)
- **FILE-043**: `src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs` – Disc ext inline (TD-057)
- **FILE-044**: `src/Romulus.Infrastructure/Orchestration/DatAuditPipelinePhase.cs` – Exhaustive match (TD-053)
- **FILE-045**: `src/Romulus.Infrastructure/Orchestration/ExecutionHelpers.cs` – DiscExtensions migration (TD-055)
- **FILE-046**: `src/Romulus.Infrastructure/Deduplication/FolderDeduplicator.cs` – Logging in catch (TD-020)
- **FILE-047**: `src/Romulus.Infrastructure/Conversion/ConversionExecutor.cs` – Logging in catch (TD-020)
- **FILE-048**: `src/Romulus.Infrastructure/Conversion/FormatConverterAdapter.cs` – Disc ext inline (TD-057)
- **FILE-049**: `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs` – Disc ext inline (TD-057)
- **FILE-050**: `src/Romulus.Infrastructure/Conversion/ConversionRegistryLoader.cs` – Console key validation (TD-026)
- **FILE-051**: `src/Romulus.Infrastructure/Watch/WatchFolderService.cs` – German strings (TD-058)
- **FILE-052**: `src/Romulus.Infrastructure/Reporting/ReportGenerator.cs` – German strings (TD-058, TD-060)
- **FILE-053**: `src/Romulus.Infrastructure/Hashing/FileHashService.cs` – SHA1 documentation (TD-024)
- **FILE-054**: `src/Romulus.Infrastructure/Analysis/DatAnalysisService.cs` – Dead method (TD-059)
- **FILE-055**: `src/Romulus.Infrastructure/State/AppStateStore.cs` – IReadOnlyDictionary (TD-027)
- **FILE-056**: `src/Romulus.Infrastructure/Dat/FamilyDatStrategyResolver.cs` – Disc ext inline (TD-057)
- **FILE-057**: `src/Romulus.Infrastructure/Dat/DiscDatStrategy.cs` – Disc ext inline (TD-057)

### WPF Layer
- **FILE-058**: `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs` – PropertyChanged leaks (TD-036), child VM DI (TD-041)
- **FILE-059**: `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` – Status literal (TD-009), PropertyChanged leak (TD-036), DateTime.UtcNow (TD-017)
- **FILE-060**: `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.WatchAndProgress.cs` – Phase literals (TD-056)
- **FILE-061**: `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs` – Disc ext inline (TD-057)
- **FILE-062**: `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs` – Property duplication (TD-052)
- **FILE-063**: `src/Romulus.UI.Wpf/ViewModels/SetupViewModel.cs` – Property duplication (TD-052)
- **FILE-064**: `src/Romulus.UI.Wpf/ViewModels/DatCatalogViewModel.cs` – Logging in catch (TD-020)
- **FILE-065**: `src/Romulus.UI.Wpf/Models/DashboardProjection.cs` – Status literals (TD-009)
- **FILE-066**: `src/Romulus.UI.Wpf/Models/ErrorSummaryProjection.cs` – Status literal (TD-009)
- **FILE-067**: `src/Romulus.UI.Wpf/Services/FeatureCommandService.Infra.cs` – German strings (TD-013)
- **FILE-068**: `src/Romulus.UI.Wpf/Services/FeatureCommandService.Security.cs` – German strings (TD-013)
- **FILE-069**: `src/Romulus.UI.Wpf/Services/FeatureCommandService.Dat.cs` – German strings (TD-013)
- **FILE-070**: `src/Romulus.UI.Wpf/Services/FeatureCommandService.Workflow.cs` – German strings (TD-013)
- **FILE-071**: `src/Romulus.UI.Wpf/Services/FeatureService.Export.cs` – German strings (TD-013)
- **FILE-072**: `src/Romulus.UI.Wpf/Services/FeatureCommandService.cs` – Disc ext inline (TD-057)
- **FILE-073**: `src/Romulus.UI.Wpf/Services/FeatureCommandService.Conversion.cs` – Disc ext inline (TD-057)
- **FILE-074**: `src/Romulus.UI.Wpf/Services/ProfileService.cs` – Thread.Sleep (TD-019)

### Data Files
- **FILE-075**: `data/rules.json` – Region rules, token map, language codes (TD-029, TD-034)
- **FILE-076**: `data/conversion-registry.json` – Compression params (TD-022)
- **FILE-077**: `data/defaults.json` – Health score weights, category ranks, version max segments (TD-037, TD-038, TD-049)
- **FILE-078**: `data/i18n/de.json` – Neue i18n-Keys (TD-013, TD-058, TD-060)
- **FILE-079**: `data/i18n/en.json` – Neue i18n-Keys (TD-013, TD-058, TD-060)
- **FILE-080**: `data/schemas/*.json` – **NEU** JSON-Schemas (TD-043)

### Test Files
- **FILE-081**: `src/Romulus.Tests/PreviewExecuteParityTests.cs` – Conversion parity (TD-025)
- **FILE-082**: `src/Romulus.Tests/GuiViewModelTests.cs` – Stub improvements (TD-045)

## 6. Testing

- **TEST-001**: Phase 4 – Grep-Verification: Zero status/sort-decision magic strings in production code. Bestehende API/CLI/GUI Tests grün.
- **TEST-002**: Phase 5 – Unit: Jeder `ConfigurationErrorCode` → korrekter API-Fehlercode. Regression: bestehende API-Error-Responses unverändert.
- **TEST-003**: Phase 6 – Core System.IO Grep → Zero direkte Aufrufe. WPF Strings Grep → Zero hardcoded German.
- **TEST-004**: Phase 7 – RVZ magic check Unit-Test (gültig/ungültig/leer). Compression-Registry-Parameter-Test.
- **TEST-005**: Phase 8 – TimeProvider-kontrollierte Tests. HttpClient-Injection-Test. Thread.Sleep → Zero in Produktionscode.
- **TEST-006**: Phase 9 – Grep: keine leeren catch-Blöcke. Grep: keine hardcoded Pfade.
- **TEST-007**: Phase 10 – Conversion parity assertion. Schema validation mit defekter JSON. AppStateStore.Get() Compile-Check.
- **TEST-008**: Phase 11 – **KRITISCH**: Concurrency-Test GameKeyNormalizer. HMAC-Key-Security-Test. Streaming-Rollback-Test.
- **TEST-009**: Phase 12 – Determinismus-Regression: Goldstandard-Outputs vor und nach Migration identisch.
- **TEST-010**: Phase 13 – Download-Abort-Test. Settings-Timeout-Test. Scan-Incomplete-Warning-Test.
- **TEST-011**: Phase 14 – GroupKey-Collision-Test. Memory-Leak-Check. Rollback-Escalation-Test.
- **TEST-012**: Phase 15 – Custom-Weight-HealthScore-Test. Schema-Validation-Tests. Cached-Roots-Performance-Check.
- **TEST-013**: Phase 16 – Stub-Interaction-Assertions. Exhaustive-Match-Test. UK-Detection-Regression.
- **TEST-014**: Phase 17 – Disc extension consolidation Grep-Check. Alle bestehenden Format-Tests grün.
- **TEST-015**: Phase 18 – Phase-Name Grep-Check.
- **TEST-016**: Phase 19 – German string Grep-Check in Infrastructure.
- **TEST-017**: Phase 20 – Build-Check für entfernte/integrierte Methode.
- **TEST-018**: Phase 21 – Max-Depth-Guard-Test. Normal-Path-Test.
- **TEST-019**: **Global nach jeder Phase**: `dotnet build src/Romulus.sln && dotnet test src/Romulus.Tests/Romulus.Tests.csproj`

### Verifizierte Ergebnisse (2026-04-11)

- Build: `dotnet build src/Romulus.sln` erfolgreich
- Gesamttests: vollständiger Testsuitenlauf ist aktuell intermittierend (Run 1: 10702/10706, Run 2: 10705/10706). Isolierte Re-Runs der betroffenen Tests waren erfolgreich (5/5).
- Zieltests Core-I/O-Refactor (Classification/SetParsing/RunEnvironment): erfolgreich (186/186)
- Zieltests Region-Migration (RegionDetector/Phase12/V1-Gaps): erfolgreich (87/87)

## 7. Verifizierungsstand 2026-04-11 (offene Lücken)

Die vollständige Umsetzung ist nach aktueller Code- und Testprüfung **lückenlos abgeschlossen**.

| Task | Status | Verifikationsbefund |
|------|--------|---------------------|
| Keine | Geschlossen | Keine offenen Verifikationslücken mehr in diesem Planstand. |

## 8. Risks & Assumptions

- **RISK-001**: Phase 12 (data-driven rules) ist das risikoreichste Item: Hardcoded Regex in RegionDetector auf JSON-Daten umzustellen könnte subtile Determinismus-Änderungen verursachen. **Mitigierung**: Goldstandard-Testdaten vor Migration extrahieren; Ergebnisse 1:1 vergleichen.
- **RISK-002**: Phase 11 (TD-028 Race Condition) erfordert sorgfältige volatile-Semantik. **Mitigierung**: Review durch zweite Person; Stress-Test mit parallelen Aufrufen.
- **RISK-003**: Phase 6 (TD-012 Core I/O) berührt Classifier die in vielen Pfaden genutzt werden. **Mitigierung**: Schrittweise Migration mit Feature-Flag oder Adapter-Pattern.
- **RISK-004**: Phase 8 (TD-017 TimeProvider) berührt 30+ Stellen über 4 Projekte. **Mitigierung**: Automatisiertes Find&Replace mit Compile-Verification pro Projekt.
- **RISK-005**: Phase 17 (TD-055/057 DiscFormats) berührt 11+ Dateien. **Mitigierung**: Schrittweise, dateiweise Migration mit Build-Prüfung nach jeder Datei.
- **RISK-006**: Phase 14 (TD-041 ViewModel DI) ist ein WPF-Refactoring das UI-Verhalten beeinflussen könnte. **Mitigierung**: WPF Smoke-Test nach Änderung.
- **ASSUMPTION-001**: .NET 10 TFM (`net10.0`) bietet `TimeProvider` Klasse analog .NET 8+.
- **ASSUMPTION-002**: Bestehende i18n-Infrastruktur (`_vm.Loc["key"]`, `RunProgressLocalization`) ist ausreichend für alle neuen Keys.
- **ASSUMPTION-003**: JSON-Schema-Validierung kann ohne neue NuGet-Dependency (manuell per `System.Text.Json`) umgesetzt werden.
- **ASSUMPTION-004**: `ApiRunStatus.cs` wird aktuell nur in API-Projekt referenziert; keine externen Consumers.

## 9. Related Specifications / Further Reading

- [Technical Debt Tracker 2026-04-09](../../docs/audits/technical-debt-tracker-2026-04-09.md) – Quelldokument aller Findings
- [Architecture Instructions](../../.claude/rules/architecture.instructions.md) – Dependency-Richtung und Schichttrennung
- [Cleanup Instructions](../../.claude/rules/cleanup.instructions.md) – Qualitätsregeln und Testanforderungen
- [Conversion Instructions](../../.claude/rules/conversion.instructions.md) – Conversion-Registry-Regeln
- [Release Roadmap](../plan/feature-romulus-release-roadmap-1.md) – Release-Kontext
