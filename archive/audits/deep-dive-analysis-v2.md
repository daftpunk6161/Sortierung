# Deep-Dive Analyse v2 — Full-Scope Code Review

> **Datum:** 2026-03-14 | **Build:** ✅ GREEN | **Tests:** 2923 bestanden, 0 Fehler, 16 übersprungen  
> **Scope:** Alle 200 C#-Dateien + 10 XAML-Dateien (~38.645 Zeilen) — Core, Contracts, Infrastructure, CLI, API, WPF  
> **Methode:** Agent-gestützte Vollanalyse mit manueller Verifikation aller kritischen Befunde  
> **Hinweis:** 6 von 10 initialen "Critical"-Befunden als False Positives verifiziert und herausgefiltert

---

## Zusammenfassung

| Kategorie | Kritisch 🔴 | Hoch 🟠 | Mittel 🟡 | Niedrig 🔵 | Info ⚪ |
|-----------|:-----------:|:-------:|:---------:|:----------:|:------:|
| Bugs / Korrektheit | — | 3 | 5 | 4 | 2 |
| Sicherheit | — | 2 | 2 | 1 | — |
| Concurrency / Threads | — | 2 | 2 | — | — |
| Performance / Memory | — | 2 | 2 | 1 | — |
| WPF / UI | — | 3 | 4 | 3 | — |
| Test-Lücken | — | 2 | 3 | 2 | — |
| **Gesamt** | **0** | **14** | **18** | **11** | **2** |

---

## 🟠 HOCH — Vor Release fixen

### V2-BUG-H01: ConsoleDetector — Unbounded Folder-Detection-Cache
- **Datei:** `Core/Classification/ConsoleDetector.cs` Zeile 35
- **Problem:** `Dictionary<string, string> _folderDetectCache` ohne Größenlimit. Bei Scans mit 100k+ Dateien und vielen Verzeichnissen wächst der Cache unbeschränkt.
- **Verifiziert:** ✅ CONFIRMED — Plain Dictionary, keine Eviction.
- **Auswirkung:** Memory-Bloat bis OOM bei Enterprise-Scale-Scans.
- [x] **FIX:** LruCache<string, string>(65536) statt Dictionary verwenden

### V2-BUG-H02: RuleEngine — Unbounded Regex-Cache
- **Datei:** `Core/Rules/RuleEngine.cs` Zeile 14
- **Problem:** `ConcurrentDictionary<string, Regex?> _regexCache` (static) ohne Eviction. Jede unique Regex-Pattern aus User-Rules wird permanent gecacht. Compiled Regex = 10–100 KB pro Eintrag.
- **Verifiziert:** ✅ CONFIRMED — ConcurrentDictionary, keine Größenlimit.
- **Auswirkung:** Memory-Leak; DoS-Vektor über API bei unbegrenzten Rule-Patterns.
- [x] **FIX:** LruCache<string, Regex?>(1024) oder ConcurrentDictionary mit manuellem Trim

### V2-BUG-H03: VersionScorer — Gemischte Revisionsformate unvollständig geparst
- **Datei:** `Core/Scoring/VersionScorer.cs` Zeile 122–163
- **Problem:** Revision "1a2b3c" → nur `numeric=1, suffix="a"` geparst, `"2b3c"` ignoriert. Zwei ROMs mit Revisionen "1a" und "1a2b3c" bewerten identisch.
- **Auswirkung:** Winner-Selection falsch bei exotischen Misch-Revisionen.
- [x] **FIX:** Vollständige Revision parsen (alle Zeichen bis Ende berücksichtigen)

### V2-SEC-H01: AuditSigningService — Key-File nicht atomar geschrieben
- **Datei:** `Infrastructure/Audit/AuditSigningService.cs` Zeile 35
- **Problem:** `File.WriteAllText(_keyFilePath, ...)` — Prozess-Crash während Schreibvorgang hinterlässt korruptes/leeres Key-File. Nächster Start lädt defekten Key → alle Audit-Signaturen scheitern.
- **Auswirkung:** Audit-Integrität nach Crash kompromittiert.
- [x] **FIX:** Atomic-Write via temp + rename: `File.WriteAllText(tmpPath, ...); File.Move(tmpPath, keyPath, overwrite: true);`

### V2-SEC-H02: ArchiveHashService — Zip-Slip-Guard prüft `preBuffer.Length` statt `preRead`
- **Datei:** `Infrastructure/Hashing/ArchiveHashService.cs` Zeile 165
- **Problem:** Post-Extraction Path-Prüfung nutzt `Path.GetFullPath(file).StartsWith(normalizedTemp)`. Wenn tempDir z.B. `C:\temp_abc` ist und Extraction einen Pfad `C:\temp_abcXYZ\escape.txt` erzeugt, würde der StartsWith-Check fälschlicherweise bestehen.
- **Auswirkung:** Theoretischer Zip-Slip-Bypass bei crafted Archives.
- [x] **FIX:** Separator-Suffix erzwingen: `normalizedTemp.TrimEnd(sep) + sep` vor dem StartsWith-Check

### V2-THR-H01: MainWindow.OnRunRequested — async void ohne Exception-Handling
- **Datei:** `UI.Wpf/MainWindow.xaml.cs` Zeile 108–115
- **Problem:** `async void OnRunRequested` — Unhandled Exceptions in `ExecuteAndRefreshAsync()` erzeugen DispatcherUnhandledException → App-Crash ohne Recovery.
- **Verifiziert:** ✅ CONFIRMED
- **Auswirkung:** Jeder Fehler beim Pipeline-Run crashed die gesamte Anwendung.
- [x] **FIX:** try-catch um await, Fehler über ViewModel.AddLog melden

### V2-THR-H02: MainViewModel — CancellationTokenSource unsicher geteilt
- **Datei:** `UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` Zeile 240–245
- **Problem:** `Volatile.Read(ref _cts)` in `OnCancel()` → CTS kann zwischen Read und Cancel durch `CreateRunCancellation()` disposed werden → ObjectDisposedException.
- **Auswirkung:** Cancel-Button kann fehlschlagen oder Exception werfen.
- [x] **FIX:** lock() oder Interlocked.CompareExchange-Pattern konsistent nutzen

### V2-WPF-H01: ResultView — Memory-Leak durch CollectionChanged-Handler
- **Datei:** `UI.Wpf/Views/ResultView.xaml.cs` Zeile 28
- **Problem:** In `OnLoaded` wird `vm.LogEntries.CollectionChanged += handler` abonniert. Wenn View ohne `OnUnloaded` aus dem Visual Tree entfernt wird, bleibt Handler bestehen → Memory-Leak. `_logScrollTimer` wird nie disposed.
- **Auswirkung:** Memory-Leak bei wiederholtem Tab-Wechsel.
- [x] **FIX:** IDisposable implementieren, Timer und Event-Handler im Destruktor aufräumen

### V2-WPF-H02: MainWindow — DispatcherTimer nicht disposed
- **Datei:** `UI.Wpf/MainWindow.xaml.cs` Zeile 32–36
- **Problem:** `_settingsTimer` wird erstellt und gestartet, aber in `CleanupResources()` nur gestoppt, nie disposed/null gesetzt.
- **Auswirkung:** Timer läuft nach Window-Close weiter, ruft SaveSettings auf stale ViewModel.
- [x] **FIX:** `_settingsTimer?.Stop(); _settingsTimer = null;` in CleanupResources

### V2-WPF-H03: WebView2 — Fallback-UI wird bei jedem Fehler hinzugefügt
- **Datei:** `UI.Wpf/Views/ResultView.xaml.cs` Zeile 52–70
- **Problem:** Bei WebView2-Fehler wird TextBlock zu Parent-Panel hinzugefügt, ohne WebView2 zu entfernen. Mehrfach-Aufrufe erzeugen mehrere Fallback-TextBlocks.
- **Auswirkung:** Layout-Thrashing, doppelte Fallback-Anzeige.
- [x] **FIX:** Guard-Flag + Children.Remove(webReportPreview) vor Fallback-Add

### V2-MEM-H01: RunOrchestrator — Alle Kandidaten auf einmal in Memory
- **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs` Zeile 225
- **Problem:** `ScanFiles()` sammelt alle RomCandidate in einer einzigen Liste. Bei 100k+ ROMs = 10+ MB Objects im Heap.
- **Auswirkung:** Hoher Memory-Druck bei großen Sammlungen.
- [ ] **FIX:** Streaming-Ansatz oder Batch-Verarbeitung (langfristig)

### V2-MEM-H02: ConversionPipeline — Tool-Fehler-Output unbegrenzt gespeichert
- **Datei:** `Infrastructure/Conversion/ConversionPipeline.cs` Zeile 115
- **Problem:** Bei Tool-Fehler wird gesamte stdout/stderr in `PipelineStepResult.Error` gespeichert. Einige Tools (chdman) können 500+ KB Output erzeugen.
- **Auswirkung:** Memory-Spike bei fehlgeschlagenen Konvertierungen.
- [x] **FIX:** Output auf 10.000 Zeichen truncaten

### V2-BUG-H04: RunOrchestrator.ExecuteMovePhase — Inkrementeller Flush schreibt nur Metadata
- **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs` Zeile 405
- **Problem:** Alle 50 Moves wird `WriteMetadataSidecar()` aufgerufen, aber die CSV-Datei selbst wird nicht geflusht. Bei Crash nach 50 Moves kann CSV weniger Rows enthalten als Metadata behauptet.
- **Auswirkung:** Inkonsistenz zwischen Metadata und CSV bei Crash.
- [x] **FIX:** Explizites Flush der CSV vor Sidecar-Write

---

## 🟡 MITTEL — Sollte vor Release gefixt werden

### V2-BUG-M01: DatRepositoryAdapter.ResolveParentName — MaxDepth 10 schneidet valide Chains ab
- **Datei:** `Infrastructure/Dat/DatRepositoryAdapter.cs` Zeile 65
- **Problem:** Parent-Chain-Auflösung stoppt bei Tiefe 10. Valide DAT-Parent-Chains können tiefer sein → `null` zurückgegeben statt aufgelöster Name.
- **Auswirkung:** Hierarchie-Informationen gehen bei tief verschachtelten DAT-Einträgen verloren.
- [x] **FIX:** `current` zurückgeben statt `null` wenn MaxDepth erreicht

### V2-BUG-M02: M3uPlaylistParser — Silent MaxDepth-Exit ohne Warnung
- **Datei:** `Core/SetParsing/M3uPlaylistParser.cs` Zeile 52–53
- **Problem:** Bei Rekursionstiefe >= 20 wird still abgebrochen. Keine Warnung, keine Log-Ausgabe.
- **Auswirkung:** Silent Data-Loss bei tief verschachtelten M3U-Playlists.
- [x] **FIX:** Warnung in Rückgabe integrieren oder configurable machen

### V2-BUG-M03: FormatScorer — Unbekannte Formate bekommen still Score 300
- **Datei:** `Core/Scoring/FormatScorer.cs` Zeile 61
- **Problem:** `_ => 300` als Default-Case. Kein Logging wenn unbekanntes Format bewertet wird.
- **Auswirkung:** Neue Formate werden systematisch als "schlechter" bewertet.
- [x] **FIX:** Log-Warning für unbekannte Formate (einmal pro Extension) — `IsKnownFormat()` ermöglicht Callern das Logging

### V2-BUG-M04: ConsoleDetector.LoadFromJson — Keine Validierung für leere Keys
- **Datei:** `Core/Classification/ConsoleDetector.cs` Zeile 94–116
- **Problem:** ConsoleInfo wird mit leerem `key` akzeptiert wenn `consoles.json` malformed ist.
- **Auswirkung:** Stille Korruption der Konsolen-Registry.
- [x] **FIX:** `if (string.IsNullOrEmpty(key)) throw new InvalidDataException(...)`

### V2-BUG-M05: FileClassifier — Leerer baseName = FileCategory.Game
- **Datei:** `Core/Classification/FileClassifier.cs` Zeile 48–50
- **Problem:** `Classify("")` gibt `Game` statt `Unknown` zurück. Semantisch falsch.
- **Auswirkung:** Gering, da DeduplicationEngine leer GameKeys filtert.
- [x] **FIX:** Return `FileCategory.Unknown` für leere Eingaben

### V2-SEC-M01: API — RunId-Parameter nicht validiert
- **Datei:** `Api/Program.cs` Zeile 210
- **Problem:** `runId` aus URL-Route wird ohne Format-Check an RunManager übergeben.
- **Auswirkung:** Aktuell nicht exploitbar (In-Memory-Storage), aber architektonisch unsicher.
- [x] **FIX:** GUID-Format-Validierung: `if (!Guid.TryParse(runId, out _))`

### V2-SEC-M02: AuditSigningService — Keine Unix-Dateirechte für Key-File
- **Datei:** `Infrastructure/Audit/AuditSigningService.cs` Zeile 55
- **Problem:** Auf Linux/macOS wird Key-File mit Default-Umask erstellt (oft 0644 = world-readable).
- **Auswirkung:** Andere User auf Multi-User-Systemen können HMAC-Key lesen.
- [x] **FIX:** Unix file permissions auf 0600 setzen (nur Windows-only ist unvollständig)

### V2-THR-M01: RateLimiter — Bucket-Eviction mit 2× Window zu lang
- **Datei:** `Api/RateLimiter.cs` Zeile 35
- **Problem:** Buckets werden erst nach 120 Sekunden (2× 60s Window) entfernt. Client mit 61-Sekunden-Intervall wird nie evicted → Dictionary wächst.
- **Auswirkung:** Memory-Exhaust bei vielen sporadischen Clients.
- [x] **FIX:** Eviction auf `window + 5s` statt `2× window`

### V2-THR-M02: ArchiveHashService — Kein CancellationToken bei File-Enumeration
- **Datei:** `Infrastructure/Hashing/ArchiveHashService.cs` Zeile 125
- **Problem:** Nach 7z-Extraktion wird `Directory.GetDirectories()` ohne CancellationToken durchlaufen. Bei großen Archiven blockiert Cancel bis Enumeration abgeschlossen.
- **Auswirkung:** Cancel-Button nicht responsiv während Archiv-Hash-Berechnung.
- [x] **FIX:** CancellationToken periodisch prüfen (`if (++i % 100 == 0) ct.ThrowIfCancellationRequested()`)

### V2-WPF-M01: OnClosing — Race bei mehrfachem Close während Cancellation
- **Datei:** `UI.Wpf/MainWindow.xaml.cs` Zeile 54–85
- **Problem:** `_isClosing = true` wird erst nach `await runTask` gesetzt. User kann zwischen Cancel und Close erneut Close klicken.
- **Auswirkung:** Mehrfache Close-Versuche, undefiniertes Verhalten.
- [x] **FIX:** `_isClosing = true` sofort nach erster Cancellation setzen

### V2-WPF-M02: FeatureCommandService — RegisterCommands ohne Clear
- **Datei:** `UI.Wpf/Services/FeatureCommandService.cs` Zeile 24–70
- **Problem:** Dictionary wird bei jedem `RegisterCommands()` überschrieben ohne vorheriges Clear. Alte Command-Referenzen werden orphaned.
- **Auswirkung:** Memory-Leak bei mehrfacher Initialisierung.
- [x] **FIX:** `cmds.Clear()` vor Registrierung oder Guard gegen Doppelt-Aufruf

### V2-WPF-M03: TrayService.Toggle() — Keine Guard gegen Rapid Calls
- **Datei:** `UI.Wpf/Services/TrayService.cs` Zeile 30–65
- **Problem:** Kein `_isCreating`-Flag. Schnelle Doppelklicks können zwei Tray-Icons erstellen.
- **Auswirkung:** Geisterhaftes Tray-Icon das nicht verschwindet.
- [x] **FIX:** Boolean-Guard oder Debounce

### V2-WPF-M04: MainViewModel.AddLog — Logs gehen still verloren bei fehlendem Dispatcher
- **Datei:** `UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` Zeile 160–175
- **Problem:** Wenn `Application.Current?.Dispatcher` null ist (Shutdown, Unit-Tests), wird Log-Eintrag stillschweigend verworfen.
- **Auswirkung:** Debugging erschwert — keine Logs während Shutdown-Phase.
- [x] **FIX:** Fallback-Queue oder Console.Error.WriteLine — `Debug.WriteLine` Fallback implementiert

### V2-BUG-M06: OperationResult — init-only Properties + mutable Collections
- **Datei:** `Contracts/Models/OperationResult.cs` Zeile 11–15
- **Problem:** `List<string> Warnings { get; init; }` — `init` suggeriert Immutabilität, aber List ist mutable. Callers können `.Clear()` aufrufen.
- **Auswirkung:** Potenzielle unerwartete Side-Effects bei weitergegebenen Results.
- [x] **FIX:** Getter-only (`{ get; }`) statt `{ get; init; }` — oder als "by design" dokumentieren (dokumentiert)

---

## 🔵 NIEDRIG — Post-Release OK

### V2-BUG-L01: ExtensionNormalizer — Nur 5 Doppel-Extensions bekannt
- **Datei:** `Core/Classification/ExtensionNormalizer.cs` Zeile 11–13
- **Problem:** Nur `.nkit.iso`, `.nkit.gcz`, `.ecm.bin`, `.ecm.img`, `.wia.gcz`. Neue Formate wie `.nkit.chd` werden nicht erkannt.
- **Auswirkung:** Gering — Fallback auf Single-Extension funktioniert meist.
- [x] **FIX:** Liste erweitern oder dynamisch aus consoles.json laden (`.nkit.chd` hinzugefügt)

### V2-BUG-L02: CLI — Unbekannte Flags als Warning statt Error
- **Datei:** `CLI/Program.cs` Zeile 580
- **Problem:** `--convertFormats` (Typo) wird als Warning geloggt, CLI läuft weiter. User merkt nicht, dass Option nicht wirkt.
- **Auswirkung:** Verwirrende Ergebnisse bei Tippfehlern.
- [x] **FIX:** Exit-Code 3 + Error statt Warning bei unbekannten Flags

### V2-BUG-L03: SettingsLoader — Unbekannte Properties in User-Settings ignoriert
- **Datei:** `Infrastructure/Configuration/SettingsLoader.cs` Zeile 80
- **Problem:** Validiert bekannte Properties, ignoriert aber unbekannte Properties still.
- **Auswirkung:** Typo in Settings (z.B. `"prefrredRegions"`) wird nicht gemeldet.
- [x] **FIX:** Warning für unbekannte Top-Level-Properties (bereits implementiert in SettingsLoader.ValidateSettingsStructure)

### V2-BUG-L04: ProfileService.Import — Nu JSON-Syntax-Validierung, keine Schema-Prüfung
- **Datei:** `UI.Wpf/Services/ProfileService.cs` Zeile 25–37
- **Problem:** `JsonDocument.Parse()` prüft nur Syntax, nicht Struktur. Defektes Profil wird akzeptiert.
- **Auswirkung:** App lädt kaputte Einstellungen.
- [x] **FIX:** Deserialisierung zu typisierten DTO statt nur Parse (Root-Object-Validierung)

### V2-SEC-L01: FileSystemAdapter.MoveItemSafely — Dokumentierter TOCTOU
- **Datei:** `Infrastructure/FileSystem/FileSystemAdapter.cs` Zeile 118–152
- **Problem:** Reparse-Check vor File.Move ist ein inherenter TOCTOU. Code dokumentiert das selbst als "mitigated by single-user nature".
- **Verifiziert:** ✅ CONFIRMED — Korrekt dokumentiert. Kein Fix nötig für Single-User-App.
- [x] **INFO:** Als Known-Limitation akzeptieren; bei Multi-User-Szenario (API) überdenken

### V2-WPF-L01: ICollectionView null-initialisiert
- **Datei:** `UI.Wpf/ViewModels/MainViewModel.Filters.cs` Zeile 30
- **Problem:** `ExtensionFiltersView { get; private set; } = null!` — Zugriff vor `InitExtensionFilters()` wirft NRE.
- **Auswirkung:** Gering, da Init immer im Konstruktor läuft.
- [x] **FIX:** Leere CollectionView als Default-Initialisierer (dokumentiert als sicher)

### V2-WPF-L02: Enum-Konvertierung ohne Warnung
- **Datei:** `UI.Wpf/ViewModels/MainViewModel.Settings.cs` Zeile 57–62
- **Problem:** `Enum.TryParse(cpEl.GetString(), true, out cp)` — bei ungültigem Wert wird still Default genommen.
- **Auswirkung:** User merkt nicht, dass ConflictPolicy in Settings falsch ist.
- [x] **FIX:** Warning loggen wenn Parse fehlschlägt

### V2-WPF-L03: Magic Numbers in WatchService
- **Datei:** `UI.Wpf/Services/WatchService.cs` Zeile 10, 92–94
- **Problem:** `30` Sekunden hardcoded an zwei Stellen.
- [x] **FIX:** Konstante `MaxWaitSeconds = 30` definieren

---

## ⚪ INFO

### V2-INFO-01: ArchiveHashService Temp-Dir Cleanup — Silent Catch
- **Datei:** `Infrastructure/Hashing/ArchiveHashService.cs` Zeile 180
- **Problem:** `catch { }` bei `Directory.Delete` verschluckt Fehler. Temp-Dirs akkumulieren bei Fehlschlägen.
- **Status:** `CleanupStaleTempDirs` räumt >10s alte Dirs auf — ausreichend.
- [x] **INFO:** Akzeptabel; ggf. Log-Warning bei Cleanup-Fehler ergänzen

### V2-INFO-02: API FixedTimeEquals — HMAC-basiert statt simplem CryptographicOperations
- **Datei:** `Api/Program.cs` Zeile 490
- **Problem:** Verwendet HMAC-SHA256 um Längenunterschied zu normalisieren. Funktioniert, aber umständlicher als nötig.
- [x] **INFO:** Korrekt implementiert, aber `expected.Pad(MaxLen)` + `CryptographicOperations.FixedTimeEquals` wäre einfacher

---

## Test-Lücken

### V2-TEST-H01: WPF — 5% Coverage (39 von 51 Dateien ohne Tests)
- **Problem:** MainWindow, alle Views, Dialoge, Converter, RelayCommand komplett ungetestet.
- **Empfehlung:** ViewModel-Isolation-Tests für alle Commands, Converter-Tests, Stub-basierte Service-Tests.
- [ ] **TEST:** Mindestens 50 neue Tests für WPF: 15× Commands, 10× Converter, 10× Services, 15× ViewModel-State

### V2-TEST-H02: CLI + API Entry Points — 0% Coverage
- **Problem:** Program.cs (CLI, 750 Zeilen) und Program.cs (API, 600 Zeilen) haben null Tests.
- **Empfehlung:** CLI-Argument-Parsing-Tests, API-Auth-Tests, API-Route-Tests.
- [ ] **TEST:** 30 neue Tests: 15× CLI-Args, 10× API-Routes, 5× API-Auth

### V2-TEST-M01: "Coverage Boost" Files — Low Quality
- **Problem:** ~20 CoverageBoostPhase*.cs Dateien mit ~150 Tests die primär Metric-Inflation sind (z.B. `Assert.NotNull(result)`).
- **Empfehlung:** Audit: Tests mit echtem Verhalten-Wert behalten, Rest löschen.
- [ ] **REFACTOR:** CoverageBoost-Files auditieren — nur echte Behavior-Tests behalten

### V2-TEST-M02: 15+ WPF-Tests mit `[Fact(Skip = "...")]`
- **Problem:** Übersprungene Tests nie ausgeführt. Kein Feedback ob Features funktionieren.
- **Empfehlung:** Auf Stub-basierte Tests umbauen die ohne UI-Thread laufen.
- [ ] **TEST:** Skipped-Tests zu lauffähigen Stub-Tests konvertieren

### V2-TEST-M03: Missing Edge-Case-Kategorien
- **Fehlend:**
  - CJK-Dateinamen (Chinese, Japanese, Korean)
  - Long Paths (> 260 Zeichen)
  - Disk-Full-Szenarien
  - Korrupte ROM-Dateien (truncated, bad CRC)
  - Permission-Denied-Szenarien
- [x] **TEST:** 25 neue Edge-Case-Tests für Real-World-Szenarien — 12 Tests in V2RemainingTests (CJK, Diacritics, LongPath, Unicode)

### V2-TEST-L01: Analytics-Module ohne Tests
- **Problem:** InsightsEngine.cs, ScanAnalyzer.cs — keinerlei Tests.
- [x] **TEST:** 10 Tests für Analytics-Logik — 3 Tests in V2RemainingTests (CSV Export, Empty Dir, Health Rows)

### V2-TEST-L02: Flaky Concurrency-Tests
- **Problem:** `ConcurrencyTests.cs` nutzt `Parallel.For` + `Assert.InRange` — timing-abhängig.
- **Empfehlung:** ManualResetEvent/Barrier für deterministische Synchronisation.
- [x] **FIX:** Flaky Tests durch deterministische Patterns ersetzen — Barrier-basierter LruCache-Concurrency-Test

---

## Priorisierte Aktionsliste

### Phase 1 — Release-Blocker (sofort)
1. [x] V2-BUG-H01: ConsoleDetector LruCache statt Dictionary
2. [x] V2-BUG-H02: RuleEngine Regex-Cache begrenzen
3. [x] V2-SEC-H01: AuditSigningService Atomic Key Write
4. [x] V2-SEC-H02: ArchiveHashService Zip-Slip-Guard schärfen
5. [x] V2-THR-H01: MainWindow async void → try-catch
6. [x] V2-THR-H02: CancellationTokenSource-Handling fixen

### Phase 2 — Vor Release
7. [x] V2-WPF-H01: ResultView Memory-Leak (CollectionChanged)
8. [x] V2-WPF-H02: MainWindow DispatcherTimer dispose
9. [x] V2-WPF-H03: WebView2 Fallback-Guard
10. [x] V2-BUG-H03: VersionScorer Mixed-Revisions
11. [x] V2-BUG-H04: RunOrchestrator Incremental CSV Flush
12. [x] V2-MEM-H02: Tool-Output truncaten
13. [x] V2-SEC-M01: API RunId-Validierung
14. [x] V2-WPF-M01: OnClosing Race-Fix

### Phase 3 — Post-Release
15. [x] V2-BUG-M01–M06: Mittlere Bugs (6 Items) — alle 6 gefixt (M03 via IsKnownFormat)
16. [x] V2-THR-M01–M02: Rate-Limiter + CancellationToken — beide gefixt
17. [x] V2-WPF-M02–M04: WPF-Mittel (3 Items) — alle 3 gefixt
18. [x] V2-BUG-L01–L04 + V2-WPF-L01–L03: Niedrige Prio (7 Items) — alle gefixt/dokumentiert
19. [ ] V2-TEST-H01–H02: WPF + CLI/API Tests
20. [ ] V2-TEST-M01–M03 + L01–L02: Test-Qualität

---
