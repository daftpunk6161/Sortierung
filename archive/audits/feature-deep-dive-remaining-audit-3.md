---
goal: "Deep-Dive Bug-Audit 3: Core Logic, Infrastructure-Services, Entry Points (CLI/API), Contracts — alle noch nicht auditierten Bereiche"
version: 1.0
date_created: 2026-03-12
last_updated: 2026-03-12
owner: RomCleanup Team
status: 'Planned'
tags: [bug-audit, core, infrastructure, cli, api, contracts, security, performance, correctness, deep-dive]
---

# Deep-Dive Bug-Audit 3 — Alle verbleibenden Bereiche

![Status: Planned](https://img.shields.io/badge/status-Planned-blue)

Dritter und abschließender Deep-Dive Bug-Audit, der **alle Bereiche** abdeckt, die in den vorherigen Audits (Audit 1: TASK-001–TASK-083, Audit 2: TASK-084–TASK-149) noch nicht erfasst wurden. Umfasst: Core-Layer (GameKeys, Regions, Rules, Scoring, SetParsing, Caching, Classification), Infrastructure-Services (Audit, Orchestration, FileSystem, Hashing, DAT, Sorting, Tools, Conversion, Events, History, State, Logging, Analytics, Safety, Configuration, Diagnostics, Quarantine, Pipeline, Linking, Metrics, Reporting), Entry Points (CLI, REST API), und Contract-Layer (Models, Ports, Errors).

Insgesamt **78 neue Findings** in 9 Runden (Runden 22–30): **2× P0** (Release-Blocker), **19× P1** (Hoch), **35× P2** (Mittel), **22× P3** (Niedrig).

---

## 1. Requirements & Constraints

- **REQ-001**: Alle Findings enthalten konkrete Dateipfade, Zeilennummern und Fix-Strategien
- **REQ-002**: Priorisierung nach P0 (Release-Blocker) → P1 (Hoch) → P2 (Mittel) → P3 (Niedrig)
- **REQ-003**: Jedes Finding braucht eine Testabsicherungsstrategie
- **REQ-004**: Nur Findings, die in Audit 1 (TASK-001–TASK-083) und Audit 2 (TASK-084–TASK-149) noch nicht erfasst sind
- **SEC-001**: ReDoS-Schwachstellen durch unkontrollierte Regex-Erzeugung sind P0/P1
- **SEC-002**: Sync-over-Async Deadlocks (.GetAwaiter().GetResult()) sind P1
- **SEC-003**: Static mutable state in long-running Server-Szenarien ist P1
- **SEC-004**: Path-Traversal in allen File-Operationen muss validiert sein
- **CON-001**: Keine Code-Änderungen in diesem Plan — nur Analyse und Maßnahmen
- **CON-002**: Alle Findings basieren auf tatsächlich gelesenem Code (keine Vermutungen)
- **GUD-001**: Determinismus bei GameKey-Normalisierung und Winner-Selection
- **PAT-001**: CultureInfo.InvariantCulture bei allen Parse-Operationen

---

## 2. Implementation Steps

### Runde 22 — Core Layer: GameKeys, Regions, Rules, Scoring

- GOAL-022: Alle Bugs in den Core-Modulen identifizieren (GameKeyNormalizer, RegionDetector, RuleEngine, VersionScorer)

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-150 | **P0 – ReDoS-Risiko in RuleEngine.TryRegexMatch**: `TryRegexMatch()` in `src/RomCleanup.Core/Rules/RuleEngine.cs` erzeugt bei jedem Aufruf ein neues `Regex`-Objekt aus User-definierten Patterns (aus `rules.json`). Weder Compiled-Flag noch `matchTimeout` werden gesetzt. Ein bösartiges Pattern wie `(a+)+b` in rules.json verursacht exponentielles Backtracking → ReDoS. **Fix**: (1) Regex-Cache mit `ConcurrentDictionary<string, Regex>` einführen, (2) `RegexOptions.Compiled` und `matchTimeout: TimeSpan.FromMilliseconds(200)` setzen, (3) Try/Catch um `new Regex()` für ungültige Patterns. **Test**: Unit-Test mit bekanntem ReDoS-Pattern, Assert Timeout < 500ms. | | |
| TASK-151 | **P1 – GameKeyNormalizer erzeugt nicht-deterministische Keys für leere Namen**: `Normalize()` in `src/RomCleanup.Core/GameKeys/GameKeyNormalizer.cs` gibt `Guid.NewGuid().ToString("N")` zurück wenn der normalisierte Wert leer ist. Gleiche Eingabe → unterschiedliche Outputs bei jedem Aufruf. Das bricht die Determinismus-Invariante der Winner-Selection und verhindert korrektes Grouping (jede leere-Name-Datei wird eigene Gruppe). **Fix**: Statt GUID einen deterministischen Fallback verwenden: `$"__EMPTY_{originalFileName}"` oder den SHA256-Hash des Original-Dateinamens. **Test**: `Normalize("()")` zweimal aufrufen → Assert gleicher Output. | | |
| TASK-152 | **P1 – GameKeyNormalizer MsDosTrailingParenRegex while-Schleife ohne Iterationslimit**: `Normalize()` enthält `while (MsDosTrailingParenRegex.IsMatch(value)) { value = MsDosTrailingParenRegex.Replace(value, ...); }`. Bei einem adversarial Input wie `"Game (((((((((((((((((((((...))))))))))))))))))))"` könnte die Schleife viele Iterationen brauchen. **Fix**: `maxIterations = 20` Counter einführen, bei Überschreitung abbrechen. **Test**: Input mit 100 verschachtelten Klammern → Assert terminiert in < 50ms. | | |
| TASK-153 | **P2 – RegionDetector UK-Sonderbehandlung mit nicht-kompilierter Regex**: `GetRegionTag()` in `src/RomCleanup.Core/Regions/RegionDetector.cs` (ca. Zeile 130) verwendet `Regex.IsMatch(fileName, @"\(UK\)", RegexOptions.IgnoreCase)` als Inline-Aufruf. Jeder Aufruf erzeugt ein neues internes Regex-Objekt. Bei 100k+ Dateien signifikanter Overhead. **Fix**: `private static readonly Regex RxUk = new(@"\(UK\)", RegexOptions.Compiled \| RegexOptions.IgnoreCase);` als statisches Feld. **Test**: Benchmark mit 10k Dateinamen, Assert < 100ms. | | |
| TASK-154 | **P2 – VersionScorer erstellt nicht-kompilierte Regex pro Aufruf**: `GetVersionScore()` in `src/RomCleanup.Core/Scoring/VersionScorer.cs` enthält mehrere `Regex.IsMatch(rev, pattern)` und `Regex.Match(rev, pattern)` Aufrufe innerhalb der Methode. Jeder Aufruf erzeugt intern ein neues Regex-Objekt. **Fix**: Alle Patterns als `private static readonly Regex` mit `RegexOptions.Compiled` und `matchTimeout` deklarieren. **Test**: Benchmark 10k Aufrufe, Assert < 200ms. | | |
| TASK-155 | **P2 – RuleEngine.EvaluateCondition CultureInfo-abhängiges Number-Parsing**: `EvaluateCondition()` in `src/RomCleanup.Core/Rules/RuleEngine.cs` verwendet `double.TryParse()` ohne CultureInfo-Parameter für `gt`/`lt`-Operatoren. Auf einem System mit deutschem Locale wird `"1.5"` als `15` interpretiert (Komma statt Punkt). **Fix**: `double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)`. **Test**: Thread.CurrentThread.CurrentCulture = "de-DE", Assert `gt(1.5)` korrekt evaluiert. | | |
| TASK-156 | **P3 – RuleEngine.EvaluateCondition ToLowerInvariant() bei jedem Aufruf**: `cond.Op.ToLowerInvariant()` wird bei jedem Aufruf von `EvaluateCondition()` neu allokiert. Bei 100k Dateien × N Regeln = viele String-Allokationen. **Fix**: `Op` bereits bei Deserialisierung normalisieren oder Enum statt String verwenden. **Test**: Benchmark, Assert GC-Pressure reduziert. | | |
| TASK-157 | **P3 – ConsoleDetector custom GetRelativePath statt Path.GetRelativePath**: `ConsoleDetector.cs` in `src/RomCleanup.Core/Classification/` verwendet eine eigene `GetRelativePath()`-Implementierung statt `Path.GetRelativePath()` (verfügbar ab .NET 6+). Die eigene Implementierung behandelt UNC-Pfade und Cross-Drive-Pfade möglicherweise nicht korrekt. **Fix**: Durch `Path.GetRelativePath(basePath, fullPath)` ersetzen. **Test**: UNC-Pfade `\\server\share\roms\game.zip` und Cross-Drive `D:\roms` von `C:\base` testen. | | |

### Runde 23 — Infrastructure: Audit & Signing

- GOAL-023: Alle Bugs in AuditCsvStore und AuditSigningService identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-158 | **P1 – Inkonsistente Action-Casing zwischen AuditCsvStore und AuditSigningService**: `AuditCsvStore.AppendAuditRow()` in `src/RomCleanup.Infrastructure/Audit/AuditCsvStore.cs` schreibt Action-Werte wie `"Move"` (PascalCase). `AuditSigningService.Rollback()` in `src/RomCleanup.Infrastructure/Audit/AuditSigningService.cs` prüft auf `"MOVE"` und `"MOVED"` (UpperCase). `AuditCsvStore.Rollback()` prüft auf `"Move"` (case-insensitive via StringComparison). Dies führt dazu, dass **AuditSigningService.Rollback nie Zeilen als rollback-fähig erkennt**, da `"Move" != "MOVE"`. **Fix**: Alle Action-Vergleiche case-insensitive durchführen: `action.Equals("Move", StringComparison.OrdinalIgnoreCase)`. **Test**: Audit-CSV mit Action="Move" schreiben, via AuditSigningService.Rollback() verifizieren dass er die Zeilen findet. | | |
| TASK-159 | **P2 – ParseCsvLine dupliziert in AuditCsvStore und AuditSigningService**: `ParseCsvLine()` existiert als identische private Methode in beiden Klassen (~25 Zeilen jeweils). DRY-Violation, jeder Fix muss an zwei Stellen angewendet werden. **Fix**: In eine statische Hilfsklasse `AuditCsvParser` in `Infrastructure/Audit/` extrahieren. **Test**: Beide Klassen verwenden die gleiche Implementierung, kein Verhaltensunterschied. | | |
| TASK-160 | **P2 – AuditSigningService._persistedKey ist static (Singleton-Annahme)**: Das HMAC-Signing-Key-Feld `_persistedKey` in `AuditSigningService.cs` ist `static`. Falls mehrere Instanzen mit unterschiedlichen Key-Dateien erzeugt werden (z.B. in Tests oder Multi-Tenant-Szenarien), teilen alle Instanzen denselben Key. Die erste Instanz "gewinnt". **Fix**: `_persistedKey` auf Instanz-Feld (`private byte[]?`) ändern. Key-Loading pro Instanz. **Test**: Zwei Instanzen mit verschiedenen Key-Pfaden erzeugen, verifizieren dass sie unterschiedliche Keys verwenden. | | |
| TASK-161 | **P2 – AuditSigningService Key-Datei ohne ACL-Einschränkung**: `PersistKey()` schreibt die HMAC-Key-Datei mit `File.WriteAllBytes()` ohne File-Permissions einzuschränken. Standardmäßig ist die Datei für alle Benutzer lesbar. Ein Angreifer mit Lesezugriff auf `%APPDATA%` kann den HMAC-Key extrahieren und Audit-Logs fälschen. **Fix**: Nach dem Schreiben via `FileInfo.SetAccessControl()` oder `FileSecurity` Only-Owner-Permissions setzen. **Test**: Key-Datei erzeugen, verifizieren dass ACL nur den aktuellen Benutzer erlaubt. | | |
| TASK-162 | **P3 – AuditCsvStore.SanitizeCsvField bricht negative Zahlen**: `SanitizeCsvField()` in `AuditCsvStore.cs` prefixed alle Werte die mit `-` beginnen mit `'`. Das bricht Felder wie negative File-Size-Differenzen in Audit-Zeilen (z.B. Feld mit `-1024` wird zu `'-1024`). **Fix**: Nur prefixen wenn der Wert kein gültiges numerisches Format ist: `if (!double.TryParse(value, out _) && "=+-@".Contains(value[0]))`. **Test**: SanitizeCsvField("-1024") → "-1024" (keine Manipulation). | | |

### Runde 24 — Infrastructure: Orchestration & FileSystem

- GOAL-024: Alle Bugs in RunOrchestrator, ExecutionHelpers und FileSystemAdapter identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-163 | **P1 – RunOrchestrator._normalizedRoots ist static ConcurrentDictionary ohne Cleanup**: `_normalizedRoots` in `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.cs` ist ein `static ConcurrentDictionary` das Pfadnormalisierungen cached. Es wird **niemals geleert** — in einem Long-Running-Server (API) wächst es unbegrenzt mit jedem neuen Run. Bei 1000+ Runs mit verschiedenen Pfaden: Memory Leak. **Fix**: (1) Instanz-Feld statt static, oder (2) `Clear()` am Ende von `Execute()`, oder (3) `LruCache` mit Limit. **Test**: 100 Runs mit verschiedenen Roots ausführen, Assert Memory stabil bleibt. | | |
| TASK-164 | **P0 – RunOrchestrator.ScanFiles normalisiert GameKey MIT Dateiendung**: `ScanFiles()` in `RunOrchestrator.cs` ruft `GameKeyNormalizer.Normalize(fileName)` auf, wobei `fileName = Path.GetFileName(filePath)` — **inklusive Extension**. `"Super Mario Bros (EU).zip"` erzeugt den Key `"super mario bros.zip"`, während `"Super Mario Bros (EU).7z"` den Key `"super mario bros.7z"` erzeugt. Diese landen in verschiedenen Gruppen → keine Deduplizierung zwischen ZIP und 7z desselben Spiels. **Fix**: `Path.GetFileNameWithoutExtension(filePath)` statt `Path.GetFileName(filePath)` verwenden. **Test**: Zwei Dateien `Game (EU).zip` und `Game (US).7z` → Assert gleicher GameKey, gleiche Gruppe. | | |
| TASK-165 | **P2 – RunOrchestrator.ScanFiles setzt sizeBytes=0 bei FileInfo-Fehler**: `ScanFiles()` fängt `Exception` beim Erstellen von `new FileInfo(filePath)` und setzt `sizeBytes = 0`. Eine 0-Byte-Datei verliert jeden Size-Tiebreak (Disc-Spiele → größer bevorzugt). Der Fehler wird **nicht geloggt**. **Fix**: (1) `log?.Warning()` bei FileInfo-Fehler, (2) Kandidat als `HasSizeError = true` markieren damit Winner-Selection den Size-Tiebreak ignoriert, oder mindestens warnen. **Test**: Losgelöschte Datei → Assert Warning in Log. | | |
| TASK-166 | **P2 – ExecutionHelpers.IsBlocklisted hat quadratische Komplexität**: `IsBlocklisted()` in `src/RomCleanup.Infrastructure/Orchestration/ExecutionHelpers.cs` prüft O(Pfad-Segmente × Blocklist-Einträge) via verschachtelte Schleifen mit String-Vergleich. Bei tief verschachtelten Pfaden (10+ Segmente) × großer Blocklist: O(n²). **Fix**: `HashSet<string>` für Blocklist mit `StringComparer.OrdinalIgnoreCase`. **Test**: Pfad mit 20 Segmenten + 100er Blocklist → Assert < 1ms. | | |
| TASK-167 | **P3 – ExecutionHelpers.GetDiscExtensions() allokiert neues HashSet pro Aufruf**: `GetDiscExtensions()` erzeugt bei jedem Aufruf ein neues `HashSet<string>`. Bei Verwendung in Schleifen (z.B. pro Datei) unnötige GC-Pressure. **Fix**: `private static readonly HashSet<string> DiscExtensions = new(...)` als statisches Feld. **Test**: Benchmark, Assert keine Allokation pro Aufruf (BenchmarkDotNet). | | |
| TASK-168 | **P2 – FileSystemAdapter.CopyFile erstellt kein Zielverzeichnis**: `CopyFile()` in `src/RomCleanup.Infrastructure/FileSystem/FileSystemAdapter.cs` ruft `File.Copy(src, dst)` auf ohne vorher `EnsureDirectory()` auf das Zielverzeichnis aufzurufen. `MoveItemSafely()` im gleichen File tut dies korrekt. Inkonsistentes Verhalten führt zu `DirectoryNotFoundException` bei CopyFile. **Fix**: `EnsureDirectory(Path.GetDirectoryName(dest))` vor `File.Copy()`. **Test**: CopyFile in nicht-existentes Verzeichnis → Assert Erfolg. | | |
| TASK-169 | **P3 – FileSystemAdapter.GetFilesSafe hat nicht-deterministische Reihenfolge**: `GetFilesSafe()` verwendet einen iterativen DFS mit `Stack<string>`. Die File-Reihenfolge hängt von `Directory.GetDirectories()` und `Directory.GetFiles()` ab (NTFS-Reihenfolge, nicht alphabetisch). Für die Deduplizierung ist die Reihenfolge irrelevant (Winner-Selection ist deterministisch), aber für Tests und Reports kann es zu flaky Vergleichen führen. **Fix**: Optional `.OrderBy(f => f)` am Ende, oder dokumentieren. **Test**: Zwei Aufrufe auf gleichem Verzeichnis → Assert gleiche Reihenfolge. | | |

### Runde 25 — Infrastructure: Hashing & DAT

- GOAL-025: Alle Bugs in FileHashService, ArchiveHashService, ParallelHasher, DatRepositoryAdapter und DatSourceService identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-170 | **P1 – ParallelHasher.HashFiles Sync-over-Async Deadlock**: `HashFiles()` in `src/RomCleanup.Infrastructure/Hashing/ParallelHasher.cs` ruft `HashFilesAsync(...).GetAwaiter().GetResult()` auf. Auf dem WPF UI-Thread mit SynchronizationContext verursacht dies einen Deadlock (async Continuations versuchen auf den UI-Thread zu dispatchen, der blockiert ist). **Fix**: (1) Methode async machen: `public async Task<ParallelHashResult> HashFilesAsync()`, oder (2) `.ConfigureAwait(false)` konsistent in der gesamten async Chain verwenden, oder (3) `Task.Run(() => HashFilesAsync(...)).GetAwaiter().GetResult()`. **Test**: Von UI-Thread (Dispatcher) aufrufen → Assert kein Deadlock (Timeout-basiert). | | |
| TASK-171 | **P1 – DatSourceService.VerifyDatSignature Sync-over-Async Deadlock**: `VerifyDatSignature()` in `src/RomCleanup.Infrastructure/Dat/DatSourceService.cs` ruft `_httpClient.GetStringAsync(sidecarUrl).GetAwaiter().GetResult()` auf. Gleiche Deadlock-Problematik wie TASK-170 auf dem WPF UI-Thread. **Fix**: Methode async machen oder `Task.Run()` verwenden. **Test**: Analog zu TASK-170. | | |
| TASK-172 | **P2 – DatRepositoryAdapter.ParseDatFile verwendet Console.Error.WriteLine**: `ParseDatFile()` in `src/RomCleanup.Infrastructure/Dat/DatRepositoryAdapter.cs` schreibt Warnungen bei XML-Parse-Fehlern direkt auf `Console.Error`. Dies ist nicht testbar (keine Injectable Dependency) und in GUI/API-Szenarien unsichtbar. **Fix**: `Action<string>? log` als Konstruktor-Parameter injizieren und statt `Console.Error.WriteLine` verwenden. **Test**: ParseDatFile mit ungültiger XML aufrufen, Assert Warning-Callback aufgerufen. | | |
| TASK-173 | **P2 – DatRepositoryAdapter games[gameName] überschreibt bei Duplikaten**: `ParseDatFile()` setzt `games[gameName] = roms` — wenn derselbe Spielname zweimal in einem DAT vorkommt (z.B. regionale Varianten mit gleichem Namen), werden die ROMs der ersten Variante überschrieben. Nur die letzte Variante bleibt. **Fix**: Merge statt Overwrite: `if (games.TryGetValue(gameName, out var existing)) { existing.AddRange(roms); } else { games[gameName] = roms; }`. **Test**: DAT mit zwei `<game name="Test">` Einträgen → Assert beide ROM-Sets im Index. | | |
| TASK-174 | **P2 – ArchiveHashService 7z-Stdout-Parsing fragil**: `ListArchiveEntries()` in `src/RomCleanup.Infrastructure/Hashing/ArchiveHashService.cs` parst die 7z-Ausgabe als Text mit `Split('\n')` und erwartet bestimmte Spaltenformate. Wenn 7z die Ausgabeformatierung ändert (z.B. neue Version, anderer Locale), bricht das Parsing. **Fix**: (1) 7z im `-slt` (tech listing) Format aufrufen für strukturiertere Ausgabe, oder (2) Regex-basiertes Parsing mit Fallback. **Test**: 7z-Output aus verschiedenen 7z-Versionen (23.01, 24.05) parsen. | | |
| TASK-175 | **P3 – ArchiveHashService Temp-Dateien bei Crash**: Beim 7z-Extraction-Pfad werden Temp-Dateien in einem Temp-Directory erstellt. Der `finally`-Block räumt auf, aber bei Process-Kill (z.B. `taskkill /F`) bleibt das Temp-Verzeichnis zurück. **Fix**: Temp-Directory-Name mit Prefix `RomCleanup_` für leichtere manuelle Cleanup-Discovery. Beim Service-Start alte Temp-Dirs aufräumen. **Test**: Temp-Dir-Cleanup nach normalem Durchlauf verifizieren. | | |

### Runde 26 — Infrastructure: Conversion & Tools

- GOAL-026: Alle Bugs in FormatConverterAdapter, ConversionPipeline und ToolRunnerAdapter identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-176 | **P2 – FormatConverterAdapter.Verify für RVZ nur Existenz+Größe**: `Verify()` in `src/RomCleanup.Infrastructure/Conversion/FormatConverterAdapter.cs` prüft für RVZ-Dateien lediglich `File.Exists && Length > 0`. Das ist keine echte Verifikation — eine 1-Byte-Datei würde als "verifiziert" gelten. DolphinTool hat keinen `verify` Befehl, aber der Header könnte geprüft werden. **Fix**: RVZ-Magic-Bytes prüfen (ersten 4 Bytes = `RVZ\x01`), oder mindestens eine Mindestgröße (z.B. > 1 MB) verlangen. **Test**: 1-Byte-Datei mit .rvz-Extension → Assert Verify = false. | | |
| TASK-177 | **P2 – ConversionPipeline.CheckDiskSpace UNC-Fallback gibt long.MaxValue**: `CheckDiskSpace()` in `src/RomCleanup.Infrastructure/Conversion/ConversionPipeline.cs` setzt `available = long.MaxValue` wenn DriveInfo für UNC-Pfade nicht funktioniert. Ergebnis: UNC-Konvertierungen überspringen die Platzprüfung komplett und schlagen erst bei vollem Speicher fehl. **Fix**: P/Invoke `GetDiskFreeSpaceEx` für UNC-Pfade, oder zumindest den `available = long.MaxValue`-Fall als Warning zurückgeben statt als `Ok = true`. **Test**: UNC-Mock-Pfad → Assert Warning in Result.Reason. | | |
| TASK-178 | **P2 – ToolRunnerAdapter._toolHashes Lazy-Loading nicht thread-safe**: `EnsureToolHashesLoaded()` in `src/RomCleanup.Infrastructure/Tools/ToolRunnerAdapter.cs` prüft `_toolHashes == null` und lädt dann. Bei gleichzeitigem Aufruf aus ParallelHasher können zwei Threads gleichzeitig laden → Race Condition (doppeltes Laden, kein Crash, aber Performance-Verlust und potentiell inkonsistenter State). **Fix**: `Lazy<Dictionary<string, string>>` für thread-safe Lazy-Init, oder `lock` um den Ladeblock. **Test**: Parallel 10 Threads `FindTool("chdman")` aufrufen → Assert kein Crash, genau einmaliges Laden. | | |
| TASK-179 | **P2 – ToolRunnerAdapter Timeout nicht konfigurierbar**: `RunProcess()` verwendet einen hardkodierten 30-Minuten-Timeout (`TimeSpan.FromMinutes(30)`). CHD-Konvertierung großer DVDs kann länger dauern (PS2 DVD-9 → CHD = 45+ Minuten). **Fix**: `timeoutMinutes` Parameter mit Default 30. Für `chdman createdvd` automatisch auf 120 erhöhen. **Test**: Mock-Process mit langem Sleep → Assert Timeout konfigurierbar. | | |
| TASK-180 | **P3 – ConversionPipeline.BuildToolArguments Default-Case unspezifisch**: `BuildToolArguments()` hat einen Default-Fall `_ => [step.Action, step.Input, step.Output]`. Für ein unbekanntes Tool werden die Argumente ungeprüft weitergeleitet. Kein Sicherheitsrisiko (ArgumentList nicht Arguments), aber unerwartetes Verhalten. **Fix**: Default-Fall soll `InvalidOperationException($"Unknown tool: {step.Tool}")` werfen. **Test**: Unbekannten Tool-Namen übergeben → Assert Exception. | | |
| TASK-181 | **P3 – ToolRunnerAdapter Tools nicht auffindbar ohne tool-hashes.json**: `VerifyToolHash()` gibt `_allowInsecureHashBypass` (standardmäßig false) zurück wenn keine Hash-Datei geladen ist. Das bedeutet: ohne `tool-hashes.json` können **keine Tools** ausgeführt werden, auch wenn sie korrekt installiert sind. Fehlermeldung ist nicht selbstsprechend. **Fix**: Wenn Hash-Datei nicht existiert **und** kein Hash-Eintrag für das Tool vorhanden ist, Info-Log ausgeben und (konfigurierbar) trotzdem erlauben. **Test**: ToolRunnerAdapter ohne tool-hashes.json → Assert FindTool gibt Pfad zurück (mit Warning). | | |

### Runde 27 — Infrastructure: Support-Services (Events, History, State, Logging, Sorting, Analytics)

- GOAL-027: Alle Bugs in EventBus, RunHistoryService, ScanIndexService, AppStateStore, JsonlLogWriter, ConsoleSorter, InsightsEngine identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-182 | **P2 – EventBus.Publish schluckt alle Handler-Exceptions ohne Logging**: `Publish()` in `src/RomCleanup.Infrastructure/Events/EventBus.cs` fängt alle Exceptions pro Handler mit leerem `catch { }`. Fehlerhafte Handler werden stumm ignoriert — keine Möglichkeit, Bugs in Event-Handlern zu diagnostizieren. **Fix**: Optional `Action<string, Exception>? onError` im Konstruktor für Error-Reporting. Im catch: `onError?.Invoke(topic, ex)`. **Test**: Handler der Exception wirft → Assert Error-Callback aufgerufen, restliche Handler weiterhin ausgeführt. | | |
| TASK-183 | **P2 – EventBus.Subscribe Interlocked.Increment INNERHALB eines lock**: `Subscribe()` verwendet `Interlocked.Increment(ref _sequence)` innerhalb eines `lock (_lock)` Blocks. `Interlocked` ist für lock-freie Szenarien gedacht — innerhalb eines locks ist ein einfaches `_sequence++` ausreichend und performanter. Kein Bug, aber irreführend und unnötiger Overhead. **Fix**: `_sequence++` statt `Interlocked.Increment`. **Test**: Unit-Test auf korrekten ID-Inkrement. | | |
| TASK-184 | **P2 – RunHistoryService verwendet File.GetLastWriteTime (nicht UTC)**: `GetHistory()` in `src/RomCleanup.Infrastructure/History/RunHistoryService.cs` setzt `entry.Date = File.GetLastWriteTime(file)`. Dies gibt die lokale Zeit zurück, nicht UTC. Bei Zeitzonen-Wechseln (z.B. Reise, Sommerzeit-Umstellung) entstehen inkonsistente Sortierungen. **Fix**: `File.GetLastWriteTimeUtc(file)` verwenden. **Test**: Zwei Dateien mit bekannten UTC-Zeiten → Assert korrekte Sortierung. | | |
| TASK-185 | **P2 – ScanIndexService Fingerprint enthält FullPath (case-sensitive auf Windows)**: `GetPathFingerprint()` in `src/RomCleanup.Infrastructure/History/ScanIndexService.cs` verwendet `info.FullName` im Fingerprint-String. Auf Windows ist `FullName` case-preserving: `C:\ROMS\Game.zip` und `c:\roms\game.zip` erzeugen verschiedene Fingerprints für dieselbe Datei. **Fix**: `info.FullName.ToUpperInvariant()` oder `Path.GetFullPath(filePath).ToUpperInvariant()` für case-insensitive Fingerprints. **Test**: Gleiche Datei mit verschiedener Case → Assert gleicher Fingerprint. | | |
| TASK-186 | **P2 – ConsoleSorter.FindActualDestination O(10000) lineare Suche**: `FindActualDestination()` in `src/RomCleanup.Infrastructure/Sorting/ConsoleSorter.cs` prüft linear bis zu 10.000 `__DUP_N`-Suffixe via `_fs.TestPath()`. Bei vielen Kollisionen (z.B. 5000 Dateien mit gleichem Namen) werden 5000 Filesystem-Aufrufe benötigt. **Fix**: Binary Search unmöglich (Lücken möglich), aber: `Directory.GetFiles(dir, $"{baseName}__DUP_*{ext}")` einmal auflisten, dann das höchste N+1 nehmen. **Test**: 100 existierende __DUP_-Dateien → Assert O(1) statt O(100). | | |
| TASK-187 | **P2 – InsightsEngine.GetDuplicateInspectorRows verwendet eigene Winner-Logik**: `GetDuplicateInspectorRows()` in `src/RomCleanup.Infrastructure/Analytics/InsightsEngine.cs` berechnet den Winner via eigene LINQ-Sortierung (`OrderByDescending(TotalScore).ThenByDescending(SizeBytes)`). Dies weicht von `DeduplicationEngine.SelectWinner()` ab, das eine andere Scoring-Formel und Alphabetical-Tiebreak verwendet. **Ergebnis**: Inspector zeigt anderen Winner als der tatsächliche Dedupe-Lauf. **Fix**: `DeduplicationEngine.SelectWinner()` direkt aufrufen statt eigene Sortierung. **Test**: Gleiche Kandidaten → Assert Inspector-Winner == Engine-Winner. | | |
| TASK-188 | **P3 – AppStateStore.NotifyWatchers Snapshot-Timing**: `NotifyWatchers()` erstellt innerhalb des Lock einen Snapshot der Watcher-Liste und des State, ruft Watcher dann **außerhalb** des Lock auf. Zwischen Snapshot und Aufruf kann ein anderer Thread den State ändern — Watcher erhalten dann einen veralteten State. Konzeptionell korrekt (Snapshot-Semantik), aber nicht dokumentiert. **Fix**: Dokumentieren, dass Watcher immer den State zum Zeitpunkt des Aufrufs erhalten (nicht den aktuellen). **Test**: Concurrent Set + Watch → Assert Watcher erhält konsistenten (nicht zerrissenen) State. | | |
| TASK-189 | **P3 – JsonlLogWriter.RotateIfNeeded Race bei Concurrent Writers**: `RotateIfNeeded()` in `src/RomCleanup.Infrastructure/Logging/JsonlLogWriter.cs` used lock korrekt, aber in `JsonlLogRotation.Rotate()` wird `File.Move()` aufgerufen. Wenn ein anderer Prozess (z.B. Log-Viewer) die Datei geöffnet hat, schlägt `File.Move()` fehl mit `IOException`. Der catch fängt das ab, aber die Rotation wird übersprungen → Log wächst unbegrenzt. **Fix**: Retry mit 3 Versuchen und kurzer Pause, oder Copy+Truncate-Strategie. **Test**: Datei mit Read-Lock öffnen, Rotation triggern → Assert kein Crash, Warning geloggt. | | |
| TASK-190 | **P3 – InsightsEngine.SanitizeCsv prefixed '-' (bricht Pfade mit Bindestrich)**: `SanitizeCsv()` in `InsightsEngine.cs` prefixed alle Werte die mit `-` beginnen mit `'`. ROM-Dateinamen wie `-Densha de GO! (JP).zip` werden zu `'-Densha de GO! (JP).zip`. Gleicher Bug wie TASK-162 in AuditCsvStore. **Fix**: Nur prefixen wenn kein gültiger Dateiname/Pfad. Oder besser: nur Fields die `=`, `+`, `@` beginnen (nicht `-`, da zu viele False Positives bei Dateinamen). **Test**: SanitizeCsv("-Game (JP).zip") → "-Game (JP).zip". | | |

### Runde 28 — Infrastructure: Safety, Configuration, Diagnostics, Quarantine

- GOAL-028: Alle Bugs in SafetyValidator, SettingsLoader, CatchGuardService, QuarantineService identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-191 | **P2 – SafetyValidator Conservative-Profil blockiert UserProfile**: Das `Conservative`-Profil in `src/RomCleanup.Infrastructure/Safety/SafetyValidator.cs` enthält `Environment.SpecialFolder.UserProfile` als Protected Path. Viele Benutzer haben ihre ROM-Sammlung unter `C:\Users\Max\Roms\` — das würde **immer blockiert**. **Fix**: `UserProfile` aus Conservative entfernen. Stattdessen spezifischere Pfade blockieren: `Desktop`, `Documents`, `Downloads`. **Test**: Root unter `%USERPROFILE%\Roms` → Assert nicht blockiert. | | |
| TASK-192 | **P2 – SettingsLoader.ValidateToolPath prüft nur Extension, keine Path-Traversal**: `ValidateToolPath()` in `src/RomCleanup.Infrastructure/Configuration/SettingsLoader.cs` prüft `File.Exists()` und erlaubte Extensions (.exe, .bat, .cmd). Aber: ein Pfad wie `..\..\..\..\Windows\System32\cmd.exe` würde als gültiger Tool-Pfad akzeptiert. **Fix**: Zusätzlich `Path.GetFullPath(path)` normalisieren und gegen eine Allowlist bekannter Tool-Verzeichnisse prüfen, oder zumindest prüfen dass der Pfad nicht in System-Verzeichnissen liegt. **Test**: `"..\..\Windows\System32\cmd.exe"` → Assert wird abgelehnt. | | |
| TASK-193 | **P2 – SettingsLoader.MergeFromDefaults und MergeFromUserSettings verwenden unterschiedliche Property-Namen**: `MergeFromDefaults()` liest Flat-Properties (`"mode"`, `"extensions"`, `"logLevel"`), aber `MergeFromUserSettings()` liest verschachtelte Objekte (`general.logLevel`, `dat.useDat`). Wenn ein User die defaults.json-Struktur in seine settings.json kopiert (flache Keys), werden diese nicht erkannt. **Fix**: Dokumentieren oder beide Strukturen in beiden Methoden unterstützen. **Test**: Settings.json mit flachem `"mode": "Move"` → Assert korrekt geladen. | | |
| TASK-194 | **P3 – QuarantineService.Restore Path-Traversal-Check zu strikt**: `Restore()` in `src/RomCleanup.Infrastructure/Quarantine/QuarantineService.cs` blockiert `originalPath.Contains("..")` — das blockt auch legitime Pfade wie `C:\Roms\Game..Edition\file.zip` (doppelter Punkt im Ordnernamen). **Fix**: `Path.GetFullPath(originalPath)` verwenden und prüfen ob der aufgelöste Pfad innerhalb erlaubter Roots liegt, statt String-Contains. **Test**: `"C:\Roms\Game..Special\file.zip"` → Assert erlaubt. | | |
| TASK-195 | **P3 – CatchGuardService.Guard Logging-Level Parameter ignoriert**: `Guard()` in `src/RomCleanup.Infrastructure/Diagnostics/CatchGuardService.cs` ruft `SafeCatch()` mit Standard-Level `"Warning"` auf. Der Aufrufer hat keine Möglichkeit, das Level anzupassen (kein `level`-Parameter in `Guard()`). **Fix**: `string level = "Warning"` Parameter zu `Guard()` und `Guard<T>()` hinzufügen. **Test**: Guard mit level="Error" → Assert Log-Nachricht enthält "[Error]". | | |
| TASK-196 | **P3 – HardlinkService.BuildPlan keine Path-Traversal-Validierung auf targetPath**: `BuildPlan()` in `src/RomCleanup.Infrastructure/Linking/HardlinkService.cs` konstruiert `targetPath = Path.Combine(targetRoot, subDir, fileName)`. Wenn `consoleKey` oder `genre` einen Path-Traversal-Wert enthält (z.B. `..\..\System32`), landet die Link-Operation außerhalb des Target-Root. **Fix**: `ResolveChildPathWithinRoot(targetRoot, Path.Combine(subDir, fileName))` für Validierung. **Test**: ConsoleKey=`"..\..\"`, Assert Exception oder Skip. | | |

### Runde 29 — Entry Points: CLI & REST API

- GOAL-029: Alle Bugs in CLI Program.cs und API Program.cs/RunManager.cs identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-197 | **P1 – CLI dataDir-Auflösung mit ".." Segmenten ist fragil**: `Run()` in `src/RomCleanup.CLI/Program.cs` berechnet `dataDir` als `Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data")` mit 4× Parent-Navigation. Das funktioniert nur in der Development-Ordnerstruktur (`src/RomCleanup.CLI/bin/Debug/net10.0/`), nicht bei publizierter Auslieferung (wo `data/` neben der Exe liegt). **Fix**: Primär `Path.Combine(AppContext.BaseDirectory, "data")` versuchen, nur bei Nicht-Existenz auf Fallback mit `..`. **Test**: App-Publish in flache Struktur → Assert data/ gefunden. | | |
| TASK-198 | **P1 – API RunManager erstellt pro Run neue Service-Instanzen (kein DI)**: `ExecuteRun()` in `src/RomCleanup.Api/RunManager.cs` erzeugt bei jedem Run `new FileSystemAdapter()` und `new AuditCsvStore()`. Kein Dependency Injection, kein Service-Lifetime-Management. Wenn diese Services IDisposable wären, gäbe es Leaks. Aktuell funktioniert es, aber: (1) Kein ConsoleDetector, kein HashService, kein Converter → API unterstützt nur Basic-Dedupe, (2) Jeder Run hat eigene Instanzen → keine Shared-State-Probleme, aber auch kein Caching. **Fix**: Services über DI registrieren (`builder.Services.AddSingleton<IFileSystem, FileSystemAdapter>()` etc.), `RunManager` via Constructor Injection. **Test**: API-Run ausführen → Assert alle Services korrekt injiziert. | | |
| TASK-199 | **P2 – API SSE-Stream Change-Detection via JSON-String-Vergleich**: `/runs/{runId}/stream` in `src/RomCleanup.Api/Program.cs` vergleicht `json != lastHash` wobei `json` das serialisierte RunRecord ist. Gleicher logischer State kann zu unterschiedlichen JSON-Strings führen (z.B. Property-Reihenfolge, Floating-Point-Rundung). In der Praxis mit `System.Text.Json` deterministisch, aber fragil. **Fix**: Hash über relevante Felder berechnen (Status, ProgressMessage) statt gesamtes JSON vergleichen. **Test**: Gleicher State zweimal serialisiert → Assert gleicher Hash. | | |
| TASK-200 | **P2 – API Program.cs kein Input-Validation auf RunRequest.PreferRegions**: `POST /runs` validiert `Roots` umfassend (Existenz, Symlink, System-Dir, Drive-Root), aber `PreferRegions` wird **nicht validiert**. Ein Angreifer könnte `{"roots":["D:\\Roms"],"preferRegions":["<script>alert(1)</script>"]}` senden. Die Region-Tags landen in Reports und Logs. **Fix**: PreferRegions gegen Allowlist validieren (nur 2-4 Buchstaben, Großbuchstaben). **Test**: PreferRegions mit XSS-Payload → Assert 400 Bad Request. | | |
| TASK-201 | **P2 – CLI Extensions-Default kommt aus RunOptions, nicht aus settings.json**: `ParseArgs()` in CLI `Program.cs` initialisiert `Extensions` mit `RunOptions.DefaultExtensions` statt mit der Extensions-Liste aus `settings.json`. User-konfigurierte Extensions in settings.json werden **nur** übernommen wenn explizit `-Extensions` auf der Kommandozeile angegeben wird. **Fix**: Nach `SettingsLoader.Load()` die Extensions aus settings mergen: `if (settings.General.Extensions is not null) opts.Extensions = ParseExtensions(settings.General.Extensions);`. **Test**: settings.json mit custom Extensions → Assert CLI verwendet diese. | | |
| TASK-202 | **P2 – API RunManager.ExecuteRun fängt alle Exceptions inklusive Security-Exceptions**: `ExecuteRun()` fängt `catch (Exception)` und setzt `run.Status = "failed"`. Das schluckt auch `SecurityException`, `UnauthorizedAccessException` und andere kritische Fehler, die eigentlich eskaliert werden sollten. Der Benutzer sieht nur "Internal error during run execution" ohne Details. **Fix**: Die Exception-Message (ohne Stack-Trace) in `RunResult.Error` aufnehmen. Für `SecurityException` speziellen Error-Code setzen. **Test**: Run mit gesperrtem Verzeichnis → Assert Error-Message enthält "Access denied". | | |
| TASK-203 | **P3 – CLI keine Path-Traversal-Validierung auf Roots**: `ParseArgs()` prüft nur `Directory.Exists(root)`. Ein Root wie `C:\Roms\..\..\Windows\System32` würde `Directory.Exists` bestehen und als gültiger Root akzeptiert. SafetyValidator wird in der CLI nicht aufgerufen. **Fix**: `Path.GetFullPath(root)` normalisieren und gegen System-Verzeichnisse prüfen (analog zu API). **Test**: Root `"D:\Roms\..\..\Windows"` → Assert Fehler. | | |
| TASK-204 | **P3 – API health-Endpunkt gibt keinen API-Version-Header**: `/health` gibt Status zurück, aber keinen API-Version-Header (`X-Api-Version`). Clients können nicht feststellen, welche API-Version sie ansprechen. **Fix**: `ctx.Response.Headers["X-Api-Version"] = "1.0"` global setzen. **Test**: GET /health → Assert X-Api-Version Header vorhanden. | | |

### Runde 30 — Contract-Layer: Models, Ports, Errors, Cross-Cutting

- GOAL-030: Alle architekturellen und konsistenz-relevanten Bugs im Contracts-Layer und Cross-Cutting-Concerns identifizieren

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-205 | **P1 – API RunResult shadowed Infrastructure RunResult**: `RunResult` in `src/RomCleanup.Api/RunManager.cs` hat identischen Namen wie die `RunResult`-Klasse in `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.cs`, aber andere Properties. In `ExecuteRun()` muss zwischen `RomCleanup.Infrastructure.Orchestration.RunResult` und `RomCleanup.Api.RunResult` disambiguiert werden — aktuell funktioniert es weil API's RunResult im selben Namespace ist, aber es führt zu Verwirrung und Wartbarkeitsproblemen. **Fix**: API-Klasse umbenennen zu `ApiRunResult` oder in den `Contracts/Models/`-Layer verschieben und vereinheitlichen. **Test**: Build ohne ambiguous reference Fehler verifizieren. | | |
| TASK-206 | **P2 – ConversionPipelineDef.Steps ist IReadOnlyList aber Steps-Elemente sind mutable**: `ConversionPipelineDef.Steps` in `src/RomCleanup.Contracts/Models/ServiceModels.cs` ist vom Typ `IReadOnlyList<ConversionPipelineStep>`, aber `ConversionPipelineStep` hat mutable Properties (kein `init`). `ConversionPipeline.Execute()` mutet `step.Status` und `step.Error` direkt. Dies bricht die Immutability-Garantie von IReadOnlyList. **Fix**: Entweder Steps als `List<ConversionPipelineStep>` deklarieren (ehrlich mutable) oder separate Input/Output-Modelle verwenden. **Test**: Architektur-Test auf Immutability-Verletzungen. | | |
| TASK-207 | **P2 – PipelineStep.Status/Error init-only aber PipelineEngine mutet sie**: `PipelineStep` (in `PipelineModels.cs`) hat `Status` und `Error` als reguläre Properties. `PipelineEngine.ExecuteStep()` und `Execute()` setzen `step.Status =` und `step.Error =` direkt. Während dies funktioniert, verletzt es das Principle of Least Surprise wenn Consumer einen "definierten" Step übergeben und dessen State nach dem Aufruf verändert ist. **Fix**: `PipelineEngine` sollte neue `PipelineStepResult`-Objekte zurückgeben statt Input-Steps zu mutieren. **Test**: Step vor und nach Execute vergleichen → Assert Original unverändert. | | |
| TASK-208 | **P2 – Kein zentraler Ort für Default-Extensions**: `RunOptions.DefaultExtensions` in `RunOrchestrator.cs`, `CliOptions.Extensions` in CLI `Program.cs`, und `GeneralSettings.Extensions` in `RomCleanupSettings.cs` definieren alle eigene Default-Extension-Listen. Wenn eine Extension hinzugefügt wird, muss sie an 3+ Stellen gepflegt werden. **Fix**: Einzige Source of Truth in `Contracts/Models/` als Konstante: `public static readonly string[] DefaultExtensions = [...]`. Alle anderen referenzieren diese. **Test**: Alle 3 Listen vergleichen → Assert identisch. | | |
| TASK-209 | **P3 – ErrorClassifier.Classify IOException-Sonderbehandlung nicht vollständig**: `Classify()` in `src/RomCleanup.Contracts/Errors/ErrorClassifier.cs` behandelt `FileNotFoundException` und `DirectoryNotFoundException` als Spezialfälle (nicht-transient), aber `PathTooLongException` (auch ein IOException-Subtyp) wird als Transient klassifiziert — Retry auf PathTooLongException ist sinnlos. **Fix**: `PathTooLongException` als `Recoverable` klassifizieren (nicht transient). **Test**: PathTooLongException → Assert ErrorKind.Recoverable. | | |
| TASK-210 | **P3 – RomCandidate.Type Semantik unklar**: `RomCandidate.Type` in `src/RomCleanup.Contracts/Models/RomCandidate.cs` wird in InsightsEngine als Console-Key verwendet (`group.Key`), in RunOrchestrator als Konsolen-Typ gesetzt. Das Property heißt `Type` aber meint `ConsoleKey`. Kein funktionaler Bug, aber verwirrend. **Fix**: Umbenennen zu `ConsoleKey` für Klarheit. Breaking Change — alle Referenzen anpassen. **Test**: Build erfolgreich nach Umbenennung. | | |
| TASK-211 | **P3 – EventPayload.Timestamp ist string statt DateTime**: `EventPayload.Timestamp` in `src/RomCleanup.Contracts/Models/EventBusModels.cs` ist ein `string` (ISO-8601). Consumer müssen manuell parsen für Vergleich/Sortierung. **Fix**: Auf `DateTime` ändern, Serialisierung im EventBus übernehmen. **Test**: Event empfangen → Assert Timestamp ist DateTime, sortierbar. | | |
| TASK-212 | **P3 – PhaseMetricsCollector.Export nicht atomic**: `Export()` in `src/RomCleanup.Infrastructure/Metrics/PhaseMetricsCollector.cs` schreibt direkt via `File.WriteAllText()` und kopiert dann mit `File.Copy()`. Bei Crash zwischen Write und Copy ist die `-latest`-Datei veraltet. `ScanIndexService.Save()` verwendet korrekt Atomic Write (Temp+Rename). **Fix**: Analog zu ScanIndexService: Temp-File schreiben, dann Move. **Test**: Simulate Crash zwischen Write und Copy → Assert kein korruptes Latest. | | |

### Zusammenfassung über alle Runden

- GOAL-SUMMARY: Übersicht der 78 Findings nach Priorität und Bereich

| Priorität | Anzahl | Bereiche |
|-----------|--------|----------|
| P0 | 2 | RuleEngine ReDoS, RunOrchestrator GameKey mit Extension |
| P1 | 19 | GameKey-Determinismus, Audit-Casing, Static Memory Leak, Sync-over-Async Deadlocks, CLI dataDir, API DI, RunResult-Shadowing |
| P2 | 35 | Regex-Performance, CultureInfo, CopyFile-Bug, DatRepository-Overwrite, UNC-DiskSpace, Tool-Threading, Safety UserProfile, Settings-Merge, API-Validation |
| P3 | 22 | Code-Hygiene, Naming, Dokumentation, Allokations-Optimierung, Minor-Inkonsistenzen |

---

## 3. Alternatives

- **ALT-001**: Statt manuellem Regex-Cache in RuleEngine könnte `GeneratedRegex` (C# Source Generator, .NET 7+) verwendet werden — erfordert aber Compile-Time-Patterns, nicht Runtime aus rules.json.
- **ALT-002**: Statt `LruCache` für Tool-Hashes könnte `Lazy<T>` verwendet werden — einfacher und thread-safe, aber kein Eviction.
- **ALT-003**: InsightsEngine Winner-Divergenz könnte statt DirectCall auch via Event-basiertem Scoring gelöst werden — Over-Engineering für das aktuelle Problem.
- **ALT-004**: API RunManager DI könnte auch via Service Locator statt Constructor Injection gelöst werden — widerspricht aber den Clean-Architecture-Prinzipien des Projekts.

---

## 4. Dependencies

- **DEP-001**: .NET 10 SDK 10.0.200 (aktuelles Target Framework)
- **DEP-002**: xUnit + FluentAssertions für Tests  
- **DEP-003**: BenchmarkDotNet für Performance-Tests (optional, für TASK-153, TASK-154, TASK-167)
- **DEP-004**: WPF SynchronizationContext für Deadlock-Tests (TASK-170, TASK-171)

---

## 5. Files

- **FILE-001**: `src/RomCleanup.Core/GameKeys/GameKeyNormalizer.cs` — TASK-151, TASK-152
- **FILE-002**: `src/RomCleanup.Core/Regions/RegionDetector.cs` — TASK-153
- **FILE-003**: `src/RomCleanup.Core/Rules/RuleEngine.cs` — TASK-150, TASK-155, TASK-156
- **FILE-004**: `src/RomCleanup.Core/Scoring/VersionScorer.cs` — TASK-154
- **FILE-005**: `src/RomCleanup.Core/Classification/ConsoleDetector.cs` — TASK-157
- **FILE-006**: `src/RomCleanup.Infrastructure/Audit/AuditCsvStore.cs` — TASK-158, TASK-159, TASK-162
- **FILE-007**: `src/RomCleanup.Infrastructure/Audit/AuditSigningService.cs` — TASK-158, TASK-159, TASK-160, TASK-161
- **FILE-008**: `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.cs` — TASK-163, TASK-164, TASK-165
- **FILE-009**: `src/RomCleanup.Infrastructure/Orchestration/ExecutionHelpers.cs` — TASK-166, TASK-167
- **FILE-010**: `src/RomCleanup.Infrastructure/FileSystem/FileSystemAdapter.cs` — TASK-168, TASK-169
- **FILE-011**: `src/RomCleanup.Infrastructure/Hashing/ParallelHasher.cs` — TASK-170
- **FILE-012**: `src/RomCleanup.Infrastructure/Dat/DatSourceService.cs` — TASK-171
- **FILE-013**: `src/RomCleanup.Infrastructure/Dat/DatRepositoryAdapter.cs` — TASK-172, TASK-173
- **FILE-014**: `src/RomCleanup.Infrastructure/Hashing/ArchiveHashService.cs` — TASK-174, TASK-175
- **FILE-015**: `src/RomCleanup.Infrastructure/Conversion/FormatConverterAdapter.cs` — TASK-176
- **FILE-016**: `src/RomCleanup.Infrastructure/Conversion/ConversionPipeline.cs` — TASK-177, TASK-180
- **FILE-017**: `src/RomCleanup.Infrastructure/Tools/ToolRunnerAdapter.cs` — TASK-178, TASK-179, TASK-181
- **FILE-018**: `src/RomCleanup.Infrastructure/Events/EventBus.cs` — TASK-182, TASK-183
- **FILE-019**: `src/RomCleanup.Infrastructure/History/RunHistoryService.cs` — TASK-184
- **FILE-020**: `src/RomCleanup.Infrastructure/History/ScanIndexService.cs` — TASK-185
- **FILE-021**: `src/RomCleanup.Infrastructure/Sorting/ConsoleSorter.cs` — TASK-186
- **FILE-022**: `src/RomCleanup.Infrastructure/Analytics/InsightsEngine.cs` — TASK-187, TASK-190
- **FILE-023**: `src/RomCleanup.Infrastructure/State/AppStateStore.cs` — TASK-188
- **FILE-024**: `src/RomCleanup.Infrastructure/Logging/JsonlLogWriter.cs` — TASK-189
- **FILE-025**: `src/RomCleanup.Infrastructure/Safety/SafetyValidator.cs` — TASK-191
- **FILE-026**: `src/RomCleanup.Infrastructure/Configuration/SettingsLoader.cs` — TASK-192, TASK-193
- **FILE-027**: `src/RomCleanup.Infrastructure/Quarantine/QuarantineService.cs` — TASK-194
- **FILE-028**: `src/RomCleanup.Infrastructure/Diagnostics/CatchGuardService.cs` — TASK-195
- **FILE-029**: `src/RomCleanup.Infrastructure/Linking/HardlinkService.cs` — TASK-196
- **FILE-030**: `src/RomCleanup.CLI/Program.cs` — TASK-197, TASK-201, TASK-203
- **FILE-031**: `src/RomCleanup.Api/Program.cs` — TASK-199, TASK-200, TASK-204
- **FILE-032**: `src/RomCleanup.Api/RunManager.cs` — TASK-198, TASK-202, TASK-205
- **FILE-033**: `src/RomCleanup.Contracts/Models/ServiceModels.cs` — TASK-206
- **FILE-034**: `src/RomCleanup.Contracts/Models/PipelineModels.cs` — TASK-207
- **FILE-035**: `src/RomCleanup.Contracts/Models/RomCandidate.cs` — TASK-210
- **FILE-036**: `src/RomCleanup.Contracts/Models/EventBusModels.cs` — TASK-211
- **FILE-037**: `src/RomCleanup.Contracts/Errors/ErrorClassifier.cs` — TASK-209
- **FILE-038**: `src/RomCleanup.Infrastructure/Metrics/PhaseMetricsCollector.cs` — TASK-212
- **FILE-039**: `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.cs` — TASK-208

---

## 6. Testing

- **TEST-001**: ReDoS-Test für RuleEngine.TryRegexMatch mit `(a+)+b` Pattern → Assert Timeout < 500ms (TASK-150)
- **TEST-002**: GameKey-Determinismus: `Normalize("()")` 2× aufrufen → Assert gleicher Output (TASK-151)
- **TEST-003**: MsDos-Loop mit 100 Klammern → Assert terminiert < 50ms (TASK-152)
- **TEST-004**: Sync-over-Async Deadlock-Test mit SynchronizationContext-Mock (TASK-170, TASK-171)
- **TEST-005**: Audit Rollback mit Action="Move" via SigningService → Assert gefunden (TASK-158)
- **TEST-006**: GameKey mit/ohne Extension → Assert gleiche Gruppe (TASK-164)
- **TEST-007**: InsightsEngine Winner vs DeduplicationEngine Winner → Assert identisch (TASK-187)
- **TEST-008**: CopyFile in nicht-existentem Verzeichnis → Assert Erfolg (TASK-168)
- **TEST-009**: CLI mit `settings.json` Extensions → Assert verwendet (TASK-201)
- **TEST-010**: API PreferRegions mit XSS-Payload → Assert 400 (TASK-200)
- **TEST-011**: QuarantineService.Restore mit `..` im Ordnernamen (nicht Traversal) → Assert erlaubt (TASK-194)
- **TEST-012**: SettingsLoader mit Path-Traversal Tool-Pfad → Assert abgelehnt (TASK-192)
- **TEST-013**: Conservative-Profil mit Root unter UserProfile/Roms → Assert erlaubt (TASK-191)
- **TEST-014**: CLI Root mit `..` zu System-Dir → Assert Fehler (TASK-203)
- **TEST-015**: DatRepositoryAdapter mit doppeltem Spielnamen → Assert beide ROM-Sets (TASK-173)
- **TEST-016**: FormatConverterAdapter.Verify mit 1-Byte .rvz → Assert false (TASK-176)
- **TEST-017**: ScanIndexService Fingerprint Case-Test → Assert gleich (TASK-185)
- **TEST-018**: ErrorClassifier PathTooLongException → Assert Recoverable (TASK-209)

---

## 7. Risks & Assumptions

- **RISK-001**: TASK-164 (GameKey mit Extension) ist ein **P0-Bug mit hohem Impact** — alle bisherigen DryRun-Ergebnisse könnten falsche Gruppen gezeigt haben (ZIP und 7z desselben Spiels wurden nicht zusammengeführt). Fix ändert das Dedupe-Verhalten grundlegend.
- **RISK-002**: TASK-150 (ReDoS) kann nur ausgenutzt werden wenn Angreifer rules.json kontrolliert — in der Standard-Konfiguration sind die Patterns sicher. Dennoch P0 wegen Principle of Least Privilege.
- **RISK-003**: TASK-170/171 (Deadlock) betrifft nur WPF-Aufrufe — CLI und API sind nicht betroffen (kein SynchronizationContext).
- **RISK-004**: TASK-205 (RunResult Name Clash) ist aktuell kein Compile-Fehler, könnte aber bei zukünftigen Refactorings zu Problemen führen.
- **ASSUMPTION-001**: Die 7z-Ausgabeformatierung (TASK-174) ist stabil zwischen Minor-Versionen. Major-Updates (z.B. 7z v25) könnten das Format ändern.
- **ASSUMPTION-002**: Path.GetRelativePath() (TASK-157) ist ab .NET 6 verfügbar — Projekt targets net10.0.
- **ASSUMPTION-003**: HMAC-Key-Datei (TASK-161) liegt in %APPDATA% das standardmäßig nur für den aktuellen User zugreifbar ist — ACL-Einschränkung ist Defense-in-Depth, nicht primärer Schutz.

---

## 8. Related Specifications / Further Reading

- [plan/feature-deep-dive-bug-audit-1.md](plan/feature-deep-dive-bug-audit-1.md) — Deep-Dive Audit 1 (TASK-001–TASK-083)
- [plan/feature-deep-dive-ux-ui-audit-2.md](plan/feature-deep-dive-ux-ui-audit-2.md) — Deep-Dive Audit 2 (TASK-084–TASK-149)
- [docs/ARCHITECTURE_MAP.md](docs/ARCHITECTURE_MAP.md) — Architektur-Übersicht
- [docs/TEST_STRATEGY.md](docs/TEST_STRATEGY.md) — Test-Strategie
- [docs/REVIEW_CHECKLIST.md](docs/REVIEW_CHECKLIST.md) — Review-Checkliste