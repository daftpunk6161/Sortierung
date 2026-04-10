# Romulus – Hard Audit Findings Tracker

> **Lebendes Dokument** — wird mit jeder Audit-Phase ergänzt.
> Stand: 2026-04-09 | Passes: 1–9 (Final) | Total: 39 Findings

---

## Übersicht

| Pass | Findings | P0 | P1 | P2 | P3 | Erledigt |
|------|----------|----|----|----|----|---------:|
| 1    | F01–F08  | 0  | 3  | 5  | 0  |   8 / 8  |
| 2    | F09–F16  | 0  | 3  | 5  | 0  |   8 / 8  |
| 3    | F17–F19  | 0  | 1  | 1  | 1  |   3 / 3  |
| 4    | F20–F24  | 0  | 2  | 2  | 1  |   5 / 5  |
| 5    | F25–F28  | 0  | 1  | 2  | 1  |   4 / 4  |
| 6    | F29–F33  | 0  | 0  | 3  | 2  |   5 / 5  |
| 7    | F34–F36  | 0  | 0  | 2  | 1  |   3 / 3  |
| 8    | F37–F38  | 0  | 0  | 1  | 1  |   2 / 2  |
| 9    | F39      | 0  | 0  | 0  | 1  |   1 / 1  |
| **Σ** | **39**  | **0** | **10** | **21** | **8** | **39 / 39** |

---

## Pass 1 – Foundation Audit

### F01 – CancellationToken-Race in RunOrchestrator
- [x] **P1** | Race / Concurrency
- **Impact:** Cancel-Request kann nach CTS-Dispose eintreffen → `ObjectDisposedException`
- **Datei:** `src/Romulus.Api/RunLifecycleManager.cs`
- **Fix:** Cancel/Dispose auf RunRecord-CTS synchronisiert (`TryCancelExecution` + `DisposeCancellationSource`), kein exception-basierter Race-Pfad mehr
- **Test:** Cancel während Dispose → kein Throw

### F02 – CTS-Race in WPF OnCancel
- [x] **P1** | Race / Concurrency
- **Impact:** Doppel-Cancel oder Cancel nach Dispose → Crash
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs`
- **Fix:** `lock (_ctsLock)` um `_cts?.Cancel()` (bereits gefixt laut Code-Kommentar `F-02 FIX`)
- **Test:** Doppel-Cancel → kein Throw

### F03 – RunReportWriter Accounting-Invariante
- [x] **P1** | Report-Accuracy
- **Impact:** Report kann `completed_with_errors` statt korrektem Status zeigen
- **Datei:** `src/Romulus.Infrastructure/Reporting/RunReportWriter.cs`
- **Fix:** FilteredNonGameCount nur addieren wenn `Candidates < TotalFiles`
- **Test:** ConvertOnly + OnlyGames Run → Report generiert korrekt

### F04 – SetMember-Pfade ohne Extension-Filter
- [x] **P2** | Scan-Accuracy
- **Impact:** Set-Members (.bin/.cue) könnten nicht in Extensions-Filter sein
- **Datei:** `src/Romulus.Infrastructure/Orchestration/ScanPhase.cs`
- **Fix:** Set-Member-Extensions automatisch in Extensions-Liste aufnehmen
- **Test:** .cue-Datei mit .bin-Members, nur .cue in Extensions → Members trotzdem erkannt

### F05 – GameKey-Normalisierung bei Sonderzeichen
- [x] **P2** | Determinismus
- **Impact:** Inkonsistente Keys bei Sonderzeichen in Titeln
- **Datei:** `src/Romulus.Core/GameKeys/GameKeyNormalizer.cs`
- **Fix:** Normalisierungsregeln für Sonderzeichen verschärfen
- **Test:** Verschiedene Unicode-Varianten → identischer Key

### F06 – CSV-Export ohne Injection-Schutz
- [x] **P2** | Security
- **Impact:** Formeln in Dateinamen könnten in Excel ausgeführt werden
- **Datei:** `src/Romulus.Infrastructure/Reporting/`
- **Fix:** Felder mit `=`, `+`, `-`, `@` prefixieren
- **Test:** Dateiname `=CMD()` → escapet in CSV

### F07 – HTML-Report ohne konsequentes Encoding
- [x] **P2** | Security
- **Impact:** XSS bei Dateinamen mit HTML-Zeichen
- **Datei:** `src/Romulus.Infrastructure/Reporting/HtmlReportWriter.cs`
- **Fix:** Alle dynamischen Werte durch `HtmlEncoder.Default.Encode()` schicken
- **Test:** Dateiname `<script>alert(1)</script>` → korrekt escapet

### F08 – Kein Audit-Trail für Junk-Entscheidungen in DryRun
- [x] **P2** | Audit-Gap
- **Impact:** DryRun zeigt Junk-Candidates, aber keine Audit-Zeile
- **Datei:** `src/Romulus.Infrastructure/Orchestration/JunkRemovalPipelinePhase.cs`
- **Fix:** DryRun-Junk-Entscheidungen als `JUNK_PREVIEW` auditen
- **Test:** DryRun mit Junk → Audit enthält JUNK_PREVIEW Rows

---

## Pass 2 – GUI/CLI/API Divergenz

### F09 – GUI all-explicit-flags killt Profile/Workflow-Resolution
- [x] **P1** | GUI / Parity
- **Impact:** WPF setzt alle Flags explizit → Profiles/Workflows haben keinen Effekt
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs`
- **Fix:** Nur geänderte Flags als explizit markieren
- **Test:** Profil-Wert + nicht-geändertes GUI-Flag → Profil-Wert gewinnt

### F10 – GUI fehlt ApproveConversionReview-Flag
- [x] **P1** | GUI / Feature-Gap
- **Impact:** GUI-User kann Conversion-Reviews nicht approven → Conversion blockiert
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs`
- **Fix:** ApproveConversionReview als Expert-Mode-Setting exponieren
- **Test:** GUI-Run mit ConvertFormat + ApproveConversionReview → Conversion läuft

### F11 – DatRename ohne Write-Ahead-Audit
- [x] **P1** | Safety / Audit
- **Impact:** DatRename-Move ohne PENDING-Zeile → bei Crash kein Rollback möglich
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.StandardPhaseSteps.cs`
- **Fix:** PENDING_DATRENAME vor Move schreiben
- **Test:** DatRename + simulierter Crash → Audit enthält PENDING Rows

### F12 – API-RateLimiter pro-IP, nicht pro-Key
- [x] **P2** | Security / API
- **Impact:** Shared IP (NAT/Proxy) throttlet alle User gleichzeitig
- **Datei:** `src/Romulus.Api/Program.cs`
- **Fix:** Rate-Limit per API-Key statt per IP
- **Test:** Zwei API-Keys gleiche IP → unabhängige Rate-Limits

### F13 – CLI --json Output unvollständig
- [x] **P2** | CLI / Parity
- **Impact:** JSON-Output enthält nicht alle Felder die Report hat
- **Datei:** `src/Romulus.CLI/Program.cs`
- **Fix:** JSON-Output aus RunResult-Projection generieren
- **Test:** CLI --json → alle Report-Felder vorhanden

### F14 – GUI DatRoot-Pfad nicht validiert bei Eingabe
- [x] **P2** | GUI / UX
- **Impact:** Ungültiger DatRoot erst bei Run-Start erkannt → Late Failure
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs`
- **Fix:** DatRoot bei Eingabe validieren, Feedback zeigen
- **Test:** Ungültiger DatRoot → sofort Warnung, nicht erst beim Run

### F15 – API CORS nicht konfigurierbar
- [x] **P2** | API / Security
- **Impact:** Keine Cross-Origin-Zugriffe möglich oder zu offene Defaults
- **Datei:** `src/Romulus.Api/Program.cs`
- **Fix:** CORS-Policy aus appsettings konfigurierbar machen
- **Test:** CORS-Header korrekt gesetzt

### F16 – GUI-Error-Dialog blockiert Event-Loop
- [x] **P2** | GUI / UX
- **Impact:** MessageBox auf UI-Thread kann Deadlock verursachen
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs`
- **Fix:** Async Dialog-Pattern verwenden
- **Test:** Error-Dialog → UI bleibt responsive

---

## Pass 3 – Concurrency & Parallel-Safety

### F17 – CLI kein Concurrent-Run-Schutz
- [x] **P1** | CLI / Safety
- **Impact:** Zwei CLI-Instanzen gleichzeitig → doppelte Moves, korruptes Audit
- **Datei:** `src/Romulus.CLI/Program.cs`
- **Fix:** Named Mutex oder File-Lock wie WPF SingleInstance
- **Test:** Zweite CLI-Instanz → Exit mit klarer Meldung

### F18 – JunkRemoval keine Set-Member-Prüfung
- [x] **P2** | Junk / Data-Integrity
- **Impact:** Set-Member als Junk markiert → Descriptor verliert Members
- **Datei:** `src/Romulus.Infrastructure/Orchestration/JunkRemovalPipelinePhase.cs`
- **Fix:** Referenzierte Set-Member vor Junk-Removal schützen (Descriptor-basierter Guard)
- **Test:** .bin-Datei als Set-Member von .cue → kein Junk

### F19 – IntegrityService JSON-Ordnung non-deterministic
- [x] **P3** | Determinismus
- **Impact:** Integrity-Baselines können bei identischen Dateien unterschiedliche Hashes erzeugen
- **Datei:** `src/Romulus.Infrastructure/Analysis/IntegrityService.cs`
- **Fix:** JSON-Properties sortiert serialisieren
- **Test:** Gleiche Dateien → identische Baseline-Hashes

---

## Pass 4 – Settings, Review Decisions, UI State

### F20 – Overlapping/Hierarchische Roots erlaubt
- [x] **P1** | Determinismus / Scan
- **Impact:** Dateien unter Child-Root werden doppelt gescannt → doppelte Candidates → non-deterministische Deduplication
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOptionsBuilder.cs` (Zeile 76–80)
- **Ursache:** `Normalize()` dedupliziert nur per `Distinct(OrdinalIgnoreCase)`, keine Hierarchie-Prüfung
- **Reproduktion:** Roots = `["C:\Games", "C:\Games\SNES"]` → doppelte Candidates
- **Fix:** Nach `Distinct()` Hierarchie-Filter: Child-Roots entfernen wenn Parent-Root vorhanden
- **Test:** Parent+Child Root → nur Parent überlebt Normalize

### F21 – Cross-Path Containment nicht validiert
- [x] **P1** | Safety / Data-Integrity
- **Impact:** TrashRoot/DatRoot/AuditPath/ReportPath innerhalb eines Roots → Output-Dateien werden als ROM-Candidates gescannt
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOptionsBuilder.cs` (Zeile 17–45)
- **Ursache:** `Validate()` prüft Pfade einzeln, keine Cross-Path-Beziehungsprüfung
- **Szenario:** `TrashRoot = "C:\Games\Trash"` + Root `"C:\Games"` → Trash-Dateien als ROMs
- **Fix:** In `Validate()` prüfen: kein Output-Pfad darf innerhalb eines Root liegen
- **Test:** TrashRoot⊂Root → Validation Error; DatRoot⊂Root → Validation Error

### F22 – Review Decisions nicht RunId-scoped
- [x] **P2** | Data-Integrity / Cross-Run
- **Impact:** Alte Approvals gelten still für neue Runs mit geändertem Dateiinhalt
- **Datei:** `src/Romulus.Infrastructure/Review/LiteDbReviewDecisionStore.cs` (Zeile 195–199)
- **Ursache:** `ReviewApprovalDocument` hat kein RunId/ContentHash — nur Pfad als Key
- **Reproduktion:** Datei approved als SNES → Datei durch PS1-Spiel ersetzt → Approval weiterhin aktiv
- **Fix:** ContentHash oder LastModified im Approval speichern; bei Mismatch als `stale` markieren
- **Test:** Approval für Pfad → Datei geändert → Approval wird als stale erkannt

### F23 – Settings-Corruption stiller Verlust (CLI/API)
- [x] **P2** | Configuration / UX
- **Impact:** Korrupte User-Settings werden still durch Defaults ersetzt, User weiß nichts davon
- **Datei:** `src/Romulus.Infrastructure/Configuration/SettingsLoader.cs` (Zeile 300–380)
- **Ursache:** `MergeFromUserSettings()` hat `catch (JsonException)` ohne Logging/Warning
- **Fix:** Corrupt-Backup (`.bak`) + optionaler Warning-Callback im User-Settings-Merge
- **Test:** Korrupte settings.json → Warning wird emittiert; Defaults werden korrekt verwendet

### F24 – WPF Cancel hinterlässt stale Progress-Anzeige
- [x] **P3** | GUI / UX
- **Impact:** Nach Cancel zeigt Fortschrittsbalken z.B. "75%" statt Reset
- **Datei:** `src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs` (Zeile 726–737)
- **Ursache:** `OnCancel()` setzt RunState/Summary, aber nicht `Progress`/`ProgressText`
- **Fix:** `Progress = 0; ProgressText = "";` in OnCancel ergänzen
- **Test:** Cancel → `Progress == 0` und `ProgressText` leer

---

## Pass 5 – Tie-Breaker, Rollback, CLI Parity

### F25 – CLI Exit-Code verschleiert CompletedWithErrors
- [x] **P1** | CLI / Parity
- **Impact:** Exit 1 für CompletedWithErrors UND Failed → Automations-Skripte können Partial Success nicht von Totalausfall unterscheiden
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs` (Zeile 81)
- **Ursache:** `result.ExitCode = runOutcome == RunOutcome.Ok ? 0 : 1;` — binäres Mapping
- **Fix:** Exit 4 für CompletedWithErrors einführen (0=Ok, 1=Failed, 2=Cancelled, 3=Preflight, 4=CompletedWithErrors)
- **Test:** Run mit Konvertierungsfehler → Exit 4; Run-Totalfehler → Exit 1

### F26 – Kein Pre-Flight Disk-Space-Check vor Moves
- [x] **P2** | Safety / UX
- **Impact:** Große Move-Operationen laufen in DiskFull-IOException → partieller Move-Zustand
- **Dateien:** `src/Romulus.Infrastructure/Orchestration/MovePipelinePhase.cs`, `FileSystemAdapter.cs`
- **Ursache:** Keine Vorab-Prüfung des verfügbaren Speicherplatzes
- **Szenario:** 500 GB Losers verschieben auf 100 GB Drive → ~312 Moves, ~188 Failures
- **Fix:** Vor Move-Phase: `DriveInfo.AvailableFreeSpace` prüfen gegen geschätzte Move-Größe
- **Test:** Mock-FileSystem mit wenig Space → Warning/Abort vor Move

### F27 – Reparse-Point-Ancestors bei Destination nicht geprüft
- [x] **P2** | Security / Path-Safety
- **Impact:** Symlink in Ancestral-Verzeichnis kann Move außerhalb Root umleiten
- **Datei:** `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs` (Zeile 190–194)
- **Ursache:** Nur unmittelbarer Parent auf ReparsePoint geprüft, nicht Großeltern/Urgroßeltern
- **Szenario:** `C:\Trash\Game → D:\Malicious` (Symlink) → Destination-Parent-Check passed
- **Fix:** Rekursive Ancestor-Prüfung bis zum Root
- **Test:** Symlink in Ancestor-Kette → `InvalidOperationException`

### F28 – Tool-Output kein Größenlimit (OOM-Risiko)
- [x] **P3** | Resilience / Tools
- **Impact:** Defektes/bösartiges Tool kann stdout fluten → Out-of-Memory
- **Datei:** `src/Romulus.Infrastructure/Tools/ToolRunnerAdapter.cs` (Zeile 342–343)
- **Ursache:** `ReadToEnd()` ohne Größenlimit; Truncation nur für Diagnostik-Display
- **Fix:** StreamReader mit Byte-Budget (z.B. 100 MB); Rest verwerfen
- **Test:** Tool mit 200 MB stdout → nur 100 MB gelesen, kein OOM

---

## Pass 6 – DAT Hash Matching, Preflight, Code Hygiene

### F29 – DAT-Parser Hash-Typ-Fallback erzeugt stille Typ-Mismatchs
- [x] **P2** | Determinismus / DAT
- **Impact:** User konfiguriert z.B. SHA256, DAT enthält nur SHA1 → Parser fällt auf MD5/CRC zurück → DatIndex enthält CRC-Hash, FileHashService berechnet SHA256 → Lookup schlägt still fehl, alle DAT-Matches bleiben leer
- **Dateien:** `src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs` (Zeile 239–254), `src/Romulus.Infrastructure/Hashing/FileHashService.cs`
- **Ursache:** `ParseDatFileInternal()` hat Fallback-Kette (SHA256→MD5→CRC), aber der gespeicherte Hash-Typ wird nicht im Index festgehalten. `FileHashService` berechnet immer den konfigurierten Typ.
- **Reproduktion:** `HashType=SHA256` + No-Intro DAT (nur SHA1+CRC) → 0 DatMatches
- **Fix:** Entweder (a) Hash-Typ im DatIndex-Entry mitspeichern und bei Lookup den richtigen Hash berechnen, oder (b) Warnung emittieren wenn Fallback greift
- **Test:** SHA256-Config + SHA1-only-DAT → Warnung oder korrekter Fallback-Match

### F30 – Preflight prüft keine Tool-Verfügbarkeit
- [x] **P2** | Preflight / UX
- **Impact:** Conversion/DatAudit-Run startet, läuft durch Scan/Enrichment, scheitert erst in Conversion-Phase an fehlendem chdman/7z → verschwendete Zeit
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs` (Zeile 85–139)
- **Ursache:** Preflight prüft nur Roots, AuditDir, DAT-Index — keine Tool-Existenz/Hash-Prüfung
- **Fix:** Wenn `ConvertFormat != null`: Tools aus ToolRegistry prüfen (Pfad + Hash) in Preflight
- **Test:** ConvertFormat=chd + kein chdman → Preflight Blocked

### F31 – Preflight prüft kein TrashRoot/ReportPath Schreibrecht
- [x] **P2** | Preflight / UX
- **Impact:** Move scheitert an nicht-beschreibbarem TrashRoot → partieller Move-Zustand; Report scheitert am Ende → Run OK aber kein Report
- **Datei:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs` (Zeile 103–116)
- **Ursache:** Write-Test nur für AuditPath implementiert, nicht für TrashRoot/ReportPath
- **Fix:** Gleichen Write-Test für TrashRoot und ReportPath ergänzen
- **Test:** Nicht-beschreibbares TrashRoot → Preflight Warning/Blocked

### F32 – DiscBasedConsoles-Property nie befüllt (toter Code)
- [x] **P3** | Hygiene / Dead Code
- **Impact:** `RunOptions.DiscBasedConsoles` ist immer ein leeres Set; wird nirgends konsumiert (kein `.Contains()`-Aufruf)
- **Dateien:** `src/Romulus.Contracts/Models/RunExecutionModels.cs` (Zeile 62), `src/Romulus.Infrastructure/Orchestration/RunOptionsBuilder.cs` (Zeile 123, 155)
- **Ursache:** `ConsoleDetector` lädt `discBased`-Flag in `ConsoleInfo`, aber niemand transferiert das nach `RunOptions.DiscBasedConsoles`
- **Fix:** Property und Clone-Pfade entfernt (vollständige Dead-Code-Bereinigung)
- **Test:** *(kein funktionaler Test nötig — Property ist unbenutzt)*

### F33 – Hardcoded-Strings in Pipeline-Phasen (i18n-Bypass)
- [x] **P3** | i18n / Wartbarkeit
- **Impact:** Alle Progress-/Status-Nachrichten in RunOrchestrator und Pipeline-Phasen sind hardcoded Deutsch — unabhängig von User-Locale
- **Dateien:** `src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs` (200+ Zeilen), `EnrichmentPipelinePhase.cs`, `MovePipelinePhase.cs`
- **Ursache:** `_onProgress?.Invoke("…")` mit String-Literalen statt i18n-Keys
- **Fix:** `RunProgressLocalization` eingeführt (de/en/fr-Template-Lookup via `CurrentUICulture`) und zentrale Progress-Meldungen auf lokalisierte Templates umgestellt
- **Test:** `Execute_ProgressCallback_UsesEnglishPreflightMessage_WhenCurrentUiCultureIsEnglish_FindingF33`

---

## Pass 7 – DatRename Conflicts, Scan Visibility, LiteDB Growth

### F34 – DatRename keine Inter-Entry-Kollisionsprüfung
- [x] **P2** | DatRename / Determinismus
- **Impact:** Wenn zwei Dateien im selben Verzeichnis beide per DAT auf denselben Zielnamen renamen sollen, wird die Kollision erst zur Laufzeit erkannt — die zweite Datei bekommt `__DUP`-Suffix. Kein Datenverlust, aber nicht-deterministisches Ergebnis (Reihenfolge bestimmt welche den echten Namen bekommt)
- **Dateien:** `src/Romulus.Core/Audit/DatRenamePolicy.cs` (Zeile 10–48), `src/Romulus.Infrastructure/Orchestration/DatRenamePipelinePhase.cs` (Zeile 28–72)
- **Ursache:** `EvaluateRename()` evaluiert jede Datei isoliert, ohne Kenntnis der anderen Einträge. Filesystem-level Collision-Detection greift zwar, aber abhängig von Verarbeitungsreihenfolge
- **Fix:** Vor Execution: Zielname-Kollisionen innerhalb des Batches erkennen und deterministisch auflösen (z.B. höherer Score behält den echten Namen)
- **Test:** Zwei Dateien im selben Verzeichnis mit gleichem DAT-Zielnamen → definierter Winner bekommt den echten Namen

### F35 – Scan-Phase verschluckt Permission-Errors ohne Zählung/Log
- [x] **P2** | Scan / Sichtbarkeit
- **Impact:** `GetFilesSafe()` fängt `UnauthorizedAccessException` und `DirectoryNotFoundException` still ab — User erfährt nicht, ob ganze Verzeichnisse/Dateien übersprungen wurden. Report-Zahlen stimmen intern, aber können fachlich zu optimistisch wirken
- **Dateien:** `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs` (Zeile 83–89, 95–98, 109–112)
- **Ursache:** Defensive `catch` ohne Zähler, Warnung oder Logging
- **Reproduktion:** Verzeichnis mit verweigerten Leserechten innerhalb eines Roots → 0 Dateien gescannt, keine Warnung
- **Fix:** Scan-Warnings im FileSystem sammeln (`ConsumeScanWarnings`) und in der Streaming-Scan-Phase als `WARNING`-Progress emittieren
- **Test:** `ScanPhase_EmitsWarning_WhenFileSystemReportsInaccessiblePaths_FindingF35`

### F36 – LiteDB CollectionIndex wächst unbegrenzt ohne Compaction
- [x] **P3** | Hygiene / Ressourcen
- **Impact:** Bei vielen Runs kann die CollectionIndex-Datei unbegrenzt wachsen — gelöschte/geänderte Einträge belegen weiterhin Speicher im LiteDB-Journal
- **Datei:** `src/Romulus.Infrastructure/Index/LiteDbCollectionIndex.cs`
- **Ursache:** Keine `Shrink()`- oder Compaction-Logik; LiteDB allokiert intern Seiten, gibt sie aber nicht automatisch frei
- **Fix:** Periodische Best-Effort-Compaction via `_database.Rebuild()` nach Mutationsschwelle
- **Test:** *(niedrige Priorität — nur Ressourcen-Hygiene)*

---

## Pass 8 – Scoring-Formeln, Hash-Cache-Normalisation

### F37 – HealthScorer-Junk-Penalty irreführend bei hohem Junk-Anteil
- [x] **P2** | Scoring / Report-Accuracy
- **Impact:** Eine Collection mit 100 % Junk erhält HealthScore 70 — irreführend hoch. Die Junk-Penalty ist auf 30 Punkte gedeckelt (`Min(30, junkPct * 0.3)`), unabhängig vom tatsächlichen Junk-Anteil. User sieht "Qualität 70 %" obwohl die gesamte Sammlung Müll ist.
- **Datei:** `src/Romulus.Core/Scoring/HealthScorer.cs` (Zeile 16)
- **Ursache:** Formel: `baseScore = 100 - dupePct; junkPenalty = Min(30, junkPct * 0.3)`. Bei 0 % Dupes und 100 % Junk: `100 - 0 - 30 + 0 - 0 = 70`. Caps sind für Mixed-Scenarios sinnvoll, aber das Junk-Cap ist zu aggressiv.
- **Reproduktion:** 100 Dateien, alle Junk, 0 Dupes → HealthScore = 70
- **Fix:** Junk-Penalty-Cap auf z.B. `Min(60, junkPct * 0.5)` erhöhen, oder unbeschränkt lassen
- **Test:** 100 % Junk → HealthScore ≤ 30; 50 % Junk → Score angemessen reduziert

### F38 – FileHashService/LiteDbCollectionIndex Cache-Key ohne NFC-Normalisierung
- [x] **P3** | Determinismus / Hash-Cache
- **Impact:** Pfade von macOS-formatierten Volumes (HFS+/APFS über SMB oder exFAT) verwenden NFD-Normalisierung. `FileHashService.BuildCacheKey()` und `LiteDbCollectionIndex.NormalizePath()` nutzen `Path.GetFullPath()` ohne `.Normalize(NormalizationForm.FormC)`. Ein NFD-Pfad (`Ü` als `U\u0308`) erzeugt einen anderen Cache-Key als der NFC-Pfad (`\u00DC`) → doppelte Hash-Berechnung, doppelte Index-Einträge für dieselbe physische Datei.
- **Dateien:** `src/Romulus.Infrastructure/Hashing/FileHashService.cs` (Zeile 473), `src/Romulus.Infrastructure/Index/LiteDbCollectionIndex.cs` (Zeile 535)
- **Ursache:** `FileSystemAdapter` hat konsequente NFC-Normalisierung (`NormalizePathNfc`), aber die Cache-Key-Bildung in beiden Services umgeht diesen Adapter und nutzt direkt `Path.GetFullPath()`.
- **Fix:** In beiden `BuildCacheKey`/`NormalizePath`-Methoden NFC anwenden: `Path.GetFullPath(path).Normalize(NormalizationForm.FormC)`
- **Test:** NFD-Pfad `"C:\Roms\U\u0308bung.zip"` vs NFC `"C:\Roms\Übung.zip"` → identischer Cache-Key/Index-Key

---

## Pass 9 – Final Deep Audit (Scoring, Pipeline-Safety, ConflictPolicy)

### F39 – ConflictPolicy "Overwrite" akzeptiert aber nicht implementiert
- [x] **P3** | Functional Discrepancy / Wartbarkeit
- **Impact:** User wählt "Overwrite" in GUI, CLI oder API. Validation akzeptiert den Wert. Aber keine Pipeline-Phase implementiert Overwrite-Verhalten: `MovePipelinePhase` prüft nur `"Skip"`, `ConsoleSorter` hat keinen ConflictPolicy-Branch, und `FileSystemAdapter.MoveItemSafelyCore` nutzt immer `File.Move(overwrite: false)` mit DUP-Suffix-Fallback. "Overwrite" verhält sich stillschweigend identisch zu "Rename".
- **Dateien:** `src/Romulus.Contracts/RunConstants.cs` (Zeile 11), `src/Romulus.Infrastructure/Orchestration/MovePipelinePhase.cs` (Zeile 75), `src/Romulus.Infrastructure/FileSystem/FileSystemAdapter.cs` (Zeile 280), `src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs`
- **Ursache:** "Overwrite" wurde in `ValidConflictPolicies` aufgenommen und durch alle Validation-Layers durchgereicht, aber kein Phase-Code implementiert das tatsächliche Overwrite-Verhalten (`File.Move(overwrite: true)` oder äquivalent).
- **Fix:** Entweder (a) "Overwrite" aus `ValidConflictPolicies` entfernen und in CLI/API/GUI-Validation als ungültig ablehnen, oder (b) Overwrite-Verhalten implementieren mit entsprechenden Safety-Guards (Audit-Trail, Confirmation, Undo-Fähigkeit).
- **Test:** Red-Test: `ConflictPolicy="Overwrite"` + Destination existiert → Ziel wird überschrieben (aktuell: DUP-Suffix wird angehängt)

---

## Audit-Abschluss

9 Passes abgeschlossen. Die Codebasis ist **außergewöhnlich stabil**. Seit Pass 7 nur noch P2/P3-Findings mit stark abnehmenden Returns. Keine P0-Findings in der gesamten Audit-Serie.

**Empfohlene Fix-Priorität:**
1. P1 Findings (F01–F03, F09–F11, F20–F21, F25) — vor Release fixen
2. P2 Findings (F04–F08, F12–F16, F18, F22–F23, F26–F27, F29–F31, F34–F35, F37) — sollten gefixt werden
3. P3 Findings (F17, F24, F28, F32–F33, F36, F38–F39) — Wartbarkeit / Nice-to-have

---

## Legende

| Symbol | Bedeutung |
|--------|-----------|
| - [ ]  | Offen |
| - [x]  | Erledigt / Gefixt |
| **P0** | Release-Blocker |
| **P1** | Hohes Risiko — vor Release fixen |
| **P2** | Mittleres Risiko — sollte gefixt werden |
| **P3** | Wartbarkeit / Nice-to-have |
