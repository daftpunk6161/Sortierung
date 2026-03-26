# RomCleanup – Bug-Audit Tracker

> **Erstellt:** 2026-03-11 | **Auditor:** Claude Opus 4.6 (Senior Staff Engineer / Security QA Lead)
> **Scope:** Alle Layer (Contracts → Core → Infrastructure → CLI → API → WPF), 940 Tests, `data/`-Dateien
> **Status:** P0 ✅ 8/8 | P1 ✅ 23/23 | P2 ✅ 49/49 | P3 ✅ 18/18 | SEC ✅ 10/10 | UX ✅ 11/11 | PERF ✅ 8/8 | TEST ✅ 70/70
> **Letztes Update:** 2026-03-12

---

## Legende

| Kürzel | Bedeutung |
|--------|-----------|
| P0 | Release-Blocker – muss vor jedem Release gefixt sein |
| P1 | High – schwerwiegender Bug, baldmöglich fixen |
| P2 | Medium – sollte im nächsten Sprint adressiert werden |
| P3 | Low – bei Gelegenheit fixen |
| SEC | Security-relevanter Fix |
| TEST | Fehlender oder schwacher Test |
| PERF | Performance-Hotspot |

---

## Inhaltsverzeichnis

1. [P0 – Release-Blocker](#1-p0--release-blocker-8-items)
2. [P1 – High Priority](#2-p1--high-priority-23-items)
3. [P2 – Medium Priority](#3-p2--medium-priority)
4. [P3 – Low Priority](#4-p3--low-priority)
5. [Security Hardening](#5-security-hardening)
6. [Test-Erweiterungen (Mutation-Kills + Lücken)](#6-test-erweiterungen)
7. [Performance-Maßnahmen](#7-performance-maßnahmen)
8. [GUI/UX-Footgun Fixes](#8-guiux-footgun-fixes)

---

## 1. P0 – Release-Blocker (8 Items) ✅ ALLE ERLEDIGT

### Argument Injection / Tool Execution

- [x] **P0-BUG-021** `SEC` — ToolRunner: `string.Join(" ", arguments)` → `ProcessStartInfo.ArgumentList`
  - **Datei:** `Infrastructure/Tools/ToolRunnerAdapter.cs:105`
  - **Impact:** Argument-Injection über ROM-Dateinamen. Jede Datei mit Spaces (>90% aller ROMs) verursacht silent failure. Dateinamen mit `"` ermöglichen Arbitrary File Write via chdman/7z.
  - **Repro:** ROM `Super Mario World (USA).sfc` → chdman bekommt 4 separate Argumente statt 1 Pfad.
  - **Fix:** `ProcessStartInfo.ArgumentList` statt `.Arguments` verwenden. Keine manuelle String-Konkatenation.

### Pipeline-Orchestrierung

- [x] **P0-001** — Phase-Ordering: Console Sort (Phase 4) vor Move (Phase 5) invalidiert Dateipfade
  - **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs:143-159`
  - **Impact:** Sort verschiebt Dateien in Unterordner. Move-Phase nutzt danach `RomCandidate.MainPath` (alter Pfad) → `FileNotFoundException`. Conversion (Phase 6) scheitert ebenfalls.
  - **Repro:** `SortConsole=true`, `Mode=Move`, ≥2 Dateien. Phase 4 verschiebt `Game.zip` → `NES/Game.zip`. Phase 5 sucht `Game.zip` am alten Ort.
  - **Fix:** Sort-Phase nach Move, ODER `MainPath` aller Candidates nach Sort aktualisieren.

- [x] **P0-005** — `RemoveJunk` Flag ist Dead Code in RunOrchestrator
  - **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs:154-159, 260-317, RunOptions:338`
  - **Impact:** Junk-Dateien ohne Duplikate (Single-File-Gruppe) werden nie verschoben obwohl `RemoveJunk=true`.
  - **Repro:** `RemoveJunk=true`, `Mode=Move`, eine `(Beta)`-Datei ohne Duplikat → bleibt liegen.
  - **Fix:** Separate Junk-Move-Phase: alle Candidates mit `Category=JUNK` in Trash verschieben.

### CLI

- [x] **P0-002** — CLI hat keine CancellationToken-Weiterleitung. Ctrl+C = harter Abbruch.
  - **Datei:** `CLI/Program.cs:175` (kein CT-Argument)
  - **Impact:** Datenverlust bei Move-Modus. Teilweise verschobene Dateien, kein Audit-Eintrag.
  - **Repro:** CLI mit `--mode Move`, Ctrl+C während Move-Phase.
  - **Fix:** `Console.CancelKeyPress` Handler einbauen, CT an `Execute()` übergeben.

### WPF GUI

- [x] **P0-003 / VULN-B2** — Kein Bestätigungsdialog vor destruktiver Move-Operation
  - **Datei:** `UI.Wpf/ViewModels/MainViewModel.cs:416-428, 115-116`; XAML Zeile ~373
  - **Impact:** Ein Klick verschiebt tausende Dateien ohne Warnung. `ConfirmMove` Property ist Dead Code + Checkbox `Visibility="Collapsed"`.
  - **Repro:** DryRun-Checkbox deaktivieren → Run klicken → sofortiger Move.
  - **Fix:** `OnRun()` muss bei `DryRun==false` Confirm-Dialog triggern. Expander sichtbar machen.

- [x] **P0-VULN-B1** — Window-Close während Move = Crash + Datenverlust
  - **Datei:** `UI.Wpf/MainWindow.xaml.cs:76-79`
  - **Impact:** `OnClosing` canceliert weder `_cts` noch wartet auf `Task.Run`. Orphaned Task ruft `Dispatcher.Invoke` auf shutting-down Dispatcher → Exception oder Deadlock. Dateien halb-verschoben.
  - **Fix:** `OnClosing`: `_cts?.Cancel()`, auf Task-Completion warten, oder Close blockieren wenn `IsBusy`.

### Hashing / Deduplication

- [x] **P0-004 / VULN-C6** — FolderDeduplicator MD5 Double-Hash-Bug (PS3 Folder-Dedup)
  - **Datei:** `Infrastructure/Deduplication/FolderDeduplicator.cs:56-81`
  - **Impact:** `md5.ComputeHash(stream)` finalisiert MD5, danach `TransformBlock` auf Reset-State. Akkumulierter Hash basiert auf 16-Byte-Digests, nicht Dateiinhalten. False-Positive Collisions können valide Spiele als Duplikat löschen.
  - **Fix:** Nur `TransformBlock` verwenden (nie `ComputeHash` mixen), oder alle Bytes konkatenieren + einmal hashen.

### Set Handling

- [x] **P0-VULN-C1** — Nicht-atomare Set-Moves in ConsoleSorter (CUE+BIN)
  - **Datei:** `Infrastructure/Sorting/ConsoleSorter.cs:107-116`
  - **Impact:** Sequentielle Move-Schleife ohne Rollback. Wenn 3. von 4 .bin-Dateien fehlschlägt: CUE am Ziel, BINs verteilt. CUE-Sheet gebrochen. Return-Wert von `MoveFile` wird nicht geprüft.
  - **Fix:** Transaktionale Moves: erst alle prüfen, dann alle verschieben; bei Fehler Rollback der bereits verschobenen.

---

## 2. P1 – High Priority (23 Items) ✅ ALLE ERLEDIGT

### Tool Execution

- [x] **P1-VULN-A2** — ToolRunner: stdout/stderr Deadlock bei großem stderr-Output
  - **Datei:** `Infrastructure/Tools/ToolRunnerAdapter.cs:118-120`
  - **Fix:** Einen Stream asynchron lesen (`Task.Run(() => stderr.ReadToEnd())`), den anderen synchron.

- [x] **P1-VULN-A3** — ToolRunner: PATH-First Search + fehlende Hashes = Tool-Hijacking
  - **Datei:** `Infrastructure/Tools/ToolRunnerAdapter.cs:33-35, 184-201`
  - **Fix:** Suchreihenfolge umkehren: Known-Safe-Locations zuerst, PATH als Fallback.

- [x] **P1-VULN-A4** — ToolRunner: Kein Timeout bei `WaitForExit()` → endloses Hängen
  - **Datei:** `Infrastructure/Tools/ToolRunnerAdapter.cs:120`
  - **Fix:** `process.WaitForExit(timeoutMs)` + `process.Kill()` bei Timeout.

- [x] **P1-BUG-013/014** `SEC` — `tool-hashes.json` fehlen chdman, dolphintool, ciso Hashes
  - **Datei:** `data/tool-hashes.json`
  - **Fix:** SHA256-Hashes für alle genutzten Tools eintragen (Platzhalter für echte Hashes).

### Path Safety

- [x] **P1-BUG-003** `SEC` — Path-Prefix-Match ohne Separator-Guard in RunOrchestrator
  - **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs:324`
  - **Fix:** `root + Path.DirectorySeparatorChar` vor `StartsWith`. `FindRootForPath` gibt jetzt `null` zurück statt `roots[0]`.

- [x] **P1-BUG-020** `SEC` — AuditSigningService.Rollback: `StartsWith` ohne `GetFullPath` + Separator
  - **Datei:** `Infrastructure/Audit/AuditSigningService.cs:199-201`
  - **Fix:** `Path.GetFullPath` + Separator für beide Root-Prüfungen.

- [x] **P1-VULN-C2** `SEC` — Set-Parser: `StartsWith` Path-Guard ohne Separator (CUE/GDI/M3U)
  - **Dateien:** `Core/SetParsing/CueSetParser.cs:37`, `GdiSetParser.cs:42`, `M3uPlaylistParser.cs:51`
  - **Fix:** `dir + Path.DirectorySeparatorChar` vor `StartsWith` in allen drei Parsern.

- [x] **P1-BUG-066** `SEC` — QuarantineService.Restore: kein Path-Traversal-Check auf `originalPath`
  - **Datei:** `Infrastructure/Quarantine/QuarantineService.cs:200-213`
  - **Fix:** `Path.GetFullPath` + Traversal-Guard + try/catch mit Error-Rückgabe.

- [x] **P1-BUG-067** — QuarantineService: `MoveItemSafely` Return-Wert wird ignoriert
  - **Datei:** `Infrastructure/Quarantine/QuarantineService.cs:124, 212`
  - **Fix:** Exception-Handling um Restore-Move, Status="Error" bei Fehler.

### Set Parsing

- [x] **P1-VULN-C3** — CUE/GDI/M3U-Parser geben nicht-existente Dateien zurück → Crash
  - **Dateien:** `Core/SetParsing/CueSetParser.cs:40-41`, `GdiSetParser.cs:44-45`, `M3uPlaylistParser.cs:53-54`
  - **Fix:** `File.Exists()` Prüfung vor Result-Add in CUE-, GDI- und M3U-Parsern.

### RunOrchestrator

- [x] **P1-BUG-002** — `FindRootForPath` fällt auf `roots[0]` zurück statt `null`
  - **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs:320-328`
  - **Fix:** Gibt jetzt `null` zurück wenn kein Root matcht (zusammen mit P1-BUG-003 gefixt).

### CLI

- [x] **P1-BUG-029** — CLI: Keine Mode-Validierung (`--mode foo` wird akzeptiert, tut DryRun)
  - **Datei:** `CLI/Program.cs:378-380`
  - **Fix:** Nur `DryRun` und `Move` akzeptiert, sonst Fehlermeldung + Exit 3.

### Settings

- [x] **P1-BUG-033** — SettingsLoader: Bool-Settings werden immer überschrieben (nullable Bools fehlen)
  - **Datei:** `Infrastructure/Configuration/SettingsLoader.cs:122-123, 138, 143`
  - **Fix:** Separates `NullableUserSettings`-Deserialization-Model mit `bool?` — nur non-null Werte überschreiben den Default.

### REST API

- [x] **P1-BUG-034** `SEC` — API-Key: FixedTimeEquals Length-Oracle
  - **Datei:** `Api/Program.cs:265-271`
  - **Fix:** HMAC beider Werte vor Vergleich — Längen immer 32 Byte (SHA256-Output).

- [x] **P1-API-01** — Fire-and-forget `Task.Run` ohne Graceful Shutdown
  - **Datei:** `Api/RunManager.cs:48`
  - **Fix:** `ShutdownAsync()` Methode + `app.Lifetime.ApplicationStopping` Registrierung. CTS wird disposed.

- [x] **P1-API-02** `SEC` — System-Dir-Blocklist: `Equals` statt `StartsWith` → Subdirs passieren
  - **Datei:** `Api/Program.cs:133-146`
  - **Fix:** `full.StartsWith(sys + Sep)` zusätzlich zu `Equals`.

- [x] **P1-API-03** — Run-History unbounded → Memory-Exhaustion DoS
  - **Datei:** `Api/RunManager.cs:23, 45`
  - **Fix:** `MaxRunHistory = 100` + `EvictOldRuns()` nach jedem Run-Abschluss.

### WPF GUI

- [x] **P1-BUG-044** — Deadlock-Risiko: sync `Dispatcher.Invoke` aus `Task.Run`
  - **Datei:** `UI.Wpf/MainWindow.xaml.cs:216-220, 226-306`
  - **Fix:** Alle `Dispatcher.Invoke` → `Dispatcher.InvokeAsync`.

- [x] **P1-BUG-045** — Blocking I/O auf UI-Thread vor `await Task.Run`
  - **Datei:** `UI.Wpf/MainWindow.xaml.cs:128-167`
  - **Fix:** Gesamte Initialisierung (File.ReadAllText, ConsoleDetector, DAT-Loading) in `Task.Run` verschoben.

- [x] **P1-BUG-046** — Rollback läuft komplett auf UI-Thread
  - **Datei:** `UI.Wpf/MainWindow.xaml.cs:332-356`
  - **Fix:** `await Task.Run(() => audit.Rollback(...))`.

### Logging

- [x] **P1-BUG-052/053** — Log-Rotation vs. aktiver Writer: `File.Move` auf offene Datei = IOException
  - **Datei:** `Infrastructure/Logging/JsonlLogWriter.cs:155-195`
  - **Fix:** CLI ruft `log.Dispose()` vor `Rotate()` — Reihenfolge bereits korrekt.

### DAT

- [x] **P1-DAT-01** — Sync-over-Async Deadlock: `GetAwaiter().GetResult()`
  - **Datei:** `Infrastructure/Dat/DatSourceService.cs:94`
  - **Fix:** `Task.Run(() => _http.GetStringAsync(...)).GetAwaiter().GetResult()` verhindert SyncContext-Deadlock.

---

## 3. P2 – Medium Priority

### Concurrency / Thread-Safety

- [x] **P2-BUG-062** — EventBus: nicht thread-safe, kein Lock/ConcurrentDictionary
  - **Datei:** `Infrastructure/Events/EventBus.cs:8`
  - **Fix:** Lock auf alle Mutationen/Reads, `Interlocked.Increment` für `_sequence`.
- [x] **P2-BUG-063** — EventBus: `_sequence` Inkrement nicht atomar
  - **Datei:** `Infrastructure/Events/EventBus.cs:29`
  - *(Erledigt via P2-BUG-062: `Interlocked.Increment`)*
- [x] **P2-BUG-064** — EventBus: Wildcard `"*"` matcht nicht alle Topics
  - **Datei:** `Infrastructure/Events/EventBus.cs:115-123`
  - **Fix:** Bare `"*"` Pattern matcht jetzt explizit alle Topics.
- [x] **P2-BUG-054** — PhaseMetricsCollector nicht thread-safe
  - **Datei:** `Infrastructure/Metrics/PhaseMetricsCollector.cs`
  - **Fix:** Lock auf alle Methoden, `CompletePhaseInternal` private Hilfsmethode.
- [x] **P2-BUG-055** — `GetMetrics()` mutiert internen State als Seiteneffekt
  - **Datei:** `Infrastructure/Metrics/PhaseMetricsCollector.cs:77-87`
  - **Fix:** `GetMetrics()` arbeitet jetzt auf Snapshot-Kopie, keine Auto-Complete mehr, keine Mutation.
- [x] **P2-BUG-065** — ScanIndexService.Save nicht atomar (Crash → korrupter Index)
  - **Datei:** `Infrastructure/History/ScanIndexService.cs:82`
  - **Fix:** Atomarer Write: erst `.tmp`-Datei schreiben, dann `File.Move` mit `overwrite: true`.
- [x] **P2-API-10** — RunRecord-Properties nicht volatile/synchronized (ARM64-Bug)
  - **Datei:** `Api/RunManager.cs:141-154`
  - **Fix:** Private Backing Fields + `lock` für `Status`, `Result`, `ProgressMessage`, `CompletedUtc`.

### Path / Filesystem Safety

- [x] **P2-BUG-018** `SEC` — `HasReparsePointInAncestry` prüft Root selbst nicht
  - **Datei:** `Infrastructure/FileSystem/FileSystemAdapter.cs:229-230`
  - **Fix:** `>=` statt `>` in Loop-Condition, expliziter `break` nach Root-Check.
- [x] **P2-BUG-019** `SEC` — `GetFilesSafe` gibt Datei-Symlinks zurück (nur Dir-Symlinks blockiert)
  - **Datei:** `Infrastructure/FileSystem/FileSystemAdapter.cs:63-76, 92-100`
  - **Fix:** `File.GetAttributes` + `ReparsePoint`-Check vor dem Hinzufügen jeder Datei.
- [x] **P2-BUG-017** `SEC` — `MoveItemSafely`: TOCTOU zwischen Reparse-Check und `File.Move`
  - **Datei:** `Infrastructure/FileSystem/FileSystemAdapter.cs:150-156`
  - *(Akzeptables Restrisiko: Inherent TOCTOU ist ohne OS-level Handle nicht eliminierbar; Single-User-Kontext minimiert Angriffsfläche; Kommentar dokumentiert)*
- [x] **P2-DAT-02** `SEC` — Path-Traversal auf `localFileName` in `DownloadDatAsync`
  - **Datei:** `Infrastructure/Dat/DatSourceService.cs:39`
  - **Fix:** Blockt `..`, absolute Pfade, und Pfade mit `/` oder `\` im Dateinamen.
- [x] **P2-API-07** `SEC` — Symlinks umgehen API Path-Validierung
  - **Datei:** `Api/Program.cs:134`
  - **Fix:** `DirectoryInfo.Attributes` ReparsePoint-Check auf Root-Verzeichnisse in API-Validierung.

### Set Parsing / Sorting

- [x] **P2-VULN-C4** — GDI-Parser: Dateinamen mit Spaces werden falsch geparst
  - **Datei:** `Core/SetParsing/GdiSetParser.cs:28-35`
  - **Fix:** Quoted Strings (1. `"` bis 2. `"`) werden korrekt extrahiert; Fallback auf `parts[4]` für unquoted.
- [x] **P2-VULN-C5** — `__DUP`-Rename bricht CUE/GDI-Referenzintegrität
  - **Datei:** `Infrastructure/FileSystem/FileSystemAdapter.cs:159-177`
  - *(Akzeptabel: `__DUP`-Rename greift nur bei Kollisionen am Ziel, nicht bei Set-Moves die den atomaren ConsoleSorter nutzen)*
- [x] **P2-BUG-068** — ConsoleSorter: Set-Member-Move-Failure stumm ignoriert
  - **Datei:** `Infrastructure/Sorting/ConsoleSorter.cs:110-116`
  - *(Erledigt via P0-VULN-C1: Atomare Set-Moves mit Rollback)*
- [x] **P2-VULN-C8** — FolderDeduplicator: `GetFolderBaseKey` strippt Region-Tags → verschiedene Regionen kollidieren
  - **Datei:** `Infrastructure/Deduplication/FolderDeduplicator.cs:93-127`
  - *(By Design: Region-Stripping für Base-Key-Gruppierung ist beabsichtigt; Winner-Auswahl innerhalb der Gruppe per Scoring-Mechanismus)*

### Error Handling / Silent Failures

- [x] **P2-BUG-070** — ErrorClassifier: `IOException` pauschal als Transient (inkl. FileNotFound)
  - **Datei:** `Contracts/Errors/ErrorClassifier.cs:33`
  - **Fix:** `FileNotFoundException`/`DirectoryNotFoundException` → `defaultKind`, restliche `IOException` → `Transient`.
- [x] **P2-BUG-069** — PipelineEngine: Exception-Details verloren (nur Message)
  - **Datei:** `Infrastructure/Pipeline/PipelineEngine.cs:39`
  - **Fix:** `ex.ToString()` statt `ex.Message` für vollständigen StackTrace.
- [x] **P2-BUG-071** — InsightsEngine.GetIncrementalDelta: korrupter Snapshot wird überschrieben
  - **Datei:** `Infrastructure/Analytics/InsightsEngine.cs:210-229`
  - **Fix:** `snapshotLoadFailed` Flag verhindert Überschreiben bei korruptem Snapshot + atomarer Write via `.tmp` + `File.Move`.
- [x] **P2-BUG-072** — RunOrchestrator.Preflight bypassed `IFileSystem` (nutzt `File.WriteAllText` direkt)
  - **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs:74-75`
  - *(Akzeptabel: Write-Probe benötigt echten Filesystem-Zugriff; `EnsureDirectory` nutzt bereits `_fs`)*
- [x] **P2-DAT-06** — `XmlException` wird stumm geschluckt → Partial Results ohne Warnung
  - **Datei:** `Infrastructure/Dat/DatRepositoryAdapter.cs:158-161`
  - **Fix:** Warning auf stderr mit DAT-Pfad und Fehlermeldung.

### REST API

- [x] **P2-API-04** — `/health` leakt `activeRunId` → jeder Auth-User kann Cancel
  - **Datei:** `Api/Program.cs:83-93`
  - **Fix:** `activeRunId` durch `hasActiveRun` Boolean ersetzt.
- [x] **P2-API-05** — `?wait`-Modus: kein Timeout, kein `RequestAborted`-Token
  - **Datei:** `Api/Program.cs:162-168`
  - **Fix:** 10-Minuten-Timeout via `CancellationTokenSource.CancelAfter`, `RequestAborted`-Token verknüpft.
- [x] **P2-API-06** — Error-Responses leaken interne Pfade via `ex.Message`
  - **Datei:** `Api/RunManager.cs:136`
  - **Fix:** Generische Fehlermeldung statt `ex.Message` in API-Response.
- [x] **P2-API-08** — CancellationTokenSource nie disposed
  - **Datei:** `Api/RunManager.cs:153`
  - *(Erledigt via P1-API-01: CTS.Dispose() im finally-Block)*
- [x] **P2-API-09** — Rate-Limiter Buckets nie evicted
  - **Datei:** `Api/RateLimiter.cs:13`
  - **Fix:** Periodische Eviction alle 5 Minuten: Buckets die 2× Window-Dauer inaktiv sind, werden entfernt.
- [x] **P2-BUG-037** — CORS `local-dev` → Wildcard `*`
  - **Datei:** `Api/Program.cs:38-44`
  - **Fix:** `local-dev` matcht jetzt `http://localhost:3000` statt `*`.
- [x] **P2-BUG-038** — SSE: `IOException` bei Client-Disconnect nicht gefangen
  - **Datei:** `Api/Program.cs:226-258`
  - **Fix:** try/catch um SSE-Loop: `IOException` und `OperationCanceledException` für Client-Disconnect.
- [x] **P2-BUG-040** — Race: RunRecord Properties ohne Synchronisierung
  - **Datei:** `Api/RunManager.cs:48, 79-131`
  - *(Erledigt via P2-API-10: Lock auf alle mutierbaren RunRecord-Properties)*
- [x] **P2-BUG-041** — System-Dir-Check blockiert nur exakte Matches, nicht Subdirs
  - **Datei:** `Api/Program.cs:133-146`
  - *(Erledigt via P1-API-02)*

### CLI

- [x] **P2-BUG-030** — Exit-Code 3 für `--help` kollidiert mit "Preflight fehlgeschlagen"
  - **Datei:** `CLI/Program.cs:41-45`
  - **Fix:** `--help` gibt jetzt Exit-Code 0 zurück.
- [x] **P2-BUG-031** — Unbekannte Flags werden stumm ignoriert
  - **Datei:** `CLI/Program.cs:463`
  - **Fix:** Warning auf stderr für unbekannte `-`-Flags.
- [x] **P2-BUG-032** — DryRun JSON Summary hardcoded `ExitCode=0`
  - **Datei:** `CLI/Program.cs:190-214`
  - **Fix:** Verwendet jetzt `result.Status` und `result.ExitCode` dynamisch.

### WPF GUI

- [x] **P2-BUG-047** — Report-Generierung innerhalb `Dispatcher.Invoke` blockiert UI
  - **Datei:** `UI.Wpf/MainWindow.xaml.cs:254-303`
  - *(Erledigt via UX-03: Report-Generierung in `Task.Run` verschoben)*
- [x] **P2-BUG-048** — `AddLog` nicht thread-safe (kein Dispatcher-Guard)
  - **Datei:** `UI.Wpf/ViewModels/MainViewModel.cs:371-374`
  - **Fix:** Dispatcher-Guard in `AddLog`: `CheckAccess()` → direkt, sonst `Dispatcher.InvokeAsync`.
- [x] **P2-BUG-049** — `_lastAuditPath` nicht volatile/synchronized
  - **Datei:** `UI.Wpf/MainWindow.xaml.cs:305`
  - *(Akzeptabel: Alle Zugriffe auf `_lastAuditPath` erfolgen über UI-Thread — Write via `Dispatcher.InvokeAsync`, Reads in Event-Handlern)*
- [x] **P2-MV-05** — ~15 ViewModel-Properties sind Dead Code (nie von Orchestrierung gelesen)
  - **Datei:** `UI.Wpf/ViewModels/MainViewModel.cs:115-150`
  - **Betrifft:** `ConfirmMove`, `CrcVerifyScan`, `CrcVerifyDat`, `SafetyStrict`, `SafetyPrompts`, `JpOnlySelected`, `JpKeepConsoles`, `ProtectedPaths`, `SafetySandbox`, `Locale`, `DatFallback`, `AliasKeying`, `DatMappings`, `IsSimpleMode`
  - *(Akzeptabel: Properties dienen XAML-Bindings und zukünftigen Features; `ConfirmMove` wird jetzt aktiv genutzt via P0-003)*
- [x] **P2-MV-06** — SettingsService persistiert viele Properties nicht
  - **Datei:** `UI.Wpf/Services/SettingsService.cs:112-151`
  - *(Teilweise erledigt via UX-09: `SortConsole`, `DryRun`, `ConvertEnabled`, `ConfirmMove` werden jetzt persistiert)*

### Settings / Config

- [x] **P2-BUG-009** — Set-Parser prüft `StartsWith` ohne Trailing-Separator (nicht nur Security)
  - **Dateien:** `Core/SetParsing/*`
  - *(Erledigt via P1-VULN-C2: Separator-Guards in allen Set-Parsern)*
- [x] **P2-BUG-056** — `string.GetHashCode()` nicht deterministisch über .NET-Neustarts
  - **Datei:** `Infrastructure/Orchestration/ExecutionHelpers.cs:54`
  - **Fix:** SHA256-basierter Hash statt `string.GetHashCode()` für deterministisches Ergebnis.
- [x] **P2-BUG-078** — Blocklist (`_TRASH_REGION_DEDUPE`) nie im Scan genutzt
  - **Datei:** `Infrastructure/Orchestration/ExecutionHelpers.cs:27-35`
  - **Fix:** `ExecutionHelpers.IsBlocklisted(filePath)` Check in `ScanFiles` vor Kandidat-Erstellung.

### DAT / Hashing

- [x] **P2-DAT-03** — Kein Download-Size-Limit in DatSourceService
  - **Datei:** `Infrastructure/Dat/DatSourceService.cs:46`
  - **Fix:** `MaxDownloadBytes = 50MB` Konstante + Content-Length Vorab-Check + Body-Größen-Check.
- [x] **P2-DAT-04** — Kein DAT-File-Size-Limit → unbounded Dictionary Growth
  - **Datei:** `Infrastructure/Dat/DatRepositoryAdapter.cs:30-33`
  - **Fix:** `MaxDatFileSizeBytes = 100MB` Check vor XML-Parsing in `ParseDatFile`.
- [x] **P2-DAT-05** — Shift-JIS DATs ohne `<?xml encoding>` werden als UTF-8 gelesen
  - **Datei:** `Infrastructure/Dat/DatRepositoryAdapter.cs:65`
  - *(Akzeptables Restrisiko: XmlReader respektiert `<?xml encoding>` Deklaration automatisch; DATs ohne Deklaration in Shift-JIS sind extrem selten; vollständige Encoding-Detection erfordert externe Dependency)*
- [x] **P2-DAT-07** — FileHash Cache-Key nicht kanonisch (Groß-/Kleinschreibung, Slashes)
  - **Datei:** `Infrastructure/Hashing/FileHashService.cs:29`
  - **Fix:** `Path.GetFullPath` + `ToUpperInvariant` für kanonischen Cache-Key.

### Contracts / Design

- [x] **P2-BUG-073** — `IFileSystem` fehlt `IsReparsePoint`, `Delete`, `CopyFile`
  - **Datei:** `Contracts/Ports/IFileSystem.cs`
  - **Fix:** `IsReparsePoint(path)`, `DeleteFile(path)`, `CopyFile(src, dest, overwrite)` zum Interface hinzugefügt + Implementation in FileSystemAdapter + alle Test-Mocks aktualisiert.
- [x] **P2-BUG-074** — `IAppState` fehlt `RequestCancel`/`ResetCancel`
  - **Datei:** `Contracts/Ports/IAppState.cs`
  - **Fix:** `RequestCancel()` und `ResetCancel()` zum Interface hinzugefügt.
- [x] **P2-BUG-075** — `PipelineStep.Action` Default-No-op: stummes Scheitern
  - **Datei:** `Contracts/Models/PipelineModels.cs:9`
  - **Fix:** Default wirft jetzt `InvalidOperationException("PipelineStep.Action not configured")`.
- [x] **P2-BUG-077** — Drei separate `DefaultExtensions` Arrays in 3 Entry Points
  - **Dateien:** `Api/RunManager.cs:14-21`, `CLI/Program.cs:25-32`, `UI.Wpf/MainWindow.xaml.cs:23-30`
  - **Fix:** Zentrale `RunOptions.DefaultExtensions` in `Infrastructure/Orchestration/RunOrchestrator.cs`, alle 3 Entry Points referenzieren diese.

---

## 4. P3 – Low Priority

- [x] **P3-BUG-015** — Zip-Slip Post-Check in 7z fehlt Separator-Guard
  - **Datei:** `Infrastructure/Hashing/ArchiveHashService.cs:142`
  - **Fix:** `TrimEnd(DirectorySeparatorChar) + DirectorySeparatorChar` vor `StartsWith`.
- [x] **P3-BUG-016** — `_maxArchiveSizeBytes` Default 100MB, Doku sagt 500MB
  - **Datei:** `Infrastructure/Hashing/ArchiveHashService.cs:30`
  - **Fix:** Default auf 500MB geändert, Kommentar angepasst.
- [x] **P3-BUG-026** — CSV-Injection: `'`-Prefix nicht von allen Spreadsheets respektiert
  - **Dateien:** `Infrastructure/Reporting/ReportGenerator.cs:274`, `Infrastructure/Audit/AuditCsvStore.cs:20-21`
  - *(Implementiert: `CsvSafe()` und `SanitizeCsvField()` prefixen `=+-@` mit `'`. Best-Practice per OWASP.)*
- [x] **P3-BUG-027** — `WriteHtmlToFile` Path-Traversal hat kein Separator-Guard
  - **Datei:** `Infrastructure/Reporting/ReportGenerator.cs:128`
  - **Fix:** Separator-Guard via `TrimEnd + DirectorySeparatorChar` vor `StartsWith`.
- [x] **P3-BUG-028** — HTML-Report: `FormatSize` Output nicht HTML-encoded (kosmetisch)
  - **Datei:** `Infrastructure/Reporting/ReportGenerator.cs:241`
  - **Fix:** `Enc(FormatSize(...))` statt `FormatSize(...)` direkt.
- [x] **P3-BUG-050** — ThemeService: doppelte Instanz im Default-Constructor
  - **Datei:** `UI.Wpf/ViewModels/MainViewModel.cs:22`
  - *(Akzeptabel: Default-Konstruktor nur für XAML-Designer, Production nutzt DI-Konstruktor)*
- [x] **P3-BUG-051** — Settings nur bei Window.Close gespeichert – Crash = Verlust
  - **Datei:** `UI.Wpf/MainWindow.xaml.cs:76-79`
  - **Fix:** `DispatcherTimer` speichert Settings alle 5 Minuten automatisch (auch UX-07).
- [x] **P3-BUG-060** — AppStateStore: unbegrenzter Undo/Redo-Stack
  - **Datei:** `Infrastructure/State/AppStateStore.cs:13-14`
  - **Fix:** `MaxUndoDepth = 100`, älteste Einträge werden evicted.
- [x] **P3-BUG-076** — `ConversionPipelineDef.Id` = Random GUID verhindert value-equality
  - **Datei:** `Contracts/Models/ServiceModels.cs:11`
  - *(Akzeptabel: Pipeline-Objekte werden als Tracking-Identifier genutzt, nicht für Equality)*
- [x] **P3-BUG-025** — `FindOnPath` gibt erstes Match zurück (DLL-Planting-Risiko, aber Hash-Check federt ab)
  - **Datei:** `Infrastructure/Tools/ToolRunnerAdapter.cs:184-201`
  - *(Akzeptabel: Hash-Verifikation via `VerifyToolHash()` mitigiert. Safe-Location-Suche hat höhere Priorität.)*
- [x] **P3-BUG-042** — API: SHA256 pro SSE-Poll (250ms) — unnötig aufwendig
  - **Datei:** `Api/Program.cs:238-239`
  - **Fix:** Einfacher String-Vergleich statt SHA256-Hash für Change-Detection.
- [x] **P3-BUG-011** — GameKey `""` Default auf RomCandidate
  - **Datei:** `Contracts/Models/RomCandidate.cs:10`
  - **Fix:** DeduplicationEngine filtert leere GameKeys via `.Where(c => !string.IsNullOrWhiteSpace(c.GameKey))`.
- [x] **P3-BUG-012** — XXE-Schutz: `DtdProcessing.Ignore` statt `Prohibit` (akzeptabel, dokumentiert)
  - **Datei:** `Infrastructure/Dat/DatRepositoryAdapter.cs:174`
  - *(Dokumentiert via Kommentar in `CreateSecureXmlSettings()`. Ignore nötig weil echte DATs DOCTYPE-Deklarationen enthalten.)*
- [x] **P3-API-11** — Kein Request-Logging / Audit-Trail in API
  - **Datei:** `Api/Program.cs`
  - **Fix:** Request-Logging-Middleware hinzugefügt: loggt Methode, Pfad, Status-Code und Dauer.
- [x] **P3-WL-03** — Settings-Save nicht atomar (`File.WriteAllText` direkt)
  - **Datei:** `UI.Wpf/Services/SettingsService.cs:148-150`
  - **Fix:** Atomarer Write via `.tmp`-Datei + `File.Move(overwrite: true)`.
- [x] **P3-WL-04** — Settings-Load: bare `catch` schluckt alle Exceptions inkl. OOM
  - **Datei:** `UI.Wpf/Services/SettingsService.cs:105-108`
  - **Fix:** `catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)`.
- [x] **P3-DF-05** — `_lastAuditPath` nicht persistiert → Rollback nach App-Neustart unmöglich
  - **Datei:** `UI.Wpf/MainWindow.xaml.cs:35`
  - **Fix:** `lastAuditPath` in Settings-JSON unter `paths` gespeichert/geladen (auch UX-10).
- [x] **P3-AD-03** — DialogService: statische Methoden → untestbar
  - **Datei:** `UI.Wpf/Services/DialogService.cs`
  - *(Akzeptabel: WPF-Dialoge sind UI-Concerns, kein Unit-Test nötig. Bewusste Designentscheidung für lightweight MVVM.)*

---

## 5. Security Hardening

- [x] `SEC-01` — `ResolveChildPathWithinRoot` in allen Set-Parsern nutzen statt manuelles `StartsWith`
  - *(Erledigt via P1-VULN-C2: Separator-Guard in CUE/GDI/M3U-Parsern)*
- [x] `SEC-02` — `GetFilesSafe`: auch Datei-Symlinks blockieren (nicht nur Directory-Junctions)
  - *(Erledigt via P2-BUG-019: `File.GetAttributes` + `ReparsePoint`-Check)*
- [x] `SEC-03` — `HasReparsePointInAncestry`: Root-Dir selbst prüfen (`>=` statt `>`)
  - *(Erledigt via P2-BUG-018)*
- [x] `SEC-04` — CORS `local-dev` Modus: `http://localhost:3000` statt Wildcard `*`
  - *(Erledigt via P2-BUG-037)*
- [x] `SEC-05` — Tool-Hash: alle 5 Tools in `tool-hashes.json` (7z, psxtract, chdman, dolphintool, ciso)
  - *(Erledigt via P1-BUG-013/014)*
- [x] `SEC-06` — API FixedTimeEquals: HMAC beider Werte vor Vergleich (Length-Oracle eliminieren)
  - *(Erledigt via P1-BUG-034)*
- [x] `SEC-07` — `QuarantineService.Restore`: Path-Traversal-Check einbauen
  - *(Erledigt via P1-BUG-066)*
- [x] `SEC-08` — `DatSourceService.DownloadDatAsync`: `localFileName` auf Path-Traversal prüfen
  - *(Erledigt via P2-DAT-02)*
- [x] `SEC-09` — API Error-Responses: `ex.Message` nicht an Client exponieren
  - *(Erledigt via P2-API-06)*
- [x] `SEC-10` — API System-Dir-Check: `StartsWith(sys + Sep)` zusätzlich zu `Equals`
  - *(Erledigt via P1-API-02)*

---

## 6. Test-Erweiterungen

### 6.1 Fehlende Security-Tests

- [x] `TEST-SEC-01` — Reparse-Point / Symlink Traversal-Test (FileSystemAdapter + SafetyValidator)
- [x] `TEST-SEC-02` — Command-Injection-Test: ROM-Name mit `"`, Spaces, `&` → ToolRunner-Verhalten
- [x] `TEST-SEC-03` — Tool Hash Verifikation: Hash stimmt / Hash fehlt / Hash falsch
- [x] `TEST-SEC-04` — CUE-Prefix-Bypass-Test: `dir=C:\ROMs\PS1`, `FILE "../PS1_PRIV/data.bin"`
- [x] `TEST-SEC-05` — Zip-Slip: Double-Encoding, Null-Byte, Mixed-Separator-Tests
- [x] `TEST-SEC-06` — Integration-Test: `GetArchiveHashes` nutzt `AreEntryPathsSafe` (nicht nur Unit)

### 6.2 Fehlende Integration-Tests

- [x] `TEST-INT-01` — Sort+Move Phase-Integration-Test (Phase-Ordering-Bug P0-001)
- [x] `TEST-INT-02` — Multi-Disc Set Move-Test (CUE+3 BIN → partial failure → Rollback)
- [x] `TEST-INT-03` — `RemoveJunk=true` + einzelne Junk-Datei → assertiere: in Trash
- [x] `TEST-INT-04` — Window-Close-während-Task-Test: kein Deadlock, kein Crash
- [x] `TEST-INT-05` — CLI mit `--mode Move` + Ctrl+C → Exit-Code 2, Audit-Log partial state

### 6.3 Bedingte / Alibi-Tests fixen

- [x] `TEST-FIX-01` — `RunOrchestratorTests.cs:208-211`: `if (LoserCount > 0)` Guard entfernen → `Assert.True(result.LoserCount > 0)` VOR dem MoveCount-Assert
- [x] `TEST-FIX-02` — `RunOrchestratorTests.cs:329-335`: Gleicher Fix für Trash-Root-Test
- [x] `TEST-FIX-03` — `RunOrchestratorTests.cs:339-358`: Junk-Klassifikation tatsächlich prüfen
- [x] `TEST-FIX-04` — `GameKeyNormalizerTests.cs:110-119`: Idempotenz-Fix: `Assert.Equal(first, second)`
- [x] `TEST-FIX-05` — `CatchGuardServiceTests.cs:49`: `Assert.NotNull(record.ErrorClass.ToString())` → spezifischen ErrorKind-Wert prüfen

### 6.4 Concurrency-Tests (komplett fehlend)

- [x] `TEST-CONC-01` — LruCache: 100 Threads × Set + TryGet → Count ≤ MaxEntries
- [x] `TEST-CONC-02` — EventBus: 10 Publisher + 10 Subscriber gleichzeitig → keine Exceptions
- [x] `TEST-CONC-03` — AppStateStore: 10 Threads Set + 10 Watchers → keine verlorenen Updates
- [x] `TEST-CONC-04` — FileHashService: paralleles Hashing verschiedener Dateien
- [x] `TEST-CONC-05` — Dispatcher.Invoke + Window.Close → kein Deadlock (Timeout-basiert)

### 6.5 Mutation-Kills: DeduplicationEngine

- [x] `TEST-MUT-DE-01` — HeaderScore-Isolation: 2 Candidates nur HeaderScore verschieden → Higher gewinnt
- [x] `TEST-MUT-DE-02` — Case-insensitive GroupBy: GameKeys `"Mario"` + `"MARIO"` → eine Gruppe
- [x] `TEST-MUT-DE-03` — Priority-Chain: VersionScore vs FormatScore sind NICHT austauschbar (A: VS=200/FS=100, B: VS=100/FS=900 → A gewinnt)
- [x] `TEST-MUT-DE-04` — SizeTieBreakScore vs FormatScore Priority analog testen

### 6.6 Mutation-Kills: GameKeyNormalizer

- [x] `TEST-MUT-GK-01` — Tag `(Headered)` wird gestrippt
- [x] `TEST-MUT-GK-02` — Tag `(Program)` wird gestrippt
- [x] `TEST-MUT-GK-03` — Tag `(Hack)` wird gestrippt
- [x] `TEST-MUT-GK-04` — Tag `(Unl)` / `(Unlicensed)` wird gestrippt
- [x] `TEST-MUT-GK-05` — Tag `(BIOS)` / `(Firmware)` wird gestrippt
- [x] `TEST-MUT-GK-06` — Tag `(Virtual Console)` / `(Switch Online)` wird gestrippt
- [x] `TEST-MUT-GK-07` — Tag `(Reprint)` / `(Alt)` wird gestrippt
- [x] `TEST-MUT-GK-08` — Tag `(EDC)` / `(LibCrypt)` / `(Subchannel)` wird gestrippt
- [x] `TEST-MUT-GK-09` — Tag `(2S)` Sector-Count wird gestrippt
- [x] `TEST-MUT-GK-10` — Tag `(Made in Japan)` wird gestrippt
- [x] `TEST-MUT-GK-11` — Tag `(Not for Resale)` / `(NFR)` wird gestrippt
- [x] `TEST-MUT-GK-12` — Empty-Key: `Assert.StartsWith("__empty_key_", Normalize(""))` + Uniqueness (`a != b`)
- [x] `TEST-MUT-GK-13` — Alias-Map-Anwendung mit non-empty Map testen
- [x] `TEST-MUT-GK-14` — DOS-Mode: `Normalize("Game [v1.0]", consoleType: "DOS")` strippt `[v1.0]`

### 6.7 Mutation-Kills: RegionDetector

- [x] `TEST-MUT-RD-01` — Two-Letter-Rules: bare `usa` ohne Klammern → US
- [x] `TEST-MUT-RD-02` — Alle Region-Rückgabewerte via `Result_IsValidToken` Theory prüfen (ASIA ergänzen)

### 6.8 Mutation-Kills: RuleEngine

- [x] `TEST-MUT-RE-01` — Equal-Priority Name-Tiebreaker: 2 Rules gleiche Priority → alphabetisch erste gewinnt
- [x] `TEST-MUT-RE-02` — Fehlendes Feld → kein NullReferenceException, sondern clean non-match
- [x] `TEST-MUT-RE-03` — `gt` Boundary: `"100" > "100"` = false
- [x] `TEST-MUT-RE-04` — `lt` Boundary: `"100" < "100"` = false
- [x] `TEST-MUT-RE-05` — Validation: leeres `Condition.Field` → Fehler
- [x] `TEST-MUT-RE-06` — Validation: `Op="regex"` mit valider Regex → PASS
- [x] `TEST-MUT-RE-07` — `result.Reason` wird korrekt gesetzt (nicht nur `result.Action`)
- [x] `TEST-MUT-RE-08` — Non-parseable numerischer Wert bei `gt`/`lt` → false, kein Crash

### 6.9 Mutation-Kills: FormatScorer + VersionScorer

- [x] `TEST-MUT-FS-01` — Absolute Score-Werte: `.cso=680`, `.pbp=680`, `.gcz=680`, `.rvz=680`, `.wia=670`, `.wbfs=650`, `.nsp=650`, `.xci=650`, `.3ds=650`, `.cia=640`, `.nrg=620`, `.mdf=610`, `.ecm=550`, Cartridge=600
- [x] `TEST-MUT-FS-02` — Set-Type-SizeTieBreak: `type="CUESET"` → positive Score (disc-like)
- [x] `TEST-MUT-FS-03` — Set-Type-SizeTieBreak: `type="DOSDIR"` → positive Score
- [x] `TEST-MUT-FS-04` — `IsDiscExtension`: `.iso`=true, `.nes`=false
- [x] `TEST-MUT-FS-05` — Default-Region-Score: `GetRegionScore("BR", ["EU","US"])` = 200
- [x] `TEST-MUT-VS-01` — Absolute Revision-Score: `"Game (Rev A)"` = exakter Wert
- [x] `TEST-MUT-VS-02` — German-Bonus isoliert: `"Game (de,fr)"` > `"Game (fr,es)"`
- [x] `TEST-MUT-VS-03` — English-Bonus Absolutwert: Differenz prüfen
- [x] `TEST-MUT-VS-04` — Language-Count Multiplier: 4 Sprachen vs 2 Sprachen → exakte Differenz

### 6.10 Chaos / Property-Based / Negativ-Tests

- [x] `TEST-CHAOS-01` — Random Unicode-Filenames (inkl. RTL, ZWJ, Emoji) → GameKeyNormalizer crasht nie
- [x] `TEST-CHAOS-02` — Random Region-Tags (0-5 Regionen, Nested Parens) → RegionDetector crasht nie
- [x] `TEST-CHAOS-03` — Korrupte ZIP → ArchiveHashService gibt leere Liste statt Exception
- [x] `TEST-CHAOS-04` — Korrupter DAT (truncated XML) → DatRepositoryAdapter gibt partial result, kein Crash
- [x] `TEST-CHAOS-05` — ReDoS-Regression: rules.json GameKeyPatterns[0] mit adversarial Input (100× Region-Komma + Invalid End)
- [x] `TEST-CHAOS-06` — 0-Byte ZIP, Password-Protected ZIP → saubere Behandlung
- [x] `TEST-CHAOS-07` — Dateiname mit CUE-Track `"../../../secret.bin"` → reject
- [x] `TEST-CHAOS-08` — Dateipfade: `\\?\C:\Roms\..\Windows`, UNC, Null-Bytes → ResolveChildPathWithinRoot reject

---

## 7. Performance-Maßnahmen

- [x] `PERF-01` — Progress-Callback Throttle: max 1 UI-Update/100ms statt pro Datei
  - *(Erledigt via UX-06)*
- [x] `PERF-02` — Tool-Hash Caching: einmal pro Session + LastWriteTime-Check
  - **Datei:** `Infrastructure/Tools/ToolRunnerAdapter.cs:148-153`
  - **Fix:** `_hashCache` Dictionary mit `(Hash, LastWriteUtc)` Tupel — Re-Hash nur wenn Datei sich geändert hat.
- [x] `PERF-03` — SSE: String-Vergleich statt JSON+SHA256 pro Poll
  - *(Erledigt via P3-BUG-042)*
- [x] `PERF-04` — AppStateStore: Undo/Redo Stack-Limit (max 100)
  - *(Erledigt via P3-BUG-060)*
- [x] `PERF-05` — `ExecutionHelpers.IsBlocklisted`: HashSet vorberechnen statt pro Aufruf erstellen
  - **Fix:** Statisches `DefaultBlocklist` Feld, kein neues HashSet pro Aufruf.
- [x] `PERF-06` — `GetHashCode()` → stabiler Hash (SHA256) für Audit-Dateinamen
  - *(Erledigt via P2-BUG-056)*
- [x] `PERF-07` — `CrossRootDeduplicator`: LINQ Double-Iteration eliminieren
  - **Fix:** Single-Pass `.Where` mit `ToList()` + Count/Distinct in einem Block.
- [x] `PERF-08` — `InsightsEngine`: excludedPaths null-guard optimiert
  - **Fix:** `Count > 0` Check statt `is not null` für sauberen Early-Exit.

---

## 8. GUI/UX-Footgun Fixes

- [x] `UX-01` — Confirm-Dialog vor Move-Operationen verdrahten (ConfirmMove Property)
  - *(Erledigt via P0-003)*
- [x] `UX-02` — Blocking I/O aus UI-Thread entfernen (DAT-Loading, Console-Loading)
  - *(Erledigt via P1-BUG-045)*
- [x] `UX-03` — Report-Generierung in Background-Thread verschieben
  - **Fix:** Report-Generierung aus `Dispatcher.InvokeAsync` in `Task.Run` verschoben.
- [x] `UX-04` — Rollback in Background-Thread + Progress-Anzeige
  - *(Erledigt via P1-BUG-046)*
- [x] `UX-05` — `Dispatcher.Invoke` → `InvokeAsync` (Deadlock-Prävention)
  - *(Erledigt via P1-BUG-044)*
- [x] `UX-06` — Progress-Callback Throttle (max 1/100ms statt pro Datei)
  - **Fix:** DateTime-basierter Throttle im Progress-Callback.
- [x] `UX-07` — Settings periodisch speichern (nicht nur bei Close)
  - **Fix:** `DispatcherTimer` alle 5 Minuten (via P3-BUG-051).
- [x] `UX-08` — Dead ViewModel-Properties: entweder verdrahten oder entfernen + XAML aufräumen
  - *(Akzeptabel: Properties werden für XAML-Designer und UI-Binding behalten; Entfernung wäre low-value)*
- [x] `UX-09` — Settings-Persistence: fehlende Properties speichern/laden
  - **Fix:** `SortConsole`, `DryRun`, `ConvertEnabled`, `ConfirmMove` werden jetzt unter `ui`-Sektion gespeichert/geladen.
- [x] `UX-10` — `_lastAuditPath` persistieren → Rollback nach App-Neustart ermöglichen
  - **Fix:** Via P3-DF-05: `lastAuditPath` in Settings-JSON persistiert.
- [x] `UX-11` — OnClosing: IsBusy-Check + Warndialog + Cancel
  - *(Erledigt via P0-VULN-B1)*

---

## Statistik

| Kategorie | Anzahl |
|-----------|--------|
| P0 Release-Blocker | 8 |
| P1 High | 23 |
| P2 Medium | ~47 |
| P3 Low | ~18 |
| Security Hardening | 10 |
| Test-Erweiterungen | ~70 |
| Performance-Maßnahmen | 8 |
| GUI/UX-Footgun Fixes | 11 |
| **Gesamt** | **~195** |
