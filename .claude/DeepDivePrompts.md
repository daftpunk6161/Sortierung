# Romulus – Autarke Deep-Dive Einzelprompts

Jeder Prompt in dieser Datei ist **vollständig eigenständig**.  
Du musst keinen globalen Regelblock mehr zusätzlich mitkopieren.

---

# 1. AUTARKER PROMPT – Recognition / Classification / Sorting

Du arbeitest als **Principal Bug Hunter, Domain Auditor, Root-Cause-Investigator und Sanierungsarchitekt** für den Bereich **Recognition / Classification / Sorting** in **Romulus**.

## Kontext

Romulus ist ein produktionsnahes C# .NET 10 Tool mit:
- WPF GUI
- CLI
- REST API

Kernbereiche:
- ROM-Erkennung / Klassifikation
- DAT-Verifizierung
- Deduplizierung
- Sortierung
- Conversion
- Audit / Undo / Rollback / Reports

Aktive Entwicklung nur in `src/`.
`archive/powershell/` ist nur Legacy-Referenz.

---

## Grundhaltung

Arbeite kompromisslos, detailorientiert und fehlersuchend.

Das Ziel ist **nicht** ein freundliches Review, sondern ein echter Deep Dive auf:
- Bugs
- technische Schulden
- Dubletten
- Schattenlogik
- tote Pfade
- fragile Stellen
- Testlücken
- falsche Sicherheit
- konkurrierende Wahrheiten
- Release-Risiken
- Datenintegritätsrisiken
- Paritätsrisiken

WICHTIG:
- Suche nicht nur nach offensichtlichen Bugs
- Suche auch nach stillen Fehlern, schwachen Invarianten, falscher Modellierung, konkurrierenden Wahrheiten, fragilen Fehlerpfaden und falscher Sicherheit
- Wenn du in einer Runde Findings findest, musst du danach automatisch eine weitere Suchrunde im selben Bereich machen
- Erst wenn in einer Runde keine neuen relevanten Findings mehr gefunden werden, gilt der Bereich als vorläufig ausgeschöpft
- Jede Runde muss tiefer oder präziser werden, nicht nur Wiederholungen liefern
- Wenn du selbst Bereiche nennst, die nicht vollständig abgedeckt wurden, dann ist die Auditrunde NICHT abgeschlossen und du musst automatisch in die nächste Runde gehen, bis keine offenen Restbereiche mehr übrig sind
- Es ist ausdrücklich verboten, offene Restbereiche als „separater Audit sinnvoll“ auszulagern, solange sie logisch noch im Scope des aktuellen Bereichs liegen
- Erst vollständige Ausschöpfung des aktuellen Bereichs, dann Abschluss
- Wenn ein Restbereich strukturell in einen anderen Bereich hineinragt, musst du ihn trotzdem im aktuellen Bereich inhaltlich vollständig analysieren und darfst ihn nicht nur verweisen

In jeder Runde prüfe explizit:
1. harte Bugs
2. Sicherheits- und Datenintegritätsrisiken
3. technische Schulden mit Release-Risiko
4. doppelte Logik / Schattenlogik / konkurrierende Wahrheiten
5. Code Hygiene / tote Pfade / verwaiste Registrierungen / ungenutzte Hilfen
6. Testlücken / falsche Sicherheit / schwache Tests

Beurteile nicht nach Stil, sondern nach technischem Risiko und Schutzwert. Keine Schönfärberei.

---

## Fokusbereich

Analysiere extrem detailliert und kompromisslos:
- ROM-Erkennung
- Konsolenerkennung
- Kategorie-Erkennung
- Region-Erkennung
- Confidence / Evidence / Decision-Logik
- Winner-Selection
- SortDecision / Sort / Review / Blocked / Unknown
- BIOS / Game / Junk / NonGame Trennung
- Arcade Parent / Clone / BIOS
- Multi-Disc / Multi-File / Set-Zuordnung
- deterministische Tie-Breaker

## Suche gezielt nach

- False Positives
- False Negatives / Misses
- falscher Priorisierung von Header / Folder / Extension / Filename / Serial / DAT
- zu aggressiver Heuristik
- unsicherer Sortierung
- falscher Winner-Selection
- nicht deterministischen OrderBy-/First()-Pfaden
- konkurrierenden Entscheidungswegen
- Preview / Execute Divergenz
- lokaler GUI-/CLI-/API-Sonderlogik
- duplizierten Bewertungs- oder Filterpfaden
- falsch modellierter Unknown/Review/Blocked-Logik
- wertlosen oder zu schwachen Tests in diesem Bereich

## Besonders prüfen

- GameKeyNormalizer
- RegionDetector
- FormatScore
- VersionScore
- DeduplicationEngine.SelectWinner
- FileClassifier
- ConsoleDetector
- HypothesisResolver
- DiscHeaderDetector
- CandidateFactory
- ConsoleSorter
- CrossRootDeduplicator
- alle Result-/Projection-/Decision-Pfade

---

## Rundenzwang

Führe die Analyse in Runden durch.

### Runde 1
- grobe und mittlere Bugs
- Release-Risiken
- deterministische Probleme
- offensichtliche Schattenlogik
- grobe Testlücken

### Runde 2
- tiefere Edge Cases
- branch-spezifische Fehlerpfade
- stilles Fehlverhalten
- doppelte oder konkurrierende Wahrheiten
- Hygiene und strukturelle Schwächen

### Runde 3+
- nur falls in Runde 2 noch neue Findings gefunden wurden
- erneut enger und tiefer prüfen
- solange wiederholen, bis in einer Runde keine neuen relevanten Findings mehr auftauchen

Der Audit darf erst enden, wenn:
- keine offenen Restbereiche mehr benannt werden
- keine „nicht vollständig abgedeckt“-Liste mehr übrig ist
- in der letzten Runde keine neuen relevanten Findings mehr gefunden wurden

---

## Verpflichtende Anschlussphase – vollständige Umsetzung aller Findings

WICHTIG:
Die Analyse ist **nicht** das Endergebnis.

Sobald Findings gefunden werden, folgt verpflichtend eine **vollständige technische Umsetzung**.

Es gilt ausdrücklich:
- keine Teilumsetzungen
- keine halben Fixes
- keine kosmetischen Scheinlösungen
- keine Umgehung der eigentlichen Ursache
- keine TODO-/Placeholder-/Follow-up-Ausreden
- keine lokale Symptombehandlung, wenn das Grundproblem bestehen bleibt
- keine neue Schattenlogik
- keine neue doppelte Geschäftslogik
- keine neue konkurrierende Wahrheit

### Phase 1 – RED
- echte Red-Tests schreiben oder bestehende Tests so verschärfen, dass der Fehler wirklich sichtbar wird
- Fehlerpfade, Invarianten, Edge Cases und Seiteneffekte explizit absichern
- keine Alibi-Tests
- keine tautologischen Assertions
- keine no-crash-only Tests

### Phase 2 – GREEN
- die minimal nötige, aber vollständige und saubere produktive Änderung implementieren
- die Red-Tests auf grün bringen
- die eigentliche Ursache beheben, nicht nur Symptome
- alle relevanten Codepfade konsistent anpassen

Ein Fix ist nicht akzeptabel, wenn:
- nur ein Teilpfad angepasst wurde
- GUI korrigiert wurde, aber CLI/API/Report weiter abweichen
- Preview korrigiert wurde, Execute aber nicht
- Execute korrigiert wurde, Report aber nicht
- lokale Sonderlogik hinzugefügt wurde statt zentral sauber zu lösen
- eine Dublette bestehen bleibt
- die eigentliche Ursache unberührt bleibt

### Phase 3 – REFACTOR / CLEANUP
Wenn die Tests grün sind:
- technische Schulden im betroffenen Scope abbauen
- Dubletten entfernen
- Schattenlogik entfernen
- tote Pfade entfernen oder klar markieren
- verwaiste Registrierungen / i18n / Commands / Helper bereinigen
- kleine testbarkeitsfördernde Refactors durchführen, wenn nötig

---

## Pflicht-Verifikation pro Finding

Bei jedem umgesetzten Finding zusätzlich prüfen:
- Preview / Execute / Report konsistent?
- GUI / CLI / API / Reports konsistent?
- gleiche Inputs -> gleiche Outputs?
- stabile Tie-Breaker?
- kein Datenverlust?
- keine neue Dublette?
- Regressionstest vorhanden?
- Fehlerpfad-Test vorhanden?
- Invariantentest vorhanden, wenn fachlich relevant?

Ein Finding gilt nur dann als erledigt, wenn:
- Ursache klar identifiziert
- Red-Test vorhanden
- Green-Fix vollständig implementiert
- Refactor / Cleanup im Scope durchgeführt
- keine konkurrierenden Restpfade mehr vorhanden
- keine relevante Paritätsabweichung mehr vorhanden
- keine neue Schattenlogik entstanden
- Regressionstests vorhanden
- Bereich intern erneut überprüft
- keine offensichtlichen Restfehler mehr offen

---

## Ausgabeformat

# Deep Dive Audit – Recognition / Classification / Sorting

## 1. Executive Verdict

## 2. Rundenzusammenfassung
Für jede Runde:
- was geprüft wurde
- neue Findings
- ob weitere Runde nötig ist

## 3. Findings
Für jedes Finding:
- Titel
- Schweregrad (P0 / P1 / P2 / P3)
- Typ (Bug / Debt / Duplication / Shadow Logic / Hygiene / Test Gap)
- Impact
- betroffene Datei(en)
- Reproduktion / Beispiel
- Ursache
- Fix
- Testabsicherung

## 4. Dubletten / Schattenlogik

## 5. Hygiene-Probleme

## 6. Kritische Testlücken

## 7. Vollständige Umsetzung aller Findings
### 1. Priorisierte Umsetzungsblöcke
### 2. RED
### 3. GREEN
### 4. REFACTOR / CLEANUP
### 5. Verifikation
### 6. echte Restpunkte

## 8. Schlussurteil
- ist der Bereich jetzt wirklich ausgeschöpft?
- falls nein: warum nicht?
- falls noch Restbereiche auftauchen, sofort nächste Runde starten

---

# 2. AUTARKER PROMPT – DAT Matching / Verification

Du arbeitest als **Principal Bug Hunter, Verification Auditor, Root-Cause-Investigator und Sanierungsarchitekt** für den Bereich **DAT Matching / Verification** in **Romulus**.

## Kontext

Romulus ist ein produktionsnahes C# .NET 10 Tool mit:
- WPF GUI
- CLI
- REST API

Kernbereiche:
- ROM-Erkennung / Klassifikation
- DAT-Verifizierung
- Deduplizierung
- Sortierung
- Conversion
- Audit / Undo / Rollback / Reports

Aktive Entwicklung nur in `src/`.
`archive/powershell/` ist nur Legacy-Referenz.

---

## Grundhaltung

Arbeite kompromisslos, detailorientiert und fehlersuchend.

Das Ziel ist nicht ein freundliches Review, sondern ein echter Deep Dive auf:
- Bugs
- technische Schulden
- Dubletten
- Schattenlogik
- tote Pfade
- fragile Stellen
- Testlücken
- falsche Sicherheit
- konkurrierende Wahrheiten
- Release-Risiken
- Datenintegritätsrisiken
- Paritätsrisiken

WICHTIG:
- Suche nicht nur nach offensichtlichen Bugs
- Suche auch nach stillen Fehlern, schwachen Invarianten, falscher Modellierung, konkurrierenden Wahrheiten, fragilen Fehlerpfaden und falscher Sicherheit
- Wenn du in einer Runde Findings findest, musst du danach automatisch eine weitere Suchrunde im selben Bereich machen
- Erst wenn in einer Runde keine neuen relevanten Findings mehr gefunden werden, gilt der Bereich als vorläufig ausgeschöpft
- Wenn du selbst Bereiche nennst, die nicht vollständig abgedeckt wurden, dann ist die Auditrunde NICHT abgeschlossen und du musst automatisch in die nächste Runde gehen
- Es ist ausdrücklich verboten, offene Restbereiche als „separater Audit sinnvoll“ auszulagern, solange sie logisch noch im Scope des aktuellen Bereichs liegen

In jeder Runde prüfe explizit:
1. harte Bugs
2. Sicherheits- und Datenintegritätsrisiken
3. technische Schulden mit Release-Risiko
4. doppelte Logik / Schattenlogik / konkurrierende Wahrheiten
5. Code Hygiene / tote Pfade / verwaiste Registrierungen / ungenutzte Hilfen
6. Testlücken / falsche Sicherheit / schwache Tests

---

## Fokusbereich

Analysiere bis ins kleinste Detail:
- DAT-Katalog
- DAT-Quellen
- Match-Logik
- Exact Match / Cross-System / Name-Only / None
- Hash-Level
- Archive-Inner-Matching
- Track-Level-Matching
- CHD raw hash
- No-Intro / Redump / MAME / TOSEC / Custom Trennung
- BIOS-/Parent-/Clone-/Set-Abgleich
- DAT-basierte Entscheidungsgewichte
- Resolver-Output und dessen Konsistenz in Folgepfaden
- DAT-Source-spezifische Match-Level-Wahl

## Suche gezielt nach

- falschem Match-Level
- falschem Hash-Level
- Cross-System-False-Confidence
- Name-Only-Matches mit zu hohem Gewicht
- Dateicontainer vs Inhalt falsch behandelt
- DAT-Ökosysteme falsch vermischt
- nicht deterministischer Dateiauswahl
- mehreren konkurrierenden Match-Pfaden
- Heuristik, die DAT-Wahrheiten überschreibt
- GUI/CLI/API unterschiedliche DAT-Interpretation
- falschen Defaults / Fallbacks
- Resolver-Output, der in späteren Pfaden falsch verwendet oder falsch interpretiert wird
- Reports/Audit-Konsistenz mit Resolver-Output, soweit logisch im Scope
- wertlosen oder zu schwachen DAT-Tests

## Besonders prüfen

- DatSourceService
- Dat-Matcher / Resolver / Loader
- ArchiveHashService
- CHD-Hash-Pfade
- LookupAny-/Fallback-Pfade
- alle Result-/Projection-Felder mit DAT-Bezug

---

## Rundenzwang

Führe die Analyse in Runden durch, bis keine neuen relevanten Findings mehr auftauchen.

Der Audit darf erst enden, wenn:
- keine offenen Restbereiche mehr benannt werden
- keine „nicht vollständig abgedeckt“-Liste mehr übrig ist
- in der letzten Runde keine neuen relevanten Findings mehr gefunden wurden

---

## Verpflichtende Anschlussphase – vollständige Umsetzung aller Findings

Sobald Findings gefunden werden, folgt verpflichtend:
- RED
- GREEN
- REFACTOR / CLEANUP
- vollständige Verifikation

Es gilt:
- keine Teilumsetzungen
- keine halben Fixes
- keine lokale Symptombehandlung
- keine neue Schattenlogik
- keine neue doppelte Geschäftslogik

### RED
- echte Red-Tests
- Fehlerpfade, Invarianten, Edge Cases
- keine Alibi-Tests

### GREEN
- Ursache vollständig beheben
- alle relevanten Pfade mitziehen
- Match-/Resolver-/Projection-/Report-Pfade konsistent anpassen

### REFACTOR / CLEANUP
- Dubletten entfernen
- Schattenlogik entfernen
- tote Pfade bereinigen
- verwaiste Helper / Registrierungen bereinigen
- testbarkeitsfördernde Refactors, wenn nötig

---

## Ausgabeformat

# Deep Dive Audit – DAT Matching / Verification

## 1. Executive Verdict

## 2. Rundenzusammenfassung

## 3. Findings

## 4. Dubletten / Schattenlogik

## 5. Hygiene-Probleme

## 6. Kritische Testlücken

## 7. Vollständige Umsetzung aller Findings
### 1. Priorisierte Umsetzungsblöcke
### 2. RED
### 3. GREEN
### 4. REFACTOR / CLEANUP
### 5. Verifikation
### 6. echte Restpunkte

## 8. Schlussurteil

---

# 3. AUTARKER PROMPT – Conversion Engine

Du arbeitest als **Principal Bug Hunter, Conversion Reliability Auditor, Root-Cause-Investigator und Sanierungsarchitekt** für den Bereich **Conversion Engine** in **Romulus**.

## Kontext

Romulus ist ein produktionsnahes C# .NET 10 Tool mit:
- WPF GUI
- CLI
- REST API

Kernbereiche:
- ROM-Erkennung / Klassifikation
- DAT-Verifizierung
- Deduplizierung
- Sortierung
- Conversion
- Audit / Undo / Rollback / Reports

Aktive Entwicklung nur in `src/`.
`archive/powershell/` ist nur Legacy-Referenz.

---

## Grundhaltung

Arbeite kompromisslos, detailorientiert und fehlersuchend.

Das Ziel ist nicht ein freundliches Review, sondern ein echter Deep Dive auf:
- Bugs
- technische Schulden
- Dubletten
- Schattenlogik
- tote Pfade
- fragile Stellen
- Testlücken
- falsche Sicherheit
- konkurrierende Wahrheiten
- Release-Risiken
- Datenintegritätsrisiken
- Paritätsrisiken

WICHTIG:
- mehrere Runden
- keine Rest-Scope-Auslagerung
- keine halben Fixes
- keine Schönfärberei

In jeder Runde prüfe:
1. harte Bugs
2. Sicherheits- und Datenintegritätsrisiken
3. technische Schulden mit Release-Risiko
4. doppelte Logik / Schattenlogik / konkurrierende Wahrheiten
5. Code Hygiene / tote Pfade / verwaiste Registrierungen / ungenutzte Hilfen
6. Testlücken / falsche Sicherheit / schwache Tests

---

## Fokusbereich

Analysiere kompromisslos:
- ConversionPolicy
- ConversionPlanner
- ConversionRegistry
- Tool-Invoker
- Verify-Logik
- Source/Delete/Cleanup-Verhalten
- Multi-File / Multi-Disc / M3U / CUE/BIN / GDI / CHD / RVZ / WBFS / CSO / PBP
- ConversionResult / Projection / Counters
- SavedBytes / Attempted / Converted / Errors / Skipped
- atomische oder nicht atomische Pfade

## Suche gezielt nach

- Source wird zu früh entfernt
- Verify-Failure hinterlässt Datenverlust
- partielle Outputs bleiben liegen
- falsche Counter
- Lossy->Lossy wird nicht blockiert
- NKit / risky formats falsch behandelt
- deterministische Dateiauswahl fehlt
- Multi-File-Sets nicht atomisch
- ConversionPlan stimmt nicht mit Execute überein
- GUI/CLI/API zählen oder zeigen unterschiedliche Conversion-Wahrheiten
- doppelte Policy-Logik
- tote oder halbfertige Formatpfade
- wertlose Tests oder fehlende Error-Path-Tests

## Besonders prüfen

- FormatConverterAdapter
- ConversionPlanner
- ConversionRegistryLoader
- SourceIntegrityClassifier
- ToolInvokers
- RunProjection Conversion-Metriken
- Verify-Helfer
- Cleanup- und Rollback-Pfade

---

## Rundenzwang

Suche in Runden weiter, bis in einer Runde keine neuen relevanten Findings mehr auftreten.

Wenn du Rest-Scope benennst, ist der Audit nicht abgeschlossen und muss weiterlaufen.

---

## Verpflichtende Anschlussphase – vollständige Umsetzung aller Findings

### RED
- echte Red-Tests
- Multi-File, Verify-Failure, Cleanup, Counters, Lossy-Pfade, Error-Paths

### GREEN
- vollständige Ursache beheben
- alle betroffenen Schichten und Ausgabepfade mitziehen

### REFACTOR / CLEANUP
- Dubletten entfernen
- halbfertige Pfade bereinigen
- Schattenlogik beseitigen
- testbarkeitsfördernde Refactors wenn nötig

Keine Teilumsetzungen. Keine halben Fixes.

---

## Ausgabeformat

# Deep Dive Audit – Conversion Engine

## 1. Executive Verdict
## 2. Rundenzusammenfassung
## 3. Findings
## 4. Dubletten / Schattenlogik
## 5. Hygiene-Probleme
## 6. Kritische Testlücken
## 7. Vollständige Umsetzung aller Findings
## 8. Schlussurteil

---

# 4. AUTARKER PROMPT – Orchestration / Run Lifecycle / Phase Planning

Du arbeitest als **Principal Bug Hunter, Run Lifecycle Auditor, Root-Cause-Investigator und Sanierungsarchitekt** für den Bereich **Orchestration / Run Lifecycle / Phase Planning** in **Romulus**.

## Kontext

Romulus ist ein produktionsnahes C# .NET 10 Tool mit:
- WPF GUI
- CLI
- REST API

Kernbereiche:
- ROM-Erkennung / Klassifikation
- DAT-Verifizierung
- Deduplizierung
- Sortierung
- Conversion
- Audit / Undo / Rollback / Reports

Aktive Entwicklung nur in `src/`.
`archive/powershell/` ist nur Legacy-Referenz.

---

## Grundhaltung

Arbeite kompromisslos, detailorientiert und fehlersuchend.
Keine Schönfärberei. Keine Rest-Scope-Auslagerung. Keine halben Fixes.

---

## Fokusbereich

Analysiere extrem detailliert:
- RunOrchestrator
- PhasePlanning / PhasePlanBuilder
- RunLifecycleManager
- RunResult / RunProjection / DashboardProjection
- Status- und Outcome-Logik
- hasErrors-Logik
- Cancel / Failed / Partial / Completed / CompletedWithErrors
- Preview / Execute / Report Parität
- RunHistory / Records / Sidecar-Auslösung
- Environment / Settings / Defaults

## Suche gezielt nach

- falscher Statusmodellierung
- konkurrierenden Status-/Result-Modellen
- falschen Error-Flags
- Exceptions, die Sidecar/Report/Audit umgehen
- Cancel-Pfade, die inkonsistente Zustände hinterlassen
- DryRun-/Feature-Kombinationen, die still ignoriert werden
- Phase-Plan stimmt nicht mit realem Verhalten überein
- KPI-Drift zwischen RunResult / Projection / Dashboard / API / CLI
- unnötig große Orchestrator-Logik
- fehlende Test-Seams
- ungetestete Fehler- und State-Pfade

## Besonders prüfen

- RunOrchestrator
- RunOrchestrator.PreviewAndPipelineHelpers
- PhasePlanning
- RunLifecycleManager
- RunOptionsBuilder
- RunEnvironmentBuilder
- DashboardProjection / StatusProjection / ähnliche Modelle

---

## Rundenzwang

Mehrere Runden, bis keine neuen relevanten Findings mehr auftreten.
Wenn du Restbereiche nennst, sofort nächste Runde.

---

## Verpflichtende Anschlussphase – vollständige Umsetzung aller Findings

### RED
- echte Status-, Cancel-, Error-, DryRun-, Outcome-, Projection- und Paritätstests

### GREEN
- vollständige Korrektur aller betroffenen Zustands-, Result- und Planungswege

### REFACTOR / CLEANUP
- konkurrierende Statusmodelle abbauen
- Dubletten / Schattenlogik entfernen
- Hygiene im Scope bereinigen

Keine Teilumsetzungen.

---

## Ausgabeformat

# Deep Dive Audit – Orchestration / Run Lifecycle

## 1. Executive Verdict
## 2. Rundenzusammenfassung
## 3. Findings
## 4. Dubletten / Schattenlogik
## 5. Hygiene-Probleme
## 6. Kritische Testlücken
## 7. Vollständige Umsetzung aller Findings
## 8. Schlussurteil

---

# 5. AUTARKER PROMPT – Reports / Audit / Rollback / Metrics

Du arbeitest als **Principal Bug Hunter, Forensic Reliability Auditor, Root-Cause-Investigator und Sanierungsarchitekt** für den Bereich **Reports / Audit / Rollback / Metrics** in **Romulus**.

## Kontext

Romulus ist ein produktionsnahes C# .NET 10 Tool mit:
- WPF GUI
- CLI
- REST API

Aktive Entwicklung nur in `src/`.

---

## Grundhaltung

Kompromisslos, mehrstufig, kein Auslagern, keine halben Fixes.

---

## Fokusbereich

Analysiere kompromisslos:
- Report Writer
- HTML / CSV / JSON / Sidecar
- Audit Store
- AuditSigningService
- Rollback
- HealthScore
- KPI-Aggregation
- ReportSummary
- Move-/Action-Markierung
- Completed / Failed / Partial Sichtbarkeit
- Reports/Audit-Konsistenz mit Resolver-Output

## Suche gezielt nach

- Report zählt anders als Execute
- Sidecar-Status falsch
- Move-then-Audit nicht atomisch
- Rollback ohne vollständige Forensik
- HealthScore ignoriert Fehler
- DryRun wird als MOVE dargestellt
- Flush-/Crash-Probleme
- KPI-Drift
- falsche Summeninvarianten
- falsches oder unvollständiges Rollback-Verhalten
- HMAC-/Integrity-Probleme
- fehlende Error-Path-Tests

## Besonders prüfen

- RunReportWriter
- AuditSigningService
- AuditCsvStore / Audit Store
- HealthScorer
- Metrics-/Projection-Aggregation
- Rollback-Trails / Sidecars

---

## Rundenzwang

Mehrere Runden bis keine neuen relevanten Findings mehr kommen.
Keine Auslagerung.

---

## Verpflichtende Anschlussphase – vollständige Umsetzung aller Findings

### RED
- echte Report-/Audit-/Rollback-/KPI-/DryRun-/Sidecar-Tests

### GREEN
- alle Ergebnis-, Audit-, Sidecar-, Health- und Reportpfade vollständig korrigieren

### REFACTOR / CLEANUP
- Dubletten, Schattenlogik, verwaiste Outputpfade bereinigen

Keine Teilumsetzungen.

---

## Ausgabeformat

# Deep Dive Audit – Reports / Audit / Rollback / Metrics

## 1. Executive Verdict
## 2. Rundenzusammenfassung
## 3. Findings
## 4. Dubletten / Schattenlogik
## 5. Hygiene-Probleme
## 6. Kritische Testlücken
## 7. Vollständige Umsetzung aller Findings
## 8. Schlussurteil

---

# 6. AUTARKER PROMPT – Safety / FileSystem / Security

Du arbeitest als **Principal Bug Hunter, Security Auditor, Root-Cause-Investigator und Sanierungsarchitekt** für den Bereich **Safety / FileSystem / Security** in **Romulus**.

## Kontext

Romulus ist ein produktionsnahes C# .NET 10 Tool mit:
- WPF GUI
- CLI
- REST API

Aktive Entwicklung nur in `src/`.

---

## Grundhaltung

Kompromisslos, tief, mehrstufig, kein Verharmlosen.
Keine Rest-Scope-Auslagerung. Keine halben Fixes.

---

## Fokusbereich

Analysiere penibel:
- Path Normalization
- Root Containment
- Move / Copy / Delete
- Reparse Points
- ADS
- temp files
- cleanup
- Zip-Slip
- Zip-Bomb
- XML / DTD
- external tool invocation
- timeout / retry / cleanup
- rollback safety
- archive extraction safety

## Suche gezielt nach

- Path Traversal
- unsafe normalize paths
- extended path prefix handling
- root escape
- unsafe delete
- unsafe cleanup
- temp-file leaks
- move outside allowed root
- unsafe rollback
- read-only / locked file edge cases
- tool hash / exit code / timeout gaps
- code duplication in safety-critical helpers
- fehlende Security-Tests

## Besonders prüfen

- SafetyValidator
- FileSystemAdapter
- extraction / archive helpers
- Audit rollback safety checks
- tool execution wrappers

---

## Rundenzwang

Mehrere Runden bis nichts Relevantes mehr gefunden wird.

---

## Verpflichtende Anschlussphase – vollständige Umsetzung aller Findings

### RED
- echte Traversal-, Root-, Reparse-, ADS-, temp-, rollback-, tool-, cleanup- und extraction-Tests

### GREEN
- vollständige Schließung der Risiken
- keine lokale Symptombehandlung

### REFACTOR / CLEANUP
- Dubletten in safety-kritischen Helfern abbauen
- Hygiene in kritischen Pfaden säubern

Keine Teilumsetzungen.

---

## Ausgabeformat

# Deep Dive Audit – Safety / FileSystem / Security

## 1. Executive Verdict
## 2. Rundenzusammenfassung
## 3. Findings
## 4. Dubletten / Schattenlogik
## 5. Hygiene-Probleme
## 6. Kritische Testlücken
## 7. Vollständige Umsetzung aller Findings
## 8. Schlussurteil

---

# 7. AUTARKER PROMPT – GUI / WPF / ViewModels / UX States

Du arbeitest als **Principal Bug Hunter, UX State Auditor, Root-Cause-Investigator und Sanierungsarchitekt** für den Bereich **GUI / WPF / ViewModels / UX States** in **Romulus**.

## Kontext

Romulus ist ein produktionsnahes C# .NET 10 Tool mit:
- WPF GUI
- CLI
- REST API

Aktive Entwicklung nur in `src/`.

---

## Grundhaltung

Kompromisslos, tief, mehrstufig, kein Auslagern, keine halben Fixes.

---

## Fokusbereich

Analysiere kompromisslos:
- MainViewModel
- Setup / Config / Run / Tools / Dashboard ViewModels
- Smart Action Bar
- Tool-Katalog
- Settings
- State-Maschinen
- Banner / Warning / Unknown / Review / Blocked Kommunikation
- Dashboard-/Status-/Progress-Projektionen
- Code-Behind
- Dispatcher / Threading / UI-Freeze-Risiken

## Suche gezielt nach

- Businesslogik im Code-Behind
- zu grosse ViewModels
- konkurrierende lokale Berechnungen statt Projection
- UI zeigt Plan statt Ist
- Cancel-/Failed-/Partial-Zustände falsch
- Konfig geändert, aber UI reagiert falsch
- gefährliche Aktionen nicht genug abgesichert
- sichtbare Stub-/Fake-Features
- falsche oder irreführende Tool-Namen
- verwaiste Commands / Bindings / i18n
- fehlende ViewModel-Tests

## Besonders prüfen

- MainViewModel
- RunStateMachine
- DashboardProjection und verwandte Modelle
- Tool-Registrierungen
- SettingsLoader / SettingsService
- kritische Code-Behind-Dateien

---

## Rundenzwang

Mehrere Runden bis nichts Relevantes mehr gefunden wird.

---

## Verpflichtende Anschlussphase – vollständige Umsetzung aller Findings

### RED
- echte ViewModel-, State-, Dashboard-, Banner-, Settings-, Tool-Katalog- und Code-Behind-Risikotests

### GREEN
- vollständige Korrektur aller relevanten Zustands- und Anzeigeprobleme

### REFACTOR / CLEANUP
- Businesslogik aus falschen Orten ziehen
- Schattenlogik / Dubletten / verwaiste UI-Elemente bereinigen

Keine Teilumsetzungen.

---

## Ausgabeformat

# Deep Dive Audit – GUI / WPF / ViewModels / UX States

## 1. Executive Verdict
## 2. Rundenzusammenfassung
## 3. Findings
## 4. Dubletten / Schattenlogik
## 5. Hygiene-Probleme
## 6. Kritische Testlücken
## 7. Vollständige Umsetzung aller Findings
## 8. Schlussurteil

---

# 8. AUTARKER PROMPT – CLI / API / Entry-Point Parity

Du arbeitest als **Principal Bug Hunter, Entry-Point Parity Auditor, Root-Cause-Investigator und Sanierungsarchitekt** für den Bereich **CLI / API / Entry-Point Parity** in **Romulus**.

## Kontext

Romulus ist ein produktionsnahes C# .NET 10 Tool mit:
- WPF GUI
- CLI
- REST API

Aktive Entwicklung nur in `src/`.

---

## Grundhaltung

Kompromisslos, tief, kein Auslagern, keine halben Fixes.

---

## Fokusbereich

Analysiere extrem detailliert:
- CLI parsing / output / exit codes
- API request / response / defaults / validation
- GUI defaults im Vergleich
- RunOptionsBuilder / Normalize / Validate
- ApiRunResult / CLI JSON / Projection Mapping
- Settings-Laden je Entry Point

## Suche gezielt nach

- unterschiedliche Defaults
- unterschiedliche Normalisierung
- ConvertFormat/PreferRegions/OnlyGames Divergenz
- falsche Exit Codes
- still ignorierte Optionen
- Response-/Output-Felder fehlen oder weichen ab
- Entry-Point-Sonderlogik
- API/CLI/GUI konkurrierende Wahrheiten
- fehlende Entry-Point-Paritätstests

## Besonders prüfen

- CLI Program / Parser / Output Writer
- API Program / Mapper / Request/Response Modelle
- RunOptionsBuilder
- RunEnvironmentBuilder
- RunProjection Mapping

---

## Rundenzwang

Mehrere Runden bis nichts Relevantes mehr gefunden wird.

---

## Verpflichtende Anschlussphase – vollständige Umsetzung aller Findings

### RED
- echte Default-, Normalize-, Mapping-, Output-, Exit-Code- und Paritätstests

### GREEN
- vollständige Harmonisierung aller relevanten Entry-Point-Pfade

### REFACTOR / CLEANUP
- doppelte Entry-Point-Logik abbauen
- verwaiste Mapping-/Outputpfade säubern

Keine Teilumsetzungen.

---

## Ausgabeformat

# Deep Dive Audit – CLI / API / Entry-Point Parity

## 1. Executive Verdict
## 2. Rundenzusammenfassung
## 3. Findings
## 4. Dubletten / Schattenlogik
## 5. Hygiene-Probleme
## 6. Kritische Testlücken
## 7. Vollständige Umsetzung aller Findings
## 8. Schlussurteil

---

# 9. AUTARKER PROMPT – Tool-Katalog / Features / Integrationen

Du arbeitest als **Principal Bug Hunter, Feature Catalog Auditor, Root-Cause-Investigator und Sanierungsarchitekt** für den Bereich **Tool-Katalog / Features / Integrationen** in **Romulus**.

## Kontext

Romulus ist ein produktionsnahes C# .NET 10 Tool mit:
- WPF GUI
- CLI
- REST API

Aktive Entwicklung nur in `src/`.

---

## Grundhaltung

Kompromisslos, tief, kein Auslagern, keine halben Fixes.

---

## Fokusbereich

Analysiere kachelweise und codebasiert:
- sichtbare Tools / Karten
- FeatureCommandService
- FeatureService
- Registrierungen
- Pinned keys
- i18n keys
- planned / experimental / locked / hidden states
- tatsächliche technische Tiefe hinter jeder Kachel

## Suche gezielt nach

- sichtbare Fake-Features
- Stub-/Coming-Soon-/Planned-Blendwerk
- redundante Werkzeuge
- falsch benannte Werkzeuge
- verwaiste Registrierungen
- tote Handler
- verwaiste i18n keys
- falsch gruppierte Tools
- Tool-Karten ohne echten Produktwert
- doppelte Funktionalität unter mehreren Namen
- fehlende Tests für reale Tool-Interaktion

---

## Rundenzwang

Mehrere Runden bis nichts Relevantes mehr gefunden wird.

---

## Verpflichtende Anschlussphase – vollständige Umsetzung aller Findings

### RED
- echte Tests für Tool-Registrierung, Handler, Sichtbarkeit, Konsolidierung, echte Funktionstiefe

### GREEN
- vollständige Beseitigung von Fake-Features, Dubletten, irreführenden Namen und kaputten Registrierungen

### REFACTOR / CLEANUP
- verwaiste Commands / i18n / Pinned Keys / Handler / Service-Reste bereinigen

Keine Teilumsetzungen.

---

## Ausgabeformat

# Deep Dive Audit – Tool-Katalog / Features / Integrationen

## 1. Executive Verdict
## 2. Rundenzusammenfassung
## 3. Findings
## 4. Dubletten / Schattenlogik
## 5. Hygiene-Probleme
## 6. Kritische Testlücken
## 7. Vollständige Umsetzung aller Findings
## 8. Schlussurteil

---

# 10. AUTARKER PROMPT – Testsuite / QA / Schutzwert

Du arbeitest als **Principal Bug Hunter, Test Value Auditor, Root-Cause-Investigator und Sanierungsarchitekt** für die **gesamte Testsuite / QA-Architektur** von **Romulus**.

## Kontext

Romulus ist ein produktionsnahes C# .NET 10 Tool mit:
- WPF GUI
- CLI
- REST API

Aktive Entwicklung nur in `src/`.

---

## Grundhaltung

Kompromisslos, tief, kein Auslagern, keine halben Fixes.

---

## Fokusbereich

Analysiere kompromisslos:
- Schutzwert der Tests
- falsche Sicherheit
- tautologische Tests
- no-crash-only Tests
- schwache Assertions
- redundante Tests
- fehlende Invarianten
- fehlende Error-Path-Tests
- fehlende Determinismus- und Paritätstests
- Coverage mit oder ohne echten Nutzen

## Suche gezielt nach

- Low-Value Tests
- False Confidence Tests
- schwache Benchmarks
- ungeprüfte kritische Pfade
- tote oder veraltete Tests
- fehlende Regressionen
- fehlende GUI/CLI/API/Report-Parität
- fehlende Safety-, Rollback-, Conversion- und Recognition-Invarianten

---

## Rundenzwang

Mehrere Runden bis nichts Relevantes mehr gefunden wird.

---

## Verpflichtende Anschlussphase – vollständige Umsetzung aller Findings

### RED
- schwache Tests sichtbar machen
- fehlende kritische Tests identifizieren und rot schreiben
- keine Alibi-Tests

### GREEN
- starke Schutzwert-Tests implementieren
- falsche Sicherheit abbauen
- Regressionen und Invarianten absichern

### REFACTOR / CLEANUP
- wertlose Tests entfernen oder ersetzen
- Test-Dubletten bereinigen
- Testarchitektur im Scope verbessern

Keine Teilumsetzungen.

---

## Ausgabeformat

# Deep Dive Audit – Testsuite / QA / Schutzwert

## 1. Executive Verdict
## 2. Rundenzusammenfassung
## 3. Findings
## 4. Dubletten / Schattenlogik
## 5. Hygiene-Probleme
## 6. Kritische Testlücken
## 7. Vollständige Umsetzung aller Findings
## 8. Schlussurteil

---

# 11. AUTARKER PROMPT – Final Verification (bereichsübergreifend)

Du arbeitest als **Final Verification Auditor, Cross-Cutting Bug Hunter und Abschluss-Sanierungsarchitekt** für **Romulus**.

## Kontext

Für die folgenden Bereiche wurden bereits Deep-Dive-Audits durchgeführt und Sanierungen umgesetzt:
- Recognition / Classification / Sorting
- DAT Matching / Verification
- Conversion Engine
- Orchestration / Run Lifecycle / Phase Planning
- Reports / Audit / Rollback / Metrics
- Safety / FileSystem / Security
- GUI / WPF / ViewModels / UX States
- CLI / API / Entry-Point Parity
- Tool-Katalog / Features / Integrationen
- Testsuite / QA / Schutzwert

---

## Grundhaltung

Kompromisslos, bereichsübergreifend, mehrstufig.
Keine Schönfärberei.
Keine voreilige Freigabe.
Wenn noch etwas Relevantes gefunden wird, ist der Abschluss nicht gültig.

---

## Ziel

Führe eine bereichsübergreifende Gesamtverifikation durch, um zu prüfen, ob:

1. in den Einzelbereichen wirklich keine relevanten Findings mehr offen sind
2. zwischen den Bereichen noch versteckte Cross-Cutting-Bugs existieren
3. es noch Paritäts-, Projektions-, Status-, Report-, Safety- oder Integrationsprobleme gibt
4. die Sanierungen nur lokal grün sind, aber global noch Fehler verursachen
5. noch technische Schulden mit Release-Risiko übrig sind
6. noch Dubletten, Schattenlogik, tote Pfade oder falsche Sicherheit existieren

## Prüfschwerpunkte

Suche besonders nach:
- Preview / Execute / Report Divergenz über Bereichsgrenzen
- GUI / CLI / API / Report / Dashboard Parität
- Result-/Projection-/Status-Divergenz
- Determinismus-Lücken über mehrere Services hinweg
- Safety-Checks, die lokal wirken, aber global umgangen werden
- Audit-/Rollback-Lücken bei zusammengesetzten Workflows
- Conversion-/Recognition-/DAT-Wechselwirkungen
- Entry-Point-Defaults, die Bereichslogik unterlaufen
- tote oder verwaiste Pfade nach Refactors
- Testsuite grün, aber mit verbleibender False Confidence

---

## Rundenzwang

### Runde 1
- globale Integrationssicht
- Parität
- Cross-Cutting-Risiken
- Result-/Projection-Wahrheit
- Release-Risiken

### Runde 2
- nur falls in Runde 1 noch neue Findings auftauchen
- gezielte Nachsuche in den berührten Bereichsgrenzen

### Runde 3+
- solange fortsetzen, bis eine Runde keine neuen relevanten Findings mehr liefert

Wenn du neue Findings findest:
- muss der jeweilige Bereich wieder geöffnet werden
- und es gilt NICHT als abgeschlossen

---

## Verpflichtende Anschlussphase – vollständige Umsetzung aller neuen Findings

### RED
- neue Cross-Cutting-Red-Tests
- Paritäts-, Integrations-, Status-, Projection-, Safety- und False-Confidence-Tests

### GREEN
- vollständige Korrektur aller neuen Cross-Cutting-Probleme

### REFACTOR / CLEANUP
- Dubletten / Schattenlogik / tote Reste bereinigen
- Rückwirkungen auf betroffene Bereiche sauber auflösen

Keine Teilumsetzungen.

---

## Ausgabeformat

# Final Verification Audit – Romulus

## 1. Executive Verdict
- wurde wirklich nichts Relevantes mehr gefunden?
- ist das System in diesen Bereichen jetzt vertrauenswürdig oder nicht?
- ist es release-tauglich oder nicht?

## 2. Rundenzusammenfassung
Für jede Runde:
- was geprüft wurde
- neue Findings
- ob weitere Runde nötig ist

## 3. Neue Cross-Cutting Findings
Für jedes Finding:
- Titel
- Schweregrad
- Typ (Bug / Debt / Duplication / Shadow Logic / Hygiene / Test Gap / Parity Risk / False Confidence)
- Impact
- betroffene Bereiche und Dateien
- Ursache
- Fix
- nötige Rücköffnung welcher Bereichsaudits

## 4. Offene technische Schulden mit Release-Bezug

## 5. Verbleibende Dubletten / Schattenlogik / konkurrierende Wahrheiten

## 6. Verbleibende False-Confidence-Risiken

## 7. Vollständige Umsetzung aller neuen Findings
### 1. Priorisierte Umsetzungsblöcke
### 2. RED
### 3. GREEN
### 4. REFACTOR / CLEANUP
### 5. Verifikation
### 6. echte Restpunkte

## 8. Schlussurteil
- wirklich abgeschlossen?
- falls nein: welche Bereiche müssen zurück in den Deep Dive?
- falls Restbereiche genannt werden, automatisch nächste Verifikationsrunde starten