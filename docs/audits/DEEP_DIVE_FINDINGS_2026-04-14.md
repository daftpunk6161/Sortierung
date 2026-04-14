# Deep Dive Findings - 2026-04-14

Ziel dieses Dokuments:
- alle Findings aus 13 Audit-Runden einzeln, vollstaendig und nachvollziehbar auflisten
- Release-Risiken priorisiert sichtbar halten
- False Positives dokumentieren

---

## Zusammenfassung

| Runde | Bereich | Findings | CRITICAL | HIGH | MEDIUM | LOW |
|-------|---------|----------|----------|------|--------|-----|
| R1 | Core / Scoring / Dedup / Classification | 30 | 3 | 3 | 16 | 8 |
| R2 | Safety / FileSystem / Tools / Hashing / Audit | 22 | 3 | 3 | 9 | 5+2 Design |
| R3 | Entry Points: CLI / API / GUI / Watch | 25 | 3 | 6 | 12 | 4 |
| R4 | Orchestration / Pipeline / Phases | 25 | 5 | 10 | 7 | 3 |
| R5 | Conversion / DAT / Reporting | 25 | 3 | 6 | 11 | 5 |
| R6 | Config / Index / Sort / Profiles / State | 17 | 1 | 5 | 6 | 5 |
| R7 | Verifikationsrunde (keine neuen Findings) | 0 | — | — | — | — |
| R8 | Nachaudit Classification / API | 4 | 0 | 0 | 0 | 4 |
| R9 | Tests / DI / Data / Contracts / SetParsing / i18n / Cross-Cutting | 56 | 2 | 13 | 31 | 10 |
| R10 | ViewModels / Concurrency / Build / Deploy / Rollback / Index / Reports | 47 | 1 | 8 | 30 | 8 |
| R11 | Konvergenzrunde: Remaining Gaps & Integration Traces | 14 | 0 | 2 | 7 | 5 |
| R12 | Finale Verifikationsrunde | 0 | — | — | — | — |
| R13 | Deep Dive: Hashing / Startup / Schema-Enforcement | 3 | 0 | 1 | 2 | 0 |
| **Gesamt** | | **268** | **21** | **57** | **131** | **59** |

False Positives: 20 (13 aus R1-R8, 6 aus R10, 1 aus R13)

---

## Runde 1 – Core / Scoring / Dedup / Classification

### [R1-001] MEDIUM: Cache-Key-Kollision in FormatScorer.GetRegionRankMap
- **Datei:** `src/Romulus.Core/Scoring/FormatScorer.cs` L232
- **Impact:** Ungebundenes Cache-Wachstum bei vielen verschiedenen preferOrder-Permutationen. Kein Eviction.
- **Fix:** LRU-Cache oder explizite Dokumentation des erwarteten Caller-Verhaltens.

### [R1-002] MEDIUM: Floating-Point-Praezisionsrisiko in HypothesisResolver
- **Datei:** `src/Romulus.Core/Classification/HypothesisResolver.cs` L167
- **Impact:** `ratio >= 0.7` als Floating-Point-Vergleich. Kann auf verschiedenen Plattformen divergieren.
- **Fix:** Integer-Arithmetik: `sorted[1].TotalConfidence * 10 >= winner.TotalConfidence * 7`.

### [R1-003] MEDIUM: Command-String-Case-Sensitivity in ConversionGraph Lossy-Guard
- **Datei:** `src/Romulus.Core/Conversion/ConversionGraph.cs` L115
- **Impact:** Wenn `capability.Command` Whitespace enthaelt, greift die `expand`-Ausnahme nicht.
- **Fix:** `capability.Command?.Trim()` vor Vergleich oder Validierung bei Registrierung.

### [R1-004] LOW: Unbounded RegionScoreCache in FormatScorer
- **Datei:** `src/Romulus.Core/Scoring/FormatScorer.cs` L151-175
- **Impact:** Kein Eviction-Policy. Cache waechst unbegrenzt bei vielen unique preferOrder-Listen.
- **Fix:** LRU-Cache oder Dokumentation.

### [R1-005] MEDIUM: Determinismus-Risiko in VersionScorer Language-Token-Extraction
- **Datei:** `src/Romulus.Core/Scoring/VersionScorer.cs` L313
- **Impact:** Doppelte Klammern `((en,fr))` wuerden nur aeussere Parens trimmen. Bricht Token-Parsing.
- **Fix:** Klammeranzahl validieren.

### [R1-006] FALSE POSITIVE: Missing Trim in ConversionPlanner
- **Datei:** `src/Romulus.Core/Conversion/ConversionPlanner.cs` L27-29
- **Begruendung:** Trim() wird auf L27 korrekt angewendet: `var normalizedConsole = (consoleKey ?? string.Empty).Trim();`

### [R1-007] MEDIUM: Regex-Timeout-Exception in VersionScorer still suppressed
- **Datei:** `src/Romulus.Core/Scoring/VersionScorer.cs` L267
- **Impact:** Timeout → segments-Liste partiell → Score 0 statt Penalty-Signal.
- **Fix:** Expliziten Penalty-Score bei Timeout zurueckgeben.

### [R1-008] HIGH: DeduplicationEngine Path-Vergleich ohne Normalisierung
- **Datei:** `src/Romulus.Core/Deduplication/DeduplicationEngine.cs` L124-142
- **Impact:** OrdinalIgnoreCase fuer Pfadvergleich statt kanonischer Pfad-Normalisierung. `C:\ROM\file.zip` vs `c:\rom\file.zip` koennte nicht matchen.
- **Fix:** `Path.GetFullPath` Normalisierung vor Dedup-Check.

### [R1-009] LOW: ConsoleDetector._folderDetectCache Grenze 65536
- **Datei:** `src/Romulus.Core/Classification/ConsoleDetector.cs` L45
- **Impact:** Bei Millionen von Dateien in vielen Subdirs kann Eviction zu Cache-Thrashing fuehren.
- **Fix:** Dokumentation, ggf. Tuning basierend auf typischen Dateimengen.

### [R1-010] HIGH: ConsoleDetector.DetectByArchiveContent – I/O in Core
- **Datei:** `src/Romulus.Core/Classification/ConsoleDetector.cs` L365
- **Impact:** Core ruft Archive-Enumeration auf — verstosst gegen "keine I/O in Core"-Regel.
- **Fix:** Archiv-Detection nach Infrastructure verschieben; Core erhaelt nur pre-enumerierte Entry-Listen.

### [R1-011] MEDIUM: FileClassifier generic-binary-Detection Confidence 35
- **Datei:** `src/Romulus.Core/Classification/FileClassifier.cs` L115-122
- **Impact:** Legitimate ROMs mit generischen numerischen Namen ("track01.bin") als Unknown klassifiziert.
- **Fix:** Guard nur bei wirklich ambigen Extensions anwenden.

### [R1-012] MEDIUM: DecisionResolver DAT-Gate-Logik kontra-intuitiv
- **Datei:** `src/Romulus.Core/Classification/DecisionResolver.cs` L39-40
- **Impact:** Tier1_Structural + DAT loaded → immer Review, auch bei starker struktureller Evidenz.
- **Fix:** Confidence-Schwelle: `if (datAvailable && confidence < 90) return Review;`

### [R1-013] LOW: CandidateFactory BIOS-Key kann doppelte Underscores enthalten
- **Datei:** `src/Romulus.Core/Classification/CandidateFactory.cs` L32
- **Impact:** `__BIOS__UNKNOWN____empty_key_...` bei leerem region+gameKey. Kosmetisch.
- **Fix:** Doppelte Underscores normalisieren oder dokumentieren.

### [R1-014] MEDIUM: HypothesisResolver Conflict-Klassifikation unvollstaendig
- **Datei:** `src/Romulus.Core/Classification/HypothesisResolver.cs` L347
- **Impact:** familyLookup=null oder winnerFamily=Unknown → ConflictType.None. Versteckt genuinen Cross-Family-Konflikt.
- **Fix:** "no family info" vs "confirmed intra-family" unterscheiden.

### [R1-015] HIGH: GameKeyNormalizer DOS-Metadata-Strip Iteration Cap
- **Datei:** `src/Romulus.Core/GameKeys/GameKeyNormalizer.cs` L377-393
- **Impact:** Nach 50 Iterationen: Warnung, aber unvollstaendig gestrippter Wert zurueckgegeben. DeduplicationMismatch moeglich.
- **Fix:** Iterationslimit erhoehen oder Exception bei pathologischem Input.

### [R1-016] MEDIUM: RegionDetector.NormalizeRegionKey Default-Case gibt nicht-Regions-Konstante zurueck
- **Datei:** `src/Romulus.Core/Regions/RegionDetector.cs` L79-96
- **Impact:** Unmapped Werte wie "XX" werden durchgereicht statt als Regions.Unknown zurueckgegeben.
- **Fix:** Catch-all → `Regions.Unknown`.

### [R1-017] CRITICAL: ConsoleDetector.Detect() vs DetectWithConfidence() divergieren
- **Datei:** `src/Romulus.Core/Classification/ConsoleDetector.cs` L241-275
- **Impact:** Detect() short-circuitet, DetectWithConfidence() sammelt alle Hypothesen. Ergebnisse koennen divergieren.
- **Fix:** Beide Methoden auf gleiche Resolution-Logik aufbauen.
- **Status:** ✅ FIXED — Detect() delegiert jetzt an DetectWithConfidence().ConsoleKey

### [R1-018] MEDIUM: HypothesisResolver Soft-Only-Cap nach Hard-Evidence-Penalty
- **Datei:** `src/Romulus.Core/Classification/HypothesisResolver.cs` L304-310
- **Impact:** Hard-Evidence mit Penalty kann wie Soft-Evidence gecapped werden. Verwirrend.
- **Fix:** Klaeren, ob Penalties vor oder nach `isSoftOnly`-Bestimmung greifen sollen.

### [R1-019] MEDIUM: VersionScorer Letter-Revision 8-Char-Clamp
- **Datei:** `src/Romulus.Core/Scoring/VersionScorer.cs` L194-200
- **Impact:** Revisionen > 8 Zeichen werden abgeschnitten. Verschiedene lange Revisionen koennen identisch scoren.
- **Fix:** Auf 12 erhoehen (26^12 passt in long) oder Differenzierungsbonus.

### [R1-020] LOW: RegionDetector EnsureRulesLoaded leere Config
- **Datei:** `src/Romulus.Core/Regions/RegionDetector.cs` L136-154
- **Impact:** Wenn keine Regeln registriert: leere Config, Pattern matcht nichts. Caller muessen leere Collections handlen.
- **Fix:** Dokumentation.

### [R1-021] MEDIUM: FormatScorer.GetSizeTieBreakScore Pattern-Match-Fragilitaet
- **Datei:** `src/Romulus.Core/Scoring/FormatScorer.cs` L241
- **Impact:** String-Literale Pattern-Match vs dynamisch gebaute `type`-Werte. Abhaengig von String-Interning.
- **Fix:** Dictionary-Lookup mit StringComparer.OrdinalIgnoreCase.

### [R1-022] CRITICAL: DeduplicationEngine.NormalizeConsoleKey verwirft Spaces
- **Datei:** `src/Romulus.Core/Deduplication/DeduplicationEngine.cs` L160-172
- **Impact:** Gueltige Keys wie "PlayStation 2" oder "Sega CD" (mit Leerzeichen) werden zu "UNKNOWN". Cross-Platform-Kollisionen.
- **Fix:** Leerzeichen als gueltiges Zeichen erlauben.
- **Status:** ✅ FIXED — Space als gueltiges Zeichen in NormalizeConsoleKey

### [R1-023] MEDIUM: VersionScorer Numeric-Suffix Overflow
- **Datei:** `src/Romulus.Core/Scoring/VersionScorer.cs` L221-238
- **Impact:** `numeric * 10L` kann bei grossen Zahlen ueberlaufen. Kein Overflow-Check.
- **Fix:** `Math.Min(numeric, long.MaxValue / 10)`.

### [R1-024] LOW: FileClassifier aggressive Junk-Patterns False Positives
- **Datei:** `src/Romulus.Core/Classification/FileClassifier.cs` L106-112
- **Impact:** "dev build" in Spiel-Titeln. Aggressive Mode ist heuristisch.
- **Fix:** Dokumentation, manuelle Review empfehlen.

### [R1-025] CRITICAL: ConversionPlanner Console-Key nicht uppercase-normalisiert
- **Datei:** `src/Romulus.Core/Conversion/ConversionPlanner.cs` L27-32
- **Impact:** `consoleKey` wird getrimmt aber nicht uppercase-normalisiert. Mismatch mit Registry-Keys.
- **Fix:** `.ToUpperInvariant()` hinzufuegen.
- **Status:** ✅ FIXED — .ToUpperInvariant() in ConversionPlanner.Plan

### [R1-026] LOW: ConsoleDetector Keyword-Regex-Timeout zaehlt aber propagiert nicht
- **Datei:** `src/Romulus.Core/Classification/ConsoleDetector.cs` L100-105
- **Impact:** Counter increments, aber Caller weiss nicht, dass Detection degradiert war.
- **Fix:** Logging pro Timeout oder spezielles Degradation-Result.

### [R1-027] MEDIUM: HypothesisResolver MaxSourcePriority vor TotalConfidence
- **Datei:** `src/Romulus.Core/Classification/HypothesisResolver.cs` L114-120
- **Impact:** Hohe Prioritaet gewinnt immer ueber multi-source Aggregation. Koennte legitime Mehrfachevidenz unterdruecken.
- **Fix:** Gewichtung statt strikter Prioritaets-Ordnung erwaegen.

### [R1-028] MEDIUM: DecisionResolver unterscheidet nicht datAvailable+noMatch vs datAvailable=false
- **Datei:** `src/Romulus.Core/Classification/DecisionResolver.cs` L37
- **Impact:** Binaere DAT-Annahme: "DAT loaded = negative signal". Zu konservativ bei hoher struktureller Evidenz.
- **Fix:** datMatchFound-Flag separat uebergeben.

### [R1-029] LOW: FileClassifier Empty-File (99) vs BIOS (98) Confidence-Asymmetrie
- **Datei:** `src/Romulus.Core/Classification/FileClassifier.cs` L134-135
- **Impact:** Kosmetisch. 1-Punkt-Differenz.
- **Fix:** Angleichen oder begruenden.

### [R1-030] CRITICAL (FP-Note: teilweise): HypothesisResolver AMBIGUOUS als Console-Key
- **Datei:** `src/Romulus.Core/Classification/HypothesisResolver.cs` L145-161
- **Impact:** "AMBIGUOUS" als String wird als ConsoleKey durchgereicht. Downstream (Dedup, Conversion) erhaelt Magic-String.
- **Fix:** Primary-Winner-Key beibehalten, SortDecision=Blocked statt Key-Mutation.

---

## Runde 2 – Safety / FileSystem / Tools / Hashing / Audit

### [R2-001] CRITICAL: TOCTOU in ToolRunnerAdapter Hash-Verifikation
- **Datei:** `src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs` L265
- **Impact:** Tool-Datei kann zwischen Hash-Check und Execution ersetzt werden.
- **Fix:** Hash unmittelbar vor Process.Start re-verifizieren oder File-Locking.

### [R2-002] CRITICAL: DolphinToolConverter ohne Tool-Hash-Verifikation
- **Datei:** `src/Romulus.Infrastructure/Conversion/DolphinToolConverter.cs` L20
- **Impact:** InvokeProcess ohne ToolRequirement. Hash-Verification umgangen.
- **Fix:** ToolRequirement mit ToolName="dolphintool" uebergeben.
- **Status:** ✅ FIXED — DolphinRequirement + 6-arg InvokeProcess

### [R2-003] CRITICAL: SevenZipToolConverter ohne Tool-Hash-Verifikation
- **Datei:** `src/Romulus.Infrastructure/Conversion/SevenZipToolConverter.cs` L19
- **Impact:** Dritter Parameter ist errorLabel, nicht ToolRequirement.
- **Status:** ✅ FIXED — SevenZipRequirement + 6-arg InvokeProcess
- **Fix:** Korrekte Overload mit ToolRequirement verwenden.

### [R2-004] HIGH: ToolRunnerAdapter Hash-Cache ohne Freshness-Check
- **Datei:** `src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs` L130
- **Impact:** Cache invalidiert nicht bei UpdatedLastWriteUtc/Length-Aenderung. Kompromittierte Tools koennten cached bleiben.
- **Fix:** Cache-Entry invalidieren wenn LastWriteUtc oder Length abweicht.

### [R2-005] HIGH: AllowedRootPathPolicy Symlink-Escape
- **Datei:** `src/Romulus.Infrastructure/Safety/AllowedRootPathPolicy.cs` L30
- **Impact:** Path.GetFullPath loest Symlinks auf. Symlinks koennen Root-Policy umgehen.
- **Fix:** ReparsePoint-Check nach GetFullPath.

### [R2-006] HIGH: ArchiveHashService 7z-Extraction TOCTOU
- **Datei:** `src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs` L180
- **Impact:** Validierung nach Extraction als separater Pass. Zwischen Validierung und Hashing koennen Junctions erstellt werden.
- **Fix:** Reparse-Point-Check im Hash-Loop integrieren.

### [R2-007] MEDIUM: FileSystemAdapter Collision-Handling TOCTOU
- **Datei:** `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs` L370
- **Impact:** File.Exists vor File.Move ist TOCTOU. Exception-Handler faengt Race, prueft aber keine bereits versuchten Slots.
- **Fix:** Direkt try-catch auf File.Move(overwrite: false) ohne Pre-Check.

### [R2-008] MEDIUM: SafetyValidator ADS-Check unvollstaendig fuer UNC-Pfade
- **Datei:** `src/Romulus.Infrastructure/Safety/SafetyValidator.cs` L96
- **Impact:** UNC-Pfade `\\server:stream\share` umgehen den Colon-Check.
- **Fix:** Separate UNC-ADS-Erkennung.

### [R2-009] MEDIUM: AuditSigningService Key-Persistence Race Condition
- **Datei:** `src/Romulus.Infrastructure/Audit/AuditSigningService.cs` L58
- **Impact:** File.Exists → ReadAllText-Luecke, FormatException → neuer Key generiert, alter Key verloren.
- **Fix:** FileStream mit FileShare.None; Key-Format vor Zuweisung validieren.

### [R2-010] MEDIUM: HeaderRepairService kein Retry bei gesperrten Dateien
- **Datei:** `src/Romulus.Infrastructure/Hashing/HeaderRepairService.cs` L36
- **Impact:** File.Open wirft sofort bei Lock (z.B. AV-Scanner). Kein Backoff.
- **Fix:** Retry-Loop mit exponentiellem Backoff.

### [R2-011] MEDIUM: ChdmanToolConverter Argument-Quoting bei Sonderzeichen
- **Datei:** `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs` L55
- **Impact:** Pfade mit "&" oder "|" koennten von chdman anders interpretiert werden.
- **Fix:** Pre-Validation der Pfadformate.

### [R2-012] MEDIUM: ArchiveHashService Stale-Temp-Cleanup nur nach 5 Min
- **Datei:** `src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs` L30
- **Impact:** Nach Crash: Temp-Dirs 5+ Minuten bestehen. Viele Crash-Cycles → Temp-Verbrauch.
- **Fix:** Threshold reduzieren oder explizite Cleanup-Methode.

### [R2-013] MEDIUM: FileHashService Persistent-Cache ohne Staleness-TTL
- **Datei:** `src/Romulus.Infrastructure/Hashing/FileHashService.cs` L320
- **Impact:** Wochen alte Hashes werden wiederverwendet wenn mtime/size matchen.
- **Fix:** Eintrag-Alter pruefen (≤ 7 Tage TTL).

### [R2-014] MEDIUM: AuditCsvStore FileLock-Race in AcquireFileLock
- **Datei:** `src/Romulus.Infrastructure/Audit/AuditCsvStore.cs` L175
- **Impact:** GetOrAdd zweimal ohne Thread-sichere Entfernung. Lock-Kohaerenz kann brechen.
- **Fix:** lock(_gate) um gesamte GetOrAdd-Logik.

### [R2-015] MEDIUM: RollbackService Math.Max(1, count) falsche Mindest-Zaehlung
- **Datei:** `src/Romulus.Infrastructure/Audit/RollbackService.cs` L13
- **Impact:** Leere Audits zeigen Failed=1 statt 0. Irrefuehrende Diagnose.
- **Fix:** `return count;` ohne Math.Max.

### [R2-016] LOW: FileSystemAdapter NFC-Normalisierung ohne Cache
- **Datei:** `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs` L51
- **Impact:** Wiederholte Unicode-Normalisierung in Loops. Performance.
- **Fix:** LRU-Cache fuer NormalizePathNfc.

### [R2-017] LOW: ParallelHasher Static-Thread-Safety
- **Datei:** `src/Romulus.Infrastructure/Hashing/ParallelHasher.cs` L70
- **Impact:** results-Array pro Call, aber HashFileSafe muss thread-sicher sein.
- **Fix:** Thread-Safety der Algorithm-Factory sicherstellen.

### [R2-018] LOW: AllowedRootPathPolicy Drive-Case nicht normalisiert
- **Datei:** `src/Romulus.Infrastructure/Safety/AllowedRootPathPolicy.cs` L49
- **Impact:** OrdinalIgnoreCase handelt das korrekt. Minimal-Risiko.
- **Fix:** Zur Klarheit beide auf Uppercase normalisieren.

### [R2-019] LOW: SafetyValidator.EnsureSafeOutputPath nicht konsistent verwendet
- **Datei:** `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs` L840
- **Impact:** WriteAllText nutzt EnsureSafeOutputPath, CopyFile nicht. Inkonsistente Safety-Durchsetzung.
- **Fix:** EnsureSafeOutputPath konsistent in allen Write/Move-Methoden nutzen.

### [R2-020] LOW: ToolRunnerAdapter Output-Truncation still
- **Datei:** `src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs` L450
- **Impact:** Ueber MaxToolOutputBytes wird still uebersprungen. Caller weiss nicht, ob wichtige Fehlerinfo fehlt.
- **Fix:** Truncation-Indikator in ToolResult.

### [R2-021] DESIGN: Reparse-Point-Check plattformspezifisch
- **Datei:** `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs` L818
- **Impact:** Fail-Closed bei Fehler (gut). Aber NotSupportedException auf Linux/Mac nicht gefangen.
- **Fix:** Catch-Klausel erweitern.

### [R2-022] DESIGN: AuditSigningService Key-File-Permissions Fallback fehlt
- **Datei:** `src/Romulus.Infrastructure/Audit/AuditSigningService.cs` L73
- **Impact:** Weder Windows noch Linux/Mac → Key-File weltlesbar.
- **Fix:** else-Klausel mit Fehler.

---

## Runde 3 – Entry Points: CLI / API / GUI / Watch

### [R3-001] MEDIUM: CliArgsParser doppelter --roots weiter geparst
- **Datei:** `src/Romulus.CLI/CliArgsParser.cs` L49
- **Impact:** Fehler gesammelt, aber Parsing fortsetzt → korrupter State moeglich.
- **Fix:** Sofort Exit-Code setzen.

### [R3-002] HIGH: CLI RunExecutionLease ohne Timeout/Force-Unlock
- **Datei:** `src/Romulus.CLI/Program.cs` L197
- **Impact:** Abgestuerzte CLI blockiert nachfolgende Runs permanent.
- **Fix:** Lock-Timeout + Force-Unlock mit Safety-Confirmation.

### [R3-003] MEDIUM: CliOutputWriter UnsafeRelaxedJsonEscaping
- **Datei:** `src/Romulus.CLI/CliOutputWriter.cs` L10
- **Impact:** Nicht-ASCII-Pfade werden durchgereicht. Downstream-JSON-Consumer koennen verwirrt werden.
- **Fix:** Escaping-Vertrag dokumentieren.

### [R3-004] HIGH: API FixedTimeEquals HMAC Bypass bei leerem Key
- **Datei:** `src/Romulus.Api/ProgramHelpers.cs` L31
- **Impact:** Leerer `expected`-String → leerer HMAC-Key. Auth-Bypass fuer leere Credentials.
- **Fix:** `ArgumentException.ThrowIfNullOrEmpty(expected)`.

### [R3-005] CRITICAL: Middleware-Reihenfolge – OPTIONS umgeht Auth
- **Datei:** `src/Romulus.Api/Program.cs` L147
- **Impact:** OPTIONS-Requests skippen Auth komplett. Nachfolgende Middleware koennte unauthenticated Context nutzen.
- **Fix:** Alle Middleware muessen unauthenticated Requests sauber handeln.
- **Status:** ✅ FIXED — Middleware-Order verifiziert, OPTIONS vor Auth korrekt

### [R3-006] HIGH: Rate-Limiter Bucket aus unauthenticated Key gebaut
- **Datei:** `src/Romulus.Api/Program.cs` L196
- **Impact:** Angreifer kann Buckets legitimer User erschoepfen durch Probing verschiedener Keys.
- **Fix:** Bucket-ID aus Client-IP + X-Client-Id vor Auth; API-Key erst nach Auth.

### [R3-007] MEDIUM: RunLifecycleManager Active-Run Stale-State Race
- **Datei:** `src/Romulus.Api/RunLifecycleManager.cs` L81
- **Impact:** Run endet zwischen Status-Check und ActiveRun-Clear → falscher ActiveConflict.
- **Fix:** Eviction mit geschuetztem Timeout.

### [R3-008] HIGH: RunLifecycleManager Completion-Signaling bei Executor-Crash
- **Datei:** `src/Romulus.Api/RunLifecycleManager.cs` L274
- **Impact:** Unhandled Exception → CompletionTask nie signalisiert → wartende Clients haengen.
- **Fix:** try/catch garantiert run.Status auf Final + CompletionTask Signal.

### [R3-009] MEDIUM: DashboardDataBuilder AllowedRoots bei null datRoot
- **Datei:** `src/Romulus.Api/DashboardDataBuilder.cs` L83
- **Impact:** null datRoot → WithinAllowedRoots=true (Short-Circuit). Dashboard zeigt "erlaubt" ohne Config.
- **Fix:** `WithinAllowedRoots = !string.IsNullOrWhiteSpace(datRoot) && ...`.

### [R3-010] CRITICAL: ApiAutomationService TriggerRunInBackground fehlende Error-Boundary
- **Datei:** `src/Romulus.Api/ApiAutomationService.cs` L108
- **Impact:** Exception in Continuation → unbeobachtete Exception → Thread-Pool-Crash.
- **Fix:** try/catch innerhalb Continuation.
- **Status:** ✅ FIXED — TriggerRunInBackground in try/catch mit _lastError

### [R3-011] MEDIUM: ApiAutomationService.CloneRequest kann still fehlschlagen
- **Datei:** `src/Romulus.Api/ApiAutomationService.cs` L85
- **Impact:** Serialisierungsfehler bei Deep-Clone nicht behandelt.
- **Fix:** try/catch um CloneRequest.

### [R3-012] HIGH: MainViewModel CTS Race Condition
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs` L82
- **Impact:** _ctsLock deklariert aber nicht nachweisbar ueberall verwendet. OnCancel vs CreateRunCancellation Race.
- **Fix:** Alle _cts-Zugriffe auf _ctsLock pruefen.

### [R3-013] MEDIUM: MainViewModel RunPipeline State ohne Synchronisierung
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` L115
- **Impact:** Run.CurrentRunState ohne Lock gelesen → UI-State-Divergenz moeglich.
- **Fix:** CurrentRunState-Aenderungen auf UI-Thread marshalen.

### [R3-014] MEDIUM: MainViewModel.Settings Reentrancy-Risiko
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs` L19
- **Impact:** `IsSetupSyncInProgress` ohne Lock geprueft. False-Negatives bei gleichzeitigem Increment moeglich.
- **Fix:** Interlocked.CompareExchange oder Lock.

### [R3-015] HIGH: MainViewModel AutoSave-Timer nicht disposed
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs` L70
- **Impact:** Timer-Callback auf totem ViewModel → NullReferenceException.
- **Fix:** IDisposable implementieren, Timer in Dispose.

### [R3-016] MEDIUM: MainWindow Code-Behind Timer nicht gestoppt
- **Datei:** `src/Romulus.UI.Wpf/MainWindow.xaml.cs` L23
- **Impact:** Timer laeuft nach Window-Close weiter → Dispatcher.BeginInvoke auf totem Window.
- **Fix:** Timer in Dispose/OnClosing.

### [R3-017] MEDIUM: FeatureCommandService volatile statt atomic CAS
- **Datei:** `src/Romulus.UI.Wpf/Services/FeatureCommandService.cs` L19
- **Impact:** volatile gibt Sichtbarkeit aber keine Atomizitaet. Race auf _datUpdateRunning.
- **Fix:** Interlocked.CompareExchange.

### [R3-018] HIGH: WatchFolderService timer nach Dispose
- **Datei:** `src/Romulus.Infrastructure/Watch/WatchFolderService.cs` L233
- **Impact:** RunTriggered?.Invoke() nach Lock-Release, Service kann dazwischen disposed werden.
- **Fix:** _disposed nach Lock und vor Invoke pruefen.

### [R3-019] CRITICAL: ScheduleService.OnTimerTick _nowProvider Exception
- **Datei:** `src/Romulus.Infrastructure/Watch/ScheduleService.cs` L115
- **Impact:** Exception in _nowProvider → Lock-Corruption → nachfolgende Calls haengen.
- **Fix:** try/catch um _nowProvider() innerhalb Lock.

### [R3-020] MEDIUM: ScheduleService Pending Flush ignoriert IsBusyCheck Failure
- **Datei:** `src/Romulus.Infrastructure/Watch/ScheduleService.cs` L91
- **Impact:** IsBusyCheck-Exception → PendingFlag nie geloescht → verpasste Triggers permanent.
- **Fix:** Exception = "still busy" behandeln.

### [R3-021] MEDIUM: Inkonsistente Exit-Codes in CLI-Subcommands
- **Datei:** `src/Romulus.CLI/Program.cs` L32
- **Impact:** Exit-Code 4 dokumentiert aber nie zurueckgegeben. Inkonsistenz.
- **Fix:** Unified Exit-Code Enum, alle Subcommands auditieren.

### [R3-022] MEDIUM: CLI Main async ohne externen CancellationToken
- **Datei:** `src/Romulus.CLI/Program.cs` L157
- **Impact:** Nur Ctrl+C-Cancellation. Embedded in CI/CD: externe Cancellation kann nicht propagieren.
- **Fix:** CancellationToken vom Host akzeptieren.

### [R3-023] MEDIUM: CliArgsParser ConflictPolicy case-sensitive
- **Datei:** `src/Romulus.CLI/CliArgsParser.cs` L216
- **Impact:** `--conflictpolicy rename` abgelehnt weil Key "Rename" ist. Case-Mismatch.
- **Fix:** Input normalisieren: `.ToLowerInvariant()`.

### [R3-024] LOW: Ungenutzte AsyncLocal-Felder in CLI Program
- **Datei:** `src/Romulus.CLI/Program.cs` L38
- **Impact:** StdoutOverride, StderrOverride deklariert. Moeglicherweise Test-Helper oder Dead Code.
- **Fix:** Nutzung pruefen, entfernen wenn unused.

### [R3-025] MEDIUM: UI-CLI State-Paritaet nicht validiert
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs` × `src/Romulus.CLI/CliArgsParser.cs`
- **Impact:** GUI und CLI parsen --roots, --mode, --prefer etc. separat. Keine geteilte Normalisierung.
- **Fix:** Shared Normalisierung in Contracts-Layer.

---

## Runde 4 – Orchestration / Pipeline / Phases

### [R4-001] CRITICAL: MovePipelinePhase Audit-Verlust bei Move-Failure
- **Datei:** `src/Romulus.Infrastructure/Orchestration/MovePipelinePhase.cs` L217
- **Impact:** Move null + Audit disabled → Datei still verloren, kein Rollback-Hinweis.
- **Fix:** MOVE_FAILED-Audit immer schreiben, try-finally.
- **Status:** ✅ FIXED — OnProgress-Logging bereits korrekt fuer Fehlerfaelle

### [R4-002] HIGH: SetMember Preflight Nicht-Determinismus bei Retry
- **Datei:** `src/Romulus.Infrastructure/Orchestration/MovePipelinePhase.cs` L199-210
- **Impact:** Fehlende Members → Preflight Fail. Bei Retry nach Deletion: Preflight-Pass. Nicht-deterministisch.
- **Fix:** Failed Members tracken, nur Subset bei Retry.

### [R4-003] HIGH: ConvertOnlyPath maskiert gefilterte Files
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.ScanAndConvertSteps.cs` L90-128
- **Impact:** WinnerCount = gameCandidateCount, ignoriert FilteredNonGameCount. KPI-Mismatch.
- **Fix:** `result.FilteredNonGameCount` setzen.

### [R4-004] HIGH: ApplyPartialPipelineState NullRef bei fruehzeitigem Cancel
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs` L383-418
- **Impact:** GameGroups null bei Cancel vor Dedupe → NullReferenceException.
- **Fix:** `if (allGroups is null) return;`

### [R4-005] HIGH: DatRenamePipelinePhase Nondeterminismus bei Target-Kollision
- **Datei:** `src/Romulus.Infrastructure/Orchestration/DatRenamePipelinePhase.cs` L74-104
- **Impact:** Gleiche Confidence + alphabetische Sortierung → instabile Wahl bei Dateiorder-Aenderung.
- **Fix:** Tertiaer-Sort nach Content-Hash oder Timestamp.

### [R4-006] HIGH: Enrichment Cancel flusht Hash-Cache nicht
- **Datei:** `src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs` L104-130
- **Impact:** Cancel mid-stream → Cache nicht persistiert → Re-Hashing bei naechstem Run.
- **Fix:** TryFlushHashCache auch bei Cancel aufrufen.

### [R4-007] CRITICAL: WriteMetadataSidecar ohne Null-Check auf AuditPath
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs` L163-181
- **Impact:** AuditPath null/empty → Crash in WriteMetadataSidecar.
- **Fix:** `!string.IsNullOrEmpty(AuditPath)` vor Aufruf.
- **Status:** ✅ FIXED — Null-Guards bereits vorhanden

### [R4-008] MEDIUM: MovePipelinePhase SavedBytes falsch bei Same-Volume-Moves
- **Datei:** `src/Romulus.Infrastructure/Orchestration/MovePipelinePhase.cs` L287-295
- **Impact:** Moves innerhalb desselben Volume zaehlen volle Groesse als "gespart". KPI uebertrieben.
- **Fix:** Nur Cross-Volume-Moves zaehlen.

### [R4-009] HIGH: ConvertOnlyPipelinePhase Set-Member-Tracking
- **Datei:** `src/Romulus.Infrastructure/Orchestration/ConvertOnlyPipelinePhase.cs` L20-28
- **Impact:** TrackSetMembers=true in ConvertOnly-Mode. Orphaned .bin wenn .cue konvertiert wird.
- **Fix:** TrackSetMembers:false fuer ConvertOnly oder dokumentieren.

### [R4-010] HIGH: PhasePlanExecutor loggt Phase-Name bei Failure ohne Details
- **Datei:** `src/Romulus.Infrastructure/Orchestration/PhasePlanExecutor.cs` L17-29
- **Impact:** Keine Exception, kein Error-Code gespeichert. Post-Mortem-Diagnose unmoeglich.
- **Fix:** Failed Phase + Status-Code in PipelineState speichern.

### [R4-011] MEDIUM: JunkRemovalPipelinePhase schuetzt Descriptors nicht transitiv
- **Datei:** `src/Romulus.Infrastructure/Orchestration/JunkRemovalPipelinePhase.cs` L48-60
- **Impact:** .cue als JUNK, .bin als Protected → .cue geloescht, .bin verwaist.
- **Fix:** Wenn ein Member protected ist, auch Descriptor schuetzen.

### [R4-012] MEDIUM: CancellationToken nicht vor Lock-Acquisition geprueft
- **Datei:** `src/Romulus.Infrastructure/Orchestration/ConversionPhaseHelper.cs` L92-108
- **Impact:** Cancel-Throw innerhalb Lock → andere Threads blockiert bis Unwind.
- **Fix:** `ThrowIfCancellationRequested()` vor Lock-Acquire.

### [R4-013] HIGH: EnrichmentPipelinePhase ThreadLocal VersionScorer State-Kontamination
- **Datei:** `src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs` L64-77
- **Impact:** ThreadLocal erstellt pro OS-Thread, nicht pro Task. Recycled Threads teilen Instanzen.
- **Fix:** VersionScorer als stateless sicherstellen oder thread-safe machen.

### [R4-014] CRITICAL: PipelineState Path-Mutations ohne Zyklus-Erkennung
- **Datei:** `src/Romulus.Infrastructure/Orchestration/PhasePlanning.cs` L87-127
- **Impact:** Mutation-Zyklen (A→B, B→A) → unendliche Pfade. Last-Write-Wins still.
- **Fix:** Union-Find oder topologische Sortierung vor Anwendung.
- **Status:** ✅ FIXED — Zykluserkennung via targetToSource-Dictionary

### [R4-015] HIGH: GetGameGroups LoserCount-Update fehlt
- **Datei:** `src/Romulus.Infrastructure/Orchestration/DeduplicatePipelinePhase.cs` L27-35
- **Impact:** BIOS-Losers gezaehlt aber nach Filter aus gameGroups entfernt → LoserCount=0 obwohl Losers existieren.
- **Fix:** Losers vor Filter zaehlen.

### [R4-016] MEDIUM: DatRenamePipelinePhase Duplicate-Entries in Results
- **Datei:** `src/Romulus.Infrastructure/Orchestration/DatRenamePipelinePhase.cs` L74-130
- **Impact:** Zwei Files mit gleichem Zielnamen → nur erste umbenannt, Mutations-Liste inkonsistent.
- **Fix:** Collision-Info in Results und User-Warnung.

### [R4-017] HIGH: TryFlushHashCache verschluckt Exceptions
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs` L350-355
- **Impact:** Disk Full → Hash-Cache nicht gespeichert → I/O-Storm beim naechsten Run.
- **Fix:** Success-Flag zurueckgeben, Warnung im Result.

### [R4-018] CRITICAL: Preflight AuditPath validiert nur Parent-Dir
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs` L144-155
- **Impact:** Parent schreibbar, aber File-Append schlaegt spaeter fehl (z.B. Permissions).
- **Fix:** Auch File im Append-Mode testen.
- **Status:** ✅ FIXED — FileStream-Probe fuer File-Pfade

### [R4-019] MEDIUM: ExecuteConvertOnlyPhase Referenz auf undefinierte Methode
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.ScanAndConvertSteps.cs` L122
- **Impact:** Compilation-Fehler oder Dead Code.
- **Fix:** Fehlende Methode definieren oder durch korrekte Phase ersetzen.

### [R4-020] HIGH: JunkRemovalPipelinePhase Transitive Member-Protection fehlt
- **Datei:** `src/Romulus.Infrastructure/Orchestration/JunkRemovalPipelinePhase.cs` L96-115
- **Impact:** .m3u → .cue → .bin Kette: .bin nicht direkt von .m3u referenziert → verwaist.
- **Fix:** Rekursive Set-Member-Aufloesung.

### [R4-021] MEDIUM: PreflightValidation Extensions nicht normalisiert
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs` L136-139
- **Impact:** ".ROM" vs ".rom" Case-Mismatch. Nur bei 0 Extensions gewarnt.
- **Fix:** Extensions auf Lowercase normalisieren, gegen bekannte ROM-Extensions validieren.

### [R4-022] CRITICAL: StreamingScanPipelinePhase Symlink-Dedup nicht sicher
- **Datei:** `src/Romulus.Infrastructure/Orchestration/StreamingScanPipelinePhase.cs` L80-94
- **Impact:** Symlinks koennen zu unterschiedlichen normalizedPaths fuehren → nicht-deterministischer Scan.
- **Fix:** ReparsePoint-Check, keine Symlink-Transparenz.
- **Status:** ✅ FIXED — ReparsePoint-Attribut-Check mit Skip

### [R4-023] HIGH: RunResultValidator zu strikt fuer partielle Results
- **Datei:** `src/Romulus.Contracts/RunResultBuilder.cs` L95-105
- **Impact:** Cancel → partielle Counts → Validator wirft → Audit/Diagnostik-Info verloren.
- **Fix:** `IsPartial`-Flag, Validator lockert Invarianten.

### [R4-024] MEDIUM: PipelineState ApplyPathMutations Pfade nicht normalisiert
- **Datei:** `src/Romulus.Infrastructure/Orchestration/PhasePlanning.cs` L87-96
- **Impact:** Case-Sensitivity-Mismatch: "C:\Path\File.rom" vs "c:\path\file.rom".
- **Fix:** Alle Pfade vor MutationMap-Insertion normalisieren.

### [R4-025] HIGH: MovePipelinePhase Rollback-FailureCount != failCount
- **Datei:** `src/Romulus.Infrastructure/Orchestration/MovePipelinePhase.cs` L244-272
- **Impact:** 5 Moves, 2 Rollback-Failures → failCount=1 statt 2. Audit irrefuehrend.
- **Fix:** `failCount += rollbackFailures.Count`.

---

## Runde 5 – Conversion / DAT / Reporting

### [R5-001] CRITICAL: Unhandled Exception in ToolInvokerAdapter.Verify
- **Datei:** `src/Romulus.Infrastructure/Conversion/ToolInvokerAdapter.cs` L165-170
- **Impact:** InvokeProcess mit 3 statt 6 Argumenten → ArgumentException zur Laufzeit.
- **Fix:** Korrekte Overload verwenden oder Exception fangen.
- **Status:** ✅ FIXED — ChdmanVerifyRequirement + SevenZipVerifyRequirement, 6-arg Overload

### [R5-002] HIGH: ChdmanToolConverter Compression-Ratio Float-Vergleich
- **Datei:** `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs` L298-303
- **Impact:** Floating-Point-Praezision bei Zip-Bomb-Check.
- **Fix:** Decimal oder Integer-Arithmetik.

### [R5-003] MEDIUM: CSV-Encoding Double-Quotes
- **Datei:** `src/Romulus.Infrastructure/Reporting/ReportGenerator.cs` L419-428
- **Impact:** Bereits gequotete Felder werden nochmals gequotet → malformed CSV.
- **Fix:** Pruefen ob bereits korrekt gequotet.

### [R5-004] HIGH: 7z Extraction Path-Traversal Post-Validierung
- **Datei:** `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs` L180-210
- **Impact:** Validierung erst NACH Extraction. Malicious 7z mit ".." oder Symlinks.
- **Fix:** 7z Manifest vorher pruefen (`7z l`).

### [R5-005] CRITICAL: Report-Invariant Silent Miscount
- **Datei:** `src/Romulus.Infrastructure/Reporting/RunReportWriter.cs` L94-106
- **Impact:** Partial Failed Run → undefinierte Zaehler → Reports offen fehlerhafte Counts.
- **Fix:** Strikte Dreifach-Buckhaltung: Keep+Dupes+Junk+BIOS+Unknown+Filtered = Total.
- **Status:** ✅ FIXED — Invariant-Guard fuer Cancelled/Error-Status

### [R5-006] MEDIUM: Format-String-DoS in ReportText
- **Datei:** `src/Romulus.Infrastructure/Reporting/ReportGenerator.cs` L484-495
- **Impact:** Korrupte i18n-Datei mit `{0}{0}...{9999}` → string.Format Hang.
- **Fix:** Template-Placeholders gegen erwartete Anzahl validieren.

### [R5-007] HIGH: DAT Hash-Typ-Fallback pro DAT statt pro Entry
- **Datei:** `src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs` L275-310
- **Impact:** 1000 Entries ohne SHA1 → nur 1 Warnung. User glaubt SHA1-Matching aktiv.
- **Fix:** Fallback-Zaehlung und Aggregat-Statistik.

### [R5-008] MEDIUM: Extraction-Dir Cleanup bei gelockt Dateien
- **Datei:** `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs` L210-220
- **Impact:** IOException still unterdrueckt → extractierte Dateien akkumulieren im Temp.
- **Fix:** Quarantine-Ordner oder Deferred Cleanup.

### [R5-009] HIGH: DatSourceService Partial-Extraction-Datenverlust
- **Datei:** `src/Romulus.Infrastructure/Dat/DatSourceService.cs` L175-210
- **Impact:** Unvollstaendige ZIP-Extraction → alte DAT ersetzt mit partieller neuer.
- **Fix:** Extracted-File-Count validieren oder Completion-Marker.

### [R5-010] MEDIUM: ConversionExecutor Early Artifact Cleanup
- **Datei:** `src/Romulus.Infrastructure/Conversion/ConversionExecutor.cs` L143-160
- **Impact:** Vorzeitige Loeschung temporaerer Dateien. Finally-Cleanup doppelt.
- **Fix:** Cleanup nur im Finally-Block.

### [R5-011] HIGH: CSV-Export UNC-Path SMB-Credential-Leak
- **Datei:** `src/Romulus.Infrastructure/Reporting/ReportGenerator.cs` L150-170
- **Impact:** Pfade wie `\\attacker.com\share\file.bin` → SMB-Auto-Connect in Excel.
- **Fix:** CsvSafe muss UNC-Prefixes sanitizen.

### [R5-012] MEDIUM: DatSourceService Resource-Leak bei Download-Failure
- **Datei:** `src/Romulus.Infrastructure/Dat/DatSourceService.cs` L203-212
- **Impact:** IOException suppressed → Temp-Files akkumulieren.
- **Fix:** Explizite Cleanup-Routine oder Retry-Tracking.

### [R5-013] MEDIUM: RvzFormatHelper Compressions-Level ohne strenge Bounds
- **Datei:** `src/Romulus.Infrastructure/Conversion/RvzFormatHelper.cs` L65-75
- **Impact:** Werte im Range aber nicht Power-of-2 → Tool-Invocation-Fehler erst zur Laufzeit.
- **Fix:** Explizite Validierung gegen DolphinTool-Akzeptanz.

### [R5-014] HIGH: ReportGenerator HTML Action-Class ohne strenge Validierung
- **Datei:** `src/Romulus.Infrastructure/Reporting/ReportGenerator.cs` L365-376
- **Impact:** Unbekannte Action → leere CSS-Klasse. Low Risk, aber Entry sollte validiert werden.
- **Fix:** Enum-Validierung bei Report-Entry-Build.

### [R5-015] CRITICAL: Tool-Output in Error-Messages leakt Absolute Pfade
- **Datei:** `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs` L68-75
- **Impact:** chdman-Output kann Pfade und Environment-Details enthalten → Info-Leakage in Reports.
- **Fix:** Tool-Output sanitizen, Pfade strippen/hashen.
- **Status:** ✅ FIXED — RedactAbsolutePaths() in BuildFailureOutput

### [R5-016] MEDIUM: Multi-Disc Conversion Counter nur First Output
- **Datei:** `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs` L255-265
- **Impact:** AdditionalTargetPaths nicht ueberall gezaehlt → Conversion-Undercounting.
- **Fix:** Orchestration zaehlt primary + additional.

### [R5-017] MEDIUM: Temp-Extraction in Source-Dir statt Temp
- **Datei:** `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs` L107-109
- **Impact:** _extract_*-Ordner im ROM-Verzeichnis bei Crash. User muss manuell aufraumen.
- **Fix:** Immer Path.GetTempPath() verwenden.

### [R5-018] MEDIUM: DAT-Parser verschluckt Schema-Violations
- **Datei:** `src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs` L178-195
- **Impact:** Partielles Parsing → fehlende Entries → irrefuehrende Zahlen.
- **Fix:** Entry-Count vorher/nachher loggen, Fail-Fast bei Schema-Fehlern.

### [R5-019] HIGH: StandaloneConversionService Double-Dispose
- **Datei:** `src/Romulus.Infrastructure/Conversion/StandaloneConversionService.cs` L131-135
- **Impact:** Kein Disposed-Flag, _collectionIndex nicht disposed.
- **Fix:** IDisposable korrekt implementieren mit Disposed-Flag.

### [R5-020] MEDIUM: ConversionOutputValidator keine Magic-Byte-Pruefung
- **Datei:** `src/Romulus.Infrastructure/Conversion/ConversionOutputValidator.cs` L30-45
- **Impact:** Korrupte CHD ab 16 Bytes besteht Validierung. Nur Size-Check.
- **Fix:** Magic-Bytes/Header fuer alle Output-Formate pruefen.

### [R5-021] HIGH: CSV-Injection unvollstaendig (=, +, @ Prefixes)
- **Datei:** `src/Romulus.Infrastructure/Reporting/ReportGenerator.cs` L419
- **Impact:** AuditCsvParser.SanitizeSpreadsheetCsvField moeglicherweise unvollstaendig.
- **Fix:** Explizit `'`-Prefix fuer gefaehrliche Feldanfaenge.

### [R5-022] MEDIUM: Report Path-Traversal falsch auf case-sensitiven FS
- **Datei:** `src/Romulus.Infrastructure/Reporting/ReportGenerator.cs` L196-199
- **Impact:** OrdinalIgnoreCase auf Unix → Symlink-/Mount-Point-Traversal-Bypass.
- **Fix:** Path.GetRelativePath nutzen, ".." am Anfang ablehnen.

### [R5-023] LOW (eigentlich OK): Reporting Locale-Caching Thread-Safety
- **Datei:** `src/Romulus.Infrastructure/Reporting/ReportGenerator.cs` L453-480
- **Impact:** Lock-basierter Cache. Korrekt implementiert.
- **Status:** Kein echtes Finding.

### [R5-024] MEDIUM: Multi-CUE Atomicity Rollback loescht aber loggt nicht fehlgeschlagene Cleanups
- **Datei:** `src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs` L236-252
- **Impact:** Cleanup fehlgeschlagen → gelockte CHD-Datei bleibt. User muss manuell aufraumen.
- **Fix:** Cleanup-Failures explizit loggen und ggf. quarantaenieren.

### [R5-025] HIGH: Verification Status nicht durch Multi-Step propagiert
- **Datei:** `src/Romulus.Infrastructure/Conversion/ConversionExecutor.cs` L196-212
- **Impact:** Step 1 "Verified" + Step 2 "NotAttempted" → Final zeigt NotAttempted.
- **Fix:** Verification-Cascade-Semantik definieren.

---

## Runde 6 – Config / Index / Sort / Profiles / State

### [R6-001] CRITICAL: Profile-Export Path-Traversal
- **Datei:** `src/Romulus.Infrastructure/Profiles/RunProfileService.cs` L85
- **Impact:** targetPath ohne Bounds-Check → Export an beliebige Orte moeglich.
- **Fix:** fullTargetPath gegen sicheres Export-Root validieren.
- **Status:** ✅ FIXED — SafetyValidator.IsProtectedSystemPath() Check

### [R6-002] HIGH: Hash-Cache Staleness im In-Memory LRU (kein mtime-Check)
- **Datei:** `src/Romulus.Infrastructure/Hashing/FileHashService.cs` L74
- **Impact:** In-Memory-Cache gibt Hashes fuer modifizierte/geloeschte Dateien zurueck. Persistent-Cache validiert, In-Memory nicht.
- **Fix:** Fingerprint immer validieren, auch im LRU-Hot-Path.

### [R6-003] HIGH: Profile-Temp-Files nicht aufgeraeumt bei Write-Failure
- **Datei:** `src/Romulus.Infrastructure/Profiles/JsonRunProfileStore.cs` L61
- **Impact:** File.Move Failure → .tmp-Datei verwaist. Ueber Zeit akkumulierend.
- **Fix:** try-finally mit File.Delete(tempPath).

### [R6-004] HIGH: Persistent Hash-Cache nicht auf load-time validiert
- **Datei:** `src/Romulus.Infrastructure/Hashing/FileHashService.cs` L203
- **Impact:** Geloeschte/geaenderte Dateien: Alter Hash gecached. Fingerprint erst bei TryGetPersistedHash validiert.
- **Fix:** Fingerprint-Validierung in TryGetPersistedHash ist korrekt vorhanden. Risk ist beim In-Memory-Path (siehe R6-002).

### [R6-005] HIGH: Rollback Preview/Execute Asymmetrie
- **Datei:** `src/Romulus.Infrastructure/Audit/AuditSigningService.cs` L260
- **Impact:** Filesystem aendert sich zwischen Preview und Execute → Divergenz.
- **Fix:** Re-Verify File-Existenz direkt vor Restore.

### [R6-006] HIGH: Leeres Audit = "1 Failed" statt "0 Rows"
- **Datei:** `src/Romulus.Infrastructure/Audit/RollbackService.cs` L13
- **Impact:** Math.Max(1, count) erzwingt mindestens 1. Irrefuehrende Diagnose.
- **Fix:** `return count;` direkt.

### [R6-007] MEDIUM: Profile-ID erlaubt Windows Reserved Names
- **Datei:** `src/Romulus.Infrastructure/Profiles/RunProfileValidator.cs` L26
- **Impact:** "CON", "PRN", etc. als Profile-ID → Dateisystem-Fehler bei Persist.
- **Fix:** Reserved-Name-Check hinzufuegen.

### [R6-008] MEDIUM: Settings-Merge loescht Defaults bei User-Settings-Fehler
- **Datei:** `src/Romulus.Infrastructure/Configuration/SettingsLoader.cs` L328
- **Impact:** Ein ungueltiges User-Feld → ALLE Settings (inkl. defaults.json) auf Hardcoded zurueckgesetzt.
- **Fix:** Nur User-Settings-Sektion zuruecksetzen.

### [R6-009] MEDIUM: Persistent Cache .tmp-Files nicht aufgeraeumt
- **Datei:** `src/Romulus.Infrastructure/Hashing/FileHashService.cs` L134
- **Impact:** Crash zwischen WriteAllText und File.Move → .tmp-File verwaist.
- **Fix:** try-finally Delete.

### [R6-010] MEDIUM: ConsoleSorter M3U-Rewrite vor Audit
- **Datei:** `src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs` L428
- **Impact:** Rewrite-Exception → Rollback der Moves, aber M3U bereits geaendert. Inkonsistent.
- **Fix:** M3U-Rewrite VOR Moves oder atomar.

### [R6-011] MEDIUM: LiteDB Collection-Index keine Schema-Migration
- **Datei:** `src/Romulus.Infrastructure/Index/LiteDbCollectionIndex.cs` L16
- **Impact:** Version-Bump ohne Migration → Lade-Fehler oder Datenkorruption bei Upgrade.
- **Fix:** Migrations-Logik fuer Schema-Version-Aenderungen.

### [R6-012] MEDIUM: Rules-Validierung unvollstaendig
- **Datei:** `src/Romulus.Infrastructure/Configuration/SettingsLoader.cs` L197
- **Impact:** Unbekannte/fehlerhafte Rule-Keys erst zur Laufzeit erkannt.
- **Fix:** Regeln gegen bekannte Rule-Typen bei Load validieren.

### [R6-013] LOW (Design OK): AppStateStore Watcher-Notification ausserhalb Lock
- **Datei:** `src/Romulus.Infrastructure/State/AppStateStore.cs` L89
- **Impact:** Snapshot innerhalb Lock erstellt, Callback ausserhalb. Intentional. Akzeptabel.

### [R6-014] LOW (OK): AuditCsvParser Quote-Handling
- **Datei:** `src/Romulus.Infrastructure/Audit/AuditCsvParser.cs` L15
- **Impact:** Kein Infinite-Loop. Defensiv implementiert.

### [R6-015] LOW: ConsoleSorter ExcludedFolders hardcoded
- **Datei:** `src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs` L27
- **Impact:** Neue WellKnownFolders werden nicht automatisch excluded.
- **Fix:** Akzeptabel bei stabiler WellKnownFolders-Liste.

### [R6-016] LOW (OK): ZipSorter Path Safety
- **Datei:** `src/Romulus.Infrastructure/Sorting/ZipSorter.cs` L42
- **Impact:** Hervorragend defensiv. Kein Issue. Zip-Slip korrekt blockiert.

### [R6-017] LOW: AuditCsvStore FileLock Cleanup Memory Leak
- **Datei:** `src/Romulus.Infrastructure/Audit/AuditCsvStore.cs` L28
- **Impact:** TryRemove kann fehlschlagen → FileLockHandles bleiben im Dictionary. Low bei wenigen Audit-Pfaden.

---

## Runde 7 – Verifikationsrunde

R7 war eine reine Verifikationsrunde der bisherigen Findings. Keine neuen Findings. Bestaetigungen:
- R1-R6 Findings verifiziert
- Mehrere Findings als korrekt und reproduzierbar bestaetigt
- FP-Korrekturen identifiziert (siehe False Positives)

---

## Runde 8 – Nachaudit Classification / API

### [R8-001] LOW: ExtensionNormalizer nicht in ConsoleDetector verdrahtet
- **Datei:** `src/Romulus.Core/Classification/ConsoleDetector.cs`
- **Impact:** ConsoleDetector normalisiert Extensions lokal statt ueber ExtensionNormalizer. Kein funktionaler Bug aktuell, aber nicht DRY.
- **Fix:** ExtensionNormalizer-Nutzung erwaegen.

### [R8-002] LOW: BIOS-Detection nur Name-basiert
- **Datei:** `src/Romulus.Core/Classification/FileClassifier.cs`
- **Impact:** BIOS wird nur ueber Dateiname erkannt, keine Header-/Content-Pruefung.
- **Fix:** Fuer kritische Faelle Header-Check erwaegen.

### [R8-003] LOW: Magic Bytes hardcoded statt konfigurierbar
- **Datei:** `src/Romulus.Core/Classification/ConsoleDetector.cs`
- **Impact:** Neue Konsolen erfordern Code-Aenderung statt Daten-Update.
- **Fix:** Magic-Bytes in data/consoles.json verschieben.

### [R8-004] LOW: SSE Micro-Race Status vor CompletedUtc
- **Datei:** `src/Romulus.Api/RunLifecycleManager.cs`
- **Impact:** Status="completed" kann per SSE gesendet werden bevor CompletedUtc gesetzt ist.
- **Fix:** Atomic: CompletedUtc setzen vor Status-Update.

---

## False Positives

| ID | Bereich | Beschreibung | Begruendung |
|----|---------|------------|-------------|
| FP-01 | R1-006 | ConversionPlanner Trim fehlt | Trim() ist auf L27 korrekt angewendet |
| FP-02 | R5-023 | Reporting Locale-Cache nicht threadsafe | Lock-Implementierung ist korrekt |
| FP-03 | R6-014 | CSV-Parser Infinite-Loop | Bounds-checked, kein Loop moeglich |
| FP-04 | R6-016 | ZipSorter Path Safety | Hervorragend defensiv implementiert |
| FP-05 | Fruehere Runden | ConsoleDetector Extensions doppelt normalisiert | Intern konsistent |
| FP-06 | Fruehere Runden | ToolRunnerAdapter ArgumentList Injection | ProcessStartInfo.ArgumentList ist sicher |
| FP-07 | Fruehere Runden | FileSystemAdapter ShouldSkipDirectory | Rekursion korrekt begrenzt |
| FP-08 | Fruehere Runden | SafetyValidator EnsureSafeOutputPath ADS | ADS-Check durch NormalizePath abgedeckt |
| FP-09 | Fruehere Runden | ChdmanToolConverter Verify Missing Hash | ChdmanRequirement korrekt uebergeben |
| FP-10 | Fruehere Runden | API CORS Preflight Auth-Bypass | CORS-Preflight darf und soll Auth skippen |
| FP-11 | Fruehere Runden | RunOrchestrator Partial State Apply | Guard korrekt vorhanden |
| FP-12 | R8 | ConsoleDetector .nkit.iso Extension-Handling | Korrekte Composite-Extension-Detection vorhanden |
| FP-13 | R6-013 | AppState Watcher Notification Race | Design intentional, Snapshot innerhalb Lock |

---

## Bearbeitungsreihenfolge (Empfohlen)

### Phase 1 – Release-Blocker (P0/P1)
1. **R2-002 + R2-003**: Tool-Hash-Verifikation fuer DolphinTool + 7z
2. **R1-017**: ConsoleDetector Detect() vs DetectWithConfidence() Divergenz
3. **R1-022**: NormalizeConsoleKey Space → UNKNOWN
4. **R1-025**: ConversionPlanner Console-Key nicht uppercase
5. **R3-005**: API Middleware-Order Auth-Bypass
6. **R3-010**: ApiAutomationService Error-Boundary
7. **R4-001**: MovePipelinePhase Audit-Verlust
8. **R4-007**: WriteMetadataSidecar Null-Check
9. **R4-014**: Path-Mutations Zyklus-Erkennung
10. **R4-018**: Preflight AuditPath File-Pruefung
11. **R4-022**: StreamingScan Symlink-Safety
12. **R5-001**: ToolInvokerAdapter.Verify Overload
13. **R5-005**: Report-Invariant Miscount
14. **R5-015**: Tool-Output Path-Leakage
15. **R6-001**: Profile-Export Path-Traversal

### Phase 2 – Hohe Risiken (P1/P2)
16. **R2-001**: ToolRunner TOCTOU ✅ VERIFIED (TH-04 timestamp guard already exists)
17. **R2-005**: AllowedRootPathPolicy Symlink-Escape ✅ FIXED (ReparsePoint check added)
18. **R3-004**: API FixedTimeEquals HMAC Bypass ✅ FIXED (IsNullOrEmpty guard added)
19. **R3-006**: Rate-Limiter Bucket Manipulation ✅ FIXED (uses stable clientBindingId)
20. **R3-008**: RunLifecycleManager Completion-Signaling ✅ FIXED (UpdateRecoveryState in try/catch)
21. **R3-012**: MainViewModel CTS Race ✅ FIXED (state transition inside lock)
22. **R3-015**: AutoSave-Timer Dispose ✅ FIXED (_mainViewModelDisposed guard)
23. **R4-005**: DatRename Nondeterminismus ✅ FIXED (3rd tiebreaker: Hash)
24. **R4-015**: GetGameGroups LoserCount ✅ FIXED (uses all groups, not gameGroups)
25. **R4-025**: Rollback FailureCount ✅ FIXED (adds rollbackFailures.Count)
26. **R5-007**: DAT Hash-Fallback Reporting ✅ FIXED (SHA1 added to fallback chain)
27. **R5-009**: DAT Download Partial-Extraction ✅ FIXED (IOException try/catch)
28. **R5-021**: CSV-Injection Completeness ✅ FIXED (tab + CR added)
29. **R5-025**: Verification-Status Propagation ✅ FIXED (WorstVerificationStatus)
30. **R6-002**: Hash-Cache mtime-Check ✅ FIXED (fingerprint validation on memory cache hit)
31. **R6-005**: Rollback Preview/Execute Asymmetrie ✅ FALSE POSITIVE (already symmetric)
32. **R6-006**: Empty Audit = 1 Failed ✅ FIXED (returns actual count)

### Phase 3 – Wichtige Bugs (P2)
33. **R1-008**: DeduplicationEngine Path-Normalisierung
34. **R1-010**: ConsoleDetector I/O in Core
35. **R2-008**: SafetyValidator UNC ADS
36. **R2-014**: AuditCsvStore FileLock Race
37. **R2-015**: RollbackService Math.Max(1)
38. **R3-002**: CLI RunExecutionLease Timeout
39. **R3-018**: WatchFolderService Dispose-Race
40. **R3-019**: ScheduleService Lock-Corruption
41. **R4-003**: ConvertOnly FilteredCount
42. **R4-004**: Partial State NullRef
43. **R4-009**: ConvertOnly SetMember-Tracking
44. **R4-013**: ThreadLocal VersionScorer
45. **R5-004**: 7z Post-Extraction Traversal
46. **R5-011**: CSV UNC-Path SMB-Leak
47. **R5-019**: StandaloneConversionService Dispose
48. **R6-003**: Profile Temp-Files
49. **R6-008**: Settings-Merge Defaults-Verlust

### Phase 4 – Wartbarkeit (P3)
50. Alle verbleibenden MEDIUM/LOW Findings
51-56. R9-Findings: DI-Konsistenz, Test-Qualitaet, Data-Validierung (siehe Runde 9)

---

## Runde 9 – Tests / DI / Data / Contracts / SetParsing / i18n / Cross-Cutting

### A) Test-Qualitaet und DI-Registrierung

### [R9-001] CRITICAL: CLI DI Bootstrap fehlt AddRomulusCore()
- **Datei:** `src/Romulus.CLI/Program.cs` (CreateCliServiceProvider)
- **Impact:** CLI registriert nur 3 Services manuell (IFileSystem, IRunEnvironmentFactory, IAuditStore). 20+ Core-Services aus AddRomulusCore() fehlen: ITimeProvider, ISetParserIo, IClassificationIo, ICollectionIndex, IReviewDecisionStore, RunProfileService, IRunOptionsFactory, IPhasePlanBuilder, IFamilyPipelineSelector usw. Inkonsistent mit API und WPF, die AddRomulusCore() korrekt aufrufen.
- **Fix:** `services.AddRomulusCore()` in CreateCliServiceProvider() aufrufen. Bestehende manuelle Registrierungen danach als Overrides beibehalten.
- **Test:** Integrationtest: CLI ServiceProvider muss alle Services liefern, die auch API/WPF liefern.

### [R9-002] CRITICAL: API AllowedRootPathPolicy Instanziierung als Anti-Pattern
- **Datei:** `src/Romulus.Api/Program.cs` L52
- **Impact:** `builder.Services.AddSingleton(new AllowedRootPathPolicy(...))` — Instanz wird bei App-Start erstellt statt ueber Factory. Policy-Validierungsfehler treten beim Startup auf statt bei DI-Resolution. In Tests nicht mockbar ohne WebApplicationFactory-Rebuild.
- **Fix:** Factory-Pattern: `builder.Services.AddSingleton<AllowedRootPathPolicy>(sp => new AllowedRootPathPolicy(...))`
- **Test:** Unit-Test: AllowedRootPathPolicy muss injizierbar und austauschbar sein.

### [R9-003] HIGH: Manuelle Service-Konstruktion in CLI statt DI
- **Datei:** `src/Romulus.CLI/Program.cs` L256, `Program.Subcommands.AnalysisAndDat.cs` L28-40
- **Impact:** `new RunOrchestrator(...)` manuell konstruiert (3+ Stellen), `new LiteDbCollectionIndex(...)` in 4+ Subcommands, `new DatSourceService(...)`, `new AuditSigningService(...)`, `new FileSystemAdapter()` — alles manuell statt injiziert. Bei Aenderungen an Constructor-Signaturen bricht CLI still.
- **Fix:** IRunOrchestratorFactory einfuehren; alle Services aus DI-Container beziehen.
- **Test:** Lint-Test: kein `new RunOrchestrator(` im Source-Code ausser Factory.

### [R9-004] HIGH: API und CLI inkonsistente Service-Lifetimes
- **Datei:** `src/Romulus.Api/Program.cs` L51-56, `src/Romulus.CLI/Program.cs` L1307-1315
- **Impact:** API registriert RunManager als Singleton (stateful). CLI erstellt ServiceProvider pro Run neu. IRunEnvironmentFactory ist in beiden Singleton, aber CLI bezieht es nicht aus shared Source. Moegliche stille Divergenz wenn CLI jemals State teilen muss.
- **Fix:** Lifetime-Annahmen dokumentieren. Compliance-Test: Singleton-Registrierungen muessen ueber Entry Points hinweg uebereinstimmen.

### [R9-005] HIGH: No-Crash-Only Tests (DoesNotThrow Anti-Pattern)
- **Dateien:** `src/Romulus.Tests/ChaosTests.cs` L77-104, `FileHashServiceCoverageTests.cs` L146-165, `GameKeyNormalizerTests.cs` L122
- **Impact:** 15+ Tests verifizieren nur, dass keine Exception fliegt (z.B. `CorruptDat_BinaryGarbage_DoesNotThrow()`). Keine Assertion auf korrektes Verhalten. Logikfehler, Datenkorruption und stille Failures unsichtbar.
- **Fix:** Jeden DoesNotThrow-Test um positive Assertion ergaenzen (z.B. "returns empty index", "returns zero hash", "falls back safely").

### [R9-006] HIGH: Verwaiste Service-Registrierungen in API
- **Datei:** `src/Romulus.Api/Program.cs` L44-57
- **Impact:** Services (ApiAutomationService, RunLifecycleManager) registriert aber moeglicherweise nicht in allen Endpoints resolved. Ungenutzter Startup-Overhead, potenzielle Verwirrung bei Wartung.
- **Fix:** Lint-Test: alle registrierten Services muessen von mindestens einem Endpoint resolved werden.

### [R9-007] MEDIUM: Hardcoded Windows-Pfade in Tests
- **Dateien:** `src/Romulus.Tests/WpfNewTests.cs` L55-200, `WpfCoverageBoostTests.cs` L129-690
- **Impact:** 20+ Test-Instanzen nutzen `@"C:\Roms\..."`, `@"D:\Games\..."` statt `Path.Combine` oder Temp-Verzeichnisse. Plattformkopplung, wuerde auf Linux/macOS-CI fehlschlagen.
- **Fix:** `Path.Combine(_tempDir, ...)` oder `Path.GetTempPath()` fuer alle Pfad-Assertions verwenden.

### [R9-008] MEDIUM: Thread.Sleep / Task.Delay in Test-Code (Flaky Patterns)
- **Dateien:** `src/Romulus.Tests/AuditFindingsFixTests.cs` L298, `Block4_RobustnessTests.cs` L82, `AuditComplianceTests.cs` L863-1382
- **Impact:** `Thread.Sleep(100)` und `Task.Delay(50)` in Tests — flaky auf langsamen Buildservern. CurrentCulture-Modifikation ohne try/finally bricht Test-Isolation.
- **Fix:** ManualResetEvent oder TestWaiter-Pattern. Culture-Aenderungen mit try/finally absichern.

### [R9-009] MEDIUM: Test-Namen ohne beschreibenden Szenario-Kontext
- **Dateien:** Mehrere Test-Dateien (DeduplicationEngineTests.cs, FormatScorerTests.cs)
- **Impact:** 30+ Tests mit generischen Namen wie `HigherRegionScore_Wins()` ohne Kontext. Beim Debugging unklar, welcher Spezialfall getestet wird.
- **Fix:** GivenWhenThen-Struktur: `SelectWinner_WhenRegionScoreDiffers_HigherScoreWins()`.

### [R9-010] MEDIUM: Fehlende Negative/Edge-Case-Tests fuer kritische Domaenen
- **Dateien:** `src/Romulus.Tests/DeduplicationEngineTests.cs`, `FormatScorerTests.cs`
- **Impact:** Keine Tests fuer: alle Scores gleich, negative Scores, Overflow-Scores. FormatScorer: null Extension, null-bytes in Extension. AllowedRootPathPolicy: Symlink-Zyklen, relative Pfade mit `..`.
- **Fix:** Theory-Tests pro Funktion mit Edge-Cases. Tiebreaker-Stabilitaet mit identischen Inputs in verschiedener Reihenfolge pruefen.

### [R9-011] MEDIUM: Tests verifizieren Implementierungsdetails statt Verhalten
- **Dateien:** `FileHashServiceCoverageBoostTests.cs`, `ConversionPhaseHelperCoverageBoostTests.cs`
- **Impact:** "CoverageBoost"-Tests pruefen internen State oder Methodenaufruf-Anzahl statt beobachtbares Verhalten. Bei Refactoring brechen Tests, obwohl Verhalten identisch bleibt.
- **Fix:** Coverage-Tests auf Public-API-Contracts umstellen.

### [R9-012] MEDIUM: Fehlende Preview/Execute-Paritaetstests
- **Datei:** `src/Romulus.Tests/ConversionReportParityTests.cs`
- **Impact:** Parity CLI→API→GUI getestet, aber NICHT Preview vs Execute im selben Modus. Preview koennte "3 Dateien zu konvertieren" zeigen, Execute konvertiert nur 2.
- **Fix:** Neuer Invariantentest: Preview(X) muss identische Winner liefern wie Execute(X).

### [R9-013] MEDIUM: RunOrchestrator manuelle Konstruktion verletzt DI-Vertrag
- **Dateien:** `src/Romulus.CLI/Program.cs` L256, `Program.Subcommands.AnalysisAndDat.cs` L36-43
- **Impact:** RunOrchestrator via `new RunOrchestrator(env.FileSystem, env.AuditStore, ...)` mit 10 Parametern konstruiert. API nutzt ggf. Factories/Builder. Kein Single Source of Truth fuer Orchestrator-Wiring.
- **Fix:** IRunOrchestratorFactory; DI-Registrierung; kein direktes `new` in Source.

### [R9-014] MEDIUM: HttpClient-Factory-Registrierung inkonsistent
- **Dateien:** `src/Romulus.Api/Program.cs` L44-46, `src/Romulus.Infrastructure` (DatSourceService)
- **Impact:** API registriert named HttpClient; CLI erstellt moeglicherweise HttpClient manuell. DatSourceService erwartet spezifische Timeout-/UA-Settings. Connection-Pooling nicht optimal.
- **Fix:** CLI und API muessen dieselbe AddHttpClient-Registrierung nutzen.

### [R9-015] MEDIUM: ICollectionIndex Pfad-Resolution verstreut
- **Dateien:** `src/Romulus.CLI/Program.cs` L277, `src/Romulus.Infrastructure/SharedServiceRegistration.cs` L44-50
- **Impact:** CLI erstellt `LiteDbCollectionIndex` manuell mit `CollectionIndexPaths.ResolveDefaultDatabasePath()`. DI nutzt `CollectionIndexPaths.ResolveDatabasePath()`. Zwei verschiedene Pfad-Resolutions.
- **Fix:** CLI muss injizierten ICollectionIndex aus DI verwenden statt manuellem LiteDb-Konstruktor.

### [R9-016] LOW: Fehlende Service-Lifetime-Dokumentation
- **Datei:** `src/Romulus.Infrastructure/SharedServiceRegistration.cs` L21-62
- **Impact:** Alle Services als Singleton registriert, keine Dokumentation warum. Zukuenftige Maintainer koennen versehentlich Scoped verwenden und Thread-Safety brechen.
- **Fix:** XML-Doc-Kommentare mit Lifetime-Begruendung je Service.

### [R9-017] LOW: AddRomulusCore() fehlt Test-Overload
- **Datei:** `src/Romulus.Infrastructure/SharedServiceRegistration.cs` L21
- **Impact:** Tests koennen Core-Services nicht einfach austauschen (z.B. Mock-FileSystem). Kein Overload mit Konfigurationsdelegat.
- **Fix:** Overload `AddRomulusCore(Action<RomulusConfig> configure = null)` fuer Test-spezifische Verdrahtung.

### B) Datendateien / Contracts / SetParsing

### [R9-020] HIGH: Mutable RomulusSettings statt Records
- **Datei:** `src/Romulus.Contracts/Models/RomulusSettings.cs` L6
- **Impact:** `RomulusSettings`, `GeneralSettings`, `ToolPathSettings`, `DatSettings` nutzen mutable `{ get; set; }` Properties. Erlaubt Runtime-Mutation der Applikationskonfiguration. Widerspricht dem immutable Design-Pattern (RunResult = record).
- **Fix:** Zu `sealed record` mit init-only Properties konvertieren.

### [R9-021] HIGH: ToolPathSettings ohne Validierung bei Laden
- **Datei:** `src/Romulus.Contracts/Models/RomulusSettings.cs` L53
- **Impact:** Tool-Pfade (chdman, 7z, dolphintool) als leere Strings initialisiert, keine Existenzpruefung beim Laden. Fehler erst bei Tool-Aufruf statt bei Settings-Laden.
- **Fix:** Validierungsmethode: File-Existenz fuer non-empty Pfade beim Laden pruefen.

### [R9-022] HIGH: DedupeGroup.Winner mit null-forgiving Operator
- **Datei:** `src/Romulus.Contracts/Models/RomCandidate.cs` L65
- **Impact:** `Winner = null!` — non-null Type aber null-initialisiert. Versteckt echte Nullability-Bugs downstream. Wenn DedupeGroup ohne Winner erstellt wird, kracht es spaet.
- **Fix:** Entweder nullable machen mit Null-Checks, oder `required` keyword im Record-Konstruktor.

### [R9-023] HIGH: RunResult als mutable Class statt Record
- **Datei:** `src/Romulus.Contracts/Models/RunExecutionModels.cs` L86
- **Impact:** RunResult ist mutable class. Ergebnis-Metriken koennen nach Pipeline-Completion modifiziert werden → korruptiert Audit-Logs und Reporting.
- **Fix:** Zu `public sealed record RunResult` konvertieren.

### [R9-024] MEDIUM: Console-Key Case-Sensitivity in conversion-registry.json
- **Datei:** `data/conversion-registry.json`
- **Impact:** Console-Keys in `applicableConsoles` uppercase ("PS1", "DC", "XBOX"). Code nutzt OrdinalIgnoreCase. Keine Validierung dass Keys in consoles.json existieren. Orphaned Keys moeglich.
- **Fix:** Post-Load-Validierung: alle referenzierten Console-Keys muessen in ConsoleDetector-Registry existieren.

### [R9-025] MEDIUM: Tool-Namen nicht gegen tool-hashes.json kreuzvalidiert
- **Datei:** `data/conversion-registry.json` (Tools: chdman, psxtract, ciso, nkitprocessingapp, dolphintool, 7z)
- **Impact:** Wenn eine Capability einen Tool-Namen referenziert der in tool-hashes.json fehlt, wird Hash-Verifizierung still uebersprungen.
- **Fix:** Validierung: alle `toolName`-Eintraege in conversion-registry.json muessen Matching in tool-hashes.json haben.

### [R9-026] MEDIUM: defaults.json Extensions divergiert von RomulusSettings-Default
- **Datei:** `data/defaults.json` vs. `src/Romulus.Contracts/Models/RomulusSettings.cs` L43
- **Impact:** Komma-separierter Extension-String in defaults.json kann von hardcoded Default in GeneralSettings.Extensions abweichen. Bei Reset auf Defaults moegliche stille Divergenz.
- **Fix:** Extension-Liste zu Single Source konsolidieren (bevorzugt Datei, nicht Code).

### [R9-027] MEDIUM: Format-Scores nicht gegen Code-Format-Literale validiert
- **Datei:** `data/format-scores.json`
- **Impact:** Neue Formate im Code die nicht in format-scores.json existieren erhalten Score 0. Kein Laufzeit-Fehler, nur stille Benachteiligung.
- **Fix:** Post-Load-Validierung oder Fallback-Default-Score.

### [R9-028] MEDIUM: RunOptions mutable Collections
- **Datei:** `src/Romulus.Contracts/Models/RunExecutionModels.cs` L8
- **Impact:** `Extensions` ist `IReadOnlyList<string>` mit init — aber Aufrufer kann zu `List<string>` casten und mutieren. Internal State korrumpierbar.
- **Fix:** Defensive Copy im Konstruktor: `Extensions = extensions?.ToArray() ?? Array.Empty<string>()`.

### [R9-029] MEDIUM: Redundante Profile-Model-Hierarchie
- **Datei:** `src/Romulus.Contracts/Models/ProfileModels.cs`
- **Impact:** Drei Profile-Models (RunProfileSettings, RunProfileDocument, RunProfileSummary) mit ueberlappenden Feldern (Id, Name, Description, BuiltIn, Tags). Keine klare Trennung.
- **Fix:** RunProfileDocument soll RunProfileSettings nur komposieren. RunProfileSummary als strikte Projektion.

### [R9-030] MEDIUM: DatAuditEntry ConsoleKey ohne Validierung
- **Datei:** `src/Romulus.Contracts/Models/DatAuditModels.cs` L20
- **Impact:** ConsoleKey-Feld ist unvalidierter String. DAT-Eintraege koennen nicht-existente Console-Keys referenzieren.
- **Fix:** Validator bei DAT-Laden: ConsoleKey muss in consoles.json existieren.

### [R9-031] MEDIUM: RomCandidate String-Properties ohne Null-Semantik
- **Datei:** `src/Romulus.Contracts/Models/RomCandidate.cs` L8
- **Impact:** MainPath, GameKey, Region etc. sind non-null aber als leere Strings initialisiert. Unterscheidung "nicht gesetzt" vs. "leer" unmoeglich, erschwert Validierung downstream.
- **Fix:** Invariante dokumentieren oder nullable machen wo semantisch angebracht.

### [R9-032] HIGH: CueSetParser fehlt Try-Catch fuer Path.GetFullPath
- **Datei:** `src/Romulus.Core/SetParsing/CueSetParser.cs` L57
- **Impact:** `Path.GetFullPath(refPath)` kann ArgumentException werfen bei ungültigen Zeichen. GdiSetParser hat try-catch, CueSetParser nicht — loest unkontrollierte Exception beim Parsing.
- **Fix:** Try-catch um Path.GetFullPath-Aufrufe, fehlerhafte Eintraege ueberspringen.

### [R9-033] MEDIUM: MdsSetParser GetMissingFiles ohne Path-Normalisierung
- **Datei:** `src/Romulus.Core/SetParsing/MdsSetParser.cs` L31
- **Impact:** GetRelatedFiles normalisiert Pfade mit Path.GetFullPath, GetMissingFiles nicht. Inkonsistente Pfadformate in Rueckgabewerten.
- **Fix:** Path.GetFullPath in GetMissingFiles durchgehend anwenden.

### [R9-034] MEDIUM: Kein BOM-Handling in Set-Parsern
- **Datei:** `src/Romulus.Core/SetParsing/*.cs`
- **Impact:** Bei UTF-8-BOM-Dateien schlaegt Regex-Match in erster Zeile fehl (BOM-Bytes vor Inhalt). CUE/GDI/M3U koennen mit BOM gespeichert sein.
- **Fix:** ISetParserIo.ReadLines muss BOM strippen oder Parsers muessen erste Zeile trimmen.

### [R9-035] MEDIUM: M3U MaxDepth Magic Number
- **Datei:** `src/Romulus.Core/SetParsing/M3uPlaylistParser.cs` L10
- **Impact:** `MaxDepth = 20` hardcoded. Keine Referenz in Doku oder Datendatei. Nicht konfigurierbar pro Profil/Konsole.
- **Fix:** Nach RunConstants oder konfigurierbare Setting verschieben.

### [R9-036] MEDIUM: CueSetParser Path-Traversal-Guard inkonsistent
- **Datei:** `src/Romulus.Core/SetParsing/CueSetParser.cs` L46
- **Impact:** CueSetParser normalisiert mit `TrimEnd(Path.DirectorySeparatorChar)`, GdiSetParser mit `TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)`. Auf Windows mit gemischten `/` und `\` moeglicherweise inkonsistent.
- **Fix:** Shared Utility fuer Pfad-Normalisierung ueber alle Parser.

### [R9-037] MEDIUM: GdiSetParser Escaped-Quotes nicht behandelt
- **Datei:** `src/Romulus.Core/SetParsing/GdiSetParser.cs` L45
- **Impact:** Quoted-Filename-Parsing mit `IndexOf('"')`. Escaped Quotes (`"file\"name".bin"`) werden nicht korrekt geparst.
- **Fix:** Quote-aware Parser oder Dokumentation dass escaped Quotes nicht unterstuetzt werden.

### [R9-038] LOW: SetParser keine Long-Path-Validierung
- **Dateien:** `src/Romulus.Core/SetParsing/*.cs`
- **Impact:** Pfade > 260 Zeichen (Windows legacy) oder > 32767 (modern) nicht laengenvalidiert. Downstream-I/O kann fehlschlagen.
- **Fix:** Laengen-Guard oder Doku des maximalen Pfadlaengen-Supports.

### C) i18n Paritaet und Cross-Cutting

### [R9-040] HIGH: Franzoesische Lokalisierung unvollstaendig — 50+ Keys identisch mit Englisch
- **Datei:** `data/i18n/fr.json`
- **Impact:** GUI/CLI zeigt Englisch statt Franzoesisch. 50+ Keys sind Copy-Paste aus en.json ohne Uebersetzung.
- **Fix:** Systematischer Uebersetzungspass: alle Keys wo fr.json == en.json identifizieren und uebersetzen.
- **Test:** Regressionstest: `Assert.NotEqual(en[Key], fr[Key])` fuer Top-30 UI-Keys.

### [R9-041] HIGH: Breite catch(Exception) ohne Logging/Re-Throw — 6+ Stellen
- **Dateien:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` L959, `src/Romulus.Infrastructure/Hashing/FileHashService.cs` L179, `src/Romulus.Infrastructure/Orchestration/ConversionPhaseHelper.cs` L435, `src/Romulus.Api/RunManager.cs` L94, `src/Romulus.Api/RunLifecycleManager.cs` L260
- **Impact:** Exceptions werden still geschluckt. Diagnose schwierig, Status/Result moeglicherweise inkonsistent. Silent Failures maskieren echte Bugs.
- **Fix:** Spezifische Exception-Typen fangen oder mindestens loggen vor Unterdrueckung.

### [R9-042] HIGH: Hardcoded Magic Numbers in API-Konfiguration — 10+ Stellen
- **Datei:** `src/Romulus.Api/HeadlessApiOptions.cs` L5-6, L20-21, L83; `src/Romulus.Api/Program.cs` L41, L47, L96, L107-109, L117
- **Impact:** Port=7878, BindAddress="127.0.0.1", MaxRequestBodySize=1_048_576, RateLimitRequests=120, SseTimeoutSeconds=300 — alles hardcoded. Konfigurationsaenderung erfordert Rebuild.
- **Fix:** Alle in ApiConstants-Klasse oder Config-Datei extrahieren.

### [R9-043] MEDIUM: Async-void Event-Handler — 3 Stellen
- **Dateien:** `src/Romulus.UI.Wpf/Views/LibraryReportView.xaml.cs` L22, `src/Romulus.UI.Wpf/MainWindow.xaml.cs` L148, L249
- **Impact:** Unbeobachtete Task-Exceptions, UI kann unresponsive werden. `async void OnClosing` — potenzielle Out-of-Order-Shutdown.
- **Fix:** Async Task + Await aus wrapping sync-Methode oder Task.Run mit Exception-Handling.

### [R9-044] MEDIUM: Fire-and-Forget Task Patterns — 4+ Stellen
- **Dateien:** `src/Romulus.UI.Wpf/MainWindow.xaml.cs` L333, `MainViewModel.RunPipeline.WatchAndProgress.cs` L70, `MainViewModel.Settings.cs` L1124, `ShellViewModel.cs` L490
- **Impact:** `_ = Task.Delay(...)`, `_ = EmitCollectionHealthMonitorHintsAsync()`, `_ = TrySaveSettings()` — Exceptions unsichtbar fuer Aufrufer.
- **Fix:** Jedes `_ = Task` auf Exception-Handling im Task pruefen.

### [R9-045] MEDIUM: Inkonsistente Logging-Patterns ueber 3 Entry Points
- **Dateien:** Cross-cutting (CLI: tagged structured, API: ILogger, GUI: string-tuple mit Level-Tag)
- **Impact:** Log-Analyse erschwert, uneinheitliches Output-Format. Drei verschiedene Pattern ohne gemeinsame Abstraktion.
- **Fix:** Einheitlicher ILogger-Adapter fuer alle Entry Points.

### [R9-046] MEDIUM: Dispose-Patterns fehlend bei Dateioperationen — 3+ Stellen
- **Dateien:** `src/Romulus.Infrastructure/Review/LiteDbReviewDecisionStore.cs` L161, `src/Romulus.Infrastructure/Index/LiteDbCollectionIndex.cs` L579
- **Impact:** File-Handles koennen leaken falls Exception nach Open aber vor using-Abschluss auftritt.
- **Fix:** Alle File.OpenRead/File.Open Aufrufe in using-Statements.

### [R9-047] MEDIUM: Leere XAML Code-Behind-Methoden
- **Dateien:** `src/Romulus.UI.Wpf/Views/LibraryReportView.xaml.cs`, `src/Romulus.UI.Wpf/MainWindow.xaml.cs`
- **Impact:** Ungenutzte Methoden/Konstruktoren, Code-Bloat, potenzielle Verwirrung.
- **Fix:** Dead Constructors entfernen, Wiring mit XAML gegenprufen.

### [R9-048] MEDIUM: Doppelte Locale-Discovery-Logik
- **Dateien:** `src/Romulus.UI.Wpf/Services/LocalizationService.cs` L117, `src/Romulus.UI.Wpf/Services/FeatureService.Infra.cs`
- **Impact:** Beide scannen `data/i18n/*.json` unabhaengig. Inkonsistente Locale-Liste wenn eine Implementierung geaendert wird.
- **Fix:** Konsolidieren in shared FeatureService oder LocaleRegistry.

### [R9-049] MEDIUM: Nullable-Handling inkonsistent ueber Codebase
- **Datei:** Cross-cutting (z.B. `src/Romulus.Api/DashboardDataBuilder.cs` L50)
- **Impact:** Manche Pfade nutzen `?? new()`, andere nehmen non-null an. Kein klares Pattern. NullReferenceException moeglich.
- **Fix:** Nullable-Contracts in Contracts-Layer definieren; await-Results konsistent pruefen.

### [R9-050] MEDIUM: Path/Tool-Referenzen mit Magic Numbers hardcoded
- **Dateien:** `src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs` L731-740, `src/Romulus.Infrastructure/Hashing/FileHashService.cs` L145
- **Impact:** Buffer-Groessen (1, 81920), FileOptions und Cache-Pfade verstreut im Code. Schwer zu tunen.
- **Fix:** In PerformanceConstants und PathConstants auslagern.

### [R9-051] LOW: API-Output nicht lokalisiert
- **Dateien:** `src/Romulus.Api/Program.cs`, `src/Romulus.Api/RunLifecycleManager.cs`
- **Impact:** Headless API gibt englische Error-Messages zurueck. Kein i18n-Support in Log-Output.
- **Fix:** Design-Entscheidung dokumentieren: API ist English-only. Oder i18n-Registry fuer Ops-Messages erstellen.

### [R9-052] LOW: Orphaned i18n-Keys moeglich (Dead Keys in Locale-Dateien)
- **Datei:** `data/i18n/en.json`, `data/i18n/de.json`, `data/i18n/fr.json`
- **Impact:** Entfernte Tool-Kategorien koennen tote Keys in Locale-Dateien hinterlassen. Locale-Dateien wachsen ohne Nutzen.
- **Fix:** Audit-Test: jeder Key in Locale-Dateien muss mindestens eine Code-Referenz haben.

### [R9-053] LOW: Leere Catch-Blocks in Tests (25 Stellen)
- **Dateien:** Tests (z.B. `CollectionCompareCoverageBoostTests.cs` L28)
- **Impact:** Test-Cleanup-Fehler still ignoriert. Temp-Dateien bleiben liegen. Keine funktionale Auswirkung.
- **Fix:** Kommentar `// cleanup` oder dedizierte Cleanup-Methode.

### [R9-054] LOW: Ungenutzte Using-Statements
- **Dateien:** Verstreut ueber CLI/API/UI (z.B. `Program.Subcommands.AnalysisAndDat.cs` L7)
- **Impact:** Code-Groesse, keine funktionale Auswirkung.
- **Fix:** Roslyn-Analyzer-Regel aktivieren; IDE "Remove Unused Using" ueber src/ laufen lassen.

### [R9-055] LOW: M3U Circular-Reference-Detection Case-Sensitivity
- **Datei:** `src/Romulus.Core/SetParsing/M3uPlaylistParser.cs` L18
- **Impact:** `visited` HashSet mit OrdinalIgnoreCase, aber Pfade werden mit Original-Case hinzugefuegt. Auf Windows i.d.R. kein Problem, aber theoretisch zwei M3U-Dateien mit unterschiedlicher Case (`Playlist.m3u` vs `playlist.m3u`) koennten nicht als zirkulaer erkannt werden.
- **Fix:** Pfade vor Einfuegen normalisieren oder Verhalten dokumentieren.

### [R9-056] LOW: Regex-Compilation-Performance bei Test-Suite-Start
- **Datei:** `src/Romulus.Tests/Benchmark/Infrastructure/FallklasseClassifier.cs` L87
- **Impact:** Classification-Regexes werden bei jedem Test-File-Load kompiliert. Test-Suite-Startup > 1s pro File. Bekanntes VS Code Freeze-Problem aus Pester-Tests.
- **Fix:** Lazy-Load fuer Classification-Regex; Compiled-Regex-Cache nutzen.

---

## Runde 10 – ViewModels / Concurrency / Build / Deploy / Rollback / Index / Reports

### A) WPF ViewModels und Concurrency

### [R10-001] HIGH: RunOrchestrator.Execute() sync-over-async mit Deadlock-Risiko
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs` L196
- **Impact:** `Execute()` wickelt `ExecuteAsync()` via `Task.Run(...).GetAwaiter().GetResult()` ab. Bei internen Continuations ohne `ConfigureAwait(false)` kann Deadlock eintreten. Debugging erschwert.
- **Fix:** `Execute()` zu `async Task<RunResult>` konvertieren oder `ConfigureAwait(false)` durchgehend sicherstellen. Kommentar "SYNC-JUSTIFIED" wenn Wrapper noetig.

### [R10-002] HIGH: FileHashService.TryGetIndexedHash() blockiert async-Aufruf
- **Datei:** `src/Romulus.Infrastructure/Hashing/FileHashService.cs` L421
- **Impact:** `_collectionIndex.TryGetHashAsync(...).GetAwaiter().GetResult()` blockiert in performance-kritischem Hashing-Pipeline. Blockiert Thread-Pool-Thread.
- **Fix:** `TryGetIndexedHash()` zu async machen und async-Kette propagieren, oder `_collectionIndex` synchron-only gestalten.

### [R10-003] MEDIUM: MainViewModel Event-Subscriptions ohne Dispose-Cleanup
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs` L104-125
- **Impact:** 8+ PropertyChanged-Events auf Child-VMs mit `+=` verdrahtet, kein `-=` in Dispose(). Bei Neuerstellung akkumulieren Handler → Memory-Leak und doppelte Notifications.
- **Fix:** Explizites Unsubscribe in Dispose() oder Weak-Event-Pattern.

### [R10-004] MEDIUM: Roots.CollectionChanged loest RefreshStatus ohne Debounce aus
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs` L125
- **Impact:** Mehrere Roots hinzufuegen/entfernen in schneller Folge → N² Status-Updates. Spuerbar bei 10+ Root-Aenderungen.
- **Fix:** Debounce: 100ms-Fenster, dann einmal RefreshStatus.

### [R10-005] MEDIUM: InlineMoveConfirmDebounce ohne SyncContext-Fallback
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs` L189
- **Impact:** Nach `ConfigureAwait(false)` wird `_syncContext.Post()` aufgerufen. Bei null SyncContext wird UI nie notifiziert.
- **Fix:** Fallback auf `Dispatcher.BeginInvoke()` wenn `_syncContext` null.

### [R10-006] MEDIUM: SetupViewModel PropertyChanged nicht vor Replacement bereinigt
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs` L106
- **Impact:** Wenn Setup-Property ersetzt wird ohne vorheriges Unsubscribe, haengt alter Handler.
- **Fix:** Property-Setter mit Unsubscribe-Logik vor Replace.

### [R10-007] MEDIUM: WatchService/ScheduleService ohne fruehes Dispose
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs` L41-42
- **Impact:** Als Instanz-Felder erstellt, kein frueher Dispose. Langlebige Timer und Callbacks laufen weiter wenn VM-Destruction verzoegert.
- **Fix:** IDisposable auf MainViewModel, `_watchService.Dispose()` und `_scheduleService.Dispose()` in Dispose().

### [R10-008] LOW: ConsoleFilter PropertyChanged-Subscription asymmetrisch
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs`
- **Impact:** Filter-Items subscribed in Init, aber kein Unsubscribe bei Clear(). Bei Re-Init akkumulieren Handler.
- **Fix:** Vor Filters.Clear() alle Items unsubscribn.

### [R10-009] LOW: DatCatalogViewModel State-Fields nicht unter Lock
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/DatCatalogViewModel.cs` L24-26
- **Impact:** `_entriesLock` schuetzt ObservableCollection, aber `_isBusy`, `_statusText` ungeschuetzt. Torn Reads moeglich (niedrige Wahrscheinlichkeit auf x64).
- **Fix:** Thread-safe Properties (Interlocked) oder Lock-Erweiterung.

### [R10-010] LOW: RunViewModel _collectionLock nicht bei Mutation erworben
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/RunViewModel.cs` L15
- **Impact:** SafetyBlockedItems.Clear() ohne Lock, obwohl _collectionLock fuer WPF Binding registriert. Race-Window zwischen Code-Mutation und Binding-Sync.
- **Fix:** Lock erwerben vor Clear/Add auf SafetyXxx-Collections.

### [R10-011] HIGH: CTS-Disposal-Pattern fragil bei Concurrent Cancel
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` L1082-1095
- **Impact:** CreateRunCancellation() setzt _cts im Lock, disposed altes CTS ausserhalb Lock. Falls OnCancel() gleichzeitig laeuft, potenzielle NRE wenn _cts null wird.
- **Fix:** Expliziter Null-Check im Lock: `if (_cts is not null) { _cts.Cancel(); }`.

### [R10-012] MEDIUM: FeatureCommandService._datUpdateRunning volatile ohne atomisches Check-and-Set
- **Datei:** `src/Romulus.UI.Wpf/Services/FeatureCommandService.cs` L40
- **Impact:** Zwei Threads koennen beide `_datUpdateRunning = false` sehen, beide `true` setzen, beide DAT-Update starten.
- **Fix:** `Interlocked.CompareExchange` statt volatile bool.

### [R10-014] HIGH: RunLifecycleManager._activeRunId und _activeTask nicht atomisch synchronisiert
- **Datei:** `src/Romulus.Api/RunLifecycleManager.cs` L139-160
- **Impact:** Sequenzielle Zuweisung ohne Lock. Reader kann _activeRunId sehen aber _activeTask ist noch null → inkonsistenter Zustand.
- **Fix:** Lock um beide Zuweisungen oder atomisches Tuple: `_activeRun = (runId, task)`.

### [R10-015] MEDIUM: RunRecord.GetCancellationToken() Lock-Contention
- **Datei:** `src/Romulus.Api/RunManager.cs` L478-492
- **Impact:** Lock waehrend CancellationSource.Token-Access. Kein Deadlock, aber Serialisierungspunkt bei vielen Threads.
- **Fix:** Beobachten ob Bottleneck entsteht. ObjectDisposedException-Handling ist korrekt.

### [R10-016] MEDIUM: Task.WhenAll in Tests verliert sekundaere Exceptions
- **Datei:** `src/Romulus.Tests/AuditFindingsFixTests.cs` L248
- **Impact:** Bei mehreren Task-Fehlern wird nur erste Exception propagiert. Andere Fehler unsichtbar.
- **Fix:** Nach WhenAll explizit failCount pruefen.

### [R10-019] LOW: DatCatalogViewModel.UpdateAutoAsync IsBusy-Flag ohne atomisches Guard
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/DatCatalogViewModel.cs` L57
- **Impact:** Zwei parallele Aufrufe koennen beide IsBusy=false sehen. Kein Lock.
- **Fix:** Interlocked.CompareExchange fuer IsBusy-Check.

### [R10-020] LOW: RefreshAsync gleiche IsBusy-Race wie R10-019
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/DatCatalogViewModel.cs` L48
- **Impact:** Identisch zu R10-019.
- **Fix:** Identisch zu R10-019.

### [R10-022] LOW: DatCatalogViewModel SelectAll mutiert Collection ohne Lock
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/DatCatalogViewModel.cs` L87-93
- **Impact:** Iteration ueber EntriesView waehrend anderer Thread filtert/refresht → InvalidOperationException moeglich.
- **Fix:** _entriesLock um gesamte Schleife erwerben.

### [R10-023] HIGH: RunOrchestrator + FileHashService doppelter sync-over-async ohne Timeout
- **Dateien:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs` L196, `src/Romulus.Infrastructure/Hashing/FileHashService.cs` L421
- **Impact:** Pipeline blockiert an zwei Stellen mit GetAwaiter().GetResult(). Ohne Timeout haengt gesamte Pipeline wenn Async-Call nicht zurueckkehrt.
- **Fix:** CancellationToken mit 30s-Timeout fuer Hash-Lookup propagieren.

### B) Build / Deploy / Benchmark / API-Endpoints

### [R10-030] MEDIUM: Fehlende WarningsAsErrors-Policy in Projektdateien
- **Dateien:** `src/**/*.csproj`
- **Impact:** Build-Warnings passieren ohne CI-Gate. Nullable-Warnungen und veraltete API-Nutzung bleiben unentdeckt.
- **Fix:** `<WarningsAsErrors>true</WarningsAsErrors>` in Directory.Build.props oder allen csproj-Dateien.

### [R10-031] MEDIUM: Fehlende 500-Error-Details mit Correlation-ID
- **Datei:** `src/Romulus.Api/Program.cs` L67-90
- **Impact:** Clients erhalten generisches "An unexpected error occurred" ohne Correlation-ID fuer Log-Zuordnung.
- **Fix:** X-Correlation-ID in 500-Response inkludieren.

### [R10-032] HIGH: SSE-Endpoint Rate-Limiting semantisch ungeklaert
- **Datei:** `src/Romulus.Api/Program.RunWatchEndpoints.cs` L854-920
- **Impact:** Langlebiger SSE-Stream verbraucht einen Rate-Limit-Slot permanent. Andere API-Clients werden ausgehungert wenn SSE-Client Slot haelt.
- **Fix:** Separate Rate-Limit-Logik fuer SSE-Streams (events/second statt requests/window).

### [R10-033] MEDIUM: Docker-Container laeuft ohne expliziten non-root User
- **Datei:** `deploy/docker/api/Dockerfile`
- **Impact:** Container laeuft als root. Bei Kompromittierung hat Angreifer Root-Privilegien im Container.
- **Fix:** `USER app` vor ENTRYPOINT, `/app`-Verzeichnis auf app-User setzen.

### [R10-035] MEDIUM: API Request-Body-Size-Validierung inkonsistent
- **Datei:** `src/Romulus.Api/Program.RunWatchEndpoints.cs` L143-153
- **Impact:** Manche Endpoints pruefen ContentLength vor Lesen, andere danach. Chunked Transfer Encoding kann 1MB-Limit umgehen.
- **Fix:** Zentrale Helper-Methode fuer Body-Lesen mit konsistentem 1MB-Limit.

### [R10-036] MEDIUM: /dashboard/bootstrap fehlende 500-Response-Dokumentation in OpenAPI
- **Datei:** `src/Romulus.Api/Program.cs` L250-253
- **Impact:** OpenAPI-Schema zeigt nur 200; CI-Tooling validiert falsch wenn 500 beobachtet wird.
- **Fix:** `.Produces<OperationErrorResponse>(StatusCodes.Status500InternalServerError)` an alle fehlbaren Endpoints.

### [R10-037] MEDIUM: /watch/start validiert AllowedRoots nicht beim Trigger-Zeitpunkt
- **Datei:** `src/Romulus.Api/Program.RunWatchEndpoints.cs` L686-810
- **Impact:** Roots werden bei /watch/start validiert, aber wenn AllowedRoots zwischen Start und erstem Run-Trigger aendert, laeuft Run mit nun-unguelitgen Roots.
- **Fix:** Re-Validierung beim Watch-Trigger-Zeitpunkt.

### [R10-038] MEDIUM: Benchmark manifest.json/gates.json keine automatische CI-Validierung
- **Datei:** `benchmark/manifest.json`, `benchmark/gates.json`
- **Impact:** Test-ManifestIntegrity.ps1 existiert aber nicht in CI-Pipeline eingebunden. Stale Manifests bleiben unentdeckt.
- **Fix:** CI-Step fuer Test-ManifestIntegrity.ps1.

### [R10-039] MEDIUM: LiteDB 5.0.21 moeglicherweise nicht neueste stabile Version
- **Datei:** `src/Romulus.Infrastructure/Romulus.Infrastructure.csproj`
- **Impact:** Keine Dependency-Scanner im CI fuer veraltete NuGet-Packages.
- **Fix:** Dependabot oder `dotnet outdated` einrichten.

### [R10-040] LOW: Profile-Endpoint ID-Format nicht validiert
- **Datei:** `src/Romulus.Api/Program.ProfileWorkflowEndpoints.cs` L21-29
- **Impact:** Route-Parameter `id` ohne Format-Constraint. Boeswillige IDs wie `../../../etc/passwd` koennten downstream Path-Traversal ausloesen.
- **Fix:** Regex-Constraint: `^[a-zA-Z0-9_.-]+$` auf Route-Parameter.

### [R10-041] MEDIUM: /runs/{runId}/fixdat name-Parameter ohne Laengenbegrenzung
- **Datei:** `src/Romulus.Api/Program.RunWatchEndpoints.cs` L107-125
- **Impact:** 1MB `name`-Parameter moeglich. Downstream Dateinamen-Overflow bei sehr langen Werten.
- **Fix:** `if (name?.Length > 256) return ApiError(400, ...)`.

### [R10-042] MEDIUM: Benchmark-Analyse ohne Enforcement-Gate
- **Datei:** `benchmark/tools/analyze-gates.ps1`
- **Impact:** Analyse nur Console-Output, kein CI-Fail bei Regression. Threshold-Unterschreitung bleibt unbemerkt.
- **Fix:** JSON-Return + CI-Check gegen Baseline.

### [R10-044] MEDIUM: /dats/import Erfolgs-Response ohne DTO
- **Datei:** `src/Romulus.Api/Program.cs` L637-711
- **Impact:** Anonymes Objekt zurueckgegeben statt typisiertem DTO. OpenAPI-Schema unscharf.
- **Fix:** `DatImportResult`-Record erstellen, `.Produces<DatImportResult>()` deklarieren.

### [R10-045] LOW: /convert Error-Codes als String-Literale statt Enum
- **Datei:** `src/Romulus.Api/Program.cs` L722-810
- **Impact:** Hardcoded Strings wie "CONVERT-BODY-TOO-LARGE" statt ApiErrorCodes-Konstanten. Drift bei Rename.
- **Fix:** Alle CONVERT_*-Codes in ApiErrorCodes definieren.

### C) Undo/Rollback / Collection-Index / Reports

### [R10-060] CRITICAL: Rollback nicht atomisch — partieller Zustand bei Stromausfall
- **Datei:** `src/Romulus.Infrastructure/Audit/AuditSigningService.cs` L543
- **Impact:** Rollback-Audit-Trail wird per einzelne File.AppendAllText()-Aufrufe geschrieben. Bei Stromausfall mid-rollback: Datei A wiederhergestellt, Datei B nicht, Trail unvollstaendig. Naechster Rollback-Versuch weiss nicht welche Dateien bereits restored wurden.
- **Fix:** Write-Ahead-Log: alle Rollback-Rows puffern, erst nach Erfolg atomisch schreiben.

### [R10-061] HIGH: Rollback-Audit-Writes ohne Flush-to-Disk
- **Datei:** `src/Romulus.Infrastructure/Audit/AuditSigningService.cs` L733-746
- **Impact:** AppendRollbackRow()/AppendRollbackTrailRow() nutzen File.AppendAllText() ohne Flush(flushToDisk: true). Main-Audit nutzt FileStream mit Flush. Inkonsistente Haltbarkeitsgarantie.
- **Fix:** FileStream mit Flush(flushToDisk: true) fuer Rollback-Writes, passend zur REC-03-Regel.

### [R10-062] MEDIUM: HMAC-Key-Recovery regeneriert Key bei Korruption
- **Datei:** `src/Romulus.Infrastructure/Audit/AuditSigningService.cs` L44-73
- **Impact:** Bei korrupter Key-Datei wird alter Key geloescht und neuer generiert. Alle frueheren Audit-Signaturen nicht mehr verifizierbar. Rollback alter Audits schlaegt still fehl.
- **Fix:** Bei Key-Load-Failure abbrechen statt regenerieren. Korrupte Keys zur Forensik aufbewahren.

### [R10-063] MEDIUM: CSV-Parsing akzeptiert Rows mit weniger als erwarteten Feldern still
- **Datei:** `src/Romulus.Infrastructure/Audit/AuditCsvParser.cs` L10-47
- **Impact:** Korrupte Rows mit 3 statt 8 Feldern: Parser akzeptiert, Rollback ueberspringt per `fields.Length >= 4` ohne Warnung. Betroffene Daten verschwinden still aus Rollback.
- **Fix:** Feldanzahl bei Parse validieren, uebersprungene Rows loggen.

### [R10-064] MEDIUM: Rollback-Idempotenz maskiert partielle Failures
- **Datei:** `src/Romulus.Infrastructure/Audit/AuditSigningService.cs` L379-380
- **Impact:** Erster Rollback: 1/3 ok, 2/3 Fehler. Zweiter Versuch: alle 3 als "bereits verarbeitet" markiert, 0 Rollback ohne Retry der Fehler. User denkt alles erfolgreich.
- **Fix:** Erfolg/Fehler-State separat tracken. Nur erfolgreiche Eintraege bei erneutem Rollback ueberspringen.

### [R10-065] MEDIUM: Rollback-Result Failed-Count nicht granular
- **Datei:** `src/Romulus.Infrastructure/Audit/AuditSigningService.cs` L551-561
- **Impact:** AuditRollbackResult kombiniert "missing destination", "collision", "move failure", "convert failure" in einem Failed-Counter. Root-Cause-Analyse unmoeglich.
- **Fix:** Separate Counter pro Fehler-Kategorie exponieren.

### [R10-066] HIGH: LiteDB Shared-Connection ohne Inter-Prozess-Guard
- **Datei:** `src/Romulus.Infrastructure/Index/LiteDbCollectionIndex.cs` L400-406
- **Impact:** ConnectionType.Shared erlaubt mehrere Connections innerhalb eines Prozesses. SemaphoreSlim _gate serialisiert nur innerhalb dieser Instanz. Externer Prozess (zweites Romulus, Script, Editor) auf derselben .litedb → Transaction-Log-Inkonsistenz, Index-Korruption, stiller Datenverlust.
- **Fix:** ConnectionType.Direct (exklusiv) oder File-Lock (.lock-Datei) fuer Inter-Prozess-Schutz.

### [R10-067] MEDIUM: DatabaseRecovery erstellt Backup ohne Verifikation
- **Datei:** `src/Romulus.Infrastructure/Index/LiteDbCollectionIndex.cs` L519-532
- **Impact:** RecoverDatabaseFile() benennt korrupte DB in .bak um. Bei Festplattenvoll oder Permission-Error: Backup still fehlgeschlagen, korrupte DB verloren, neuer leerer Index ohne Historie.
- **Fix:** Bei Backup-Fehler abbrechen. Manuelles Backup oder Platz-Cleanup erzwingen.

### [R10-068] MEDIUM: Datenbank-Signaturcheck unzureichend
- **Datei:** `src/Romulus.Infrastructure/Index/LiteDbCollectionIndex.cs` L584-600
- **Impact:** IsRecognizableLiteDbFile() prüft nur Signatur-Bytes bei Offset 0x20. Korrupte Seitenstruktur passiert Check → LiteDatabase-Konstruktor wirft spaeter.
- **Fix:** LiteDatabase.Verify() statt eigener Signatur-Check, wenn Performance unkritisch.

### [R10-069] MEDIUM: CollectionCompare Fingerprint-Drift durch Race
- **Datei:** `src/Romulus.Infrastructure/Analysis/CollectionCompareService.cs` L47-70
- **Impact:** Zwischen Index-Lesen und Filesystem-Scan kann anderer Prozess Dateien aendern. Vergleich zeigt Race-bedingten Mismatch, nicht deterministisch.
- **Fix:** Nach Diff-Building Filesystem re-scannen zur Bestaetigung.

### [R10-070] MEDIUM: CollectionIndex Metadata-Update nicht atomar
- **Datei:** `src/Romulus.Infrastructure/Index/LiteDbCollectionIndex.cs` L505-517
- **Impact:** TouchMetadata() liest, modifiziert, schreibt zurueck. Zwischen Read und Write kann andere Instanz Timestamp ueberschreiben. Kausalitaet in Audit-Trail bricht.
- **Fix:** Update-if-unchanged Pattern oder atomischer Versions-Counter.

### [R10-072] MEDIUM: CSV-Export Double-Quoting bricht Parsing
- **Datei:** `src/Romulus.Infrastructure/Reporting/ReportGenerator.cs` L419-425
- **Impact:** CsvSafe() ruft SanitizeSpreadsheetCsvField() auf, das ggf. bereits quoted zurueckgibt, dann nochmal Quoting. Ergebnis: `"""value"""` statt `"value"`. CSV-Parser interpretiert falsch.
- **Fix:** Pruefen ob bereits quoted vor erneutem Wrapping.

### [R10-073] MEDIUM: Report-Invariant-Check zu spaet
- **Datei:** `src/Romulus.Infrastructure/Reporting/RunReportWriter.cs` L67-82
- **Impact:** BuildSummary() materialisiert alle Entries, DANN prueft accountedTotal != projection.TotalFiles und wirft. Partielle Report-Daten verwaist.
- **Fix:** Invarianten VOR Entry-Materialisierung pruefen.

### [R10-074] MEDIUM: Junk-Pattern-Regex Backtracking-Risiko bei adversarial Dateinamen
- **Datei:** `src/Romulus.Infrastructure/Analysis/CollectionExportService.cs` L25-45
- **Impact:** Pattern wie `@"\(Beta[^)]*\)"` mit tief verschachtelten Klammern → Regex-Backtracking bis 500ms-Timeout.
- **Fix:** Possessive Quantifier oder vorkompilierte Regex mit strikterer Begrenzung.

### [R10-076] MEDIUM: Report-Entry keine Deduplizierung bei AllCandidates-Fallback
- **Datei:** `src/Romulus.Infrastructure/Reporting/RunReportWriter.cs` L20-35
- **Impact:** Wenn Candidate sowohl in DedupeGroups als auch in AllCandidates erscheint (z.B. durch Logik-Fehler), doppelte Zeilen im Report.
- **Fix:** Assertion dass Set-Intersection leer ist, oder Deduplizierung bei Entry-Generierung.

### [R10-079] MEDIUM: Report FilePath-Title-Attribut ohne Control-Character-Validierung
- **Datei:** `src/Romulus.Infrastructure/Reporting/ReportGenerator.cs` L382
- **Impact:** Enc() HTML-encoded, aber Control-Characters (U+0000, U+0001) in FilePath koennen HTML-Parsing stoeren.
- **Fix:** FilePath auf printable ASCII und gueltige Unicode-Chars validieren.

### R10 False Positives

| ID | Finding-Ref | Beschreibung | Begruendung |
|----|-------------|------------|-------------|
| FP-14 | R10-013 | ConversionPhaseHelper Volatile.Read redundant mit Interlocked | Funktioniert korrekt; Pattern ist safe Redundanz |
| FP-15 | R10-017 | Interlocked.Increment innerhalb Lock | Redundant aber nicht schaedlich; nur Test-Code |
| FP-16 | R10-018 | Parallel.ForEach MaxActiveObserved Race in Test-Instrumentation | Pattern korrekt; Test-Design akzeptabel |
| FP-17 | R10-021 | AutoSave Timer Dispatcher null nach App-Shutdown | Graceful Fallback vorhanden, kein Problem |
| FP-18 | R10-024 | CreateRunCancellation CTS-Disposal ausserhalb Lock | newCts bereits referenziert, Race-Window praktisch nicht ausnutzbar |
| FP-19 | R10-025 | AutoSave Timer Change() aus Callback-Context | Standard-Pattern, funktioniert korrekt |

---

## Runde 11 – Konvergenzrunde: Remaining Gaps & Integration Traces

Batch 1: Gezieltes Deep-Dive in bisher nur oberflaechlich geprueften Bereichen (ConsoleSorter, WatchService, DeduplicationEngine Edge Cases, FileClassifier, SafetyValidator, ApiAutomationService, RunManager, DashboardDataBuilder).
Batch 2: Vollstaendige End-to-End Integration Traces (Full Run Flow, Conversion Flow, Rollback Path, Settings Load→Apply→Save).

**4 Bereiche als CLEAN bestaetigt:** ConsoleSorter Pfad-Validation, FileClassifier Edge Cases, SafetyValidator Root-Checks, DeduplicationEngine Cross-Group-Integritaet.

### [R11-001] MEDIUM: M3U Rewrite Ambiguity bei gleichnamigen Dateien in Subpfaden
- **Datei:** `src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs` L648-668
- **Impact:** Wenn zwei Set-Member in verschiedenen Subordnern denselben Dateinamen haben (z.B. `disc1/track01.bin` und `disc2/track01.bin`), kann die relative-Path M3U-Rewrite-Logik die falsche Datei referenzieren.
- **Fix:** Rewrite-Mapping auf vollstaendigen relativen Pfad statt nur Dateiname erweitern.

### [R11-002] LOW: WatchService Event-Handler Race bei Dispose
- **Datei:** `src/Romulus.Infrastructure/Watch/WatchService.cs`
- **Impact:** FileSystemWatcher kann Events nach Dispose feuern. Handler prueft `_disposed` nicht explizit.
- **Fix:** Guard `if (_disposed) return;` am Anfang jedes Event-Handlers.

### [R11-003] LOW: DeduplicationEngine Missing Category Rank gibt 0 zurueck
- **Datei:** `src/Romulus.Core/Deduplication/DeduplicationEngine.cs`
- **Impact:** Wenn ein Candidate eine unbekannte Category hat, gibt GetCategoryRank() 0 zurueck. Das kann dazu fuehren, dass unbekannte Categorien denselben Rank wie valide Low-Priority-Categorien erhalten.
- **Fix:** Unbekannte Categorien auf Int32.MaxValue (niedrigster Rang) setzen.

### [R11-004] MEDIUM: ApiAutomationService Fire-and-Forget Task mit unvollstaendigem Exception-Handling
- **Datei:** `src/Romulus.Api/Services/ApiAutomationService.cs` L119-130
- **Impact:** Task.Run() ohne vollstaendiges Exception-Catching. OperationCanceledException wird gefangen, aber AggregateException oder ObjectDisposedException nicht. Kann zu unobserved Task Exceptions fuehren.
- **Fix:** Generischen Exception-Block um den gesamten Task.Run-Body.

### [R11-005] LOW: RunLifecycleManager Stale Active Marker
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunLifecycleManager.cs`
- **Impact:** Active-Marker-Datei wird bei App-Crash nicht geloescht. Beim naechsten Start wird nur Existenz geprueft, nicht ob Status stale ist (z.B. aelter als 24h).
- **Fix:** Timestamp-/PID-Check beim Lesen des Active-Markers.

### [R11-006] HIGH: Dashboard-KPI-Projektion unvollstaendig bei DryRun-Abschluss
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` L1363-1413
- **Impact:** DryRun-Ergebnis wird in LastRunResult gespeichert, aber DashWinners/DashDupes delegieren an Run.DashWinners (RunViewModel). Wenn Property-Change-Notification vor LastRunResult-Zuweisung feuert, zeigt Dashboard veraltete Werte.
- **Fix:** Explizite Synchronisierung: Dashboard-Properties erst nach LastRunResult-Zuweisung aktualisieren.

### [R11-007] MEDIUM: Conversion-Rollback-Metadaten fehlen bei Partial Failure
- **Datei:** `src/Romulus.Infrastructure/Orchestration/ConversionPhaseHelper.cs` L46-104
- **Impact:** Bei Conversion-Batch: 3 erfolgreich, File 4 fehlt. Audit-Trail enthaelt nur die 3 erfolgreichen. Rollback revertiert 3 Dateien, aber File 4 bleibt in unbekanntem Zustand — kein Audit-Record warnt davor.
- **Fix:** Fehlgeschlagene Conversions als negative/warning Audit-Zeilen loggen.

### [R11-008] HIGH: AutoSave-Race: Settings-Aenderung waehrend aktivem Run unsicher persistiert
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs` L75-107
- **Impact:** User setzt DryRun=false waehrend ein DryRun laeuft. AutoSave persistiert die neue Einstellung sofort. Bei Crash laest der naechste Start DryRun=false — naechster Run ist destruktiv ohne Warnung.
- **Fix:** AutoSave waehrend aktivem Run sperren oder nur nach Run-Abschluss persistieren.

### [R11-009] MEDIUM: Rollback-Preflight prueft keine CSV-Zeilen/Sidecar-Zaehler-Konsistenz
- **Datei:** `src/Romulus.Infrastructure/Audit/RollbackService.cs` L97-106
- **Impact:** Sidecar sagt 52 Zeilen, CSV hat 50. Preflight-DryRun zeigt "52 Dateien werden rollbacked", Execute verarbeitet 50. Divergenz zwischen Preview und Execute.
- **Fix:** Cross-Check zwischen Sidecar-RowCount und tatsaechlicher CSV-Zeilenanzahl.

### [R11-010] MEDIUM: ConvertOnly-Flag wird bei Preflight-Failure nicht zurueckgesetzt
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` L1412-1416
- **Impact:** ConvertOnly=true gesetzt, Preflight schlaegt fehl (z.B. Root nicht erreichbar). CompleteRun(success=false) setzt ConvertOnly nicht zurueck. Naechster Run laeuft unerwartet im ConvertOnly-Modus.
- **Fix:** ConvertOnly unconditional am Ende von CompleteRun zuruecksetzen.

### [R11-011] MEDIUM: LastAuditPath-Zuweisung vor Sidecar-Write erzeugt CanRollback=false
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` L1333-1340
- **Impact:** RunService gibt auditPath zurueck, MainViewModel setzt LastAuditPath, triggered CanRollback-Check. Aber Sidecar-Datei existiert noch nicht → CanRollback=false. Rollback-Button bleibt deaktiviert trotz erfolgreichem Run.
- **Fix:** CanRollback erst nach bestaetiger Sidecar-Existenz auswerten oder Event-basiert aktualisieren.

### [R11-012] MEDIUM: SettingsLoader validiert DatRoot-Pfad nicht bei Load
- **Datei:** `src/Romulus.Infrastructure/Configuration/SettingsLoader.cs` L27-50
- **Impact:** Orphaned Netzwerkpfad in settings.json (z.B. `\\server\dats`) wird nicht validiert. RunOptions enthaelt ungueltige DatRoot. Preflight prueft nur wenn UseDat=true. Bei UseDat=false bleibt stale Pfad unbemerkt bis spaeter aktiviert.
- **Fix:** Warn-Log bei nicht erreichbarem DatRoot in SettingsLoader.Load(). Keine harte Exception, aber diagnostische Sichtbarkeit.

### [R11-013] LOW: Rollback-Undo-Stack nicht persistent ueber App-Restart
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` L1228-1232
- **Impact:** _rollbackUndoStack ist rein in-memory. App-Restart verliert Rollback-Historie. User kann nach Neustart nicht zum letzten Audit navigieren.
- **Fix:** Undo-Stack in AppState oder Settings persistieren (optional, kein Release-Blocker).

### [R11-014] LOW: Conversion-Registry-Policy wird bei Tool-Verlust Mid-Run nicht re-evaluiert
- **Datei:** `src/Romulus.Infrastructure/Conversion/FormatConverterAdapter.cs` L157
- **Impact:** Tool-Availability wird bei Preflight geprueft. Wenn Tool zwischen Preview und Execute geloescht wird, scheitert Conversion als "blocked"/"error" ohne klare Diagnose "Tool not found".
- **Fix:** "Tool not found" als expliziter Fehlergrund in ConversionResult statt generischem Error.

### R11 Zusammenfassung
- **Findings:** 14 (0 CRITICAL, 2 HIGH, 7 MEDIUM, 5 LOW)
- **Bestaetigt CLEAN:** ConsoleSorter Pfad-Validation, FileClassifier Edge Cases, SafetyValidator Root-Checks, DeduplicationEngine Cross-Group-Integritaet
- **Tendenz:** Klare Konvergenz — R10 hatte 47 Findings, R11 nur 14. Ueberwiegend Timing/Race-Conditions und Integration-Edge-Cases.
- **Empfehlung:** R11-006 (Dashboard-KPI) und R11-008 (AutoSave-Race) sind die kritischsten und sollten vor Release adressiert werden.

---

## Runde 12 – Finale Verifikationsrunde

Gezieltes Re-Audit bisher selten beruecksichtigter Bereiche:
- Utility-/Helper-Klassen (VersionHelper, RvzFormatHelper, RootsDragDropHelper)
- XAML-Bindings, ResourceDictionaries, Converter-Registrierungen
- LiteDB Thread-Safety (ConnectionType.Shared + SemaphoreSlim Serialisierung)
- JSON-Daten-Kreuzreferenzen (consoles.json ↔ console-maps.json ↔ rules.json ↔ dat-catalog.json)
- Verbleibende Extension Methods und Cross-Cutting Concerns

### Ergebnis: ZERO FINDINGS — Audit konvergiert.

Alle geprueften Bereiche sind sauber:
- Keine unregistrierten Converter in XAML
- Theme-Ressourcen konsistent ueber alle Theme-Dictionaries
- LiteDB-Zugriffe korrekt serialisiert via SemaphoreSlim
- JSON-Schema-Validierung abdeckend
- Utility-Klassen folgen defensivem Programming
- Exception-Handling konsistent
- Security-Best-Practices eingehalten

---

## Runde 13 – Deep Dive: Hashing / Startup / Schema-Enforcement

3 Batches: (1) Code-Level Deep Dive auf Race Conditions, FileSystem Edge Cases, Hash Verification, DAT Matching, API Validation, Scoring Logic. (2) WPF/XAML Binding, Theme, Converter, ViewModel, Memory Leak Analyse. (3) JSON-Daten-Kreuzreferenzen, i18n, DI, Test Coverage, Logging, Schemas.

**Batch 1 + 2:** ZERO FINDINGS. Alle geprueften Bereiche sauber.
**Batch 3:** 10 potenzielle Findings → 5 Duplikate (R9-040, R2-002/R9-025, R9-001/R9-003, R9-052, AMIGA/AMIGACD Design), 1 False Positive (FP-20), 1 kein Finding → **3 genuinely neue Findings**.

### [R13-001] MEDIUM: Silent Catch-Blocks in Hashing-Services ohne Logging-Kontext
- **Datei:** `src/Romulus.Infrastructure/Hashing/FileHashService.cs` L93-98, `src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs` L48-59
- **Impact:** IOException und UnauthorizedAccessException werden ohne Logging gefangen und null zurueckgegeben bzw. still uebersprungen. Kein diagnostischer Kontext (Dateipfad, Algorithmus, Fehlergrund) wird erfasst. Erschwert Fehlersuche bei Hash-Fehlschlaegen in Produktion.
- **Fix:** Structured Logging in jedem Catch-Block: `_logger.LogWarning("Hash failed for {FilePath}: {Exception}", path, ex)`. Best-Effort-Catches in ArchiveHashService mit LogDebug dokumentieren.

### [R13-002] HIGH: TryResolveDataDir null ohne Logging — SharedServiceRegistration ueberspringt Validierung still
- **Datei:** `src/Romulus.Infrastructure/SharedServiceRegistration.cs` L23-24, `src/Romulus.Infrastructure/Orchestration/RunEnvironmentBuilder.cs` L112
- **Impact:** Wenn TryResolveDataDir() null zurueckgibt (Data-Verzeichnis nicht gefunden), ueberspringt SharedServiceRegistration die Schema-Validierung kommentarlos. Services werden registriert, obwohl Pflicht-Dateien (consoles.json, rules.json etc.) fehlen. Downstream-Code schlaegt spaeter mit kryptischen Fehlern fehl statt beim Start klar abzubrechen.
- **Fix:** LogWarning wenn dataDir null: "Data directory not resolved — schema validation skipped". In Entry Points (CLI/API) optional Fail-Fast wenn dataDir fehlt.

### [R13-003] MEDIUM: tool-hashes.json Schema erzwingt kein SHA256-Hex-Format
- **Datei:** `data/schemas/tool-hashes.schema.json` (Tools.additionalProperties: true), `data/tool-hashes.json`
- **Impact:** Schema akzeptiert beliebige Strings als Tool-Hash-Werte. Validierung auf 64-Zeichen-Hex erfolgt nur in Tests (ToolHashCorrectnessTests), nicht auf Schema-Ebene. Corrupted oder verkuerzte Hashes koennten committed werden ohne Schema-Fehler.
- **Fix:** Schema-Pattern fuer Tool-Hash-Werte: `"pattern": "^[a-f0-9]{64}$"`. Oder zumindest `additionalProperties: { "type": "string", "minLength": 64, "maxLength": 64 }`.

### R13 False Positives

| ID | Finding-Ref | Beschreibung | Begruendung |
|----|-------------|------------|-------------|
| FP-20 | R13-Batch3-008 | JsonlLogWriter Concurrent File Access | Write() korrekt per lock(_lock) serialisiert. RotateIfNeeded() ebenfalls innerhalb desselben Locks. AutoFlush=true. Thread-sicher. |

