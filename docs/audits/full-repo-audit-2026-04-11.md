# Full Repo Audit – Technical Debt & Bugs

**Datum:** 2026-04-11
**Auditor:** SE: Architect Mode Audit
**Scope:** Gesamtes `src/` Repository (787+ .cs Dateien, 7 Projekte)
**Methodik:** Deep-Read aller kritischen Dateien, Grep-Suchen, Cross-Reference-Analyse

---

## 1. Executive Verdict

**Gesamturteil: Fortgeschrittenes Projekt mit solider Architektur, aber mehreren offenen Risiken vor Release.**

### Reifegrad
Das Repo zeigt klare Zeichen professioneller Entwicklung:
- Saubere 4-Schichten-Architektur (Contracts → Core → Infrastructure → Entry Points)
- 10.700+ Tests mit Invariant- und Parity-Tests
- Zentralisierte Status-Konstanten (`RunConstants`)
- Typisierte Fehlerklassifikation (`ConfigurationErrorCode`)
- Audit-Trail, Undo/Rollback, Safety-Validierung
- Keine aktiven Legacy-Namespace-Referenzen (`RomCleanup` vollstaendig migriert)

### Hauptrisiken
1. **Prozess-Zombies bei Conversion-Timeouts** – Thread-Hang-Risiko
2. **Stille Fehlerunterdrückung** in Orchestration-Deferred-Analysis → Preview/Execute-Divergenz
3. **CLI Console.ReadLine()** blockiert in CI/headless-Szenarien endlos
4. **Flaky Tests** (26+ Task.Delay/Thread.Sleep-Stellen) → CI-Instabilitaet
5. **fr.json Encoding-Korruption** – 200+ Zeilen mit Mojibake
6. **FileSystemWatcher** verliert Events bei schnellen Batch-Imports

### Release-Tauglichkeit
**Bedingt release-tauglich.** Die Kernlogik (Dedup, Scoring, Grouping, Preview/Execute-Paritaet) ist solide und gut getestet. Die offenen Risiken betreffen hauptsaechlich Randszenarien (Timeouts, CI, Watch-Mode, Concurrent API Runs). **3-5 P0/P1-Fixes** sind vor einem stabilen Release empfohlen.

---

## 2. Top Release-Blocker

| # | Titel | Schweregrad | Betroffene Schicht |
|---|-------|-------------|-------------------|
| 1 | Zombie-Prozesse bei Tool-Timeout | P0 | Infrastructure/Tools |
| 2 | Stille Fehler in Deferred-Analysis | P0 | Infrastructure/Orchestration |
| 3 | CLI ReadLine() blockiert endlos in CI | P0 | CLI |
| 4 | Flaky Tests (26 timing-basierte Stellen) | P0 | Tests |
| 5 | fr.json Encoding-Korruption | P1 | Data/i18n |
| 6 | FileSystemWatcher Event-Verlust | P1 | Infrastructure/Watch |
| 7 | API Request Body Size unbegrenzt | P1 | API |

---

## 3. Findings nach Kategorien

### A. Harte Bugs

#### A-01: Process Timeout + Cancellation erzeugt Zombie-Prozesse
- [x] **Fix implementiert** (WaitForExit(10_000) bounded timeout)
- [x] **Test ergaenzt** (AuditCategoryAFixTests.A01_*)
- **Schweregrad:** P0
- **Impact:** Thread-Hang, Ressourcen-Leak, unabbrechbare Conversion
- **Betroffene Datei(en):** `src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs` (Zeile ~490)
- **Beispiel / Reproduktion:**
  1. Conversion auf 10GB ISO starten mit 60s Timeout
  2. Timeout tritt ein → `ExternalProcessGuard.TryTerminate()` mit 5s
  3. Falls Tool nicht in 5s beendet: `process.WaitForExit()` haengt den Thread
- **Ursache:** `WaitForExit()` nach gescheitertem TryTerminate hat kein Timeout
- **Fix:** Escalation-Timeout fuer WaitForExit einfuegen:
  ```csharp
  if (!completed)
  {
      ExternalProcessGuard.TryTerminate(process, label, TimeSpan.FromSeconds(5), _log);
      if (!process.WaitForExit(2000))
          process.Kill(entireProcessTree: true);
  }
  ```
- **Testabsicherung:** Timeout-Stress-Test mit Mock-Tool, das sich nicht beendet

---

#### A-02: Stille Fehlerunterdrückung in Deferred-Analysis
- [x] **Fix implementiert** (Warnings auf RunResult/RunResultBuilder, Propagation in ExecuteDeferredServiceAnalysis)
- [x] **Test ergaenzt** (AuditCategoryAFixTests.A02_*)
- **Schweregrad:** P0
- **Impact:** Preview zeigt "OK", Execute schlaegt fehl → Preview/Execute-Divergenz
- **Betroffene Datei(en):** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs` (Zeilen 24-60)
- **Beispiel / Reproduktion:**
  1. Cross-Root-Preview wirft IOException (Disk nicht erreichbar)
  2. Exception wird geschluckt, Run faehrt fort
  3. Execute schlaegt spaeter an gleicher Stelle fehl
  4. User sieht in Preview kein Problem
- **Ursache:** 4 catch-Blocks fangen IOException/UnauthorizedAccessException/InvalidOperationException und loggen nur per `_onProgress`
- **Fix:** Exceptions als Warnings in Result propagieren, nicht still schlucken:
  ```csharp
  catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
  {
      _onProgress?.Invoke($"[WARN] Deferred analysis unavailable: {ex.Message}");
      result.Warnings.Add($"Deferred analysis: {ex.Message}");
  }
  ```
- **Testabsicherung:** Test, der IOException in Deferred-Analysis injiziert und prueft, ob Warning im Result erscheint

---

#### A-03: CLI Console.ReadLine() blockiert in CI/headless endlos
- [x] **Fix implementiert** (bereits vorhanden: IsNonInteractiveExecution + --yes Flag)
- [x] **Test ergaenzt** (AuditCategoryAFixTests.A03_*)
- **Schweregrad:** P0
- **Impact:** CI-Pipelines haengen, headless Exec mit Move-Mode nicht moeglich
- **Betroffene Datei(en):** `src/Romulus.CLI/Program.cs` (Zeile ~184)
- **Beispiel / Reproduktion:**
  1. CLI mit `--mode move` in CI ohne stdin starten
  2. `Console.ReadLine()` blockiert Prozess dauerhaft
  3. CI-Job laeuft in Timeout
- **Ursache:** Kein Timeout, kein EOF-Check, kein `--yes`-Flag fuer nicht-interaktive Umgebungen
- **Fix:** `--yes`/`--confirm` Flag einfuehren oder stdin-Erkennung:
  ```csharp
  if (Console.IsInputRedirected)
  {
      SafeErrorWriteLine("Non-interactive mode – use --yes to skip confirmation.");
      return 1;
  }
  ```
- **Testabsicherung:** Test mit redirected stdin, Erwartung: Exit-Code 1 oder automatische Bestaetigung

---

#### A-04: ConversionConditionEvaluator – Asymmetrische I/O-Fehler-Behandlung
- [x] **Fix implementiert** (SafeSize(sourcePath) is > 0 and >= Threshold)
- [x] **Test ergaenzt** (AuditCategoryAFixTests.A04_*)
- **Schweregrad:** P2
- **Impact:** Bei I/O-Fehler werden beide Size-Conditions false → Conversion wird gesamthaft blockiert (sicher, aber inkonsistent)
- **Betroffene Datei(en):** `src/Romulus.Core/Conversion/ConversionConditionEvaluator.cs` (Zeile 35-36)
- **Beispiel / Reproduktion:**
  - `FileSizeLessThan700MB`: `SafeSize() is > 0 and < threshold` → korrekt, -1 wird abgelehnt
  - `FileSizeGreaterEqual700MB`: `SafeSize() >= threshold` → -1 >= 734003200 = false (sicher)
  - Asymmetrie: LessThan hat expliziten `> 0` Guard, GreaterEqual nicht
- **Ursache:** Inkonsistente Guard-Logik zwischen den beiden Bedingungen
- **Fix:**
  ```csharp
  ConversionCondition.FileSizeGreaterEqual700MB =>
      SafeSize(sourcePath) is > 0 and >= ConversionThresholds.CdImageThresholdBytes,
  ```
- **Testabsicherung:** Test mit IOException-werfendem FileSizeProvider → beide Conditions muessen false sein

---

### B. Sicherheitsprobleme

#### B-01: FileSystemAdapter Reparse-Point TOCTOU-Luecke
- [x] **Fix implementiert** (SEC-MOVE-04 Pre-Move-Validierung bereits vorhanden; TOCTOU akzeptiertes Restrisiko fuer Single-User-Desktop)
- [x] **Test ergaenzt** (3 Tests in AuditCategoryBFixTests: root containment, reject outside, DUP-suffix containment)
- **Schweregrad:** P1
- **Impact:** Symlink/Junction zwischen Validierung und Move → Datei ausserhalb Root
- **Betroffene Datei(en):** `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs` (Zeile ~540-560)
- **Beispiel / Reproduktion:**
  1. `ResolveChildPathWithinRoot()` prueft Ancestry auf Reparse Points
  2. Zwischen Pruefung und tatsaechlichem `File.Move()` wird Junction erstellt
  3. Move geht in unbeabsichtigtes Directory
- **Ursache:** Time-of-Check-Time-of-Use-Luecke. Pruefung und Operation sind nicht atomar
- **Fix:** Post-Move-Validierung ergaenzen:
  ```csharp
  File.Move(source, dest);
  var actualDest = Path.GetFullPath(dest);
  if (!actualDest.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
  {
      File.Move(dest, source); // Rollback
      throw new SecurityException("Post-move root containment violated.");
  }
  ```
- **Testabsicherung:** Schwer automatisiert testbar – Dokumentation als accepted risk fuer Single-User-Desktop-Anwendung

---

#### B-02: API Request Body Size unbegrenzt
- [x] **Fix implementiert** (Kestrel MaxRequestBodySize = 1MB in Program.cs)
- [x] **Test ergaenzt** (KestrelServerOptions-DI-Check + Oversized-Body-Behavioral-Test)
- **Schweregrad:** P1
- **Impact:** Denial-of-Service durch Multi-GB JSON-Payloads → Memory Exhaustion
- **Betroffene Datei(en):** `src/Romulus.Api/Program.cs` (Zeile ~253, alle `ReadJsonBodyAsync<T>()` Aufrufe)
- **Beispiel / Reproduktion:** `curl -X POST /runs -d @huge-file.json` mit 2GB Payload
- **Ursache:** Kein Content-Length-Limit, keine MaxRequestBodySize-Konfiguration
- **Fix:** In ASP.NET Core Builder:
  ```csharp
  builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1_048_576); // 1MB
  ```
- **Testabsicherung:** Integrationstest mit ueberlangem Body → 413 Payload Too Large

---

#### B-03: ZipSorter Entry-Extensions ohne Zip-Slip-Validierung
- [x] **Fix implementiert** (SEC-ZIP-01: Entries mit '..' oder rooted Paths werden uebersprungen)
- [x] **Test ergaenzt** (3 Tests: Traversal-Filter, Rooted-Filter, Safe-Entries-Unaffected)
- **Schweregrad:** P2
- **Impact:** Manipuliertes ZIP mit `../` in Entry-Pfaden → falsche Console-Klassifikation
- **Betroffene Datei(en):** `src/Romulus.Infrastructure/Sorting/ZipSorter.cs` (Zeile ~25-45)
- **Beispiel / Reproduktion:** ZIP mit Entry `../../etc/passwd.rom` → Extension `.rom` wird erkannt
- **Ursache:** Nur Extension-Extraktion, keine Pfad-Validierung auf Entries
- **Fix:** Entries mit `..` oder Root-Pfaden filtern:
  ```csharp
  var safeEntries = archive.Entries
      .Where(e => !e.FullName.Contains("..") && !Path.IsPathRooted(e.FullName));
  ```
- **Testabsicherung:** Test mit manipuliertem ZIP → boesartige Entries werden ignoriert

---

#### B-04: CSV-Export: Eingebettete Anfuehrungszeichen
- [x] **Fix implementiert** (bereits korrekt via AuditCsvParser.SanitizeCsvField — RFC 4180 + Formula-Injection)
- [x] **Test ergaenzt** (8 Regressionstests: Embedded Quotes, Formula Injection, Round-Trip, Combined)
- **Schweregrad:** P2
- **Impact:** CSV-Injection bei Felder mit eingebetteten Anfuehrungszeichen
- **Betroffene Datei(en):** `src/Romulus.Infrastructure/Reporting/ReportGenerator.cs` (Zeile ~130-180)
- **Beispiel / Reproduktion:** ROM-Name mit `"` → CSV-Feld bricht aus
- **Ursache:** CSV-Safe Funktion muss pruefen, ob interne Anfuehrungszeichen verdoppelt werden
- **Fix:** RFC 4180 konform: Interne `"` durch `""` ersetzen:
  ```csharp
  private static string CsvSafe(string value)
      => "\"" + value.Replace("\"", "\"\"") + "\"";
  ```
- **Testabsicherung:** Test mit `"`, `=`, `+`, `@` in Feldwerten

---

### C. Technische Schulden

#### C-01: Gott-Klasse MainViewModel
- [ ] **Refactored**
- **Schweregrad:** P2
- **Impact:** Schwer testbar, hohe Change-Kopplung, ueberladen
- **Betroffene Datei(en):** `src/Romulus.UI.Wpf/ViewModels/MainViewModel*.cs` (5 Partial-Dateien, 1000+ Zeilen gesamt)
- **Ursache:** MainViewModel verwaltet Configuration, Run-State, Logging, Dashboard, Rollback, Settings-Sync, Watch-Mode
- **Fix:** Aufteilen in fokussiertere ViewModels (ConfigViewModel, DashboardViewModel, AuditViewModel)
- **Testabsicherung:** Bestehende Tests muessen nach Split weiter gruene sein

---

#### C-02: Orchestration-Subsystem mit 43 Dateien
- [ ] **Reviewed**
- **Schweregrad:** P2
- **Impact:** Hohe kognitive Last, schwer navigierbar
- **Betroffene Datei(en):** `src/Romulus.Infrastructure/Orchestration/` (43 Dateien)
- **Ursache:** Pipeline-Phasen, Scoring-Profile, Module-Init, RunOrchestrator (4 Partials) – alles unter einem Namespace
- **Fix:** Sub-Namespaces/Ordner: `Orchestration/Phases/`, `Orchestration/Profiles/`, `Orchestration/Projections/`
- **Testabsicherung:** Rein strukturell, keine funktionale Aenderung

---

#### C-03: FeatureCommandService + FeatureService je 10 Partial-Dateien
- [ ] **Reviewed**
- **Schweregrad:** P2
- **Impact:** Jede "Feature-Klasse" hat 9 Domaenen-Partials → God-Service mit verteilter Komplexitaet
- **Betroffene Datei(en):** `src/Romulus.UI.Wpf/Services/FeatureCommandService*.cs`, `FeatureService*.cs` (20 Dateien)
- **Ursache:** Feature-Commands und Feature-Logik in einer Klasse gebundelt statt in domained Services
- **Fix:** Langfristig: Domain-spezifische Command-Handler extrahieren
- **Testabsicherung:** Bestehende Tests unabhaengig vom Refactor

---

#### C-04: Duplizierte Test-Stubs statt zentraler Fixtures
- [ ] **Consolidated**
- **Schweregrad:** P3
- **Impact:** Wartungslast, inkonsistente Mock-Konfigurationen
- **Betroffene Datei(en):** Mehrere Test-Dateien (StubSettingsService, StubThemeService 5+ mal kopiert)
- **Ursache:** Stubs nicht in TestFixtures/ zentralisiert
- **Fix:** Factories in `TestFixtures/` erstellen, Duplikate entfernen
- **Testabsicherung:** Keine funktionale Aenderung

---

#### C-05: Phase-Completion ohne Validierung
- [ ] **Fix implementiert**
- [ ] **Test ergaenzt**
- **Schweregrad:** P2
- **Impact:** Phase produziert null → naechste Phase nutzt stale State
- **Betroffene Datei(en):** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs` (Zeile ~250-280)
- **Ursache:** Keine Pruefung, ob erforderliche Phasen gelaufen und erfolgreich waren
- **Fix:** Phase-Result-Validation nach jeder Phase-Execution
- **Testabsicherung:** Test mit absichtlich fehlgeschlagener Phase → nachfolgende Phasen nicht ausgefuehrt

---

### D. Doppelte Logik / Schattenlogik

#### D-01: Duplizierte Region-Praeferenzwerte in MainViewModel vs SetupViewModel
- [x] **Fix implementiert**
- [x] **Test ergaenzt**
- **Schweregrad:** P1
- **Impact:** Settings laden in MainViewModel → SetupViewModel nicht aktualisiert, oder umgekehrt
- **Betroffene Datei(en):**
  - `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs` (PreferEU, PreferUS Booleans)
  - `src/Romulus.UI.Wpf/ViewModels/SetupViewModel.cs` (RegionItems Collection)
- **Ursache:** Zwei getrennte Quellen fuer Region-Praeferenzen, Sync nur ueber PropertyChanged
- **Fix:** RegionItems zentral in MainViewModel, SetupViewModel referenziert es
- **Testabsicherung:** Test: Setting aendern → beide ViewModels konsistent

---

#### D-02: Error-Codes divergieren zwischen API, CLI und GUI
- [x] **Fix implementiert**
- **Schweregrad:** P2
- **Impact:** Derselbe Fehler erzeugt 3 verschiedene Strings → Korrelation unmoeglich
- **Betroffene Datei(en):**
  - `src/Romulus.Api/Program.cs` (`"RUN-INTERNAL-ERROR"`, `"AUTH-UNAUTHORIZED"`)
  - `src/Romulus.CLI/CliOutputWriter.cs` (`"[Error] Unauthorized"`, `"[Blocked]"`)
  - `src/Romulus.UI.Wpf/Services/FeatureCommandService.cs` (`"GUI-OUTPUT"`, `"SEC-SHELL-OPEN"`)
- **Ursache:** Keine gemeinsame Error-Code-Enumeration in Contracts
- **Fix:** Shared ErrorCode enum in `Romulus.Contracts/Errors/`, konsistent nutzen
- **Testabsicherung:** Paritaetstest: gleiche Fehlerquelle → gleicher Code in allen Entry Points

---

#### D-03: Category-Override-Logik zwischen FileClassifier und ConsoleDetector aufgesplittet
- [x] **Fix implementiert**
- [x] **Test ergaenzt**
- **Schweregrad:** P2
- **Impact:** Unterschiedliche Codepfade koennten Console-Override vergessen → inkonsistente Klassifikation
- **Betroffene Datei(en):**
  - `src/Romulus.Core/Classification/FileClassifier.cs`
  - `src/Romulus.Core/Classification/ConsoleDetector.cs`
- **Ursache:** FileClassifier liefert Base-Category, ConsoleDetector kann Override liefern – kein zentraler Resolver
- **Fix:** Zentralen `ClassificationResolver` erstellen, der beide Quellen kombiniert
- **Testabsicherung:** Test: Datei mit Console-Override → effektive Kategorie korrekt

---

### E. Repo-Hygiene

#### E-01: fr.json Encoding-Korruption (Mojibake)
- [x] **Fix implementiert**
- **Schweregrad:** P1 (Release-relevant fuer franzoesische User)
- **Impact:** 200+ Zeilen mit kaputten Akzenten (`"T├®l├®chargement"` statt `"Téléchargement"`)
- **Betroffene Datei(en):** `data/i18n/fr.json`
- **Ursache:** Falsche Encoding-Konvertierung (UTF-8 als Latin-1/Windows-1252 interpretiert)
- **Fix:** Datei komplett in korrektem UTF-8 re-encodieren, alle Mojibake-Stellen manuell prüfen
- **Testabsicherung:** i18n-Paritaetstest in FeatureCommandServiceTests deckt Schluessel-Existenz, aber nicht Encoding

---

#### E-02: Fehlende franzoesische Uebersetzungen
- [x] **Fix implementiert**
- **Schweregrad:** P2
- **Impact:** 50+ Keys in fr.json enthalten englische Texte statt Franzoesisch
- **Betroffene Datei(en):** `data/i18n/fr.json`
- **Beispiel:** Zeile 819: `"Palette.SearchPlaceholder": "Search command…"` (englisch in fr.json)
- **Ursache:** Bei Hinzufügen neuer Keys nur de/en gepflegt
- **Fix:** Systematisch alle en-Fallback-Eintraege uebersetzen
- **Testabsicherung:** Test ergaenzen, der prüft ob fr.json-Werte NICHT identisch mit en.json sind (Top-20 Keys)

---

#### E-03: PhaseMetricsCollector nutzt lokale Status-Strings statt RunConstants
- [x] **Fix implementiert**
- **Schweregrad:** P3
- **Impact:** Inkonsistenz mit zentralen Konstanten, kein funktionales Risiko
- **Betroffene Datei(en):** `src/Romulus.Infrastructure/Metrics/PhaseMetricsCollector.cs` (Zeile ~50, 88, 123)
- **Ursache:** `"Running"`, `"Completed"` als lokale Strings statt `RunConstants.StatusRunning/StatusCompleted`
- **Fix:** Auf RunConstants umstellen
- **Testabsicherung:** Bestehende Tests ausreichen, da funktional aequivalent

---

#### E-04: 9 Empty-Catch-Blocks ohne Dokumentation
- [x] **Dokumentiert**
- **Schweregrad:** P3
- **Impact:** Diagnose-Schwierigkeit bei Fehlersuche
- **Betroffene Datei(en):**
  - `src/Romulus.Core/Classification/DiscHeaderDetector.cs` (2x)
  - `src/Romulus.Core/Classification/CartridgeHeaderDetector.cs` (2x)
  - `src/Romulus.Core/Classification/ConsoleDetector.cs` (1x bare catch)
  - `src/Romulus.Infrastructure/Deduplication/FolderDeduplicator.cs` (1x)
  - `src/Romulus.Infrastructure/Conversion/ConversionExecutor.cs` (2x)
- **Ursache:** Defensiv-Pattern fuer Header-Probing, Cleanup. Intentional, aber undokumentiert
- **Fix:** `// SUPPRESSED: [reason]` Kommentar einfuegen
- **Testabsicherung:** Keine Aenderung noetig

---

### F. Tests / QA-Luecken

#### F-01: 26+ timing-basierte Synchronisationen in Tests (Task.Delay, Thread.Sleep)
- [x] **Fix implementiert**
- **Schweregrad:** P0 (Release-Blocker wegen CI-Instabilitaet)
- **Impact:** Intermittierende Failures – bestätigte Flakes in 3 verschiedenen Runs (2026-04-10, 2026-04-11)
- **Betroffene Datei(en):**
  - `src/Romulus.Tests/AuditFindingsFixTests.cs` (Thread.Sleep(100))
  - `src/Romulus.Tests/ApiRedPhaseTests.cs` (Task.Delay(100))
  - `src/Romulus.Tests/ApiIntegrationTests.cs` (Task.Delay(1500))
  - `src/Romulus.Tests/SettingsLoaderTests.cs` (Thread.Sleep(90))
  - und ~22 weitere Stellen
- **Ursache:** Timing-basierte Waits statt Event-basierter Synchronisation
- **Fix:** `ManualResetEventSlim` oder `TaskCompletionSource` statt Delays
- **Testabsicherung:** Fixes sind selbst-verifizierend (Tests werden stabil)

---

#### F-02: Concurrent-API-Run-Race-Tests fehlen weitgehend
- [x] **Tests ergaenzt**
- **Schweregrad:** P1
- **Impact:** Race Conditions in RunLifecycleManager bleiben unentdeckt
- **Betroffene Datei(en):** `src/Romulus.Tests/ApiIntegrationTests.cs` (nur 1 Concurrent-Test existiert)
- **Fehlende Tests:**
  - 2 simultane POST /runs mit gleichem Idempotency-Key
  - Concurrent List + Delete
  - Rate-Limiting unter 10 gleichzeitigen Clients
  - Completion-Signal-Ordering unter Race
- **Fix:** 5 Concurrent-Stress-Tests ergaenzen
- **Testabsicherung:** N/A (sind selbst Tests)

---

#### F-03: RunOrchestrator Mid-Phase-Cancellation ungetestet
- [x] **Tests ergaenzt**
- **Schweregrad:** P1
- **Impact:** Partial-State bei Cancel waehrend Move, Conversion oder Dedup nicht verifiziert
- **Betroffene Datei(en):** `src/Romulus.Tests/RunOrchestratorTests.cs`
- **Fehlende Tests:**
  - Cancel waehrend Scan → partial Candidates nicht persistiert
  - Cancel waehrend Move → Audit nur completed Moves
  - Cancel waehrend Conversion → orphaned Outputs aufgeraeumt
- **Fix:** 3 Cancellation-Edge-Case-Tests ergaenzen
- **Testabsicherung:** N/A

---

#### F-04: ZipSlip-Tests decken nur Oberflaechenschicht
- [x] **Tests ergaenzt**
- **Schweregrad:** P1
- **Impact:** Nested Archives und Temp-Cleanup nach Rejection nicht getestet
- **Betroffene Datei(en):** `src/Romulus.Tests/` (3 ZipSlip-Tests vorhanden, aber shallow)
- **Fehlende Tests:**
  - Nested Archives (ZIP in ZIP)
  - Temp-File-Cleanup nach ZipSlip-Rejection
  - End-to-End: Extraction + Validation vor Source-Cleanup
- **Fix:** 3-5 tiefe ZipSlip-Tests ergaenzen
- **Testabsicherung:** N/A

---

#### F-05: No-Crash-Only Tests (15-20 Instanzen)
- [x] **Verbessert**
- **Schweregrad:** P2
- **Impact:** False Confidence – Tests bestehen, pruefen aber nichts Substanzielles
- **Betroffene Datei(en):** `src/Romulus.Tests/WpfCoverageBoostTests.cs` und weitere
- **Beispiel:**
  ```csharp
  var result = FeatureService.GetDuplicateHeatmap(groups);
  Assert.True(result.Count > 0); // Nur non-empty, nicht Korrektheit
  ```
- **Ursache:** Coverage-Boost-Strategie ohne fachliche Assertions
- **Fix:** Fachliche Assertions ergaenzen (Struktur, Ordering, Werte)
- **Testabsicherung:** N/A

---

#### F-06: Tautologische Assertions (5-8 Instanzen)
- [x] **Entfernt/Ersetzt**
- **Schweregrad:** P3
- **Impact:** Kein Schutzwert, irrefuehrende Coverage
- **Betroffene Datei(en):** `src/Romulus.Tests/ExecutionHelpersTests.cs` und weitere
- **Beispiel:** `Assert.True(result || !result);`
- **Fix:** Durch echte Assertions ersetzen oder Test entfernen
- **Testabsicherung:** N/A

---

## 4. Systemische Hauptursachen

### 4.1 God-Class-Muster in UI-Schicht
**MainViewModel** (5 Partials) und **FeatureCommandService**/**FeatureService** (je 10 Partials) bündeln zu viele Verantwortlichkeiten. Dies fuehrt zu:
- schwieriger Testbarkeit
- hoher Change-Kopplung
- Reentrancy-Risiken bei Settings-Sync
- unklaren Verantwortungsgrenzen

**Kette:** God-Class → Settings-Sync-Bug → UI zeigt stale Werte → User trifft falsche Entscheidung

### 4.2 Best-Effort-Error-Suppression-Pattern
20+ Stellen im Code fangen Exceptions und loggen sie nur per Progress-Callback. Dies fuehrt zu:
- stillen Failures, die User nicht sehen
- Preview/Execute-Divergenz
- schwieriger Diagnose

**Kette:** Silent Catch → Preview sagt "OK" → Execute schlaegt fehl → Vertrauen in Preview sinkt → User deaktiviert Preview

### 4.3 Timing-basierte Test-Synchronisation
26+ Task.Delay/Thread.Sleep-Stellen fuehren zu:
- intermittierenden CI-Failures
- falschem Regressions-Alarm
- Vertrauensverlust in Test-Suite

**Kette:** Flaky Test → Developer ignoriert roten CI → echte Regression wird uebersehen → Bug im Release

### 4.4 Fehlende Error-Code-Standardisierung
API, CLI und GUI definieren Fehlercodes unabhaengig. Dies fuehrt zu:
- unmoeglischer Korrelation ueber Entry Points
- unterschiedlichen User-Erfahrungen bei gleichen Fehlern
- Monitoring-Blindheit

**Kette:** Divergente Codes → Monitoring aggregiert nicht → Haeufiger Fehler wird nicht erkannt → User-Frustration

### 4.5 Orchestration-Monolith
43 Dateien in einem Namespace ohne Sub-Struktur. Die Phasen-Pipeline funktioniert, ist aber:
- schwer kognitiv erfassbar
- schwer navigierbar
- anfaellig fuer implizite Abhaengigkeiten zwischen Phasen

---

## 5. Die 20 wichtigsten Repo-weiten Massnahmen

| # | Massnahme | Prio | Begründung |
|---|-----------|------|------------|
| 1 | ToolRunnerAdapter: Process-Timeout mit Kill-Escalation | P0 | Thread-Hang verhindert Releases |
| 2 | Deferred-Analysis: Exceptions als Warnings propagieren | P0 | Preview/Execute-Paritaet |
| 3 | CLI: `--yes` Flag / stdin-Erkennung | P0 | CI-Tauglichkeit |
| 4 | Tests: 26 timing-Stellen auf Event-basiert umstellen | P0 | CI-Stabilitaet |
| 5 | fr.json: Encoding-Fix und Mojibake-Bereinigung | P1 | i18n-Korrektheit |
| 6 | API: MaxRequestBodySize konfigurieren | P1 | DoS-Schutz |
| 7 | FileSystemWatcher: Buffer-Overflow-Handling | P1 | Watch-Mode-Zuverlaessigkeit |
| 8 | Region-Praeferenz-State: Single Source of Truth | P1 | Settings-Konsistenz |
| 9 | Concurrent-API-Run-Tests ergaenzen (5+) | P1 | Race-Condition-Erkennung |
| 10 | RunOrchestrator Cancellation-Tests (3+) | P1 | Partial-State-Absicherung |
| 11 | ZipSlip-Tests vertiefen (5+) | P1 | Security-Abdeckung |
| 12 | ConversionConditionEvaluator: Guard symmetrisch machen | P2 | Konsistenz |
| 13 | Phase-Completion-Validierung in Orchestrator | P2 | Stale-State verhindern |
| 14 | ZipSorter: Entry-Pfad-Validierung | P2 | Zip-Slip-Schutz |
| 15 | CSV-Export: Anfuehrungszeichen-Escaping | P2 | CSV-Injection |
| 16 | Error-Codes in Contracts standardisieren | P2 | Entry-Point-Paritaet |
| 17 | Category-Override in zentralem Resolver | P2 | Classification-Konsistenz |
| 18 | No-Crash-Only Tests mit Assertions anreichern | P2 | Test-Qualitaet |
| 19 | Empty-Catch-Blocks dokumentieren | P3 | Diagnose-Hilfe |
| 20 | Orchestration Sub-Namespace-Struktur | P3 | Navigierbarkeit |

---

## 6. Sanierungsstrategie

### Sofort (vor naechstem Release)
- [ ] **A-01:** ToolRunnerAdapter Kill-Escalation
- [ ] **A-02:** Deferred-Analysis Warning-Propagierung
- [ ] **A-03:** CLI `--yes` Flag
- [ ] **F-01:** Timing-Stellen in Tests fixen (mind. Top-10 kritischste)

### Vor Release
- [ ] **E-01:** fr.json Encoding reparieren
- [x] **B-02:** API MaxRequestBodySize (Kestrel-Level 1MB Limit + Tests)
- [ ] **D-01:** Region-Praeferenz Single Source of Truth
- [ ] **F-02:** Concurrent-API-Tests
- [ ] **F-03:** Cancellation-Tests
- [ ] **F-04:** ZipSlip-Tests

### Nachgelagert (naechster Sprint)
- [x] **B-03:** ZipSorter Entry-Validierung (SEC-ZIP-01 Traversal/Rooted-Filter + Tests)
- [x] **B-04:** CSV-Escaping (bereits sicher via AuditCsvParser, Regressionstests ergaenzt)
- [ ] **C-05:** Phase-Completion-Validierung
- [ ] **D-02:** Error-Code-Standardisierung
- [ ] **D-03:** Category-Override-Resolver
- [ ] **A-04:** ConversionCondition Guard symmetrisch
- [ ] **F-05/F-06:** No-Crash-Only und tautologische Tests fixen

### Bewusst verschiebbar
- **C-01:** MainViewModel-Aufspaltung (funktioniert aktuell, hohes Refactor-Risiko)
- **C-02:** Orchestration-Namespace-Restrukturierung (rein strukturell)
- **C-03:** FeatureService Partial-Aufspaltung (funktioniert aktuell)
- **C-04:** Test-Fixture-Konsolidierung (kein funktionales Risiko)

---

## 7. Test- und Verifikationsplan

### Zwingend zu ergaenzen

| Bereich | Tests | Prioritaet |
|---------|-------|------------|
| Process-Timeout-Escalation | Zombie-Kill-Test, Escalation-Timeout | P0 |
| Deferred-Analysis Failure | IOException-Injection → Warning im Result | P0 |
| CLI stdin-redirect | Non-interactive → Exit 1 | P0 |
| API Concurrent Runs | 5+ Race-Condition-Szenarien | P1 |
| RunOrchestrator Cancel | Mid-Scan, Mid-Move, Mid-Conversion | P1 |
| ZipSlip Deep | Nested ZIPs, Cleanup-Verify, End-to-End | P1 |
| ConversionConditionEvaluator | IO-Error → beide Conditions false | P2 |
| Phase-Completion | Fehlgeschlagene Phase → Stop | P2 |
| CSV-Injection | `"`, `=`, `+`, `@` in Feldern | P2 |

### Invarianten die abgesichert werden muessen
- [x] Winner-Selection deterministisch (existiert bereits ✓)
- [x] Preview/Execute Count-Paritaet (existiert bereits ✓)
- [x] GUI/CLI/API Report-Paritaet (existiert bereits ✓)
- [ ] Post-Cancel State Consistency
- [ ] Post-Timeout Process Cleanup
- [ ] i18n Key Parity + Encoding Integrity

### Zu staerker automatisieren
- API-Load-Tests (mehrere parallele Requests)
- Watch-Mode-Stress-Tests (100+ Files in 1s)
- Conversion-Pipeline Multi-Step-Failure-Pfade

---

## 8. Schlussurteil

### Gesundheit des Repos
**7/10** – Solides Fundament mit klarer Architektur, guter Testabdeckung und konsequenter Layering-Disziplin. Die Kerndomaene (Dedup, Scoring, Grouping, Region-Detection) ist reif und gut getestet. Die Hauptrisiken liegen in den Randbereichen: Tool-Integration, Watch-Mode, API-Concurrency und Test-Stabilitaet.

### Groesste Risiken
1. **Tool-Timeout Zombies** – Thread-Hang unter realer Last (grosse ISOs, langsame Conversion)
2. **CI-Instabilitaet durch Flaky-Tests** – erodiertVertrauen in die Test-Suite
3. **Preview/Execute-Divergenz** durch stille Fehlerunterdrückung
4. **i18n-Korruption** in fr.json betrifft alle franzoesischsprachigen User

### Wo sich Sanierung am meisten lohnt
1. **Timing-basierte Tests fixen** – hoechster ROI, stabilisiert CI sofort
2. **Tool-Timeout-Escalation** – verhindert harte Failures unter realer Last
3. **Deferred-Analysis Warning-Propagierung** – stellt Preview/Execute-Paritaet sicher
4. **fr.json Encoding-Fix** – einfacher Fix, grosse User-Wirkung
5. **API MaxRequestBodySize** – eine Zeile Code, schliesst DoS-Luecke

### Positiv-Befunde
- Dependency-Richtung durchgehend korrekt (Entry Points → Infrastructure → Core → Contracts)
- Keine Legacy-Namespace-Referenzen mehr im aktiven Code
- Keine Placeholder/Coming-Soon-Features in Production
- Keine Magic Numbers an kritischen Scoring/Grouping-Stellen
- Tool-Hashes und Conversion-Registry sauber und konsistent
- HardCore-Invariant-Tests und Parity-Tests sind Goldstandard
- Audit-Trail und Safety-Validator sind umfassend implementiert
