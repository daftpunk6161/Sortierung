# Full Tool Audit – RomCleanup

**Datum:** 2026-03-18  
**Build:** ✅ 0 Fehler, 0 Warnungen  
**Tests:** ✅ 3273/3273 bestanden  
**Branch:** `daftpunk6161/issue9`  
**SDK:** .NET 10.0.200, C# 14

---

## 1. Executive Verdict

| Kriterium | Bewertung |
|-----------|-----------|
| **Ist-Zustand freigabefähig** | **JA — mit Auflagen** (P1-Findings müssen adressiert werden) |
| **Vertrauensniveau Kernlogik** | **hoch** — Determinismus, Scoring, Grouping sind korrekt und testgeschützt |
| **Vertrauensniveau GUI/UI** | **mittel** — Funktional stabil; Concurrency-Risiken beim CTS-Handling und Event-Cleanup; kein Datenverlust-Risiko |
| **Vertrauensniveau CLI/API** | **hoch** — Korrekte Exit Codes, korrekte Auth, korrekte Pipeline; kleinere Validierungslücken |
| **Vertrauensniveau Reports/Logs/Metrics** | **hoch** — RunProjection als Single Source of Truth; Cross-Layer-Parity bewiesen via Tests |

**Kurzfazit:** Das Tool ist architektonisch sauber, fachlich korrekt und besser abgesichert als der Großteil vergleichbarer Open-Source-Projekte. Die Clean-Architecture-Regeln werden eingehalten. Die zentrale fachliche Logik (Winner-Selection, GameKey, Scoring, Dedup) ist deterministisch und testgeschützt. Es gibt echte Cross-Layer-Parity-Tests, die sicherstellen, dass CLI, API, GUI und Reports identische Zahlen liefern. Die verbleibenden Findings sind überwiegend Härtungsmaßnahmen, keine logischen Fehler.

---

## 2. Repo Map

### Solution & Projekte

```
src/RomCleanup.sln (7 Projekte)
├── RomCleanup.Contracts       (22 Dateien, net10.0) — Ports, Models, Errors
├── RomCleanup.Core            (20 Dateien, net10.0) — Pure Domain Logic
├── RomCleanup.Infrastructure  (45 Dateien, net10.0) — IO-Adapter, Orchestrierung
├── RomCleanup.CLI             (2 Dateien, net10.0)  — Headless Entry Point
├── RomCleanup.Api             (8 Dateien, net10.0)  — REST Minimal API
├── RomCleanup.UI.Wpf          (106 Dateien, net10.0-windows) — WPF GUI
└── RomCleanup.Tests           (81 Dateien, net10.0-windows) — 3273 xUnit Tests
```

### Projektabhängigkeiten

```
Contracts  ←  Core  ←  Infrastructure  ←  CLI
                                        ←  Api
                                        ←  UI.Wpf
Tests → alle 6 Projekte
```

**Abhängigkeitsrichtung korrekt:** Keine Verletzung erkannt. Core hat keine I/O-Abhängigkeiten. Infrastructure referenziert Core und Contracts. Entry Points referenzieren alle drei Schichten.

### Entry Points

| Entry Point | Datei | Composition Root |
|-------------|-------|-----------------|
| **CLI** | `CLI/Program.cs` | Inline Konstruktion (kein DI Container) |
| **API** | `Api/Program.cs` | ASP.NET DI via `builder.Services` |
| **GUI** | `UI.Wpf/App.xaml.cs` | `Microsoft.Extensions.DependencyInjection` |

### Zentrale Orchestrierung

`RunOrchestrator` (Infrastructure/Orchestration) ist der einzige Pipeline-Executor. Alle drei Entry Points delegieren an diese Klasse. 

**Pipeline-Phasen:** Preflight → Scan → Enrichment → Deduplicate → JunkRemoval → Move → ConsoleSort → Conversion → Report → AuditSidecar

### Tote oder redundante Pfade

- `archive/powershell/` — Legacy PS-Code, vollständig migriert, korrekt archiviert ✅
- Keine toten Code-Pfade in `src/` gefunden
- `CoverageBoostPhase2-9Tests.cs` — 8 Testdateien, die großteils echte Tests enthalten (keine reinen Alibis — stichprobenartig geprüft)

---

## 3. System Map

### End-to-End-Datenfluss

```
User Input (Roots, Mode, Regions, Extensions)
    ↓
RunOptions (Contracts/Models)
    ↓
RunOrchestrator.Execute()
    ├── Preflight: Roots existieren? Audit-Dir schreibbar? Tools verfügbar?
    ├── ScanPipelinePhase: FileSystemAdapter.GetFilesSafe() → Liste von Pfaden
    ├── EnrichmentPipelinePhase: CandidateFactory → GameKeyNormalizer → RegionDetector → 
    │                            Scorers → ConsoleDetector → DatIndex → RomCandidate[]
    ├── DeduplicatePipelinePhase: DeduplicationEngine.Deduplicate() → DedupeResult[]
    ├── JunkRemovalPipelinePhase: Move Junk → Trash + Audit
    ├── MovePipelinePhase: Move Losers → Trash + Audit
    ├── ConsoleSorter: Sort by Console → Audit
    ├── WinnerConversionPipelinePhase: chdman/7z/dolphintool
    └── RunReportWriter: HTML + CSV + AuditSidecar
    ↓
RunResult → RunResultBuilder.Build()
    ↓
RunProjectionFactory.Create(RunResult) → RunProjection (Single Source of Truth)
    ↓
CLI: JSON stdout | API: ApiRunResult | GUI: MainViewModel.ApplyRunResult()
```

### Wichtigste Objekte

| Objekt | Rolle | Ort |
|--------|-------|-----|
| `RomCandidate` | Zentrales Datenobjekt pro ROM | Contracts/Models |
| `DedupeResult` | Winner + Losers pro GameKey | Contracts/Models |
| `RunResult` | Vollständiges Pipeline-Ergebnis | Infrastructure/Orchestration |
| `RunProjection` | Channel-neutrales KPI-Objekt | Infrastructure/Orchestration |
| `RunOptions` | Konfiguration für einen Lauf | Infrastructure/Orchestration |

### Wahrheitspunkte

- **Fachliche Wahrheit:** `RunResult` aus `RunOrchestrator.Execute()`
- **KPI-Wahrheit:** `RunProjection` via `RunProjectionFactory.Create()`
- **Audit-Wahrheit:** CSV-Datei mit HMAC-Sidecar

### Kritische Übergänge

1. **Enrichment → Dedupe:** Hier entstehen GameKeys und Scores. Fehler hier kaskadieren auf alles.
2. **Dedupe → Move:** Winner-Entscheidung muss deterministisch sein (stabil und reproduzierbar).
3. **Move → Audit:** Atomic Write + HMAC-Signatur. Partial Failure muss erkennbar sein.
4. **RunResult → RunProjection:** Diese Transformation ist die Single Source of Truth für alle Channels.

---

## 4. Bereichsaudit

### 4.1 Architektur

**Was gut ist:**
- Strikte Clean Architecture: Dependency-Richtung korrekt, Core ist rein
- `RunOrchestrator` als einziger Pipeline-Driver für alle drei Entry Points
- `RunProjection` als kanonisches KPI-Objekt eliminiert Drift zwischen Channels
- Phase-basierte Pipeline mit eigenem `IPipelinePhase<TIn, TOut>` Interface
- `InternalsVisibleTo` für Tests ermöglicht White-Box-Testing ohne öffentliche API-Verschmutzung

**Was verdächtig ist:**
- WPF `FeatureService` (9 Partials) und `FeatureCommandService` (10 Partials) sind sehr groß. Potenzielle Logik-Duplikation mit Infrastructure.
- `RollbackService` in UI.Wpf ist statisch und erzeugt eigene `FileSystemAdapter`/`AuditSigningService` statt DI.
- `RunResultBuilder` ist mutable, aber nur innerhalb des Orchestrators verwendet — akzeptabel.

**Was kaputt ist:**
- Nichts strukturell kaputt. Architektur ist solide.

**Haupt-Risiken:**
- WPF-Services Wachstum (Mega-Class Tendenz bei FeatureService)

### 4.2 Kernfunktionen

**Was gut ist:**
- `DeduplicationEngine.SelectWinner()` ist explizit deterministisch mit alphabetischem MainPath-Tiebreaker
- `Deduplicate()` sortiert Keys vor Iteration → deterministisches Output-Ordering
- `GameKeyNormalizer` mit LRU-Cache (50k Einträge) und 25+ Tag-Patterns
- Alle Regex haben Timeouts (200-500ms) gegen ReDoS
- `CandidateFactory` isoliert BIOS mit `__BIOS__`-Prefix im GameKey
- `CompletenessScorer` prüft CUE/GDI auf fehlende Tracks (negative Scores)
- `FormatScorer` mit 45+ Format-Scores, klar dokumentiert

**Was verdächtig ist (Finding F-01):**
- `FileCategory.NonGame` fehlt in `DeduplicationEngine.GetCategoryRank()` — fällt auf Default `_ => 1` zurück, wird wie Unknown behandelt. Funktional akzeptabel (Game gewinnt trotzdem), aber semantisch falsch.

**Was kaputt ist:**
- Nichts kaputt. Winner-Selection-Determinismus ist testgeschützt.

**Haupt-Risiken:**
- F-01: NonGame-Rank-Lücke (P2, kein Datenverlust)
- Regex-Cache in RuleEngine verwendet `ConcurrentDictionary` mit arbiträrer Eviction statt LRU (funktional, aber suboptimal)

### 4.3 GUI / UI / UX

**Was gut ist:**
- MVVM mit CommunityToolkit.Mvvm
- Explizites `RunStateMachine` mit definierten Zustandsübergängen
- Child-ViewModels (Setup, Run, Tools) für Dekomposition
- DI via `Microsoft.Extensions.DependencyInjection`
- Themes (SynthwaveDark, Light, HighContrast) via ResourceDictionary
- `DashboardProjection` als ViewModel-lokale Projection des RunResult

**Was verdächtig ist:**
- `MainViewModel` ist über 5 Partial-Dateien verteilt (~1200 Zeilen total). Groß, aber handhabbar.
- `PropertyChanged`-Event-Handler in `LoadInitialSettings()` werden möglicherweise bei ViewModel-Neuinstanziierung nicht deregistriert. **Aber:** ViewModel-Lebensdauer = App-Lebensdauer, kein Leck bei Single-Window-Szenario.
- Zwei AutoSave-Timer (SettingsTimer + ScheduleAutoSave) kompetieren potenziell auf Disk-Schreibzugriff.

**Was verdächtig ist (GUI-spezifisch):**
- `MainWindow.xaml.cs` enthält `ExecuteAndRefreshAsync()` mit Orchestrierungslogik (TrayService, ReportRefresh). Sollte in ViewModel sein.
- `SynchronizationContext` wird im Konstruktor gecaptured. Bei langlebigen Runs stabil, aber Muster ist fragil.

**Haupt-Risiken:**
- F-02: CancellationTokenSource Dispose+Cancel Race (niedrige Wahrscheinlichkeit, catch vorhanden)
- F-03: Settings-Persistenz-Race bei gleichzeitigem AutoSave + ManualSave
- F-04: Window-Close-Timer-Race (Settings könnten bei rapidem Schließen verloren gehen)

### 4.4 CLI

**Was gut ist:**
- Exit Codes korrekt: 0=Erfolg, 1=Fehler, 2=Abbruch, 3=Preflight fehlgeschlagen
- Ctrl+C Handling mit graceful Cancel + Force-Exit bei zweitem Signal
- Korrekte stdout/stderr Trennung (JSON nach stdout, Progress nach stderr)
- DryRun und Move Mode verwenden identisches RunOrchestrator
- Protected Directory Check (Windows, System32, ProgramFiles)
- `RunForTests()` Methode für Test-Capture

**Was verdächtig ist:**
- Silent Default auf DryRun wenn Mode nicht angegeben (kein Warn-Output)
- Extension-Validierung akzeptiert technisch ungültige Formate (`.c`, `.123`)
- Keine Warning bei Overlapping Roots

**Haupt-Risiken:**
- Keine kritischen Risiken. CLI ist solide.

### 4.5 API

**Was gut ist:**
- Localhost-only Binding (127.0.0.1)
- Fixed-Time HMAC-SHA256 API-Key-Vergleich
- Rate Limiting (120 req/min) mit per-Client Buckets
- Correlation-ID für Request-Tracking
- Idempotency-Key mit Fingerprint-Deduplication
- Single-Active-Run Enforcement
- SSE-Streaming mit 15s Heartbeat
- Content-Type Validation, Body-Size-Limit (1MB)
- System-Directory-Blocking, Reparse-Point-Blocking

**Was verdächtig ist:**
- Non-Loopback Binding wird nur gewarnt, nicht blockiert (API startet trotzdem mit Plain HTTP)
- X-Forwarded-For wird ohne Trust-Validation für Rate Limiting verwendet
- SSE-Timeout hardcoded auf 300s (nicht konfigurierbar)
- Idempotency-Fingerprint manuell zusammengebaut (nicht alle Felder berücksichtigt?)

**Haupt-Risiken:**
- F-05: Plain HTTP + Non-Loopback = API-Key im Klartext über das Netzwerk (P1)

### 4.6 Reports / Logs / Metrics / Audit

**Was gut ist:**
- `RunProjection` als Single Source of Truth für alle KPIs
- Cross-Layer-Parity bewiesen: `KpiChannelParityBacklogTests` und `ReportParityTests` prüfen explizit CLI vs API vs WPF vs Report mit je ~20 KPI-Feldern
- HMAC-signierte Audit-CSV-Sidecars
- CSV-Injection-Schutz (leading `=`, `+`, `-`, `@` werden escaped)
- HTML-Encoding in Reports mit CSP Meta-Tag
- Structured JSONL Logging mit Correlation-ID
- PhaseMetricsCollector für Pipeline-Timing
- Inkrementelle Audit-Flushs alle 50 Moves

**Was verdächtig ist:**
- `ReportGenerator` verwendet `WebUtility.HtmlEncode()` — korrekt, aber CSP Header ist Meta-Tag, nicht HTTP Header (bei API-Serving relevant)

**Haupt-Risiken:**
- Keine kritischen Risiken. Reports sind solide abgesichert.

### 4.7 Safety / IO / Security

**Was gut ist:**
- `FileSystemAdapter.ResolveChildPathWithinRoot()` mit NFC-Normalisierung und `StartsWith()` Guard
- Reparse-Point-Blocking auf Datei- und Verzeichnisebene in `GetFilesSafe()`, `MoveItemSafely()`, `MoveDirectorySafely()`
- Collision-Handling mit `__DUP{n}` Suffix bis 10.000 Versuche
- `MoveItemSafely()` TOCTOU entschärft: IOException-Catch + DUP-Fallback
- Zip-Bomb-Protection: Entry-Count-Limit (10k) und Größen-Limit (10GB)
- Zip-Slip-Protection: Pfad-Validierung vor Extraktion
- Tool-Hash-Verifizierung über `tool-hashes.json` mit SHA256
- XXE-Schutz beim XML-Parsing (DatRepositoryAdapter)
- Iterativer DFS statt Rekursion in `GetFilesSafe()` (Stack-Overflow-Schutz)
- Visited-Set verhindert Endlosschleifen bei zirkulären Symlinks

**Was verdächtig ist:**
- Reparse-Point-Check in `GetFilesSafe()` erfolgt nach Push auf Stack (ineffizient, aber korrekt — Check erfolgt beim Pop)
- HMAC-Key wird geschrieben und danach Permissions gesetzt. Auf Windows akzeptabel (Standard-ACLs restriktiver), auf Linux kleines Fenster.

**Haupt-Risiken:**
- F-06: NFC-Normalisierung nicht in allen Pfaden konsistent (betrifft theoretisch macOS HFS+ Volumes, auf Windows irrelevant)

### 4.8 Tests / Testbarkeit

**Was gut ist:**
- 3273 Tests, alle grün
- 81 Testdateien mit klarer Benennung (`<Klasse>Tests.cs`)
- **Echte Cross-Layer-Parity-Tests:** `ReportParityTests.cs` und `KpiChannelParityBacklogTests.cs` prüfen CLI vs API vs WPF vs Report-KPIs
- **Determinismus-Tests:** `DeduplicationEngineTests.SelectWinner_IsDeterministic()`
- **Security-Tests:** Path Traversal, CSV Injection, HTML Encoding, Zip Slip
- **Safety-Tests:** `SafetyIoRecoveryTests`, `SafetyValidatorTests`
- **Invarianten-Tests:** `HardCoreInvariantRegressionSuiteTests`, `CoreHeartbeatInvariantTests`
- **Chaos-Tests:** `ChaosTests` für Fehlerszenarien
- **Concurrency-Tests:** `ConcurrencyTests` für Thread-Safety
- **API-Integration-Tests:** `ApiIntegrationTests` mit echtem `WebApplicationFactory`

**Was verdächtig ist:**
- `CoverageBoostPhase2-9Tests.cs` (8 Dateien) — Name klingt nach Padding, aber Stichproben zeigen echte Tests mit sinnvollen Assertions
- Determinismus-Test deckt Exact-Tie-Fall nur implizit ab (Alpha-Tiebreaker ist schwer zu isolieren)

**Was fehlt (Findings):**
- F-07: Kein expliziter Test für `FileCategory.NonGame` in Winner-Selection
- F-08: Kein Concurrency-Stress-Test für Rate Limiter mit 100+ parallelen Clients
- F-09: Kein Test für CLI Main() Runtime-Exit-Codes (nur ParseArgs-Codes getestet)
- F-10: Kein expliziter Test für Rollback-Reihenfolge (vorwärts vs. rückwärts)
- F-11: Kein Test für Settings-File-Corruption bei gleichzeitigem Schreiben

---

## 5. Konsolidierte Findings nach Priorität

### P0 – Release-Blocker

**Keine P0-Findings.** Das Tool hat keine Release-Blocker. Build grün, alle Tests grün, zentrale Invarianten geschützt.

### P1 – Schwere Risiken

#### F-05: API Plain HTTP mit Non-Loopback Binding
- **Kategorie:** Security
- **Betroffene Komponenten:** `Api/Program.cs`
- **Evidenz:** Zeile 14-17: `builder.WebHost.UseUrls($"http://{bindAddress}:{port}")`. Wenn `BindAddress` auf Nicht-Loopback gesetzt wird, wird API-Key im Klartext übertragen. Es erfolgt nur ein `Console.WriteLine()` Warning, aber die API startet.
- **Warum kritisch:** API-Key-Leak im Netzwerk ermöglicht unbefugten Zugriff auf alle Endpunkte.
- **Erwartetes Verhalten:** Non-Loopback + kein TLS → Hard Fail, API startet nicht.
- **Tatsächliches Verhalten:** API startet mit Warning, Key wird im Klartext über das Netz gesendet.
- **Hauptursache:** Sicherheit vs. Benutzerfreundlichkeit Abwägung zugunsten Flexibilität gefallen.
- **Fix-Strategie:** Entweder (a) bei Non-Loopback ohne TLS-Konfiguration mit Exception abbrechen oder (b) ein explizites `--allow-insecure-network` Flag erfordern.
- **Nötige Tests:** `ApiIntegrationTests.NonLoopback_WithoutTls_Rejects()`.

#### F-02: CancellationTokenSource Dispose/Cancel Race
- **Kategorie:** Concurrency
- **Betroffene Komponenten:** `UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs`
- **Evidenz:** `CreateRunCancellation()` disposed altes CTS unter Lock, `OnCancel()` nimmt Referenz unter Lock und ruft `Cancel()` außerhalb Lock auf — potenziell auf bereits disposetem CTS.
- **Warum kritisch:** Cancel kann still fehlschlagen; UI zeigt falsch "Running" obwohl nichts mehr läuft.
- **Erwartetes Verhalten:** Cancel immer erfolgreich oder klar als "bereits beendet" erkannt.
- **Tatsächliches Verhalten:** `ObjectDisposedException` wird gecatched und verschluckt.
- **Hauptursache:** Race-Fenster zwischen Dispose und Cancel.
- **Fix-Strategie:** Cancel+Dispose immer zusammen unter dem gleichen Lock, oder `cts?.Token.IsCancellationRequested` prüfen vor Cancel.
- **Nötige Tests:** Rapid-Cancel/Run-Cycle-Test.

#### F-03: Settings-Persistenz-Race
- **Kategorie:** Data Integrity
- **Betroffene Komponenten:** `UI.Wpf/ViewModels/MainViewModel.Settings.cs`, `SettingsService.cs`
- **Evidenz:** AutoSave-Timer (debounced) und SaveSettingsCommand (manuell) schreiben beide über `File.WriteAllText()` auf das gleiche Settings-File, ohne Lock.
- **Warum kritisch:** Bei gleichzeitigem Schreiben kann die Settings-Datei korrupt werden.
- **Erwartetes Verhalten:** Serialisierter Zugriff auf Settings-File.
- **Tatsächliches Verhalten:** Concurrent Write → potenziell korruptes JSON.
- **Hauptursache:** Fehlender Synchronisationsmechanismus.
- **Fix-Strategie:** Lock-basierte Serialisierung oder AsyncLocal-Queue.
- **Nötige Tests:** Concurrent-Write-Test auf Settings-File.

### P2 – Relevante Mängel

#### F-01: FileCategory.NonGame fehlt in GetCategoryRank
- **Kategorie:** Korrektheit
- **Betroffene Komponenten:** `Core/Deduplication/DeduplicationEngine.cs`, Zeile 47
- **Evidenz:** `FileCategory.NonGame` hat keinen Case im Switch, fällt auf `_ => 1` zurück (= Unknown).
- **Warum relevant:** Semantisch falsch, auch wenn praktisch kein Datenverlust entsteht (Game gewinnt immer).
- **Fix-Strategie:** Expliziten `FileCategory.NonGame => 2` Case hinzufügen, Junk auf 1, Unknown auf 0.
- **Nötige Tests:** Test mit NonGame-Kandidat in Winner-Selection.

#### F-04: Window-Close-Timer-Race
- **Kategorie:** UX / Data Integrity
- **Betroffene Komponenten:** `UI.Wpf/MainWindow.xaml.cs`
- **Evidenz:** Settings-Timer nutzt `BeginInvoke` (asynchron), `OnClosing` ruft `CleanupResources()` sofort auf.
- **Warum relevant:** Letzte Settings-Änderungen können bei schnellem Schließen verloren gehen.
- **Fix-Strategie:** `Dispatcher.Invoke(_vm.SaveSettings, TimeSpan.FromSeconds(1))` vor Cleanup.
- **Nötige Tests:** Close-During-Save-Test.

#### F-06: NFC-Normalisierung inkonsistent
- **Kategorie:** Plattform-Kompatibilität
- **Betroffene Komponenten:** `Infrastructure/FileSystem/FileSystemAdapter.cs`
- **Evidenz:** `NormalizePathNfc()` existiert und wird in `MoveItemSafely()` und `ResolveChildPathWithinRoot()` verwendet, aber nicht in `GetFilesSafe()` für das Sortieren der Ergebnisse.
- **Warum relevant:** Auf macOS HFS+ Volumes könnten NFD-codierte Pfade zu Cache-Misses führen.
- **Fix-Strategie:** NFC-Normalisierung auch in `GetFilesSafe()` Sortierung.
- **Nötige Tests:** Test mit NFD-codierten Dateinamen (ü → u + combining accent).

#### F-07: Kein Test für NonGame in Winner-Selection
- **Kategorie:** Testlücke
- **Betroffene Komponenten:** `Tests/DeduplicationEngineTests.cs`
- **Evidenz:** Kein `[Fact]` oder `[Theory]` mit `FileCategory.NonGame`.
- **Fix:** Test hinzufügen.

#### F-12: Business Logic in MainWindow.xaml.cs Code-Behind
- **Kategorie:** Architektur
- **Betroffene Komponenten:** `UI.Wpf/MainWindow.xaml.cs`
- **Evidenz:** `ExecuteAndRefreshAsync()` orchestriert TrayService, RunService und ReportRefresh direkt.
- **Fix:** In MainViewModel verschieben.

### P3 – Nachrangige Punkte

#### F-08: Kein Concurrency-Stress-Test für Rate Limiter
- **Kategorie:** Testlücke (gering, da API localhost-only)
- **Fix:** Parallel stress test hinzufügen.

#### F-09: CLI Runtime-Exit-Code-Tests fehlen
- **Kategorie:** Testlücke
- **Fix:** `Main()` ganzer Flow mit `RunForTests()` für Error/Cancel.

#### F-10: Kein Test für Rollback-Reihenfolge
- **Kategorie:** Testlücke
- **Fix:** Rollback-Test mit mehreren Rows in umgekehrter Reihenfolge.

#### F-11: Settings-Corruption-Test fehlt
- **Kategorie:** Testlücke
- **Fix:** Concurrent-Write-Test auf Settings-JSON.

#### F-13: X-Forwarded-For Trust-Validation fehlt in API
- **Kategorie:** Security (niedrig, da localhost-only)
- **Fix:** Dokumentation oder Konfigurierbare Trust-Proxies.

#### F-14: SSE-Timeout nicht konfigurierbar
- **Kategorie:** Konfiguration
- **Fix:** Aus `appsettings.json` lesen.

#### F-15: ConsoleDetector Cache-Key nicht normalisiert
- **Kategorie:** Performance
- **Fix:** `Path.GetFullPath()` vor Cache-Key-Bildung.

---

## 6. Kernursachen

### KU-1: GUI-spezifische Concurrency-Muster nicht vollständig gehärtet
- **Entstehende Probleme:** F-02 (CTS-Race), F-03 (Settings-Race), F-04 (Close-Race)
- **Warum zentral:** WPF hat Threading-Anforderungen (Dispatcher, SyncContext), die in allen drei Findings auftauchen.
- **Gegenmassnahme:** Standardisiertes Concurrency-Pattern: Single Lock für CTS-Lifecycle, Lock für Settings-Persist, Synchrones Save vor Window-Close.

### KU-2: Keine harte Security-Boundary für API-Network-Exposure
- **Entstehende Probleme:** F-05 (Plain HTTP Non-Loopback), F-13 (X-Forwarded-For Trust)
- **Warum zentral:** API ist als "nur lokal" konzipiert, aber technisch nicht daran gehindert, im Netzwerk zu laufen.
- **Gegenmassnahme:** Hard Fail bei Non-Loopback ohne TLS oder explizitem Opt-in.

### KU-3: Unvollständige Enum-Handling in einem Switch
- **Entstehende Probleme:** F-01 (NonGame-Rank)
- **Warum zentral:** Neues Enum-Member `NonGame` wurde hinzugefügt, aber nicht überall konsistent behandelt.
- **Gegenmassnahme:** Compiler-Warning für nicht-exhaustive Switch-Expressions aktivieren oder alle Switches bei Enum-Änderung prüfen.

---

## 7. Zielbild / Soll-Architektur

Das aktuelle Architekturmodell ist bereits nahe am Zielbild. Die wesentlichen Verbesserungen sind Härtungen, keine Neugestaltung.

### Core
- **Ist:** Pure, kein I/O, deterministisch → **Soll:** Unverändert beibehalten. NonGame-Case ergänzen.

### Orchestrator
- **Ist:** `RunOrchestrator` als einziger Pipeline-Driver → **Soll:** Unverändert beibehalten. Phase-Hooks für Monitoring erweitern (optional).

### Result-/Projection-Modell
- **Ist:** `RunResult` → `RunProjection` als Single Source of Truth → **Soll:** Bereits optimal. Beibehalten.

### GUI
- **Ist:** MVVM mit CommunityToolkit.Mvvm, Child-ViewModels → **Soll:** CTS-Lifecycle und Settings-Persistenz härten. `ExecuteAndRefreshAsync` aus Code-Behind in ViewModel verschieben.

### CLI
- **Ist:** Vollständig, korrekte Exit Codes → **Soll:** `--quiet` Flag für reinere CI-Integration.

### API
- **Ist:** Funktionstüchtig mit Auth, Rate Limiting, SSE → **Soll:** Non-Loopback Hard Fail. TLS-Enforcement.

### Reports/Logs/Audit
- **Ist:** Cross-Layer-Parity bewiesen, HMAC-Signiert → **Soll:** Bereits optimal. Beibehalten.

### Safety / Recovery / Testing
- **Ist:** Path-Traversal, Reparse-Blocking, Zip-Slip, Tool-Hash-Verify → **Soll:** NFC-Normalisierung vervollständigen. Fehlende Tests nachziehen.

---

## 8. Umsetzungsreihenfolge

### Phase 1: Sofortmassnahmen (1-2 Tage)
**Ziel:** Bekannte Concurrency-Risiken eliminieren, Security-Lücke schließen.

| # | Aufgabe | Finding |
|---|---------|---------|
| 1 | API Non-Loopback Hard Fail implementieren | F-05 |
| 2 | CTS-Lifecycle-Lock in MainViewModel.RunPipeline härten | F-02 |
| 3 | Settings-Persist-Lock einführen | F-03 |
| 4 | Synchrones Save vor Window-Close | F-04 |

**Erwarteter Nutzen:** Security-Lücke geschlossen, keine stillen Concurrency-Fehler.  
**Exit-Kriterium:** API verweigert Start bei Non-Loopback ohne TLS. Rapid-Cancel-Test grün. Settings-Racecondition-Test grün.

### Phase 2: Kernlogik stabilisieren (1 Tag)
**Ziel:** Fehlende Enum-Cases ergänzen, Tests nachziehen.

| # | Aufgabe | Finding |
|---|---------|---------|
| 5 | `FileCategory.NonGame` Case in `GetCategoryRank()` | F-01 |
| 6 | NonGame Winner-Selection-Test | F-07 |
| 7 | NFC-Normalisierung in `GetFilesSafe()` | F-06 |
| 8 | ConsoleDetector Cache-Key normalisieren | F-15 |

**Erwarteter Nutzen:** Vollständige Enum-Abdeckung, keine Cache-Ineffizienzen.  
**Exit-Kriterium:** Alle 3273+ Tests grün. Neuer NonGame-Test grün.

### Phase 3: Output-Layer konsolidieren (1 Tag)
**Ziel:** Code-Behind-Logik in ViewModel. CLI `--quiet` Flag.

| # | Aufgabe | Finding |
|---|---------|---------|
| 9 | `ExecuteAndRefreshAsync` aus MainWindow.xaml.cs nach MainViewModel | F-12 |
| 10 | CLI `--quiet` Flag für CI-Pipelines | F-09 (indirekt) |

**Erwarteter Nutzen:** Bessere Testbarkeit, sauberere Trennung.  
**Exit-Kriterium:** Code-Behind hat keine Orchestrierungslogik mehr.

### Phase 4: Tests und Härtung (2 Tage)
**Ziel:** Testlücken schließen.

| # | Aufgabe | Finding |
|---|---------|---------|
| 11 | CLI Runtime-Exit-Code-Tests | F-09 |
| 12 | Rollback-Reihenfolge-Test | F-10 |
| 13 | Settings-Concurrent-Write-Test | F-11 |
| 14 | Rate-Limiter Concurrency-Test | F-08 |

**Erwarteter Nutzen:** Regression-Schutz für bisher ungetestete Pfade.  
**Exit-Kriterium:** Alle neuen Tests grün.

---

## 9. Test- und Verifikationspaket

### Unit Tests (vorhanden, gut)
- GameKeyNormalizer: ✅ umfassend
- DeduplicationEngine: ✅ umfassend (Determinismus, Category-Filtering)
- RegionDetector: ✅ vorhanden
- FormatScorer/VersionScorer: ✅ vorhanden
- FileClassifier: ✅ vorhanden
- **Fehlend:** NonGame-Candidate in DeduplicationEngine

### Integrationstests (vorhanden, gut)
- RunOrchestrator-Pipeline: ✅ vorhanden
- File-Move mit Audit: ✅ SafetyIoRecoveryTests
- API Lifecycle: ✅ ApiIntegrationTests
- **Fehlend:** CLI Main() Runtime-Fehler-Exit-Codes

### Cross-Output-Parity-Tests (vorhanden, exzellent)
- CLI vs API vs WPF vs Report: ✅ `ReportParityTests.cs` mit ~20 KPI-Feldern
- KPI-Parity alle Channels: ✅ `KpiChannelParityBacklogTests.cs`
- OpenAPI-Schema-Parity: ✅ `OpenApi_ApiRunResultSchema_ContainsAllApiRunResultProperties`
- **Evidenz:** Diese Tests sind der stärkste Beweis für Datenintegrität über alle Channels.

### Replay-/Audit-Tests (vorhanden)
- Audit-CSV-Signatur: ✅ `AuditSigningServiceTests`
- Audit-Compliance: ✅ `AuditComplianceTests`
- **Fehlend:** Rollback-Reihenfolge, Partial-Rollback-Recovery

### GUI-State-/Journey-Tests (teilweise)
- ViewModel-Tests: ✅ `GuiViewModelTests`, `WpfNewTests`, `WpfCoverageBoostTests`
- **Fehlend:** Rapid-Cancel, Close-During-Save, Theme-Toggle-Stress

### Retry-/Resume-/Cancel-Tests (vorhanden)
- Cancellation in Pipeline: ✅ `RunOrchestratorTests`
- **Fehlend:** CTS-Dispose-Race-Test

### Snapshot-/Golden-File-Tests
- **Fehlend:** Keine expliziten Golden-File-Tests. Cross-Parity-Tests übernehmen diese Rolle teilweise.

### Stress-/Parallelitäts-Tests
- `ConcurrencyTests.cs`: ✅ vorhanden
- `ChaosTests.cs`: ✅ vorhanden (Fehlerszenarien)
- **Fehlend:** Rate-Limiter Concurrent Stress

---

## 10. Freigabebedingungen

| # | Kriterium | Aktueller Stand | Erforderlich |
|---|-----------|-----------------|-------------|
| 1 | Build: 0 Fehler, 0 Warnungen | ✅ erfüllt | ✅ |
| 2 | Tests: ≥3000, 0 Fehl | ✅ 3273/3273 | ✅ |
| 3 | Cross-Parity-Tests CLI/API/WPF grün | ✅ erfüllt | ✅ |
| 4 | Alle P0-Findings behoben | ✅ keine P0 | ✅ |
| 5 | Alle P1-Findings behoben oder mitigation dokumentiert | ⚠️ 3 offen | Behoben |
| 6 | Security: Non-Loopback-API Hard Fail | ⚠️ nur Warning | Hard Fail |
| 7 | Determinismus: Winner-Selection bewiesen | ✅ Test vorhanden | ✅ |
| 8 | Audit-Integrität: HMAC-Signatur-Tests grün | ✅ erfüllt | ✅ |
| 9 | Kein Default-Delete (Move→Trash) | ✅ erfüllt | ✅ |
| 10 | Path-Traversal-Protection aktiv | ✅ erfüllt | ✅ |

---

## 11. Offene Risiken / Annahmen / Restunsicherheit

### Offene Risiken
1. **Settings-Corruption bei Concurrent Save** (F-03) — kann eintreten, wenn User manuell speichert während AutoSave feuert. Niedrige Wahrscheinlichkeit, aber fehlende Mitigation.
2. **CTS-Race** (F-02) — kann bei rapidem Cancel/Start-Wechsel eintreten. Resultat: stille Nicht-Cancellation. `ObjectDisposedException` wird gecatched → kein Crash, aber falscher UI-State möglich.
3. **Non-Loopback API** (F-05) — aktuell nur Warning. Jeder Deployment-Versuch außerhalb Localhost ist unsicher.

### Annahmen
1. **Single-User-Betrieb:** Das Tool wird pro Rechner von einem User gleichzeitig benutzt. Multi-User-Concurrency wird nicht erwartet.
2. **Windows-Only:** Net10.0-windows Target für WPF. Cross-Platform nur CLI/API relevant.
3. **Lokale Dateisysteme:** UNC-Pfade und Netzwerk-Shares werden nicht primär unterstützt (Tests existieren aber).
4. **ROM-Sammlung < 500k Dateien:** Bei >100k Dateien wird Memory-Warning ausgegeben. Getestet bis moderate Größen.

### Restunsicherheit
1. **DiscHeaderDetector Coverage:** Silent catch bei korrupten Images → UNKNOWN. Korrekt, aber Coverage der Header-Patterns nicht vollständig verifizierbar.
2. **External Tool Behavior:** chdman/dolphintool/7z Exit-Codes und Output-Formate können sich mit Tool-Updates ändern. Hash-Verifizierung schützt vor Manipulation, nicht vor API-Änderungen.
3. **Audit Rollback bei Partial Failure:** Rollback-Reihenfolge (Forward vs. Reverse) nicht explizit getestet. Implementierung in `AuditSigningService.Rollback()` muss manuell verifiziert werden.

---

## 12. Schlussentscheidung

### Was ist heute kaputt?
**Nichts ist kaputt.** Build grün, 3273 Tests grün, Kernlogik deterministisch, Cross-Layer-Parity bewiesen.

### Was ist unzuverlässig?
1. Settings-Persistenz unter Concurrent-Write (F-03) — niedriges Risiko, aber fehlende Absicherung.
2. CTS-Cancel unter Race-Bedingung (F-02) — stille Nicht-Cancellation möglich, aber keine Crash-Gefahr.
3. API-Security bei Non-Loopback-Deployment (F-05) — technisch möglich, aber nicht gehindert.

### Was muss zuerst stabilisiert werden?
1. **API Non-Loopback Hard Fail** — Einziges echtes Security-Risiko.
2. **CTS-Lifecycle-Lock** — Concurrency-Korrektheit.
3. **Settings-Lock** — Datenintegrität.

### Ist das Zielmodell überzeugend?
**Ja.** Die Architektur ist bereits nahe am Zielbild. `RunProjection` als Single Source of Truth, phasenbasierte Pipeline, HMAC-signierte Audits, Cross-Parity-Tests — das ist industriestandard-fähig.

### Ist der Umbau realistisch?
**Ja.** Die P1-Findings sind alle kleine, fokussierte Änderungen (je <30 Minuten). Keine architekturelle Neugestaltung nötig.

### Ab wann kann man dem Tool wieder vertrauen?
**Das Tool ist bereits vertrauenswürdig** für den primären Anwendungsfall (lokale Benutzung mit DryRun→Preview→Move Workflow). Nach Behebung der P1-Findings ist es für alle dokumentierten Szenarien freigabefähig.

### Top 10 Punkte jetzt

| # | Aktion | Priorität | Aufwand |
|---|--------|-----------|---------|
| 1 | API Non-Loopback Hard Fail | P1 | 30 min |
| 2 | CTS-Lifecycle-Lock in RunPipeline | P1 | 30 min |
| 3 | Settings-Persist-Lock | P1 | 20 min |
| 4 | Synchrones Save vor Window-Close | P2 | 15 min |
| 5 | `NonGame` Case in GetCategoryRank | P2 | 5 min |
| 6 | NonGame Winner-Selection-Test | P2 | 15 min |
| 7 | NFC-Normalisierung in GetFilesSafe | P2 | 10 min |
| 8 | CLI Runtime-Exit-Code-Tests | P3 | 30 min |
| 9 | Rollback-Reihenfolge-Test | P3 | 30 min |
| 10 | Rate-Limiter Concurrency-Test | P3 | 20 min |
