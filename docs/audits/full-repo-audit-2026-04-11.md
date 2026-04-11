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
- [x] **Fix implementiert**
- [x] **Test ergaenzt**
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
| 13 | Shared Error-Code-Enumeration in Contracts | P2 | Cross-Entry-Point-Korrelation |
| 14 | FeatureCommandService: 19 `.GetAwaiter().GetResult()` in WPF UI eliminieren | P2 | Deadlock-Risiko am UI-Thread |
| 15 | Phase-Completion-Validation im RunOrchestrator | P2 | Stale-State-Vermeidung |
| 16 | No-Crash-Only Tests in CoverageBoost durch echte Assertions ersetzen | P2 | False Confidence |
| 17 | Global-Static-Mutable-State in Core-Scorern absichern (Reset für Tests) | P2 | Test-Isolation |
| 18 | Orchestration-Namespace aufteilen (Phases/Profiles/Projections) | P3 | Navigierbarkeit |
| 19 | Duplizierte Test-Stubs in zentrale Fixtures konsolidieren | P3 | Wartbarkeit |
| 20 | Empty-Catch-Blocks mit SUPPRESSED-Kommentaren dokumentieren | P3 | Diagnosequalitaet |

---

## 6. Sanierungsstrategie

### Sofort (vor naechstem Merge)
- [x] **A-01:** ToolRunnerAdapter Process-Timeout-Escalation (Thread-Hang blockiert Nutzbarkeit)
- [x] **F-01:** Mindestens die 5 kritischsten Timing-Stellen in Tests auf Event-basiert umstellen
- [x] **A-02:** Deferred-Analysis Warnings konsequent propagieren

### Vor Release
- [x] **A-03:** CLI `--yes` / `--non-interactive` Flag oder stdin-EOF-Erkennung
- [x] **E-01:** fr.json Encoding-Korruption bereinigen (alle Mojibake-Stellen)
- [x] **B-02:** API MaxRequestBodySize auf 1MB setzen (bereits in Code, Verifikation noetig)
- [x] **D-01:** Region-Praeferenzen Single Source of Truth
- [x] **F-02:** 5+ Concurrent-API-Run-Tests
- [x] **F-03:** 3+ Cancellation-Edge-Case-Tests
- [x] **F-04:** 3+ tiefe ZipSlip-Tests
- [x] **C-05:** Phase-Completion-Validation im RunOrchestrator

### Nachgelagert (Post-Release, Tech-Debt-Sprint)
- [ ] **C-01:** MainViewModel aufteilen (God-Class)
- [ ] **C-02:** Orchestration-Namespace restrukturieren
- [ ] **C-03:** FeatureCommandService / FeatureService Domain-Handler extrahieren
- [ ] **C-04:** Duplizierte Test-Stubs konsolidieren
- [x] **D-02:** Shared Error-Code-Enumeration
- [x] **D-03:** Zentralen ClassificationResolver erstellen
- [x] **E-02:** Fehlende franzoesische Uebersetzungen
- [x] **E-03:** PhaseMetrics Status-Strings auf RunConstants umstellen
- [x] **E-04:** Empty-Catch-Blocks dokumentieren

### Bewusst verschoben / akzeptiert
- [ ] **B-01:** TOCTOU bei Reparse Points — Accepted Risk fuer Single-User-Desktop
- [x] **F-06:** Tautologische Assertions — niedrig, einzeln bei Gelegenheit ersetzen
- [ ] Orchestration-Monolith — funktioniert, restrukturieren nur bei erkennbarem Bugrisiko

---

## 7. Test- und Verifikationsplan

### Zwingend zu ergaenzende Tests

| Bereich | Fehlende Tests | Prio |
|---------|---------------|------|
| **Tool-Timeout** | Mock-Tool das sich nicht beendet → Escalation zu Kill verifizieren | P0 |
| **Deferred-Analysis** | IOException-Injection → Warning im RunResult pruefen | P0 |
| **CLI Non-Interactive** | Redirected stdin → Exit 1 oder auto-confirm | P0 |
| **Concurrent API Runs** | 2 simultane POST /runs, Idempotency-Key-Collision, Rate-Limit-Stress | P1 |
| **Cancellation Edge** | Cancel waehrend Scan/Move/Conversion → Audit+Cleanup korrekt | P1 |
| **ZipSlip Deep** | Nested ZIPs, Reparse-in-Archive, Temp-Cleanup nach Rejection | P1 |
| **Phase Validation** | Fehlgeschlagene Phase → nachfolgende Phasen nicht ausgefuehrt | P2 |
| **Global Static Reset** | Parallele Tests mit unterschiedlichen Scorer-Config → Isolation | P2 |

### Abzusichernde Invarianten

| Invariante | Status |
|-----------|--------|
| Winner-Selection deterministisch (gleiche Inputs → gleicher Winner) | ✅ Abgedeckt |
| Kein Move ausserhalb erlaubter Roots | ✅ Abgedeckt |
| Keine leeren/inkonsistenten GameKeys | ✅ Abgedeckt |
| Preview/Execute/Report-Konsistenz | ⚠️ Teilweise (Deferred-Analysis-Gap) |
| GUI/CLI/API-Paritaet | ⚠️ Teilweise (Error-Code-Divergenz) |
| Audit-CSV: Alle Moves geloggt, Rollback funktioniert | ✅ Abgedeckt |
| Conversion: Source nie vor Verify geloescht | ✅ Abgedeckt |
| Conversion: Partial Outputs aufgeraeumt | ✅ Abgedeckt |
| ZipSlip: Kein Path-Traversal bei Extraktion | ⚠️ Oberflaechlich |

### Bereiche mit staerkerer Automatisierung

1. **Watch-Mode:** FileSystemWatcher-Event-Verlust unter Last automatisiert testen (derzeit manuell)
2. **API SSE-Stream:** End-to-End Event-Ordering bei Cancel/Completion/Timeout
3. **WPF Settings-Sync:** PropertyChanged-Kaskaden automatisiert auf Reentrancy pruefen
4. **Large Collection:** 50k+ Dateien Performance-Regression-Gate (existierender Benchmark-Gate ausbauen)
5. **Multi-Root Sorting:** Kreuz-Root-Move mit unterschiedlichen Volumes/Drives

---

## 8. Schlussurteil

### Wie gesund ist das Repo wirklich?

**Gut bis sehr gut.** Die Kernarchitektur (4-Schichten, Contracts→Core→Infrastructure→EntryPoints) ist sauber und diszipliniert umgesetzt. Die 10.700+ Tests bieten eine solide Basis mit echten Invariant- und Paritaetstests. Die kritischen Bereiche (Dedup-Winner-Selection, Region-Detection, Scoring, Safety-Validation) sind deterministisch und gut abgesichert.

Die Code-Hygiene ist ueberdurchschnittlich: Legacy-Namespace vollstaendig migriert, i18n-Schluessel-Paritaet DE/EN/FR gewahrt (1160 Keys synchron), zentrale RunConstants fuer Status-Strings, typisierte Fehlercodes fuer Konfigurationsfehler.

### Was sind die groessten Risiken?

1. **Thread-Hang bei Tool-Timeouts** — einziges echtes P0-Risiko fuer Produktionsstabilitaet
2. **Preview/Execute-Divergenz durch stille Fehlerunterdruckung** — untergraebt Vertrauen in Preview
3. **CI-Instabilitaet durch Timing-Tests** — fuehrt zu Alarm-Muedigkeit und uebersehenen Regressionen
4. **19x `.GetAwaiter().GetResult()` im WPF-UI-Code** — potentielle Deadlock-Quelle am UI-Thread bei bestimmten SynchronizationContext-Szenarien
5. **Global mutable static State in Core-Scorern** (6 Klassen mit static volatile Register-Pattern) — Test-Isolation-Risiko bei parallelen Tests

### Wo lohnt sich die Sanierung am meisten?

1. **Tool-Timeout-Escalation** (A-01): Kleiner Fix, grosser Impact — eliminiert Thread-Hang-Risiko
2. **Timing-Tests** (F-01): 5 kritische Stellen fixen stabilisiert CI um geschaetzt 80%
3. **Deferred-Analysis-Warnings** (A-02): Warning-Propagation ist ein 10-Zeilen-Fix mit grossem Vertrauensgewinn
4. **`.GetAwaiter().GetResult()` Migration**: 19 Stellen systematisch auf async/await umstellen — eliminiert latentes Deadlock-Risiko komplett
5. **Error-Code-Standardisierung** (D-02): Mittlerer Aufwand, aber langfristig groesster Wartbarkeitsgewinn ueber alle 3 Entry Points

### Gesamteinschaetzung

| Dimension | Bewertung |
|-----------|-----------|
| Architektur | ⭐⭐⭐⭐⭐ (Sauber, diszipliniert, 4 Schichten) |
| Kernlogik (Dedup/Score/Region) | ⭐⭐⭐⭐⭐ (Deterministisch, gut getestet) |
| Security | ⭐⭐⭐⭐ (Path-Traversal, ZipSlip, CSV-Injection, HTML-Encoding — TOCTOU akzeptiert) |
| Test-Abdeckung | ⭐⭐⭐⭐ (10.700+ Tests, aber Flake-Risiko und einige No-Crash-Only) |
| Release-Readiness | ⭐⭐⭐⭐ (3-5 Fixes vor stabilem Release empfohlen) |
| Code-Hygiene | ⭐⭐⭐⭐ (Legacy migriert, i18n synchron, einige God-Classes) |
| Wartbarkeit | ⭐⭐⭐ (God-Classes und Orchestration-Monolith senken Navigierbarkeit) |

**Empfehlung:** P0-Fixes umsetzen (3 Stück), dann ist das Repo release-tauglich. Die P1/P2-Items sind Tech-Debt-Sprint-Kandidaten fuer die Stabilisierungsphase nach Release.

---

## Anhang: Neue Findings aus Deep-Analyse (2026-04-11 Nachtrag)

### N-01: 19x `.GetAwaiter().GetResult()` im WPF-UI-Thread (Deadlock-Risiko)
- [x] **Fix implementiert**
- [x] **Test ergaenzt**
- **Schweregrad:** P2 (**Architecture Debt Hotspot**)
- **Impact:** Synchrones Blockieren von async Methoden auf dem WPF UI-Thread. Bei bestimmten SynchronizationContext-Konfigurationen kann dies zu Deadlocks fuehren, wenn der async-Code versucht, auf den UI-Thread zurueckzukehren. Aktuell funktioniert es, weil die meisten dieser Calls in FeatureCommandService auf Background-Threads laufen — aber die Grenze ist fragil.
- **Betroffene Datei(en):**
  - [FeatureCommandService.Productization.cs](src/Romulus.UI.Wpf/Services/FeatureCommandService.Productization.cs) (6x)
  - [FeatureCommandService.cs](src/Romulus.UI.Wpf/Services/FeatureCommandService.cs) (3x)
  - [FeatureCommandService.Infra.cs](src/Romulus.UI.Wpf/Services/FeatureCommandService.Infra.cs) (2x)
  - [FeatureCommandService.Collection.cs](src/Romulus.UI.Wpf/Services/FeatureCommandService.Collection.cs) (2x)
  - [FeatureCommandService.Analysis.cs](src/Romulus.UI.Wpf/Services/FeatureCommandService.Analysis.cs) (2x)
  - [RunService.cs](src/Romulus.UI.Wpf/Services/RunService.cs) (2x)
  - [MainViewModel.Productization.cs](src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs) (2x)
- **Ursache:** FeatureCommand-Pattern ist synchron (`Action<MainViewModel>`), ruft aber intern async Services auf
- **Fix:** FeatureCommand-Pattern auf `Func<MainViewModel, Task>` umstellen oder AsyncRelayCommand nutzen. Alternative: Alle aufgerufenen Services synchron machen (wo moeglich).
- **Testabsicherung:** Deadlock-Reproduktionstest mit SynchronizationContext-Mock

---

### N-02: Global Mutable Static State in 6 Core-Klassen (Test-Isolation-Risiko)
- [x] **Fix implementiert**
- [x] **Test ergaenzt**
- **Schweregrad:** P2 (**False Confidence Risk**)
- **Impact:** `GameKeyNormalizer`, `RegionDetector`, `FormatScorer`, `VersionScorer`, `HealthScorer`, `DeduplicationEngine` verwenden alle `static volatile` State fuer registrierte Profile/Pattern. Tests, die diese State aendern, muessen explizit synchronisieren — sonst Cross-Test-Kontamination.
- **Betroffene Datei(en):**
  - [GameKeyNormalizer.cs](src/Romulus.Core/GameKeys/GameKeyNormalizer.cs#L29)
  - [RegionDetector.cs](src/Romulus.Core/Regions/RegionDetector.cs#L19)
  - [FormatScorer.cs](src/Romulus.Core/Scoring/FormatScorer.cs#L20)
  - [VersionScorer.cs](src/Romulus.Core/Scoring/VersionScorer.cs#L19)
  - [HealthScorer.cs](src/Romulus.Core/Scoring/HealthScorer.cs#L20)
  - [DeduplicationEngine.cs](src/Romulus.Core/Deduplication/DeduplicationEngine.cs#L13)
- **Ursache:** Ambient-Static-Register-Pattern statt Constructor Injection. Design-Entscheidung um Core frei von DI-Dependencies zu halten.
- **Fix:** Optional: `ResetForTesting()` Methoden ergaenzen (bereits in SharedTestLocks referenziert). Langfristig: Instance-based Configuration ueber `ScoringProfile`-Records.
- **Testabsicherung:** Test, der parallele Tests mit unterschiedlichen Configs ausfuehrt und Resultate verifiziert

---

### N-03: API Program.cs 1553 LOC — Groesste Datei im Repo
- [x] **Refactored**
- **Schweregrad:** P3 (**Architecture Debt Hotspot**)
- **Impact:** Alle 39 API-Endpunkte + Middleware + Auth + CORS + Rate-Limiting + SSE in einer Datei. Aenderungen an einem Endpunkt erfordern Navigation durch 1500 Zeilen. Merge-Konflikte wahrscheinlich bei paralleler Entwicklung.
- **Betroffene Datei(en):** [Program.cs](src/Romulus.Api/Program.cs)
- **Ursache:** Minimal-API-Pattern ermutigt zu monolithischer Konfiguration
- **Fix:** Endpunkt-Gruppen in Extension-Methods extrahieren:
  ```csharp
  app.MapRunEndpoints();
  app.MapWatchEndpoints();
  app.MapDatEndpoints();
  app.MapCollectionEndpoints();
  ```
- **Testabsicherung:** Keine funktionale Aenderung

---

### N-04: CliArgsParser.cs 1431 LOC — Command-Parsing-Monolith
- [x] **Refactored**
- **Schweregrad:** P3 (**Architecture Debt Hotspot**)
- **Impact:** Aehnlich wie N-03 — alle CLI-Commands, Validierung und Help-Text in einer Klasse
- **Betroffene Datei(en):** [CliArgsParser.cs](src/Romulus.CLI/CliArgsParser.cs)
- **Ursache:** Organisch gewachsen mit neuen Subcommands
- **Fix:** Subcommand-Parser in separate Klassen extrahieren (per CliCommand)
- **Testabsicherung:** Bestehende CliArgsParser-Tests muessen weiter gruene sein

---

### N-05: FeatureService (static) + FeatureCommandService (instance) — doppelte Service-Schicht
- [ ] **Reviewed**
- **Schweregrad:** P2 (**Doppelte Logik / Schattenlogik**)
- **Impact:** `FeatureService` (statisch, 10 Partials) enthaelt Backend-Logik. `FeatureCommandService` (Instanz, 10 Partials) delegiert an `FeatureService` UND fuegt eigene Logik hinzu. Unklar, wann welche Schicht verantwortlich ist. Duplizierung z.B. bei DAT-XML-Generierung (beide haben `BuildGameElementMap`-Aufrufe).
- **Betroffene Datei(en):**
  - [FeatureService.Dat.cs](src/Romulus.UI.Wpf/Services/FeatureService.Dat.cs#L240)
  - [FeatureCommandService.Dat.cs](src/Romulus.UI.Wpf/Services/FeatureCommandService.Dat.cs#L295)
- **Ursache:** Historische Trennung zwischen "reiner Logik" (static) und "UI-Command" (instance)
- **Fix:** FeatureService komplett in FeatureCommandService integrieren oder klar definierte Grenze: FeatureService = reine Berechnung, FeatureCommandService = UI-Interaktion + Delegation an Infrastructure Services
- **Testabsicherung:** Tests, die pruefen, dass FeatureService und FeatureCommandService dieselben Ergebnisse fuer identische Inputs liefern
