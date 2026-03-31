# Full Deep Bughunt – Romulus Tracker

> **Datum:** 2026-03-29
> **Status:** Bedingt release-fähig – P1/P2 geschlossen, verbleibende Restpunkte P4/Security niedrig
> **Hinweis:** Neue Findings werden fortlaufend in diesem Dokument ergänzt.

## Update-Log (2026-03-30)

- [x] BUG-01 umgesetzt: deterministischer Equal-Cost-Tie-Break in ConversionGraph
- [x] BUG-03 umgesetzt: atomarer Set-Member-Move mit Preflight + Rollback
- [x] BUG-04 umgesetzt: numerische `__DUP`-Suffix-Aufloesung
- [x] BUG-52 umgesetzt: API-Default auf `RunConstants.DefaultPreferRegions`
- [x] BUG-53 umgesetzt: `EnableDatAudit`/`EnableDatRename` in RunRecord propagiert
- [x] BUG-61 umgesetzt: Report `ErrorCount` auf `projection.FailCount` normalisiert
- [x] BUG-84/85/86 umgesetzt: Tests auf echte Invarianten gehaertet (Security/Idempotenz/Overflow)
- [x] BUG-18 umgesetzt: BIOS-Key region-aware (`__BIOS__{REGION}__{gameKey}`)
- [x] BUG-19 umgesetzt: DAT-Hash-Match hebt Junk/Unknown/NonGame auf `Game` (außer BIOS)
- [x] BUG-26 umgesetzt: Conversion `SourceBytes`/`TargetBytes` im Result, SavedBytes robust auch nach Source-Move
- [x] BUG-27 umgesetzt: `ConvertLossyWarningCount`/`ConvertVerifyPassedCount`/`ConvertVerifyFailedCount` in `ApplyConversionReport` befüllt
- [x] BUG-28 umgesetzt: Multi-CUE trackt zusätzliche Outputs in `ConversionResult.AdditionalTargetPaths`
- [x] BUG-29 umgesetzt: `PsxtractInvoker.Verify` validiert ISO-artige Outputs statt CHD-Magic
- [x] BUG-30 umgesetzt: Legacy-Pfad blockiert lossy->lossy (`.cso -> .chd`, `.nkit.* -> .rvz`)
- [x] BUG-31 umgesetzt: Tool-Fehlerpfade behalten attempted OutputPath für verlässliches Cleanup
- [x] BUG-05 umgesetzt: ParallelHasher respektiert Cancellation auch im Single-Thread-Pfad
- [x] BUG-06 umgesetzt: RunReportWriter-Invariante validiert auch ohne DedupeGroups
- [x] BUG-13 umgesetzt: Review-Approvals im API-RunRecord nur noch über lock-geschützte Methoden
- [x] BUG-14 umgesetzt: CSV-Formula-Injection nutzt RFC-4180-Quoting statt Prefix-Manipulation
- [x] BUG-15 umgesetzt: Rollback DryRun/Execute nutzen konsistente Unsafe-Counter-Semantik
- [x] BUG-20 umgesetzt: AmbiguousExtension kann Review-Schwelle erreichen
- [x] BUG-21 umgesetzt: Archive-Tie-Break ist deterministisch bei Equal-Size-Entries
- [x] BUG-22 umgesetzt: Switch-Pakete (.nsp/.xci/.nsz/.xcz) nutzen Disc-Size-Tie-Break
- [x] BUG-17 umgesetzt: Timeout + Cancellation werden bis in die Tool-Prozessausführung durchgereicht
- [x] BUG-23 umgesetzt: DAT-Authority greift auch aus UNKNOWN-Startlage bei eindeutiger DAT-Auflösung
- [x] BUG-32 umgesetzt: ToolInvokers übergeben CancellationToken an `InvokeProcess(...)`
- [x] BUG-33 umgesetzt: SourceIntegrity klassifiziert CHD/RVZ/NSP/XCI als Lossless
- [x] BUG-34 umgesetzt: Verify-Fail setzt ConversionOutcome auf Error (Counter/Outcome-Parität)
- [x] BUG-35 umgesetzt: ConversionPhaseHelper schützt gegen DryRun-Ausführung
- [x] BUG-37 umgesetzt: ConversionRegistryLoader blockiert doppelte Console-Keys
- [x] BUG-38 umgesetzt: chdman CD/DVD-Heuristik zentralisiert in ToolInvokerSupport
- [x] Verifikation: `dotnet build src/RomCleanup.sln` grün; fokussierter Testlauf 145 passed / 0 failed
- [x] Verifikation: gezielte Regressionen grün (`CandidateFactoryTests`, `EnrichmentPipelinePhaseAuditPhase3And4Tests`, `ConversionMetricsPipelineTests`)

---

## Release-Blocker (P1)

- [x] **BUG-01** – ConversionGraph: Nicht-deterministischer Pfad bei gleichen Kosten
  - Datei: `ConversionGraph.cs:50-67`
  - Impact: Preview ↔ Execute Divergenz, CLI/GUI Inkonsistenz
  - Fix: Sekundären Tie-Breaker einführen (Tool-Name, Ziel-Extension)
  - [x] TGAP-01: `ConversionGraph_EqualCostPaths_ReturnsDeterministicResult()`

- [x] **BUG-03** – MovePipelinePhase: Set-Member-Move nicht atomar
  - Datei: `MovePipelinePhase.cs:90-130`
  - Impact: Orphaned BIN/TRACK files nach partiellem Fehler (Datenverlust-Risiko)
  - Fix: Preflight-Check ob alle Members moveable, dann Move, bei Fehler Rollback
  - [x] TGAP-02: `MovePipelinePhase_SetMember_PartialFailure_RollsBackDescriptor()`

- [x] **BUG-04** – ConsoleSorter: `__DUP` alphabetische Sortierung bricht bei ≥10
  - Datei: `ConsoleSorter.cs:334-341`
  - Impact: Rollback findet falsches File
  - Fix: Numerischen Comparer verwenden (DUP-Suffix als int parsen)
  - [x] TGAP-03: `FindActualDestination_10PlusDuplicates_ReturnsHighestNumber()`

- [x] **BUG-12** – API: OnlyGames/KeepUnknownWhenOnlyGames Validierung invertiert (false positive)
  - Datei: `Program.cs:322-325`
  - Impact: Kein produktiver Defekt; bestehende Guard-Logik ist konsistent mit CLI/RunOptions-Validierung
  - Fix: Als false positive geschlossen, kein Code-Fix erforderlich
  - [x] TGAP-07: `Api_OnlyGames_KeepUnknown_ValidationMatrix()` (als nicht erforderlich/false positive geschlossen)
  - ⚠️ **Korrekturnotiz (Bughunt #5):** Logik ist tatsächlich korrekt — siehe Analyse in Bughunt #5

- [x] **BUG-52** – API: PreferRegions-Reihenfolge divergiert von RunConstants
  - Datei: `RunLifecycleManager.cs:112`
  - Impact: JP/WORLD vertauscht → andere Dedupe-Ergebnisse als CLI/WPF
  - Fix: `RunConstants.DefaultPreferRegions` statt hardcoded Array
  - [x] TGAP-42: `Api_DefaultPreferRegions_MatchRunConstants()`

- [x] **BUG-53** – API: EnableDatAudit/EnableDatRename nicht in RunRecord propagiert
  - Datei: `RunLifecycleManager.cs:104-130`
  - Impact: DAT-Audit/Rename via API unmöglich; Fingerprint-Widerspruch
  - Fix: `EnableDatAudit = request.EnableDatAudit, EnableDatRename = request.EnableDatRename` in TryCreateOrReuse
  - [x] TGAP-43: `Api_EnableDatAudit_PropagatedToRunRecord()`

---

## Hohe Priorität (P2)

- [x] **BUG-14** – CSV-Report: Formula-Injection via Prefix statt Quoting
  - Datei: `AuditCsvParser.cs:54-73`
  - Impact: Security (OWASP CSV-Injection)
  - Fix: Felder mit `=`, `+`, `-`, `@` in RFC-4180-Quotes wrappen
  - [x] TGAP-08: `SanitizeCsvField_PreventsInjection()`

- [x] **BUG-06** – RunReportWriter: Invarianten-Check übersprungen ohne DedupeGroups
  - Datei: `RunReportWriter.cs:82-88`
  - Impact: Accounting-Fehler in ConvertOnly-Mode bleiben stumm
  - Fix: Guard ändern zu `if (projection.TotalFiles > 0)`
  - [x] TGAP-05: `BuildSummary_NoDedupeGroups_DoesNotThrowInvariant()`

- [x] **BUG-05** – ParallelHasher: CancellationToken im Single-Thread-Pfad ignoriert
  - Datei: `ParallelHasher.cs:46-54`
  - Impact: Cancel wird bei ≤4 Dateien nicht respektiert
  - Fix: CancellationToken an `HashFilesSingleThread()` durchreichen
  - [x] TGAP-04: `HashFilesAsync_SingleThread_RespectsCancellation()`

- [x] **BUG-15** – Rollback DryRun vs Execute: Unterschiedliche Zählung
  - Datei: `AuditSigningService.cs:393-410`
  - Impact: Preview/Execute zeigen unterschiedliche Zahlen
  - Fix: Unified Counter-Semantik
  - [x] TGAP-09: `Rollback_DryRunAndExecute_ReparseUnsafe_CountSemanticsMatch()`

- [x] **BUG-17** – ToolInvokerAdapter: Kein Timeout bei Tool-Aufruf
  - Datei: `ToolInvokerAdapter.cs:64-85`
  - Impact: Hängender Tool-Prozess blockiert Pipeline unbegrenzt
  - Fix: Timeout-Parameter für `InvokeProcess()` implementieren
  - [x] TGAP-25: `Invoke_ForwardsCancellationTokenAndTimeout_ToToolRunner()` + `InvokeProcess_CancelledToken_StopsLongRunningProcess()`

- [x] **BUG-13** – API: ApprovedReviewPaths nicht Thread-Safe
  - Datei: `Program.cs:554`
  - Impact: Parallele POST-Requests können `List<string>` korrumpieren
  - Fix: `ConcurrentBag<string>` oder `lock()` verwenden

---

## Mittlere Priorität (P3)

- [x] **BUG-10** – CLI: Naive CSV-Parsing in `DeriveRootsFromAudit()` — erledigt (2026-06-28)
  - Datei: `Program.cs:340-343`
  - Impact: Root-Pfade mit Komma werden abgeschnitten
  - Fix: `ExtractFirstCsvField()` mit RFC-4180-Quoting statt manuelles `IndexOf(',')`
  - [x] TGAP-06: `ExtractFirstCsvField_HandlesQuotedPaths()` (5 Theory-Cases) — erledigt

- [x] **BUG-11** – CLI: `GetAwaiter().GetResult()` in `UpdateDats()` — erledigt (2026-03-30)
  - Datei: `Program.cs:237`
  - Impact: Deadlock-Risiko (gering im CLI, aber Anti-Pattern)
  - Fix: Methode async machen oder `.Result` mit `ConfigureAwait(false)`

- [x] **BUG-09** – GUI: `async void` Public Method `RefreshReportPreview()` — erledigt (2026-03-30)
  - Datei: `LibraryReportView.xaml.cs:26`
  - Impact: Exceptions können unobserved bleiben
  - Fix: Return-Type auf `async Task` ändern, Caller anpassen

- [x] **BUG-08** – Audit-Action-Strings: Inkonsistente Großschreibung — erledigt (2026-03-30)
  - Datei: `MovePipelinePhase.cs:96` vs `AuditSigningService.cs:262-268`
  - Impact: Audit-Trail inkonsistent (funktional mitigiert durch OrdinalIgnoreCase)
  - Fix: Zentrale Action-Constants einführen (`AuditActions.Move`, etc.)

- [x] **SEC-01** – API JSON Deserialization ohne TypeInfo — erledigt (2026-03-30)
  - Impact: Security (niedrig)

---

## Niedrige Priorität (P4)

- [x] **BUG-07** – DatRenamePipelinePhase: TOCTOU Race Condition — erledigt (2026-03-30)
  - Datei: `DatRenamePipelinePhase.cs:42-52`
  - Impact: Defensiv abgesichert durch `RenameItemSafely`
  - Fix: Vorab-`File.Exists`-Check in Pipeline entfernt; atomare Kollisionserkennung bleibt ausschließlich in `RenameItemSafely()`

- [x] **BUG-16** – RegionDetector: Stille Unknown-Rückgabe ohne Diagnostik — erledigt (2026-03-30)
  - Datei: `RegionDetector.cs:118-145`
  - Impact: Debugging von False-Unknown schwierig
  - Fix: Diagnostische API ergänzt (`GetRegionTagWithDiagnostics` / `DetectWithDiagnostics`)

- [x] **BUG-02** – CompletenessScorer: Hardcodierte Set-Descriptor-Extensions — erledigt (2026-03-30)
  - Datei: `CompletenessScorer.cs:25`
  - Impact: Neue Descriptor-Formate nicht erkannt
  - Fix: Zentrale Descriptor-Quelle in `SetDescriptorSupport` eingeführt und in Scoring/Orchestration verdrahtet

- [ ] **SEC-02** – SSE Stream ohne Max-Concurrency
  - Impact: DoS (niedrig)

- [ ] **SEC-03** – TrustForwardedFor Doku fehlt
  - Impact: Security (gering)

---

## Neue Findings – Fokus Recognition / Classification / Sorting (2026-03-29)

### Priorität 1 (P1)

- [x] **BUG-18** – BIOS-Varianten werden über Region hinweg dedupliziert
  - Dateien: `src/RomCleanup.Core/Classification/CandidateFactory.cs`, `src/RomCleanup.Core/GameKeys/GameKeyNormalizer.cs`
  - Impact: BIOS (z. B. USA/Japan) kann fälschlich zusammengeführt werden
  - Ursache: BIOS-Key basiert auf normalisiertem `gameKey` ohne Region (`__BIOS__{gameKey}`)
  - Fix: BIOS-Key um Region erweitern oder BIOS aus Dedupe-Gruppierung ausnehmen
  - [x] TGAP-11: `Create_BiosSameBaseKeyDifferentRegions_DifferentGameKeys()`
  - Status: erledigt (2026-03-30)

- [x] **BUG-19** – DAT-Hash-Match überschreibt Junk-Kategorie nicht
  - Datei: `src/RomCleanup.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs`
  - Impact: DAT-verifizierte Spiele können trotz Hash-Match in `_TRASH_JUNK` landen
  - Ursache: DAT-Authority aktualisiert Confidence/SortDecision, aber nicht `category`
  - Fix: Bei echtem DAT-Hash-Match Kategorie auf `Game` anheben (Name-only Match ausgenommen)
  - [x] TGAP-12: `Execute_DatHashMatch_OverridesJunkToGame()`
  - Status: erledigt (2026-03-30)

### Priorität 2 (P2)

- [x] **BUG-20** – AmbiguousExtension kann Review-Schwelle nie erreichen
  - Dateien: `src/RomCleanup.Core/Classification/DetectionHypothesis.cs`, `src/RomCleanup.Core/Classification/HypothesisResolver.cs`
  - Impact: AmbiguousExtension-Pfad ist praktisch tot (immer Blocked)
  - Ursache: `SingleSourceCap(AmbiguousExtension)=40` bei `ReviewThreshold=55`
  - Fix: Cap anheben oder Pfad explizit entfernen/dokumentieren
  - [x] TGAP-13: `AmbiguousExtension_SingleSource_CanReachReview()`

- [x] **BUG-21** – ZIP-Inhaltsdetektion ist bei Gleichstand nicht deterministisch
  - Datei: `src/RomCleanup.Core/Classification/ConsoleDetector.cs`
  - Impact: Gleiche ZIP-Inhalte können je nach Entry-Reihenfolge unterschiedliche ConsoleKeys liefern
  - Ursache: Largest-file-Heuristik ohne stabilen Secondary-Tie-Break
  - Fix: Sekundären Sortschlüssel (`Entry.FullName`) ergänzen
  - [x] TGAP-14: `ArchiveDetection_EqualSizeEntries_IsDeterministic()`

- [x] **BUG-22** – Size-TieBreak für Switch-Formate bevorzugt fälschlich kleinere Dateien
  - Datei: `src/RomCleanup.Core/Scoring/FormatScorer.cs`
  - Impact: Bei `nsp/xci` kann unvollständiger Dump gewinnen
  - Ursache: Switch-Formate sind nicht in `DiscExtensions`
  - Fix: `nsp/xci` (ggf. `nsz/xcz`) in Disc-TieBreak-Logik aufnehmen
  - [x] TGAP-15: `SwitchPackages_SizeTieBreak_PrefersLargerCanonicalDump()`

- [x] **BUG-23** – DAT-Match bei UNKNOWN-Konsole wird nicht sauber auf DatVerified gehoben
  - Datei: `src/RomCleanup.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs`
  - Impact: Echte DAT-Treffer können im Review/Blocked-Korridor bleiben
  - Ursache: DAT-Authority ist an Guard `consoleKey != UNKNOWN` gekoppelt
  - Fix: DAT-Authority auch bei UNKNOWN anwenden, wenn DAT-Konsole eindeutig ist
  - [x] TGAP-16: `Execute_UnknownConsoleDatHashMatch_UpgradesToDatVerified()`

### Priorität 3 (P3)

- [x] **BUG-24** – SNES Copier-Header-Bypass nur über Dateigröße — erledigt (2026-03-30)
  - Datei: `src/RomCleanup.Core/Classification/CartridgeHeaderDetector.cs`
  - Impact: False Positives bei Dateien mit `size % 1024 == 512`
  - Ursache: Header-Skip ohne zusätzliche SNES-Header-Validierung
  - Fix: Checksum/Complement-Validierung oder zusätzliche Magic-Prüfung
  - [x] TGAP-17: `SnesHeaderSkip_RequiresValidHeaderConsistency()` — erledigt (2026-03-30)

- [x] **BUG-25** – Regex-Timeouts in Keyword-Detection werden still geschluckt — erledigt (2026-03-30)
  - Datei: `src/RomCleanup.Core/Classification/ConsoleDetector.cs`
  - Impact: Diagnose schwierig bei fehlerhaften/teuren Patterns
  - Ursache: Leerer Catch bei `RegexMatchTimeoutException`
  - Fix: mind. Warn-Logging/Telemetry bei Timeout
  - [x] TGAP-18: `KeywordDetection_RegexTimeout_IsLoggedAndNonFatal()` — erledigt (2026-03-30)

---

## Neue Findings – Fokus Conversion Engine (2026-03-29)

### Executive Verdict

Die Conversion-Engine ist architektonisch solide (Planner→Graph→Executor→Invoker Kette, SEC-CONV-01..07 Guards,
atomisches Multi-CUE-Rollback). Keine akuten Datenverlust-Bugs. Aber: **SavedBytes ist im Execute-Modus
systematisch 0** (P1), **3 Metriken-Counter permanent 0** (P2), **Legacy-Pfad hat keine Lossy→Lossy-Blockade**
(P2), und die **PsxtractInvoker-Verify prüft falsches Format** (P2). In Summe 3 P1, 6 P2, 4 P3 Findings.

### Datenintegritätsrisiken

| Risiko | Stelle | Schutzstatus |
|---|---|---|
| Source vor Verify löschen | ConversionPhaseHelper L82-101 | ✅ Verify VOR Move — korrekt |
| Partial Outputs | ConversionExecutor finally-Block | ✅ Intermediate-Cleanup korrekt |
| Partial Outputs bei Fehler-TargetPath=null | ToolInvokerSupport→ConversionPhaseHelper | ⚠️ Lücke: SEC-CONV-05 greift nicht |
| Lossy→Lossy im Graph-Pfad | ConversionGraph L107-108 | ✅ Geblockt |
| Lossy→Lossy im Legacy-Pfad | FormatConverterAdapter Legacy-Methoden | ❌ Nicht geprüft |
| Multi-CUE Atomizität | ConvertMultiCueArchive | ✅ Rollback bei Teilfehler |
| ZIP-Slip / Zip-Bomb | ExtractZipSafe SEC-CONV-01..04 | ✅ Guards vorhanden |

### Priorität 1 (P1)

- [x] **BUG-26** – SavedBytes ist im Execute-Modus systematisch 0
  - Datei: `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs` (L381-392)
  - Impact: CLI, API, GUI und Reports zeigen Conversion Savings als 0
  - Reproduktion: Beliebige erfolgreiche Conversion ausführen → SavedBytes = 0
  - Erwartetes Verhalten: `SavedBytes = SourceSize - TargetSize`
  - Tatsächliches Verhalten: `ApplyConversionReport` prüft `sourceInfo.Exists` auf Original-Pfad, der bereits nach `_TRASH_CONVERTED` verschoben wurde → `Exists == false` → kein Savings-Delta
  - Ursache: Source wird in `ProcessConversionResult` (L97-101) in Trash verschoben BEVOR `ApplyConversionReport` die Dateigröße liest
  - Fix: Source-Größe im `ConversionResult` als `SourceSizeBytes`-Property speichern (z.B. vor dem Move), oder aus Trash-Pfad ablesen
  - [x] TGAP-19: `ConversionReport_UsesByteSnapshots_WhenSourceFileNoLongerExists()`
  - Status: erledigt (2026-03-30)

- [x] **BUG-27** – LossyWarning/VerifyPassed/VerifyFailed Counter permanent 0
  - Dateien: `src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs`, `RunResultBuilder.cs` (L31-33)
  - Impact: CLI, API, GUI, Reports zeigen immer 0 für Lossy-Warnungen und Verify-Statistik
  - Reproduktion: Beliebige Conversion mit Verify → ConvertVerifyPassedCount bleibt 0
  - Erwartetes Verhalten: Counter werden aus ConversionResults berechnet
  - Tatsächliches Verhalten: Properties `ConvertLossyWarningCount`, `ConvertVerifyPassedCount`, `ConvertVerifyFailedCount` werden **nirgends zugewiesen**
  - Ursache: Fehlende Zuweisungslogik in `ApplyConversionReport`
  - Fix: In `ApplyConversionReport` berechnen aus `results`:
    - `LossyWarning = results.Count(r => r.SourceIntegrity == Lossy && r.Outcome == Success)`
    - `VerifyPassed = results.Count(r => r.VerificationResult == Verified)`
    - `VerifyFailed = results.Count(r => r.VerificationResult == VerifyFailed)`
  - [x] TGAP-20: `RunResultBuilder_VerifyAndLossyCounters_AreProjectedAndMapped()`
  - Status: erledigt (2026-03-30)

- [x] **BUG-28** – Multi-CUE ConversionResult gibt nur ersten Output zurück
  - Datei: `src/RomCleanup.Infrastructure/Conversion/FormatConverterAdapter.cs` (L653)
  - Impact: Bei Multi-Disc-Archiv (z.B. 3-Disc PS1 ZIP) wird nur Disc 1 als TargetPath gespeichert → Disc 2+3 CHDs existieren aber werden nicht auditiert/getrackt
  - Reproduktion: ZIP mit 3 CUE-Dateien konvertieren → `ConversionResult.TargetPath = disc1.chd` nur
  - Erwartetes Verhalten: Alle erzeugten CHDs müssen im Result oder einem TargetPaths-Array referenziert sein
  - Tatsächliches Verhalten: `outputs[0]` als einziger TargetPath, Disc 2+3 ungetrackt
  - Ursache: `ConversionResult` hat nur ein `TargetPath`-Feld, Multi-Output nicht modelliert
  - Fix: `ConversionResult` um `AdditionalTargetPaths` erweitern oder Multi-CUE als separate ConversionResults modellieren
  - [x] TGAP-21: `Convert_MultiCueArchive_TracksAllGeneratedOutputs()`
  - Status: erledigt (2026-03-30)

### Priorität 2 (P2)

- [x] **BUG-29** – PsxtractInvoker.Verify prüft CHD-Magic statt ISO
  - Datei: `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/PsxtractInvoker.cs` (L57-80)
  - Impact: psxtract PBP→ISO erzeugt gültige ISO, aber Verify schlägt fehl wegen CHD-Magic-Check → Error-Counter statt Converted
  - Ursache: Verify-Methode sucht "MComprHD" in Bytes 0-7 — das ist CHD-Format, nicht ISO
  - Fix: ISO-Verify durch Dateigröße > 0 + ggf. ISO-9660-Magic (`CD001` at offset 0x8001) ersetzen
  - [x] TGAP-22: `Verify_ValidIso9660Magic_ReturnsVerified()`
  - Status: erledigt (2026-03-30)

- [x] **BUG-30** – Legacy-Pfad hat keine Lossy→Lossy-Blockade
  - Datei: `src/RomCleanup.Infrastructure/Conversion/FormatConverterAdapter.cs` (Legacy-Methoden ConvertWithChdman/DolphinTool/SevenZip/Psxtract)
  - Impact: CSO→CHD oder NKit→RVZ (beide Lossy) können im Legacy-Pfad durchrutschen
  - Ursache: Nur der Graph-Pfad hat die Lossy→Lossy-Blockade (ConversionGraph L107-108). Der Legacy-Pfad (`Convert()`, `ConvertLegacy()`) prüft SourceIntegrity nicht
  - Fix: SourceIntegrity-Check in `Convert()`/`ConvertLegacy()` vor Tool-Aufruf einbauen
  - [x] TGAP-23: `Convert_LegacyCsoToChd_IsBlocked()` + `Convert_LegacyNkitToRvz_IsBlocked()`
  - Status: erledigt (2026-03-30)

- [x] **BUG-31** – Partial-Output-Cleanup greift nicht bei TargetPath=null
  - Dateien: `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/ToolInvokerSupport.cs` (L69), `src/RomCleanup.Infrastructure/Orchestration/ConversionPhaseHelper.cs` (L120-123)
  - Impact: Tool crasht mit partieller Datei → `ToolInvocationResult.OutputPath=null` → SEC-CONV-05 Guard prüft `convResult.TargetPath` aber dieses ist `null` → Cleanup wird übersprungen → partielle Datei bleibt auf Disk
  - Ursache: Bei `Success=false` setzt `FromToolResult` OutputPath auf null. SEC-CONV-05 kennt den tatsächlichen Pfad nicht mehr
  - Fix: TargetPath auch bei Fehler im ToolInvocationResult setzen (als `AttemptedOutputPath`), oder Cleanup im ConversionExecutor anhand von BuildOutputPath
  - [x] TGAP-24: `Invoke_ToolFailure_ReturnsAttemptedOutputPath()`
  - Status: erledigt (2026-03-30)

- [x] **BUG-32** – CancellationToken wird nicht an InvokeProcess durchgereicht
  - Dateien: `src/RomCleanup.Infrastructure/Conversion/ToolInvokers/PsxtractInvoker.cs` (L33,48), `ChdmanInvoker.cs`, `DolphinToolInvoker.cs`, `SevenZipInvoker.cs`
  - Impact: Cancel-Request während laufendem Tool-Prozess hat keine Wirkung — Tool läuft bis zum Ende
  - Ursache: Token wird nur vor dem Aufruf geprüft (`ThrowIfCancellationRequested`), aber `InvokeProcess` hat keinen CT-Parameter
  - Fix: `IToolRunner.InvokeProcess` um CancellationToken erweitern, bei Cancel den Prozess killen
  - [x] TGAP-25: `Invoke_ForwardsCancellationTokenAndTimeout_ToToolRunner()` + `InvokeProcess_CancelledToken_StopsLongRunningProcess()`

- [x] **BUG-33** – SourceIntegrityClassifier: CHD/RVZ/NKit als Unknown statt korrekt klassifiziert
  - Datei: `src/RomCleanup.Core/Conversion/SourceIntegrityClassifier.cs`
  - Impact: CHD (.chd) und RVZ (.rvz) sind Lossless-Kompressionsformate, werden aber als `Unknown` klassifiziert → bei Unknown+Lossy-Step wird Conversion geblockt obwohl sie sicher wäre
  - Ursache: `LosslessExtensions` enthält `.chd` und `.rvz` NICHT
  - Fix: `.chd`, `.rvz`, `.gcz`, `.wia`, `.nsp`, `.xci` in LosslessExtensions aufnehmen
  - [x] TGAP-26: `Classify_ByExtension_ReturnsExpectedIntegrity()`

- [x] **BUG-34** – ConversionOutcome.Success ≠ counters.Converted (Report-Inkonsistenz)
  - Dateien: `src/RomCleanup.Infrastructure/Orchestration/ConversionPhaseHelper.cs` (L75-113), `RunOrchestrator.PreviewAndPipelineHelpers.cs` (L397-406)
  - Impact: `ConversionReport.Results` enthält Einträge mit `Outcome==Success` die intern als `Errors` gezählt werden (Verify-Failed). Wer direkt Results zählt bekommt andere Zahlen als die Counter
  - Ursache: ConversionPhaseHelper re-klassifiziert `Success→Error` bei Verify-Failure, aber das Outcome im Result bleibt `Success`
  - Fix: Bei Verify-Failure das Outcome im ConversionResult auf `Error` updaten, oder ein separates `FinalOutcome`-Feld einführen
  - [x] TGAP-27: `ConvertSingleFile_VerifyFailed_ReturnsErrorOutcomeAndIncrementsErrorCounter()`

### Priorität 3 (P3)

- [x] **BUG-35** – ConversionPhaseHelper hat keine DryRun-Absicherung
  - Datei: `src/RomCleanup.Infrastructure/Orchestration/ConversionPhaseHelper.cs`
  - Impact: Wenn ein Caller versehentlich `ConvertSingleFile` im Preview-Modus aufruft, wird echte Conversion + Move ausgeführt
  - Ursache: Keine `options.DryRun`-Prüfung in dieser Helper-Klasse
  - Fix: Guard `if (options.DryRun) return null;` am Anfang von `ConvertSingleFile`
  - [x] TGAP-28: `ConvertSingleFile_DryRun_SkipsConversion()`

- [x] **BUG-36** – kein Timeout für Tool-Prozesse
  - Dateien: `src/RomCleanup.Infrastructure/Conversion/ToolInvokerAdapter.cs`, alle ToolInvokers
  - Impact: Hängender chdman/dolphintool/psxtract/7z-Prozess blockiert Pipeline unbegrenzt
  - Ursache: `IToolRunner.InvokeProcess` hat keinen Timeout-Parameter
  - Fix: Konfigurierbare Timeouts pro Tool (z.B. chdman=30min, 7z=10min), Process.Kill bei Überschreitung
  - (bereits als BUG-17 separat getrackt, hier für Conversion-Kontext referenziert)

- [x] **BUG-37** – ConversionRegistryLoader: Doppelte Console-Keys werden still überschrieben
  - Datei: `src/RomCleanup.Infrastructure/Conversion/ConversionRegistryLoader.cs` (L208)
  - Impact: Bei duplizierten Keys in consoles.json gewinnt der letzte Eintrag ohne Warnung
  - Ursache: `policies[key] = policy` ohne Duplikat-Check
  - Fix: Duplikat-Detection + Warn-Log oder Exception
  - [x] TGAP-29: `Constructor_DuplicateConsoleKey_Throws()`

- [x] **BUG-38** – ToolInvokerAdapter.BuildArguments: chdman CD/DVD-Heuristik dupliziert
  - Dateien: `src/RomCleanup.Infrastructure/Conversion/ToolInvokerAdapter.cs` (L131-137), `ChdmanInvoker.cs` (L47-51), `FormatConverterAdapter.cs` (L451-461)
  - Impact: Die "createdvd→createcd bei CD-Image"-Heuristik ist an 3 Stellen implementiert mit leicht unterschiedlichen Schwellwerten
  - Ursache: Legacy-Pfad, Adapter, und spezialisierter Invoker alle mit eigener Kopie
  - Fix: Zentralisieren in `ToolInvokerSupport.ResolveEffectiveChdmanCommand()`
  - [x] TGAP-30: `Invoke_CreatedvdWithSmallIso_UsesCreatecd()`

### Invarianten, die aktuell verletzt werden

1. **SavedBytes-Invariante**: `ConvertSavedBytes > 0` wenn mindestens eine erfolgreiche Compression stattfand → **verletzt** (immer 0)
2. **Counter-Vollständigkeit**: `LossyWarning + VerifyPassed + VerifyFailed > 0` wenn Conversions stattfanden → **verletzt** (immer 0)
3. **Outcome-Counter-Parität**: `Results.Count(Outcome==Success) == ConvertedCount` → **verletzt** (Verify-Failed Success ≠ Error-Counter)
4. **Lossy→Lossy überall blockiert**: Graph hat Guard, Legacy-Pfad nicht → **verletzt**
5. **Multi-Output-Tracking**: Alle erzeugten Dateien müssen als TargetPaths im Result stehen → **verletzt** (Multi-CUE nur outputs[0])
6. **Cleanup-Vollständigkeit**: Jeder fehlgeschlagene Conversion muss partielle Outputs aufräumen → **verletzt** (TargetPath=null Gap)

---

## Fehlende Tests (TGAP)

| ID | Test | Bug | Status |
|---|---|---|---|
| [x] TGAP-01 | `ConversionGraph_EqualCostPaths_ReturnsDeterministic()` | BUG-01 | erledigt (2026-03-30) |
| [x] TGAP-02 | `MovePipelinePhase_SetMember_PartialFailure_RollsBack()` | BUG-03 | erledigt (2026-03-30) |
| [x] TGAP-03 | `FindActualDestination_10PlusDuplicates_ReturnsHighest()` | BUG-04 | erledigt (2026-03-30) |
| [x] TGAP-04 | `HashFilesAsync_SingleThread_RespectsCancellation()` | BUG-05 | erledigt (2026-03-30) |
| [x] TGAP-05 | `BuildSummary_NoDedupeGroups_DoesNotThrowInvariant()` | BUG-06 | erledigt (2026-03-30) |
| [x] TGAP-06 | `ExtractFirstCsvField_HandlesQuotedPaths()` | BUG-10 | erledigt (2026-06-28) |
| [x] TGAP-07 | `Api_OnlyGames_KeepUnknown_ValidationMatrix()` | BUG-12 | false positive (2026-06-28) |
| [x] TGAP-08 | `SanitizeCsvField_PreventsInjection()` | BUG-14 | erledigt (2026-03-30) |
| [x] TGAP-09 | `Rollback_DryRunAndExecute_ReparseUnsafe_CountSemanticsMatch()` | BUG-15 | erledigt (2026-03-30) |
| [ ] TGAP-10 | `Rollback_MissingDestFile_CountsCorrectly()` | — | offen |
| [x] TGAP-11 | `Create_BiosSameBaseKeyDifferentRegions_DifferentGameKeys()` | BUG-18 | erledigt (2026-03-30) |
| [x] TGAP-12 | `Execute_DatHashMatch_OverridesJunkToGame()` | BUG-19 | erledigt (2026-03-30) |
| [x] TGAP-13 | `AmbiguousExtension_SingleSource_CanReachReview()` | BUG-20 | erledigt (2026-03-30) |
| [x] TGAP-14 | `ArchiveDetection_EqualSizeEntries_IsDeterministic()` | BUG-21 | erledigt (2026-03-30) |
| [x] TGAP-15 | `SwitchPackages_SizeTieBreak_PrefersLargerCanonicalDump()` | BUG-22 | erledigt (2026-03-30) |
| [x] TGAP-16 | `Execute_UnknownConsoleDatHashMatch_UpgradesToDatVerified()` | BUG-23 | erledigt (2026-03-30) |
| [x] TGAP-17 | `SnesHeaderSkip_RequiresValidHeaderConsistency()` | BUG-24 | erledigt (2026-03-30) |
| [x] TGAP-18 | `KeywordDetection_RegexTimeout_IsLoggedAndNonFatal()` | BUG-25 | erledigt (2026-03-30) |
| [x] TGAP-19 | `ConversionReport_UsesByteSnapshots_WhenSourceFileNoLongerExists()` | BUG-26 | erledigt (2026-03-30) |
| [x] TGAP-20 | `RunResultBuilder_VerifyAndLossyCounters_AreProjectedAndMapped()` | BUG-27 | erledigt (2026-03-30) |
| [x] TGAP-21 | `Convert_MultiCueArchive_TracksAllGeneratedOutputs()` | BUG-28 | erledigt (2026-03-30) |
| [x] TGAP-22 | `Verify_ValidIso9660Magic_ReturnsVerified()` | BUG-29 | erledigt (2026-03-30) |
| [x] TGAP-23 | `Convert_LegacyCsoToChd_IsBlocked()` + `Convert_LegacyNkitToRvz_IsBlocked()` | BUG-30 | erledigt (2026-03-30) |
| [x] TGAP-24 | `Invoke_ToolFailure_ReturnsAttemptedOutputPath()` | BUG-31 | erledigt (2026-03-30) |
| [x] TGAP-25 | `Invoke_ForwardsCancellationTokenAndTimeout_ToToolRunner()` + `InvokeProcess_CancelledToken_StopsLongRunningProcess()` | BUG-32 | erledigt (2026-03-30) |
| [x] TGAP-26 | `Classify_ByExtension_ReturnsExpectedIntegrity()` | BUG-33 | erledigt (2026-03-30) |
| [x] TGAP-27 | `ConvertSingleFile_VerifyFailed_ReturnsErrorOutcomeAndIncrementsErrorCounter()` | BUG-34 | erledigt (2026-03-30) |
| [x] TGAP-28 | `ConvertSingleFile_DryRun_SkipsConversion()` | BUG-35 | erledigt (2026-03-30) |
| [x] TGAP-29 | `Constructor_DuplicateConsoleKey_Throws()` | BUG-37 | erledigt (2026-03-30) |
| [x] TGAP-30 | `Invoke_CreatedvdWithSmallIso_UsesCreatecd()` | BUG-38 | erledigt (2026-03-30) |

---

## Positiv-Befunde (bestätigt ✓)

- [x] HTML-Reports: Konsequentes `WebUtility.HtmlEncode()`
- [x] XXE-Protection: `DtdProcessing.Prohibit`
- [x] Tool-Invocation: `ArgumentList` statt String-Concat
- [x] API-Auth: Fixed-Time-Comparison
- [x] FileSystem: Root-Validation mit NFC-Normalisierung
- [x] RunOrchestrator: Saubere Phase-Error-Propagation
- [x] ConversionExecutor: Intermediate-Cleanup auf allen Fehler-Pfaden
- [x] ConversionExecutor: Path-Traversal-Guard für Output-Pfade
- [x] ConversionExecutor: Contiguous-Step-Order-Validierung
- [x] ConversionExecutor: Safe-Extension-Validierung
- [x] ZIP-Extraktion: Zip-Slip + Zip-Bomb + Reparse-Point Guards (SEC-CONV-01..07)
- [x] 7z-Extraktion: Post-Extraction Path-Traversal + Reparse-Point Validierung
- [x] ConversionGraph: Lossy→Lossy Blockade im Graph-Pfad
- [x] ConversionGraph: Depth-Limit (max 5 Steps)
- [x] Multi-CUE: Atomisches Rollback bei Teilfehler
- [x] CUE-Selektion: Deterministische alphabetische Sortierung
- [x] ConversionPhaseHelper: Verify VOR Source-Move (korrekte Reihenfolge)
- [x] ConversionPhaseHelper: Counter-Partitionierung ohne Double-Counting
- [x] Set-Member-Move: Root-Validierung (SEC-MOVE-06)
- [x] PBP-Encryption-Detection: Saubere Read-Only-Analyse
- [x] ConversionConditionEvaluator: Safe IOException-Handling für FileSizeProvider

---

## Neue Findings – Fokus GUI / UX / WPF (Bughunt #4)

**Datum:** 2026-06-30
**Scope:** WPF Entry Point, ViewModels, Settings-Persistenz, State Machine, Projections, Code-Behind, XAML Bindings, Threading
**Methode:** Deep Code Reading aller ViewModels, Services, Code-Behind-Dateien, XAML-Bindings; gezielte Grep-Analyse auf Persistenz-Lücken, Threading-Patterns, CanExecute-Logik, Dispatcher-Nutzung

### Executive Verdict

Die GUI-Schicht ist architektonisch solide aufgebaut: MVVM wird konsequent eingehalten, CommunityToolkit.Mvvm wird korrekt verwendet, Projections sind immutabel, und der RunStateMachine-FSM ist sauber implementiert. Der Thread-sichere AddLog-Pattern und die Fingerprint-basierte Move-Gate-Logik sind vorbildlich.

Jedoch bestehen **zwei P1-Befunde** (toter Code mit Divergenz-Risiko), **sechs P2-Befunde** (3× fehlende Settings-Persistenz, 1× Rollback ohne Integrity-Check, 1× tote Konsolen-Filter, 1× unvollständiger Property-Sync), und **fünf P3-Befunde** (UX-Klarheit, Wartbarkeit, async void).

Die kritischsten Risiken: Ein Entwickler könnte versehentlich die toten Rollback-Stacks oder CTS in RunViewModel statt der echten in MainViewModel nutzen, und die Persistenz-Lücken erzeugen bei jedem Neustart Rücksetzungen von MinimizeToTray, IsSimpleMode und SchedulerIntervalMinutes.

---

### BUG-39 · Duplicate Rollback Stacks — Dead Code in RunViewModel — ✅ erledigt (2026-06-28)

| Feld | Wert |
|------|------|
| **Schweregrad** | P1 — Datenverlust-Risiko bei versehentlicher Nutzung |
| **Dateien** | `src/RomCleanup.UI.Wpf/ViewModels/RunViewModel.cs` L90–130, `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` L195–245 |
| **Reproduktion** | RunViewModel enthält `_rollbackUndoStack`, `_rollbackRedoStack`, `PushRollbackUndo()`, `PopRollbackUndo()`, `PopRollbackRedo()`. Identische Kopien existieren in MainViewModel.RunPipeline.cs. Nur MainVM's Kopien werden tatsächlich aufgerufen. |
| **Erwartetes Verhalten** | Eine einzige Rollback-Stack-Implementierung existiert an einem Ort. |
| **Tatsächliches Verhalten** | Zwei parallele Implementierungen — RunVM's Kopien sind Dead Code. |
| **Ursache** | Halbfertiger Refactor: Rollback-Logik wurde nach MainVM.RunPipeline verschoben, RunVM-Kopie nicht entfernt. |
| **Fix** | Rollback-Stacks aus RunViewModel entfernen. |
| **Testabsicherung** | TGAP-31: Bestehende Rollback-Tests müssen weiter grün bleiben nach Deletion. |

---

### BUG-40 · Duplicate CancellationTokenSource — Dead Code in RunViewModel — ✅ erledigt (2026-06-28)

| Feld | Wert |
|------|------|
| **Schweregrad** | P1 — Nicht-abbrechbarer Prozess bei versehentlicher Nutzung |
| **Dateien** | `src/RomCleanup.UI.Wpf/ViewModels/RunViewModel.cs` L368–385, `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` (top) |
| **Reproduktion** | RunViewModel hat eigene `_cts`, `_ctsLock`, `CreateRunCancellation()`, `CancelRun()`. MainViewModel.RunPipeline.cs hat identische Felder. Nur MainVM's CTS wird in `ExecuteRunAsync()` verwendet. |
| **Erwartetes Verhalten** | Eine CTS-Instanz, ein Cancel-Pfad. |
| **Tatsächliches Verhalten** | Zwei parallele CTS-Implementierungen — RunVM's ist Dead Code. |
| **Ursache** | Halbfertiger Refactor: CTS-Management nach MainVM verschoben, RunVM nicht bereinigt. |
| **Fix** | CTS-Logik aus RunViewModel entfernen. |
| **Testabsicherung** | TGAP-32: Cancel-Tests müssen nach Deletion weiter grün bleiben. |

---

### BUG-41 · Rollback ohne Trash-Integrity-Preflight — ✅ erledigt (2026-03-30)

| Feld | Wert |
|------|------|
| **Schweregrad** | P2 — Datenverlust-Risiko (stiller Teilfehler) |
| **Dateien** | `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` L607–640, `src/RomCleanup.Infrastructure/Audit/RollbackService.cs` L47 |
| **Reproduktion** | `OnRollbackAsync()` prüft nur `File.Exists(LastAuditPath)`, ruft dann direkt `RollbackService.Execute()` auf. `RollbackService.VerifyTrashIntegrity()` existiert (L47), wird aber nie vor dem Rollback aufgerufen. |
| **Erwartetes Verhalten** | Vor dem Rollback wird `VerifyTrashIntegrity()` aufgerufen. Bei fehlenden Trash-Dateien wird der User gewarnt und kann abbrechen. |
| **Tatsächliches Verhalten** | Rollback wird ohne Integritätsprüfung gestartet. Manuell gelöschte Trash-Dateien führen zu stillen Fehlern (SkippedMissingDest im Result). |
| **Ursache** | VerifyTrashIntegrity wurde implementiert, aber nie in den UI-Rollback-Flow integriert. |
| **Fix** | Vor `RollbackService.Execute()` erst `VerifyTrashIntegrity()` aufrufen und Ergebnis im Confirm-Dialog anzeigen. |
| **Testabsicherung** | TGAP-33: `Rollback_WithMissingTrashFiles_ShowsIntegrityWarning()` |

---

### BUG-42 · MinimizeToTray wird nicht persistiert — ✅ erledigt (2026-06-28)

| Feld | Wert |
|------|------|
| **Schweregrad** | P2 — Settings werden still zurückgesetzt |
| **Dateien** | `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.Settings.cs` L31 (AutoSavePropertyNames), `src/RomCleanup.UI.Wpf/Services/SettingsDto.cs`, `src/RomCleanup.UI.Wpf/Services/SettingsService.cs` |
| **Reproduktion** | 1) User aktiviert MinimizeToTray. 2) Neustart. 3) MinimizeToTray ist deaktiviert. |
| **Erwartetes Verhalten** | MinimizeToTray überlebt Neustarts. |
| **Tatsächliches Verhalten** | Property ist in `AutoSavePropertyNames` (Debounce-Timer triggert `_settingsDirty`), aber `SettingsDto` hat kein `MinimizeToTray`-Feld und `SettingsService.SaveFrom()`/`Load()` enthält es nicht. |
| **Ursache** | Property wurde zur AutoSave-Liste hinzugefügt, aber nie zum DTO und Service propagiert. |
| **Fix** | `MinimizeToTray` zu SettingsDto hinzufügen, in SettingsService.SaveFrom (ui-Section) und Load/ApplyToViewModel aufnehmen. |
| **Testabsicherung** | TGAP-34: `Settings_MinimizeToTray_RoundTrip()` |

---

### BUG-43 · IsSimpleMode wird nicht persistiert — ✅ erledigt (2026-06-28)

| Feld | Wert |
|------|------|
| **Schweregrad** | P2 — Settings werden still zurückgesetzt |
| **Dateien** | `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.Settings.cs` L466–472, `src/RomCleanup.UI.Wpf/ViewModels/SetupViewModel.cs` L182–188, `src/RomCleanup.UI.Wpf/Services/SettingsDto.cs`, `src/RomCleanup.UI.Wpf/Services/SettingsService.cs` |
| **Reproduktion** | 1) User wechselt in Expert-Modus. 2) Neustart. 3) App startet immer in Simple-Modus (default `true`). |
| **Erwartetes Verhalten** | IsSimpleMode überlebt Neustarts. |
| **Tatsächliches Verhalten** | Identische Property existiert in MainViewModel UND SetupViewModel (Duplikat), weder in SettingsDto noch in SettingsService enthalten. |
| **Ursache** | Property wurde als UI-State betrachtet, nicht als persistierbare Einstellung. Zusätzlich: Duplikat in zwei ViewModels. |
| **Fix** | 1) `IsSimpleMode` zu SettingsDto und SettingsService hinzufügen. 2) Duplikat in SetupViewModel entfernen, stattdessen an MainViewModel delegieren. |
| **Testabsicherung** | TGAP-35: `Settings_IsSimpleMode_RoundTrip()`, TGAP-36: `SetupVM_IsSimpleMode_DelegatesToMainVM()` |

---

### BUG-44 · Unvollständige Main→Setup Property-Synchronisation — ✅ erledigt (2026-06-28)

| Feld | Wert |
|------|------|
| **Schweregrad** | P2 — UI-Inkonsistenz |
| **Dateien** | `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.Settings.cs` L65–107, L689–705 |
| **Reproduktion** | 1) Ändere ToolDolphin im Setup-Tab → wird korrekt zu MainVM propagiert (OnSetupPropertyChanged via Reflection). 2) Ändere ToolDolphin programmatisch auf MainVM (z.B. via Settings-Load) → Setup-Tab zeigt alten Wert. |
| **Erwartetes Verhalten** | Alle Tool-Pfade werden bidirektional synchronisiert. |
| **Tatsächliches Verhalten** | `SyncToSetup()` wird nur für `TrashRoot` (L65) und `ToolChdman` (L90) aufgerufen. ToolDolphin (L93), Tool7z (L96), ToolPsxtract (L99), ToolCiso (L102) rufen `SyncToSetup()` nicht auf. Reverse-Sync (Setup→Main) funktioniert für alle via Reflection. |
| **Ursache** | SyncToSetup-Aufrufe wurden bei der Erweiterung der Tool-Pfade nicht für alle neuen Properties hinzugefügt. |
| **Fix** | `SyncToSetup()` für alle Tool-Pfad-Properties im Setter aufrufen. |
| **Testabsicherung** | TGAP-37: `MainVM_ToolPathChange_PropagesToSetupVM(string toolProperty)` |

---

### BUG-45 · Console-Filter haben keinen Pipeline-Effekt — ✅ erledigt (2026-03-30)

| Feld | Wert |
|------|------|
| **Schweregrad** | P2 — Fehlbedienungsrisiko / irreführende UI |
| **Dateien** | `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.cs` L330–337, `src/RomCleanup.UI.Wpf/Services/RunService.cs` L143–183 |
| **Reproduktion** | 1) Deaktiviere "PS1" in Console-Filtern. 2) Starte DryRun. 3) PS1-ROMs werden trotzdem verarbeitet. |
| **Erwartetes Verhalten** | Console-Filter begrenzen, welche Konsolen im Pipeline verarbeitet werden, ODER die Filter sind klar als "Anzeige-Filter" gekennzeichnet. |
| **Tatsächliches Verhalten** | `GetSelectedConsoles()` existiert in MainViewModel (L336) und SetupViewModel (L284), wird aber von `ViewModelRunOptionsSource` NICHT gelesen. `IRunOptionsSource` hat kein Console-Filter-Feld. Die Pipeline verarbeitet alle Konsolen unabhängig von der UI-Auswahl. |
| **Ursache** | Console-Filter-Feature wurde in der UI implementiert, aber nie an die Pipeline angebunden. |
| **Fix** | Entweder: (A) Console-Filter in `IRunOptionsSource` / `RunOptions` aufnehmen und im Pipeline respektieren. Oder: (B) Console-Filter in der UI klar als "Anzeige-/Report-Filter" kennzeichnen, nicht als Pipeline-Steuerung. |
| **Testabsicherung** | TGAP-38: `RunOptions_ConsoleFilter_ExcludesConsoles()` oder `ConsoleFilter_LabelClearlyIndicatesDisplayOnly()` |

---

### BUG-46 · SchedulerIntervalMinutes wird nicht persistiert — ✅ erledigt (2026-06-28)

| Feld | Wert |
|------|------|
| **Schweregrad** | P2 — Settings werden still zurückgesetzt |
| **Dateien** | `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.Settings.cs` L188–203, `src/RomCleanup.UI.Wpf/Services/SettingsDto.cs`, `src/RomCleanup.UI.Wpf/Services/SettingsService.cs` |
| **Reproduktion** | 1) User setzt Scheduler-Intervall auf 30 Minuten. 2) Neustart. 3) Intervall ist 0 (default). |
| **Erwartetes Verhalten** | SchedulerIntervalMinutes überlebt Neustarts. |
| **Tatsächliches Verhalten** | Property existiert in MainViewModel (L188), wird in RunPipeline gelesen (L1004), fehlt aber in SettingsDto und SettingsService. |
| **Ursache** | Feature wurde implementiert, DTO-/Service-Integration vergessen. |
| **Fix** | `SchedulerIntervalMinutes` zu SettingsDto und SettingsService hinzufügen. |
| **Testabsicherung** | TGAP-39: `Settings_SchedulerIntervalMinutes_RoundTrip()` |

---

### BUG-47 · Dashboard unterscheidet nicht zwischen Plan (DryRun) und Actual (Move) — ✅ erledigt (2026-03-30)

| Feld | Wert |
|------|------|
| **Schweregrad** | P3 — UX-Klarheit |
| **Dateien** | `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` L1092–1185, `src/RomCleanup.Contracts/Models/DashboardProjection.cs` |
| **Reproduktion** | 1) DryRun → Dashboard zeigt "Winners: 42, Dupes: 18". 2) Move → Dashboard zeigt "Winners: 42, Dupes: 18" im selben Format. Kein visueller Unterschied, kein Vergleich DryRun-Vorhersage ↔ Move-Ergebnis. |
| **Erwartetes Verhalten** | DryRun-Ergebnisse sind als "(Plan)" / "(Vorschau)" markiert. Nach Move werden Plan und Actual verglichen. |
| **Tatsächliches Verhalten** | `DashboardProjection.From()` nutzt dieselbe Darstellung für beide Modi. `MarkProvisional()` existiert für Cancelled/Failed, aber nicht für DryRun. |
| **Ursache** | DashboardProjection unterscheidet nur `isPartial` (Cancelled/Failed), nicht `isDryRun`. |
| **Fix** | DryRun-KPIs mit "(Plan)" Suffix markieren. Optional: Nach Move Plan↔Actual Delta anzeigen. |
| **Testabsicherung** | TGAP-40: `DashboardProjection_DryRun_ShowsPlanMarker()` |

---

### BUG-48 · ErrorSummaryProjection trunciert bei 50 ohne Report-Link — ✅ erledigt (2026-03-30)

| Feld | Wert |
|------|------|
| **Schweregrad** | P3 — UX |
| **Dateien** | `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` (ErrorSummaryProjection-Nutzung) |
| **Reproduktion** | Run mit 100+ Fehlern → Nur 50 angezeigt, "… und 50 weitere" Text, kein Klick-Link zum vollständigen Report. |
| **Erwartetes Verhalten** | Truncation-Hinweis enthält einen Link/Button zum vollständigen Report. |
| **Tatsächliches Verhalten** | Nur Texthinweis ohne Aktion. |
| **Ursache** | Feature unvollständig implementiert. |
| **Fix** | "Vollständigen Report öffnen" Link im Truncation-Hinweis ergänzen. |
| **Testabsicherung** | Kein dedizierter Test nötig — rein UI. |

---

### BUG-49 · LibraryReportView: async void + fehlende Pfadvalidierung — ✅ erledigt (2026-03-30)

| Feld | Wert |
|------|------|
| **Schweregrad** | P3 — Stabilität |
| **Dateien** | `src/RomCleanup.UI.Wpf/Views/LibraryReportView.xaml.cs` L26, L50 |
| **Reproduktion** | `RefreshReportPreview()` ist `async void` (kein Event Handler), ruft `Path.GetFullPath(vm.LastReportPath)` ohne vorige TryNormalizePath-Validierung auf. Bei ungültigem Pfad → unbehandelte Exception. |
| **Erwartetes Verhalten** | Methode ist `async Task` und der Aufrufer awaited. Pfad wird vor `GetFullPath` validiert. |
| **Tatsächliches Verhalten** | `async void` verschluckt Exception-Kontext. Pfad wird nur auf `IsNullOrEmpty` geprüft, nicht auf Gültigkeit. |
| **Ursache** | Quick-fix Implementierung ohne Robustifizierung. |
| **Fix** | Zu `async Task` ändern, `TryNormalizePath()` vor Pfad-Nutzung einsetzen. |
| **Testabsicherung** | TGAP-41: `LibraryReportView_InvalidPath_DoesNotThrow()` |

---

### BUG-50 · MissionControlViewModel unvollständig — ✅ erledigt (2026-03-30)

| Feld | Wert |
|------|------|
| **Schweregrad** | P3 — Wartbarkeit |
| **Dateien** | `src/RomCleanup.UI.Wpf/ViewModels/MissionControlViewModel.cs` |
| **Reproduktion** | ViewModel hat nur 5 Properties, keine LastRun-Persistenz, SourceCount dupliziert Roots.Count Logik. |
| **Erwartetes Verhalten** | Entweder vollständig implementiert oder entfernt/als Stub gekennzeichnet. |
| **Tatsächliches Verhalten** | Halbfertiges ViewModel ohne klaren Nutzen. |
| **Ursache** | Feature-Entwicklung wurde nicht abgeschlossen. |
| **Fix** | Entweder fertigstellen oder als expliziten Stub markieren mit Tracking-Issue. |
| **Testabsicherung** | Kein dedizierter Test nötig. |

---

### BUG-51 · Duplicate IsSimpleMode/IsExpertMode in Main+Setup ViewModel — ✅ erledigt (2026-06-28)

| Feld | Wert |
|------|------|
| **Schweregrad** | P3 — Doppelte Logik |
| **Dateien** | `src/RomCleanup.UI.Wpf/ViewModels/MainViewModel.Settings.cs` L466–472, `src/RomCleanup.UI.Wpf/ViewModels/SetupViewModel.cs` L182–188 |
| **Reproduktion** | Identischer Code in zwei ViewModels: `_isSimpleMode` Backing-Feld, Getter, Setter mit `SetProperty`, `IsExpertMode => !_isSimpleMode`. |
| **Erwartetes Verhalten** | Eine Single Source of Truth. |
| **Tatsächliches Verhalten** | Zwei unabhängige Kopien ohne Synchronisation. Änderung an einer ist für die andere unsichtbar. |
| **Ursache** | Copy-Paste bei ViewModel-Extraktion. |
| **Fix** | SetupViewModel.IsSimpleMode an MainViewModel delegieren (oder über Constructor-Parameter binden). |
| **Testabsicherung** | TGAP-36 (s. BUG-43). |

---

### Fehlbedienungsrisiken (Übersicht)

| # | Risiko | Betroffene Bugs |
|---|--------|----------------|
| 1 | Console-Filter suggerieren Pipeline-Kontrolle, haben aber keinen Effekt → User erwartet Einschränkung, die nicht stattfindet | BUG-45 |
| 2 | MinimizeToTray/IsSimpleMode/SchedulerInterval gehen bei Neustart verloren → User muss immer neu konfigurieren | BUG-42, BUG-43, BUG-46 |
| 3 | Rollback ohne Integrity-Check → stille Teilfehler wenn Trash manuell bereinigt wurde | BUG-41 |
| 4 | Dashboard-KPIs zeigen DryRun und Move identisch an → User kann Plan und Ergebnis nicht unterscheiden | BUG-47 |
| 5 | Setup-Tab zeigt ggf. veraltete Tool-Pfade nach programmatischem Settings-Load | BUG-44 |

---

### Zustands- und Paritätsprobleme

| # | Problem | Betroffene Bugs |
|---|---------|----------------|
| 1 | Dual-truth für Rollback-Stacks (RunVM vs. MainVM) — falscher Stack könnte benutzt werden | BUG-39 |
| 2 | Dual-truth für CancellationTokenSource (RunVM vs. MainVM) | BUG-40 |
| 3 | Dual-truth für IsSimpleMode (MainVM vs. SetupVM) | BUG-51 |
| 4 | Main→Setup Sync nur für 2 von 6 Tool-Pfaden implementiert | BUG-44 |
| 5 | Console-Filter-State existiert in UI, aber nicht in Pipeline-State | BUG-45 |

---

### Top 10 Fixes (priorisiert)

| # | Fix | Aufwand | Bugs |
|---|-----|---------|------|
| 1 | Dead Code entfernen: Rollback-Stacks + CTS aus RunViewModel | Klein | BUG-39, BUG-40 |
| 2 | MinimizeToTray in SettingsDto + SettingsService aufnehmen | Klein | BUG-42 |
| 3 | IsSimpleMode in SettingsDto + SettingsService aufnehmen + Duplikat in SetupVM entfernen | Klein | BUG-43, BUG-51 |
| 4 | SchedulerIntervalMinutes in SettingsDto + SettingsService aufnehmen | Klein | BUG-46 |
| 5 | SyncToSetup() für alle Tool-Pfade hinzufügen (ToolDolphin, Tool7z, ToolPsxtract, ToolCiso) | Klein | BUG-44 |
| 6 | VerifyTrashIntegrity() vor Rollback.Execute() aufrufen + Dialog | Mittel | BUG-41 |
| 7 | Console-Filter: Entweder Pipeline-Integration ODER klare "Anzeige-Filter" Kennzeichnung | Mittel | BUG-45 |
| 8 | DashboardProjection: DryRun-KPIs mit "(Plan)" markieren | Klein | BUG-47 |
| 9 | LibraryReportView.RefreshReportPreview → async Task + TryNormalizePath | Klein | BUG-49 |
| 10 | ErrorSummary: Report-Link bei Truncation ergänzen | Klein | BUG-48 |

---

### Positiv-Befunde GUI (bestätigt ✓)

- [x] MVVM konsequent: Keine Businesslogik im Code-Behind (MainWindow delegiert vollständig)
- [x] AddLog: Thread-sicherer Dispatcher-Pattern mit CheckAccess + InvokeAsync
- [x] RunStateMachine: 11-State FSM mit expliziter Transition-Validierung
- [x] Preview-Fingerprint: 23 Properties im Hash → robustes Move-Gate
- [x] ConfigChangedBanner (TASK-176): Korrekte Erkennung von Fingerprint-Divergenz
- [x] CanStartCurrentRun: Saubere Komposition aus IsBusy + Roots + Validation + Fingerprint
- [x] NotifyAllCommands: 13 Commands werden bei State-Change aktualisiert
- [x] XAML Bindings: Keine TwoWay-Bindings auf Read-Only-Properties gefunden
- [x] Path Traversal Guard: DAT-Import in FeatureCommandService korrekt geschützt
- [x] Settings Auto-Save: 2s Debounce + 5min Periodic → kein Datenverlust bei Crash
- [x] OnClosing: Sauberer busy-cancel-wait-reclose Pattern mit _isClosing Guard
- [x] INotifyDataErrorInfo: Tool-Pfade und Directories werden validiert (blocking vs warning)
- [x] Async Event Handler: OnClosing + OnRunRequested haben korrekte Exception-Handler

---

### Konsolidierte Test-Gap-Tabelle (GUI/UX/WPF)

| Status | ID | Test | Bug | Prio |
|--------|-----|------|-----|------|
| [ ] | TGAP-31 | `Rollback_AfterDeadCodeRemoval_StillWorks()` | BUG-39 | offen |
| [ ] | TGAP-32 | `Cancel_AfterDeadCodeRemoval_StillWorks()` | BUG-40 | offen |
| [x] | TGAP-33 | `Rollback_WithMissingTrashFiles_ShowsIntegrityWarning()` | BUG-41 | erledigt (2026-03-30) |
| [ ] | TGAP-34 | `Settings_MinimizeToTray_RoundTrip()` | BUG-42 | offen |
| [ ] | TGAP-35 | `Settings_IsSimpleMode_RoundTrip()` | BUG-43 | offen |
| [ ] | TGAP-36 | `SetupVM_IsSimpleMode_DelegatesToMainVM()` | BUG-43, BUG-51 | offen |
| [ ] | TGAP-37 | `MainVM_ToolPathChange_PropagesToSetupVM(string toolProperty)` | BUG-44 | offen |
| [x] | TGAP-38 | `ConsoleFilter_PipelineOrLabel_IsCorrect()` | BUG-45 | erledigt (2026-03-30) |
| [ ] | TGAP-39 | `Settings_SchedulerIntervalMinutes_RoundTrip()` | BUG-46 | offen |
| [x] | TGAP-40 | `DashboardProjection_DryRun_ShowsPlanMarker()` | BUG-47 | erledigt (2026-03-30) |
| [x] | TGAP-41 | `LibraryReportViewPathTests` (`TryNormalizeReportPath_*`) | BUG-49 | erledigt (2026-03-30) |

---

## Bughunt #5 – CLI / API / Output-Parität

> **Scope:** CLI, API, Output-Modelle, RunOptions-Defaults, Preflight, Exit-Codes, SSE, Sidecar-Parität
> **Datum:** 2026-06
> **Methode:** Deep Code Reading aller Entry Points + field-by-field Vergleich CliDryRunOutput / ApiRunResult / RunProjection

### Executive Verdict

Die drei Entry Points (CLI, API, WPF) konvergieren architektonisch sauber auf RunOptionsFactory → RunOptionsBuilder.Normalize → RunOrchestrator. Die Projection-Ebene (RunProjectionFactory) ist vollständig geteilt. Kritische Parität ist bei den numerischen KPIs sicher. Aber: **zwei P1-Propagation-Bugs** verursachen fachlich falsche API-Ergebnisse, und die zentrale RunOptionsBuilder.Validate() ist toter Code.

### Kritische Paritätsfehler

| # | Bug | Prio | Entry Point | Impact |
|---|-----|------|-------------|--------|
| 1 | PreferRegions-Reihenfolge divergiert | P1 | API | JP/WORLD vertauscht → andere Dedupe-Ergebnisse |
| 2 | EnableDatAudit/EnableDatRename nicht propagiert | P1 | API | DAT-Audit/Rename via API unmöglich |
| 3 | RunOptionsBuilder.Validate() nie aufgerufen | P2 | Alle | WPF hat keinen OnlyGames-Guard; zentrale Validierung ist dead code |
| 4 | CLI DryRun JSON ohne PreflightWarnings | P2 | CLI | Stille Feature-Skips im DryRun |
| 5 | RunStatusDto fehlen EnableDatAudit/EnableDatRename | P2 | API | Client kann DAT-Settings nicht verifizieren |

---

### BUG-52 · PreferRegions-Reihenfolge in API divergiert von RunConstants

| **Status** | Erledigt am 2026-03-30 |

| Feld | Wert |
|------|------|
| **Schweregrad** | P1 — Preview/Execute Divergenz zwischen CLI/WPF und API |
| **Dateien** | `src/RomCleanup.Api/RunLifecycleManager.cs` L112, `src/RomCleanup.Contracts/RunConstants.cs` |
| **Reproduktion** | 1) POST /runs ohne `preferRegions` → API verwendet `["EU","US","WORLD","JP"]`. 2) CLI ohne `-Prefer` → verwendet `RunConstants.DefaultPreferRegions` = `["EU","US","JP","WORLD"]`. 3) Gleiche ROM-Sammlung liefert verschiedene Winner bei JP-WORLD-Tie. |
| **Erwartetes Verhalten** | Alle Entry Points verwenden `RunConstants.DefaultPreferRegions` = `["EU","US","JP","WORLD"]` als Default. |
| **Tatsächliches Verhalten** | `RunLifecycleManager.TryCreateOrReuse()` L112 hat hardcoded `new[] { "EU", "US", "WORLD", "JP" }` — JP und WORLD sind vertauscht. |
| **Ursache** | Hardcoded Array statt `RunConstants.DefaultPreferRegions`-Referenz bei API-Sonderlogik. |
| **Fix** | L112 ersetzen: `request.PreferRegions is { Length: > 0 } ? request.PreferRegions : RunConstants.DefaultPreferRegions` |
| **Testabsicherung** | TGAP-42: `Api_DefaultPreferRegions_MatchRunConstants()` |

---

### BUG-53 · EnableDatAudit und EnableDatRename werden nicht in RunRecord propagiert

| **Status** | Erledigt am 2026-03-30 |

| Feld | Wert |
|------|------|
| **Schweregrad** | P1 — Feature-Verlust in API |
| **Dateien** | `src/RomCleanup.Api/RunLifecycleManager.cs` L104–130 |
| **Reproduktion** | 1) POST /runs mit `{"enableDat": true, "enableDatAudit": true, "enableDatRename": true}`. 2) RunRecord hat `EnableDatAudit=false`, `EnableDatRename=false` (default). 3) RunRecordOptionsSource propagiert `false` → RunOptions hat DAT-Audit/Rename deaktiviert. 4) Fingerprint (L376-377) berücksichtigt die Flags korrekt → Idempotency-Widerspruch. |
| **Erwartetes Verhalten** | RunRecord übernimmt `request.EnableDatAudit` und `request.EnableDatRename`. |
| **Tatsächliches Verhalten** | Die Properties fehlen im RunRecord-Initializer bei `TryCreateOrReuse()`. Sie existieren in RunRequest (L198-199), RunRecord (L242-243) und RunRecordOptionsSource (L124-125), aber die Brücke in TryCreateOrReuse fehlt. |
| **Ursache** | Unvollständige Property-Übernahme bei Erweiterung des RunRequest-Modells. |
| **Fix** | In `TryCreateOrReuse()` L104-130 ergänzen: `EnableDatAudit = request.EnableDatAudit, EnableDatRename = request.EnableDatRename,` |
| **Testabsicherung** | TGAP-43: `Api_EnableDatAudit_PropagatedToRunRecord()`, `Api_EnableDatRename_PropagatedToRunRecord()` |

---

### BUG-54 · RunOptionsBuilder.Validate() nie in Produktionscode aufgerufen — ✅ erledigt (2026-03-30)

| Feld | Wert |
|------|------|
| **Schweregrad** | P2 — WPF ohne OnlyGames-Guard; zentrale Validierung toter Code |
| **Dateien** | `src/RomCleanup.Infrastructure/Orchestration/RunOptionsBuilder.cs` L12-26, `src/RomCleanup.Infrastructure/Orchestration/RunOptionsFactory.cs` |
| **Reproduktion** | 1) Suche nach `RunOptionsBuilder.Validate` → 5 Treffer, alle in Tests oder Plan-Docs. 2) `RunOptionsFactory.Create()` ruft nur `Normalize()`, nicht `Validate()` auf. 3) WPF hat keine eigene OnlyGames-Validierung → `OnlyGames=false, KeepUnknown=false` kann zum Orchestrator gelangen. |
| **Erwartetes Verhalten** | `RunOptionsFactory.Create()` oder `RunOptionsBuilder.Normalize()` ruft `Validate()` auf und wirft bei Fehlern. CLI und API haben eigene Checks, WPF verlässt sich auf zentrale Validierung — die nie stattfindet. |
| **Tatsächliches Verhalten** | Zentrale Validierung ist dead code. CLI (CliArgsParser L350) und API (Program.cs L343) haben jeweils eigene, redundante Checks. WPF hat keinen. |
| **Ursache** | TASK-159 hat `Validate()` zentralisiert, aber nie in die Factory oder den Orchestrator verdrahtet. |
| **Fix** | `RunOptionsFactory.Create()` → nach Normalize() auch `Validate()` aufrufen und bei Errors eine `InvalidOperationException` werfen. CLI/API können redundante Checks behalten als defense-in-depth. |
| **Testabsicherung** | TGAP-44: `RunOptionsFactory_InvalidOptions_ThrowsFromValidate()` |

---

### BUG-55 · CLI DryRun JSON enthält keine PreflightWarnings — ✅ erledigt (2026-03-30)

| Feld | Wert |
|------|------|
| **Schweregrad** | P2 — CLI-Automation erhält keine Warnung über stille Feature-Skips |
| **Dateien** | `src/RomCleanup.CLI/CliOutputWriter.cs` L213-261, `src/RomCleanup.CLI/Program.cs` L134-145 |
| **Reproduktion** | 1) `romulus -Roots "D:\Roms" -SortConsole` (DryRun default). 2) JSON-Output enthält kein `PreflightWarnings`-Feld. 3) `SortConsole` wird still übersprungen. 4) Gleicher Request via API → `PreflightWarnings: ["SortConsole is enabled but will be skipped in DryRun mode."]` |
| **Erwartetes Verhalten** | `CliDryRunOutput` enthält `PreflightWarnings`-Array wie `ApiRunResult`. |
| **Tatsächliches Verhalten** | `CliDryRunOutput` hat kein `PreflightWarnings`-Property. Der Orchestrator emittiert Warnings via `onProgress` → `SafeErrorWriteLine`, aber diese sind nur auf stderr, nicht im JSON. CI/CD-Pipelines parsen JSON, nicht stderr. |
| **Ursache** | `CliDryRunOutput` wurde ohne Warnings-Feld definiert; `RunResult.Preflight.Warnings` wird in `FormatDryRunJson` nicht ausgewertet. |
| **Fix** | 1) `CliDryRunOutput` um `string[] PreflightWarnings` ergänzen. 2) In `FormatDryRunJson` Parameter `RunResult result` ergänzen und `result.Preflight?.Warnings` mappen. |
| **Testabsicherung** | TGAP-45: `Cli_DryRunJson_IncludesPreflightWarnings()` |

---

### BUG-56 · RunStatusDto fehlen EnableDatAudit/EnableDatRename — ✅ erledigt (2026-03-30)

| Feld | Wert |
|------|------|
| **Schweregrad** | P2 — API-Client kann akzeptierte DAT-Settings nicht verifizieren |
| **Dateien** | `src/RomCleanup.Api/RunManager.cs` L438-470 (RunStatusDto), L473-510 (ToDto) |
| **Reproduktion** | 1) POST /runs mit `enableDatAudit: true`. 2) GET /runs/{id} → Antwort enthält kein `enableDatAudit`-Feld. 3) Client kann nicht prüfen, ob sein Setting akzeptiert wurde. |
| **Erwartetes Verhalten** | `RunStatusDto` enthält `EnableDatAudit` und `EnableDatRename`, `ToDto()` mappt sie. |
| **Tatsächliches Verhalten** | Properties fehlen in `RunStatusDto` und `ToDto()`. Auch nach Fix von BUG-53 wären die Flags nicht im Status-DTO sichtbar. |
| **Ursache** | Unvollständige DTO-Erweiterung parallel zu RunRecord. |
| **Fix** | `RunStatusDto`: `bool EnableDatAudit` + `bool EnableDatRename` ergänzen. `ToDto()`: Mapping ergänzen. |
| **Testabsicherung** | TGAP-46: `RunStatusDto_IncludesAllRunRecordBooleanFlags()` |

---

### BUG-57 · ConvertOnly + DryRun produziert Leer-Output ohne Warnung — ✅ erledigt (2026-03-30)

| Feld | Wert |
|------|------|
| **Schweregrad** | P2 — Sinnlose Option-Kombination wird still akzeptiert |
| **Dateien** | `src/RomCleanup.Infrastructure/Orchestration/RunOptionsBuilder.cs` L28-51 |
| **Reproduktion** | 1) `romulus -Roots "D:\Roms" -ConvertOnly` (DryRun default). 2) Kein Warning. ConvertOnly überspringt Dedupe, DryRun überspringt Conversion → Output zeigt 0 in allen Feldern. |
| **Erwartetes Verhalten** | `GetDryRunFeatureWarnings()` warnt: "ConvertOnly is enabled but conversion will be skipped in DryRun mode." |
| **Tatsächliches Verhalten** | `ConvertOnly` + DryRun wird nicht geprüft. Nur SortConsole, ConvertFormat und EnableDatRename werden gewarnt. |
| **Ursache** | `ConvertOnly` fehlt in der Warning-Liste. |
| **Fix** | In `GetDryRunFeatureWarnings()`: `if (options.ConvertOnly) warnings.Add("ConvertOnly is enabled but conversion will be skipped in DryRun mode. Use Mode=Move to apply.");` |
| **Testabsicherung** | TGAP-47: `DryRunWarnings_ConvertOnly_IsWarned()` |

---

### BUG-58 · API verwendet hardcoded Status-Strings statt RunConstants

| Feld | Wert |
|------|------|
| **Schweregrad** | P3 — Wartbarkeit / Konsistenzrisiko |
| **Dateien** | `src/RomCleanup.Api/RunManager.cs` L95-104 |
| **Reproduktion** | Status-Mapping in `ExecuteWithOrchestrator` verwendet `"completed"`, `"completed_with_errors"`, `"cancelled"`, `"failed"` als hardcoded Strings. `RunConstants` definiert `StatusOk="ok"`, `StatusBlocked="blocked"` etc. API remappt absichtlich (ok→completed, blocked→failed), aber ohne eigene benannte Konstanten. |
| **Erwartetes Verhalten** | API-Status-Strings sind als eigenes Konstanten-Set definiert (z.B. `ApiStatusCompleted = "completed"`). |
| **Tatsächliches Verhalten** | Magic Strings in switch-Expression. Gleiche Literale in `RunLifecycleManager.ExecuteRun()` L260-270 und SSE terminal event mapping. |
| **Ursache** | API führt eigene Status-Vokabeln ein (ok→completed, blocked→failed), aber ohne zentrale Definition. |
| **Fix** | Eigenes `ApiRunStatus`-Konstantenset im API-Projekt definieren. Alle Status-String-Literale ersetzen. |
| **Testabsicherung** | TGAP-48: `Api_StatusStrings_UseCentralConstants()` |

---

### BUG-59 · CLI DryRun JSON enthält Triple-Aliases für identische Metriken

| Feld | Wert |
|------|------|
| **Schweregrad** | P3 — API-Konsistenz / Consumer-Verwirrung |
| **Dateien** | `src/RomCleanup.CLI/CliOutputWriter.cs` L213-230 |
| **Reproduktion** | DryRun JSON enthält `Keep`, `Winners`, `Dupes`, `Losers`, `Duplicates` — wobei Keep=Winners und Dupes=Losers=Duplicates denselben Wert haben. API nutzt nur `Winners`/`Losers`. |
| **Erwartetes Verhalten** | Einheitliche Feldnamen über Entry Points. Aliase nur als dokumentierte Backward-Kompatibilität. |
| **Tatsächliches Verhalten** | 3 Aliase für 1 Metrik. Consumer, die `Dupes` verwenden, sehen andere Feldnamen als API-Consumer, die `Losers` verwenden. |
| **Ursache** | Historische Kompatibilität ohne Deprecation-Strategie. |
| **Fix** | In CliDryRunOutput die canonical Names (`Winners`, `Losers`) als Primärfeld markieren. Aliase mit `[Obsolete]` oder `[JsonPropertyName]` deprecaten. Langfristig entfernen. |
| **Testabsicherung** | TGAP-49: `Cli_DryRunJson_CanonicalFieldNames_MatchApi()` |

---

### BUG-60 · Artifact-Pfad-Divergenz: CLI collocated vs. API %APPDATA%-fixed

| Feld | Wert |
|------|------|
| **Schweregrad** | P3 — Architekturentscheidung, aber operationelles Risiko bei Service-Betrieb |
| **Dateien** | `src/RomCleanup.CLI/CliOptionsMapper.cs` L50-53, `src/RomCleanup.Api/RunLifecycleManager.cs` L235-237, `src/RomCleanup.Infrastructure/Paths/ArtifactPathResolver.cs`, `src/RomCleanup.Infrastructure/Audit/AuditSecurityPaths.cs` |
| **Reproduktion** | 1) CLI single-root: Audit landet neben ROM-Root (`D:\Roms\audits\`). 2) API: Audit landet immer in `%APPDATA%\RomCleanupRegionDedupe\audit\`. 3) Wenn API als Windows-Service unter anderem User läuft → `%APPDATA%` resolves zum Service-Account-Profil. |
| **Erwartetes Verhalten** | Einheitlicher Artifact-Lokalisierungsmechanismus oder dokumentierte Divergenz. |
| **Tatsächliches Verhalten** | CLI nutzt `ArtifactPathResolver.GetArtifactDirectory(roots)` (root-adjacent), API nutzt `AuditSecurityPaths.GetDefaultAuditDirectory()` (fixed %APPDATA%). Zwei verschiedene Audit-Pfade für identische fachliche Operationen. |
| **Ursache** | API wurde als Daemon-/Service-Modell designed (fester Pfad), CLI als User-Tool (root-relativ). |
| **Fix** | API sollte optional Root-basierte Artifact-Pfade unterstützen (via RunRequest.AuditPath). Alternativ: Divergenz dokumentieren. |
| **Testabsicherung** | TGAP-50: `Api_ArtifactPaths_DocumentedOrConfigurable()` |

---

### Korrekturnotiz zu BUG-12 (aus Bughunt #1)

BUG-12 beschreibt die API-OnlyGames-Validierung als "invertiert". Nach detaillierter Analyse ist die Logik **korrekt**:
- `!OnlyGames && !KeepUnknownWhenOnlyGames` → Error: "DropUnknown ohne GamesOnly ist semantisch ungültig"
- Der vorgeschlagene Fix `!OnlyGames && KeepUnknownWhenOnlyGames` wäre **falsch**: KeepUnknown=true ist Default → jeder Request ohne explizites OnlyGames würde rejected
- Der Check ist konsistent mit CLI (CliArgsParser L350) und RunOptionsBuilder.Validate()
- **Empfehlung:** BUG-12 als "kein Bug / false positive" markieren.

---

### Entry-Point-Divergenz-Matrix

| Aspekt | CLI | API | WPF | Konsistent? |
|--------|-----|-----|-----|-------------|
| PreferRegions Default | `RunConstants` (korrekt) | Hardcoded: JP↔WORLD vertauscht | `RunConstants` (korrekt) | **NEIN (BUG-52)** |
| EnableDatAudit propagiert | ✓ (via CliOptionsMapper) | ✗ (fehlt in TryCreateOrReuse) | ✓ (via ViewModelRunOptionsSource) | **NEIN (BUG-53)** |
| EnableDatRename propagiert | ✓ | ✗ | ✓ | **NEIN (BUG-53)** |
| OnlyGames Guard | CliArgsParser L350 | Program.cs L343 | ✗ KEINER | **NEIN (BUG-54)** |
| RunOptionsBuilder.Validate | Nicht aufgerufen | Nicht aufgerufen | Nicht aufgerufen | Toter Code |
| PreflightWarnings im Output | ✗ (nur stderr) | ✓ (ApiRunResult.PreflightWarnings) | ✓ (onProgress) | **NEIN (BUG-55)** |
| ConvertOnly+DryRun Warnung | ✗ | ✗ | ✗ | Fehlt überall (BUG-57) |
| Artifact-Pfade | Root-adjacent | %APPDATA%-fixed | Settings-basiert | Divergent (BUG-60) |
| DryRun JSON field naming | Keep/Winners, Dupes/Losers/Duplicates | Winners/Losers | N/A | Alias-Divergenz (BUG-59) |
| Status field name | `Status` | `OrchestratorStatus` | `Status` | Naming-Divergenz (BUG-58) |
| Structured Error in output | ✗ | ✓ (OperationError) | ✓ (ViewModel) | CLI-Lücke |
| PhaseMetrics | ✗ | ✓ | ✗ | API-only |
| Exit-Code-Semantik | 0/1/2/3 → documented | ExitCode im JSON | N/A | Konsistent |
| SSE Status ↔ RunRecord | — | ✓ (matching switch) | — | OK |
| Settings from %APPDATA% | ✓ (user-context) | ✗ (keine Settings geladen) | ✓ | OK (API ist self-contained) |

---

### Positiv-Befunde CLI/API (bestätigt ✓)

- [x] RunProjection als Single Source of Truth für KPIs über alle Entry Points
- [x] RunProjectionFactory.Create() zentral und kanalagnostisch
- [x] RunOptionsFactory → RunOptionsBuilder.Normalize() Pipeline identisch für CLI, API, WPF
- [x] IRunOptionsSource-Pattern sauber: 3 Implementierungen (CLI, API, WPF) ohne Schattenlogik
- [x] API Path-Traversal-Schutz: ValidatePathSecurity mit SafetyValidator, Reparse-Point-Check, Drive-Root-Block
- [x] API Rate-Limiting, API-Key-Auth mit FixedTimeEquals, Client-Binding-Isolation
- [x] SSE Event-Names sanitized gegen Injection (SanitizeSseEventName)
- [x] SSE Heartbeat gegen Proxy-Timeout (V2-H05)
- [x] SSE Terminal-Events konsistent mit RunRecord.Status
- [x] Correlation-ID Sanitization (nur printable ASCII, max 64 chars)
- [x] CLI Exit-Code-Normalisierung in dokumentierten Bereich [0-3]
- [x] CLI Ctrl+C zweistufig: grace cancel → force cancel
- [x] CLI JSONL Logging mit Rotation (JsonlLogRotation.Rotate)
- [x] API Emergency-Shutdown-Sidecar bei Timeout
- [x] Rollback-Endpoint: Default dryRun=true (Danger-Action Schutz)
- [x] Review-Queue: O(1) HashSet-Lookup für Pfad-Filter statt O(n) Contains

---

### Top 10 Fixes (priorisiert)

| # | Fix | Aufwand | Bugs |
|---|-----|---------|------|
| 1 | PreferRegions: `RunConstants.DefaultPreferRegions` in RunLifecycleManager verwenden | Klein | BUG-52 |
| 2 | EnableDatAudit/EnableDatRename in TryCreateOrReuse() propagieren | Klein | BUG-53 |
| 3 | RunOptionsFactory.Create() → Validate() nach Normalize() aufrufen | Klein | BUG-54 |
| 4 | CliDryRunOutput um PreflightWarnings erweitern | Klein | BUG-55 |
| 5 | RunStatusDto um EnableDatAudit/EnableDatRename ergänzen | Klein | BUG-56 |
| 6 | GetDryRunFeatureWarnings: ConvertOnly-Check ergänzen | Klein | BUG-57 |
| 7 | API-Status-Konstanten statt Magic Strings | Klein | BUG-58 |
| 8 | CLI DryRun Aliase deprecaten (Keep→Winners, Dupes→Losers) | Mittel | BUG-59 |
| 9 | BUG-12 als false positive schließen | Klein | BUG-12 |
| 10 | Artifact-Pfad-Divergenz dokumentieren oder konfigurierbar machen | Mittel | BUG-60 |

---

### Konsolidierte Test-Gap-Tabelle (CLI/API/Parität)

| Status | ID | Test | Bug | Prio |
|--------|-----|------|-----|------|
| [x] | TGAP-42 | `Api_DefaultPreferRegions_MatchRunConstants()` | BUG-52 | erledigt (2026-03-30) |
| [x] | TGAP-43 | `Api_EnableDatAudit_PropagatedToRunRecord()` | BUG-53 | erledigt (2026-03-30) |
| [x] | TGAP-44 | `RunOptionsFactory_InvalidOptions_ThrowsFromValidate()` | BUG-54 | erledigt (2026-03-30) |
| [x] | TGAP-45 | `Cli_DryRunJson_IncludesPreflightWarnings()` | BUG-55 | erledigt (2026-03-30) |
| [x] | TGAP-46 | `RunStatusDto_IncludesAllRunRecordBooleanFlags()` | BUG-56 | erledigt (2026-03-30) |
| [x] | TGAP-47 | `DryRunWarnings_ConvertOnly_IsWarned()` | BUG-57 | erledigt (2026-03-30) |
| [x] | TGAP-48 | `Api_StatusStrings_UseCentralConstants()` | BUG-58 | erledigt (2026-06-28) |
| [x] | TGAP-49 | `Cli_DryRunJson_CanonicalFieldNames_MatchApi()` | BUG-59 | erledigt (2026-06-28) |
| [x] | TGAP-50 | `Api_ArtifactPaths_DocumentedOrConfigurable()` | BUG-60 | erledigt (2026-06-28) |

---

## Bughunt #6 – Reports / Audit / Metrics / Sidecars / Rollback / Forensik

> Scope: ReportWriter, ReportSummary, RunProjection, RunOrchestrator, Move/Junk/Sort/DatRename/Convert Phasen, AuditCsvStore, AuditSigningService, ArchiveHashing, Set-Parser
> Datum: 2026-03-29
> Methode: Deep Code Reading mit Feld-zu-Feld Vergleich zwischen Report, Projection, API, CLI und WPF

### Executive Verdict

Die Pipeline ist in den sicherheitskritischen Grundmechaniken stabil: Write-Ahead-Audit bei Moves, HMAC-Verifikation mit Constant-Time-Check, deterministische Archiv-Hash-Reihenfolge und deterministische Set-Parser.
Es bestehen jedoch mehrere Vertrauens- und Forensikprobleme in der Ergebnisdarstellung: ein Report-ErrorCounter mit Doppelzaehlung, unvollstaendige Failure-Aggregation bei Set-Member-Moves, fehlende Verify-Counter-Propagation in den RunResult-Metriken und ein Sidecar-Fehlerpfad, der still weiterlaeuft.

### Kritische Forensik- und Vertrauensprobleme

- [x] BUG-61 (P1): ReportSummary.ErrorCount zaehlt Fehler doppelt
- [x] BUG-62 (P2): Set-Member-Move-Fehler werden nicht in FailCount gezaehlt
- [x] BUG-63 (P2): SavedBytes unterschlaegt Set-Member-Moves trotz MoveCount-Inkrement
- [x] BUG-64 (P2): Sidecar-Schreibfehler werden geschluckt (null-Return), keine harte Eskalation
- [x] BUG-65 (P2): ConvertVerifyPassed/Failed und LossyWarning bleiben in RunResult dauerhaft 0
- [x] BUG-66 (P2): Verify-Fails beeinflussen hasErrors/HealthScore nicht
- [ ] BUG-67 (P3): AuditCsvStore.Rollback reduziert Detailergebnis auf Pfadliste (Forensikverlust)
- [x] BUG-68 (P3): Report-Invariante wird fuer ConvertOnly/no-dedupe nicht geprueft

### Findings

#### BUG-61 - ReportSummary.ErrorCount doppelt gezaehlt

- Status: Erledigt am 2026-03-30

- Schweregrad: P1
- Impact: Report zeigt hoehere Fehlerzahl als API/CLI/Projection; KPI-Vertrauen bricht
- Betroffene Dateien:
  - src/RomCleanup.Infrastructure/Reporting/RunReportWriter.cs
  - src/RomCleanup.Infrastructure/Orchestration/RunProjection.cs
- Reproduktion:
  1) Run mit ConsoleSortFailed > 0 oder JunkFailCount > 0 ausfuehren.
  2) ReportSummary.ErrorCount mit RunProjection.FailCount vergleichen.
- Erwartet: ErrorCount entspricht derselben fachlichen Fehleraggregation wie FailCount.
- Tatsaechlich: ErrorCount = FailCount + JunkFailCount + ConsoleSortFailed, wobei JunkFailCount und ConsoleSortFailed bereits in FailCount enthalten sind.
- Ursache: additive Doppelaggregation in RunReportWriter.BuildSummary.
- Fix: ErrorCount = projection.FailCount setzen oder FailCount-Definition zentral als einzige Quelle verwenden.
- Testabsicherung:
  - [x] TGAP-51: ReportSummary_ErrorCount_EqualsProjectionFailCount()

#### BUG-62 - Set-Member-Move-Fails fehlen in FailCount

- Status: Erledigt am 2026-03-30

- Schweregrad: P2
- Impact: Partial Failure wird unterschaetzt; Exit/Status kann zu optimistisch sein
- Betroffene Datei:
  - src/RomCleanup.Infrastructure/Orchestration/MovePipelinePhase.cs
- Reproduktion:
  1) CUE/GDI/CCD Descriptor mit mindestens einem gesperrten Member verschieben.
  2) MOVE_FAILED fuer SET_MEMBER erscheint im Audit.
  3) MovePhaseResult.FailCount pruefen.
- Erwartet: jeder fehlgeschlagene Set-Member-Move erhoeht FailCount.
- Tatsaechlich: FailCount erhoeht sich nur im Descriptor-Fehlerpfad, nicht im Set-Member-Fehlerpfad.
- Ursache: fehlendes failCount++ im else-Zweig von memberActual == null.
- Fix: Set-Member-Fehler in FailCount aggregieren.
- Testabsicherung:
  - [x] TGAP-52: Move_SetMemberFailure_IncrementsFailCount()

#### BUG-63 - SavedBytes inkonsistent zu MoveCount bei Set-Members

- Status: Erledigt am 2026-03-30

- Schweregrad: P2
- Impact: SavedBytes KPI ist systematisch zu niedrig; KPI-Drift in Dashboards/Reports
- Betroffene Datei:
  - src/RomCleanup.Infrastructure/Orchestration/MovePipelinePhase.cs
- Reproduktion:
  1) Descriptor mit mehreren Set-Membern verschieben.
  2) moveCount steigt fuer Descriptor + Member.
  3) savedBytes steigt nur um loser.SizeBytes.
- Erwartet: SavedBytes beinhaltet alle erfolgreich verschobenen Dateien, die in MoveCount enthalten sind.
- Tatsaechlich: Set-Member-Groessen werden nicht addiert.
- Ursache: fehlende Byte-Aggregation im Set-Member-Success-Pfad.
- Fix: Dateigroesse der erfolgreich verschobenen Member addieren oder MoveCount semantisch auf primaries begrenzen.
- Testabsicherung:
  - [x] TGAP-53: Move_SetMembers_AreCountedInSavedBytes()

#### BUG-64 - Sidecar-Schreibfehler laufen still weiter

- Status: Erledigt am 2026-03-30

- Schweregrad: P2
- Impact: Rollback-Trust sinkt; Run kann ohne gueltigen Sidecar als erfolgreich erscheinen
- Betroffene Dateien:
  - src/RomCleanup.Infrastructure/Audit/AuditSigningService.cs
  - src/RomCleanup.Infrastructure/Audit/AuditCsvStore.cs
- Reproduktion:
  1) Sidecar-Write-Fehler forcieren (z. B. Zugriffsproblem auf Zielpfad).
  2) WriteMetadataSidecar gibt null zurueck, Pipeline laeuft weiter.
- Erwartet: Sidecar-Fehler bei Move/Audit-kritischen Pfaden wird als harter Fehler propagiert.
- Tatsaechlich: Exception wird geloggt und in null umgewandelt.
- Ursache: catch-all in WriteMetadataSidecar mit return null.
- Fix: Fehler hochwerfen oder mindestens Status auf completed_with_errors erzwingen und expliziten Forensik-Fehler markieren.
- Testabsicherung:
  - [x] TGAP-54: AuditSidecarWriteFailure_MarksRunAsError()

#### BUG-65 - Verify-Metriken werden nicht in RunResultBuilder befuellt

- Status: Erledigt am 2026-03-30

- Schweregrad: P2
- Impact: API/CLI/WPF zeigen ConvertVerifyPassedCount, ConvertVerifyFailedCount, ConvertLossyWarningCount faktisch immer 0
- Betroffene Dateien:
  - src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs
  - src/RomCleanup.Infrastructure/Orchestration/RunResultBuilder.cs
- Reproduktion:
  1) Conversion mit validierbaren Erfolgen/Fehlern ausfuehren.
  2) Projection-Felder fuer Verify/Lossy pruefen.
- Erwartet: Counter aus ConversionResult-Liste berechnet und in Builder gesetzt.
- Tatsaechlich: ApplyConversionReport setzt nur ConvertReviewCount/ConvertSavedBytes/ConversionReport, aber keine Verify/Lossy-Counter.
- Ursache: unvollstaendige Aggregation in ApplyConversionReport.
- Fix: in ApplyConversionReport Counter fuer Verified/VerifyFailed/LossyWarning berechnen und auf Builder schreiben.
- Testabsicherung:
  - [x] TGAP-55: ConversionVerifyAndLossyCounters_AreProjected()

#### BUG-66 - Verify-Fails zaehlen weder fuer hasErrors noch fuer HealthScore

- Status: Erledigt am 2026-03-30

- Schweregrad: P2
- Impact: fehlgeschlagene Verifikation kann als zu gesundes Ergebnis erscheinen
- Betroffene Dateien:
  - src/RomCleanup.Infrastructure/Orchestration/RunProjection.cs
  - src/RomCleanup.Infrastructure/Orchestration/RunOrchestrator.cs
- Reproduktion:
  1) Run mit VerifyFailed ohne ConvertError provozieren.
  2) hasErrors/FailCount/HealthScore beobachten.
- Erwartet: VerifyFailed beeinflusst mindestens FailCount oder separaten Error-Pfad.
- Tatsaechlich: hasErrors basiert auf ConvertErrorCount und weiteren Phasen-Fails; VerifyFailed geht nicht ein.
- Ursache: FailCount-Formel ohne ConvertVerifyFailedCount und hasErrors ohne VerifyFailed-Pruefung.
- Fix: ConvertVerifyFailedCount in FailCount und hasErrors integrieren oder separaten VerificationErrorCount mit Statuswirkung einfuehren.
- Testabsicherung:
  - [x] TGAP-56: VerifyFailed_TriggersCompletedWithErrorsAndHealthPenalty()

#### BUG-67 - AuditCsvStore.Rollback verliert Detailergebnis

- Schweregrad: P3
- Impact: UI/CLI koennen keine differenzierte Forensik (SkippedUnsafe, Collision, Failed) ausgeben
- Betroffene Datei:
  - src/RomCleanup.Infrastructure/Audit/AuditCsvStore.cs
- Reproduktion:
  1) Rollback mit gemischten Outcomes ausfuehren.
  2) Rueckgabe von IAuditStore.Rollback pruefen.
- Erwartet: detaillierte Rueckgabe oder separater Detailzugriff.
- Tatsaechlich: nur RestoredPaths/PlannedPaths werden weitergereicht.
- Ursache: Port-Interface liefert nur Pfadliste.
- Fix: Vertragsupgrade auf AuditRollbackResult oder zusaetzliche Detail-API.
- Testabsicherung:
  - [ ] TGAP-57: RollbackApi_ExposesDetailedOutcomeCounters()

#### BUG-68 - Report-Invariante wird fuer no-dedupe/convert-only nicht geprueft

- Status: Erledigt am 2026-03-30

- Schweregrad: P3
- Impact: Accounting-Drift kann in ConvertOnly/Partial-Szenarien unentdeckt bleiben
- Betroffene Datei:
  - src/RomCleanup.Infrastructure/Reporting/RunReportWriter.cs
- Reproduktion:
  1) Run ohne DedupeGroups (ConvertOnly oder frueher Abbruch) erzeugen.
  2) BuildSummary-Invariant pruefen.
- Erwartet: Invariante fuer alle relevanten Runs mit TotalFiles > 0.
- Tatsaechlich: Invariant-Check nur wenn result.DedupeGroups.Count > 0.
- Ursache: zu enger Guard im Summary-Build.
- Fix: Guard auf projection.TotalFiles > 0 umstellen und ggf. statusbewusste Ausnahme fuer fruehen Cancel dokumentieren.
- Testabsicherung:
  - [x] TGAP-58: ReportInvariant_AlsoValidatesConvertOnlyAndPartial()

### KPI- und Audit-Divergenzen

| Bereich | Projection/API/CLI | Report/WPF | Divergenz |
|---|---|---|---|
| Fehleraggregat | FailCount (zentral) | ErrorCount (eigene Formel) | Doppelzaehlung von JunkFail/ConsoleSortFailed (BUG-61) |
| Verify-Counter | Felder vorhanden | ReportSummary hat keine Verify-Felder | Sichtbarkeit fehlt im Report (Transparenzluecke) |
| Verify in Status | hasErrors ignoriert VerifyFailed | HealthScore nutzt FailCount | VerifyFailed kann ohne Statuswirkung bleiben (BUG-66) |
| Move KPI | moveCount inkl. Set-Member | savedBytes ohne Set-Member | interne KPI-Inkonsistenz (BUG-63) |
| Rollback Ergebnis | SigningService liefert Detailmodell | AuditStore-Port reduziert auf Pfadliste | Forensik-Details gehen verloren (BUG-67) |

### Top 10 Fixes (priorisiert)

| # | Fix | Aufwand | Bugs |
|---|-----|---------|------|
| 1 | ErrorCount im Report auf projection.FailCount normalisieren | Klein | BUG-61 |
| 2 | Set-Member-Fails in MovePhaseResult.FailCount aufnehmen | Klein | BUG-62 |
| 3 | Set-Member-Bytes in SavedBytes aggregieren (oder MoveCount-Semantik trennen) | Mittel | BUG-63 |
| 4 | Sidecar-Schreibfehler als Statusfehler propagieren | Mittel | BUG-64 |
| 5 | ApplyConversionReport um Verify/Lossy-Counter erweitern | Klein | BUG-65 |
| 6 | VerifyFailed in hasErrors und FailCount integrieren | Klein | BUG-66 |
| 7 | IAuditStore-Vertrag fuer detaillierte Rollback-Ergebnisse erweitern | Mittel | BUG-67 |
| 8 | Report-Invariant fuer ConvertOnly/no-dedupe aktivieren | Klein | BUG-68 |
| 9 | ReportSummary um ConvertVerifyPassed/Failed und LossyWarning erweitern | Klein | BUG-65, BUG-66 |
| 10 | KPI-Regressionstests fuer Kanal-Paritaet (Report/API/CLI/WPF) ergaenzen | Mittel | BUG-61, BUG-63, BUG-65 |

### Positiv-Befunde (Bughunt #6)

- [x] 7z/ZIP Hashing nutzt stabile Sortierung fuer deterministische Reihenfolge
- [x] CUE-Parser arbeitet deterministisch (lineares Parsing + deduplizierte Pfadliste)
- [x] hasErrors beruecksichtigt Move, JunkMove, DatRename und ConsoleSort Failures
- [x] ConsoleSorter schreibt Audit-Rows fuer Sort/Review/Junk-Routing
- [x] JunkRemoval und DatRename schreiben Audit-Rows und aggregieren Failures

---

## Bughunt #7 – Safety / FileSystem / Pfadlogik / Security

**Datum:** 2026-03-29
**Fokus:** Path Traversal, ADS, Extended-Length Prefix, Reparse Points, Zip-Slip, Zip-Bomb, DTD/XML Parser, Root Containment, Trailing Dot/Windows-Normalization, Locked Files/Read-Only, Unsafe Rollback, Temp File Handling, External Tool Argument Handling, Timeout/Retry/Cleanup, Partial Cleanup, Cross-Volume Move, Unsafe Delete, Hidden Data Loss Paths

### Executive Verdict

Die Security-Infrastruktur von Romulus ist **insgesamt solide und production-grade**. SafetyValidator, FileSystemAdapter und ToolRunnerAdapter bilden ein starkes Fundament mit Defense-in-Depth: ADS-Blocking, Reparse-Point-Erkennung, Trailing-Dot/Space-Abwehr, TOCTOU-sichere Collision-Behandlung, XXE-Schutz, Zip-Slip/Zip-Bomb-Protection, Tool-Hash-Verifizierung und Process-Tree-Kill bei Timeout.

Es wurden **6 Befunde** identifiziert (2× P2, 2× P2, 2× P3). Keiner ist ein unmittelbarer P1-Release-Blocker, aber BUG-69 (DatRenamePolicy) und BUG-70 (Extraction Dir) sollten vor Release gefixt werden, da sie Defense-in-Depth-Luecken darstellen.

### Kritische Sicherheitsrisiken

| ID | Severity | Bereich | Kurztext |
|----|----------|---------|----------|
| BUG-69 | P2 | DatRenamePolicy | IsSafeFileName() prueft nicht auf Trailing Dots/Spaces |
| BUG-70 | P2 | FormatConverterAdapter | Extraction Dir im Source-Verzeichnis statt System-Temp |
| BUG-71 | P2 | DatSourceService | Stale Temp-Dateien (dat_download_*, dat_extract_*) nicht bereinigt |
| BUG-72 | P3 | AuditSigningService | Path.GetFullPath auf CSV-Daten ohne Exception-Handling |
| BUG-73 | P3 | FileSystemAdapter | Kein Cross-Volume Move Fallback (Copy+Delete) |
| BUG-74 | P2 | AuditSigningService | HMAC Key Path ohne ADS/Traversal-Validierung |

---

### BUG-69: DatRenamePolicy.IsSafeFileName() – Fehlende Trailing Dot/Space Pruefung

- **Schweregrad:** P2
- **Impact:** Windows-Pfad-Normalisierung kann Defense-in-Depth unterlaufen. Dateiname mit Trailing Dots/Spaces passiert Policy-Check, Windows strippt diese still → tatsaechlicher Dateiname weicht vom validierten ab.
- **Betroffene Datei(en):** [DatRenamePolicy.cs](src/RomCleanup.Core/Audit/DatRenamePolicy.cs#L71-L87)
- **Reproduktion:** DAT-Game-Name `"Super Mario Bros. "` (trailing space) oder `"Game..."` (trailing dots) → `IsSafeFileName()` gibt `true` zurueck → Windows erstellt Datei ohne Trailing Chars → Name weicht ab.
- **Erwartetes Verhalten:** `IsSafeFileName()` muss Dateinamen mit Trailing Dots oder Spaces ablehnen.
- **Tatsaechliches Verhalten:** `Path.GetInvalidFileNameChars()` enthaelt auf Windows weder `.` noch ` ` — kein Check auf Trailing-Position.
- **Ursache:** `GetInvalidFileNameChars()` prueft nur komplett verbotene Zeichen, nicht positionsabhaengige Windows-Normalisierung. SafetyValidator.NormalizePath (SEC-PATH-02) und FileSystemAdapter.ResolveChildPathWithinRoot fangen dies als Secondary Defense ab, aber die Policy-Schicht selbst hat die Luecke.
- **Fix:** In `IsSafeFileName()` pruefen: `if (fileName != fileName.TrimEnd('.', ' ')) return false;`
- **Testabsicherung:** Unit-Test mit Trailing Dots, Trailing Spaces, und Kombination. Invarianten-Test dass IsSafeFileName und ResolveChildPathWithinRoot konsistent ablehnen.

---

### BUG-70: FormatConverterAdapter – Extraction Dir im Source-Verzeichnis

- **Schweregrad:** P2
- **Impact:** Archive-Extraction erstellt temp Directory neben der Source-Datei statt in System-Temp. Schlaegt fehl auf Read-Only-Medien oder Verzeichnissen mit restriktiven Permissions. Stale Extraction Dirs werden nicht von CleanupStaleTempDirs() erfasst.
- **Betroffene Datei(en):** [FormatConverterAdapter.cs](src/RomCleanup.Infrastructure/Conversion/FormatConverterAdapter.cs#L497)
- **Reproduktion:** ROM-Archiv auf schreibgeschuetztem Netzlaufwerk → `ConvertArchiveToChdman()` → `Directory.CreateDirectory(extractDir)` schlaegt fehl → Conversion scheitert ohne klare Fehlermeldung.
- **Erwartetes Verhalten:** Extraction Dir in System-Temp (`Path.GetTempPath()`) mit Praeffix fuer CleanupStaleTempDirs, oder im konfigurierten Temp-Root.
- **Tatsaechliches Verhalten:** `var extractDir = Path.Combine(dir, $"_extract_{baseName}_{Guid.NewGuid():N}")` — dir ist das Source-Verzeichnis.
- **Ursache:** Design-Entscheidung aus Einfachheit — Source-Dir ist immer bekannt. Aber: keine Pruefung ob beschreibbar, und kein Cleanup-Pattern in `CleanupStaleTempDirs()`.
- **Fix:** Entweder (a) Extraction nach `Path.GetTempPath()` mit Prefix `romcleanup_extract_` und CleanupStaleTempDirs erweitern, oder (b) Write-Check vor CreateDirectory mit Fallback auf Temp.
- **Testabsicherung:** Test mit Read-Only Source Dir. Test dass stale `_extract_*` Dirs nach Crash bereinigt werden.

---

### BUG-71: DatSourceService – Stale Temp-Dateien nicht bereinigt

- **Schweregrad:** P2
- **Impact:** Nach Crash oder Abbruch bleiben `dat_download_*.zip` und `dat_extract_*` Dateien/Verzeichnisse in System-Temp liegen. Bei wiederholten Abstuerzen waechst Temp-Verbrauch unbegrenzt.
- **Betroffene Datei(en):** [DatSourceService.cs](src/RomCleanup.Infrastructure/Dat/DatSourceService.cs), [ArchiveHashService.cs](src/RomCleanup.Infrastructure/Hashing/ArchiveHashService.cs#L44-L57)
- **Reproduktion:** DAT-Download starten → Prozess waehrend Download/Extraction killen → Temp-Dateien bleiben → bei naechstem Start kein Cleanup.
- **Erwartetes Verhalten:** `CleanupStaleTempDirs()` (oder aequivalent) bereinigt auch `dat_download_*` und `dat_extract_*` Patterns.
- **Tatsaechliches Verhalten:** Nur `romcleanup_7z_*` wird bereinigt (ArchiveHashService), die beiden DatSourceService-Patterns nicht.
- **Ursache:** CleanupStaleTempDirs wurde fuer ArchiveHashService implementiert, aber DatSourceService Temp-Patterns nicht einbezogen.
- **Fix:** Entweder (a) DatSourceService-Prefixes auf `romcleanup_dat_*` vereinheitlichen und in CleanupStaleTempDirs aufnehmen, oder (b) separate Cleanup-Methode in DatSourceService mit Aufruf beim Start.
- **Testabsicherung:** Test: stale `dat_download_*` und `dat_extract_*` Dirs/Files in Temp anlegen → Cleanup aufrufen → pruefen dass bereinigt.

---

### BUG-72: AuditSigningService Rollback – Path.GetFullPath ohne Exception-Handling

- **Schweregrad:** P3
- **Impact:** Wenn eine Audit-CSV-Zeile einen leeren oder syntaktisch ungueltigen Pfad enthaelt, wirft `Path.GetFullPath()` eine `ArgumentException` oder `NotSupportedException`. Diese Exception ist nicht gefangen → der gesamte Rollback-Loop bricht ab, weitere gueltige Eintraege werden nicht verarbeitet.
- **Betroffene Datei(en):** [AuditSigningService.cs](src/RomCleanup.Infrastructure/Audit/AuditSigningService.cs#L337-L338)
- **Reproduktion:** Audit-CSV mit leerem NewPath-Feld oder Pfad mit illegalen Zeichen → Rollback → `Path.GetFullPath("")` wirft `ArgumentException` → Rollback terminiert.
- **Erwartetes Verhalten:** Malformed Pfade sollten per try/catch uebersprungen und als `failed++` / `skippedUnsafe++` gezaehlt werden, ohne den Rest des Rollbacks zu stoppen.
- **Tatsaechliches Verhalten:** Unhandled Exception bricht die gesamte `foreach`-Schleife ab.
- **Ursache:** HMAC-Verifizierung garantiert CSV-Integritaet, daher wurde der Edge Case (korrupte/manipulierte CSV trotz HMAC) nicht defensiv behandelt.
- **Fix:** `try { ... Path.GetFullPath ... } catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { failed++; skippedUnsafe++; _log?.Invoke(...); continue; }`
- **Testabsicherung:** Rollback-Test mit leerem Pfad, Pfad mit illegalen Zeichen, Pfad mit nur Spaces. Pruefen dass restliche Eintraege trotzdem verarbeitet werden.

---

### BUG-73: FileSystemAdapter – Kein Cross-Volume Move Fallback

- **Schweregrad:** P3
- **Impact:** `File.Move()` in .NET wirft `IOException` wenn Source und Destination auf verschiedenen Laufwerken liegen. `MoveItemSafely` faengt diese IOException und gibt `null` zurueck (behandelt wie locked file). Trash auf anderem Volume als Source fuehrt zu stillem Fehlschlag aller Moves.
- **Betroffene Datei(en):** [FileSystemAdapter.cs](src/RomCleanup.Infrastructure/FileSystem/FileSystemAdapter.cs) (MoveItemSafely), alle Pipeline-Phasen die MoveItemSafely nutzen
- **Reproduktion:** Source auf `D:\ROMs`, TrashRoot auf `E:\Trash` → jeder Move gibt `null` zurueck → alle Dateien bleiben liegen → Run meldet Failures aber Ursache ist unklar.
- **Erwartetes Verhalten:** Cross-Volume Move mit Copy+Delete Fallback, oder klare Vorab-Pruefung mit Fehlermeldung.
- **Tatsaechliches Verhalten:** `IOException` bei `File.Move` wenn Source noch existiert → return null (same path as locked file).
- **Ursache:** .NET `File.Move` unterstuetzt kein Cross-Volume nativ. Der IOException-Catch unterscheidet nicht zwischen locked file und cross-volume.
- **Fix:** Entweder (a) Copy+Delete Fallback nach IOException wenn Source-Volume != Dest-Volume, oder (b) Vorab-Pruefung `Path.GetPathRoot(source) != Path.GetPathRoot(dest)` mit explizitem Fehler/Warnung. Variante (b) ist sicherer (kein partielles Copy-Risiko).
- **Testabsicherung:** Integration-Test mit Mock-FileSystem der IOException bei Cross-Volume wirft. Unit-Test fuer Volume-Root-Vergleich.

---

### BUG-74: AuditSigningService – HMAC Key Path ohne Traversal/ADS Validierung

- **Schweregrad:** P2
- **Impact:** `_keyFilePath` wird direkt in `File.Exists`, `File.ReadAllText`, `File.WriteAllText` genutzt ohne vorherige NormalizePath- oder ADS-Pruefung. Wenn der Key-Pfad ueber Settings konfigurierbar wird, koennte ein manipulierter Pfad (z.B. mit ADS oder Traversal) den HMAC-Key an beliebiger Stelle lesen/schreiben.
- **Betroffene Datei(en):** [AuditSigningService.cs](src/RomCleanup.Infrastructure/Audit/AuditSigningService.cs#L48-L72)
- **Reproduktion:** Key-Pfad auf `C:\Users\victim\secret:ads_stream` oder `..\..\Windows\key.txt` setzen → Key wird an unerwarteter Stelle geschrieben.
- **Erwartetes Verhalten:** Key-Pfad durch SafetyValidator.NormalizePath validieren. ADS und Traversal ablehnen.
- **Tatsaechliches Verhalten:** Pfad wird direkt genutzt. Aktuell kommt der Pfad aus `AuditSecurityPaths.GetDefaultSigningKeyPath()` (sicher), aber keine Validierung falls Pfadquelle sich aendert.
- **Ursache:** Defense-in-Depth-Luecke — aktuell sicher durch feste Pfadquelle, aber nicht abgesichert gegen zukuenftige Konfigurierbarkeit.
- **Fix:** `_keyFilePath = SafetyValidator.NormalizePath(keyFilePath) ?? throw new ArgumentException("Invalid key file path");` im Konstruktor.
- **Testabsicherung:** Test mit ADS-Pfad, Traversal-Pfad, und normalem Pfad. Pruefen dass ADS/Traversal abgelehnt wird.

---

### TGAP-Eintraege (Bughunt #7)

| ID | Bug-Ref | Beschreibung | Status |
|----|---------|-------------|--------|
| TGAP-59 | BUG-69 | DatRenamePolicy.IsSafeFileName trailing dot/space check ergaenzen | offen |
| TGAP-60 | BUG-70 | FormatConverterAdapter Extraction Dir nach System-Temp verlagern | offen |
| TGAP-61 | BUG-71 | Stale Temp Cleanup fuer dat_download_*/dat_extract_* Patterns | offen |
| TGAP-62 | BUG-72 | Rollback Path.GetFullPath Exception-Handling ergaenzen | offen |
| TGAP-63 | BUG-73 | Cross-Volume Move Vorab-Pruefung oder Fallback | offen |
| TGAP-64 | BUG-74 | HMAC Key Path durch NormalizePath validieren | offen |

### Datenverlust- und Security-Risiken

| Risiko | Betroffene Bugs | Bewertung |
|--------|----------------|-----------|
| Path-Normalisierung Bypass | BUG-69 | Mitigiert durch FileSystemAdapter Secondary Defense, aber Policy-Layer sollte first-line sein |
| Stale Temp Accumulation | BUG-70, BUG-71 | Kein Datenverlust, aber Disk-Space-Leak nach Abstuerzen |
| Rollback Abort | BUG-72 | Partieller Rollback bei korrupter CSV — restliche Eintraege verloren |
| Stille Move-Failures | BUG-73 | Dateien bleiben liegen statt verschoben — kein Verlust, aber falscher Status |
| Key Path Manipulation | BUG-74 | Aktuell mitigiert durch feste Pfadquelle — Risiko bei zukuenftiger Konfigurierbarkeit |

### Top 10 Fixes (priorisiert)

1. **BUG-69** – `DatRenamePolicy.IsSafeFileName()`: Trailing dot/space Check ergaenzen
2. **BUG-74** – `AuditSigningService`: Key Path durch NormalizePath validieren
3. **BUG-72** – `AuditSigningService.Rollback`: try/catch um Path.GetFullPath
4. **BUG-71** – `CleanupStaleTempDirs`: `dat_download_*` und `dat_extract_*` Patterns aufnehmen
5. **BUG-70** – `FormatConverterAdapter`: Extraction nach System-Temp mit Cleanup-Pattern
6. **BUG-73** – `FileSystemAdapter`: Cross-Volume Vorab-Pruefung mit klarer Fehlermeldung
7. Extraction-Dir Cleanup-Pattern (`_extract_*`) in stale cleanup aufnehmen (Teil von BUG-70)
8. Rollback robuster machen: jede CSV-Zeile einzeln absichern (Teil von BUG-72)
9. MoveItemSafely: IOException-Logging verbessern um Cross-Volume vs Locked zu unterscheiden
10. DatSourceService Temp-Prefixes vereinheitlichen auf `romcleanup_dat_*` (Teil von BUG-71)

### Positiv-Befunde (Bughunt #7)

- [x] SafetyValidator.NormalizePath: Blockt Extended-Length (\\\?\, \\.\), ADS, Trailing Dots/Spaces in Segmenten
- [x] FileSystemAdapter.ResolveChildPathWithinRoot: SEC-PATH-01 (ADS), SEC-PATH-02 (Trailing), SEC-PATH-03 (Reserved Names), Root Containment, Reparse Ancestry Check
- [x] FileSystemAdapter.MoveItemSafely: Traversal-Blocking, ADS-Blocking, Reparse-Check, NFC-Normalisierung, TOCTOU-sichere __DUP Collision
- [x] FileSystemAdapter.DeleteFile: Reparse-Point-Blocking, ReadOnly-Clearing vor Delete
- [x] FileSystemAdapter.GetFilesSafe: Iterative DFS, Visited-Set gegen Zyklen, Reparse-Dirs/Files uebersprungen
- [x] ToolRunnerAdapter: ArgumentList.Add (kein Shell Injection), SHA256 Hash-Verifizierung mit PLACEHOLDER-Rejection, Timeout mit Process-Tree-Kill, Async stdout/stderr gegen 4KB-Deadlock
- [x] DatRepositoryAdapter: DtdProcessing.Prohibit + XmlResolver=null (XXE-Schutz), Fallback auf Ignore, 100MB Limit
- [x] DatSourceService: HTTPS-Enforcement, 50MB Download-Limit, HTML-Detection, Zip-Slip mit Separator-Guard, SHA256-Verifizierung
- [x] ArchiveHashService: Zip-Slip (AreEntryPathsSafe + Post-Extraction Validation), Reparse-Check, Randomized Temp Dir, 500MB Limit
- [x] FormatConverterAdapter: Zip-Bomb-Protection (Ratio 50x, Count 10K, Size 10GB), Per-Entry Zip-Slip, CleanupPartialOutput auf allen Fehlerpfaden, Multi-CUE Atomic Rollback
- [x] AuditSigningService: HMAC-SHA256, Constant-Time Comparison, Atomic Key-Persist (Temp+Rename), ACL Restriction, Dry-Run Default, Reverse-Order Rollback
- [x] AuditCsvParser: OWASP CSV Injection Prevention (Prefix-Stripping fuer =+@-)
- [x] ConsoleSorter: Atomic Set Moves mit Rollback bei Partial Failure, Whitelist Console Key Regex
- [x] API Program.cs: Loopback-Only Binding, Rate Limiting, Constant-Time API Key, Comprehensive Input Validation, SSE Sanitization, CORS Validation
- [x] Report-Generation: CSP Nonce, HTML Encoding, CSV Injection Prevention
- [x] PipelinePhaseHelpers: Separator-Guards bei FindRootForPath, Reparse-Check vor Source-Trash

---

## Bughunt #8 – Tool-Katalog / Sichtbare Features

**Scope:** Alle 41 sichtbaren Tool-Items im GUI Tool-Katalog, 73 FeatureCommandKeys, 8 FeatureCommandService-Partials, 8 FeatureService-Partials, i18n-Keys (en/de), ui-lookups.json, CLI/API Paritaet.

**Suchfelder:**
1. Sichtbare Kachel ohne echte Funktion
2. Stub / Coming Soon
3. Falscher Name
4. Redundantes Tool
5. Gebrochener Handler
6. Tool-Registrierung ohne saubere Integration
7. i18n / Pinned / Lookup Ghosts
8. Tool tut nicht was der Name verspricht
9. Report-Starter ohne echten Mehrwert
10. Kaputtes oder irrefuehrend sichtbares Tool
11. Tool sollte Unter-Funktion sein
12. CommandPalette findet Tool nicht
13. Sichtbare Kachel nicht release-ready

### Executive Verdict

**Keine Stubs gefunden.** Alle 41 Tool-Items haben echte Handler mit realer Funktionalitaet. Die FeatureService-Backing-Methoden sind vollstaendig implementiert. i18n-Keys (en.json + de.json) sind lueckenlos. CLI und API sind korrekt getrennt und haben keine Tool-Katalog-Duplikation.

**9 echte Bugs gefunden (BUG-75..BUG-83).** Schwerpunkte:
- **RequiresRunResult-Mismatches**: Tool ist gesperrt obwohl es keinen Run braucht (oder umgekehrt)
- **Hardcoded German Strings**: 12+ Stellen umgehen das i18n-System komplett
- **Irreführende Namen**: 3 Tools versprechen Aktionen, liefern aber nur Reports
- **HashDatabaseExport ohne Hashes**: Kern-Feature-Versprechen wird nicht eingeloest

Release-kritisch: BUG-75 (Feature kuenstlich gesperrt), BUG-79 (i18n broken fuer Englisch), BUG-81 (Export liefert nicht was der Name verspricht).

### Kritische Tool-/Feature-Probleme

| Bug-ID | Schwere | Tool | Problem |
|--------|---------|------|---------|
| BUG-75 | P2 | HeaderAnalysis | RequiresRunResult=true aber Handler braucht keinen Run → Tool unnoetig gesperrt |
| BUG-76 | P3 | ConversionPipeline | RequiresRunResult=false aber Handler prueft LastCandidates → sieht klickbar aus, zeigt sofort Fehler |
| BUG-77 | P3 | CommandPalette, ApiServer, Accessibility, SystemTray | Conditional Registration wenn IWindowHost null → sichtbar aber Dead Click |
| BUG-78 | P3 UX | ExportCollection | InputBox "1/2/3" statt Dropdown; hardcoded German auf Fehleingabe |
| BUG-79 | P2 | Alle Handler-Partials | 12+ hardcoded German Strings umgehen _vm.Loc[] i18n System |
| BUG-80 | P3 UX | ConversionPipeline | Name "Pipeline" suggeriert Aktion, ist aber nur Schaetzung/Info-Dialog |
| BUG-81 | P2 | HashDatabaseExport | Exportiert Metadaten (GameKey, Region, Size) aber KEINE Hashes (Hash, HeaderlessHash existieren auf RomCandidate) |
| BUG-82 | P3 | Alle 41 Tools | IsPlanned=false hardcoded fuer alle Tools → Dead Code Path, XAML-Binding nutzlos |
| BUG-83 | P3 UX | Quarantine | Name suggeriert Quarantaene-Aktion, zeigt aber nur Read-Only Kandidatenliste |

---

### BUG-75: HeaderAnalysis – RequiresRunResult=true sperrt Tool unnoetig

- **Schweregrad:** P2
- **Impact:** HeaderAnalysis ist in der Tool-Kachel als `RequiresRunResult=true` markiert. Das Tool wird erst nach einem abgeschlossenen Run freigegeben (IsLocked=true vorher). Aber der Handler oeffnet einen Datei-Browser und ruft `FeatureService.AnalyzeHeader(path)` auf — komplett unabhaengig von LastCandidates oder LastDedupeGroups. Nutzer koennen das Tool nicht verwenden, bevor sie einen kompletten Pipeline-Run starten, obwohl es standalone funktioniert.
- **Betroffene Datei(en):** [ToolsViewModel.cs](src/RomCleanup.UI.Wpf/ViewModels/ToolsViewModel.cs#L142), [FeatureCommandService.Analysis.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Analysis.cs#L79-L89)
- **Reproduktion:** GUI starten → Tool-Katalog → "Header-Analyse" Kachel → gesperrt/ausgegraut → erst nach Run nutzbar.
- **Erwartetes Verhalten:** `RequiresRunResult = false` — Tool ist jederzeit klickbar, oeffnet Datei-Browser, analysiert Header.
- **Tatsaechliches Verhalten:** Tool erst nach Run freigegeben, obwohl Handler keine Run-Daten braucht.
- **Ursache:** Initiale Katalog-Definition hat `true` statt `false` fuer HeaderAnalysis.
- **Fix:** In ToolsViewModel.cs InitToolItems(): `("HeaderAnalysis", "Analysis", "\xE9D9", true)` aendern zu `("HeaderAnalysis", "Analysis", "\xE9D9", false)`.
- **Testabsicherung:** Unit-Test: HeaderAnalysis ToolItem hat `RequiresRunResult == false`. Regression-Test: HeaderAnalysis Handler funktioniert ohne vorherigen Run.

---

### BUG-76: ConversionPipeline – RequiresRunResult mismatch (false aber braucht Daten)

- **Schweregrad:** P3
- **Impact:** ConversionPipeline ist mit `RequiresRunResult=false` markiert — das Tool erscheint klickbar bevor ein Run stattfand. Aber der Handler prueft sofort `_vm.LastCandidates.Count == 0` und zeigt "Erst einen Lauf starten." wenn keine Daten vorhanden. Nutzer sehen ein klickbares Tool, das sofort eine Fehlermeldung wirft.
- **Betroffene Datei(en):** [ToolsViewModel.cs](src/RomCleanup.UI.Wpf/ViewModels/ToolsViewModel.cs#L147), [FeatureCommandService.Conversion.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Conversion.cs#L18-L27)
- **Reproduktion:** GUI starten → Tool-Katalog → "Konvertierungs-Pipeline" klicken → Fehlerdialog "Erst einen Lauf starten."
- **Erwartetes Verhalten:** Entweder (a) `RequiresRunResult = true` setzen → Tool korrekt gesperrt, oder (b) Handler ohne Run-Daten nutzbar machen (z.B. Conversion-Registry-Info anzeigen).
- **Tatsaechliches Verhalten:** Tool klickbar, aber Handler blockiert sofort.
- **Ursache:** RequiresRunResult wurde auf `false` gesetzt, aber Handler-Logik setzt Run-Daten voraus.
- **Fix:** `("ConversionPipeline", "Conversion", "\xE8AB", false)` aendern zu `("ConversionPipeline", "Conversion", "\xE8AB", true)`.
- **Testabsicherung:** Unit-Test: ConversionPipeline ToolItem hat `RequiresRunResult == true`.

---

### BUG-77: Conditional Command Registration ohne UI-Feedback

- **Schweregrad:** P3
- **Impact:** Vier Commands (CommandPalette, SystemTray, ApiServer, Accessibility) werden in `RegisterCommands()` nur innerhalb eines `if (_windowHost is not null)` Blocks registriert. Die zugehoerigen Tool-Kacheln sind IMMER im Katalog sichtbar. Wenn `_windowHost` null ist (z.B. in Test-Szenarien oder alternativer Komposition), sind die Kacheln sichtbar und klickbar, aber `Command = null` → Klick bewirkt nichts, kein Feedback. In Production ist MainWindow der WindowHost, daher tritt das Problem dort nicht auf.
- **Betroffene Datei(en):** [FeatureCommandService.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.cs#L225-L231), [ToolsViewModel.cs](src/RomCleanup.UI.Wpf/ViewModels/ToolsViewModel.cs)
- **Reproduktion:** FeatureCommandService mit `windowHost: null` konstruieren → RegisterCommands() → WireToolItemCommands() → CommandPalette-Kachel klicken → nichts passiert.
- **Erwartetes Verhalten:** Entweder (a) Kacheln nur anzeigen wenn Command verfuegbar, oder (b) immer registrieren mit Fallback-Nachricht, oder (c) Guard in ToolItem (IsAvailable property).
- **Tatsaechliches Verhalten:** Sichtbare Kachel, klickbar, Dead Click.
- **Ursache:** Defensive Registrierung (windowHost koennte null sein), aber keine Kopplung zur Kachel-Sichtbarkeit.
- **Fix:** Kacheln konditional filtern wenn Command null bleibt, oder Commands immer registrieren mit Fehler-Dialog wenn windowHost fehlt.
- **Testabsicherung:** Test: FeatureCommandService mit null windowHost → pruefen dass betroffene Commands nicht in FeatureCommands-Dictionary sind ODER dass Kacheln gefiltert werden.

---

### BUG-78: ExportCollection – InputBox "1/2/3" statt Auswahl-Dialog

- **Schweregrad:** P3 UX
- **Impact:** ExportCollection fragt das Exportformat per Text-InputBox: "Nummer eingeben: 1 — CSV, 2 — Excel-XML, 3 — CSV (nur Duplikate)". Nutzer muessen eine Zahl tippen statt aus einer Dropdown/Radio-Liste zu waehlen. Bei Fehleingabe erscheint hardcoded German: "Ungültige Auswahl. Bitte 1, 2 oder 3 eingeben." — auch wenn die GUI auf Englisch steht.
- **Betroffene Datei(en):** [FeatureCommandService.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.cs#L468-L505)
- **Reproduktion:** Tool-Katalog → "Sammlung exportieren" → InputBox → "abc" eingeben → German Fehlermeldung.
- **Erwartetes Verhalten:** `IDialogService.ShowSelectionDialog()` oder Enum-basierte Auswahl. Fehlermeldung ueber i18n.
- **Tatsaechliches Verhalten:** InputBox mit Freitext-Eingabe und hardcoded German Error.
- **Ursache:** Schnelle Implementierung ohne Dialog-Erweiterung.
- **Fix:** (a) IDialogService um ShowSelectionDialog erweitern, oder (b) ComboBox-basierter InputDialog, oder (c) Mindestens Fehlermeldung ueber `_vm.Loc[...]` leiten.
- **Testabsicherung:** Test: Alle Auswahl-Dialoge nutzen i18n. UX-Test: Kein Freitext-Eingabe fuer Enum-Auswahlen.

---

### BUG-79: Hardcoded German Strings umgehen i18n System

- **Schweregrad:** P2
- **Impact:** Mindestens 12 Instanzen von `"Erst einen Lauf starten."` und zahlreiche weitere hardcoded German Strings in Dialog-Titeln, Beschreibungen und Fehlermeldungen. Diese Strings werden NICHT durch `_vm.Loc[...]` geleitet. Englische Nutzer sehen German Fehlermeldungen und Dialog-Texte. Betroffen sind alle 8 FeatureCommandService-Partials.
- **Betroffene Datei(en):** [FeatureCommandService.Analysis.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Analysis.cs), [FeatureCommandService.Collection.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Collection.cs), [FeatureCommandService.Conversion.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Conversion.cs), [FeatureCommandService.Dat.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Dat.cs), [FeatureCommandService.Export.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Export.cs), [FeatureCommandService.Infra.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Infra.cs), [FeatureCommandService.Security.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Security.cs), [FeatureCommandService.Workflow.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Workflow.cs)
- **Reproduktion:** GUI-Sprache auf Englisch stellen → beliebiges Tool ohne vorherigen Run klicken → "Erst einen Lauf starten." (German).
- **Erwartetes Verhalten:** Alle sichtbaren Strings ueber `_vm.Loc["..."]` aufloesen.
- **Tatsaechliches Verhalten:** Hardcoded German Strings direkt in `_dialog.Warn(...)`, `_dialog.Info(...)`, etc.
- **Ursache:** Systematisches i18n-Vergessen bei Handler-Implementierung. Die Loc-Keys existieren fuer Tool-Namen und Kategorien (Tool.*, Cmd.*), aber die Dialog-Inhalte wurden nicht migriert.
- **Fix:** Alle hardcoded German Strings durch `_vm.Loc[...]` oder `_vm.Loc.Format(...)` Aufrufe ersetzen. Fehlende i18n-Keys in en.json und de.json ergaenzen. Systematischer Grep fuer alle String-Literale in FeatureCommandService*.cs.
- **Testabsicherung:** Regression-Test: Grep ueber alle FeatureCommandService-Dateien. Kein deutsches String-Literal ausserhalb von Loc[]-Aufrufen. Oder: Localization-Completeness-Test der alle Dialog-Aufrufe auf Loc-Nutzung prueft.

---

### BUG-80: ConversionPipeline – Name suggeriert Aktion, liefert nur Report

- **Schweregrad:** P3 UX
- **Impact:** "Konvertierungs-Pipeline" suggeriert dem Nutzer, dass eine Konvertierung durchgefuehrt wird. Der Handler zeigt aber nur einen Info-Dialog mit geschaetzter Ersparnis und "Aktiviere 'Konvertierung' und starte einen Move-Lauf." — keine Aktion, nur eine Schaetzung/Info.
- **Betroffene Datei(en):** [FeatureCommandService.Conversion.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Conversion.cs#L18-L27), i18n: `Tool.ConversionPipeline.*`
- **Reproduktion:** Run durchfuehren → Tool-Katalog → "Konvertierungs-Pipeline" klicken → nur Info-Dialog, keine Konvertierung.
- **Erwartetes Verhalten:** Entweder (a) Tool umbenennen zu "Konvertierungs-Vorschau" / "Conversion Estimate", oder (b) tatsaechliche Konvertierung starten koennen.
- **Tatsaechliches Verhalten:** Info-Dialog mit Schaetzwerten.
- **Ursache:** Das Tool wurde als Einstieg/Preview implementiert, aber der Name impliziert eine Pipeline-Ausfuehrung.
- **Fix:** i18n Keys `Tool.ConversionPipeline.Name` und `Tool.ConversionPipeline.Description` auf "Konvertierungs-Vorschau" / "Conversion Estimate" aendern. FeatureCommandKey Konstante kann bleiben.
- **Testabsicherung:** Kein funktionaler Test noetig — reine Namensaenderung in i18n.

---

### BUG-81: HashDatabaseExport – Exportiert Metadaten ohne Hashes

- **Schweregrad:** P2
- **Impact:** "Hash-Datenbank-Export" exportiert `{ MainPath, GameKey, Extension, Region, DatMatch, SizeBytes }` — aber KEINE Hashes. RomCandidate hat `Hash` (L22) und `HeaderlessHash` (L23) Properties, die befuellt sind wenn ein Run mit Hashing gelaufen ist. Der Export ignoriert diese Felder. Nutzer die eine Hash-Datenbank erwarten bekommen nur Metadaten.
- **Betroffene Datei(en):** [FeatureCommandService.Dat.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Dat.cs#L337-L345), [RomCandidate.cs](src/RomCleanup.Contracts/Models/RomCandidate.cs#L22-L23)
- **Reproduktion:** Run mit Hashing → Tool-Katalog → "Hash-Datenbank-Export" → JSON oeffnen → keine Hashes.
- **Erwartetes Verhalten:** Export enthaelt mindestens `Hash` und `HeaderlessHash` Felder pro Eintrag.
- **Tatsaechliches Verhalten:** Nur MainPath, GameKey, Extension, Region, DatMatch, SizeBytes.
- **Ursache:** Anonymes Objekt bei Export-Erstellung vergisst Hash-Properties.
- **Fix:** Anonymous Object um `c.Hash` und `c.HeaderlessHash` erweitern: `new { c.MainPath, c.GameKey, c.Extension, c.Region, c.DatMatch, c.SizeBytes, c.Hash, c.HeaderlessHash }`.
- **Testabsicherung:** Unit-Test: HashDatabaseExport enthaelt Hash/HeaderlessHash Felder. Regression-Test: Export-JSON parsen und Hash-Felder pruefen.

---

### BUG-82: IsPlanned Dead Code – Hardcoded false fuer alle Tools

- **Schweregrad:** P3
- **Impact:** `ToolItem.IsPlanned` Property existiert, ist in XAML gebunden (fuer ein "Geplant"-Badge). Aber `var isPlanned = false;` in ToolsViewModel.cs L196 ist hardcoded fuer alle 41 Tools. Der IsPlanned-Code-Pfad ist komplett tot — die XAML-Bindung wird nie ausgeloest. Kein funktionaler Impact, aber Dead Code in einer release-relevanten UI-Komponente.
- **Betroffene Datei(en):** [ToolsViewModel.cs](src/RomCleanup.UI.Wpf/ViewModels/ToolsViewModel.cs#L196)
- **Reproduktion:** Alle 41 Tools inspizieren → keines hat IsPlanned=true.
- **Erwartetes Verhalten:** Entweder (a) IsPlanned-Mechanismus nutzen (z.B. per data-driven Config), oder (b) Dead Code entfernen (Property + XAML-Binding + Logik).
- **Tatsaechliches Verhalten:** Hardcoded false, toter Code.
- **Ursache:** Feature-Gate Mechanismus wurde angelegt aber nie aktiviert.
- **Fix:** Dead Code entfernen: `IsPlanned` Property aus ToolItem, `isPlanned` Variable aus InitToolItems, XAML-Binding fuer Planned-Badge entfernen. Oder: Planungs-Funktion als Feature nachrüsten.
- **Testabsicherung:** Hygiene-Test: Kein ToolItem hat IsPlanned-Bindung ohne Setter-Logik.

---

### BUG-83: Quarantine – Name suggeriert Aktion, ist nur Read-Only Report

- **Schweregrad:** P3 UX
- **Impact:** "Quarantäne" suggeriert, dass verdaechtige Dateien in einen Quarantaene-Ordner verschoben werden. Der Handler filtert aber nur Candidates (`Junk` Kategorie oder `!DatMatch && Region == "UNKNOWN"`) und zeigt sie in einem Text-Dialog als Liste. Keine Move-Aktion, kein Quarantaene-Verzeichnis, kein Follow-Up moeglich.
- **Betroffene Datei(en):** [FeatureCommandService.Security.cs](src/RomCleanup.UI.Wpf/Services/FeatureCommandService.Security.cs#L68-L82)
- **Reproduktion:** Run durchfuehren → Tool-Katalog → "Quarantäne" → Text-Dialog mit Kandidatenliste → kein Button zum Verschieben.
- **Erwartetes Verhalten:** Entweder (a) Tool umbenennen zu "Quarantäne-Vorschau" / "Quarantine Preview", oder (b) tatsaechliche Quarantaene-Move-Aktion anbieten.
- **Tatsaechliches Verhalten:** Read-Only Text-Dialog mit Kandidatenliste.
- **Ursache:** Service wurde als Report implementiert, Name impliziert Aktion.
- **Fix:** (a) Kurzfristig: i18n Keys auf "Quarantäne-Vorschau" aendern. (b) Langfristig: Move-to-Quarantine Aktion ueber RunConstants.WellKnownFolders.Quarantine implementieren.
- **Testabsicherung:** Falls Move implementiert: Unit-Test fuer Quarantaene-Move in _QUARANTINE Folder. Test dass Quarantaene-Move kein Silent-Delete ist.

---

### TGAP-Eintraege (Bughunt #8)

| ID | Bug-Ref | Beschreibung | Status |
|----|---------|-------------|--------|
| TGAP-65 | BUG-75 | HeaderAnalysis RequiresRunResult auf false setzen | offen |
| TGAP-66 | BUG-76 | ConversionPipeline RequiresRunResult auf true setzen | offen |
| TGAP-67 | BUG-77 | Conditional Commands: Kachel-Sichtbarkeit an Command-Verfuegbarkeit koppeln | offen |
| TGAP-68 | BUG-78 | ExportCollection InputBox durch Selection-Dialog ersetzen; Fehlermeldung i18n | offen |
| TGAP-69 | BUG-79 | Hardcoded German Strings systematisch durch _vm.Loc[] ersetzen | offen |
| TGAP-70 | BUG-80 | ConversionPipeline i18n-Name auf "Konvertierungs-Vorschau" aendern | offen |
| TGAP-71 | BUG-81 | HashDatabaseExport: Hash + HeaderlessHash in Export aufnehmen | offen |
| TGAP-72 | BUG-82 | IsPlanned Dead Code entfernen oder Feature nachrüsten | offen |
| TGAP-73 | BUG-83 | Quarantine: Umbenennen oder Move-Aktion implementieren | offen |

### RequiresRunResult-Audit (komplett)

Alle 41 Tool-Items wurden auf RequiresRunResult-Korrektheit geprueft:

| Tool | RequiresRunResult | Tatsaechlich benoetigt | Status |
|------|-------------------|------------------------|--------|
| HeaderAnalysis | true | false (Datei-Browser) | **MISMATCH → BUG-75** |
| ConversionPipeline | false | true (LastCandidates) | **MISMATCH → BUG-76** |
| Alle anderen 39 Tools | korrekt | korrekt | OK |

### Release-Ballast im Katalog

| Bereich | Beschreibung | Empfehlung |
|---------|-------------|------------|
| IsPlanned Dead Code | Property, Variable, XAML-Binding — nie aktiviert | Entfernen oder nachrüsten (BUG-82) |
| 12+ hardcoded German Strings | Bypass i18n in allen Handler-Partials | Systematisch ersetzen (BUG-79) |
| ExportCollection InputBox UX | Freitext statt Selection | Dialog-Typ upgraden (BUG-78) |
| 3 irrefuehrende Tool-Namen | ConversionPipeline, Quarantine, HashDatabaseExport | Umbenennen (BUG-80, BUG-83, BUG-81) |

### Top 10 Fixes (priorisiert)

1. **BUG-75** – HeaderAnalysis `RequiresRunResult = false` setzen (1 Zeile)
2. **BUG-81** – HashDatabaseExport: `Hash` + `HeaderlessHash` in Export-Objekt aufnehmen (1 Zeile)
3. **BUG-79** – Hardcoded German Strings durch `_vm.Loc[]` ersetzen (12+ Stellen, i18n-Keys ergaenzen)
4. **BUG-76** – ConversionPipeline `RequiresRunResult = true` setzen (1 Zeile)
5. **BUG-80** – ConversionPipeline i18n-Name auf "Konvertierungs-Vorschau" / "Conversion Estimate"
6. **BUG-83** – Quarantine i18n-Name auf "Quarantäne-Vorschau" / "Quarantine Preview"
7. **BUG-78** – ExportCollection Fehlermeldung mindestens ueber i18n leiten
8. **BUG-77** – Conditional Commands: IsVisible an Command-Verfuegbarkeit koppeln
9. **BUG-82** – IsPlanned Dead Code entfernen
10. **BUG-78** – ExportCollection InputBox durch Selection-Dialog ersetzen (erfordert IDialogService-Erweiterung)

### Positiv-Befunde (Bughunt #8)

- [x] **Keine Stubs**: Alle 41 Tool-Items haben echte, funktionale Handler
- [x] **Alle FeatureService-Backing-Methoden real**: CalculateHealthScore, AnalyzeHeader, GetConversionEstimate, CompareDatFiles, BuildCloneTree, etc. — keine Pseudocode-Methoden
- [x] **i18n-Keys komplett**: Alle `Tool.*` und `Cmd.*` Keys in en.json und de.json vorhanden, keine Orphans
- [x] **FeatureCommandKeys**: 73 typisierte Konstanten, keine Magic Strings fuer Tool-Identifikation
- [x] **Tool-Wiring sauber**: WireToolItemCommands() matched korrekt, keine verwaisten Kacheln
- [x] **Kategorie-Icons komplett**: Alle 8 Kategorien (Analysis, Conversion, DatVerify, Collection, Security, Workflow, Export, Infra) haben Icons
- [x] **Quick Access funktional**: Pinning (DefaultPinnedKeys: 6), Recent (max 4), Search Filter — alles korrekt implementiert
- [x] **CLI/API keine Tool-Duplikation**: CLI hat Run/Rollback/UpdateDats/Help/Version, API hat 10 Endpoints — keine Schatten-Tool-Logik
- [x] **DatAutoUpdate**: Vollstaendige async Implementierung mit Catalog-Loading, Stale-Detection, Download, No-Intro Import, Redump-Hint
- [x] **CustomDatEditor**: Multi-Step Logiqx XML Builder mit Crash-Safe Temp+Rename
- [x] **IntegrityMonitor**: Async SHA256 Baseline Create/Verify — real
- [x] **HeaderRepair**: NES Dirty-Byte Repair, SNES Copier-Header Removal mit Backup+Restore — real
- [x] **CommandPalette**: Fuzzy-Search ueber alle FeatureCommands mit Auto-Execute bei Exact Match — real
- [x] **FilterBuilder**: Expression Parser (field=value, field>value Syntax) — real
- [x] **ArcadeMergeSplit**: DAT-basierter Arcade Merge/Split Report — real

---

## Bughunt #9 - Testsuite / Invarianten / QA-Luecken

### Deep Bughunt - Tests / Invarianten / QA-Luecken

## 1. Executive Verdict

Die Testsuite ist breit und deckt viele Kernpfade ab, aber es bestehen mehrere QA-Luecken mit hoher Release-Relevanz.

Hauptprobleme:
- no-crash-only Tests in Security-/Determinismus-nahen Bereichen
- einzelne irrefuehrende Testnamen (Name widerspricht Assertion)
- Benchmark-Gates sind teils standardmaessig nur informativ
- Snapshot-Tests sind teilweise nur Datei-Existenz statt inhaltlicher Regression
- Meta-Tests pruefen Dateiinhalt/Stringsuche statt Laufzeitverhalten

Bewertung: **bedingt release-faehig**, aber mit klaren Testqualitaets-Schulden.

## 2. Kritische Testluecken

1. **Security no-crash-only statt Sicherheitsinvariante**
2. **Idempotenz-/Overflow-Regressionen mit schwachen Assertions**
3. **Benchmark-Gates nicht ueberall hard-enforced**
4. **Snapshot-Luecken (inv04/inv05 ohne semantische Verifikation)**
5. **Meta-Tests liefern strukturelle statt fachliche Sicherheit**
6. **Mid-run Cancellation nicht real abgesichert**
7. **Conversion-Fehlerpfade nicht granular genug getestet**

## 3. Findings

### BUG-84: Security-Tests sind no-crash-only statt Sanitizing-Assertions

- **Status:** erledigt am 2026-03-30 (Assertions auf echte Invarianten erweitert)

- **Schweregrad:** P1
- **Betroffene Dateien:** [SecurityTests.cs](src/RomCleanup.Tests/SecurityTests.cs#L53), [SecurityTests.cs](src/RomCleanup.Tests/SecurityTests.cs#L66), [SecurityTests.cs](src/RomCleanup.Tests/SecurityTests.cs#L133), [SecurityTests.cs](src/RomCleanup.Tests/SecurityTests.cs#L140)
- **Problem:** Tests wie `GameKeyNormalizer_PathTraversalInFileName_DoesNotCrash` und `GameKeyNormalizer_ZipSlipPaths_Normalized` pruefen nur `NotNull`/`NotEmpty` bzw. `Null(ex)`, nicht aber echte Sicherheitsinvarianten.
- **Warum gefaehrlich:** Falsche Sicherheit bei Security-Fixes; unsichere Outputs koennen unbemerkt bleiben.
- **Fix:** Assertions auf echte Invarianten erweitern (`..` entfernt, kein Root-Escape, erwartete Key-Form).
- **Empfohlene neue Tests:** `Security_PathTraversal_IsSanitized_NotOnlyNoCrash()`, `Security_ZipSlipInput_DoesNotLeakTraversalTokens()`.

---

### BUG-85: Idempotenz-Test prueft keine Idempotenz

- **Status:** erledigt am 2026-03-30 (`Assert.Equal(first, second)` umgesetzt)

- **Schweregrad:** P1
- **Betroffene Dateien:** [GameKeyNormalizerTests.cs](src/RomCleanup.Tests/GameKeyNormalizerTests.cs#L114)
- **Problem:** `Normalize_IsIdempotent` assertiert nur "nicht leer" statt `first == second`.
- **Warum gefaehrlich:** Regessionen in der Key-Bildung bleiben unentdeckt.
- **Fix:** `Assert.Equal(first, second)` als Kernassertion.
- **Empfohlene neue Tests:** Theorie mit problematischen Inputs (Unicode, lange Tag-Ketten, gemischte Varianten).

---

### BUG-86: Overflow-Regressionstests sind fachlich zu schwach

- **Status:** erledigt am 2026-03-30 (deterministische und erwartungsbasierte Score-Assertions)

- **Schweregrad:** P1
- **Betroffene Dateien:** [VersionScorerTests.cs](src/RomCleanup.Tests/VersionScorerTests.cs#L115), [VersionScorerTests.cs](src/RomCleanup.Tests/VersionScorerTests.cs#L123)
- **Problem:** Tests mit "DoesNotThrow" pruefen nur `score >= 0`.
- **Warum gefaehrlich:** Overflow-/Parsing-Fehler koennen weiter existieren, solange Ergebnis nicht negativ ist.
- **Fix:** Zusatzaussagen auf erwartetes Clamp-/Fallback-Verhalten und Determinismus.
- **Empfohlene neue Tests:** `VersionScore_ExtremeRevision_UsesStableFallbackRange()`, `VersionScore_ExtremeInput_IsDeterministicAcrossRuns()`.

---

### BUG-87: Irrefuehrender Testname widerspricht Assertion

- **Schweregrad:** P2
- **Betroffene Dateien:** [DeduplicationEngineTests.cs](src/RomCleanup.Tests/DeduplicationEngineTests.cs#L305)
- **Problem:** `SelectWinner_BiosCategory_BeatsGameDespiteHigherScores` behauptet BIOS gewinnt, Assertion prueft aber `Game` gewinnt.
- **Warum gefaehrlich:** Reviewer werden in zentrale Ranking-Logik fehlgeleitet.
- **Fix:** Testname + Kommentar an reale Regel angleichen.
- **Empfohlene neue Tests:** Matrix-Test fuer Kategorie-Ranking mit expliziter Erwartung pro Paar.

---

### BUG-88: Holdout/Tier Benchmark-Gates sind standardmaessig optional

- **Schweregrad:** P1
- **Betroffene Dateien:** [HoldoutGateTests.cs](src/RomCleanup.Tests/Benchmark/HoldoutGateTests.cs#L21), [HoldoutGateTests.cs](src/RomCleanup.Tests/Benchmark/HoldoutGateTests.cs#L55), [SystemTierGateTests.cs](src/RomCleanup.Tests/Benchmark/SystemTierGateTests.cs#L41)
- **Problem:** Gates returnen ohne Hard-Fail, wenn `ROMCLEANUP_ENFORCE_QUALITY_GATES` nicht gesetzt ist.
- **Warum gefaehrlich:** CI kann gruen sein, obwohl Qualitaetsschwellen verletzt sind.
- **Fix:** Release-Pipeline mit verpflichtendem Hard-Fail-Modus; informational nur in explizitem Non-Release-Job.
- **Empfohlene neue Tests:** `QualityGate_ReleaseMode_AlwaysEnforced()` als CI-Contract-Test.

---

### BUG-89: Snapshot-Luecke fuer inv04/inv05 (nur Existenz, keine Semantik)

- **Schweregrad:** P2
- **Betroffene Dateien:** [Issue9InvariantRegressionRedPhaseTests.cs](src/RomCleanup.Tests/Issue9InvariantRegressionRedPhaseTests.cs#L500)
- **Problem:** `inv04-score-golden.json` und `inv05-classification-golden.json` werden nur auf Existenz geprueft.
- **Warum gefaehrlich:** Snapshot-Drift bleibt unerkannt.
- **Fix:** Snapshot laden und gegen Runtime-Berechnung vergleichen.
- **Empfohlene neue Tests:** `ScoreGoldenSnapshot_MatchesCurrentScoring()`, `ClassificationGoldenSnapshot_MatchesCurrentClassifier()`.

---

### BUG-90: Meta-Tests pruefen Quelltext statt Verhalten

- **Schweregrad:** P2
- **Betroffene Dateien:** [Issue9InvariantRegressionRedPhaseTests.cs](src/RomCleanup.Tests/Issue9InvariantRegressionRedPhaseTests.cs#L546), [Issue9InvariantRegressionRedPhaseTests.cs](src/RomCleanup.Tests/Issue9InvariantRegressionRedPhaseTests.cs#L558), [Issue9InvariantRegressionRedPhaseTests.cs](src/RomCleanup.Tests/Issue9InvariantRegressionRedPhaseTests.cs#L590)
- **Problem:** Tests wie `Should_ContainExpandedPipelineIsolationScenarios...` oder `Should_HaveUnifiedRunOptionsBuilder...` pruefen nur Dateiinhalte/Type.Exists.
- **Warum gefaehrlich:** Fachliches Verhalten kann brechen, obwohl Tests gruen bleiben.
- **Fix:** Meta-Checks durch verhaltensorientierte Integrations-/Invariantentests ersetzen.
- **Empfohlene neue Tests:** End-to-end Tests fuer Pipeline-Isolation, Audit-Roundtrip und RunOptionsBuilder-Validierung.

---

### BUG-91: Mid-run Cancellation wird nicht real getestet

- **Schweregrad:** P2
- **Betroffene Dateien:** [RunOrchestratorTests.cs](src/RomCleanup.Tests/RunOrchestratorTests.cs#L219)
- **Problem:** `Execute_Cancellation_ThrowsOperationCanceled` cancelt den Token vor Start (`cts.Cancel()`), kein echter Mid-Run-Abbruch.
- **Warum gefaehrlich:** Teilzustandsfehler bei Abbruch in laufenden Phasen bleiben unentdeckt.
- **Fix:** Test mit `CancelAfter` in einem echten Mehrdatei-/Langlauf-Szenario.
- **Empfohlene neue Tests:** `Execute_CancelDuringMovePhase_PreservesAuditAndStateInvariant()`.

---

### BUG-92: Conversion-Fehlerpfad-Audit nicht explizit abgesichert

- **Schweregrad:** P2
- **Betroffene Dateien:** [ConversionPhaseHelper.cs](src/RomCleanup.Infrastructure/Orchestration/ConversionPhaseHelper.cs#L130), [PipelinePhaseHelpers.cs](src/RomCleanup.Infrastructure/Orchestration/PipelinePhaseHelpers.cs#L52)
- **Problem:** Kein dedizierter Test, der sicherstellt, dass `AppendConversionErrorAudit` im Error-Zweig immer korrekt geschrieben wird.
- **Warum gefaehrlich:** Forensik-Luecke bei Conversion-Fehlern moeglich.
- **Fix:** Error-Branch mit kontrolliertem Converter-Testdouble und Audit-Assertion absichern.
- **Empfohlene neue Tests:** `Conversion_ErrorOutcome_WritesConversionErrorAuditRow()`.

---

### BUG-93: CI-Coverage-Gate-Test ist semantisch weich

- **Schweregrad:** P2
- **Betroffene Dateien:** [AuditComplianceTests.cs](src/RomCleanup.Tests/AuditComplianceTests.cs#L174)
- **Problem:** `Audit1_Test005_CoverageGate_CI` faellt nicht, wenn die Workflow-Datei fehlt.
- **Warum gefaehrlich:** Testname suggeriert harte CI-Gate-Verifikation, liefert aber nur bedingte Aussage.
- **Fix:** Entweder klar als informational benennen oder fehlende Pipeline-Datei hart failen lassen.
- **Empfohlene neue Tests:** `CiCoverageWorkflow_MustExistAndContainCoverageGate()`.

---

### TGAP-Eintraege (Bughunt #9)

| ID | Bug-Ref | Beschreibung | Status |
|----|---------|-------------|--------|
| TGAP-74 | BUG-84 | Security no-crash-only durch Sanitizing-Invarianten ersetzen | erledigt (2026-03-30) |
| TGAP-75 | BUG-85 | Idempotenz-Test auf `Assert.Equal(first, second)` umstellen | erledigt (2026-03-30) |
| TGAP-76 | BUG-86 | Overflow-Regressionen mit Fachassertions absichern | erledigt (2026-03-30) |
| TGAP-77 | BUG-87 | Irrefuehrenden Dedupe-Testnamen korrigieren + Ranking-Matrix-Test | offen |
| TGAP-78 | BUG-88 | Holdout/Tier-Gates im Release-Mode hard-enforced absichern | offen |
| TGAP-79 | BUG-89 | inv04/inv05 Snapshot inhaltlich validieren | offen |
| TGAP-80 | BUG-90 | Meta-Tests in Verhaltens-/Integrations-Tests umwandeln | offen |
| TGAP-81 | BUG-91 | Mid-run Cancellation mit `CancelAfter` absichern | offen |
| TGAP-82 | BUG-92 | Conversion Error-Audit branch explizit testen | offen |
| TGAP-83 | BUG-93 | CI Coverage-Gate als harte Bedingung testen | offen |

## 4. Top 10 QA-Luecken

1. Security no-crash-only in [SecurityTests.cs](src/RomCleanup.Tests/SecurityTests.cs#L53)
2. Idempotenz-Test ohne Gleichheitsassertion in [GameKeyNormalizerTests.cs](src/RomCleanup.Tests/GameKeyNormalizerTests.cs#L114)
3. Overflow-Regressionen mit schwacher Aussage in [VersionScorerTests.cs](src/RomCleanup.Tests/VersionScorerTests.cs#L115)
4. Optionalisierte Holdout/Tier-Gates in [HoldoutGateTests.cs](src/RomCleanup.Tests/Benchmark/HoldoutGateTests.cs#L21)
5. Snapshot-Semantikluecke in [Issue9InvariantRegressionRedPhaseTests.cs](src/RomCleanup.Tests/Issue9InvariantRegressionRedPhaseTests.cs#L500)
6. Meta-Tests statt Verhaltensabsicherung in [Issue9InvariantRegressionRedPhaseTests.cs](src/RomCleanup.Tests/Issue9InvariantRegressionRedPhaseTests.cs#L546)
7. Irrefuehrender Dedup-Testname in [DeduplicationEngineTests.cs](src/RomCleanup.Tests/DeduplicationEngineTests.cs#L305)
8. Mid-run Cancellation nicht getestet in [RunOrchestratorTests.cs](src/RomCleanup.Tests/RunOrchestratorTests.cs#L219)
9. Conversion Error-Audit branch nicht explizit getestet in [ConversionPhaseHelper.cs](src/RomCleanup.Infrastructure/Orchestration/ConversionPhaseHelper.cs#L130)
10. CI Coverage Gate Test semantisch weich in [AuditComplianceTests.cs](src/RomCleanup.Tests/AuditComplianceTests.cs#L174)

## 5. Priorisierter Test-Sanierungsplan

1. [x] **P1 sofort:** BUG-84/85/86 (Security + Idempotenz + Overflow) auf echte Fachinvarianten umstellen.
2. **P1 sofort:** BUG-88 (Benchmark Holdout/Tier Gates fuer Release hard-enforce).
3. **P2 kurzfristig:** BUG-89/90 (Snapshot-Semantik + Meta-Tests zu Verhaltens-Tests).
4. **P2 kurzfristig:** BUG-91/92 (Mid-run Cancellation + Conversion Error-Audit branch).
5. **P2 kurzfristig:** BUG-93 (CI Coverage Gate Test hart/spezifisch machen).
6. **Hygiene:** BUG-87 (irrefuehrender Name) als schneller Klarheitsfix.
7. **Verifikation:** Nach Sanierung Unit + Integration + Benchmark-Gates laufen lassen; Paritaets-/Determinismus-Suites als Pflichtgate markieren.

---