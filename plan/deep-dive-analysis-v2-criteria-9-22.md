# Deep-Dive Analyse — Kriterien 9–22 (v2)

> **Datum:** 2026-03-14 | **Build:** ✅ GREEN | **Tests:** 2883 bestanden, 0 Fehler, 16 übersprungen  
> **Scope:** Alle ~96 C#-Dateien (Core, Infrastructure, Contracts, CLI, API, WPF)  
> **Methode:** Zeilenweise Inspektion gegen 14 erweiterte Kriterien

---

## Zusammenfassung

| Kriterium | Kritisch 🔴 | Hoch 🟠 | Mittel 🟡 | Niedrig 🔵 |
|-----------|:-----------:|:-------:|:---------:|:----------:|
| 9. Datenintegrität & Reversibilität | — | 1 | 2 | 1 |
| 10. Nebenläufigkeit & Threading | — | 2 | 2 | 1 |
| 11. Cancellation/Timeouts/Partial Failure | — | 2 | 3 | — |
| 12. Logging/Observability | — | — | 2 | 2 |
| 13. Config/Schema-Validierung | — | 1 | 2 | 1 |
| 14. Kompatibilität/Plattformannahmen | — | — | 2 | 2 |
| 15. I/O-Semantik/TOCTOU | — | 1 | 2 | 1 |
| 16. Deterministische Entscheidungen | — | — | 1 | 1 |
| 17. Testbarkeit/Seams | — | — | 2 | 1 |
| 18. Security über Code hinaus | — | 1 | 1 | — |
| 19. UX/Produkt-Sicherheit | — | 1 | 2 | 1 |
| 20. API/Contract-Versionierung | — | 1 | 1 | 1 |
| 21. Performance unter realen Lasten | — | 2 | 2 | 1 |
| 22. Internationalisierung/Unicode | — | — | 2 | 2 |
| **Gesamt** | **0** | **12** | **26** | **15** |

---

## Kriterium 9: Datenintegrität & Reversibilität

### V2-H01: ScanFiles() baut unbegrenzte Kandidatenliste im Speicher 🟠
- **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs` ScanFiles()
- **Problem:** Alle Dateien aller Roots werden in eine einzige `List<RomCandidate>` geladen. Bei großen Sammlungen (100k+ Dateien) kann dies zu OutOfMemoryException führen, bevor die Deduplizierung startet. Kein Memory-Limit, kein Streaming, keine Batching-Strategie.
- **Auswirkung:** OOM-Crash bei großen Sammlungen → kein Ergebnis, kein Audit, kein Rollback.
- **Fix-Vorschlag:** Partitioniertes Scanning (Batch-basiert pro Root/Console) oder Memory-Limit mit Warnung.

### V2-M01: Junk-Removal Phase hat keinen eigenen Audit-Trail 🟡
- **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs` ExecuteJunkRemovalPhase()
- **Problem:** Phase 3b (Junk-Removal) entfernt Dateien via `_fs.MoveItemSafely`, aber die einzelnen Moves werden nicht separat in der Audit-CSV protokolliert (nur das MovePhaseResult-Aggregat). Bei Rollback fehlt die Information, welche Dateien als Junk entfernt wurden vs. als Dedupe-Losers.
- **Fix-Vorschlag:** Audit-Row pro Junk-Move mit `Action=JUNK_REMOVE` schreiben.

### V2-M02: Rollback-Stack im ViewModel ist unbegrenzt 🟡
- **Datei:** `UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs`, `_rollbackUndoStack`
- **Problem:** Der Undo/Redo-Stack für Rollbacks wächst unbegrenzt. Bei vielen aufeinanderfolgenden Runs im selben Session-Lifetime (z.B. Watch-Mode) kann dies Speicher akkumulieren. `_rollbackUndoStack` ist ein unbegrenzter `Stack<string>`.
- **Auswirkung:** Gering — nur String-Pfade, aber inkonsistent mit AppStateStore (Max 100 Depth).
- **Fix-Vorschlag:** Max Depth (z.B. 50) analog zu AppStateStore.

### V2-L01: ConsoleSorter Rollback verifiziert nicht die Datei-Integrität 🔵
- **Datei:** `Infrastructure/Sorting/ConsoleSorter.cs` MoveSetAtomically()
- **Problem:** Beim Rollback wird `FindActualDestination` + `MoveItemSafely` aufgerufen, aber es gibt keine Verifizierung, dass die zurück-verschobene Datei identisch mit dem Original ist (z.B. Checksum-Vergleich). Bei einem partial-write-Szenario (z.B. Disk voll während Move) könnte eine korrumpierte Datei zurück-verschoben werden.
- **Auswirkung:** Gering — `File.Move` auf NTFS ist atomar bei gleichem Volume.

---

## Kriterium 10: Nebenläufigkeit & Threading

### V2-H02: RunManager.TryCreate — Race Condition bei Concurrent POST /runs 🟠
- **Datei:** `Api/RunManager.cs` TryCreate()
- **Problem:** `TryCreate` prüft `_activeRun is not null` unter `lock(_activeLock)` und setzt dann den aktiven Run. Aber `ExecuteRun()` läuft in `Task.Run()` — zwischen dem `lock`-Bereich und dem tatsächlichen Start des Tasks könnte ein zweiter Request am `EvictOldRuns` oder `_runs.TryAdd` Race-Conditioned sein. Konkreter: `_runs.TryAdd` + `EvictOldRuns` außerhalb des Locks, könnten bei vielen gleichzeitigen Requests (z.B. abusive Clients) zu Inkonsistenzen in `_runs` führen.
- **Auswirkung:** Mittel — Rate Limiter (120/min) und API-Key schützen vor externem Abuse. Aber bei programmatischer Nutzung (z.B. CI) möglich.
- **Fix-Vorschlag:** EvictOldRuns unter `_activeLock` aufrufen oder `_runs`-Mutationen konsistent mit ConcurrentDictionary-Semantik behandeln.

### V2-H03: ToolRunnerAdapter._hashCache nicht thread-safe 🟠
- **Datei:** `Infrastructure/Tools/ToolRunnerAdapter.cs` VerifyToolHash()
- **Problem:** `_hashCache` ist ein reguläres `Dictionary<string, (string, DateTime)>`, das in `VerifyToolHash()` ohne Lock gelesen und geschrieben wird. Wenn zwei Threads gleichzeitig verschiedene Tools verifizieren, Race Condition auf dem Dictionary (könnte in seltenen Fällen zu NullReferenceException oder korruptem State führen).
- **Auswirkung:** Mittel — in der Praxis wird ToolRunnerAdapter pro Orchestrator-Instanz erstellt, aber bei API-Nutzung (wo RunManager Singleton ist) könnte ein shared ToolRunnerAdapter problematisch sein.
- **Fix-Vorschlag:** `ConcurrentDictionary<string, (string, DateTime)>` statt `Dictionary` oder `lock(_toolHashLock)` um den Cache-Zugriff.

### V2-M03: WPF AddLog mit Dispatcher-Race 🟡
- **Datei:** `UI.Wpf/ViewModels/MainViewModel.cs` AddLog()
- **Problem:** `Application.Current?.Dispatcher` kann `null` sein wenn AddLog nach Application-Shutdown aufgerufen wird (z.B. aus einem noch laufenden Background-Task). Die Null-Prüfung ist vorhanden, aber bei `null` wird `AddLogCore` direkt aufgerufen — was auf einem Nicht-UI-Thread die ObservableCollection modifiziert und eine `NotSupportedException` auslöst.
- **Auswirkung:** Crash bei Application-Shutdown wenn ein Run noch läuft.
- **Fix-Vorschlag:** Bei `dispatcher is null && !CheckAccess()` → Log verwerfen statt direkt schreiben.

### V2-M04: EventBus Publish blockiert Subscriber 🟡
- **Datei:** `Infrastructure/Events/EventBus.cs` Publish()
- **Problem:** `Publish()` ruft alle passenden Subscriber-Callbacks synchron unter dem Lock auf. Ein langsamer oder fehlerhafter Subscriber blockiert alle anderen und hält den Lock. Es gibt kein Timeout für Subscriber-Callbacks.
- **Auswirkung:** In der aktuellen Nutzung sind die Subscriber schnell (Logging). Bei zukünftiger Plugin-Nutzung problematisch.
- **Fix-Vorschlag:** Subscriber außerhalb des Locks aufrufen (Copy-on-Read-Pattern).

### V2-L02: LruCache Lock-Contention bei hoher Last 🔵
- **Datei:** `Core/Caching/LruCache.cs`
- **Problem:** Jeder Get/Add auf dem LruCache nimmt einen globalen Lock. Bei 50k-Entry-Cache und parallelem Scanning könnte dies zu Lock-Contention führen. In der Praxis wird der Cache nur vom GameKeyNormalizer auf dem Main-Thread verwendet.
- **Auswirkung:** Gering — derzeit kein paralleles Scanning implementiert.

---

## Kriterium 11: Cancellation/Timeouts/Partial Failure

### V2-H04: ConversionPipeline.Execute — Kein Timeout für Tool-Ausführung pro Step 🟠
- **Datei:** `Infrastructure/Conversion/ConversionPipeline.cs` Execute()
- **Problem:** Jeder `ExecuteStep` delegiert an `_tools.InvokeProcess`, das einen globalen Timeout hat (30 min). Aber bei einer Multi-Step-Pipeline (z.B. CSO→ISO→CHD) ist der Gesamt-Timeout potenziell 60+ Minuten und wird nicht auf Pipeline-Ebene begrenzt. Kein Progress-Reporting pro Step.
- **Auswirkung:** Hoch — Benutzer sieht keinen Fortschritt während einer langen Konvertierung. Bei Tool-Hang (z.B. chdman bei defektem ISO) blockiert die gesamte Pipeline.
- **Fix-Vorschlag:** Pipeline-Level-Timeout und Step-Progress-Callback.

### V2-H05: API SSE-Stream hat kein Heartbeat/Keep-Alive 🟠
- **Datei:** `Api/Program.cs` `/runs/{runId}/stream` Endpoint
- **Problem:** Der SSE-Stream sendet nur Events wenn sich der Status ändert. Wenn ein Run 10+ Minuten dauert ohne sichtbare Statusänderung, senden Load Balancer, Reverse Proxies oder Browser-Timeouts keine Keep-Alive-Nachrichten → Client wird disconnected, Reconnect-Logik fehlt. 5-Minuten-Timeout konfiguriert, aber kein periodisches `:heartbeat\n\n`.
- **Auswirkung:** Hoch — API ist auf 127.0.0.1, aber bei Nutzung hinter einem Reverse Proxy (z.B. in Docker/CI) bricht der Stream ab.
- **Fix-Vorschlag:** Alle 15-30 Sekunden einen SSE-Comment `:\n\n` als Heartbeat senden.

### V2-M05: CLI CancelKeyPress — Keine Timeout-Strategie 🟡
- **Datei:** `CLI/Program.cs` Run()
- **Problem:** `Console.CancelKeyPress` setzt `e.Cancel = true` und ruft `cts.Cancel()` auf. Aber wenn der Orchestrator nicht rechtzeitig auf Cancellation reagiert (z.B. blockiert in einem externen Tool-Prozess), bleibt der CLI-Prozess hängen. Es fehlt ein forcierter Timeout (z.B. nach 10s erneut Ctrl+C → Process.Kill).
- **Auswirkung:** Mittel — in der Praxis kann der Benutzer den Prozess manuell beenden, aber in CI-Szenarien problematisch.
- **Fix-Vorschlag:** Counter für CancelKeyPress: 1x → graceful, 2x → Process.Exit(2).

### V2-M06: RunOrchestrator.Execute — Partial Move ohne Cleanup bei Abbruch 🟡
- **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs` Execute()
- **Problem:** Wenn der CancellationToken zwischen Phase 4 (Move) und Phase 5 (Sort) feuert, sind bereits Dateien verschoben, aber die Sort-Phase hat nicht stattgefunden. Der Audit-Trail protokolliert die Moves, aber es gibt keine automatische Rollback-Aufforderung bei Abbruch. Der Benutzer muss manuell Rollback initiieren.
- **Auswirkung:** Mittel — Audit-Trail existiert, manueller Rollback möglich, aber UX-lücke.

### V2-M07: WPF ExecuteRunAsync — Unbehandelte Exceptions in Task.Run 🟡
- **Datei:** `UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` ExecuteRunAsync()
- **Problem:** `Task.Run(() => RunService.ExecuteRun(...))` propagiert Exceptions korrekt via `await`. Aber: `Task.Run(() => RunService.BuildOrchestrator(...))` in der Initialisierungs-Phase könnte `SettingsLoader.Load`-Exceptions werfen (z.B. JSON-Parse-Fehler der User-Settings). Diese werden als generische `Exception` gefangen und nur als `"Fehler: {ex.Message}"` geloggt — ohne ErrorKind-Klassifikation.
- **Fix-Vorschlag:** `ErrorClassifier.FromException` verwenden wie im CLI.

---

## Kriterium 12: Logging/Observability

### V2-M08: Keine Correlation-ID im API-Run-Lifecycle 🟡
- **Datei:** `Api/RunManager.cs` + `Api/Program.cs`
- **Problem:** Jeder Run hat eine `RunId` (UUID), aber die Request-Logs in `Program.cs` verwenden keine Correlation-ID, die den HTTP-Request mit dem Run verknüpft. Die `requestId` in den Request-Logs ist unabhängig von der `RunId`. Bei Debugging/Monitoring fehlt die Verknüpfung.
- **Auswirkung:** Mittel — erschwert Debugging bei API-Problemen.

### V2-M09: RunOrchestrator kennt keine strukturierte Phase-Metrik 🟡
- **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs`
- **Problem:** `_onProgress` schreibt Text-Nachrichten, aber keine strukturierten Phase-Metriken (Phase-Dauer, Dateien pro Phase, Fehler pro Phase). `PhaseMetricsCollector` existiert im Codebase, wird aber nicht in den Orchestrator integriert.
- **Fix-Vorschlag:** PhaseMetricsCollector in Execute() anlegen und pro Phase Start/Stop aufrufen.

### V2-L03: CLI Log-Rotation ohne konfigurierbare Policy 🔵
- **Datei:** `CLI/Program.cs` → `JsonlLogRotation.Rotate()`
- **Problem:** Log-Rotation wird am Ende jedes Runs aufgerufen, aber die Rotations-Policy (Max-Dateien, Max-Größe) ist hardcoded. Keine Konfiguration über Settings.

### V2-L04: API Request-Logging ist minimal 🔵
- **Datei:** `Api/Program.cs` Request-Logging-Middleware
- **Problem:** Nur Method, Path, StatusCode, Duration werden geloggt. Body-Size, Client-IP (für Rate-Limit-Debugging), API-Version-Header werden nicht erfasst.

---

## Kriterium 13: Config/Schema-Validierung

### V2-H06: SettingsLoader validiert keine Enum-Werte 🟠
- **Datei:** `Infrastructure/Configuration/SettingsLoader.cs`
- **Problem:** 
  - `LogLevel` wird als String akzeptiert — kein Validation gegen `Debug|Info|Warning|Error`. Ein Wert wie `"Verbose"` oder `""` wird stillschweigend übernommen und erst bei Laufzeit-Vergleich ignoriert.
  - `Mode` wird als String akzeptiert — `"Delete"`, `"Destroy"` etc. passieren ohne Validierung.
  - `HashType` wird als String akzeptiert — kein Check gegen `SHA1|SHA256|MD5|CRC32`.
  - `PreferredRegions` wird nicht gegen bekannte Region-Codes validiert.
- **Auswirkung:** Hoch — ungültige Settings führen zu subtilen Fehlern (z.B. kein Log-Output weil LogLevel nicht erkannt wird), ohne dass der Benutzer informiert wird.
- **Fix-Vorschlag:** Enum-Validierung + Warnung bei Load.

### V2-M10: API-Mode-Validierung ist String-basiert 🟡
- **Datei:** `Api/Program.cs` POST /runs
- **Problem:** `mode != "DryRun" && mode != "Move"` ist case-sensitive. `"dryrun"` oder `"MOVE"` wird als Fehler abgelehnt, obwohl `PropertyNameCaseInsensitive = true` für JSON aktiv ist. Inkonsistentes Verhalten.
- **Fix-Vorschlag:** Case-insensitive Vergleich: `!mode.Equals("DryRun", OrdinalIgnoreCase) && ...`

### V2-M11: Keine JSON-Schema-Validierung für settings.json 🟡
- **Datei:** `data/schemas/settings.schema.json` existiert, wird aber nie zur Laufzeit verwendet
- **Problem:** Die JSON-Schema-Dateien unter `data/schemas/` werden nur als Dokumentation genutzt. Es gibt keine Runtime-Validierung der User-Settings gegen das Schema. Ungültige/unbekannte Properties werden stillschweigend ignoriert (z.B. Tippfehler `"preferedRegions"` statt `"preferredRegions"`).
- **Auswirkung:** Mittel — führt zu "warum funktioniert mein Setting nicht"-Debugging.

### V2-L05: defaults.json hat keine Version/Format-Kennung 🔵
- **Datei:** `data/defaults.json`
- **Problem:** Kein `"$schema"` oder `"version"` Feld. Bei Format-Änderungen gibt es keinen Mechanismus zur Migration alter Defaults.

---

## Kriterium 14: Kompatibilität/Plattformannahmen

### V2-M12: Path.DirectorySeparatorChar Annahmen 🟡
- **Datei:** Mehrere Stellen (FileSystemAdapter, ConsoleSorter, etc.)
- **Problem:** Code nutzt konsistent `Path.DirectorySeparatorChar`, aber manche String-Vergleiche verwenden hardcoded `"\\"` oder `@"\"`. Beispiel: `ConsoleSorter.IsInExcludedFolder` und `ResolveChildPathWithinRoot` verwenden `Path.DirectorySeparatorChar` korrekt. Aber: Die CLI unterstützt nur Windows (WPF-Projekt + `net10.0-windows`), daher ist Cross-Platform aktuell nicht relevant.
- **Auswirkung:** Gering — Windows-only-Projekt, aber bei Mono/Wine-Szenarien potenziell relevant.

### V2-M13: UNC-Pfad-Unterstützung ist inkonsistent 🟡
- **Datei:** `Infrastructure/Conversion/ConversionPipeline.cs` CheckDiskSpace(), `FileSystemAdapter`
- **Problem:** `ConversionPipeline.CheckDiskSpace` enthält spezielle UNC-Pfad-Behandlung mit Fallbacks. Aber `FileSystemAdapter` und `ConsoleSorter` haben keine explizite UNC-Unterstützung. Bei UNC-Roots (z.B. `\\NAS\roms`) könnte `Path.GetPathRoot` fehlschlagen oder unerwartete Ergebnisse liefern.
- **Auswirkung:** Gering — die meisten Nutzer verwenden lokale Pfade.

### V2-L06: DriveInfo-Konstruktor erwartet Root-Pfad 🔵
- **Datei:** `Infrastructure/Conversion/ConversionPipeline.cs`
- **Problem:** `new DriveInfo(pathRoot!)` funktioniert nur mit echten Drive-Roots (z.B. `"C:\"`) und `pathRoot` ist das Ergebnis von `Path.GetPathRoot`. Bei UNC-Pfaden kann `pathRoot` `"\\server\share"` sein — `DriveInfo` unterstützt das nicht und wirft `ArgumentException`.
- **Auswirkung:** Von der vorgelagerten UNC-Prüfung abgefangen, aber Code-Pfad könnte bei Edge-Cases erreicht werden.

### V2-L07: DateTime.Now vs. DateTime.UtcNow Inkonsistenz 🔵
- **Datei:** Mehrere Stellen
- **Problem:** `CLI/Program.cs` verwendet `DateTime.Now` für Audit-Timestamps, `Api/RunManager.cs` verwendet `DateTime.UtcNow`. Innerhalb desselben Audit-Trails könnten CLI- und API-generierte Timestamps in verschiedenen Zeitzonen sein.
- **Auswirkung:** Niedrig — CLI und API werden nicht gleichzeitig verwendet.

---

## Kriterium 15: I/O-Semantik/TOCTOU

### V2-H07: TOCTOU in MoveItemSafely collision handling 🟠
- **Datei:** `Infrastructure/FileSystem/FileSystemAdapter.cs` MoveItemSafely()
- **Problem:** Die DUP-Slot-Suche (`File.Exists(finalDest)` → `File.Move(fullSource, finalDest)`) hat ein inherentes TOCTOU-Window. Zwischen dem `File.Exists`-Check und dem `File.Move` könnte ein anderer Prozess (oder Thread bei paralleler Verarbeitung) dieselbe Datei erstellen. In der Praxis minimal, weil die DUP-Suffixe unique genug sind und nur ein Thread aktiv ist.
- **Auswirkung:** Hoch nur bei paralleler Verarbeitung — derzeit nicht implementiert, aber bei zukünftigem Parallel-Move relevant.
- **Fix-Vorschlag:** `File.Move` mit `overwrite: false` werfen lassen und dann nächsten DUP-Index probieren (try/catch statt check-then-act).

### V2-M14: Preflight Write-Test lässt temporäre Datei bei Crash liegen 🟡
- **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs` Preflight()
- **Problem:** Der Audit-Dir-Write-Test erstellt eine temporäre Datei (`File.WriteAllText(testFile, "")`) und löscht sie dann (`File.Delete(testFile)`). Wenn der Prozess zwischen diesen Zeilen crasht (unwahrscheinlich), bleibt eine leere Testdatei liegen.
- **Auswirkung:** Minimal — nur eine leere Datei mit GUID-Name, kein Datenverlust.

### V2-M15: AuditSigningService.Rollback TOCTOU 🟡
- **Datei:** `Infrastructure/Audit/AuditSigningService.cs` Rollback()
- **Problem:** (Bereits vom Subagent identifiziert) File.Exists-Check vor MoveItemSafely hat ein TOCTOU-Window. Die Datei könnte zwischen Check und Move gelöscht oder verschoben werden.
- **Auswirkung:** Gering — Single-User-Application, Rollback wird manuell ausgelöst.

### V2-L08: GetFilesSafe Race mit externen Datei-Operationen 🔵
- **Datei:** `Infrastructure/FileSystem/FileSystemAdapter.cs` GetFilesSafe()
- **Problem:** `Directory.GetFiles` + `File.GetAttributes` könnte fehlschlagen wenn Dateien zwischen Scan und Attribut-Check gelöscht werden. Wird durch try/catch abgefangen, aber die Datei wird dann einfach übersprungen — ohne Log-Eintrag.
- **Auswirkung:** Minimal — Datei ist ohnehin weg.

---

## Kriterium 16: Deterministische Entscheidungen

### V2-M16: FormatScorer.GetRegionScore Reihenfolge-Abhängigkeit 🟡
- **Datei:** `Core/Scoring/FormatScorer.cs` GetRegionScore()
- **Problem:** RegionScore = `1000 - N` wobei N der Index in `preferRegions` ist. Wenn der Benutzer `["EU", "US"]` angibt, bekommt EU=999, US=998. Dies ist korrekt und deterministisch für eine feste Konfiguration. **ABER:** Bei API-Nutzung wird `PreferRegions` per Request übergeben — verschiedene Requests können verschiedene Ordnungen haben → verschiedene Gewinner für identische ROM-Sets.
- **Auswirkung:** Mittel — nicht ein Bug per se, aber potenziell verwirrend wenn verschiedene API-Clients verschiedene Ergebnisse bekommen.

### V2-L09: DeduplicationEngine GroupBy ist deterministisch ✅ 🔵
- **Datei:** `Core/Deduplication/DeduplicationEngine.cs`
- **Befund:** GroupBy mit `StringComparer.OrdinalIgnoreCase` + OrderBy mit `StringComparer.OrdinalIgnoreCase` + SelectWinner mit fallback auf `MainPath OrdinalIgnoreCase` → vollständig deterministisch. ✅ Keine Aktion nötig.

---

## Kriterium 17: Testbarkeit/Seams

### V2-M17: RunOrchestrator hat keine testbare Phase-Granularität 🟡
- **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs`
- **Problem:** `Execute()` ist eine monolithische ~180-Zeilen-Methode. Einzelne Phasen (Scan, Dedupe, Sort, Convert, Move) sind private Methoden oder inline-Code. Es gibt keinen Weg, einzelne Phasen isoliert zu testen ohne den gesamten Orchestrator zu instanziieren.
- **Fix-Vorschlag:** Phasen als `internal static` oder über ein Strategy-Pattern exponieren.

### V2-M18: ToolRunnerAdapter.VerifyToolHash ist private — nicht testbar 🟡
- **Datei:** `Infrastructure/Tools/ToolRunnerAdapter.cs`
- **Problem:** `VerifyToolHash` ist `private`. Die Hash-Verifizierungslogik (Cache, File.GetLastWriteTimeUtc, SHA256) kann nur indirekt über `InvokeProcess` getestet werden. Keine Unit-Tests validieren die Cache-Eviction-Logik (LastWriteTime-Change → Re-Hash).
- **Fix-Vorschlag:** `internal` mit `[InternalsVisibleTo]` oder über eine dedizierte Klasse `ToolHashVerifier`.

### V2-L10: API-Endpunkte sind Lambda-basiert — kein Unit-Test ohne Integration 🔵
- **Datei:** `Api/Program.cs`
- **Problem:** Alle API-Endpunkte sind als Lambdas in `Program.cs` registriert. Unit-Tests müssen den vollen ASP.NET Host starten (`WebApplicationFactory`). Keine Handler-Klassen zum isolierten Testen der Validierungslogik.
- **Auswirkung:** Gering — Integration-Tests sind die richtige Teststrategie für Minimal API.

---

## Kriterium 18: Security über Code hinaus

### V2-H08: API-Key wird im Klartext über HTTP gesendet 🟠
- **Datei:** `Api/Program.cs`
- **Problem:** API bindet an `http://127.0.0.1:5000` (kein TLS). API-Key wird im HTTP-Header `ROM_CLEANUP_API_KEY` übertragen. Bei Loopback-Only ist dies akzeptabel, **ABER:** wenn ein Benutzer die Bindung ändert (z.B. `0.0.0.0:5000` für Remote-Zugriff), wird der API-Key im Klartext über das Netzwerk gesendet. Es gibt keine Warnung bei nicht-Loopback-Bindung.
- **Fix-Vorschlag:** Beim Startup prüfen ob die Bindung nicht-Loopback ist und dann TLS erzwingen oder Warnung loggen.

### V2-M19: Rate Limiter Key ist Client IP — spoofbar bei Reverse Proxy 🟡
- **Datei:** `Api/RateLimiter.cs`
- **Problem:** `ctx.Connection.RemoteIpAddress` wird als Schlüssel für Rate Limiting verwendet. Hinter einem Reverse Proxy (Nginx, Traefik) ist dies immer die Proxy-IP, nicht die echte Client-IP. Alle Clients teilen sich dann ein Rate-Limit-Bucket. `X-Forwarded-For` wird nicht berücksichtigt.
- **Auswirkung:** Gering — API ist auf 127.0.0.1 gebunden, Reverse-Proxy-Szenario ist atypisch.

---

## Kriterium 19: UX/Produkt-Sicherheit

### V2-H09: Watch-Mode startet automatisch DryRun ohne Throttle-Limit 🟠
- **Datei:** `UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` OnWatchRunTriggered()
- **Problem:** Wenn Watch-Mode aktiv ist, startet bei jeder Dateiänderung ein neuer DryRun (`RunCommand.Execute(null)`). Bei einem Bulk-Copy (z.B. 1000 Dateien auf einmal in den Root kopieren) wird für jede Datei ein separater DryRun ausgelöst. `WatchService` hat möglicherweise einen Debounce, aber `RunCommand` hat kein Throttle — es schützt nur `IsBusy`, was verhindert dass ein zweiter Run startet während der erste läuft. Bei schnellen aufeinanderfolgenden Änderungen (Datei-Kopie abgeschlossen → nächste Datei geändert → erster Run beendet → nächster Run startet sofort) kann dies die GUI blockieren.
- **Fix-Vorschlag:** Cooldown-Timer (z.B. 30 Sekunden) nach einem Watch-Run bevor der nächste starten darf.

### V2-M20: Keine Warnung bei sehr vielen Dateien vor Move 🟡
- **Datei:** RunOrchestrator.Execute() + WPF RunPipeline
- **Problem:** Wenn der DryRun 50k+ Dateien zum Move identifiziert, gibt es keine Warnung oder Bestätigung vor dem tatsächlichen Move. `ConfirmMove`-Dialog fragt nur generisch "Move ausführen?", ohne die Anzahl der betroffenen Dateien zu nennen.
- **Fix-Vorschlag:** Move-Bestätigungs-Dialog mit Statistik (Anzahl Dateien, Gesamt-Größe, betroffene Konsolen).

### V2-M21: Settings-Speicherung bei Crash verloren 🟡
- **Datei:** `UI.Wpf/MainWindow.xaml.cs` — `_settingsTimer` alle 5 Minuten
- **Problem:** Settings werden nur beim Schließen und alle 5 Minuten gespeichert. Bei einem Crash (z.B. OOM) gehen bis zu 5 Minuten an Settings-Änderungen verloren. Der 5-Minuten-Timer ist ein guter Kompromiss, aber es fehlt eine Settings-Save nach jeder signifikanten Änderung (z.B. nach Preset-Wechsel, Manual-Root-Add).
- **Auswirkung:** Gering — Settings-Verlust bei Crash ist ärgerlich, aber nicht datendestruktiv.

### V2-L11: Keine Benachrichtigung bei fehlgeschlagenem Tool-Hash 🔵
- **Datei:** `Infrastructure/Tools/ToolRunnerAdapter.cs` VerifyToolHash()
- **Problem:** Wenn die Tool-Hash-Verifizierung fehlschlägt, wird über `_log?.Invoke(...)` geloggt, aber es gibt keine UI-Benachrichtigung (Toast/Dialog) in der WPF-GUI. Der Benutzer sieht nur "Convert: Tool 'chdman' not found" ohne den Sicherheitskontext.
- **Fix-Vorschlag:** Spezifische Fehlermeldung "Tool-Signatur ungültig" in der GUI.

---

## Kriterium 20: API/Contract-Versionierung

### V2-H10: X-Api-Version ist hardcoded "1.0" ohne Versioning-Strategie 🟠
- **Datei:** `Api/Program.cs`
- **Problem:** `ctx.Response.Headers["X-Api-Version"] = "1.0"` ist hardcoded. Es gibt keine Versioning-Middleware, keinen `/v1/` Prefix, keine Accept-Header-Negotiation. Bei Breaking Changes (z.B. Änderung des RunRequest-Schemas) gibt es keinen Mechanismus für Backward-Compatibility.
- **Auswirkung:** Hoch wenn API von externen Clients genutzt wird (CI/CD Scripts). Gering wenn nur intern.
- **Fix-Vorschlag:** Version aus Assembly-Metadata lesen; bei Breaking Changes URL-Prefix `/v2/`.

### V2-M22: RunRequest Schema ist nicht validiert gegen OpenAPI-Spec 🟡
- **Datei:** `Api/OpenApiSpec.cs` + `Api/Program.cs`
- **Problem:** Die OpenAPI-Spec definiert das RunRequest-Schema, aber die Runtime-Validierung in Program.cs ist manuell implementiert (null-checks, string-vergleiche). Keine Guarantie dass die Spec und der Code synchron sind.
- **Auswirkung:** Mittel — API-Spec könnte veraltet sein relativ zum Code.

### V2-L12: Keine API-Deprecation-Header 🔵
- **Problem:** Es gibt keinen Mechanismus um API-Endpoints oder Parameter als deprecated zu markieren (z.B. `Sunset`, `Deprecation` Header).

---

## Kriterium 21: Performance unter realen Lasten

### V2-H11: ConsoleDetector.Detect wird pro Datei aufgerufen — kein Caching 🟠
- **Datei:** `Infrastructure/Orchestration/RunOrchestrator.cs` ScanFiles() + `Core/Classification/ConsoleDetector.cs`
- **Problem:** `consoleDetector.Detect(filePath, root)` wird für jede einzelne Datei aufgerufen. Bei 50k+ Dateien in einem Root mit 100+ Console-Definitions werden pro Datei ~100 Extension-Checks und ggf. Header-Reads durchgeführt. Ergebnisse werden nicht gecached (z.B. alle Dateien im selben Ordner haben dieselbe Console).
- **Fix-Vorschlag:** Folder-Level-Caching: einmal pro Ordner detecten, Ergebnis für alle Dateien im Ordner verwenden.

### V2-H12: DeduplicationEngine.Deduplicate materialisiert alle Gruppen sofort 🟠
- **Datei:** `Core/Deduplication/DeduplicationEngine.cs` Deduplicate()
- **Problem:** `GroupBy(...).OrderBy(...).ToList()` materialisiert sofort alle Gruppen in den Speicher. Bei 50k Dateien mit 20k unique GameKeys werden 20k Listen erstellt. Combined mit V2-H01 (ScanFiles unbegrenzt) ergibt sich eine 2x-Amplifikation des Speicherverbrauchs.
- **Auswirkung:** Hoch bei großen Sammlungen — verdoppelt den Peak-Memory-Verbrauch.
- **Fix-Vorschlag:** Yield-basierte Verarbeitung (IEnumerable statt List), oder Streaming.

### V2-M23: GameKeyNormalizer.Normalize — 26 Regex pro Dateiname 🟡
- **Datei:** `Core/GameKeys/GameKeyNormalizer.cs`
- **Problem:** Jeder Dateiname wird durch 26+ kompilierte Regex-Patterns geschickt. Bei 50k Dateien = 1.3M Regex-Evaluierungen. Die Regex sind compiled und gecached, aber der LruCache-Lookup (Hash + LinkedList-Move) pro Datei addiert sich.
- **Auswirkung:** Mittel — compiled Regex sind schnell (~microseconds), aber bei 50k+ Dateien summiert sich die Zeit.

### V2-M24: VersionScorer erstellt 4 Regex-Matches pro Datei 🟡
- **Datei:** `Core/Scoring/VersionScorer.cs` GetVersionScore()
- **Problem:** `_rxVerified.IsMatch`, `_rxRevision.Match`, `_rxVersion.Match`, `_rxLang.Match` — 4 Regex-Evaluierungen pro Datei. Combined mit GameKeyNormalizer (26 Regex) = 30 Regex pro Datei. Bei 50k Dateien = 1.5M Regex-Evaluierungen insgesamt.
- **Auswirkung:** Gering — alle Regex sind compiled, Gesamtdauer ~2-3 Sekunden für 50k Dateien.

### V2-L13: ReportGenerator String-Concatenation für große Reports 🔵
- **Datei:** `Infrastructure/Reporting/ReportGenerator.cs` GenerateHtml()
- **Problem:** `StringBuilder` mit initial capacity 64KB. Bei 50k Report-Entries könnte der Report 10+MB werden. `StringBuilder` verdoppelt intern bei jedem Resize — führt zu temporären LOH-Allokationen.
- **Auswirkung:** Niedrig — Reports werden am Ende der Pipeline generiert, Memory-Pressure ist zu diesem Zeitpunkt bereits am Sinken.

---

## Kriterium 22: Internationalisierung/Unicode

### V2-M25: AsciiFold behandelt nicht alle Unicode-Normalization-Formen 🟡
- **Datei:** `Core/GameKeys/GameKeyNormalizer.cs` AsciiFold()
- **Problem:** `AsciiFold` normalisiert via `NormalizationForm.FormD` und entfernt `NonSpacingMark`, `SpacingCombiningMark`, `EnclosingMark`. Dies funktioniert für die meisten westeuropäischen Diakritika, aber:
  - Ligaturen wie `Æ`→ bleiben erhalten (sollte `AE` werden)
  - `ø`→ bleibt erhalten (sollte `o` werden)
  - `đ`→ bleibt erhalten (sollte `d` werden)
  - Japanische/Chinesische Zeichen werden nicht folded (korrekt — sie sollten erhalten bleiben)
- **Auswirkung:** Mittel — nordische ROM-Namen (z.B. skandinavische Titel mit `Ø`, `Æ`) werden nicht korrekt normalisiert für GameKey-Matching, was zu falschen Duplikaterkennung führen kann.
- **Fix-Vorschlag:** Explizite Mapping-Tabelle für nicht-decomposable Sonderzeichen: `Æ→AE, æ→ae, Ø→O, ø→o, Đ→D, đ→d, Ł→L, ł→l`.

### V2-M26: Log/Report-Timestamps ohne Locale-Bewusstsein 🟡
- **Datei:** `CLI/Program.cs`, `Infrastructure/Reporting/ReportGenerator.cs`
- **Problem:** HTML-Reports verwenden `DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")` (deutsches Format hardcoded). CLI-Audit-Sidecar verwendet `DateTime.Now.ToString("o")` (ISO 8601 — korrekt). Inkonsistenz zwischen Report und Audit. Die hardcoded deutsche Formatierung im Report ist korrekt für die Zielgruppe, aber nicht über i18n konfigurierbar.
- **Auswirkung:** Gering — Projekt ist deutschsprachig.

### V2-L14: Konsolen-Keys sind ASCII-only — korrekt 🔵
- **Datei:** `Infrastructure/Sorting/ConsoleSorter.cs`
- **Befund:** `RxValidConsoleKey = @"^[A-Z0-9_-]+$"` — erzwingt ASCII-only Console-Keys. ✅ Korrekt, da Console-Keys aus `consoles.json` kommen und dort ASCII-only definiert sind.

### V2-L15: CSV-Header sind englisch, Daten können Unicode enthalten 🔵
- **Datei:** `Infrastructure/Reporting/ReportGenerator.cs` GenerateCsv()
- **Befund:** CSV-Header sind ASCII-englisch, aber Datenwerte (GameKey, FileName) können volle Unicode-Strings enthalten. Die CSV wird mit `Encoding.UTF8` geschrieben — korrekt. ✅ Aber: einige CSV-Viewer (Excel) interpretieren UTF-8 ohne BOM nicht korrekt.
- **Fix-Vorschlag (optional):** UTF-8 BOM (`0xEF, 0xBB, 0xBF`) am Anfang der CSV.

---

## Priorisierte Aktionsliste v2

### Phase 1 — Vor Release (Hoch-Priorität)
1. [x] V2-H03: ToolRunnerAdapter._hashCache → ConcurrentDictionary ✅
2. [x] V2-H04: ConversionPipeline Pipeline-Level-Timeout ✅
3. [x] V2-H05: API SSE-Heartbeat ✅
4. [x] V2-H06: SettingsLoader Enum-Validierung (LogLevel, Mode, HashType) ✅
5. [x] V2-H07: MoveItemSafely try/catch statt check-then-act für DUP-Slots ✅
6. [x] V2-H08: API Warnung bei Nicht-Loopback-Bindung ✅
7. [x] V2-H09: Watch-Mode Cooldown-Timer ✅
8. [x] V2-H10: API-Version aus Assembly lesen ✅
9. [x] V2-H11: ConsoleDetector Folder-Level-Caching ✅
10. [x] V2-H12: Deduplicate Dictionary-basiertes Grouping ✅
11. [x] V2-H01: ScanFiles() Memory-Warnung bei >100k Dateien ✅

### Phase 2 — Nach Release (Mittel-Priorität)  
12. [x] V2-M01: Junk-Removal Audit-Trail (JUNK_REMOVE Action) ✅
13. [x] V2-M03: WPF AddLog Shutdown-Safety ✅
14. [x] V2-M05: CLI Double-Ctrl+C Force-Kill ✅
15. [x] V2-M07: WPF ErrorClassifier für Build-Exceptions ✅
16. [x] V2-M08: API Correlation-ID Request↔Run ✅
17. [x] V2-M09: PhaseMetricsCollector in Orchestrator integrieren ✅
18. [x] V2-M10: API Mode-Validierung case-insensitive ✅
19. [x] V2-M20: Move-Bestätigung mit Statistik ✅
20. [x] V2-M25: AsciiFold Ligatur-Tabelle (Æ, Ø, Đ) ✅

### Phase 3 — Nice-to-Have (Niedrig-Priorität)
21. [x] V2-L01: Rollback Datei-Integrität (NTFS-atomar, kein Fix nötig) ✅
22. [x] V2-L03: Log-Rotation DateTime.UtcNow ✅
23. [x] V2-L15: CSV mit UTF-8 BOM ✅
24. [x] V2-M02: Rollback-Stack Max Depth (50) ✅
25. [x] V2-L07: DateTime.Now → DateTime.UtcNow konsistent (8 Stellen) ✅

---

## Methodologie

Jede Datei wurde Zeile für Zeile auf folgende 14 erweiterte Kriterien geprüft:

9. **Datenintegrität & Reversibilität:** Audit-Trail, Rollback-Fähigkeit, Daten-Konsistenz bei Abbruch
10. **Nebenläufigkeit & Threading:** Race Conditions, Lock-Disziplin, ConcurrentCollection-Nutzung
11. **Cancellation/Timeouts/Partial Failure:** CancellationToken-Propagation, Timeout-Strategien, Partial-Failure-Handling
12. **Logging/Observability:** Strukturiertes Logging, Correlation-IDs, Phase-Metriken
13. **Config/Schema-Validierung:** Enum-Validierung, Schema-Enforcement, Default-Fallbacks
14. **Kompatibilität/Plattformannahmen:** Path-Separator, UNC, Zeitzone, Encoding
15. **I/O-Semantik/TOCTOU:** Time-of-Check-to-Time-of-Use, atomische Operationen, Race-Windows
16. **Deterministische Entscheidungen:** Gleiche Inputs → gleiche Outputs, Ordnungs-Abhängigkeiten
17. **Testbarkeit/Seams:** Testbare Grenzen, Dependency-Injection, interne Sichtbarkeit
18. **Security über Code hinaus:** TLS, Key-Management, Bindung, Header-Sicherheit
19. **UX/Produkt-Sicherheit:** Bestätigungs-Dialoge, Throttling, Settings-Persistenz
20. **API/Contract-Versionierung:** Versioning-Strategie, Schema-Sync, Deprecation
21. **Performance unter realen Lasten:** Memory-Verbrauch, Regex-Last, Caching, Batch-Größen
22. **Internationalisierung/Unicode:** ASCII-Fold-Vollständigkeit, Encoding, Locale-Awareness
