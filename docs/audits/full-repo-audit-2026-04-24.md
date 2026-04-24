# Full Repo Audit – Romulus `src/` (2026-04-24)

> **Status:** Tracking-Dokument fuer alle Audit-Findings aus mehreren Tiefen-Audit-Runden (Round 1-8).
> Jedes Finding hat eine Checkbox, die beim Umsetzen abgehakt werden muss.
> Quelle: parallele Audits durch `gem-reviewer`, `SE: Architect`, `gem-critic`, `SE: Security`.
> Scope: ausschliesslich `src/` (alle Projekte). `archive/powershell/` ignoriert.

---

## Executive Verdict

**Repo ist NICHT release-tauglich.** Kumulativ 19 P0-Funde (Datenverlust + Audit-Integritaet + Broken Access Control), 74 P1-Funde, 113 P2-Funde und 55 P3-Funde. Wichtigste systemische Wurzeln:

1. **Verify-Vertrag ist fail-open** statt fail-closed (Conversion, HMAC, Sidecar).
2. **„Eine fachliche Wahrheit" verletzt** an >10 Stellen (CSV-Sanitizer, DAT-Update, RunOrchestrator-Komposition, Status-Strings, Settings-Loader).
3. **Statisch-Mutable State in Core** untergraebt Determinismus (Dedup-Ranks, ClassificationIo).
4. **Halbfertige Refactors** als Schattenlogik (Avalonia, FeatureService, MainViewModel).
5. **Tests zementieren Bug-Verhalten** (z. B. Verify-Bypass-Test, Alibi-Determinismus-Test).
6. **Audit-Kette ohne kryptografische Anker** (kein Hash-Chain, kein KeyId, additionalMetadata unsigniert).

---

## Priorisierung & Fortschritt

| Severity | Round 1+2 | Round 3 | Round 4 | Round 5 | Round 6 | Round 7 | Round 8 | Gesamt | Erledigt |
|----------|----------:|--------:|--------:|--------:|--------:|--------:|--------:|-------:|---------:|
| **P0** (Release-Blocker)         |  8 |  1 |  9 |  1 |  0 |  0 |  0 |  19 | 6 |
| **P1** (Hohe Risiken)            | 22 | 15 | 17 | 11 |  6 |  3 |  0 |  74 | 13 |
| **P2** (Mittlere Risiken)        | 26 | 22 | 22 | 19 | 18 |  6 |  0 | 113 | 7 |
| **P3** (Wartbarkeit / niedrig)   | 12 |  6 | 13 |  9 | 12 |  3 |  0 |  55 | 2 |
| **Gesamt**                       | 68 | 44 | 61 | 40 | 36 | 12 |  0 | 261 | 28 |

> Bitte beim Abhaken die Tabelle hier oben mit aktualisieren.
> Round-3-Funde: `R3-A-*` (DAT/Hash/Tools), `R3-B-*` (Settings/Loc/UI), `R3-C-*` (API/CLI/Reports/Logging).
> Round-4-Funde: `R4-A-*` (Avalonia), `R4-B-*` (Tests/Benchmark), `R4-C-*` (Deploy/Safety/Logging/Audit).
> Round-5-Funde: `R5-A-*` (Core/Contracts), `R5-B-*` (Infrastructure/Orchestration/Reporting), `R5-C-*` (WPF ViewModels/API).
> Round-6-Funde: `R6-A-*` (CLI/Tools/Hashing/Safety/Sorting), `R6-B-*` (Data/Schemas/i18n/Loader), `R6-C-*` (XAML/Resources/Build/Avalonia).
> Round-7-Funde: `R7-A-*` (Scanner/Reparse/Conversion), `R7-B-*` (Reports/Exports/Determinismus), `R7-C-*` (GUI-Import/Merge/Profile-Hygiene).
> Round-8-Abschluss: keine weiteren nicht-duplizierten P3-Funde in den gezielten Rest-Suchen.

---

## Inhalt

- [P0 – Release-Blocker](#p0--release-blocker)
- [P1 – Hohe Risiken](#p1--hohe-risiken)
- [P2 – Mittlere Risiken](#p2--mittlere-risiken)
- [P3 – Wartbarkeit / Niedrige Risiken](#p3--wartbarkeit--niedrige-risiken)
- [Round 3 – Neue Funde](#round-3--neue-funde)
- [Round 4 – Neue Funde](#round-4--neue-funde)
- [Round 5 – Neue Funde](#round-5--neue-funde)
- [Round 6 – Neue Funde](#round-6--neue-funde)
- [Round 7 – Neue Funde](#round-7--neue-funde)
- [Round 8 – Abschlussrunde ohne neue Funde](#round-8--abschlussrunde-ohne-neue-funde)
- [Test- & Verifikationsplan](#test---verifikationsplan)
- [Sanierungsstrategie](#sanierungsstrategie)

---

## P0 – Release-Blocker

> Diese Funde MUESSEN vor jedem weiteren Feature-Commit oder Release behoben werden.

### P0-01 — Conversion-Source wandert in Trash ohne echte Verifikation
**Tags:** Release-Blocker · Data-Integrity Risk · Bug
- [ ] **Fix umsetzen**
- **Files:** [src/Romulus.Infrastructure/Orchestration/ConversionVerificationHelpers.cs](../../src/Romulus.Infrastructure/Orchestration/ConversionVerificationHelpers.cs#L21-L46), [ConversionPhaseHelper.cs](../../src/Romulus.Infrastructure/Orchestration/ConversionPhaseHelper.cs#L196), [ChdmanToolConverter.cs](../../src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs#L64), [SevenZipToolConverter.cs](../../src/Romulus.Infrastructure/Conversion/SevenZipToolConverter.cs#L38), [FormatConverterAdapter.cs](../../src/Romulus.Infrastructure/Conversion/FormatConverterAdapter.cs#L393)
- **Problem:** `IsVerificationSuccessful` liefert `true` bei `(NotAttempted, target=null, plan ohne Capability)`. Source landet anschliessend in `_TRASH_PostConversion`, ohne dass `chdman verify`/`7z t` jemals lief.
- **Reproduktion:** Conversion-Registry nicht ladbar -> `ConvertLegacy`-Pfad -> `ConversionResult.NotAttempted` -> Source weg.
- **Fix:** `IsVerificationSuccessful` bei `NotAttempted` immer `false`. Pflicht-Verify in `ChdmanToolConverter.Convert` und `SevenZipToolConverter.Convert`. `ConvertLegacy`-Pfad loeschen.
- **Tests fehlen:**
  - [ ] Regression `(NotAttempted, target=null) -> false`
  - [ ] Integration mit Stub-Konverter ohne Verify -> Source bleibt erhalten
  - [ ] Negative-Test: Mock-chdman exit 0 + zero-byte Output -> kein Source-Move

### P0-02 — Test zementiert Verify-Bug-Verhalten
**Tags:** Release-Blocker · False Confidence Risk
- [ ] **Test invertieren oder loeschen**
- **File:** `src/Romulus.Tests/...IsVerificationSuccessful_NotAttempted_NullTarget_ReturnsTrueForLegacyPath`
- **Problem:** Bestehender Test fixiert das Bug-Verhalten als „erwartet" und blockiert P0-01.
- **Fix:** Test umkehren: `NotAttempted_NullTarget_ReturnsFalse` ODER Test entfernen, sobald P0-01 umgesetzt ist.

### P0-03 — GameKey „__empty_key_null" kollidiert fuer alle Whitespace-Namen
**Tags:** Release-Blocker · Data-Integrity Risk · Determinism Risk
- [x] **Fix umsetzen**
- **File:** [src/Romulus.Core/GameKeys/GameKeyNormalizer.cs](../../src/Romulus.Core/GameKeys/GameKeyNormalizer.cs#L240-L283)
- **Problem:** Frueh-Return Z. 241 weist allen Whitespace-Namen denselben Key zu -> unverwandte Files werden gruppiert -> Loser landet in `_TRASH_REGION_DEDUPE`.
- **Reproduktion:** `   .iso` und `\u3000.chd` -> beide Key `__empty_key_null` -> ein File wird geloescht.
- **Fix:** Frueh-Pfad ebenfalls hash-suffigieren (analog Spaet-Fallback Z. 280-283) ODER Kandidat aus Gruppierung ausschliessen.
- **Tests fehlen:**
  - [x] Property-Test: `Normalize(a) != Normalize(b)` fuer 2 verschiedene Whitespace-Inputs

### P0-04 — Cross-Volume-Move nicht atomar (Cancel/IO-Fehler hinterlaesst Partials)
**Tags:** Release-Blocker · Data-Integrity Risk
- [x] **Fix umsetzen**
- **File:** [src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs](../../src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs#L393-L465)
- **Problem:** .NET `File.Move` zwischen Volumes = Copy+Delete. Bei Cancel/IOException bleibt halbe Zieldatei + ganze Source -> Trash-Inflation, korrupte Files, Audit verweist auf abgeschnittene Datei.
- **Fix:** Stage-File `{dest}.tmpmv` schreiben -> atomarer `File.Move` innerhalb Zielvolume -> Source loeschen. `try/catch/finally` mit Tempfile-Cleanup.
- **Tests fehlen:**
  - [ ] Mock-IFS wirft `OperationCanceledException` -> keine Restdatei am Ziel
  - [ ] Mock-IFS wirft IOException nach Teil-Copy -> Tempfile geloescht

### P0-05 — Audit-Sidecar `meta.json` nicht atomar geschrieben
**Tags:** Release-Blocker · Data-Integrity Risk · Audit Integrity Risk
- [x] **Fix umsetzen**
- **File:** [src/Romulus.Infrastructure/Audit/AuditSigningService.cs](../../src/Romulus.Infrastructure/Audit/AuditSigningService.cs#L177-L181)
- **Problem:** `File.WriteAllText(metaPath, json, Encoding.UTF8)` direkt auf Zielpfad. Crash mitten im Schreiben -> Sidecar halb-geschrieben/leer -> `VerifyMetadataSidecar` wirft `JsonException` -> Rollback dauerhaft blockiert. Inkonsistent zur Key-Datei (die ist atomar via tmp+Move).
- **Fix:** Schreiben ueber `metaPath + ".tmp"` mit `File.Move(tmp, metaPath, overwrite:true)`.
- **Tests fehlen:**
  - [ ] Halben JSON nach `meta.json` schreiben -> Rollback liefert klares `INTEGRITY_BROKEN`-Result, NICHT alle Zeilen als Failed

### P0-06 — HMAC-Signing-Key wird aus leerer / korrupter Datei stillschweigend regeneriert
**Tags:** Release-Blocker · Security Risk · Critical (OWASP A08)
- [x] **Fix umsetzen**
- **File:** [src/Romulus.Infrastructure/Audit/AuditSigningService.cs](../../src/Romulus.Infrastructure/Audit/AuditSigningService.cs#L42-L60)
- **Problem:** Bei leerer Datei (`Convert.FromHexString("") == byte[0]`) oder `FormatException` wird neuer Key generiert ohne Laengenpruefung. Alle bestehenden Sidecars werden ab sofort als „tampered" abgelehnt -> Denial-of-Rollback. Angreifer mit Schreibrecht kann durch Byte-Flip jeden Rollback blockieren.
- **Fix:** Nach `Convert.FromHexString` validieren `Length == 32`, sonst fail-closed (`InvalidOperationException`). Korrupten Key in `quarantine/<utc>.bad` verschieben statt ueberschreiben. Niemals stillschweigend rotieren.
- **Tests fehlen:**
  - [ ] Negative: leere/Whitespace/zu kurze Key-Datei -> Konstruktor wirft, alte Sidecars bleiben verifizierbar
  - [ ] Korrupte Hex-Datei -> wirft, Datei bleibt unveraendert

### P0-07 — Audit-Rollback liefert leeres Ergebnis bei geloeschter CSV
**Tags:** Release-Blocker · Audit Integrity Risk
- [x] **Fix umsetzen**
- **File:** [src/Romulus.Infrastructure/Audit/AuditSigningService.cs](../../src/Romulus.Infrastructure/Audit/AuditSigningService.cs#L222)
- **Problem:** Frueher Exit-Branch behandelt fehlende CSV als „nichts zu tun". Sidecar-Existenz wird nicht gegengeprueft. UI zeigt „Rollback erfolgreich, 0 Dateien" obwohl Tampering vorliegt.
- **Fix:** Wenn `File.Exists(metaPath) && !File.Exists(auditCsvPath)` -> `Failed = metadata.RowCount, Tampered=true`. Result-Felder `Tampered` ergaenzen.
- **Tests fehlen:**
  - [ ] `Rollback_WithMissingCsvAndPresentSidecar_ReportsTampered`
  - [ ] `Rollback_WithBothMissing_ReportsFailedNotZero`

### P0-08 — Run/Snapshot mit leerem `OwnerClientId` ist fuer alle API-Keys offen
**Tags:** Release-Blocker · Security Risk · Broken Access Control (OWASP A01)
- [x] **Fix umsetzen**
- **File:** [src/Romulus.Api/ProgramHelpers.cs](../../src/Romulus.Api/ProgramHelpers.cs#L181-L190)
- **Problem:** `CanAccessRun`/`CanAccessSnapshot` geben `true` zurueck, sobald `OwnerClientId` `null/whitespace` ist. Legacy-/importierte Records ohne Owner sind fuer jeden gueltigen Key sichtbar UND steuerbar (inkl. `/runs/{id}/rollback`, `/export/frontend`, `/collections/merge/apply`).
- **Fix:** `OwnerClientId` zur Pflicht. Records ohne Owner als „system-locked" -> eigener Admin-Scope. `CanAccessRun` defaultet auf `false`.
- **Tests fehlen:**
  - [ ] Run mit `OwnerClientId=""` -> fremder API-Key bekommt 403, nicht 200

---

## P1 – Hohe Risiken

### P1-01 — Avalonia ist Shadow-UI ohne RunOrchestrator-Anbindung
**Tags:** Shadow Logic · Parity Risk
- [ ] **Entscheidung treffen + umsetzen** (entweder Stub markieren ODER echte Anbindung)
- **Files:** [MainWindowViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/MainWindowViewModel.cs#L113), [ProgressViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/ProgressViewModel.cs#L51-L75), [StartViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/StartViewModel.cs), [ResultViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/ResultViewModel.cs)
- **Problem:** `StartPreview()` ruft Progress an und setzt `Progress=100%`. Kein Aufruf von RunOrchestrator/RunService. Verstoesst „Keine halben Loesungen".
- **Fix:** Avalonia per Feature-Flag aus Build-Default rausnehmen ODER an `RunService` anschliessen.

### P1-02 — `RunOrchestrator`-Komposition dreifach dupliziert
**Tags:** Parity Risk · Duplicate Logic
- [ ] **Fix umsetzen**
- **Files:** [RunManager.cs](../../src/Romulus.Api/RunManager.cs#L113-L130), [RunService.cs](../../src/Romulus.UI.Wpf/Services/RunService.cs#L106-L120), [Program.cs](../../src/Romulus.CLI/Program.cs#L243-L283), [Program.Subcommands.AnalysisAndDat.cs](../../src/Romulus.CLI/Program.Subcommands.AnalysisAndDat.cs#L34)
- **Problem:** 17-Parameter-`new RunOrchestrator(...)` wortgleich an 3+ Stellen. Jeder neue Parameter muss ueberall nachgezogen werden.
- **Fix:** `env.CreateOrchestrator(onProgress, reviewDecisionService)` in `RunEnvironment`/Factory.

### P1-03 — DAT-Update-Pipeline dreifach implementiert
**Tags:** Shadow Logic · Duplicate Logic
- [ ] **Fix umsetzen**
- **Files:** [Program.cs API](../../src/Romulus.Api/Program.cs#L546-L650), [Program.cs CLI](../../src/Romulus.CLI/Program.cs#L370-L420), [DatCatalogViewModel.cs](../../src/Romulus.UI.Wpf/ViewModels/DatCatalogViewModel.cs#L145)
- **Fix:** `DatSourceService.UpdateCatalogAsync(catalog, force, ct) -> DatBatchUpdateResult`.

### P1-04 — Doppelte CSV-Sanitizer (DAT-Audit fehlt UNC-Schutz)
**Tags:** Security Risk · Parity Risk · Duplicate Logic
- [x] **Fix umsetzen**
- **Files:** [AuditCsvParser.cs](../../src/Romulus.Infrastructure/Audit/AuditCsvParser.cs#L119-L138), [DatAuditViewModel.cs](../../src/Romulus.UI.Wpf/ViewModels/DatAuditViewModel.cs#L188-L251)
- **Problem:** `SanitizeDatAuditCsvField` neutralisiert nicht `\\`-Prefix (UNC). Excel oeffnet UNC-Verbindung beim CSV-Open. R5-011-Fix existiert nur in `SanitizeSpreadsheetCsvField`.
- **Fix:** Auf gemeinsamen Helper konsolidieren.
- **Tests fehlen:**
  - [ ] `SanitizeDatAuditCsvField("\\\\evil\\share")` muss prefixed/escaped sein
  - [ ] Parity-Test DAT-CSV vs. Spreadsheet-CSV

### P1-05 — `MoveSetAtomically` wirft `AggregateException` durch
**Tags:** Data-Integrity Risk · False Confidence Risk
- [ ] **Fix umsetzen**
- **File:** [ConsoleSorter.cs](../../src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs#L497-L520) (Throw), Call-Sites Z. 183, 238, 298, 376
- **Problem:** Sort-Phase bricht mitten in Iteration ab; KPIs/Counter inkonsistent. Mischt zwei Fehlerklassen.
- **Fix:** Immer `(false, 0, [])` zurueckgeben + `criticalRollbackFailures: IReadOnlyList<string>` in `ConsoleSortResult`. Reason-Tag `set-rollback-failed`.
- **Tests fehlen:**
  - [ ] Mock-IFS Move-Failure + Rollback-Failure -> Tupel statt Exception

### P1-06 — Set-Rollback `MoveItemSafely` ohne `overwrite` -> silent DUP-Suffix
**Tags:** Data-Integrity Risk
- [ ] **Fix umsetzen**
- **File:** [ConsoleSorter.cs](../../src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs#L495-L515)
- **Problem:** Rollback nimmt DUP-Suffix `original__DUP1.cue` ohne Auditspur. Verletzt Audit/Undo-Garantie.
- **Fix:** Rollback mit `overwrite: true`. Bei belegter Source-Position WERFEN.
- **Tests fehlen:**
  - [ ] Race-Test mit zwischenzeitlich auftauchender Source -> Rollback wirft

### P1-07 — `EnrichmentPipelinePhase` Parallel.For ohne dokumentierte Thread-Safety
**Tags:** Determinism Risk · Concurrency
- [ ] **Fix umsetzen**
- **File:** [EnrichmentPipelinePhase.cs](../../src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs#L56-L82)
- **Problem:** DatIndex/HashService aus mehreren Threads -> potenzielle Race-Cache-Variation -> unterschiedliche DAT-Hits je Lauf.
- **Fix:** Locks/Concurrent-Collections in DatIndex bestaetigen oder einbauen.
- **Tests fehlen:**
  - [ ] Stress-Test 200x parallel -> bit-identische Candidate-Liste

### P1-08 — `DeduplicationEngine` mit globalem mutable static state
**Tags:** Architecture Debt Hotspot · Determinism Risk
- [x] **Fix umsetzen**
- **File:** [DeduplicationEngine.cs](../../src/Romulus.Core/Deduplication/DeduplicationEngine.cs#L10-L57)
- **Problem:** `RegisterCategoryRanks()`/`RegisterCategoryRankFactory()` setzen prozessweite Felder. `ResetForTesting()` ist Eingestaendnis. In API+WPF im selben Prozess: letzter Registrant gewinnt.
- **Fix:** Ranks per ctor-Parameter durchreichen.

### P1-09 — Reparse-Point-Check ignoriert NTFS-Hardlinks
**Tags:** Data-Integrity Risk · KPI-Luege
- [x] **Fix umsetzen**
- **File:** `MoveItemSafely`/`EnsureNoReparsePointInExistingAncestry` in `FileSystemAdapter.cs`
- **Problem:** Hardlink-Loser geht in Trash, Winner-Pfad ist identisch -> KPI „Saved bytes" lügt.
- **Fix:** `BY_HANDLE_FILE_INFORMATION.NumberOfLinks > 1` per `GetFileInformationByHandle` pruefen, Audit ehrlich markieren.
- **Tests fehlen:**
  - [ ] Hardlink-Set-Test

### P1-10 — `ConversionExecutor.BuildOutputPath` zwingt Output ins Source-Directory
**Tags:** Architecture Debt · Data-Integrity Risk
- [ ] **Fix umsetzen**
- **File:** [ConversionExecutor.cs](../../src/Romulus.Infrastructure/Conversion/ConversionExecutor.cs#L298-L313)
- **Problem:** Read-only Source (NAS, ISO-Mount) -> Multi-Step bricht ab. Long-Path > 248 Zeichen -> externe Tools schlagen fehl.
- **Fix:** Konfigurierbares Output-Verzeichnis pro Run (Default = SourceDir, Override = WorkRoot). `\\?\`-Praefix fuer externe Tools bei langen Pfaden.

### P1-11 — `MainViewModel` Gott-Klasse (~4.811 LoC ueber 5 Partials)
**Tags:** Architecture Debt Hotspot
- [ ] **Refactor umsetzen** (Gross-Aufwand)
- **Files:** [MainViewModel.cs](../../src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs) + 4 Partials (Settings, RunPipeline, WatchAndProgress, Productization)
- **Fix:** RunPipeline-Partials -> `RunPipelineViewModel`. Settings-Partial -> in `SetupViewModel` integrieren. Productization -> `ProductizationViewModel`.

### P1-12 — Audit-CSV bricht bei Newlines im `Reason`-Feld
**Tags:** Data-Integrity Risk
- [x] **Fix umsetzen**
- **Files:** [DatRenamePipelinePhase.cs](../../src/Romulus.Infrastructure/Orchestration/DatRenamePipelinePhase.cs#L107-L116), [AuditSigningService.cs](../../src/Romulus.Infrastructure/Audit/AuditSigningService.cs#L645-L688)
- **Problem:** DAT-XML kann mehrzeilige Werte enthalten. `ReadAuditRowsReverse` liest zeilenweise -> Felder mit `\n` zerlegt -> Eintrag wird `skippedUnsafe` -> Rollback ueberspringt.
- **Fix:** In `WriteAuditRowCore` `\r\n` aus Werten entfernen vor Quoting; ODER echten CSV-Reader (TextFieldParser/CsvHelper) nutzen.
- **Tests fehlen:**
  - [ ] Round-trip: AppendAuditRow mit `reason="line1\nline2"` -> ReadAuditRowsReverse liefert genau eine Zeile

### P1-13 — `DatRenamePipelinePhase` ignoriert `ConflictPolicy` ausser "Skip"
**Tags:** Data-Integrity Risk · Bug
- [ ] **Fix umsetzen**
- **File:** [DatRenamePipelinePhase.cs](../../src/Romulus.Infrastructure/Orchestration/DatRenamePipelinePhase.cs#L26-L120)
- **Problem:** Bei `Rename`/`Overwrite` keine Kollisionsbehandlung -> Move-Phase respektiert Policy, DatRename nicht -> Preview/Execute-Divergenz.
- **Fix:** `ResolveDestinationByPolicy(targetPath, ConflictPolicy)` analog Move-Phase.
- **Tests fehlen:**
  - [ ] 2 Files mit kollidierendem DAT-TargetName + `ConflictPolicy=Rename` -> beide existieren mit Suffixen

### P1-14 — `LiteDbCollectionIndex` faellt nach Recovery-Race still in In-Memory-Modus
**Tags:** Data-Integrity Risk · Concurrency
- [ ] **Fix umsetzen**
- **File:** [LiteDbCollectionIndex.cs](../../src/Romulus.Infrastructure/Index/LiteDbCollectionIndex.cs#L491-L515)
- **Problem:** Bei zwei Prozessen auf korrupter DB landet einer im in-memory degraded mode -> Snapshots werden geschrieben aber NIE persistiert. UI/CLI erfaehrt nichts.
- **Fix:** Im `catch` echten Fehler hochmelden ODER `IsDegraded`-Property exponieren und in API/GUI sichtbar machen.
- **Tests fehlen:**
  - [ ] Zwei parallel oeffnende Indizes auf korrupter DB -> beide bekommen Persistenz oder einer wirft sichtbar

### P1-15 — SSRF ueber DAT-Katalog (keine Host-Allowlist)
**Tags:** Security Risk · OWASP A10
- [x] **Fix umsetzen**
- **Files:** [DatSourceService.cs](../../src/Romulus.Infrastructure/Dat/DatSourceService.cs#L62-L72), [DatSourceService.cs](../../src/Romulus.Infrastructure/Dat/DatSourceService.cs#L116-L124)
- **Problem:** `IsSecureUrl` prueft nur HTTPS. Keine Host-Allowlist, kein Private-IP/Loopback-Block, AutoRedirect ohne Re-Validation. Praeparierter Katalog kann interne HTTPS-Dienste probaen.
- **Fix:** Statische Allowlist (github.com, raw.githubusercontent.com, datomatic.no-intro.org, redump.org). Redirects manuell folgen + jeden Hop validieren. Loopback/RFC1918/Link-Local IPs hart blocken.
- **Tests fehlen:**
  - [ ] Praeparierter Katalog mit `https://127.0.0.1`, `https://10.x`, Redirect zu `169.254.169.254` -> alle ohne Connect/Read fehlschlagen

### P1-16 — HMAC-Tempfile ohne ACL beim Schreiben (Race)
**Tags:** Security Risk · OWASP A02
- [x] **Fix umsetzen**
- **File:** [AuditSigningService.cs](../../src/Romulus.Infrastructure/Audit/AuditSigningService.cs#L67-L97)
- **Problem:** Tempfile mit Default-ACL geschrieben, ACL-Haertung erst nach `File.Move`. Race-Fenster fuer fremden Prozess.
- **Fix:** Tempfile direkt mit restriktiver ACL erzeugen (Windows: `FileSecurity` ueber `FileSystemAclExtensions.Create`. Unix: `File.Create` ohne Inhalt + `File.SetUnixFileMode(0600)` + Schluessel schreiben).
- **Tests fehlen:**
  - [ ] Linux mit `umask 022`: Schluessel-Datei nie `OtherRead`

### P1-17 — Kein Hash-Chain / Replay-Schutz zwischen Audit-Sidecars
**Tags:** Audit Integrity Risk · Security · OWASP A04/A08
- [x] **Fix umsetzen**
- **Files:** [AuditSigningService.cs](../../src/Romulus.Infrastructure/Audit/AuditSigningService.cs#L131-L138), [AuditCsvStore.cs](../../src/Romulus.Infrastructure/Audit/AuditCsvStore.cs#L19)
- **Problem:** Payload `v1|<file>|<csv-sha>|<rows>|<utc>` enthaelt weder `keyId` noch Verweis auf vorherige Datei. Aelteres Sidecar+CSV kann an neuen Pfad kopiert werden -> akzeptiert.
- **Fix:** Payload um `previousSidecarHmac` und `keyId` erweitern. Append-only `audit-ledger`.
- **Tests fehlen:**
  - [ ] Aelteres Sidecar an neuen Pfad kopiert -> `VerifyMetadataSidecar` lehnt ab

### P1-18 — Audit-Rows zwischen Checkpoints sind ungeschuetzt
**Tags:** Audit Integrity Risk
- [x] **Fix umsetzen**
- **File:** [AuditCsvStore.cs](../../src/Romulus.Infrastructure/Audit/AuditCsvStore.cs#L82)
- **Problem:** Sidecar deckt nur Stand zum letzten Checkpoint. Tail-Rows sind unverankert -> Angreifer kann sie modifizieren bevor naechster Checkpoint laeuft.
- **Fix:** Spalten `Seq` (monoton) und `PrevRowSha256`. Verify prueft Luecken und Hash-Kette.
- **Tests fehlen:**
  - [ ] `AppendingRowsWithoutCheckpoint_ThenTampering_IsDetectedByVerify`

### P1-19 — Kein HSTS / HTTPS-Erzwingung im Remote-Modus
**Tags:** Security Risk · OWASP A02/A05
- [ ] **Fix umsetzen**
- **Files:** [Program.cs](../../src/Romulus.Api/Program.cs#L37-L42), [HeadlessApiOptions.cs](../../src/Romulus.Api/HeadlessApiOptions.cs#L53-L66)
- **Problem:** `WebHost.UseUrls($"http://...")` startet ausschliesslich Klartext. Kein `UseHttpsRedirection`, kein HSTS. Operator vergisst Reverse-Proxy -> X-Api-Key + Payload im Klartext im LAN.
- **Fix:** Im Remote-Modus Kestrel mit `ListenAnyIP(port, opts => opts.UseHttps(certPath))` ODER Start hart abbrechen ohne TLS-Konfig. HSTS-Header bei `AllowRemoteClients=true`.
- **Tests fehlen:**
  - [ ] `AllowRemoteClients=true` ohne TLS-Konfig -> Programmstart wirft `InvalidOperationException`

### P1-20 — CLI `convert`/`header` umgehen Allowed-Roots-Policy
**Tags:** Parity Risk · Security
- [ ] **Fix umsetzen**
- **Files:** [Program.cs](../../src/Romulus.CLI/Program.cs#L994), [Program.cs](../../src/Romulus.CLI/Program.cs#L1034)
- **Problem:** `romulus convert --input "C:\Windows\System32\drivers\etc\hosts"` ohne Path-Policy-Check. API-Pfade pruefen `AllowedRootPathPolicy`, CLI nicht.
- **Fix:** `AllowedRootPathPolicy` aus Settings auch in CLI erzwingen.
- **Tests fehlen:**
  - [ ] `CliConvert_WithPathOutsideAllowedRoots_Fails`

### P1-21 — CLI Exit-Code 4 ("Completed with errors") wird nirgendwo emittiert
**Tags:** Parity Risk · CI-Killer
- [ ] **Fix umsetzen**
- **File:** [Program.cs](../../src/Romulus.CLI/Program.cs#L33)
- **Problem:** Doc deklariert `4=Completed with errors`. `grep "return 4"` = 0 Treffer. Run mit Failures schliesst mit Exit 0 -> CI sieht nichts.
- **Fix:** `result.Failures > 0 ? 4 : 0` in `ExecuteRunCoreAsync`. `convert` analog.
- **Tests fehlen:**
  - [ ] `RunWithPartialFailures_ReturnsExitCode4`

### P1-22 — Tool-Hash-Cache umgehbar via Timestomping (in user-writable Tool-Roots)
**Tags:** Security Risk · OWASP A02
- [ ] **Fix umsetzen**
- **File:** [ToolRunnerAdapter.cs](../../src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs#L745-L774)
- **Problem:** Cache-Eintrag nur `(LastWriteTimeUtc, Length)`. Modifizierte Binary mit gleicher Groesse + zurueckgesetztem `LastWriteTime` matcht Cache. Betrifft `ROMULUS_CONVERSION_TOOLS_ROOT` und `LocalApplicationData` (psxtract, ciso, flips, xdelta3).
- **Fix:** Cheap-Preflight (erste/letzte 4 KB SHA256) zusaetzlich; ODER Cache nur pro Process-Lebenszyklus mit Re-Verify im ersten Call. Cache fuer user-writable Roots deaktivieren.
- **Tests fehlen:**
  - [ ] Bytes ueberschreiben + LastWriteTime resetten -> Re-Verify schlaegt fehl

---

## P2 – Mittlere Risiken

### P2-01 — `SafeRegex` Fail-Open: Junk-Klassifikation kann verschluckt werden
**Tags:** Determinism Risk
- [ ] **Fix umsetzen**
- **File:** [SafeRegex.cs](../../src/Romulus.Core/SafeRegex.cs#L23-L50)
- **Problem:** Bei `RegexMatchTimeoutException` liefert `IsMatch` `false`/`Match.Empty`. Pathological Input unter Last -> File gilt als „kein Junk" -> falsche Sortierung.
- **Fix:** Klassifikatorische Regexes bei Timeout in `Review` mit Reason `regex-timeout` schieben.
- **Tests fehlen:**
  - [ ] Synthetischer Pattern + Eingabe mit garantiertem Timeout -> deterministischer Endzustand

### P2-02 — `SafetyValidator` rejected `\\?\` Long-Path-Praefix komplett
**Tags:** Filesystem Edge Case
- [ ] **Fix umsetzen**
- **File:** [SafetyValidator.cs](../../src/Romulus.Infrastructure/Safety/SafetyValidator.cs#L101-L105)
- **Fix:** Praefix erkennen, abschaelen, intern ohne Praefix fuehren ODER klare Fehlermeldung.

### P2-03 — `IsSafeExtension` erlaubt mehrfach-Punkte (`.chd.exe`)
**Tags:** Security Edge Case
- [ ] **Fix umsetzen**
- **File:** [ConversionExecutor.cs](../../src/Romulus.Infrastructure/Conversion/ConversionExecutor.cs#L364-L383)
- **Fix:** Whitelist konkreter Extensions pro Tool aus Registry.

### P2-04 — `RunEnvironmentBuilder` 1.372 LoC (Builder + Resolver + RegEx + Settings-Loader)
**Tags:** Architecture Debt Hotspot
- [ ] **Refactor umsetzen**
- **File:** [RunEnvironmentBuilder.cs](../../src/Romulus.Infrastructure/Orchestration/RunEnvironmentBuilder.cs)
- **Fix:** Aufteilung `IDataDirectoryResolver`, `ISettingsLoader`, `IDatRootResolver`. DI-Registrierung in `AddRomulusCore()`/CLI-ServiceProvider.

### P2-05 — `EnrichmentPipelinePhase` 1.110 LoC mit 5 DAT-Hash-Stages + BIOS + Family + Streaming
**Tags:** Architecture Debt Hotspot
- [ ] **Refactor umsetzen**
- **File:** [EnrichmentPipelinePhase.cs](../../src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs)
- **Fix:** `IDatLookupService`, `IBiosResolver`, `ICrossConsoleDatPolicy` extrahieren.

### P2-06 — `ClassificationIoResolver` ist statischer Service Locator
**Tags:** Architecture Debt Hotspot
- [ ] **Fix umsetzen**
- **File:** [ClassificationIoResolver.cs](../../src/Romulus.Core/Classification/ClassificationIoResolver.cs#L7-L31)
- **Fix:** `IClassificationIo` per ctor in `ConsoleDetector`/`ContentSignatureClassifier`/`DiscHeaderDetector` injizieren.

### P2-07 — `MainWindow.xaml.cs` startet API per `Process.Start("dotnet run")`
**Tags:** Architecture Debt
- [ ] **Fix umsetzen**
- **File:** [MainWindow.xaml.cs](../../src/Romulus.UI.Wpf/MainWindow.xaml.cs#L317-L321)
- **Fix:** `IRomulusApiHost`-Service in Infrastructure (Kestrel oder Pfad zu `Romulus.Api.exe`); andernfalls Feature deaktivieren.

### P2-08 — Bootstrap-Idiom `ResolveDataDir + LoadSettings` 10+ mal kopiert
**Tags:** Duplicate Logic
- [ ] **Fix umsetzen**
- **Files:** API (4x), CLI (3x), WPF (3x)
- **Fix:** `IRomulusEnvironmentContext` als Singleton in DI.

### P2-09 — Konkurrierende Status-Modelle fuer Run/Job
**Tags:** Shadow Logic · Parity Risk
- [ ] **Fix umsetzen**
- **Files:** [RunState.cs](../../src/Romulus.UI.Wpf/Models/RunState.cs), [RunResultBuilder.cs](../../src/Romulus.Infrastructure/Orchestration/RunResultBuilder.cs#L13), [RunManager.cs](../../src/Romulus.Api/RunManager.cs#L323), [LiteDbCollectionIndex.cs](../../src/Romulus.Infrastructure/Index/LiteDbCollectionIndex.cs#L915), [AdvancedModels.cs](../../src/Romulus.Contracts/Models/AdvancedModels.cs#L12)
- **Fix:** `enum RunLifecycleStatus` in Contracts. WPF `RunState` als reine UI-Projektion.

### P2-10 — `FeatureService` (static) und `FeatureCommandService` (instance) parallel
**Tags:** Halbfertiger Refactor
- [ ] **Fix umsetzen**
- **Files:** `FeatureService.cs` + 8 Partials, `FeatureCommandService.cs` + 7 Partials
- **Fix:** Einen Stack waehlen, anderen aufloesen.

### P2-11 — Inline-Geschaeftslogik in API-Endpoints
**Tags:** Shadow Logic
- [ ] **Fix umsetzen**
- **Files:** [Program.cs](../../src/Romulus.Api/Program.cs#L546-L741), [Program.RunWatchEndpoints.cs](../../src/Romulus.Api/Program.RunWatchEndpoints.cs#L150-L410)
- **Fix:** `DatUpdateService.UpdateAsync()`, `RunCreateService.CreateFromRequestAsync()`, `ConvertEndpointService.RunAsync()`.

### P2-12 — `NormalizeConsoleKey` (Core) vs. `RxValidConsoleKey` (Infrastructure) — konkurrierende Wahrheit
**Tags:** Determinism Risk · Duplicate Logic
- [ ] **Fix umsetzen**
- **File:** [DeduplicationEngine.cs](../../src/Romulus.Core/Deduplication/DeduplicationEngine.cs#L184-L201)
- **Problem:** Core akzeptiert Unicode-Letter/Digit (arabisch-indische Ziffern, kyrillisch). Sorting whitelistet `[A-Z0-9_-]+`.
- **Fix:** Eine Validierung in Contracts.

### P2-13 — `FormatConverterAdapter.ConvertLegacy`-Pfad ist Refactor-Ueberbleibsel
**Tags:** Hygiene · Konsequenz von P0-01
- [ ] **Loeschen**
- **File:** [FormatConverterAdapter.cs](../../src/Romulus.Infrastructure/Conversion/FormatConverterAdapter.cs#L393)

### P2-14 — CSP `style-src 'nonce-...'` blockiert Inline-`style="..."`-Attribute
**Tags:** Bug · Security (Style-Injection-Risiko)
- [ ] **Fix umsetzen**
- **File:** [ReportGenerator.cs](../../src/Romulus.Infrastructure/Reporting/ReportGenerator.cs#L107)
- **Problem:** Inline-Styles brauchen `'unsafe-inline'` oder `style-src-attr 'unsafe-hashes'`. Bar-Chart kollabiert auf 0px in CSP3-strikten Browsern.
- **Fix:** Inline-Styles in `<style nonce="...">` mit eindeutigen Klassen pro Bar-Breite verschieben. CSP zusaetzlich `base-uri 'none'`, `form-action 'none'`, `frame-ancestors 'none'`.
- **Tests fehlen:**
  - [ ] Snapshot-Test: kein `style="` Substring im Output

### P2-15 — `FileHashService.FlushPersistentCache` nicht cross-process-atomar
**Tags:** Concurrency · Data-Integrity
- [ ] **Fix umsetzen**
- **File:** [FileHashService.cs](../../src/Romulus.Infrastructure/Hashing/FileHashService.cs#L161-L183)
- **Problem:** Zwei Prozesse auf Default-Cache -> lost updates. Keine Mutex-Bracketing wie `AuditCsvStore`.
- **Fix:** Cross-Process-Mutex einbauen; vor Schreiben Datei neu laden + mergen.
- **Tests fehlen:**
  - [ ] Stress: 2 Tasks schreiben disjunkte Hashes -> Vereinigung im JSON

### P2-16 — `HeaderlessHasher.ComputeN64CanonicalHash` doppelte Allokation, faengt OOM nicht
**Tags:** Resource · Bug
- [ ] **Fix umsetzen**
- **File:** [HeaderlessHasher.cs](../../src/Romulus.Infrastructure/Hashing/HeaderlessHasher.cs#L83-L101)
- **Fix:** `var normalized = bytes;` (Kopie weg). Groessenlimit z. B. 256 MB als harte Obergrenze + Log statt OOM.
- **Tests fehlen:**
  - [ ] Mock-FileStream 200 MB N64 -> Hash korrekt, Speicher < 1.5x

### P2-17 — `ScreenScraperMetadataProvider` HttpClient ohne Timeout
**Tags:** Resource · Concurrency
- [ ] **Fix umsetzen**
- **File:** [Program.cs](../../src/Romulus.Api/Program.cs#L46-L54)
- **Fix:** `IHttpClientFactory` mit `Timeout=TimeSpan.FromSeconds(30)` und `SocketsHttpHandler { PooledConnectionLifetime = 5min }`.
- **Tests fehlen:**
  - [ ] Mock-Handler `await Task.Delay(60_000)` -> wirft `TaskCanceledException` < 35s

### P2-18 — `RunManager.Dispose()` (synchron) cancelt aktive Runs nicht
**Tags:** Concurrency · Resource Leak
- [ ] **Fix umsetzen**
- **File:** [RunManager.cs](../../src/Romulus.Api/RunManager.cs#L98-L106)
- **Fix:** `_lifecycle.ShutdownAsync().GetAwaiter().GetResult()` mit Timeout ODER `IAsyncDisposable`-only.
- **Tests fehlen:**
  - [ ] TestServer mit aktivem DryRun -> `Dispose` (sync) -> CancellationToken `IsCancellationRequested`

### P2-19 — `SettingsService.SaveFrom` Cross-Process-Race korrumpiert `settings.json`
**Tags:** Concurrency
- [ ] **Fix umsetzen**
- **File:** [SettingsService.cs](../../src/Romulus.UI.Wpf/Services/SettingsService.cs#L389-L406)
- **Fix:** Tmp-Pfad mit `ProcessId + Guid`. Cross-Process-Mutex `Global\Romulus.Settings`. Backup `settings.json.bak` vor Migration.
- **Tests fehlen:**
  - [ ] 4 Tasks `SaveFrom` parallel -> finale Datei immer valides JSON

### P2-20 — `MovePipelinePhase` zaehlt `failCount` ohne Audit-Zeile bei `root-not-found`
**Tags:** Bug · Audit-Luecke
- [ ] **Fix umsetzen**
- **File:** [MovePipelinePhase.cs](../../src/Romulus.Infrastructure/Orchestration/MovePipelinePhase.cs#L60-L102)
- **Fix:** In Fail-Branches `AuditStore.AppendAuditRow(action="MOVE_FAILED", reason="root-not-found"|"path-resolve-failed")`.
- **Tests fehlen:**
  - [ ] Roots = `["C:\A"]`, Loser-Pfad `D:\foo.rom` -> exakt 1 Audit-Zeile

### P2-21 — Hardcoded deutsche Strings in WPF-Startup/Crash-Dialogen (i18n umgangen)
**Tags:** i18n Risk
- [ ] **Fix umsetzen**
- **Files:** [App.xaml.cs](../../src/Romulus.UI.Wpf/App.xaml.cs#L31-L37), [App.xaml.cs](../../src/Romulus.UI.Wpf/App.xaml.cs#L59-L64), [App.xaml.cs](../../src/Romulus.UI.Wpf/App.xaml.cs#L120-L125), [MainWindow.xaml.cs](../../src/Romulus.UI.Wpf/MainWindow.xaml.cs#L94-L100)
- **Fix:** Eingebetteter Mini-Resource mit `de`/`en`/`fr`-Tabelle ODER englischer Fallback.
- **Tests fehlen:**
  - [ ] Reflection-Scan auf MessageBox-Aufrufe

### P2-22 — Datenschemas ohne `schemaVersion`
**Tags:** False Confidence Risk
- [ ] **Fix umsetzen**
- **Files:** `data/consoles.json`, `data/console-maps.json`, `data/defaults.json`, `data/dat-catalog.json`, `data/format-scores.json`, `data/rules.json`, `data/ui-lookups.json`
- **Fix:** `schemaVersion` Pflichtfeld + Loader-Versionsgate + sichtbare Warnung bei unbekannter Version.
- **Tests fehlen:**
  - [ ] `LoadConsoles_WithMissingSchemaVersion_LogsWarning`
  - [ ] `LoadFormatScores_WithFutureSchemaVersion_FailsExplicitly`

### P2-23 — API-Request-Logging anfaellig fuer Log-Injection via URL-Pfad
**Tags:** Audit Integrity Risk · Log Injection · OWASP A09/A03
- [ ] **Fix umsetzen**
- **File:** [Program.cs](../../src/Romulus.Api/Program.cs#L233)
- **Problem:** `GET /healthz%0a%5bAUDIT%5d...` -> Logger schreibt zwei Zeilen. `correlationId` ist sanitized, `path` nicht.
- **Fix:** `SafeConsoleWriteLine` `\r\n\t` ersetzen ODER JSONL-Logging zentral.
- **Tests fehlen:**
  - [ ] `RequestLog_WithEncodedNewlinesInPath_DoesNotEmitMultilineEntry`

### P2-24 — `LocalizationService`-Fallback inkonsistent (DE-only Keys verschwinden fuer EN)
**Tags:** i18n Risk
- [ ] **Fix umsetzen**
- **File:** [LocalizationService.cs](../../src/Romulus.UI.Wpf/Services/LocalizationService.cs#L57-L80)
- **Problem:** Code-Comment sagt „DE base + overlay", Code nutzt EN als Base.
- **Fix:** Entscheiden + dokumentieren. Build-Time-Lint, dass alle Sprachdateien identische Key-Sets haben.
- **Tests fehlen:**
  - [ ] `Localization_AllLocalesHaveIdenticalKeySet`

### P2-25 — `JsonlLogWriter` keine Groessenbegrenzung pro Aufruf, kein Auto-Rotate
**Tags:** False Confidence Risk · Resource
- [ ] **Fix umsetzen**
- **File:** [JsonlLogWriter.cs](../../src/Romulus.Infrastructure/Logging/JsonlLogWriter.cs#L97-L127)
- **Fix:** In `Write` periodisch `RotateIfNeeded` automatisch triggern. Message auf 16 KB cappen mit `… [truncated]`-Suffix.
- **Tests fehlen:**
  - [ ] `JsonlLog_AfterMaxBytes_AutoRotates`
  - [ ] `JsonlLog_VeryLongMessage_IsTruncated`

### P2-26 — `MainWindow.OnClosing` ist `async void` -> unbeobachtete Exceptions killen App
**Tags:** False Confidence Risk · Concurrency
- [ ] **Fix umsetzen**
- **File:** [MainWindow.xaml.cs](../../src/Romulus.UI.Wpf/MainWindow.xaml.cs#L148)
- **Fix:** Methodenrumpf in try/catch wrappen mit Notfall-`crash.log`-Pfad. Settings vor Close immer flushen.
- **Tests fehlen:**
  - [ ] `OnClosing_WhenSaveSettingsThrows_StillReleasesResources`

### P2-27 — `ProcessStartInfo` erbt komplettes Eltern-Environment (DLL-Hijack / `LD_PRELOAD`)
**Tags:** Security Risk · OWASP A04/A08
- [ ] **Fix umsetzen**
- **File:** [ToolRunnerAdapter.cs](../../src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs#L375-L388)
- **Fix:** `psi.EnvironmentVariables.Clear()`. Nur explizit `SystemRoot, windir, ComSpec, TEMP, PATH` durchreichen. `WorkingDirectory` auf frischen Tempordner. Windows: `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)`.
- **Tests fehlen:**
  - [ ] `LD_PRELOAD=/tmp/evil.so` -> Child-Env enthaelt es nicht

### P2-28 — Sensible Pfade landen ueber `_log` in Audit-/Konsolen-Logs
**Tags:** Security Risk · OWASP A09/A03
- [ ] **Fix umsetzen**
- **Files:** [AuditSigningService.cs](../../src/Romulus.Infrastructure/Audit/AuditSigningService.cs#L388-L399), [Program.cs](../../src/Romulus.Api/Program.cs#L221-L225)
- **Fix:** Zentrale `LogPathRedactor.Redact(path)` (analog `RedactAbsolutePaths` in `ToolRunnerAdapter`). `\r\n\t` aus User-Strings entfernen.
- **Tests fehlen:**
  - [ ] Audit-Zeile mit `OldPath="C:\\Users\\bob\\\nFAKE"` -> Logsenke erhaelt eine Zeile, redigiert

### P2-29 — `JsonDocument.Parse` fuer Request-Bodies ohne explizites `MaxDepth`
**Tags:** Security · OWASP A03/A05
- [ ] **Fix umsetzen**
- **Files:** [Program.cs](../../src/Romulus.Api/Program.cs#L450-L465), [Program.cs](../../src/Romulus.Api/Program.cs#L519-L533)
- **Fix:** `new JsonDocumentOptions { MaxDepth = 8, AllowTrailingCommas = false }` ODER auf source-generated `ApiJsonSerializerOptions` migrieren.
- **Tests fehlen:**
  - [ ] Body mit 100 verschachtelten `[`-Objekten -> 400, kein 500

### P2-30 — Key-Datei wird ohne Berechtigungspruefung gelesen
**Tags:** Security · OWASP A02
- [ ] **Fix umsetzen**
- **File:** [AuditSigningService.cs](../../src/Romulus.Infrastructure/Audit/AuditSigningService.cs#L42-L52)
- **Problem:** Aeltere Versionen / `xcopy /O` ohne `/X` koennen `Romulus.hmac` mit `0644` hinterlassen -> kommentarlose Verwendung.
- **Fix:** Vor Verwendung Linux: `File.GetUnixFileMode(path)` pruefen. Windows: `FileInfo.GetAccessControl()` enumerieren. Bei Verstoss fail-closed.
- **Tests fehlen:**
  - [ ] Test mit „lockerer" Key-Datei -> Service muss laden ablehnen

---

## P3 – Wartbarkeit / Niedrige Risiken

### P3-01 — Mojibake `�` Literal im HTML-Report-Header
**Tags:** Encoding-Bug · Cosmetic
- [ ] **Fix umsetzen**
- **File:** [ReportGenerator.cs](../../src/Romulus.Infrastructure/Reporting/ReportGenerator.cs#L36)
- **Fix:** UTF-8 mit BOM speichern, Zeichen ersetzen. Snapshot-Test des Headers ergaenzen.

### P3-02 — Default-`MainViewModel`-ctor erzeugt Services manuell
**Tags:** Architecture Debt
- [ ] **Fix umsetzen**
- **File:** [MainViewModel.cs](../../src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs#L70)
- **Fix:** WPF auf `Microsoft.Extensions.DependencyInjection` umstellen.

### P3-03 — `RunOrchestrator.Dispose` setzt nur Flag, kein async-Shutdown
**Tags:** Architecture Debt
- [ ] **Fix umsetzen**
- **File:** [RunManager.cs](../../src/Romulus.Api/RunManager.cs#L102-L111)

### P3-04 — Magic-Status-Strings statt Konstanten/Enum
**Tags:** Hygiene
- [ ] **Fix umsetzen**
- **Files:** [AdvancedModels.cs](../../src/Romulus.Contracts/Models/AdvancedModels.cs#L12), [MainViewModel.cs](../../src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs#L51-L54)

### P3-05 — `DatCatalogViewModel` laedt Catalog dreifach in einer Klasse
**Tags:** Hygiene · zusammen mit P1-03
- [ ] **Fix umsetzen** (mit P1-03)
- **File:** [DatCatalogViewModel.cs](../../src/Romulus.UI.Wpf/ViewModels/DatCatalogViewModel.cs#L145)

### P3-06 — Reparse-Point `MaxAncestryDepth = 256` nur symbolisch
**Tags:** Hygiene
- [ ] **Fix umsetzen**
- **File:** [SafetyValidator.cs](../../src/Romulus.Infrastructure/Safety/SafetyValidator.cs#L11)
- **Fix:** Visited-Set ueber bereits besuchte Full-Paths.

### P3-07 — Test `SelectWinner_IsDeterministic` ist Alibi-Test
**Tags:** False Confidence Risk · Test-Hygiene
- [x] **Fix umsetzen**
- **File:** [DeduplicationEngineTests.cs](../../src/Romulus.Tests/DeduplicationEngineTests.cs#L127-L143)
- **Fix:** Echte Permutation einbauen ODER umbenennen `_AreIdempotent`.

### P3-08 — Test `IsValidTransition_SkipPhases_Valid` erlaubt unrealistische Spruenge
**Tags:** False Confidence Risk · Test-Hygiene
- [ ] **Fix umsetzen**
- **File:** [WpfNewTests.cs](../../src/Romulus.Tests/WpfNewTests.cs#L632-L639)

### P3-09 — `RunManager.Cancel` weckt `WaitForCompletion` nicht (Latenz im Hashing)
**Tags:** Concurrency Edge Case
- [ ] **Fix umsetzen**
- **File:** [RunLifecycleManager.cs](../../src/Romulus.Api/RunLifecycleManager.cs#L181-L201)
- **Fix:** Stream-Hashing in 64 KB-Chunks mit Token-Check.
- **Tests fehlen:**
  - [ ] Cancel waehrend simulierter 30-s-Hash-Operation -> WaitForCompletion < 2s

### P3-10 — `FormatScorer.GetRegionScore`: leere/lange `preferOrder` -> Score-Inversion
**Tags:** False Confidence
- [x] **Fix umsetzen**
- **File:** [FormatScorer.cs](../../src/Romulus.Core/Scoring/FormatScorer.cs#L246)
- **Fix:** `Math.Max(1, 1000 - idx)`. Default-Reihenfolge bei leerem `preferOrder`.
- **Tests fehlen:**
  - [x] `RegionScore_WithEmptyPreferOrder_ReturnsDocumentedDefault`
  - [x] `RegionScore_WithMoreThan1000Preferences_NeverGoesNegative`

### P3-11 — `/dashboard/bootstrap` anonym (Information Disclosure)
**Tags:** Security · OWASP A05
- [ ] **Fix umsetzen**
- **Files:** [ProgramHelpers.cs](../../src/Romulus.Api/ProgramHelpers.cs#L124-L128), [DashboardDataBuilder.cs](../../src/Romulus.Api/DashboardDataBuilder.cs#L15-L32)
- **Fix:** Hinter API-Key ziehen ODER Response auf `{ "version": "x.y" }` reduzieren bei `AllowRemoteClients=true`.
- **Tests fehlen:**
  - [ ] GET /dashboard/bootstrap ohne API-Key im Remote-Modus -> 401

### P3-12 — `force`-Flag in `/dats/update` wirft `InvalidOperationException` bei Nicht-Bool
**Tags:** Security · OWASP A05
- [ ] **Fix umsetzen**
- **File:** [Program.cs](../../src/Romulus.Api/Program.cs#L460-L469)
- **Fix:** `forceProp.ValueKind == JsonValueKind.True` ODER `TryGetBoolean`. Catch erweitern.
- **Tests fehlen:**
  - [ ] Body `{"force":"yes"}` -> 400, nicht 500

---

## Round 3 – Neue Funde

> Quellen: parallele Audits 3a (DAT/Hash/Tools – `R3-A-*`), 3b (Settings/Localization/UI – `R3-B-*`), 3c (API/CLI/Reports/Logging – `R3-C-*`).
> Insgesamt 44 zusaetzliche Funde.

### Block A – DAT / Hash / Tools / Conversion (`R3-A-*`)

#### R3-A-01 — ZIP CRC32 Fast-Path vertraut zentralem Verzeichnis ungeprueft (P0)
**Tags:** Release-Blocker · Verify Fail-Open · Determinism
- [ ] **Fix umsetzen** (gleiche Klasse wie P0-01)
- **File:** [ArchiveHashService.cs](../../src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs#L200-L210) (`HashZipEntries`, `useZipCrc32FastPath`)
- **Problem:** Bei `hashType == "CRC"|"CRC32"` wird `entry.Crc32` direkt aus dem ZIP-Central-Directory als „Hash" zurueckgegeben, ohne den entpackten Stream je gegen die CRC zu pruefen. Das CRC-Feld ist Metadaten und vom Angreifer frei waehlbar. Gerade MAME/FBNeo-DATs sind CRC32-only -> jede DAT-Match-Entscheidung kann erzwungen werden.
- **Fix:** Fast-Path entfernen ODER entpackten Stream zusaetzlich gegen `entry.Crc32` validieren.
- **Tests fehlen:**
  - [ ] ZIP mit gefaelschter CRC32 im Central-Directory -> `HashZipEntries` darf nicht den Header-Wert zurueckgeben
  - [ ] Property: `centralDirCrc == StreamCrc`, sonst Reject

#### R3-A-02 — `ContentSignatureClassifier` markiert ROMs faelschlich als BMP/MP3 (P1)
**Tags:** Junk-Misclassification · Determinism
- [x] **Fix umsetzen**
- **File:** [ContentSignatureClassifier.cs](../../src/Romulus.Core/Classification/ContentSignatureClassifier.cs#L46-L53)
- **Problem:** BMP-Erkennung nur 2 Byte (`0x42 0x4D` = "BM"). MP3 nur Frame-Sync `0xFF (b1 & 0xE0)==0xE0` -> matcht ROMs mit `0xFF`-Padding (typisch SNES/MD).
- **Fix:** BMP zusaetzlich `BinaryPrimitives.ReadUInt32LE(header[2..6]) == fileSize`. MP3-Sync um Bitrate-/Samplerate-Index validieren.
- **Tests fehlen:**
  - [x] SNES-ROM `0xFF 0xE2 ...` -> nicht MP3
  - [x] Plausibilitaets-Regression fuer BMP/MP3-Header

#### R3-A-03 — DatIndex mischt heterogene Hash-Typen ohne Tag (P1)
**Tags:** Determinism · Cross-Console False-Match · Architecture Debt
- [x] **Fix umsetzen**
- **Files:** [DatRepositoryAdapter.cs](../../src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs#L266-L307), [DatIndex.cs](../../src/Romulus.Contracts/Models/DatIndex.cs#L100-L115)
- **Problem:** Pro ROM-Zeile wird je nach Verfuegbarkeit SHA1 -> MD5 -> CRC32 in dieselbe Map indiziert. `LookupEntry` weiss nicht, was es speichert -> Type-Pollution.
- **Fix:** `DatIndexEntry.HashType`-Feld + `Lookup(consoleKey, hashType, hash)` filtert auf gleichen Typ.
- **Tests fehlen:**
  - [x] DAT mit MD5(X), andere DAT mit gleichem 32-Hex-String als CRC32 -> getrennte Lookups

#### R3-A-04 — `HeaderRepairService` ueberschreibt Backup-Datei mit korruptem Inhalt (P1)
**Tags:** Data-Integrity · Audit-Luecke
- [ ] **Fix umsetzen**
- **File:** [HeaderRepairService.cs](../../src/Romulus.Infrastructure/Hashing/HeaderRepairService.cs#L48)
- **Problem:** `CopyFile(path, path+".bak", overwrite:true)` ueberschreibt vorhandenes Backup. Sequenz: erster Repair crasht halb-geschrieben -> zweiter Repair ersetzt Original-`.bak` mit Korruption.
- **Fix:** Vor Ueberschreiben SHA256-Vergleich; sonst `.bak.{utc}.bak`. Pflicht-Verify nach Schreiben.
- **Tests fehlen:**
  - [ ] Zweimal Repair -> `.bak` enthaelt Original

#### R3-A-05 — `ChdmanToolConverter.ConvertMultiCueArchive` ignoriert effektives chdman-Subcommand pro Disc (P1)
**Tags:** Conversion-Bug · Determinism · Parity
- [ ] **Fix umsetzen**
- **File:** [ChdmanToolConverter.cs](../../src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs#L210-L235)
- **Problem:** Single-CUE-Pfad nutzt `ResolveEffectiveChdmanCommand`, Multi-CUE-Pfad nicht -> Mischarchive (PSX-CD + PS2-DVD) werden mit falschem Subcommand verarbeitet.
- **Fix:** `effectiveCommand` in der `for`-Schleife pro CUE neu aufloesen.
- **Tests fehlen:**
  - [ ] Multi-CUE mit gemischtem Disc-Typ

#### R3-A-06 — `ConversionOutputValidator.ValidateMagicHeader` nie aufgerufen (P1)
**Tags:** Verify Fail-Open · Hygiene
- [ ] **Fix umsetzen**
- **File:** [ConversionOutputValidator.cs](../../src/Romulus.Infrastructure/Conversion/ConversionOutputValidator.cs#L88-L112)
- **Problem:** Magic-Tabelle existiert, wird aber von `TryValidateCreatedOutput` nie aufgerufen -> 6-Byte-Output mit Muell-Inhalt gilt als „valid".
- **Fix:** In `TryValidateCreatedOutput` Magic-Check ergaenzen, Mindest-Header-Bytes hochziehen.
- **Tests fehlen:**
  - [ ] 6-Byte `00`-Output bei `.zip` -> false

#### R3-A-07 — DatRepositoryAdapter 7z-Extraktion folgt Junctions in Sub-Verzeichnissen (P1)
**Tags:** Security · Path Traversal · OWASP A01
- [ ] **Fix umsetzen**
- **File:** [DatRepositoryAdapter.cs](../../src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs#L539-L560) (`TryParse7zDat`)
- **Problem:** `Directory.GetFiles(tempDir, ..., AllDirectories)` ohne Reparse-Point-Pre-Check, anders als `ArchiveHashService`. Junction-Eintrag im 7z laesst Walk nach `C:\` rekursieren.
- **Fix:** Pre-Walk auf Reparse-Points; ODER `7z -snl-`.
- **Tests fehlen:**
  - [ ] 7z mit Verzeichnis-Junction -> Parse leer

#### R3-A-08 — `ExtractZipSafe` Zip-Bomb-Pruefung trustet `entry.Length` (P1)
**Tags:** Security · DoS · OWASP A05
- [ ] **Fix umsetzen**
- **File:** [ChdmanToolConverter.cs](../../src/Romulus.Infrastructure/Conversion/ChdmanToolConverter.cs#L264-L295) (`ExtractZipSafe`)
- **Problem:** Compression-Ratio-Check + `totalUncompressed` basieren auf Central-Directory-Werten (Angreifer-kontrolliert). Bombs mit gefaelschter `Length` umgehen Cap.
- **Fix:** Stream-Extraktion mit Live-Bytecounter + harter Cap.
- **Tests fehlen:**
  - [ ] Bomb mit `centralDir.uncompressed_size = 1024` aber 1 GB Output -> Abbruch

#### R3-A-09 — `HeaderlessHasher` mappt CRC32 still auf SHA1 (P1)
**Tags:** Determinism · Verify Fail-Open · DAT-Mismatch
- [x] **Fix umsetzen**
- **File:** [HeaderlessHasher.cs](../../src/Romulus.Infrastructure/Hashing/HeaderlessHasher.cs#L116-L122)
- **Problem:** Switch hat keinen `CRC`/`CRC32`-Branch -> `_ => SHA1.Create()`. Headerless-Anforderung mit CRC32 liefert SHA1-Hex, kein DAT-Match moeglich.
- **Fix:** `if (NormalizeHashType(hashType) is "CRC" or "CRC32") return Crc32.HashStream(fs);` vor Branch.
- **Tests fehlen:**
  - [x] `ComputeHeaderlessHash(nesFile, "NES", "CRC32")` -> 8-Hex-String

#### R3-A-10 — `ToolRunnerAdapter.ReadToEndWithByteBudget` liest weiter nach Truncation, ignoriert CT (P2)
**Tags:** DoS · Resource Leak · Concurrency
- [ ] **Fix umsetzen**
- **File:** [ToolRunnerAdapter.cs](../../src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs#L535-L580)
- **Problem:** Nach Truncation `continue;` ohne `ct.IsCancellationRequested`-Check -> Reader-Task verschlingt unbegrenzt Bytes; blockiert `Process.Dispose`.
- **Fix:** CancellationToken in Methodensignatur, im Inner-Loop pruefen, bei `2*maxBytes` `Process.Kill(entireProcessTree:true)`.
- **Tests fehlen:**
  - [ ] Tool emittiert 1 GB stdout -> Reader < 5s nach Cancel

#### R3-A-11 — DatRepositoryAdapter Fallback-Chain falsch verschachtelt (P2)
**Tags:** Bug · DAT-Lookup · Determinism
- [x] **Fix umsetzen**
- **File:** [DatRepositoryAdapter.cs](../../src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs#L283-L307)
- **Problem:** `is not` verschachtelte Bedingungen: SHA1-Request bekommt MD5-Eintrag mit `selectedHashType="SHA1"` -> Index semantisch falsch.
- **Fix:** Klare Tabelle pro `requestedHashType` mit Fallback-Liste; `selectedHashType` korrekt setzen + Warning.

#### R3-A-12 — `ToolRunnerAdapter.RunProcess` ignoriert `WaitForExit(10s)`-Returnwert (P2)
**Tags:** Bug · Concurrency
- [ ] **Fix umsetzen**
- **File:** [ToolRunnerAdapter.cs](../../src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs#L468-L483)
- **Problem:** Bool-Return ignoriert -> `process.ExitCode` wirft `InvalidOperationException` wenn Child detacht.
- **Fix:** `if (!process.WaitForExit(10_000)) { TryTerminate(...); return BuildFailureOutput(...); }`.

#### R3-A-13 — `GetDatGameKey` Trim-Reihenfolge bricht Whitespace-Variationen (P3)
**Tags:** Determinism · Edge Case
- [x] **Fix umsetzen**
- **File:** [DatRepositoryAdapter.cs](../../src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs#L88-L92)
- **Fix:** `$"{console.Trim()}|{gameName.Trim()}".ToLowerInvariant()`.

#### R3-A-14 — SNES-Copier-Detektion via `% 1024 == 512` ohne Header-Validierung (P2)
**Tags:** Determinism · DAT-Mismatch · Bug
- [x] **Fix umsetzen**
- **File:** [HeaderSizeMap.cs](../../src/Romulus.Core/Classification/HeaderSizeMap.cs#L29)
- **Problem:** Heuristik laesst legitime SNES-ROMs ohne Copier-Header faelschlich Headerless-Skip bekommen -> No-Intro-CRC matcht nicht.
- **Fix:** SNES-Copier-Check an LoROM/HiROM-Internal-Header-Validierung knuepfen.

#### R3-A-15 — GBA/GB-Detektion auf Basis nur 4 Logo-Bytes (P2)
**Tags:** False-Classification · Determinism
- [x] **Fix umsetzen**
- **File:** [CartridgeHeaderDetector.cs](../../src/Romulus.Core/Classification/CartridgeHeaderDetector.cs#L107-L135)
- **Problem:** 4-Byte-Match `24 FF AE 51` (GBA) bzw. `CE ED 66 66` (GB) erzeugt False-Positives bei beliebigen .bin-Dateien.
- **Fix:** Vollstaendige 156-Byte-/48-Byte-Logo-Validierung ODER Nintendo-Pruefsumme `0x134..0x14C`.

#### R3-A-16 — DatRepositoryAdapter waehlt nichtdeterministisch bei mehreren inneren `.dat`/`.xml` (P3)
**Tags:** Determinism · Bug
- [x] **Fix umsetzen**
- **File:** [DatRepositoryAdapter.cs](../../src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs#L548-L555)
- **Fix:** Multi-DAT mergen ODER explizit warnen.

---

### Block B – Settings / Localization / UI / Schemas (`R3-B-*`)

#### R3-B-01 — `SettingsService.Load` ueberschreibt gemergte Defaults mit hardcoded Fallbacks (P1)
**Tags:** Settings · Single-Source-of-Truth · Defaults Regression
- [ ] **Fix umsetzen**
- **File:** [SettingsService.cs](../../src/Romulus.UI.Wpf/Services/SettingsService.cs#L60-L185)
- **Problem:** Doppelter Lade-Pfad: erst sauberer `SettingsLoader.LoadWithExplicitUserPath`, dann erneuter `JsonDocument.Parse`-Walk mit hardcoded Defaults (`"Info"`, `false`, `Models.ConflictPolicy.Rename`). User-`general:{}` wirft alle defaults.json-Werte um.
- **Fix:** Zweiten JSON-Walk entfernen; ausschliesslich aus `mergedCoreSettings` mappen.
- **Tests fehlen:**
  - [ ] Defaults mit non-default Werten + `general:{}` -> Defaults bleiben
  - [ ] Property-Test pro `SettingsDto`-Property: Save->Load Roundtrip

#### R3-B-02 — `App.OnStartup` faengt `AbandonedMutexException` nicht (P2)
**Tags:** Startup · Single-Instance · Recovery
- [ ] **Fix umsetzen**
- **File:** [App.xaml.cs](../../src/Romulus.UI.Wpf/App.xaml.cs#L29-L43)
- **Problem:** Nach Hard-Kill der Vorgaenger-Instanz wirft Mutex-Konstruktor `AbandonedMutexException` *vor* dem `try`. App stuerzt mit Generic-Crash-Dialog.
- **Fix:** `try { new Mutex(true,name,out cn); } catch (AbandonedMutexException) { /* retry */ }`.

#### R3-B-03 — `LocalizationService.LoadStrings` synchrone Disk-IO bei jedem `SetLocale` (P2)
**Tags:** UI Thread · Performance
- [ ] **Fix umsetzen**
- **File:** [LocalizationService.cs](../../src/Romulus.UI.Wpf/Services/LocalizationService.cs#L57-L78)
- **Fix:** In-Memory-Cache; async Load.

#### R3-B-04 — `LocalizationService` Default-Locale-Drift (DE Code, EN Base) (P2)
**Tags:** Doc Drift · i18n
- [ ] **Fix umsetzen**
- **File:** [LocalizationService.cs](../../src/Romulus.UI.Wpf/Services/LocalizationService.cs#L1-L22)
- **Fix:** Doc + Code synchronisieren; Base-Locale klar definieren.

#### R3-B-05 — Overlay vernichtet leere/Whitespace-Translationen statt Fallback (P2)
**Tags:** i18n · Fallback Robustness
- [ ] **Fix umsetzen**
- **File:** [LocalizationService.cs](../../src/Romulus.UI.Wpf/Services/LocalizationService.cs#L73-L77)
- **Fix:** `if (!string.IsNullOrWhiteSpace(kv.Value)) baseDict[kv.Key] = kv.Value;`.

#### R3-B-06 — `LibraryReportView` async-void Lambda als Click-Handler (P2)
**Tags:** WPF · async void · Crash Risk
- [ ] **Fix umsetzen**
- **File:** [LibraryReportView.xaml.cs](../../src/Romulus.UI.Wpf/Views/LibraryReportView.xaml.cs#L18-L20)
- **Fix:** Wrapper-Methode mit try/catch; ODER Command-Binding.

#### R3-B-07 — `IValueConverter.ConvertBack` werfen `NotSupportedException` (P2)
**Tags:** WPF · Converters · Binding Errors
- [ ] **Fix umsetzen**
- **File:** [Converters.cs](../../src/Romulus.UI.Wpf/Converters/Converters.cs#L62-L189)
- **Fix:** `ConvertBack => Binding.DoNothing` ODER alle Bindings auf `Mode=OneWay` zwingen.

#### R3-B-08 — Hardcoded Brushes in 12+ Views umgehen Theme-/HighContrast-Switch (P2)
**Tags:** Theming · Accessibility · ResourceDictionary Bypass
- [ ] **Fix umsetzen**
- **Files:** DatAuditView, DatCatalogView, ToolsView, ResultView, WizardView, ExternalToolsView, CommandBar
- **Fix:** Alle Hex-Literale auf `{DynamicResource Brush*}` umstellen.

#### R3-B-09 — `SettingsService.version` ohne Migrations-Pfad (P2)
**Tags:** Settings · Schema Versioning · Data Loss
- [ ] **Fix umsetzen**
- **File:** [SettingsService.cs](../../src/Romulus.UI.Wpf/Services/SettingsService.cs#L19-L88)
- **Fix:** Migrations-Tabelle 0->1->2 sequentiell. Bei `fileVersion > Current`: Backup `.v{N}.bak`, Save deaktivieren.

#### R3-B-10 — `SettingsService.Load` aktualisiert `LastAuditPath`/`LastTheme` im Outer-Catch nicht (P2)
**Tags:** Settings · Restart Recovery
- [ ] **Fix umsetzen**
- **File:** [SettingsService.cs](../../src/Romulus.UI.Wpf/Services/SettingsService.cs#L185-L192)
- **Fix:** Properties direkt aus `dto` lesen ODER im Catch-Pfad zusaetzlich setzen.

#### R3-B-11 — `data/format-scores.json` mehrdeutige/kollidierende Extensions (P2)
**Tags:** Data Schema · Classification Drift · Junk Detection
- [ ] **Fix umsetzen**
- **File:** [data/format-scores.json](../../data/format-scores.json#L41-L100)
- **Problem:** `.app`, `.dmg`, `.img`, `.zip`, `.7z`, `.rar`, `.bs` als ROM gescort -> macOS-Bundles/Disk-Images werden als ROM erkannt.
- **Fix:** Schema um `family`/`requiresHashVerification` erweitern. Container-Extensions raus aus `formatScores`.

#### R3-B-12 — `LocalizationService.SetLocale` raised PropertyChanged nicht fuer `AvailableLocales` (P3)
**Tags:** WPF · INotifyPropertyChanged Coverage
- [ ] **Fix umsetzen**
- **File:** [LocalizationService.cs](../../src/Romulus.UI.Wpf/Services/LocalizationService.cs#L36-L46)

#### R3-B-13 — `data/builtin-profiles.json` Schema-Versionierung auf falscher Ebene (P3)
**Tags:** Data Schema · Versioning
- [ ] **Fix umsetzen**
- **File:** `data/builtin-profiles.json`
- **Fix:** Top-Level `{ "schemaVersion": 1, "profiles": [...] }`; Per-Item-`version` umbenennen oder entfernen.

---

### Block C – API / CLI / Reports / Logging (`R3-C-*`)

#### R3-C-01 — `RateLimiter` Bucket-Dictionary unbeschraenkt (DoS via X-Client-Id Spam) (P1)
**Tags:** Security · DoS · API
- [ ] **Fix umsetzen**
- **File:** [RateLimiter.cs](../../src/Romulus.Api/RateLimiter.cs#L30-L80)
- **Problem:** Eviction nur alle 5min. Rotierende `X-Client-Id` blaeht Dictionary auf Millionen Eintraege.
- **Fix:** `MaxBuckets` Cap (z. B. 10_000) + LRU-Eviction synchron beim Add.

#### R3-C-02 — `JsonlLogRotation` 1-Sekunden-Stamp-Kollision verliert Logzeilen (P1)
**Tags:** Logging · Data-Loss · Race
- [ ] **Fix umsetzen**
- **File:** [JsonlLogWriter.cs](../../src/Romulus.Infrastructure/Logging/JsonlLogWriter.cs#L195-L215)
- **Fix:** `yyyyMMdd-HHmmssfff` + Counter-Suffix; Logging-Fehler ueber `IDiagnosticSink`.

#### R3-C-03 — `ReportGenerator.WriteHtmlToFile` / `WriteJsonToFile` nicht atomar (P1)
**Tags:** Reporting · Data-Integrity · Atomicity
- [x] **Fix umsetzen**
- **File:** [ReportGenerator.cs](../../src/Romulus.Infrastructure/Reporting/ReportGenerator.cs#L188-L214), [RunReportWriter.cs](../../src/Romulus.Infrastructure/Reporting/RunReportWriter.cs#L155-L165)
- **Fix:** Tmp-Write + atomic-Rename Muster wie bei P0-05.

#### R3-C-04 — CLI `--log` Pfad bypasst `AllowedRoots`-Politik (P1)
**Tags:** CLI · Security · Path-Traversal · Parity
- [ ] **Fix umsetzen**
- **Files:** [Program.cs](../../src/Romulus.CLI/Program.cs#L240-L260), [JsonlLogWriter.cs](../../src/Romulus.Infrastructure/Logging/JsonlLogWriter.cs#L1-L60)
- **Problem:** `--log` UNC/Systempfad ungeprueft.
- **Fix:** `SafetyValidator.EnsureSafeOutputPath` im CLI-Parser; UNC/Reparse/Sys-Verzeichnis ablehnen.

#### R3-C-05 — SSE-Writer ohne Per-Write-Timeout -> Slow-Consumer DoS (P1)
**Tags:** API · DoS · SSE · Backpressure
- [ ] **Fix umsetzen**
- **Files:** [ProgramHelpers.cs](../../src/Romulus.Api/ProgramHelpers.cs#L88-L100), [Program.RunWatchEndpoints.cs](../../src/Romulus.Api/Program.RunWatchEndpoints.cs#L876-L955)
- **Fix:** `WriteSseEvent` mit linked CTS (5s Timeout + RequestAborted).

#### R3-C-06 — `/runs` POST: Case-Insensitive + Duplicate-Property Last-Wins-Bypass (P1)
**Tags:** API · Validation · Injection · JSON
- [ ] **Fix umsetzen**
- **Files:** [Program.RunWatchEndpoints.cs](../../src/Romulus.Api/Program.RunWatchEndpoints.cs#L171-L195)
- **Problem:** `roots` + `Roots` doppelt -> last-property-wins bypassed Proxy/WAF-Filter.
- **Fix:** Source-gen `JsonSerializerContext`; custom converter rejected Duplicate-Property mit 400.

#### R3-C-07 — `AuditCsvStore.CountAuditRows` zaehlt physische Zeilen (False-Positive TAMPERED) (P2)
**Tags:** Audit · Data-Integrity · CSV-Parsing
- [x] **Fix umsetzen**
- **File:** [AuditCsvStore.cs](../../src/Romulus.Infrastructure/Audit/AuditCsvStore.cs#L170-L195)
- **Problem:** `File.ReadLines.Count() - 1` zaehlt physische Zeilen. Quoted multi-line Field -> Sidecar-Count != Reader-Count -> falscher TAMPERED-Alarm.
- **Fix:** Ueber `AuditCsvParser.ParseCsvLine` mit Quote-State zaehlen.

#### R3-C-08 — `ReportGenerator.LoadReportLocale` Single-Slot-Cache + kein Size-Limit (P2)
**Tags:** Reporting · Performance · DoS
- [ ] **Fix umsetzen**
- **File:** [ReportGenerator.cs](../../src/Romulus.Infrastructure/Reporting/ReportGenerator.cs#L413-L462)
- **Fix:** LRU-Cache (Cap 8); `FileInfo.Length > 256KB` ablehnen; `JsonDocumentOptions { MaxDepth = 8 }`.

#### R3-C-09 — CLI `Enum.Parse<LogLevel>` wirft auf invalid -> Exit 1 statt 3 (P2)
**Tags:** CLI · UX · Error-Codes
- [ ] **Fix umsetzen**
- **File:** [Program.cs](../../src/Romulus.CLI/Program.cs#L240-L255)
- **Problem:** Profile-Datei mit `"logLevel": "Trace"` -> Exit 1, nicht 3.
- **Fix:** Zentrale Validierung nach Profile-Merge mit `TryParse` + Exit 3.

#### R3-C-10 — CLI `Console.ReadLine()` auf geschlossenem Stdin -> "Aborted by user" Fehl-Diagnose (P2)
**Tags:** CLI · UX · Headless · CI/CD
- [ ] **Fix umsetzen**
- **File:** [Program.cs](../../src/Romulus.CLI/Program.cs#L184-L195)
- **Fix:** `Console.IsInputRedirected || ReadLine() is null` -> Exit 3 mit Hinweis auf `--yes`.

#### R3-C-11 — `CliOutputWriter` nutzt Reflection-`JsonSerializer` (AOT-Trim-Drift) (P2)
**Tags:** CLI · AOT · Maintainability
- [ ] **Fix umsetzen**
- **File:** [CliOutputWriter.cs](../../src/Romulus.CLI/CliOutputWriter.cs#L11-L30)
- **Fix:** `internal partial class CliJsonSerializerContext : JsonSerializerContext` mit allen Output-Typen.

#### R3-C-12 — Report-JSON ohne `schemaVersion` -> Konsumenten erkennen Breaking Changes nicht (P2)
**Tags:** Reporting · Contracts · API-Stability
- [ ] **Fix umsetzen**
- **Files:** [ReportGenerator.cs](../../src/Romulus.Infrastructure/Reporting/ReportGenerator.cs#L172-L185), [RunReportWriter.cs](../../src/Romulus.Infrastructure/Reporting/RunReportWriter.cs#L155-L165)
- **Fix:** `SchemaVersion`-Property in `JsonReport`/`ReportSummary`; ADR fuer SemVer-Politik.

#### R3-C-13 — `SanitizeCorrelationId` -> `null` -> stille Ersetzung bricht Trace (P2)
**Tags:** API · Observability · Tracing
- [ ] **Fix umsetzen**
- **File:** [ProgramHelpers.cs](../../src/Romulus.Api/ProgramHelpers.cs#L101-L113)
- **Fix:** Bei Sanitize-Fail Warning loggen + Response-Header `X-Correlation-Id-Replaced: 1`.

#### R3-C-14 — `RunRequest.Roots` ohne MaxItems -> POST-DoS (P2)
**Tags:** API · DoS · Validation
- [ ] **Fix umsetzen**
- **Files:** [RunManager.cs](../../src/Romulus.Api/RunManager.cs#L273-L295), [Program.RunWatchEndpoints.cs](../../src/Romulus.Api/Program.RunWatchEndpoints.cs#L219-L260)
- **Fix:** `MaxRootsPerRequest=64`; analog `Extensions`/`PreferRegions` Cap 32.

#### R3-C-15 — Dedup `SelectWinner`: doppelter `MainPath`-ThenBy maskiert Upstream-Bug (P3)
**Tags:** Core · Determinism · Dead-Code
- [ ] **Fix umsetzen**
- **File:** [DeduplicationEngine.cs](../../src/Romulus.Core/Deduplication/DeduplicationEngine.cs#L97-L108)
- **Fix:** Zweiten `ThenBy` entfernen + Upstream-Validierung (Throw bei case-only-Duplikat). `CategoryRank` mit `Dictionary<FileCategory,int>`.

---

## Round 4 – Neue Funde

> Quellen: Audit 4a (Avalonia UI – `R4-A-*`), 4b (Tests/Benchmark – `R4-B-*`), 4c (Deploy/Safety/Audit/Logging – `R4-C-*`).
> Insgesamt 61 zusaetzliche Funde. Gesamtbestand: 173.

---

### Block A – Avalonia UI (`R4-A-*`)

#### R4-A-01 — Fake KPI-Berechnung in `ApplyFromPreview` — keine echte Pipeline-Anbindung (P0)
**Tags:** Release-Blocker · Preview/Execute-Parität · Fake-KPIs · Avalonia
- [ ] **Fix umsetzen**
- **File:** [ResultViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/ResultViewModel.cs#L90)
- **Problem:** `ApplyFromPreview` berechnet alle KPIs (`games`, `dupes`, `junk`, `health`) mit hardcoded Fake-Arithmetik (`rootCount * 120`, `rootCount * 14` etc.). Die GUI zeigt erfundene Zahlen, die keinerlei Bezug zur tatsaechlichen Analyse-Pipeline haben. Preview/Execute/Report-Paritaet ist fundamental gebrochen.
- **Fix:** `ApplyFromPreview` muss ein echtes Result-Modell aus Core entgegennehmen (`RunResult`, `SortProjection`). Alle KPIs aus dem tatsaechlichen Ergebnis der Pipeline. Kein `HasRunData=true` ohne echten Run.
- **Tests fehlen:**
  - [ ] Preview/Execute/Report-Paritaet: gleiche Roots → GUI, CLI, API zeigen identische KPIs
  - [ ] `HasRunData` erst nach echtem Run true

#### R4-A-02 — Path-Traversal-Risiko in `ImportRootsAsync`: importierte Pfade ohne Validierung (P0)
**Tags:** Release-Blocker · Security · Path Traversal · OWASP A01 · Avalonia
- [ ] **Fix umsetzen**
- **File:** [StartViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/StartViewModel.cs#L87)
- **Problem:** Aus Textdatei importierte Pfade werden nach `Trim()` ohne Validierung direkt in `Roots` uebernommen. Kein Check auf `../../`, keine Pruefung ob Verzeichnis existiert, keine Laengenpruefung, keine Pruefung auf System-Pfade (`C:\Windows\System32`). Verletzt explizit die Projektregel "Path Traversal blockieren".
- **Fix:** `Path.GetFullPath()` + Canonical-Path-Vergleich; Pfad muss innerhalb eines erlaubten `AllowedRootsBase` liegen; `Directory.Exists()` pruefen; relative Pfade und System-Pfade ablehnen.
- **Tests fehlen:**
  - [ ] Input `../../Windows/System32` → abgelehnt
  - [ ] Relativer Pfad → abgelehnt
  - [ ] TOCTOU: Datei zwischen Exists-Check und ReadAllLines geloescht → kein Crash

#### R4-A-03 — `SafeDialogBackend` vollstaendig non-funktional in Production-DI (P1)
**Tags:** Architecture · Dialog · No-Op · UX-Risk
- [ ] **Fix umsetzen**
- **Files:** [App.axaml.cs](../../src/Romulus.UI.Avalonia/App.axaml.cs#L49), [SafeDialogBackend.cs](../../src/Romulus.UI.Avalonia/Services/SafeDialogBackend.cs#L1)
- **Problem:** `IAvaloniaDialogBackend` ist in Production-DI mit `SafeDialogBackend` registriert. `SafeDialogBackend` ist ein reines No-Op: `BrowseFolder→null`, `Confirm→false`, `DangerConfirm→false`, `ShowInputBox→defaultValue`. Error-Dialoge werden still verschluckt.
- **Fix:** Echte `IAvaloniaDialogBackend`-Implementierung mit Avalonia `MessageBox`-API erstellen. `SafeDialogBackend` nur fuer Tests/Design verwenden.
- **Tests fehlen:**
  - [ ] `IDialogService.Error()` im Avalonia-Kontext zeigt sichtbaren Dialog
  - [ ] `DangerConfirm=false` bricht destructive Operationen tatsaechlich ab

#### R4-A-04 — Synchrones `IDialogService`-Contract strukturell inkompatibel mit Avalonia (P1)
**Tags:** Architecture · Deadlock-Risk · async · Contracts
- [ ] **Fix umsetzen**
- **File:** [AvaloniaDialogService.cs](../../src/Romulus.UI.Avalonia/Services/AvaloniaDialogService.cs#L30)
- **Problem:** `IDialogService` aus `Romulus.Contracts` definiert synchrone Methoden. Avalonia's gesamte Dialog- und File-Picker-API ist ausschliesslich `async Task<T>`. Eine echte Implementierung muesste Avalonia-async blockierend wrappen → garantierter Deadlock auf UI-Thread.
- **Fix:** `IDialogService` um async-Varianten erweitern (`Task<bool> ConfirmAsync()`) oder separaten `IAsyncDialogService` in Contracts einfuehren. Niemals `.GetAwaiter().GetResult()` auf dem UI-Thread.
- **Tests fehlen:**
  - [ ] Integrations-Test: synchrone Variante vom UI-Thread → kein Deadlock

#### R4-A-05 — Navigation State Machine: `NavigateStartCommand` ohne State-Reset waehrend IsRunning (P1)
**Tags:** Navigation · Race-Condition · State Machine
- [ ] **Fix umsetzen**
- **File:** [MainWindowViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/MainWindowViewModel.cs#L39)
- **Problem:** `NavigateStartCommand` setzt nur `CurrentScreen = WorkspaceScreen.Start` ohne `Progress.IsRunning` zu pruefen. Laufender Run bleibt aktiv; `CompleteRunCommand.CanExecute` bleibt true; nachfolgender Preview-Start wirft den laufenden Run wortlos weg. `ReturnToStartCommand` macht es korrekt (mit `Reset()`), `NavigateStartCommand` nicht.
- **Fix:** `NavigateStartCommand` bei `IsRunning==true` deaktivieren (CanExecute) oder `Progress.Reset()` + `Result.Reset()` erzwingen, analog `ReturnToStartCommand`.
- **Tests fehlen:**
  - [ ] Navigate-Start waehrend IsRunning → Progress.IsRunning danach false
  - [ ] `ReturnToStartCommand` vs `NavigateStartCommand` identischer End-State

#### R4-A-06 — `StartPreviewCommand` ohne `!Progress.IsRunning`-Guard: Double-Start moeglich (P1)
**Tags:** Navigation · Race-Condition · Command Guard
- [ ] **Fix umsetzen**
- **File:** [MainWindowViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/MainWindowViewModel.cs#L34)
- **Problem:** `StartPreviewCommand` prueft nur `HasRoots`. Zweiter Klick waehrend laufendem Preview wirft den Run wortlos weg. Kein Koordination zwischen `StartPreviewCommand` (MainWindow) und `RequestPreviewCommand` (StartView).
- **Fix:** `StartPreviewCommand = new RelayCommand(StartPreview, () => Start.HasRoots && !Progress.IsRunning)`. `NotifyCanExecuteChanged` bei `Progress.IsRunning`-Wechsel.
- **Tests fehlen:**
  - [ ] Zweiter StartPreview waehrend IsRunning → Command nicht ausfuehrbar

#### R4-A-07 — Hardcoded Entwicklungs-Roots in `StartViewModel` Production-Code (P1)
**Tags:** Release-Blocker · Hardcoded Dev-Data · Bug
- [ ] **Fix umsetzen**
- **File:** [StartViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/StartViewModel.cs#L29)
- **Problem:** `Roots` wird mit `@"C:\\ROMS\\Arcade"` und `@"C:\\ROMS\\Nintendo"` initialisiert. Existieren auf den meisten Systemen nicht. `HasRoots` ist initial `true` wegen dieser Stubs. Darf niemals ausgeliefert werden.
- **Fix:** `Roots = []`. Entwicklungs-/Design-Daten in separatem Design-Time-ViewModel oder Feature-Flag.
- **Tests fehlen:**
  - [ ] Frisch initialisierter `StartViewModel` hat `Roots.Count == 0` und `HasRoots == false`

#### R4-A-08 — `ImportRootsAsync`: kein try/catch um `File.ReadAllLinesAsync` (TOCTOU + IO) (P2)
**Tags:** Robustness · Error Handling
- [ ] **Fix umsetzen**
- **File:** [StartViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/StartViewModel.cs#L88)
- **Fix:** `try/catch(Exception ex)` um `File.ReadAllLinesAsync`; bei Fehler Status-Property setzen oder `IDialogService.Error()` aufrufen.

#### R4-A-09 — `AddRootAsync` / `ImportRootsAsync`: kein try/catch um Picker-Aufruf (P2)
**Tags:** Robustness · Error Handling
- [ ] **Fix umsetzen**
- **Files:** [StartViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/StartViewModel.cs#L68)
- **Fix:** try/catch um `BrowseFolderAsync()` und `BrowseFileAsync()` mit sichtbarem Fehler-Feedback.

#### R4-A-10 — `ProgressViewModel.LiveLog.Add` nicht thread-safe fuer kuenftige async-Operationen (P2)
**Tags:** Concurrency · Thread Safety · ObservableCollection
- [ ] **Fix umsetzen**
- **File:** [ProgressViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/ProgressViewModel.cs#L83)
- **Problem:** `LiveLog` ist eine `ObservableCollection<string>` ohne Dispatcher-Schutz. Sobald echter async-Code (Run-Orchestrator) `AppendLog` vom Background-Thread aufruft → `InvalidOperationException`-Crash.
- **Fix:** `Dispatcher.UIThread.InvokeAsync(() => LiveLog.Add(line))` in `AppendLog`.
- **Tests fehlen:**
  - [ ] `AppendLog` von Background-Thread → kein Crash, Eintrag erscheint

#### R4-A-11 — Hardcoded Hex-Farben in allen AXAML-Dateien: Theme-Toggle wirkungslos (P2)
**Tags:** Theming · Accessibility · ResourceDictionary Bypass
- [ ] **Fix umsetzen**
- **Files:** [MainWindow.axaml](../../src/Romulus.UI.Avalonia/MainWindow.axaml#L43), [StartView.axaml](../../src/Romulus.UI.Avalonia/Views/StartView.axaml#L11), [ProgressView.axaml](../../src/Romulus.UI.Avalonia/Views/ProgressView.axaml#L9), [ResultView.axaml](../../src/Romulus.UI.Avalonia/Views/ResultView.axaml#L9)
- **Problem:** `BorderBrush="#D0D7DE"` und `Background="#FCFCFD"` in allen Views hardcoded. Dark-Mode-Toggle hat keine Wirkung auf diese Farben.
- **Fix:** `DynamicResource`-Referenzen statt Hex-Literale. `ResourceDictionary` mit Semantic-Color-Keys fuer Hell/Dunkel.

#### R4-A-12 — Picker-Services: Lifetime-Auflosung via `Application.Current` statt DI (P2)
**Tags:** Testability · DI · Application.Current
- [ ] **Fix umsetzen**
- **Files:** [AvaloniaStorageFilePickerService.cs](../../src/Romulus.UI.Avalonia/Services/AvaloniaStorageFilePickerService.cs#L17), [AvaloniaStorageFolderPickerService.cs](../../src/Romulus.UI.Avalonia/Services/AvaloniaStorageFolderPickerService.cs#L15)
- **Fix:** `IClassicDesktopStyleApplicationLifetime` in DI registrieren und per Constructor-Injection uebergeben.

#### R4-A-13 — `MainWindow`: parameterloser Konstruktor erstellt `new MainWindowViewModel()` ohne DI (P2)
**Tags:** DI · Design-Time · Code Clarity
- [ ] **Fix umsetzen**
- **File:** [MainWindow.axaml.cs](../../src/Romulus.UI.Avalonia/MainWindow.axaml.cs#L8)
- **Fix:** Parameterlose Konstruktor mit `[EditorBrowsable(Never)]` und klarem Kommentar "Nur fuer XAML-Designer" kennzeichnen.

#### R4-A-14 — Fehlende `AutomationProperties` auf allen interaktiven Elementen (P3)
**Tags:** Accessibility · UI Automation
- [ ] **Fix umsetzen**
- **Files:** [MainWindow.axaml](../../src/Romulus.UI.Avalonia/MainWindow.axaml), [StartView.axaml](../../src/Romulus.UI.Avalonia/Views/StartView.axaml), [ProgressView.axaml](../../src/Romulus.UI.Avalonia/Views/ProgressView.axaml), [ResultView.axaml](../../src/Romulus.UI.Avalonia/Views/ResultView.axaml)
- **Fix:** `AutomationProperties.Name` auf allen Buttons, ListBox, ProgressBar.

#### R4-A-15 — Event-Subscriptions ohne Unsubscribe in `MainWindowViewModel` (P3)
**Tags:** Memory · Lifetime · Events
- [ ] **Fix umsetzen**
- **File:** [MainWindowViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/MainWindowViewModel.cs#L27)
- **Fix:** `IDisposable` implementieren, Unsubscribe in `Dispose()`. Oder `WeakEventManager`.

#### R4-A-16 — `Tmds.DBus.Protocol`-Paket ohne Begruendung im Windows-Deployment (P3)
**Tags:** Hygiene · Supply-Chain · Unused Dependency
- [ ] **Fix umsetzen**
- **File:** [Romulus.UI.Avalonia.csproj](../../src/Romulus.UI.Avalonia/Romulus.UI.Avalonia.csproj#L22)
- **Fix:** Paket entfernen oder Condition `'$(OS)' == 'Unix'` hinzufuegen und dokumentieren.

#### R4-A-17 — `NavigateProgressCommand` ohne Guard: freie Navigation zur leeren Progress-View (P3)
**Tags:** Navigation · UX · Guard
- [ ] **Fix umsetzen**
- **File:** [MainWindowViewModel.cs](../../src/Romulus.UI.Avalonia/ViewModels/MainWindowViewModel.cs#L41)
- **Fix:** CanExecute: `() => Progress.IsRunning || Progress.CurrentPhase != "Idle"`.

---

### Block B – Tests & Benchmark (`R4-B-*`)

#### R4-B-01 — Benchmark-DAT-Pipeline komplett blind: `DatIndex: null` (P0)
**Tags:** Release-Blocker · Benchmark · DAT-Pipeline-Blindspot
- [ ] **Fix umsetzen**
- **File:** [BenchmarkEvaluationRunner.cs](../../src/Romulus.Tests/Benchmark/BenchmarkEvaluationRunner.cs#L117)
- **Problem:** `TryEvaluateEnrichmentCandidate` uebergibt `DatIndex: null` an `EnrichmentPhase.Execute()`. Die gesamte DAT-Hash-Lookup-Pipeline laeuft in keinem einzigen Benchmark-Sample. DAT-Regressionsfehler bleiben unsichtbar.
- **Fix:** `BenchmarkFixture` muss einen `DatIndex` aus `benchmark/dats/` laden und in `TryEvaluateEnrichmentCandidate` uebergeben.
- **Tests fehlen:**
  - [ ] Benchmark-Sample mit bekanntem DAT-Treffer → `DatVerified`-Verdict

#### R4-B-02 — Silent-Catch in Benchmark-Evaluation maskiert Pipeline-Fehler (P0)
**Tags:** Release-Blocker · Benchmark · Exception-Masking
- [ ] **Fix umsetzen**
- **File:** [BenchmarkEvaluationRunner.cs](../../src/Romulus.Tests/Benchmark/BenchmarkEvaluationRunner.cs#L120-L125)
- **Problem:** Nacktes `catch { return null; }` schluckt alle Enrichment-Pipeline-Exceptions. Sample gilt als "korrekt nach Fallback", waehrend der echte Fehler verschwindet.
- **Fix:** Exception fangen, als `BenchmarkVerdict.Error` zaehlen, in `_output.WriteLine` loggen.

#### R4-B-03 — Alibi-Test: `EdgeCaseBenchmarkTests` prueft keine Korrektheit (P0)
**Tags:** Alibi-Test · Benchmark · Edge Cases
- [ ] **Fix umsetzen**
- **File:** [EdgeCaseBenchmarkTests.cs](../../src/Romulus.Tests/Benchmark/EdgeCaseBenchmarkTests.cs#L22-L30)
- **Problem:** Assertiert nur `confidence >= 0 && confidence <= 100` und dass die Sample-Datei existiert. Alle Edge Cases koennen falsch erkannt werden, Test bleibt gruen.
- **Fix:** Verdict-Assertion analog zu `GoldenCoreBenchmarkTests` ergaenzen.

#### R4-B-04 — 4 Benchmark-Sets ohne Verdict-Assertion (Chaos/Realworld/Repair/DatCoverage) (P0)
**Tags:** Alibi-Tests · Benchmark Coverage · Silent Pass
- [ ] **Fix umsetzen**
- **Files:** [ChaosMixedBenchmarkTests.cs](../../src/Romulus.Tests/Benchmark/ChaosMixedBenchmarkTests.cs#L22-L28), [GoldenRealworldBenchmarkTests.cs](../../src/Romulus.Tests/Benchmark/GoldenRealworldBenchmarkTests.cs#L18-L24), [RepairSafetyBenchmarkTests.cs](../../src/Romulus.Tests/Benchmark/RepairSafetyBenchmarkTests.cs#L21-L27), [DatCoverageBenchmarkTests.cs](../../src/Romulus.Tests/Benchmark/DatCoverageBenchmarkTests.cs#L40-L46)
- **Problem:** Alle vier prueft nur `confidence in [0,100]` und "Sample existiert". Keine Korrektheitspruefung. Jeder Produktionsfehler in diesen Bereichen ist unsichtbar.
- **Fix:** Jeweils Verdict-Assertion; fuer `RepairSafety` zusaetzlich `expected.RepairSafe` gegen `actual.SortDecision` pruefen.

#### R4-B-05 — `SafetyIoSecurityRedPhaseTests` ohne Trait-Isolation: unbekannter CI-Zustand (P0)
**Tags:** Test-Hygiene · Security Tests · CI-State
- [ ] **Fix umsetzen**
- **File:** [SafetyIoSecurityRedPhaseTests.cs](../../src/Romulus.Tests/SafetyIoSecurityRedPhaseTests.cs#L1-L20)
- **Problem:** Klasse kommentiert als "TDD RED PHASE ONLY — absichtlich ROT", aber kein `[Trait("Category", "RedPhase")]` und `xunit.runner.json` schließt keinen Trait aus. Sicherheitskritische Pfad-Guard-Tests in unklarem CI-Zustand (gruen/rot unklar).
- **Fix:** Wenn gruen: RED-Kommentar entfernen. Wenn rot: `[Trait]` + CI-Filter isolieren oder Implementierung nachziehen.

#### R4-B-06 — Alle Quality-Gates standardmaessig informational: kein CI-Schutz (P1)
**Tags:** Benchmark · Gates · CI-Wirkungslosigkeit
- [ ] **Fix umsetzen**
- **Files:** [QualityGateTests.cs](../../src/Romulus.Tests/Benchmark/QualityGateTests.cs#L23-L57), [HoldoutGateTests.cs](../../src/Romulus.Tests/Benchmark/HoldoutGateTests.cs#L40-L79), [AntiGamingGateTests.cs](../../src/Romulus.Tests/Benchmark/AntiGamingGateTests.cs#L43), [SystemTierGateTests.cs](../../src/Romulus.Tests/Benchmark/SystemTierGateTests.cs#L50)
- **Problem:** Alle Gates gehen ohne `ROMULUS_ENFORCE_QUALITY_GATES=true` mit `return` durch ohne Assertion. M4-wrongMatchRate kann auf 50% steigen, alle Tests bleiben gruen.
- **Fix:** Default auf `enforce=true`. Opt-out via `ROMULUS_SKIP_QUALITY_GATES=true`.

#### R4-B-07 — `GroundTruthComparator` prueft `SortDecision` nicht (P1)
**Tags:** Benchmark · GroundTruth · SortDecision Blindspot
- [ ] **Fix umsetzen**
- **File:** [GroundTruthComparator.cs](../../src/Romulus.Tests/Benchmark/GroundTruthComparator.cs#L23-L155)
- **Problem:** `ExpectedResult.SortDecision` wird nie mit `actual.SortDecision` verglichen. Bug im `DecisionResolver` (Sort statt Blocked) ist im Benchmark unsichtbar.
- **Fix:** `SortDecision`-Vergleich in `Compare()` + `sortDecisionMismatchRate` in `MetricsAggregator`.

#### R4-B-08 — `GroundTruthComparator` prueft `DatMatchLevel`/`DatEcosystem` nicht (P1)
**Tags:** Benchmark · GroundTruth · DAT-Match Blindspot
- [ ] **Fix umsetzen**
- **File:** [GroundTruthComparator.cs](../../src/Romulus.Tests/Benchmark/GroundTruthComparator.cs#L23)
- **Problem:** `ExpectedResult.DatMatchLevel` und `DatEcosystem` werden ignoriert. In Kombination mit R4-B-01 (DatIndex=null) sind DAT-Match-Regressionsfehler doppelt unsichtbar.
- **Fix:** `DatMatchLevel`/`DatEcosystem` in Verdict-Vergleich einbeziehen + eigene Metrik.

#### R4-B-09 — Benchmark statische Service-Instanzen: Test-Isolation verletzt (P1)
**Tags:** Test-Isolation · Static State · Benchmark
- [ ] **Fix umsetzen**
- **File:** [BenchmarkEvaluationRunner.cs](../../src/Romulus.Tests/Benchmark/BenchmarkEvaluationRunner.cs#L15-L17)
- **Problem:** `EnrichmentPhase`, `HashService`, `ArchiveHashService` als `private static readonly`. Bei internem State-Caching koennen aufeinanderfolgende Tests Zustandsreste erben.
- **Fix:** Services als stateless bestaetigen oder instanzbasiert per `BenchmarkFixture` erzeugen.

#### R4-B-10 — `PreviewExecuteParityTests` ohne Report/API-Paritaet (P1)
**Tags:** Test-Coverage · Parity · CLI/API/Report
- [ ] **Fix umsetzen**
- **File:** [PreviewExecuteParityTests.cs](../../src/Romulus.Tests/PreviewExecuteParityTests.cs#L36-L75)
- **Problem:** Prueft nur 4 Felder in GUI→DryRun vs GUI→Execute. CLI-Output, API-Endpunkt, Report-KPIs nicht geprueft. "Preview N Duplikate, Report M" ist nicht testbar.
- **Fix:** Test `RunResult.LoserCount == CLI-exit-summary == API /run/status == Report`.

#### R4-B-11 — Holdout-Gate: 20%-Mindestschwelle bietet keinen effektiven Schutz (P1)
**Tags:** Benchmark · Gates · Threshold
- [ ] **Fix umsetzen**
- **File:** [HoldoutGateTests.cs](../../src/Romulus.Tests/Benchmark/HoldoutGateTests.cs#L76)
- **Problem:** `Holdout_DetectionRate_AboveMinimum` prueft `rate >= 20.0`. Ein System das 80% falsch erkennt besteht diesen Gate.
- **Fix:** Schwelle auf ≥75% anheben. Gate standardmaessig enforced.

#### R4-B-12 — `gates.json` `minDatVerifiedRate=0.00` fuer alle Familien (P1)
**Tags:** Benchmark · Gates · DAT Coverage
- [ ] **Fix umsetzen**
- **File:** [benchmark/gates.json](../../benchmark/gates.json#L78-L89)
- **Problem:** 0% DAT-Verified gilt fuer alle Familien als erfuellt. Mit R4-B-01 kumuliert sich blind-DAT zur keiner Messung.
- **Fix:** Fuer `NoIntroCartridge` ≥30%, `RedumpDisc` ≥20% als realistische Schwellen setzen.

#### R4-B-13 — Fehlende Rollback/Undo-Tests fuer Mid-Run-Cancel (P1)
**Tags:** Test-Coverage · Rollback · Data-Integrity
- [ ] **Fix umsetzen**
- **File:** [MovePhaseAuditInvariantTests.cs](../../src/Romulus.Tests/MovePhaseAuditInvariantTests.cs)
- **Problem:** Kein Test fuer: (a) halbfertiger Move + Cancel → Dateisystem konsistent rollbackbar; (b) Undo nach vollstaendigem Move → exakt originale Pfade; (c) Undo nach fehlgeschlagenem Move → keine Exception.
- **Fix:** Integrations-Test mit Mid-Run-Cancellation-Simulation + anschliessendem Undo.

#### R4-B-14 — `gates.json` `FolderBased.maxUnknownRate=0.95` — kein effektiver Gate (P2)
**Tags:** Benchmark · Gates · Threshold
- [ ] **Fix umsetzen**
- **File:** [benchmark/gates.json](../../benchmark/gates.json#L88)
- **Fix:** Auf ≤75% setzen. Gate-Kommentar mit Begruendung ergaenzen.

#### R4-B-15 — `GroundTruthComparatorTests` — nur 4 Basis-Cases (P2)
**Tags:** Test-Coverage · GroundTruth
- [ ] **Fix umsetzen**
- **File:** [GroundTruthComparatorTests.cs](../../src/Romulus.Tests/Benchmark/GroundTruthComparatorTests.cs#L1-L80)
- **Fix:** Mindestens 8 weitere Cases: AcceptableConsoleKeys, SortDecision-Mismatch, Ambiguous, DatMatchLevel.

#### R4-B-16 — Keine Unicode-Pfad-Tests fuer `FileSystemAdapter.MoveItemSafely` (P2)
**Tags:** Test-Coverage · Unicode · Path Safety
- [ ] **Fix umsetzen**
- **File:** [FileSystemAllowedRootTests.cs](../../src/Romulus.Tests/FileSystemAllowedRootTests.cs)
- **Fix:** Parametrisierte Theory mit Unicode-Ordnernamen (`ゲーム`, `Спорт`, `Üntersuchung`).

#### R4-B-17 — `DecisionResolver` Tier1+datAvailable=false→Sort-Invariante fehlt (P2)
**Tags:** Test-Coverage · DAT-Gate · DecisionResolver
- [ ] **Fix umsetzen**
- **File:** [DecisionResolverTests.cs](../../src/Romulus.Tests/DecisionResolverTests.cs)
- **Fix:** `Tier1_WithDatAvailableButNoMatch_CapsAtReview` und `Tier1_WithoutDat_CanReachSort` als benannte Invarianten-Tests.

#### R4-B-18 — Benchmark-Scope-Luecke: kein End-to-End-Sortierweg (P2)
**Tags:** Benchmark · Scope · Sorting Blindspot
- [ ] **Fix umsetzen**
- **File:** [BenchmarkEvaluationRunner.cs](../../src/Romulus.Tests/Benchmark/BenchmarkEvaluationRunner.cs)
- **Problem:** Dedup, Sorter, MovePipeline, ConversionPhase laufen nie in einem Benchmark-Sample. Falsch sortierte erkannte ROMs sind unsichtbar.
- **Fix:** `SortingPathBenchmarkTests` mit ≥20 Samples durch vollstaendigen `RunOrchestrator` + `actualTargetFolder` vs `expectedTargetFolder`.

#### R4-B-19 — `NegativeControlBenchmarkTests` uebermassige Skips ohne Assertion (P2)
**Tags:** Alibi-Test · Benchmark
- [ ] **Fix umsetzen**
- **File:** [NegativeControlBenchmarkTests.cs](../../src/Romulus.Tests/Benchmark/NegativeControlBenchmarkTests.cs#L28-L55)
- **Fix:** Jeder early-return-Pfad benoetigt eigene Assert-Aussage.

#### R4-B-20 — Fehlende Tests fuer Reparse-Point-Erkennung (SEC-CONV-08) (P2)
**Tags:** Security · Reparse Points · Conversion Safety
- [ ] **Fix umsetzen**
- **File:** [SetMemberRootContainmentSecurityTests.cs](../../src/Romulus.Tests/SetMemberRootContainmentSecurityTests.cs)
- **Fix:** `Directory.CreateSymbolicLink` am Konvertierungsziel, dann `ConversionVerification` → `VerificationStatus.Failed` assertieren.

#### R4-B-21 — `*CoverageBoost*`-Dateien: strukturelles Coverage-Gaming (P3)
**Tags:** Test-Hygiene · Coverage Gaming
- [ ] **Fix umsetzen**
- **Files:** [ApiCoverageBoostTests.cs](../../src/Romulus.Tests/ApiCoverageBoostTests.cs), [EnrichmentPipelinePhaseCoverageBoostTests2.cs](../../src/Romulus.Tests/EnrichmentPipelinePhaseCoverageBoostTests2.cs), mind. 12 weitere `*CoverageBoost*`-Dateien
- **Fix:** Audit aller `*CoverageBoost*`-Tests; reine `Assert.NotNull`-Tests ohne fachliche Aussage loeschen oder mit echten Assertions ersetzen.

#### R4-B-22 — Phasenbasierte Testnamen ohne fachliche Aussage (P3)
**Tags:** Test-Hygiene · Naming
- [ ] **Fix umsetzen**
- **Files:** [Phase1ReleaseBlockerTests.cs](../../src/Romulus.Tests/Phase1ReleaseBlockerTests.cs) bis Phase15
- **Fix:** Umbenennen in sprechende Domaenennamen (als Hygiene-Schuld dokumentieren).

#### R4-B-23 — `BaselineRegressionGateTests` schreibt Artefakte als Test-Seiteneffekt (P3)
**Tags:** Test-Hygiene · Side Effects · Test Isolation
- [ ] **Fix umsetzen**
- **File:** [BaselineRegressionGateTests.cs](../../src/Romulus.Tests/Benchmark/BaselineRegressionGateTests.cs#L40-L65)
- **Fix:** Artefakt-Schreiben und Baseline-Update in separaten CLI-Befehl auslagern, nicht als Testlauf-Seiteneffekt.

#### R4-B-24 — `AuditEFOpenTests` tautologische `Assert.NotNull` ohne Feldpruefung (P3)
**Tags:** Alibi-Tests · Test-Hygiene
- [ ] **Fix umsetzen**
- **File:** [AuditEFOpenTests.cs](../../src/Romulus.Tests/AuditEFOpenTests.cs#L298-L319)
- **Fix:** Mindestens ein spezifisches Feld pro Test pruefen (Status, Count, konkrete Ausgabewerte).

---

### Block C – Deploy / Safety / Audit / Logging (`R4-C-*`)

#### R4-C-01 — HMAC-Schluessel ephemer: Rollback nach Neustart permanent blockiert (P0)
**Tags:** Release-Blocker · Audit · Rollback Blocked · Data-Integrity
- [x] **Fix umsetzen**
- **File:** [AuditSigningService.cs](../../src/Romulus.Infrastructure/Audit/AuditSigningService.cs#L36)
- **Problem:** HMAC-Signing-Key wird nur im Speicher gehalten wenn kein `keyFilePath` konfiguriert. Nach Neustart: neuer Key, alle bestehenden `.meta.json`-Sidecars koennen nicht mehr verifiziert werden → `Rollback()` dauerhaft blockiert.
- **Fix:** `keyFilePath` in allen produktiven Entry Points zwingend konfigurieren. Im Konstruktor bei `keyFilePath==null` mindestens Warning loggen und idealerweise Exception werfen.
- **Tests fehlen:**
  - [ ] `AuditCsvStore` ohne `keyFilePath`, neu instanziiert → `Rollback()` schlaegt sauber fehl (nicht stillen 0-Eintraege)

#### R4-C-02 — Docker: Profil-Volume unter `/root/.config` nicht erreichbar fuer `app`-User (P0)
**Tags:** Release-Blocker · Docker · Permissions · Data-Loss
- [ ] **Fix umsetzen**
- **Files:** [docker-compose.headless.yml](../../deploy/docker/docker-compose.headless.yml#L26), [Dockerfile](../../deploy/docker/api/Dockerfile#L31)
- **Problem:** `USER app` gesetzt, aber Volume mount auf `/root/.config/Romulus`. App-User hat keinen Schreibzugriff auf `/root/.config/`. Profil-Schreiboperationen schlagen lautlos fehl.
- **Fix:** Volume-Pfad auf Home des `app`-Users aendern (`/home/app/.config/Romulus`) oder `ENV APPDATA=/app/config` setzen.
- **Tests fehlen:**
  - [ ] Container-Smoke: Profil-Write nach API-Start, dann Read → round-trip korrekt

#### R4-C-03 — CI: GitHub Actions nicht auf Commit-SHA gepinnt (Supply-Chain-Angriff) (P1)
**Tags:** Security · CI/CD · Supply Chain · OWASP A08
- [ ] **Fix umsetzen**
- **Files:** [test-pipeline.yml](../../.github/workflows/test-pipeline.yml#L24), [release.yml](../../.github/workflows/release.yml#L17), [benchmark-gate.yml](../../.github/workflows/benchmark-gate.yml#L44)
- **Problem:** Alle Actions (`actions/checkout@v4`, `setup-dotnet@v4`, etc.) auf floating Tags. Tag-Neuzuweisung erlaubt beliebige Code-Ausfuehrung im CI-Runner.
- **Fix:** Alle Actions auf unveraenderliche Commit-SHAs pinnen. `pinact` oder Dependabot-Actions einsetzen.

#### R4-C-04 — `AllowedRootPathPolicy`: keine Extended-Path/ADS-Abwehr (P1)
**Tags:** Security · Path Traversal · OWASP A01 · Safety Divergenz
- [x] **Fix umsetzen**
- **File:** [AllowedRootPathPolicy.cs](../../src/Romulus.Infrastructure/Safety/AllowedRootPathPolicy.cs#L30)
- **Problem:** `IsPathAllowed()` nutzt `Path.GetFullPath()` direkt ohne `SafetyValidator.NormalizePath()`-Guards. `\\?\`/`\\.\`/ADS-Pfade werden von `AllowedRootPathPolicy` anders behandelt als von `SafetyValidator` → divergierende Sicherheitspfade.
- **Fix:** `AllowedRootPathPolicy.IsPathAllowed()` muss `SafetyValidator.NormalizePath(path)` aufrufen; bei `null`-Rueckgabe sofort `false`.
- **Tests fehlen:**
  - [ ] `\\?\`-Pfade, Device-Pfade, ADS-Pfade, Trailing-Dot-Segmente → jeweils `false`

#### R4-C-05 — `WriteMetadataSidecar` schreibt nicht-atomar (Sidecar korrupt bei Crash) (P1)
**Tags:** Audit · Atomicity · Data-Integrity
- [x] **Fix umsetzen**
- **File:** [AuditSigningService.cs](../../src/Romulus.Infrastructure/Audit/AuditSigningService.cs#L171)
- **Problem:** `File.WriteAllText(metaPath, ...)` nicht-atomar. Crash erzeugt leeres/korruptes `.meta.json` → `Rollback()` dauerhaft blockiert. Schluesseldatei-Schreiben ist korrekt atomar (Zeile 74) — Sidecar nicht.
- **Fix:** `tmpPath = metaPath + ".tmp"` + `File.WriteAllText(tmp)` + `File.Move(tmp, meta, overwrite: true)`.
- **Tests fehlen:**
  - [ ] Simulierter Write-Crash (Truncate vor Move) → `VerifyMetadataSidecar` schlaegt kontrolliert fehl

#### R4-C-06 — `AbandonedMutexException` in `AcquireCrossProcessMutex` lautlos geschluckt (P1)
**Tags:** Audit · Data-Integrity · Mutex · Recovery
- [x] **Fix umsetzen**
- **File:** [AuditCsvStore.cs](../../src/Romulus.Infrastructure/Audit/AuditCsvStore.cs#L237)
- **Problem:** `AbandonedMutexException` still gefangen → kein Log, kein Integritaets-Check. Halbgeschriebene CSV-Zeile moeglich. Naechster Write koennte korrupte Zeile anhaengen.
- **Fix:** Exception loggen; letzte Zeile der CSV auf Vollstaendigkeit pruefen (endet mit `\n`?).
- **Tests fehlen:**
  - [ ] Abandoned-Mutex-Szenario → Warn-Log wird emittiert

#### R4-C-07 — Dockerfile: floating Image-Tags ohne Digest-Pinning (P2)
**Tags:** Docker · Supply-Chain · Reproducible Builds
- [ ] **Fix umsetzen**
- **File:** [Dockerfile](../../deploy/docker/api/Dockerfile#L1)
- **Fix:** `FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:<digest>` und `FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:<digest>`.

#### R4-C-08 — Caddyfile: Security Headers unvollstaendig (P2)
**Tags:** Security · HTTP Headers · XSS · Clickjacking
- [ ] **Fix umsetzen**
- **File:** [Caddyfile](../../deploy/docker/caddy/Caddyfile#L1)
- **Problem:** Nur `Strict-Transport-Security` gesetzt. Fehlend: `X-Content-Type-Options`, `X-Frame-Options`, `Content-Security-Policy`, `Referrer-Policy`, `Permissions-Policy`.
- **Fix:** Alle 5 fehlenden Header ergaenzen.

#### R4-C-09 — Smoke-Test: kein positiver AllowedRoots-Akzeptanz-Test (P2)
**Tags:** Smoke-Test · Test Coverage · AllowedRoots
- [ ] **Fix umsetzen**
- **File:** [Invoke-HeadlessSmoke.ps1](../../deploy/smoke/Invoke-HeadlessSmoke.ps1#L172)
- **Problem:** Smoke prueft nur Blocked-Root-Ablehnung (HTTP 400), nicht dass erlaubte Root akzeptiert wird (HTTP 202). Zu-aggressives AllowedRoots-Enforcement unsichtbar.
- **Fix:** Positiver POST `/runs` mit `$allowedRomRoot` → Assert HTTP 202.

#### R4-C-10 — `settings.schema.json` / `rules.schema.json`: zu liberale `additionalProperties` (P2)
**Tags:** Schema · Validation · Config Drift
- [ ] **Fix umsetzen**
- **Files:** [settings.schema.json](../../data/schemas/settings.schema.json#L12), [rules.schema.json](../../data/schemas/rules.schema.json#L43)
- **Fix:** `"additionalProperties": false` setzen. `tool-hashes.schema.json` SHA-Pattern `^[a-fA-F0-9]{64}$` als Required ergaenzen.

#### R4-C-11 — `AuditCsvStore`: UNC-Pfade ohne Spreadsheet-Schutz (CSV-Injection via SMB) (P2)
**Tags:** Security · CSV-Injection · Audit · OWASP A03
- [x] **Fix umsetzen**
- **File:** [AuditCsvStore.cs](../../src/Romulus.Infrastructure/Audit/AuditCsvStore.cs#L204)
- **Problem:** `WriteAuditRowCore` nutzt `SanitizeCsvField` statt `SanitizeSpreadsheetCsvField`. UNC-Pfade (`\\NAS\ROMs\...`) koennen in Excel SMB-Auto-Resolution ausloesen.
- **Fix:** `SanitizeSpreadsheetCsvField` in `WriteAuditRowCore` verwenden.
- **Tests fehlen:**
  - [ ] UNC-Pfad im `oldPath`-Feld → Output enthaelt Quote-Escaping oder `'`-Praefix

#### R4-C-12 — `AppendAuditRow`: Einzelzeilen-Write nicht atomar (P2)
**Tags:** Audit · Atomicity · Data-Integrity
- [x] **Fix umsetzen**
- **File:** [AuditCsvStore.cs](../../src/Romulus.Infrastructure/Audit/AuditCsvStore.cs#L96)
- **Problem:** `AppendAuditRow` oeffnet mit `FileMode.Append` + direktem Write. `AppendAuditRows` nutzt korrekt Temp+Rename. Crash kann unvollstaendige Zeile hinterlassen.
- **Fix:** `AppendAuditRow` auf `AppendAuditRows([row])` delegieren.
- **Tests fehlen:**
  - [ ] Write-Abbruch nach `WriteLine` → CSV danach noch parsebar

#### R4-C-13 — `ConsoleSorter.MoveSetAtomically`: Primaerdatei ohne expliziten Root-Check (P2)
**Tags:** Safety · Path Containment · Sorting
- [ ] **Fix umsetzen**
- **File:** [ConsoleSorter.cs](../../src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs#L457)
- **Problem:** Set-Mitglieder bekommen `IsPathWithinRoot`-Check (Z. 470), Primaerdatei selbst nicht. Bei extern bereitgestellten `candidatePaths` keine Containment-Garantie.
- **Fix:** `if (!IsPathWithinRoot(primaryPath, root)) return (false, 0, []);` vor erstem Move.
- **Tests fehlen:**
  - [ ] `MoveSetAtomically` mit `primaryPath` ausserhalb des Roots → `(false, 0, [])`

#### R4-C-14 — CI: kein `retention-days` auf Test- und Release-Artifacts (P2)
**Tags:** CI · Data Retention · Privacy
- [ ] **Fix umsetzen**
- **Files:** [test-pipeline.yml](../../.github/workflows/test-pipeline.yml#L79), [release.yml](../../.github/workflows/release.yml#L68)
- **Fix:** `retention-days: 14` fuer Test-Artifacts, `retention-days: 30` fuer Release-ZIPs.

#### R4-C-15 — `JsonlLogWriter`: Dateisystem-Pfade im `root`-Feld aller Log-Eintraege (P2)
**Tags:** Security · Information Disclosure · OWASP A09 · Logging
- [ ] **Fix umsetzen**
- **File:** [JsonlLogWriter.cs](../../src/Romulus.Infrastructure/Logging/JsonlLogWriter.cs#L34)
- **Problem:** Vollstaendiger `root`-Pfad in jedem Log-Eintrag. Bei Remote-Logging koennen interne UNC-Pfade (`\\NAS\ROMs\SNES`) Netzwerktopologie preisgeben.
- **Fix:** `root`-Feld auf letztes Pfad-Segment reduzieren oder abstrahierte `rootId` (Hash-Praefix) loggen.
- **Tests fehlen:**
  - [ ] `JsonlLogWriter.Write()` ohne explizites Root-Argument → kein Pfad im Output

#### R4-C-16 — Docker `romulus-cli`: `read_only` fehlt (P3)
**Tags:** Docker · Security · Hardening
- [ ] **Fix umsetzen**
- **File:** [docker-compose.headless.yml](../../deploy/docker/docker-compose.headless.yml#L44)
- **Fix:** `read_only: true` und `tmpfs: [/tmp]` auch fuer `romulus-cli` ergaenzen.

#### R4-C-17 — Docker Healthcheck: `dotnet`-Prozess als Health-Probe teuer (P3)
**Tags:** Docker · Performance · Healthcheck
- [ ] **Fix umsetzen**
- **File:** [docker-compose.headless.yml](../../deploy/docker/docker-compose.headless.yml#L36)
- **Fix:** `test: ["CMD-SHELL", "curl -sf http://localhost:7878/healthz || exit 1"]`.

#### R4-C-18 — `tools/` Diagnose-Skripte: hardcodierte Pfade, kein Fehler-Handling (P3)
**Tags:** Hygiene · Scripts · Error Handling
- [ ] **Fix umsetzen**
- **Files:** [tools/dat-diag.ps1](../../tools/dat-diag.ps1#L1), [tools/dat-map-diag.ps1](../../tools/dat-map-diag.ps1#L1), [tools/DatDiag.csx](../../tools/DatDiag.csx#L3)
- **Fix:** Pfade als Parameter; `$ErrorActionPreference = 'Stop'`; Existenz-Check vor Verwendung.

#### R4-C-19 — `dat-catalog.schema.json`: `ConsoleKey` nullable aber im Code als Pflicht behandelt (P3)
**Tags:** Schema · Contracts · Data Drift
- [ ] **Fix umsetzen**
- **File:** [dat-catalog.schema.json](../../data/schemas/dat-catalog.schema.json#L13)
- **Fix:** Schema auf `"type": "string", "minLength": 1` aendern oder null-Fall mit Warn-Log im Loader explizit behandeln.

#### R4-C-20 — `RotateIfNeeded`: Rotations-Fehler lautlos ignoriert (P3)
**Tags:** Logging · Error Handling · Resilience
- [ ] **Fix umsetzen**
- **File:** [JsonlLogWriter.cs](../../src/Romulus.Infrastructure/Logging/JsonlLogWriter.cs#L105)
- **Fix:** try/catch um Rotations-Block; im catch: `Console.Error.WriteLine(...)` als Fallback-Strategie.

---

## Round 5 – Neue Funde

> Scope: `R5-A-*` = Core + Contracts, `R5-B-*` = Infrastructure (Orchestration / Reporting / FileSystem / DAT), `R5-C-*` = WPF ViewModels + API.
> Neue Funde: 1 P0 / 11 P1 / 19 P2 / 9 P3 = 40 gesamt. Kumulativ: 19 P0 / 65 P1 / 89 P2 / 40 P3 = **213 Gesamt**.

---

### R5-A-01 — `\btrial\b` false positive: legitime ROMs als Junk klassifiziert
**Tags:** `classification` `junk-detection` `false-positive` `P1`
- [x] **Fix umsetzen**
- **File:** [FileClassifier.cs](../../src/Romulus.Core/Classification/FileClassifier.cs#L91)
- **Problem:** `RxJunkWords` enthaelt `\btrial\b` als eigenstaendige Alternation. "Trial of Mana" (Square), "The Trial" (Activision), "Field Trial Edition" matchen dieses Pattern. Im Execute-Modus werden betroffene Dateien in den Trash verschoben – Datenverlust-Risiko.
- **Fix:** `trial` aus unparen­thesierter `RxJunkWords` entfernen; nur `\(trial(?:\s*version)?\)` in parenthesiertem `RxJunkTags`-Pattern erlauben (analog zu `beta`, `alpha`).
- **Tests fehlen:** `"Trial of Mana"` → `FileCategory.Game`; Regression-Suite fuer bekannte Falsch-Positiv-Kandidaten.

---

### R5-A-02 — `VersionScorer`-Konstruktor: ungueltige Regex aus rules.json → Startup-Absturz
**Tags:** `scoring` `resilience` `startup-crash` `P1`
- [x] **Fix umsetzen**
- **File:** [VersionScorer.cs](../../src/Romulus.Core/Scoring/VersionScorer.cs#L175)
- **Problem:** Parametrischer Konstruktor ruft `new Regex(verifiedPattern, ...)` ohne try/catch. Beschaedigte/manipulierte `rules.json` fuehrt zu `ArgumentException` → Startup-Absturz aller drei Entry Points.
- **Fix:** Konstruktor-Body in try/catch einwickeln; bei `ArgumentException` Warnung loggen und Default-Pattern-Fallback nutzen. Alternativ: Pattern-Validierung in Infrastructure vor Factory-Uebergabe.
- **Tests fehlen:** Konstruktor mit ungueltiger `revisionPattern` → kein Wurf, nutzt Fallback. Ungueltige rules.json → App startet mit Warnung.

---

### R5-A-03 — `\bsampler\b`: False Positive auf unparenthesierte ROM-Titel
**Tags:** `classification` `junk-detection` `false-positive` `P1`
- [x] **Fix umsetzen**
- **File:** [FileClassifier.cs](../../src/Romulus.Core/Classification/FileClassifier.cs#L94)
- **Problem:** `\bsampler\b` matcht Titelbestandteile wie "Bass Master Sampler Pack" oder "Drum Sampler 64". Diese Titel aus Arcade/Computer-ROM-Sets werden als Junk klassifiziert.
- **Fix:** `sampler` in `RxJunkWords` durch parenthesisierten Ausdruck in `RxJunkTags` ersetzen: `\((sampler|sampler\s*disc|sampler\s*cd)\)`.
- **Tests fehlen:** `"Bass Master Sampler Pack"` → `FileCategory.Game`; `"(Sampler)"` → `FileCategory.Junk`.

---

### R5-A-04 — `DeduplicationEngine.Deduplicate`: leere GameKeys still uebergangen
**Tags:** `deduplication` `data-loss` `silent-skip` `P1`
- [x] **Fix umsetzen**
- **File:** [DeduplicationEngine.cs](../../src/Romulus.Core/Deduplication/DeduplicationEngine.cs#L113)
- **Problem:** Kandidaten mit leerem/whitespace `GameKey` werden still uebersprungen (`if (string.IsNullOrWhiteSpace(c.GameKey)) continue`). Sie erscheinen weder als Winner noch Loser und werden nicht auditiert. Kein Logging, kein Counter.
- **Fix:** Zaehler fuer uebersprungene Kandidaten fuehren und als Warnung im Run-Ergebnis zurueckgeben. Guard in Pipeline sicherstellen, dass keine Kandidaten mit leerem Key ankommen.
- **Tests fehlen:** Kandidat mit leerem GameKey → erscheint als `SkippedCount`-Aequivalent im Ergebnis, nicht still ignoriert.

---

### R5-A-05 — `GameKeyNormalizer`: `"__empty_key_null"` kollabiert alle leeren Basenames
**Tags:** `gamekeys` `determinism` `grouping` `P2`
- [x] **Fix umsetzen**
- **File:** [GameKeyNormalizer.cs](../../src/Romulus.Core/GameKeys/GameKeyNormalizer.cs#L239)
- **Problem:** Alle null/whitespace-Inputs liefern denselben Key `__empty_key_null`. Zwei beschaedigte ZIP-Entries aus derselben Konsole landen in einer Dedup-Gruppe und ein falscher Winner wird gewaehlt.
- **Fix:** `return "__empty_key_null_" + ComputeStableKeySuffix(baseName ?? "")` statt Konstante; oder beide Leer-Pfade in denselben SHA-256-Fallback fuehren.
- **Tests fehlen:** Zwei verschiedene null/whitespace-Inputs → unterschiedliche Keys. Invariant: `__empty_key_null` nicht als Key fuer mehrere physische Dateien.

---

### R5-A-06 — `RegionDetector`: `[NTSC-J]`/`[Brazil]` in eckigen Klammern nicht erkannt
**Tags:** `region-detection` `scoring` `false-winner` `P2`
- [x] **Fix umsetzen**
- **File:** [RegionDetector.cs](../../src/Romulus.Core/Regions/RegionDetector.cs#L203)
- **Problem:** `ParenGroupPattern` extrahiert nur runde Klammern. Dateinamen wie `"Game [NTSC-J]"` oder `"Game [Brazil]"` (gaengiges No-Intro/TOSEC-Schema) erhalten `Region: UNKNOWN` → `RegionScore: 100` → potenziell falscher Winner.
- **Fix:** Zweiten Pattern-Extraktor fuer `\[([^\]]+)\]` einfuehren und beide Ergebnisse dem Token-Resolver uebergeben.
- **Tests fehlen:** `"Game [NTSC-J]"` → `Region: JP`; `"Game [Brazil]"` → `Region: BR`; eckige und runde Klammern-Tokens beide erkannt.

---

### R5-A-07 — `DatIndex.MaxEntriesPerConsole` default 0 = unbegrenzt
**Tags:** `dat` `memory` `dos` `P2`
- [x] **Fix umsetzen**
- **File:** [DatIndex.cs](../../src/Romulus.Contracts/Models/DatIndex.cs#L20)
- **Problem:** `MaxEntriesPerConsole` default `0` → Kapazitaetsguard inaktiv. Boesartige DAT mit Millionen Eintraegen fuellt Index unbegrenzt → OOM-Exception.
- **Fix:** Vernuenftigen Default setzen: `MaxEntriesPerConsole = 500_000`.
- **Tests fehlen:** DAT mit > MaxEntriesPerConsole Eintraegen → `DroppedByCapacityLimit > 0`, kein OOM. Default-Instance → `MaxEntriesPerConsole > 0`.

---

### R5-A-08 — `SourceIntegrityClassifier`: `.wud`, `.dax`, `.zso`, `.jso`, `.wux` fehlen
**Tags:** `conversion` `integrity` `blocked-conversion` `P2`
- [x] **Fix umsetzen**
- **File:** [SourceIntegrityClassifier.cs](../../src/Romulus.Core/Conversion/SourceIntegrityClassifier.cs#L10)
- **Problem:** `.wud`, `.wux`, `.tgc` (verlustfreie Wii-U/GC-Images) fallen in `Unknown` → Konversion geblockt. `.dax`, `.zso`, `.jso` (komprimierte PSP-Formate) ebenso falsch eingestuft.
- **Fix:** `LosslessExtensions` um `.wud`, `.wux`, `.tgc` ergaenzen; `LossyExtensions` um `.dax`, `.zso`, `.jso`.
- **Tests fehlen:** `.wud` → `Lossless`; `.dax` → `Lossy`; Konversionsplan `.wud→.rvz` nicht geblockt.

---

### R5-A-09 — `HypothesisResolver`: DAT-Gate `datAvailable: false` hardcoded
**Tags:** `classification` `dat-gate` `adr-0021` `P2`
- [x] **Fix umsetzen**
- **File:** [HypothesisResolver.cs](../../src/Romulus.Core/Classification/HypothesisResolver.cs#L229)
- **Problem:** `DecisionResolver.Resolve(..., datAvailable: false, ...)` immer fest. Strukturelle Tier-1-Evidenz kann `Sort` erreichen auch wenn ein DAT geladen ist, das die Datei NICHT enthaelt. Das konservative DAT-Gate (ADR-0021 Phase 1) wirkt nur via `EnrichmentPipelinePhase`, nicht bei direktem `DetectWithConfidence`-Aufruf.
- **Fix:** Dokumentation explizit festhalten dass DAT-Gate NUR via `EnrichmentPipelinePhase` aktiv ist. Alternativ: optionalen `bool datAvailable`-Parameter ergaenzen.
- **Tests fehlen:** Direkter `DetectWithConfidence`-Aufruf auf Tier-1-Datei ohne Enrichment → erzeugt `Sort` (Gap vs. Enrichment-Pfad belegt).

---

### R5-A-10 — `ConversionPolicyEvaluator`: `ManualOnly + Unknown` Risikogrund nicht maschinenlesbar
**Tags:** `conversion` `policy` `ux` `P2`
- [x] **Fix umsetzen**
- **File:** [ConversionPolicyEvaluator.cs](../../src/Romulus.Core/Conversion/ConversionPolicyEvaluator.cs#L35)
- **Problem:** `ManualOnly` + `Unknown`-Integrity → beide `Safety = Risky`. Im Batch-Review-Modus "Alle Risky genehmigen" werden Unknown-Integrity-Dateien ohne Einzelpruefung konvertiert; Unterschied zwischen Policy-Risky und Unknown-Source-Risky geht verloren.
- **Fix:** `ConversionSafety` um `RiskyUnknownSource` erweitern oder maschinenlesbaren `RiskReason` am Plan anfuegen.
- **Tests fehlen:** `Unknown`-Integrity + `ManualOnly` + lossless → Plan hat `RequiresReview=true`, Grund maschinenlesbar unterscheidbar.

---

### R5-A-11 — `RuleEngine._regexCache`: Eviction entfernt FIFO statt LRU
**Tags:** `performance` `caching` `P2`
- [x] **Fix umsetzen**
- **File:** [RuleEngine.cs](../../src/Romulus.Core/Rules/RuleEngine.cs#L152)
- **Problem:** `_regexCache.Keys.Take(MaxRegexCacheSize / 4)` auf `ConcurrentDictionary` ohne Ordnungsgarantie → effektiv zufaellige Eviction. Haeufig genutzte Patterns koennen evicted werden.
- **Fix:** `ConcurrentDictionary` durch `LruCache<string, Regex?>` aus `Romulus.Core.Caching` ersetzen (thread-safe, echter LRU vorhanden).
- **Tests fehlen:** Eviction unter Last → haeufig verwendetes Pattern bleibt im Cache (LRU-Invariant).

---

### R5-A-12 — `FormatScorer.RegionScoreCache`: unbegrenztes Wachstum
**Tags:** `scoring` `memory` `api` `P2`
- [x] **Fix umsetzen**
- **File:** [FormatScorer.cs](../../src/Romulus.Core/Scoring/FormatScorer.cs#L253)
- **Problem:** `RegionScoreCache` (`Dictionary<string, IReadOnlyDictionary<string, int>>`) ohne Size-Limit. Im API-Betrieb mit beliebigen `preferRegions`-Konfigurationen nicht-begrenztes Speicherwachstum moeglich.
- **Fix:** Cache durch `LruCache<string, IReadOnlyDictionary<string, int>>` mit Groesse 100 ersetzen.
- **Tests fehlen:** 200 verschiedene `preferOrder`-Kombinationen → Cachegroeße bleibt ≤ 100.

---

### R5-A-13 — `IFileSystem.MoveItemSafely(src, dst)`: kein Path-Containment im Contract
**Tags:** `security` `path-traversal` `contract` `P2`
- [x] **Fix umsetzen**
- **File:** [IFileSystem.cs](../../src/Romulus.Contracts/Ports/IFileSystem.cs#L28)
- **Problem:** Zweiargumentiger `MoveItemSafely`-Overload ohne `allowedRoot` bietet keinen Contract-Level-Schutz gegen Path Traversal. Callers koennen die sichere dreiargumentige Variante uebersehen.
- **Fix:** Basis-Overload mit `[Obsolete]`-Annotation und Security-Hinweis markieren. Alternativ: XML-Doc-Kommentar mit explizitem Sicherheitshinweis.
- **Tests fehlen:** Contract-Level-Test: Basis-Overload mit Traversal-Pfad – Implementierungsschicht muss schuetzen, Contract dokumentiert es nicht.

---

### R5-A-14 — `DedupeGroup.Winner = null!`: luegt ueber Nullbarkeit
**Tags:** `contracts` `nullability` `nre-risk` `P2`
- [x] **Fix umsetzen**
- **File:** [RomCandidate.cs](../../src/Romulus.Contracts/Models/RomCandidate.cs#L62)
- **Problem:** `public RomCandidate Winner { get; init; } = null!;` – `!`-Operator unterdruckt Null-Warning. Direkte Instanziierung ohne `Winner`-Setzen fuehrt zur Laufzeit-NRE.
- **Fix:** `Winner` als `required` deklarieren oder Typ auf `RomCandidate?` aendern.
- **Tests fehlen:** `new DedupeGroup { GameKey = "test" }` ohne Winner → Compile-Error (required) oder klare Runtime-Exception.

---

### R5-A-15 — `GameKeyNormalizer.AsciiFold`: NFC/NFD-Kollision undokumentiert
**Tags:** `gamekeys` `documentation` `P3`
- [x] **Fix umsetzen**
- **File:** [GameKeyNormalizer.cs](../../src/Romulus.Core/GameKeys/GameKeyNormalizer.cs#L160)
- **Problem:** NFC/NFD-aequivalente Strings erzeugen absichtlich denselben Key – undokumentiert, wirkt wie Bug.
- **Fix:** XML-Kommentar bei `AsciiFold`: "NFD/NFC-aequivalente Strings werden absichtlich auf denselben Wert normalisiert."
- **Tests fehlen:** NFC- und NFD-aequivalenter Input → gleicher Key (Invariant-Dokumentation als Test).

---

### R5-A-16 — `ConversionGraph.GetOutgoingEdges`: Tiebreaker fuer gleiche Kosten nicht stabil
**Tags:** `conversion` `determinism` `P3`
- [x] **Fix umsetzen**
- **File:** [ConversionGraph.cs](../../src/Romulus.Core/Conversion/ConversionGraph.cs#L110)
- **Problem:** `enqueueOrder`-Tiebreaker bei identischen Dijkstra-Pfadkosten haengt von `_capabilities`-Reihenfolge ab (keine stabile Ordnung aus IConversionRegistry).
- **Fix:** Expliziter Tiebreaker auf Ziel-Extension bei gleichem Pfadkosten: alphabetisch kleinsten Target gewinnen lassen.
- **Tests fehlen:** Zwei gleich-kostige Pfade A→C direkt und A→B→C → deterministisch derselbe Winner bei identischen Inputs.

---

### R5-A-17 — `SafeRegex.Replace` ad-hoc Overload in `GameKeyNormalizer`
**Tags:** `performance` `regex` `P3`
- [x] **Fix umsetzen**
- **File:** [GameKeyNormalizer.cs](../../src/Romulus.Core/GameKeys/GameKeyNormalizer.cs#L263)
- **Problem:** `SafeRegex.Replace(key, @"\s+", ...)` bei jedem Aufruf; BCL-Cache nur 15 Patterns. Statisches precompiled-Feld waere sicherer.
- **Fix:** `private static readonly Regex WhitespaceCollapseRegex = new(@"\s+", RegexOptions.Compiled, DefaultTimeout);` als statisches Feld.
- **Tests fehlen:** Performance-Test 10K Iterationen – kein Overhead durch Neukompilierung.

---

### R5-A-18 — `FolderKeyNormalizer`: direkte Regex-Calls ohne SafeRegex
**Tags:** `gamekeys` `consistency` `P3`
- [x] **Fix umsetzen**
- **File:** [FolderKeyNormalizer.cs](../../src/Romulus.Core/GameKeys/FolderKeyNormalizer.cs#L38)
- **Problem:** `TrailingBracketPattern.IsMatch/Replace` direkt aufgerufen (nicht ueber `SafeRegex`). Inkonsistent zum Rest des Projekts.
- **Fix:** Alle Regex-Aufrufe in `FolderKeyNormalizer` ueber `SafeRegex.IsMatch`/`SafeRegex.Replace` fuehren.
- **Tests fehlen:** Ausnahme-Invariante: `GetFolderBaseKey` wirft nie – auch bei extremen Inputs.

---

### R5-A-19 — `ConversionConditionEvaluator`: `_ => false` fuer unbekannte Enum-Werte
**Tags:** `conversion` `fail-fast` `P3`
- [x] **Fix umsetzen**
- **File:** [ConversionConditionEvaluator.cs](../../src/Romulus.Core/Conversion/ConversionConditionEvaluator.cs#L40)
- **Problem:** Switch mit `_ => false`: neue `ConversionCondition`-Werte ohne Evaluator liefern still `false` statt Fehler → Capability nie aktiviert ohne Hinweis.
- **Fix:** `_ => throw new NotSupportedException($"ConversionCondition '{condition}' has no evaluator.")` oder zumindest `Trace.TraceWarning`.
- **Tests fehlen:** Neuer `ConversionCondition`-Wert ohne Evaluator → Warnung/Exception statt stiller `false`.

---

### R5-A-20 — `RegionDetector.NormalizeRegionKey`: `"BR"`, `"AU"`, `"NZ"` undokumentiert passiert
**Tags:** `region-detection` `documentation` `P3`
- [x] **Fix umsetzen**
- **File:** [RegionDetector.cs](../../src/Romulus.Core/Regions/RegionDetector.cs#L174)
- **Problem:** `"BR"` (Brazil), `"AU"` (Australia), `"NZ"` (New Zealand) werden nicht explizit behandelt. Inkonsistent zum EU-Aggregationsmuster. Brasilianische ROMs haeufig im No-Intro-Katalog als `"(Brazil)"`.
- **Fix:** Kommentar ergaenzen, dass `"BR"` absichtlich als eigene Region behalten wird (korrekte Region, kein Mapping auf US/EU/JP).
- **Tests fehlen:** `"(Brazil)"` → `Region: BR` (Invariant-Dokumentation).

---

### R5-B-01 — Zip-Slip in TryParse7zDat: Extraktion unkontrolliert
**Tags:** `security` `zip-slip` `dat` `P1`
- [ ] **Fix umsetzen**
- **File:** [DatRepositoryAdapter.cs](../../src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs#L536)
- **Problem:** `TryParse7zDat` extrahiert mit `7z x -y -o{tempDir} {archivePath}`. Post-hoc-Validierung filtert nur welche Dateien gelesen werden – nicht welche 7z tatsaechlich geschrieben hat. Archiv mit `../`-Sequenzen kann Dateien ausserhalb `tempDir` platzieren.
- **Fix:** Nach Extraktion alle Files mit `GetFiles(tempDir, "*", AllDirectories)` enumerieren und `Path.GetFullPath(f).StartsWith(normalizedTemp)` fuer ALLE pruefen. Alternativ 7z mit `-snl` aufrufen.
- **Tests fehlen:** Crafted 7z mit `../../evil.bat` → kein File ausserhalb tempDir.

---

### R5-B-02 — Unendliche Rekursion in TryParse7zDat bei geschachteltem 7z
**Tags:** `security` `stack-overflow` `dat` `P1`
- [ ] **Fix umsetzen**
- **File:** [DatRepositoryAdapter.cs](../../src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs#L556)
- **Problem:** `ParseDatFileInternal` → `TryParse7zDat` → `ParseDatFileInternal(innerDatPath)`. Ist die extrahierte Datei ebenfalls ein 7z-Archiv, entsteht unbegrenzte Rekursion → `StackOverflowException` crasht den Host-Prozess.
- **Fix:** `depth`-Parameter zu `ParseDatFileInternal` hinzufuegen (max Tiefe 1 oder 2); oder in `TryParse7zDat` pruefen `if (Is7zFile(innerDatPath)) { log; return empty; }`.
- **Tests fehlen:** 7z-DAT das intern ein weiteres 7z enthaelt → kein StackOverflow, leere Results + Warning.

---

### R5-B-03 — `ValidateMagicHeader` nie im Produktionspfad aufgerufen
**Tags:** `conversion` `validation` `data-loss` `P1`
- [ ] **Fix umsetzen**
- **File:** [ConversionOutputValidator.cs](../../src/Romulus.Infrastructure/Conversion/ConversionOutputValidator.cs#L84)
- **Problem:** `ValidateMagicHeader` prueft Magic Bytes fuer .zip, .7z, .rvz, .gcz, .wbfs, .cso – wird aber in `TryValidateCreatedOutput` NICHT aufgerufen. Nur Dateigroesse wird geprueft. Eine Konversion die eine Fehlermeldung als .rvz ausgibt besteht die Validierung; Source wird in Trash verschoben.
- **Fix:** `ValidateMagicHeader` direkt in `TryValidateCreatedOutput` nach der Groessenprufung integrieren (Pflicht fuer Final-Outputs).
- **Tests fehlen:** Konversion produziert Datei mit falschem Magic-Header → `TryValidateCreatedOutput` gibt `false` zurueck.

---

### R5-B-04 — `FileSystemAdapter`-Singleton: `_scanWarnings` Race-Condition
**Tags:** `concurrency` `api` `data-integrity` `P2`
- [ ] **Fix umsetzen**
- **File:** [FileSystemAdapter.cs](../../src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs#L19)
- **Problem:** `FileSystemAdapter` als Singleton mit `_scanWarnings: List<string>`. Im API-Betrieb mit parallelen Requests cleart Request A beim Scan-Start die Warnings von Request B. Audit-Trail fuer parallele Scans inkorrekt.
- **Fix:** `_scanWarnings` per-call lokal halten (`out`-Parameter oder Tuple-Rueckgabe). Alternativ `FileSystemAdapter` als Scoped registrieren.
- **Tests fehlen:** 2 parallele `GetFilesSafe`-Aufrufe → Warnings des einen kontaminieren den anderen nicht.

---

### R5-B-05 — DAT XML-Parsing-Loop: kein CancellationToken
**Tags:** `cancellation` `performance` `ux` `P2`
- [ ] **Fix umsetzen**
- **File:** [DatRepositoryAdapter.cs](../../src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs#L266)
- **Problem:** `while (reader.Read())` ohne `cancellationToken.ThrowIfCancellationRequested()`. DAT-Dateien bis 100 MB / 100.000+ Eintraege. Abbruch-Request blockiert fuer mehrere Sekunden.
- **Fix:** `CancellationToken`-Parameter zu `GetDatIndex`, `ParseDatFileInternal`, `GetDatParentCloneIndex` hinzufuegen. Im Loop: `if (++entryCount % 1000 == 0) ct.ThrowIfCancellationRequested()`.
- **Tests fehlen:** DAT-Parse mit vorauslaufender Cancellation → `OperationCanceledException`, kein Partial-State.

---

### R5-B-06 — `BuildSummary` wirft `InvalidOperationException` im Report-Pfad
**Tags:** `reporting` `crash` `error-handling` `P2`
- [ ] **Fix umsetzen**
- **File:** [RunReportWriter.cs](../../src/Romulus.Infrastructure/Reporting/RunReportWriter.cs#L92)
- **Problem:** Invariant-Bruch in `BuildSummary` wirft `InvalidOperationException`, die durch `WriteReport` und `TryGenerateRequestedReport` nicht gefangen wird. Nutzer erhaelt weder Bericht noch klare Fehlermeldung.
- **Fix:** Spezifische `ReportAccountingException` werfen oder Invariante als non-fatal Warnung loggen. `WriteReport` braucht expliziten `catch (InvalidOperationException)` mit Logging.
- **Tests fehlen:** `RunResult` mit unbalancierten TotalFiles/Candidates-Zaehlers → kein unkontrollierter Exception-Propagation.

---

### R5-B-07 — Set-Member-Move-Loop: kein Cancellation in innerer Schleife
**Tags:** `cancellation` `move` `P2`
- [ ] **Fix umsetzen**
- **File:** [MovePipelinePhase.cs](../../src/Romulus.Infrastructure/Orchestration/MovePipelinePhase.cs#L190)
- **Problem:** `foreach (var plannedMemberMove in plannedMemberMoves)` ohne Cancellation-Pruefung. Grosse Disc-Sets (PS2: 20+ BIN-Tracks) werden ohne Unterbrechungsmoeglichkeit bewegt.
- **Fix:** Am Anfang der inneren Schleife `cancellationToken.ThrowIfCancellationRequested()` einfuegen.
- **Tests fehlen:** Viele Set-Members + vorauslaufende Cancellation → Rollback vollstaendig, kein Halb-Zustand.

---

### R5-B-08 — 7z `-oPath`-Argument ohne Quoting bei Leerzeichen in TempDir
**Tags:** `security` `argument-injection` `dat` `P2`
- [ ] **Fix umsetzen**
- **File:** [DatRepositoryAdapter.cs](../../src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs#L526)
- **Problem:** `var outArg = $"-o{tempDir}"` – bei `Path.GetTempPath()` mit Leerzeichen (`C:\Users\Max Mustermann\...`) interpretiert 7z nur Pfad bis zum ersten Leerzeichen als Ausgabeverzeichnis.
- **Fix:** `var outArg = $"-o\"{tempDir}\""` oder sicherstellen dass `IToolRunner.InvokeProcess` Array-Argumente korrekt quotiert.
- **Tests fehlen:** TempDir-Pfad mit Leerzeichen → 7z-Extraktion erfolgreich.

---

### R5-B-09 — `_nfcCache` statisch und unbegrenzt wachsend
**Tags:** `memory` `performance` `P3`
- [ ] **Fix umsetzen**
- **File:** [FileSystemAdapter.cs](../../src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs#L58)
- **Problem:** `private static readonly ConcurrentDictionary<string, string> _nfcCache` – bei langlaufenden API-Servern mit tausenden Dateipfaden unbegrenztes Wachstum.
- **Fix:** `MemoryCache` mit Sliding Expiration oder Cache bei jedem neuen Run flushen.

---

### R5-B-10 — Preflight `TryValidateWritablePath` modifiziert Last-Write-Time
**Tags:** `filesystem` `side-effect` `P3`
- [ ] **Fix umsetzen**
- **File:** [RunOrchestrator.cs](../../src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs#L162)
- **Problem:** `FileMode.OpenOrCreate, FileAccess.Write` auf bestehende Datei aendert Metadaten. Kommentar verspricht "side-effect free" – ist es nicht.
- **Fix:** `FileMode.Open` statt `FileMode.OpenOrCreate` fuer bestehende Dateien verwenden.

---

### R5-B-11 — `TryGeneratePartialDatAudit` ignoriert Cancellation
**Tags:** `cancellation` `ux` `P3`
- [ ] **Fix umsetzen**
- **File:** [RunOrchestrator.PreviewAndPipelineHelpers.cs](../../src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs#L167)
- **Problem:** `phase.Execute(..., CancellationToken.None)` im Cancellation-Handler. Partial DAT-Audit nach User-Abbruch nicht abbrechbar.
- **Fix:** Originalen `cancellationToken` uebergeben oder `CancellationTokenSource` mit kurzem Timeout (5s).

---

### R5-C-01 — `OnlyGames`-Validierungslogik invertiert: jeder Standard-API-Call schlaegt mit HTTP 400 fehl
**Tags:** `api` `validation-logic` `release-blocker` `P0`
- [ ] **Fix umsetzen**
- **File:** [Program.RunWatchEndpoints.cs](../../src/Romulus.Api/Program.RunWatchEndpoints.cs#L660)
- **Problem:** `if (!request.OnlyGames && !request.KeepUnknownWhenOnlyGames)` feuert wenn BEIDE Felder `false` sind – das ist der valide Standard-Aufruf. Jeder API-Client ohne explizite `onlyGames`/`keepUnknownWhenOnlyGames`-Belegung erhaelt HTTP 400.
- **Fix:** `if (request.KeepUnknownWhenOnlyGames && !request.OnlyGames)` – nur ablehnen wenn `keepUnknownWhenOnlyGames=true AND onlyGames=false`.
- **Tests fehlen:** POST `/runs` mit leerem Body (alle bool-Defaults `false`) → HTTP 202 (Regression). `keepUnknownWhenOnlyGames=true, onlyGames=false` → 400. `keepUnknownWhenOnlyGames=true, onlyGames=true` → 202.

---

### R5-C-02 — Redundanter `lock (_activeLock)` in `TryCreateOrReuse` (strukturell fehlerhaft)
**Tags:** `concurrency` `api` `structural` `P1`
- [ ] **Fix umsetzen**
- **File:** [RunLifecycleManager.cs](../../src/Romulus.Api/RunLifecycleManager.cs#L151)
- **Problem:** Innerhalb von `lock (_activeLock)` (aussen, Zeile 56) befindet sich ein zweiter `lock (_activeLock)` (innen, Zeile 151). C# erlaubt Re-entry auf demselben Thread → kein Deadlock, aber strukturell irrefuehrend. Verdeckt kuenftige Aenderungen, die einen echten Deadlock verursachen koennten.
- **Fix:** Inneren `lock (_activeLock)`-Block entfernen – der aeussere Lock schuetzt bereits vollstaendig.
- **Tests fehlen:** Gleichzeitige POST `/runs`-Anfragen → Active-Conflict-Handling ohne Deadlock.

---

### R5-C-03 — `/watch/start`: Request-Body ohne explizites Groessenlimit
**Tags:** `security` `dos` `api` `P1`
- [ ] **Fix umsetzen**
- **File:** [Program.RunWatchEndpoints.cs](../../src/Romulus.Api/Program.RunWatchEndpoints.cs#L843)
- **Problem:** `ReadToEndAsync()` ohne explizites Limit; POST `/runs` hat expliziten `MaxBytesReader` (1 MB-Check). Inkonsistenz: Buffer-Memory-Fenster offen bis Kestrel eingreift.
- **Fix:** `if (ctx.Request.ContentLength is > 1_048_576) return ApiError(400, ...)` vor `ReadToEndAsync()`.
- **Tests fehlen:** POST `/watch/start` mit body > 1 MB → 400.

---

### R5-C-04 — `/metadata/enrich` und `/metadata/enrich/batch`: kein expliziter Body-Size-Check
**Tags:** `security` `dos` `api` `P1`
- [ ] **Fix umsetzen**
- **File:** [Program.MetadataEndpoints.cs](../../src/Romulus.Api/Program.MetadataEndpoints.cs)
- **Problem:** `ReadFromJsonAsync<T>` ohne Content-Length-Check. POST `/runs` prueft explizit auf 1 MB; inkonsistentes Security-Modell. Angreifer kann per `/metadata/enrich/batch` (bis 100 Items) Memory-Exhaustion versuchen.
- **Fix:** Vor `ReadFromJsonAsync` `if (ctx.Request.ContentLength is > 1_048_576) return ApiError(400, ...)` einfuegen.
- **Tests fehlen:** POST `/metadata/enrich/batch` mit body > 1 MB → 400 mit ApiError-Struktur.

---

### R5-C-05 — PUT `/profiles/{id}`: Body-Size nicht explizit validiert
**Tags:** `security` `dos` `api` `P1`
- [ ] **Fix umsetzen**
- **File:** [Program.ProfileWorkflowEndpoints.cs](../../src/Romulus.Api/Program.ProfileWorkflowEndpoints.cs)
- **Problem:** `RunProfileDocument profile` ueber implizites Model Binding gebunden ohne 1-MB-Check. Analog R5-C-04.
- **Fix:** `.AddBodySizeLimit(1_048_576)` auf Endpoint oder expliziter Content-Length-Check.
- **Tests fehlen:** PUT mit body > 1 MB → 400.

---

### R5-C-06 — `ExtensionFilterItem.PropertyChanged`: kein Unsubscribe in `Dispose()`
**Tags:** `wpf` `memory-leak` `lifecycle` `P2`
- [ ] **Fix umsetzen**
- **File:** [MainViewModel.cs](../../src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs#L614)
- **Problem:** `InitExtensionFilters()` subscribed `item.PropertyChanged += OnExtensionCheckedChanged`. `Dispose()` enthaelt keine Schleife zum Abmelden dieser Handler. Jedes `ExtensionFilterItem`/`ConsoleFilterItem` haelt Referenz auf `MainViewModel`.
- **Fix:** In `Dispose()` `foreach (var item in ExtensionFilters) item.PropertyChanged -= OnExtensionCheckedChanged;` und analog fuer `ConsoleFilters`.
- **Tests fehlen:** `MainViewModel.Dispose()` → danach keine `PropertyChanged`-Callbacks mehr auf geclearten Items.

---

### R5-C-07 — Fire-and-Forget `Task.Run` in `ArmInlineMoveConfirmDebounce`: Exception unbeobachtet
**Tags:** `wpf` `async` `exception-handling` `P2`
- [ ] **Fix umsetzen**
- **File:** [MainViewModel.cs](../../src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs#L628)
- **Problem:** `_ = Task.Run(async () => { ... })` – Ergebnis verworfen. `ObjectDisposedException` oder `TaskCanceledException` nach Dispose des ViewModels waere unobserved. `App.xaml.cs` registriert kein `TaskScheduler.UnobservedTaskException`.
- **Fix:** try/catch im Task-Body: unerwartete Exceptions explizit loggen oder deliberat swallowed.
- **Tests fehlen:** Dispose vor Debounce-Expiry verursacht keine unbehandelte Exception.

---

### R5-C-08 — `RunService`: DI-Bypass-Konstruktor ohne Dokumentation
**Tags:** `wpf` `di` `testability` `P2`
- [ ] **Fix umsetzen**
- **File:** [RunService.cs](../../src/Romulus.UI.Wpf/Services/RunService.cs#L31)
- **Problem:** Parameterloser Konstruktor instanziiert `RunEnvironmentFactory` direkt ohne DI (`DI-BYPASS-JUSTIFIED`-Kommentar). Im Produktionspfad wird dieser Konstruktor via DI-Container nicht verwendet, aber er ist `public` und erlaubt direkte Instanziierung mit hardcoded Abhaengigkeiten.
- **Fix:** Konstruktor dokumentieren oder entfernen, da `App.xaml.cs` DI-Instanz liefert.
- **Tests fehlen:** `RunService` ueber DI-Container aufgeloest verhalt sich identisch zu direkt instanziiertem.

---

### R5-C-09 — `SettingsService.SettingsWriteLock` ist `static` – serializiert Parallel-Tests
**Tags:** `testing` `concurrency` `P2`
- [ ] **Fix umsetzen**
- **File:** [SettingsService.cs](../../src/Romulus.UI.Wpf/Services/SettingsService.cs#L18)
- **Problem:** `private static readonly object SettingsWriteLock = new()` – prozessweiter Lock. Parallele Tests mit mehreren `SettingsService`-Instanzen serialisiert → Flakiness und Timeouts moeglich.
- **Fix:** Im Produktionscode (Singleton) kein Problem; fuer Tests: Settings-Pfade pro Test-Instance isolieren. Optional Lock auf Instanzebene wenn Service nicht als Singleton garantiert werden kann.
- **Tests fehlen:** Paralleltest: zwei `SettingsService`-Instanzen schreiben gleichzeitig – kein Deadlock/Timeout.

---

## Round 6 – Neue Funde

> Scope: `R6-A-*` = CLI + Tools + Hashing + Safety + Sorting, `R6-B-*` = Data/Schemas/i18n + Loader, `R6-C-*` = XAML + Resources + Build + Avalonia-Views.
> Neue Funde: 0 P0 / 6 P1 / 18 P2 / 12 P3 = 36 gesamt. Kumulativ: 19 P0 / 71 P1 / 107 P2 / 52 P3 = **249 Gesamt**.

---

### R6-A-01 — 7z-Extraktion ohne Cancellation waehrend Long-Run-Teil
**Tags:** `cancellation` `tool-integration` `P2`
- [ ] **Fix umsetzen**
- **File:** [ArchiveHashService.cs](../../src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs#L240)
- **Problem:** `Hash7zEntries` ruft `_toolRunner.InvokeProcess(...)` ueber Ueberladung mit `CancellationToken.None`. Cancel/Ctrl+C blockiert bis Default-Timeout (30 min). Auch `ListArchiveEntries`-Vorschau und `GetArchiveEntryNames` nicht abbrechbar.
- **Fix:** `IToolRunner.InvokeProcess`-Ueberladung mit `CancellationToken` plus Timeout (10 min) verwenden und `ct` durchreichen.
- **Tests fehlen:** Cancellation-Test waehrend laufender 7z-Extraktion → `OperationCanceledException` innerhalb < 1 s.

---

### R6-A-02 — ChdTrackHashExtractor ohne Cancellation und Timeout-Override
**Tags:** `cancellation` `tool-integration` `P2`
- [ ] **Fix umsetzen**
- **File:** [ChdTrackHashExtractor.cs](../../src/Romulus.Infrastructure/Hashing/ChdTrackHashExtractor.cs#L29)
- **Problem:** `ExtractDataSha1` ruft `chdman info` ohne `CancellationToken` und ohne `timeout`. Korrupte CHDs koennen Pipeline bis 30 min blockieren.
- **Fix:** Signatur `ExtractDataSha1(string chdPath, CancellationToken ct = default)`; Aufrufer durchreichen; `TimeSpan.FromMinutes(2)` als Timeout.
- **Tests fehlen:** Cancel-Test mit fake `IToolRunner`, der Cancel-Token respektiert.

---

### R6-A-03 — Decompression-Bomb-Schutz fehlt fuer 7z-Archive
**Tags:** `security` `dos` `disk-exhaustion` `P1`
- [ ] **Fix umsetzen**
- **File:** [ArchiveHashService.cs](../../src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs#L99)
- **Problem:** Nur `_maxArchiveSizeBytes` (Default 500 MB) auf KOMPRIMIERTE Datei geprueft. 7z mit hoher Kompression (100:1) kann aus 100 MB > 10 GB in `Path.GetTempPath()` schreiben → Disk voll → parallele Move-Phasen scheitern → Datenverlust-Risiko.
- **Fix:** Vor `7z x` per `7z l -slt` Summe der `Size`-Felder parsen. Wenn > unkomprimierter Limit (z.B. 2 GB) abbrechen. Zusaetzlich `DriveInfo(tempDir).AvailableFreeSpace` pruefen. Analog fuer `.zip`.
- **Tests fehlen:** Zip mit Entries `Length` > Limit → leeres Hash-Array, keine Datei in tempDir.

---

### R6-A-04 — `ROMULUS_CONVERSION_TOOLS_ROOT`-Override ohne Pfad-Sicherheit
**Tags:** `security` `tool-hijack` `path-validation` `P2`
- [ ] **Fix umsetzen**
- **File:** [ToolRunnerAdapter.cs](../../src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs#L242)
- **Problem:** `ResolveConversionToolsRoot` liest Env-Variable ungeprueft und setzt sie VOR `ProgramFiles`. Kein Check gegen Reparse-Points/UNC/Drive-Roots. Angreifer mit Env-Setzrecht kann Tool-Discovery in `\\evil-host\share\chdman.exe` umleiten. Hash-Verify mitigiert nur partiell.
- **Fix:** Override-Pfad durch `SafetyValidator.NormalizePath` schicken; ablehnen wenn UNC/Drive-Root/Reparse-Point. Audit-Log emittieren wenn aktiv. Optional zusaetzlich `ROMULUS_ALLOW_TOOL_ROOT_OVERRIDE=1` verlangen.
- **Tests fehlen:** Override mit `\\evil\share` → `FindTool` darf UNC nicht zurueckgeben.

---

### R6-A-05 — HeaderRepairService: `.bak`-Dateien akkumulieren unbegrenzt
**Tags:** `data-hygiene` `disk-leak` `P2`
- [ ] **Fix umsetzen**
- **File:** [HeaderRepairService.cs](../../src/Romulus.Infrastructure/Hashing/HeaderRepairService.cs#L53)
- **Problem:** `RepairNesHeader`/`RemoveCopierHeader` erzeugen `path + ".bak"` mit `overwrite: true`. Wiederholte Reparaturen ueberschreiben Backup → Original-Stand verloren. Keine Audit-/Trash-/Undo-Integration. Re-Scans erfassen `.bak` nicht als Junk.
- **Fix:** Backup in Trash-Root mit eindeutigem Namen (`<original>.<timestamp>.headerrepair.bak`) verschieben. Audit-Eintrag (`HEADER_REPAIR`) schreiben. Hygiene-Cleanup-Job ergaenzen.
- **Tests fehlen:** Doppelte Reparatur derselben Datei → erstes `.bak` bleibt erhalten; Audit-Row geschrieben.

---

### R6-A-06 — HeaderRepairService nicht parallelisierungssicher (Race auf `.tmp`/`.bak`)
**Tags:** `race-condition` `concurrency` `P2`
- [ ] **Fix umsetzen**
- **File:** [HeaderRepairService.cs](../../src/Romulus.Infrastructure/Hashing/HeaderRepairService.cs#L48)
- **Problem:** `path + ".tmp"` und `path + ".bak"` deterministisch ohne PID/GUID. Parallele Reparaturen kollidieren → `IOException` und halbgeschriebene `.tmp`-Reste. Crashed-Reste nach Restart koennen `File.Move(overwrite:true)` falsch interpretieren.
- **Fix:** Per-Datei-Lock (`ConcurrentDictionary<string, SemaphoreSlim>` keyed auf `Path.GetFullPath`); eindeutige `.tmp`-Namen mit PID + GUID; verwaiste Romulus-Repair-Tmps (>5 min alt) beim Start aufraeumen.
- **Tests fehlen:** Parallel-Test: zweimal `RemoveCopierHeader` auf dieselbe Datei aus zwei Tasks → genau eine Reparatur erfolgreich, keine Reste.

---

### R6-A-07 — Watch-Daemon Move-Modus ohne Rate-Limit / Loop-Bremse
**Tags:** `data-loss-risk` `daemon` `safety` `P2`
- [ ] **Fix umsetzen**
- **File:** [Program.cs](../../src/Romulus.CLI/Program.cs#L1370)
- **Problem:** `SubcommandWatchAsync --mode Move --yes` triggert beliebig viele Move-Runs. Busy-Flag-Sperre da, aber kein Limit Runs/Stunde, kein Cool-Down nach Fehler, keine Self-Trigger-Suppression. Move-Targets innerhalb gewatchter Roots koennen Loops verursachen. `--yes` gilt fuer gesamte Daemon-Lebenszeit.
- **Fix:** (a) Debounce-untere Schranke (min. 30 s); (b) Self-Trigger-Suppression fuer Move-Targets fuer N s nach eigenem Move; (c) max. Runs/Stunde konfigurierbar; (d) periodische Re-Bestaetigung oder hartes Time-Budget.
- **Tests fehlen:** FakeWatchService 100 Events in 1 s → nur 1 Run; Move-Target-Self-Event wird gefiltert.

---

### R6-A-08 — `WriteAuditWarning` schreibt MOVE-aehnliche Audit-Zeile mit `oldPath==newPath==root`
**Tags:** `audit-pollution` `data-quality` `P3`
- [ ] **Fix umsetzen**
- **File:** [ConsoleSorter.cs](../../src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs#L846)
- **Problem:** Bei fehlendem Enrichment schreibt Sort eine Audit-Zeile mit `Action="CONSOLE_SORT"`, `RootPath=OldPath=NewPath=root`. Reports/KPIs/History koennten das als Sort-Operation zaehlen.
- **Fix:** Eigene Action `"CONSOLE_SORT_WARNING"` oder `Category="WARNING"`; oder Warnings nur in JSONL-Run-Log statt Audit-CSV. Reports/KPI-Mapper anpassen.
- **Tests fehlen:** Sort ohne `enrichedConsoleKeys` → keine Move-Zeile mit `OldPath==NewPath` in Audit-CSV.

---

### R6-A-09 — `IsRetryableProcessFailure` matcht "Win32" als Substring → falsche Retries
**Tags:** `tool-integration` `false-positive` `P3`
- [ ] **Fix umsetzen**
- **File:** [ToolRunnerAdapter.cs](../../src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs#L348)
- **Problem:** `output.Contains("Win32", OrdinalIgnoreCase)` matcht harmloses Tool-Output (Banner, Logs, Pfade mit "Win32"). Loest unnoetige Retry-Schleifen aus, idempotenz-unsichere Operationen koennten wiederholt werden.
- **Fix:** Praezisere Pattern: nur `Win32Exception:`, `error code 0x80...`, oder Exit-Code `-1` PLUS `failed to start process`. "Win32"-Substring-Branch entfernen.
- **Tests fehlen:** ToolResult `(ExitCode=1, Output="Win32 GDI module loaded")` → `IsRetryableProcessFailure=false`.

---

### R6-A-10 — `SubcommandEnrichAsync`: `Console.CancelKeyPress`-Handler-Leak und HttpClient-Singleton-Verstoss
**Tags:** `resource-leak` `cli-hygiene` `P3`
- [ ] **Fix umsetzen**
- **File:** [Program.cs](../../src/Romulus.CLI/Program.cs#L1497)
- **Problem:** (1) `Console.CancelKeyPress += ...` ohne `-=` → in Tests/Re-Entry akkumulieren Handler. (2) `using var httpClient = new HttpClient()` umgeht `IHttpClientFactory`, parkt Sockets in TIME_WAIT bei wiederholten Aufrufen.
- **Fix:** (1) Handler in Variable speichern, in `finally` mit `-=` entfernen. (2) HttpClient ueber `IHttpClientFactory`/`DatSourceService.CreateConfiguredHttpClient` beziehen.
- **Tests fehlen:** `SubcommandEnrichAsync` zweimal in Folge → keine kumulierten Cancel-Handler.

---

### R6-B-01 — Tool-Hashes-Loader stuerzt bei nicht-String Werten in `Tools`
**Tags:** `tool-hashes` `crash` `schema` `P1`
- [ ] **Fix umsetzen**
- **File:** [ToolRunnerAdapter.cs](../../src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs#L784)
- **Problem:** `EnsureToolHashesLoaded` ruft `prop.Value.GetString()` ohne Kind-Pruefung. Schema erlaubt `additionalProperties: true`. Korrupte/feindliche `tool-hashes.json` (z.B. `"7z.exe": 1`) wirft `InvalidOperationException` – Catch fileart nur `IOException`/`JsonException`. Kompletter Run crasht. Zusaetzlich keine Hex-Validierung.
- **Fix:** Schema haerten: `additionalProperties: { type: "string", pattern: "^[a-fA-F0-9]{64}$" }`. Loader: pro Eintrag `JsonValueKind.String` pruefen, regex-validieren, ungueltige Eintraege loggen+ueberspringen. `InvalidOperationException` in Catch.
- **Tests fehlen:** Mixed `tool-hashes.json` (Zahl, Objekt, leerer String, gueltiger Hex) → Loader wirft nicht, nur gueltige Eintraege im Cache.

---

### R6-B-02 — Konsolen ohne `folderAliases` koennen nie aus Folder-Pfaden erkannt werden
**Tags:** `consoles` `detection` `data-integrity` `P2`
- [x] **Fix umsetzen**
- **File:** [consoles.json](../../data/consoles.json)
- **Problem:** `A800`, `GSUPD`, `MUGEN` haben weder Eintrag in `console-maps.json/ConsoleFolderMap` noch verwendete `folderAliases`. Folder-basierte Detection und FamilyDatPolicy laufen ins Leere. `dat-catalog.json` fuehrt aber `GSUPD`-Eintraege → stille Fehlklassifikation.
- **Fix:** `folderAliases` ergaenzen und in `ConsoleFolderMap` referenzieren. Schema `folderAliases: minItems: 1` erzwingen.
- **Tests fehlen:** Invariantentest: jede Console aus `consoles.json` ↔ `console-maps.json/ConsoleFolderMap` cross-checken.

---

### R6-B-03 — `discExtensions` enthaelt Container- und Patch-Formate
**Tags:** `format-scores` `dat-fallback` `classification` `P2`
- [x] **Fix umsetzen**
- **File:** [format-scores.json](../../data/format-scores.json#L109)
- **Problem:** `discExtensions` listet `.zip`, `.7z`, `.rar`, `.ecm`, `.cso`, `.pbp`, `.dax`, `.jso`, `.zso`. Cartridge-ZIP wird als disc-like behandelt → EnrichmentPipelinePhase Stage-4 erlaubt name-only DAT-Fallback fuer UNKNOWN-Console mit Cartridge-ZIP → Redump-Treffer schlaegt Cartridge-Treffer.
- **Fix:** `discExtensions` strikt auf Disc-Container reduzieren. Container und PSP-Spezialformate ueber separate Felder (`archiveExtensions`, `pspImageExtensions`).
- **Tests fehlen:** Cartridge-ZIP unter UNKNOWN → Stage-4 name-only DAT-Fallback abgelehnt; Schema-Test.

---

### R6-B-04 — Doppelte Compression-Ratios in `ui-lookups.json` und `conversion-registry.json`
**Tags:** `single-source-of-truth` `conversion` `ui` `P2`
- [ ] **Fix umsetzen**
- **File:** [conversion-registry.json](../../data/conversion-registry.json#L329)
- **Problem:** Beide Dateien fuehren parallele `compressionEstimates`/`compressionRatios` mit teils abweichenden Werten. UI und Engine laufen auf zwei Wahrheiten → Drift-Vektor.
- **Fix:** `conversion-registry.json/compressionEstimates` als Quelle behalten; `ui-lookups.json/compressionRatios` loeschen oder dynamisch ableiten.
- **Tests fehlen:** Cleanup-Test: `compressionRatios` in `ui-lookups.json` nicht mehr existent (oder Cross-Check erzwungen).

---

### R6-B-05 — `createdvd` fuer XBOX/X360 erzeugt nicht abspielbare CHD-Dateien
**Tags:** `conversion` `usability` `data-loss-risk` `P2`
- [ ] **Fix umsetzen**
- **File:** [conversion-registry.json](../../data/conversion-registry.json#L60)
- **Problem:** `iso→chd` mit `command: createdvd` listet `applicableConsoles: ["PS2", "XBOX", "X360"]`. XBOX/X360-ISOs sind XGD/XGD3 – chdman packt bit-getreu, aber kein XBOX/X360-Emulator (xemu, xenia) liest CHD-DVD. Default-UX laeuft in Sackgasse, Originale wandern in Trash.
- **Fix:** XBOX/X360 aus `applicableConsoles` entfernen oder per Condition ausschliessen; alternativ `conversionPolicy: ManualOnly` und UI-Hinweis "nicht empfohlen".
- **Tests fehlen:** ConversionPlanner schlaegt fuer XBOX/X360-ISO keine Auto-Capability vor.

---

### R6-B-06 — Schema-Validator meldet nur den ersten Fehler pro Datei
**Tags:** `schema` `dx` `cleanup` `P2`
- [ ] **Fix umsetzen**
- **File:** [StartupDataSchemaValidator.cs](../../src/Romulus.Infrastructure/Configuration/StartupDataSchemaValidator.cs#L49)
- **Problem:** `ValidateSingleFile` wirft sofort `errors[0]`. Operator iteriert mehrfach durch das Fixen. Andere Dateien werden gar nicht erst validiert.
- **Fix:** `ValidateNode` fortsetzen statt early-return; `ValidateRequiredFiles` sammelt Fehler aller Dateien und wirft konsolidierte `InvalidOperationException`.
- **Tests fehlen:** Fehlerhafte `defaults.json` (mehrere Verstoesse) → alle Verstoesse in Exception-Message.

---

### R6-B-07 — `fr.json` mit 389 unuebersetzten Strings
**Tags:** `i18n` `quality` `P3`
- [ ] **Fix umsetzen**
- **File:** [fr.json](../../data/i18n/fr.json)
- **Problem:** 389 Schluessel haben identischen Text wie `en.json`. Frankophone Nutzer sehen UI mit englischen Brocken trotz `AvailableLocales`-Eintrag.
- **Fix:** Entweder uebersetzen oder Locale als "Beta" markieren / ausblenden via Vollstaendigkeits-Check.
- **Tests fehlen:** Coverage-Test pro Locale: Quote `value == en[key]` < 5 % Schwelle.

---

### R6-B-08 — `rules.schema.json` validiert keine `Key`-Werte fuer Region-Listen
**Tags:** `schema` `rules` `data-integrity` `P3`
- [x] **Fix umsetzen**
- **File:** [rules.schema.json](../../data/schemas/rules.schema.json#L13)
- **Problem:** `RegionOrdered`/`Region2Letter` erlauben beliebige `Key`-Strings. Tippfehler `"WLD"` statt `"WORLD"` faellt nicht auf, kippt `preferredRegions`-Selektion still.
- **Fix:** Schema um `enum` der zulaessigen Region-Keys ergaenzen (Master-Liste).
- **Tests fehlen:** Schema-Test mit injizierter Region `"WLD"` → Fehlschlag.

---

### R6-B-09 — Schema-Validator hat keinen Tiefen-Guard (Stack-Overflow-Vektor)
**Tags:** `security` `validator` `P3`
- [ ] **Fix umsetzen**
- **File:** [StartupDataSchemaValidator.cs](../../src/Romulus.Infrastructure/Configuration/StartupDataSchemaValidator.cs#L52)
- **Problem:** `ValidateNode` rekursiv ohne Tiefenzaehler. Modifizierte `*.schema.json` mit zirkulaerer/extrem tiefer Struktur → `StackOverflowException`, ungefangen → DoS gegen GUI/CLI/API beim Startup.
- **Fix:** Tiefenparameter durchreichen, Cap (z.B. 64) erzwingen, `errors.Add(...)` bei Ueberschreitung.
- **Tests fehlen:** Tief verschachteltes JSON (>100 Levels) → kontrollierte Fehlermeldung statt Crash.

---

### R6-B-10 — `defaults.json/extensions` als kommaseparierter String statt Array
**Tags:** `defaults` `schema` `dx` `P3`
- [x] **Fix umsetzen**
- **File:** [defaults.json](../../data/defaults.json#L6)
- **Problem:** `extensions` ist String wie `".zip,.7z,.rar,..."`, Schema typisiert nur `string` ohne Pattern. Whitespace/Newline brechen Split, Duplikate nicht erkannt. Inkonsistent zu `preferredRegions` (Array).
- **Fix:** Auf `string[]` migrieren (Loader akzeptiert beides). Schema `array` mit Pattern `^\.[a-z0-9]+$`, `uniqueItems: true`.
- **Tests fehlen:** Loader-Test mit Whitespace-Schmutz und Duplikaten; Schema-Test.

---

### R6-B-11 — Veralteter Kommentar in `LocalizationService.LoadStrings`
**Tags:** `cleanup` `dx` `P3`
- [ ] **Fix umsetzen**
- **File:** [LocalizationService.cs](../../src/Romulus.UI.Wpf/Services/LocalizationService.cs#L77)
- **Problem:** Kommentar "Per-key fallback: DE base + overlay from target locale" passt nicht zur Implementierung (`LoadLocale("en")` als Basis).
- **Fix:** Kommentar auf "EN base + overlay from target locale" korrigieren; Konstruktor-Verhalten klar dokumentieren.
- **Tests fehlen:** Optional Coverage-Test: `SetLocale("xx")` mit nicht-existentem Locale → strikt EN-Werte.

---

### R6-B-12 — Wildcard `sourceExtension: "*"` re-archiviert bestehende Archive
**Tags:** `conversion` `idempotency` `P3`
- [ ] **Fix umsetzen**
- **File:** [conversion-registry.json](../../data/conversion-registry.json#L246)
- **Problem:** ZIP-Normalisierungseintrag (`"sourceExtension": "*"` → `.zip`) matcht auch `.zip`/`.7z`. `Game.zip` → `Game.zip.zip` moeglich, Verify schlaegt fehl.
- **Fix:** Capability-Match: source-extension darf nicht gleich target-extension sein. Oder Schema-Erweiterung um `excludedSourceExtensions: [".zip"]`.
- **Tests fehlen:** Planer fuer `Tetris.zip` (NES) → keine `*→.zip`-Capability vorgeschlagen, fuer `Tetris.7z` schon.

---

### R6-C-01 — WebView2 in LibraryReportView ohne Sicherheits-Konfiguration und ohne Dispose
**Tags:** `security` `xss` `webview2` `memory-leak` `P1`
- [ ] **Fix umsetzen**
- **File:** [LibraryReportView.xaml.cs](../../src/Romulus.UI.Wpf/Views/LibraryReportView.xaml.cs#L34)
- **Problem:** `webReportPreview.Source = ...` und `NavigateToString(...)` ohne CoreWebView2-Settings. `IsScriptEnabled`, `AreDevToolsEnabled` etc. auf Defaults (Scripts aktiv). Reports enthalten ROM-Dateinamen (User-Input) → Stored-XSS bei Encoding-Luecke moeglich, mit `file://`-Zugriff. WebView2 nie disposed → Hostprozess-Leak.
- **Fix:** `IsScriptEnabled = false`, `AreDevToolsEnabled = false`, `IsWebMessageEnabled = false`. `NavigationStarting` mit Allowlist (nur `file://` unter ReportRoot). `Dispose()` in `Unloaded`.
- **Tests fehlen:** Report mit `<script>alert(1)</script>` im ROM-Namen laden → IsScriptEnabled=false; Negativtest fuer Navigation auf `https://example.com`.

---

### R6-C-02 — `Process.Start` mit `UseShellExecute=true` fuer beliebige Catalog-URIs (Follina-Klasse)
**Tags:** `security` `command-injection` `shell-execute` `P1`
- [ ] **Fix umsetzen**
- **File:** [ToolsViewModel.cs](../../src/Romulus.UI.Wpf/ViewModels/ToolsViewModel.cs#L656)
- **Problem:** `OpenRoadmapLink` akzeptiert beliebige absolute URIs aus `data/conversion-registry.json` und uebergibt sie an `ProcessStartInfo { FileName, UseShellExecute = true }`. `ms-msdt:`, `search-ms:`, `javascript:`, `file://...exe` lassen sich aufrufen → Lieferketten-Risiko.
- **Fix:** Scheme-Allowlist erzwingen: nur `http`/`https`. Lokale Pfade ueber bestehende `OpenSafeShellPath`-Pipeline mit Extension-Allowlist.
- **Tests fehlen:** Negativtest fuer `ms-msdt:`, `javascript:`, `file:`, `mailto:` → `Process.Start` darf nicht aufgerufen werden.

---

### R6-C-03 — Globaler `DispatcherUnhandledException` swallowed alle Exceptions
**Tags:** `data-loss` `error-handling` `P1`
- [ ] **Fix umsetzen**
- **File:** [App.xaml.cs](../../src/Romulus.UI.Wpf/App.xaml.cs#L122)
- **Problem:** `OnDispatcherUnhandledException` setzt unconditionally `e.Handled = true`. Exceptions aus Move/Convert/Repair-Pfaden (per `Dispatcher.BeginInvoke` marshalliert) werden verschluckt. App laeuft mit halb-ausgefuehrtem Run weiter → Inkonsistenz im Audit-/Move-Plan.
- **Fix:** Bei `_vm.IsBusy` Run abbrechen, Audit flushen, kontrolliert beenden statt `Handled=true`. Idle-Zustand: `Handled=true` zulaessig, aber Telemetrie/Reason-Code in ErrorSummary.
- **Tests fehlen:** Exception waehrend `IsBusy=true` simulieren → App nicht still weiter, Cancel ausgeloest, Audit konsistent.

---

### R6-C-04 — `StartApiProcess` haerdcodierte Health-URL und String-Argument-Quoting
**Tags:** `security` `argument-quoting` `hardcoded-url` `P1`
- [ ] **Fix umsetzen**
- **File:** [MainWindow.xaml.cs](../../src/Romulus.UI.Wpf/MainWindow.xaml.cs#L317)
- **Problem:** (a) `Arguments = $"run --project \"{projectPath}\""` quotet manuell – `"` in Pfad ermoeglicht Argument-Injection an `dotnet`. (b) `http://127.0.0.1:5000/health` hardgecoded; bei anderem Port wird ein Fremdprozess auf Port 5000 angesprochen.
- **Fix:** `ArgumentList.Add(...)` statt String-Interpolation. Health-URL aus tatsaechlicher API-Konfiguration (env/setting) oder erst nach erfolgreichem Health-Probe oeffnen.
- **Tests fehlen:** ProjectPath mit `"` → kein gequetschtes Argument; Health-URL aus Settings.

---

### R6-C-05 — Fachliche Strings (DAT-Status-Labels) hardcoded im Converter
**Tags:** `i18n` `architecture` `business-logic-in-converter` `P2`
- [ ] **Fix umsetzen**
- **File:** [Converters.cs](../../src/Romulus.UI.Wpf/Converters/Converters.cs#L342)
- **Problem:** `DatAuditStatusToLabelConverter` liefert hardgecoded "Have", "Wrong Name", "Miss", "Unknown", "Ambiguous". Umgeht i18n. `PhaseDetailConverter` koppelt zudem statisch an `FeatureService.GetLocalizedString`.
- **Fix:** Labels in `LocalizationService` Keys (`DatAudit.Status.Have` etc.) verlagern. Converter liefert Enum→Key-String, Binding nutzt `Loc[...]`. PhaseDetailConverter durch DataTemplate/ViewModel-Property ersetzen.
- **Tests fehlen:** Sprachumschaltung aendert DAT-Audit-Labels (DE/EN); Converter ohne `FeatureService`-Referenz.

---

### R6-C-06 — `PipelineWorkbenchView` ohne i18n und ohne `AutomationProperties`
**Tags:** `i18n` `accessibility` `P2`
- [ ] **Fix umsetzen**
- **File:** [PipelineWorkbenchView.xaml](../../src/Romulus.UI.Wpf/Views/PipelineWorkbenchView.xaml#L6)
- **Problem:** Gesamte View nutzt rohe deutsche Strings, Buttons ohne `AutomationProperties.Name`. Tab loest Conversion/Sorting/Batch (Danger) aus.
- **Fix:** Alle Texte auf `{Binding Loc[Pipeline.*]}`, Buttons mit `AutomationProperties.Name`.
- **Tests fehlen:** Resource-Key-Test fuer alle sichtbaren Strings; Accessibility-Smoke-Test.

---

### R6-C-07 — Hardcodierte Statusfarben in DAT-Views umgehen Theme-System
**Tags:** `theming` `accessibility` `style-consolidation` `P2`
- [ ] **Fix umsetzen**
- **File:** [DatAuditView.xaml](../../src/Romulus.UI.Wpf/Views/DatAuditView.xaml#L41)
- **Problem:** `Background="#00FF88"`, `"#FF0044"`, `"#FFB700"` etc. direkt im XAML. Theme-Switch (HighContrast/CleanDaylight) greift nicht; Kontrast (WCAG AA) gebrochen.
- **Fix:** `{DynamicResource BrushSuccess/Danger/Warning/TextMuted/Ambiguous}` statt Hex-Literal.
- **Tests fehlen:** Theme-Smoke-Test mit HighContrast → keine Neon-Farben in DAT-Views.

---

### R6-C-08 — `Global\Romulus_SingleInstance`-Mutex ohne ACL → Cross-Session-DoS
**Tags:** `security` `dos` `single-instance` `P2`
- [ ] **Fix umsetzen**
- **File:** [App.xaml.cs](../../src/Romulus.UI.Wpf/App.xaml.cs#L29)
- **Problem:** Global-Mutex ohne `MutexSecurity`. Anderer User auf Multi-User-Host kann Mutex erzeugen → DoS gegen Erstuser. `AbandonedMutexException` wird nicht erkannt.
- **Fix:** Auf `Local\` umstellen falls Cross-Session nicht noetig; sonst `MutexSecurity` mit `MutexAccessRule` fuer aktuellen User. `AbandonedMutexException` als createdNew=true behandeln.
- **Tests fehlen:** AbandonedMutex simulieren → App startet; DACL-Sicherheits-Test.

---

### R6-C-09 — `LibraryReportView`-Lifecycle-Cleanup fehlt → WebView2-Hostprozess-Leak
**Tags:** `memory-leak` `webview2` `lifecycle` `P2`
- [ ] **Fix umsetzen**
- **File:** [LibraryReportView.xaml.cs](../../src/Romulus.UI.Wpf/Views/LibraryReportView.xaml.cs#L11)
- **Problem:** `Loaded += OnLoaded` und Click-Lambda nie unsubscribed. msedgewebview2.exe bleibt laufen, Handler akkumulieren.
- **Fix:** `Unloaded`-Handler: Loaded/Click-Handler entfernen, `webReportPreview.Dispose()`. Click-Handler als benannte Methode.
- **Tests fehlen:** View 50× erzeugen+entladen → WebView2-Hostprozesse wachsen nicht linear.

---

### R6-C-10 — Avalonia-Views: keine `AutomationProperties`, keine `FallbackValue/TargetNullValue`, keine i18n
**Tags:** `accessibility` `i18n` `binding-robustness` `P2`
- [ ] **Fix umsetzen**
- **File:** [StartView.axaml](../../src/Romulus.UI.Avalonia/Views/StartView.axaml#L7)
- **Problem:** `StartView`, `ProgressView`, `ResultView` nutzen ausschliesslich hardgecoded deutsche/englische Strings. Bindings ohne `FallbackValue/TargetNullValue`. Keine `AutomationProperties.Name` (Screenreader liest "ListBox").
- **Fix:** Lokalisierungs-Service fuer Avalonia anbinden. `FallbackValue/TargetNullValue` setzen. `AutomationProperties.Name` fuer ListBox/Buttons.
- **Tests fehlen:** Avalonia-i18n-Smoke-Test; A11y-Test ueber AutomationPeers.

---

### R6-C-11 — `ResultDialog.OnExport` ohne i18n und ohne CSV-Injection-Schutz
**Tags:** `i18n` `export` `csv-injection` `P2`
- [ ] **Fix umsetzen**
- **File:** [ResultDialog.xaml.cs](../../src/Romulus.UI.Wpf/ResultDialog.xaml.cs#L88)
- **Problem:** `Title = "Ergebnis exportieren"` und Filter hardcoded. CSV-Pfad schreibt `_plainText` ohne Formel-Praefix-Neutralisierung (`=`, `+`, `-`, `@`) – widerspricht bestehendem Standard im Avalonia-ResultViewModel.
- **Fix:** Filter ueber `Loc[...]`. CSV-Pfad nutzt denselben CSV-Escaper wie Avalonia. Tabular-Daten via `gridContent.ItemsSource` exportieren.
- **Tests fehlen:** Inhalt `=cmd|' /C calc'!A1` → Export escaped; i18n-Resource-Test.

---

### R6-C-12 — `NoWarn=NU1701` versteckt Package-Compatibility-Warnungen
**Tags:** `release-hygiene` `nuget` `P3`
- [ ] **Fix umsetzen**
- **File:** [Romulus.UI.Wpf.csproj](../../src/Romulus.UI.Wpf/Romulus.UI.Wpf.csproj#L12)
- **Problem:** `NU1701`-Suppression versteckt echte Inkompatibilitaeten zwischen .NET 10 Target und Paketen. Kein `Directory.Packages.props`, kein NuGet.config-Source-Mapping → Lieferkette offen.
- **Fix:** `NU1701` entfernen, Pakete aktualisieren oder TargetFramework-Mismatch dokumentieren. Central Package Management einfuehren.
- **Tests fehlen:** Build-Pipeline-Gate ohne `NU1701`-Suppression.

---

### R6-C-13 — Wizard-Region-Checkboxen: hardcoded Content + Mischsprache mit AutomationName
**Tags:** `i18n` `accessibility` `inconsistency` `P3`
- [ ] **Fix umsetzen**
- **File:** [WizardView.xaml](../../src/Romulus.UI.Wpf/Views/WizardView.xaml#L155)
- **Problem:** Region-Checkboxen `Content="Europe (EU)"` etc. und `AutomationProperties.Name="Region Europa bevorzugen"` hardgecoded. Mischsprache (Content englisch / AutomationName deutsch) → Screenreader liest deutsche Beschreibung fuer englischen Text. Plural `'{}{0} Ordner ausgewaehlt'` ebenfalls hardcoded.
- **Fix:** Keys `Wizard.Region.EU/US/JP/World` und `*Tip` einfuehren; Content und AutomationProperties.Name aus derselben Sprachquelle binden.
- **Tests fehlen:** Sprachumschaltung aendert Region-Labels; Content und AutomationName aus gleicher Quelle.

---

### R6-C-14 — Theme-FOUC: Cold-Start zeigt 200–500 ms SynthwaveDark vor User-Theme
**Tags:** `theming` `startup-state` `consistency` `P3`
- [ ] **Fix umsetzen**
- **File:** [App.xaml](../../src/Romulus.UI.Wpf/App.xaml#L11)
- **Problem:** `App.xaml` mergt unconditionally `Themes/SynthwaveDark.xaml`. User-Theme wird erst in `MainWindow.OnLoaded` aktiviert → FOUC. Fuer HighContrast-User UX-Problem.
- **Fix:** Theme-Auswahl in `App.OnStartup` vor `mainWindow.Show()` aus `ISettingsService` lesen und initial setzen. Default in App.xaml auf neutrales Bridge-Theme reduzieren.
- **Tests fehlen:** Headless-Smoke-Test mit `Theme=HighContrast` → `BrushBackground` direkt nach `Show()` korrekt.

---

## Round 7 – Neue Funde

> Scope: `R7-A-*` = Scanner/Reparse/Conversion, `R7-B-*` = Reports/Exports/Determinismus, `R7-C-*` = GUI-Import/Merge/Profile-Hygiene.
> Neue Funde: 0 P0 / 3 P1 / 6 P2 / 3 P3 = 12 gesamt. Kumulativ: 19 P0 / 74 P1 / 113 P2 / 55 P3 = **261 Gesamt**.
> Abgleich gegen bestehende Findings: keine Wiederholung von bereits dokumentierten R1-R6-Themen; direkte Treffer wurden nur aufgenommen, wenn sie eine neue Code-Stelle oder neue fachliche Divergenz belegen.

---

### R7-A-01 — Standalone-Directory-Conversion folgt Reparse-Points und konvertiert ausserhalb der gewaehlten Root
**Tags:** `security` `reparse-point` `conversion` `P1`
- [x] **Fix umsetzen**
- **Files:** [StandaloneConversionService.cs](../../src/Romulus.Infrastructure/Conversion/StandaloneConversionService.cs#L117), [FileSystemAdapter.cs](../../src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs#L113)
- **Problem:** `ConvertDirectory(... recursive: true)` nutzt `Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)`. Damit wird die zentrale sichere Scan-Policy umgangen, die Reparse-Point-Verzeichnisse und Datei-Reparse-Points ueberspringt und deterministisch sortiert. Eine Junction/Symlink unterhalb der gewaehlten Root kann Dateien ausserhalb der erlaubten Root in den mutierenden Conversion-Pfad bringen.
- **Impact:** Mutierende Conversion kann an Dateien ausserhalb des vom Nutzer gewaehlten Bereichs arbeiten. Preview/Execute-Safety wird durch einen Entry-Point-spezifischen Scanner umgangen.
- **Fix:** `StandaloneConversionService` ueber `IFileSystem.GetFilesSafe` oder eine zentrale `AllowedRootPathPolicy` enumerieren lassen; Reparse-Point-Warnings in den Report aufnehmen; Reihenfolge deterministisch halten.
- **Tests fehlen:** Integrationstest mit Junction innerhalb der Input-Root auf externe Datei -> Datei darf nicht in `ConvertFile` landen; Reihenfolge zweier Runs identisch.

---

### R7-A-02 — Completeness-Filesystem-Fallback zaehlt Dateien ausserhalb der Scan-Policy
**Tags:** `report-parity` `reparse-point` `filesystem` `P2`
- [x] **Fix umsetzen**
- **Files:** [CompletenessReportService.cs](../../src/Romulus.Infrastructure/Analysis/CompletenessReportService.cs#L147), [FileSystemAdapter.cs](../../src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs#L113)
- **Problem:** `BuildFromFileSystem` nutzt `Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)` statt des sicheren Scanners. Reparse-Points, deterministische NFC-Sortierung und Scan-Warnings aus `FileSystemAdapter.GetFilesSafe` greifen hier nicht.
- **Impact:** Completeness-Reports koennen andere Dateien zaehlen als Preview/Execute. Bei Junctions kann ein Report Besitz/Fehlen fuer Inhalte ausserhalb der Collection ableiten.
- **Fix:** `IFileSystem` injizieren oder denselben Scan-Adapter verwenden wie Run/Preview. Cancellation in die Enumeration durchreichen.
- **Tests fehlen:** Completeness-Report mit Root-Junction auf externe Datei -> externe Datei bleibt unberuecksichtigt; Scan-Warning wird sichtbar.

---

### R7-A-03 — CLI-Integrity-Baseline bypassed sichere Root-Enumeration
**Tags:** `cli` `integrity` `reparse-point` `P2`
- [x] **Fix umsetzen**
- **File:** [Program.cs](../../src/Romulus.CLI/Program.cs#L578)
- **Problem:** `SubcommandIntegrityBaselineAsync` sammelt Baseline-Dateien mit `Directory.EnumerateFiles(... AllDirectories)`. Damit gelten weder Reparse-Point-Blockade noch deterministische Normalisierung noch Scan-Warnings aus dem zentralen Filesystem-Adapter.
- **Impact:** Eine Integrity-Baseline kann Dateien ausserhalb der angegebenen Roots aufnehmen. Spaetere Drift-/Bitrot-Warnungen basieren dann auf einer anderen fachlichen Wahrheit als der normale Scan.
- **Fix:** CLI-Subcommand ueber denselben `IFileSystem.GetFilesSafe`-Pfad wie Run/Preview verdrahten; Warnings in stderr und Report ausgeben.
- **Tests fehlen:** CLI-Integrationstest mit Junction unter Root -> Baseline enthaelt keine externe Datei; Warning wird ausgegeben.

---

### R7-A-04 — WPF-Helfer-Scans umgehen zentrale Scanner-Policy
**Tags:** `gui` `scanner-parity` `reparse-point` `P2`
- [x] **Fix umsetzen**
- **Files:** [FeatureCommandService.Conversion.cs](../../src/Romulus.UI.Wpf/Services/FeatureCommandService.Conversion.cs#L71), [FeatureCommandService.cs](../../src/Romulus.UI.Wpf/Services/FeatureCommandService.cs#L670), [MainViewModel.Productization.cs](../../src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs#L737)
- **Problem:** `ConversionVerify`, `AutoProfile` und Wizard-Scan verwenden direkte rekursive `Directory.GetFiles`/`Directory.EnumerateFiles`-Aufrufe. Diese GUI-Flows haben dadurch andere Root-/Reparse-/Sortierregeln als Core/Infrastructure-Scans.
- **Impact:** GUI-Vorschauen und Profil-/Wizard-Ausgaben koennen andere Dateien sehen als CLI/API/Run. Das verletzt GUI-CLI-API-Paritaet und erzeugt Fehlbedienungsrisiko bei Junctions.
- **Fix:** Gemeinsamen scanbaren Service in Infrastructure nutzen; GUI bekommt nur Projektionen/Warnings und fuehrt keine eigene rekursive Business-Enumeration aus.
- **Tests fehlen:** ViewModel-/Service-Test mit Fake-Scanner: GUI zeigt exakt die zentral gelieferten Dateien und Warnings; keine direkte `Directory.*AllDirectories`-Enumeration im WPF-Pfad.

---

### R7-B-01 — Reports/Frontend-Exports sind byteweise nicht deterministisch
**Tags:** `determinism` `reports` `exports` `P2`
- [ ] **Fix umsetzen**
- **Files:** [RunReportWriter.cs](../../src/Romulus.Infrastructure/Reporting/RunReportWriter.cs#L116), [ReportGenerator.cs](../../src/Romulus.Infrastructure/Reporting/ReportGenerator.cs#L46), [ReportGenerator.cs](../../src/Romulus.Infrastructure/Reporting/ReportGenerator.cs#L101), [FrontendExportService.cs](../../src/Romulus.Infrastructure/Export/FrontendExportService.cs#L453)
- **Problem:** Report-/Export-Code nutzt `DateTime.UtcNow` und der HTML-Report generiert pro Lauf einen zufaelligen CSP-Nonce. Bei gleichem Input entstehen dadurch unterschiedliche JSON/HTML/CSV/Frontend-Dateien.
- **Impact:** Golden-Master-Tests und reproduzierbare Reports sind unmoeglich. "Gleiche Inputs -> gleiche Outputs" gilt fuer Report-Artefakte nicht.
- **Fix:** Zeitquelle injizieren oder Run-Zeitstempel aus dem fachlichen RunResult uebergeben. CSP-Nonce nur fuer dynamische Auslieferung nutzen oder deterministisch aus Report-Id/Content ableiten; fuer statische Reports optional scriptfrei ausgeben.
- **Tests fehlen:** Zwei Report-/Export-Generierungen mit identischem Input und fixierter Zeitquelle -> bytegleiche Outputs.

---

### R7-B-02 — Report- und Frontend-Export-Writer schreiben direkt auf finale Pfade
**Tags:** `data-integrity` `partial-output` `reports` `P2`
- [x] **Fix umsetzen**
- **Files:** [RunReportWriter.cs](../../src/Romulus.Infrastructure/Reporting/RunReportWriter.cs#L172), [ReportGenerator.cs](../../src/Romulus.Infrastructure/Reporting/ReportGenerator.cs#L205), [ReportGenerator.cs](../../src/Romulus.Infrastructure/Reporting/ReportGenerator.cs#L224), [FrontendExportService.cs](../../src/Romulus.Infrastructure/Export/FrontendExportService.cs#L157), [FrontendExportService.cs](../../src/Romulus.Infrastructure/Export/FrontendExportService.cs#L677)
- **Problem:** CSV/JSON/HTML/Frontend-Exports werden mit `File.WriteAllText`/`File.Create` direkt auf dem Zielpfad erzeugt. Mehrdatei-Exports haben keinen Staging-Ordner, keine atomare Promotion und keinen Cleanup bei Fehler/Cancel.
- **Impact:** Bei IO-Fehler, Cancel oder Crash bleiben teilgeschriebene Dateien oder halb aktualisierte Export-Sets liegen. Nachfolgende Tools koennen diese Artefakte als gueltig interpretieren.
- **Fix:** In einen temp-/staging-Pfad unterhalb des Zielordners schreiben, validieren, dann atomar per Replace/Move promoten. Bei Fehlern Staging sauber entfernen und Report-Status mit PartialOutput-Warning ausgeben.
- **Tests fehlen:** Fake-Filesystem wirft nach erstem Artefakt -> Zielordner enthaelt kein teilpromotetes Export-Set; Fehler wird im Result modelliert.

---

### R7-B-03 — Junk-Report rekonstruiert Core-Klassifikation mit eigener Pattern-Liste
**Tags:** `duplicate-logic` `report-parity` `classification` `P2`
- [x] **Fix umsetzen**
- **Files:** [CollectionExportService.cs](../../src/Romulus.Infrastructure/Analysis/CollectionExportService.cs#L21), [FileClassifier.cs](../../src/Romulus.Core/Classification/FileClassifier.cs#L96)
- **Problem:** `CollectionExportService.GetJunkReason` fuehrt eigene `JunkPatterns`/`AggressivePatterns`. Die fachliche Junk-Entscheidung kommt aber aus `FileClassifier.Analyze` mit anderen Regexen und Reason-Codes (`junk-tag`, `junk-word`, `junk-aggressive-*`).
- **Impact:** Reports koennen fuer dieselbe Datei andere Gruende oder Gruppierungen anzeigen als die tatsaechliche Core-Entscheidung. Neue Core-Regeln driften automatisch vom Report ab.
- **Fix:** `RomCandidate`/Classification-Projection um den Core-Reason-Code erweitern und Report-Labels nur aus diesem Code mappen. Keine zweite Regex-Liste im Report.
- **Tests fehlen:** Parametrisierter Test fuer `(Sample)`, `(Sampler)`, `[p]`, aggressive Tags: Classification und Junk-Report muessen dieselben Reason-Codes/Levels verwenden.

---

### R7-C-01 — Rule-Pack-Import akzeptiert syntaktisches JSON und ueberschreibt `rules.json`
**Tags:** `gui` `config-integrity` `schema` `P1`
- [ ] **Fix umsetzen**
- **File:** [FeatureCommandService.Workflow.cs](../../src/Romulus.UI.Wpf/Services/FeatureCommandService.Workflow.cs#L129)
- **Problem:** `RulePackSharing` prueft beim Import nur `JsonDocument.Parse(json)`, erstellt dann `dataDir` und kopiert die Datei direkt als `rules.json`. Schema, erwartete Sections und Regex-Pattern werden nicht validiert; Write ist nicht atomar.
- **Impact:** Ein gueltiges, aber fachlich ungueltiges Rule-Pack kann die zentrale Region/GameKey/Version-Konfiguration ersetzen und den naechsten GUI/CLI/API-Start oder Scoring-Lauf kippen. Das ist ein neuer Schreibpfad fuer bereits kritisch sensible Rules.
- **Fix:** Import ueber denselben `StartupDataSchemaValidator` plus fachliche Loader-Validierung (`GameKeyNormalizationProfile`, `RegionDetectionProfile`, `VersionScoringProfile`) laufen lassen. Danach temp+move mit Backup+Rollback.
- **Tests fehlen:** Import mit syntaktisch gueltigem JSON, fehlenden Sections oder invalidem Regex -> keine Aenderung an `rules.json`; Backup bleibt konsistent.

---

### R7-C-02 — Collection-Merge verschluckt fehlgeschlagene Rollback-/Cleanup-Operationen
**Tags:** `data-integrity` `rollback` `audit` `P1`
- [x] **Fix umsetzen**
- **File:** [CollectionMergeService.cs](../../src/Romulus.Infrastructure/Analysis/CollectionMergeService.cs#L556)
- **Problem:** Nach einem Apply-Fehler ruft `ExecuteMutatingEntryAsync` `TryRevertFailedMutation` auf. Diese Methode loescht Copy-Ziele oder bewegt Move-Ziele zurueck, verschluckt aber jede Exception vollstaendig. Das Apply-Result enthaelt nur `apply-failed`, keine Information, ob Cleanup/Rollback selbst scheiterte.
- **Impact:** Operator und Report koennen nicht unterscheiden zwischen "Mutation fehlgeschlagen und sauber zurueckgerollt" und "Mutation fehlgeschlagen, Ziel/Quelle in unbekanntem Zustand". Das ist ein Datenintegritaets- und Audit-Risiko.
- **Fix:** Rollback-Ergebnis modellieren (`RollbackAttempted`, `RollbackSucceeded`, `RollbackError`), in Audit-Metadaten schreiben und bei Fehlschlag als eigener Failure-State zurueckgeben.
- **Tests fehlen:** Fake-`IFileSystem` wirft beim Revert -> ApplyResult enthaelt Rollback-Failure, Audit-Metadata dokumentiert unsauberen Zustand.

---

### R7-C-03 — Collection-Merge-Default-Auditpfad kollidiert bei zwei Runs in derselben Sekunde
**Tags:** `audit` `determinism` `P3`
- [x] **Fix umsetzen**
- **File:** [CollectionMergeService.cs](../../src/Romulus.Infrastructure/Analysis/CollectionMergeService.cs#L19)
- **Problem:** `CreateDefaultAuditPath` erzeugt `collection-merge-{yyyyMMdd-HHmmss}.csv`. Zwei Merge-Applies auf dieselbe Target-Root innerhalb einer Sekunde schreiben denselben Auditpfad.
- **Impact:** Audit-Dateien koennen kollidieren oder ueberschrieben/vermengt werden, je nach `IAuditStore`-Verhalten. Parallele GUI/CLI/API-Nutzung ist dadurch nicht sauber getrennt.
- **Fix:** Millisekunden plus RunId/GUID aufnehmen oder Auditpfad zentral ueber `CliOptionsMapper`/Run-Artifact-Service erzeugen.
- **Tests fehlen:** Zwei Aufrufe mit fixierter Uhrzeit/gleicher TargetRoot -> unterschiedliche Auditpfade.

---

### R7-C-04 — Custom-DAT-Editor dupliziert Append-Logik und hat einen nicht-atomaren Create-Pfad
**Tags:** `duplicate-logic` `dat` `gui` `P3`
- [x] **Fix umsetzen**
- **Files:** [FeatureCommandService.Dat.cs](../../src/Romulus.UI.Wpf/Services/FeatureCommandService.Dat.cs#L287), [FeatureService.Dat.cs](../../src/Romulus.UI.Wpf/Services/FeatureService.Dat.cs#L52)
- **Problem:** `CustomDatEditor` baut XML und Append/Create-Logik inline nach, obwohl `FeatureService.Dat.AppendCustomDatEntry` und `BuildCustomDatXmlEntry` existieren. Der Existing-File-Pfad nutzt temp+move, der Create-Pfad schreibt `custom.dat` direkt.
- **Impact:** Zwei DAT-Append-Wahrheiten koennen bei Header, Description, Escaping und Atomicity auseinanderlaufen. Ein Crash beim erstmaligen Erstellen laesst eine partielle `custom.dat` liegen.
- **Fix:** Einen gemeinsamen DAT-Writer in Infrastructure/Dat oder einen einzigen WPF-Service-Pfad nutzen; Create und Append atomar ueber temp+move; XML-Erzeugung zentralisieren.
- **Tests fehlen:** Existing- und New-File-Pfad erzeugen dieselbe XML-Struktur; Crash vor Move hinterlaesst keine partielle `custom.dat`.

---

### R7-C-05 — Profile-Import ueberschreibt Settings direkt und Backup-Namen kollidieren sekundengenau
**Tags:** `settings` `profile-import` `P3`
- [ ] **Fix umsetzen**
- **File:** [ProfileService.cs](../../src/Romulus.UI.Wpf/Services/ProfileService.cs#L100)
- **Problem:** `ProfileService.Import` legt Backups als `settings.json.{yyyyMMddHHmmss}.bak` an und kopiert danach mit `File.Copy(sourcePath, SettingsPath, overwrite: true)` direkt auf die aktive Settings-Datei. Mehrere Imports in derselben Sekunde kollidieren; ein IO-Fehler waehrend Copy kann die aktive Settings-Datei partiell ersetzen.
- **Impact:** Profil-Import ist nicht robust genug fuer wiederholte/automatisierte Nutzung und verletzt das Standardmuster "temp -> validate -> promote".
- **Fix:** Backup-Namen mit Millisekunden/GUID/RunId erzeugen. Import in temp-Datei schreiben, erneut laden/validieren, dann atomar ersetzen; bei Fehler Backup wiederherstellen.
- **Tests fehlen:** Zwei Imports in derselben Sekunde -> zwei Backups; simulierte Copy-Exception -> aktive Settings bleiben unveraendert.

---

## Round 8 – Abschlussrunde ohne neue Funde

> Scope: gezielte Rest-Suchen nach nicht dokumentierten P3-Themen und Dubletten nach Round 7.
> Ergebnis: 0 P0 / 0 P1 / 0 P2 / 0 P3 = 0 neue Findings.

Gepruefte Restflaechen:
- direkte rekursive `Directory.*AllDirectories`-Enumeration in Entry Points, Infrastructure und WPF,
- direkte finale `File.WriteAllText`/`File.Create`-Writes in Report-/Export-/Settings-Pfaden,
- lokale Pattern-/Reason-Listen fuer Junk, DAT und Reports,
- Rule-/Profile-Importpfade,
- timestampbasierte Artefaktnamen mit Kollisionsrisiko.

Nicht erneut aufgenommen:
- Treffer, die bereits in R1-R7 dokumentiert sind,
- kontrollierte temp-Extraktionspfade unter Tool-/DAT-Handling, sofern sie nicht ausserhalb erlaubter Roots mutieren,
- reine Log-/Metrik-Zeitstempel ohne fachliche Output-Paritaetswirkung,
- bestehende Build-/Analyzer-Suppressionen, die bereits als Hygiene-Funde erfasst wurden.

Damit bleiben nach dieser Abschlussrunde keine neuen, belegbaren und nicht-duplizierten P3-Funde aus den ausgefuehrten Deep-Dive-Suchen offen. Die offenen P3-Eintraege im Dokument sind die bereits gelisteten Arbeitspositionen.

---

## Test- & Verifikationsplan

> Pflicht-Invarianten, die als Unit-/Integration-/Property-Tests ergaenzt werden muessen.

- [ ] **INV-01** Verify-Vertrag: Source nie in Trash bei `VerificationResult != Verified`
- [ ] **INV-02** GameKey-Eindeutigkeit unter Whitespace/Sonderzeichen (Property-Test)
- [ ] **INV-03** Cross-Volume-Move-Atomicity (kein Restdatei bei Cancel)
- [ ] **INV-04** Set-Move-Rollback-Vertrag (kein Throw, dokumentierte Result-Felder)
- [ ] **INV-05** Determinismus unter Permutation (`SelectWinner` 100x)
- [ ] **INV-06** Determinismus unter Parallelitaet (`EnrichmentPipelinePhase` 200x parallel)
- [ ] **INV-07** CSV-Sanitizer-Parity (DAT-Audit vs. Spreadsheet)
- [ ] **INV-08** Tool-Hash-Verify nach Timestomping
- [ ] **INV-09** Hardlink-KPI-Ehrlichkeit (`freed bytes` = 0)
- [ ] **INV-10** GUI/CLI/API-Paritaet (gleicher Input -> gleicher Status, gleiche DAT-Match-Counter, gleiche Decision-Class-Counts)
- [ ] **INV-11** Audit-Hash-Chain (Tampering bei Sidecar-Swap erkennt)
- [ ] **INV-12** Audit-CSV-Atomicity (Crash mid-write -> kein blockierter Rollback)
- [ ] **INV-13** SSRF-Block (Loopback/RFC1918 Hosts werden vor Connect abgelehnt)
- [ ] **INV-14** OwnerClientId-Enforcement (kein Cross-Tenant-Zugriff)
- [ ] **INV-15** HSTS/HTTPS-Pflicht im Remote-Modus

---

## Sanierungsstrategie

### Sofort (vor jedem weiteren Feature-Commit)
- [ ] P0-01 Conversion-Verify fail-closed
- [ ] P0-02 Test invertieren
- [x] P0-03 GameKey-Whitespace-Hash
- [x] P0-04 Cross-Volume-Move atomar
- [x] P0-05 Sidecar atomar
- [x] P0-06 HMAC-Key fail-closed
- [x] P0-07 Rollback-Tampering-Detection
- [x] P0-08 OwnerClientId-Enforcement

### Vor Release
- [ ] Alle P1-Funde abarbeiten
- [ ] INV-01 bis INV-15 implementiert

### Nachgelagert (nach Release)
- [ ] P2-Architektur-Debt (P2-04, P2-05, P2-06, P2-07, P2-10, P2-11)
- [ ] P3-Hygiene

### Bewusst verschieben (mit Begruendung dokumentieren)
- [ ] P3-06 Reparse-Point-Tiefenlimit (symbolisch, niedriger Schaden)
- [ ] P3-08 State-Transition-Test (niedriger Hebel)

---

## Anhang: Systemische Hauptursachen

1. **Verify-Vertrag fail-open statt fail-closed** -> Symptom in Conversion (P0-01), HMAC (P0-06), Sidecar (P0-05).
2. **„Eine fachliche Wahrheit" strukturell verletzt** -> Symptom in CSV-Sanitizer (P1-04), DAT-Update (P1-03), RunOrchestrator-Komposition (P1-02), Status-Strings (P2-09), Settings (P2-08).
3. **Static Mutable State in Core** -> Determinismus-Verletzung (P1-08 erledigt, P2-06 offen).
4. **Halbfertige Refactors** -> Schattenlogik (P1-01 Avalonia, P2-10 FeatureService, P1-11 MainViewModel).
5. **Tests betonieren Bug-Verhalten** (P0-02 offen, P3-07 erledigt).
6. **Audit-Kette ohne kryptografische Anker** (P1-17, P1-18, P0-06).
7. **Filesystem-Annahmen luckenhaft** (Cross-Volume P0-04, Hardlinks P1-09, Long-Path P2-02, Free-Space).

---

**Letzte Aktualisierung:** 2026-04-24
**Naechste Review:** nach Abschluss aller P0-Funde
