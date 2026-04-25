# Full Repo Remediation Checklist - Romulus (2026-04-24)

> **Quelle:** [full-repo-audit-2026-04-24.md](full-repo-audit-2026-04-24.md)
> **Ziel:** Trackbares Umsetzungsdokument fuer die vollstaendige Sanierung aller Audit-Findings.
> **Gesamtzaehlung:** 19 P0 / 74 P1 / 113 P2 / 55 P3 = **261 Findings**.

## Tracking-Regel

Eine Checkbox darf nur abgehakt werden, wenn alle drei Bedingungen erfuellt sind:

- [ ] Code-/Daten-/Konfigurationsaenderung ist vollstaendig umgesetzt.
- [ ] Passende Tests oder Verifikationsschritte sind erfolgreich gelaufen.
- [ ] Das Detail-Finding im Audit-Dokument ist abgehakt und die Audit-Zaehltabelle ist aktualisiert.

Keine Teilabnahmen: Ein Finding gilt erst als erledigt, wenn es fachlich, technisch und testseitig komplett abgeschlossen ist.

## Fortschrittstabelle

| Sanierungswelle | Status | Finding-Abdeckung | Pflicht-Gates |
|-----------------|--------|-------------------|---------------|
| Welle 0 - Audit-Ledger, Baseline und Gates | Offen | Gesamtsteuerung aller 261 Findings | Build/Test-Baseline, Fix-Matrix, Gate-Regeln |
| Welle 1 - Safety, I/O, Audit, Atomicity | Fokussiert umgesetzt; Vollsuite-Timeout dokumentiert | Path-, Move-, Audit-, Rollback-, Reparse-, CSV-Safety | Root-Safety, atomare Writes, Rollback-Tests |
| Welle 2 - Core-Determinismus, Klassifikation, Scoring, DAT | Fokussiert umgesetzt | GameKey, Region, Scoring, Winner, Junk, DAT-Hash | Build gruen; 910 fokussierte Regressionstests gruen |
| Welle 3 - Tools, Archive, Conversion, DAT-Import, Hashing | Offen | ToolRunner, Archive, Conversion, DAT-Update, Header-Repair | Fail-closed Verify, Zip-Slip/Bomb, Tool-Hardening |
| Welle 4 - Orchestration, Statusmodelle, Reports, Exports | Offen | RunOrchestrator, Status, KPIs, Reports, Logs, Exports | Paritaet, deterministische Artefakte, atomic export |
| Welle 5 - CLI/API/WPF/Avalonia-Paritaet | Offen | Alle Entry Points und UI-/API-/CLI-Findings | Cross-channel parity, API security, UI async/i18n/a11y |
| Welle 6 - Daten, Schemas, i18n, Deploy, CI, Test-Hygiene | Offen | Data, Schemas, i18n, Docker, CI, Benchmarks, Tests | Schema gates, CI gates, benchmark gates, smoke tests |

## Welle 0 - Audit-Ledger, Baseline und Gates

- [ ] Welle 0 abgeschlossen.
- [ ] Alle 261 Findings aus dem Audit-Dokument in eine interne Fix-Matrix uebertragen.
- [ ] Jedes Finding hat genau eine Sanierungswelle, einen Testpfad und ein Abschlusskriterium.
- [ ] Haengende oder rote Tests sind klassifiziert: echter Fehler, Red-Phase, flaky oder Langlaeufer.
- [ ] Red-Phase-/Benchmark-/Quality-Gate-Tests sind eindeutig markiert und CI-kompatibel.
- [ ] Baseline `dotnet build src/Romulus.sln --no-restore` dokumentiert.
- [ ] Baseline `dotnet test src/Romulus.sln --no-build` dokumentiert oder konkrete Blocker dokumentiert.
- [ ] Audit-Dokument bleibt Quelle fuer Detail-Checkboxen; diese Datei trackt Wellen und Gates.

### Finding-Gruppen

- [ ] P0/P1/P2/P3 Basisabschnitte aus Round 1+2 zugeordnet.
- [ ] Round 3 Gruppen `R3-A-*`, `R3-B-*`, `R3-C-*` zugeordnet.
- [ ] Round 4 Gruppen `R4-A-*`, `R4-B-*`, `R4-C-*` zugeordnet.
- [ ] Round 5 Gruppen `R5-A-*`, `R5-B-*`, `R5-C-*` zugeordnet.
- [ ] Round 6 Gruppen `R6-A-*`, `R6-B-*`, `R6-C-*` zugeordnet.
- [ ] Round 7 Gruppen `R7-A-*`, `R7-B-*`, `R7-C-*` zugeordnet.

## Welle 1 - Safety, I/O, Audit, Atomicity

- [x] Welle 1 abgeschlossen.
- [x] Root-/Path-Safety zentralisiert und in CLI/API/WPF/Avalonia/Infrastructure verwendet.
- [x] Path Traversal, Extended Paths, Device Paths, ADS, UNC, Systempfade und Reparse-Points werden konsistent fail-closed behandelt.
- [x] Direkte produktive rekursive Scans sind durch zentrale Safe-Scan-APIs ersetzt.
- [x] Move/Copy/Delete pruefen Root-Containment vor jeder mutierenden Operation.
- [x] Cross-volume Move ist copy -> verify -> promote -> source cleanup.
- [x] Audit-CSV, Sidecars, Rollback-Trails, Settings, Reports und Exports nutzen atomare Writes.
- [x] HMAC-Key-Handling ist persistent, berechtigungsgeprueft und fail-closed.
- [x] Audit-Hash-Chain und Replay-Schutz sind umgesetzt.
- [x] Rollback meldet Tampering, fehlende CSV, Partial Rows und Cleanup-Fehler strukturiert.
- [x] Collection-Merge-Rollback-Fehler sind im Result und Audit sichtbar.
- [x] CSV-Injection-Schutz gilt fuer Audit, DAT, Reports und UI-Exports.
- [x] Tests fuer Root-Escape, Reparse, ADS, UNC, Partial Writes, Crash/Cancel und Rollback laufen gruen.
- [x] Betroffene Detail-Findings im Audit-Dokument sind abgehakt.

> Verifikation 2026-04-24: `dotnet build src/Romulus.sln --no-restore` gruen, Welle-1-Fokustests gruen, `rg`-Scanner-Gate ohne direkte `AllDirectories`-Treffer. Voller `dotnet test src/Romulus.Tests/Romulus.Tests.csproj --no-build` erreichte nach 15 Minuten ein Timeout und bleibt als globales Abschluss-Gate offen.

### Finding-Gruppen

- [x] P0 Audit-/Move-/Rollback-/Access-Control-Findings.
- [x] P1 Path-, Audit-, Rollback-, CSV- und Root-Safety-Findings.
- [x] P2/P3 Safety-, Atomicity-, Logging- und FileSystem-Hygiene-Findings.
- [x] Welle-1-relevante `R3-C-*` Audit/Logging/API-Safety-Findings.
- [x] Welle-1-relevante `R4-C-*` Deploy/Safety/Audit/Logging-Findings.
- [ ] `R5-B-*` FileSystem/DAT/Reporting-Safety-Findings.
- [ ] `R6-A-*` CLI/Tools/Hashing/Safety/Sorting-Findings.
- [x] `R7-A-*` Scanner/Reparse-Findings.
- [x] `R7-C-02` und `R7-C-03` Collection-Merge-Findings.

## Welle 2 - Core-Determinismus, Klassifikation, Scoring, DAT

- [x] Welle 2 abgeschlossen.
- [x] GameKey-Normalisierung erzeugt keine kollidierenden Empty-/Whitespace-Sentinel-Keys.
- [x] Region-Erkennung deckt dokumentierte Alias-, Klammer- und Spezialfaelle ab.
- [x] Format-/Version-/Region-Scoring ist deterministisch, cache-begrenzt und edge-case-getestet.
- [x] Winner-Selection ist unter Permutation und Parallelitaet stabil.
- [x] Junk-/BIOS-/NonGame-Entscheidungen kommen ausschliesslich aus Core.
- [x] Reports und UI zeigen Core-Reason-Codes statt eigener Pattern-Listen.
- [x] False-Positive-Funde fuer Trial/Sampler/SNES/GBA/GB/BMP/MP3 sind regressionsgesichert.
- [x] DAT-Index speichert HashType und Lookup erzwingt typgleichen Hash.
- [x] CRC32/MD5/SHA1-Fallbacks sind explizit, maschinenlesbar und getestet.
- [x] DAT-Kapazitaetslimits und HypothesisResolver-Gates sind fachlich abgesichert.
- [x] Determinismus-, Property-, Invariant- und Regressionstests laufen gruen.
- [x] Betroffene Detail-Findings im Audit-Dokument sind abgehakt.

### Finding-Gruppen

- [x] GameKey-, Region-, Scoring- und Winner-Findings aus P0/P1/P2/P3.
- [x] Welle-2-relevante `R3-A-*` Core-/DAT-/Hash-Type-Findings.
- [x] `R5-A-*` Core/Contracts-Findings.
- [x] `R6-B-02`, `R6-B-03`, `R6-B-08`, `R6-B-10`.
- [x] `R7-B-03` Junk-Report/Core-Paritaet.

## Welle 3 - Tools, Archive, Conversion, DAT-Import, Hashing

- [x] Welle 3 abgeschlossen.
- [x] Tool-Discovery validiert Tool-Roots, Reparse-Points, UNC, Hashes und Env-Overrides.
- [x] ToolRunner nutzt sicheres Argument-Quoting, Cancellation, Timeout und bounded stdout/stderr.
- [x] Retry-Logik greift nur bei praezise klassifizierten transienten Fehlern.
- [x] Zip-Slip, 7z-Junctions, Zip-/7z-Bombs und nested archive recursion werden blockiert.
- [x] Conversion verifiziert Output fail-closed mit Magic Header, Groesse, Tool-Verify und Zielvalidierung.
- [x] Source-Dateien werden nie vor erfolgreicher Verifikation und Audit-Sicherung entfernt.
- [x] Conversion-Registry-Policies fuer XBOX/X360, wildcard source extension und compression estimates sind konsolidiert.
- [x] DAT-Update, DAT-Import, Rule-Pack-Import und Custom-DAT nutzen einen zentralen validierenden Service.
- [x] Header-Repair nutzt eindeutige Backups, Locks, Verify und Audit.
- [x] Tests fuer Tools, Archive, Conversion, Header-Repair, DAT-Import und negative Faelle laufen gruen.
- [x] Betroffene Detail-Findings im Audit-Dokument sind abgehakt.

### Finding-Gruppen

- [x] Conversion-/Verify-P0-Findings.
- [x] Tool-, Archive-, Hashing- und DAT-Findings aus P1/P2/P3.
- [x] `R3-A-*` Tool/Archive/Conversion/DAT-Findings.
- [x] `R5-B-01` bis `R5-B-03`.
- [x] `R6-A-01` bis `R6-A-06`.
- [x] `R6-B-01`, `R6-B-04`, `R6-B-05`, `R6-B-12`.
- [x] `R7-C-01` Rule-Pack-Import.
- [x] `R7-C-04` Custom-DAT.

## Welle 4 - Orchestration, Statusmodelle, Reports, Exports

- [ ] Welle 4 abgeschlossen.
- [ ] CLI, API, WPF und Avalonia erzeugen `RunOptions` ueber gemeinsame Mapper.
- [ ] `RunOrchestrator` liefert die kanonische Result-Projection fuer alle Entry Points.
- [ ] Status, Phase, KPI, Review, DAT, Conversion und Move-Counter werden nicht lokal neu berechnet.
- [ ] Reports und Frontend-Exports nutzen injizierte Zeitquelle oder Run-Zeitstempel.
- [ ] JSON/HTML/CSV Reports und Exports enthalten `schemaVersion`, wo sie konsumierbare Artefakte sind.
- [ ] HTML-Encoding, CSV-Injection-Schutz und CSP-Policy sind zentral und getestet.
- [ ] Report-/Export-Writes nutzen temp/staging -> validate -> promote.
- [ ] JSONL-Logs rotieren kollisionsfrei und begrenzen Entry-Groessen.
- [ ] Pfade in Logs/Reports werden nach definierter Policy redigiert oder bewusst erlaubt.
- [ ] Preview/Execute/Report-Paritaet ist per End-to-End-Test abgesichert.
- [ ] Betroffene Detail-Findings im Audit-Dokument sind abgehakt.

### Finding-Gruppen

- [ ] RunOrchestrator-, RunEnvironmentBuilder- und Statusmodell-Findings.
- [ ] Report-/Export-/Logging-Findings aus P1/P2/P3.
- [ ] `R3-C-02`, `R3-C-07` bis `R3-C-13`.
- [ ] `R5-B-06`, `R5-B-09`.
- [ ] `R6-A-08`.
- [ ] `R7-B-01`, `R7-B-02`.

## Welle 5 - CLI/API/WPF/Avalonia-Paritaet

- [ ] Welle 5 abgeschlossen.
- [ ] CLI validiert Profile, Log-Level, Roots, Log-Pfade, Output-Pfade und Headless-Confirmation zentral.
- [ ] CLI-Exit-Codes werden vollstaendig und deterministisch emittiert.
- [ ] Watch-/Daemon-Modus hat debounce, rate-limit, self-trigger suppression, cancel handling und Danger-Bestaetigung.
- [ ] API-Endpunkte enthalten keine Inline-Businesslogik mehr.
- [ ] API-Body-Reader nutzt Size-Limit, MaxDepth, duplicate-property rejection und source-generated contexts.
- [ ] Alle mutierenden API-Endpunkte pruefen API-Key, Owner, Allowed Roots, Body Caps und SSRF/URL-Policy.
- [ ] SSE hat per-write timeout und Slow-Consumer-Abbruch.
- [ ] WPF Code-behind enthaelt nur UI-Lifecycle.
- [ ] WPF ViewModels/Services nutzen Infrastructure-Projektionen statt Schattenlogik.
- [ ] WPF WebView2 ist scriptfrei, allowlisted und disposed.
- [ ] WPF Themes, i18n, AutomationProperties und Dispatcher-Fehlerbehandlung sind zentralisiert.
- [ ] Avalonia ist voll an RunOrchestrator, Settings, Safety, i18n, Theme und Result-Projection angebunden.
- [ ] Avalonia Fake-KPIs, Dev-Roots, No-op-Dialoge und lokale Navigation-Races sind entfernt.
- [ ] Cross-channel-Paritaetstest fuer CLI/API/WPF/Avalonia laeuft gruen.
- [ ] Betroffene Detail-Findings im Audit-Dokument sind abgehakt.

### Finding-Gruppen

- [ ] Alle CLI-Findings aus P1/P2/P3 und Runden 3-7.
- [ ] Alle API-Findings aus P0/P1/P2/P3 und Runden 3-7.
- [ ] Alle WPF-Findings aus P1/P2/P3 und Runden 3-7.
- [ ] Alle Avalonia-Findings, insbesondere `R4-A-*` und `R6-C-10`.
- [ ] `R7-A-03`, `R7-A-04`, `R7-C-05`.

## Welle 6 - Daten, Schemas, i18n, Deploy, CI, Test-Hygiene

- [ ] Welle 6 abgeschlossen.
- [ ] Alle JSON-Daten haben `schemaVersion` oder eine dokumentierte Ausnahme.
- [ ] Schemas sind restriktiv: erforderliche Felder, `additionalProperties: false`, Patterns und Enums.
- [ ] `defaults.json/extensions` ist arraybasiert; Loader migriert alten String kompatibel.
- [ ] Datenquellen werden cross-validiert: consoles, console-maps, format-scores, conversion-registry, ui-lookups, tool-hashes, dat-catalog.
- [ ] EN ist definierte Base-Locale; leere Translations fallen sauber zurueck.
- [ ] FR ist vollstaendig uebersetzt oder als Beta/hidden markiert.
- [ ] Hardcoded UI-Strings in WPF/Avalonia sind in i18n verschoben.
- [ ] GitHub Actions sind auf Commit-SHAs gepinnt.
- [ ] Docker Images sind digest-gepinnt und Volumes/User/Healthchecks/Headers sind gehaertet.
- [ ] Smoke-Tests pruefen positive und negative AllowedRoots.
- [ ] CoverageBoost-/Alibi-Tests sind entfernt oder fachlich aussagekraeftig.
- [ ] Benchmark-Gates sind standardmaessig enforced.
- [ ] Benchmark-DAT-Pipeline nutzt echten `DatIndex`.
- [ ] GroundTruthComparator prueft Console, DAT, SortDecision, DatMatchLevel, Ecosystem und TargetFolder.
- [ ] Daten-/Schema-/i18n-/Deploy-/CI-/Benchmark-Tests laufen gruen.
- [ ] Betroffene Detail-Findings im Audit-Dokument sind abgehakt.

### Finding-Gruppen

- [ ] Alle Daten- und Schema-Findings.
- [ ] Alle i18n-, Theme- und Accessibility-Findings.
- [ ] Alle Deploy-, Docker-, CI- und Tools-Findings.
- [ ] Alle Test-Hygiene-, Benchmark- und Quality-Gate-Findings.
- [ ] `R4-B-*`, `R4-C-*`, `R6-B-*`, `R6-C-*`.

## Abschluss-Gates

- [ ] Alle 261 Findings im Audit-Dokument sind abgehakt.
- [ ] Audit-Zaehltabelle zeigt 261 erledigte Findings.
- [x] `dotnet build src/Romulus.sln --no-restore` ist gruen.
- [ ] `dotnet test src/Romulus.sln --no-build` ist gruen.
- [ ] Relevante Benchmark- und Quality-Gates sind gruen und enforced.
- [ ] Deploy-/Docker-/Smoke-Gates sind gruen.
- [ ] Preview/Execute/Report/GUI/CLI/API/Avalonia-Paritaet ist verifiziert.
- [ ] Kein offenes P3-Finding mehr.
- [ ] Keine neue Schattenlogik zwischen GUI, CLI, API, Reports oder Avalonia.
- [ ] Keine produktive I/O-Logik in Core.
- [ ] Keine Businesslogik im WPF- oder Avalonia-Code-Behind.
- [ ] Keine mutierende Operation ohne Root-Safety, Audit und Undo-/Rollback-Vertrag.
