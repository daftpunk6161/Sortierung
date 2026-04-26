# Romulus – Kompromisslose Deep-Dive Bughunting Prompt-Sammlung V2

Diese Datei enthält vollständig integrierte, kompromisslose Deep-Dive-Prompts für Romulus.

Ziel:
- extrem detailliertes Bughunting
- technische Schulden finden
- Dubletten und Schattenlogik finden
- Code-Hygiene-Probleme aufdecken
- Testlücken und falsche Sicherheit identifizieren
- jeden Bereich so lange in Runden untersuchen, bis in einer Runde keine neuen relevanten Findings mehr auftauchen
- offene Restbereiche niemals auslagern, solange sie logisch noch im Scope des aktuellen Bereichs liegen
- nach den Findings die vollständige Umsetzung erzwingen
- Red -> Green -> Refactor
- keine Teilumsetzungen
- keine halben Fixes
- keine kosmetischen Ausreden
- danach eine bereichsübergreifende Abschlussverifikation durchführen

---

## Globale Grundregeln für alle Deep Dives

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

Beurteile nicht nach Stil, sondern nach technischem Risiko und Schutzwert.
Keine Schönfärberei.

---

## Globale Abschlussbedingung für jeden Bereich

Ein Bereichsaudit darf erst enden, wenn:
- keine offenen Restbereiche mehr benannt werden
- keine „nicht vollständig abgedeckt“-Liste mehr übrig ist
- in der letzten Runde keine neuen relevanten Findings mehr gefunden wurden
- keine relevanten Dubletten / Schattenlogik / Hygieneprobleme / Testlücken mehr neu auftauchen
- keine relevanten Cross-Pfad-Risiken innerhalb des Bereichs offen bleiben

Falls du doch noch offene Restbereiche benennst, musst du automatisch in die nächste Runde gehen.

---

## Verpflichtende Anschlussphase – kompromisslose vollständige Umsetzung aller Findings

WICHTIG:
Die Analyse ist nicht das Endergebnis.

Sobald Findings gefunden werden, folgt verpflichtend eine vollständige technische Umsetzung.

Es gilt ausdrücklich:
- keine Teilumsetzungen
- keine halben Fixes
- keine kosmetischen Scheinlösungen
- keine Umgehung der eigentlichen Ursache
- keine TODO-/Placeholder-/Follow-up-Ausreden
- keine lokale Symptombehandlung, wenn das Grundproblem bestehen bleibt
- keine “good enough”-Antwort
- keine stillen Restfehler
- keine absichtliche Verschiebung wesentlicher Teile
- keine neue Schattenlogik
- keine neue doppelte Geschäftslogik
- keine neue konkurrierende Wahrheit
- keine reine Schönheitsbereinigung ohne funktionale Sanierung

Wenn ein Problem gefunden wird, muss es:
1. vollständig verstanden
2. über Red-Tests reproduzierbar gemacht
3. vollständig behoben
4. sauber refactored
5. mit Regressionstests abgesichert
6. auf Hygiene, Dubletten und Nebeneffekte geprüft
7. gegen den gesamten Bereich rückverifiziert
werden.

---

## Verpflichtendes Vorgehen pro Finding-Block

Für jeden priorisierten Problemblock gilt strikt:

### Phase 1 – RED
Zuerst müssen die Probleme sichtbar gemacht werden.

Pflicht:
- echte Red-Tests schreiben oder bestehende Tests so verschärfen, dass der Fehler wirklich sichtbar wird
- Fehlerpfade, Invarianten, Edge Cases und Seiteneffekte explizit absichern
- keine Alibi-Tests
- keine tautologischen Assertions
- keine no-crash-only Tests
- keine weich formulierten Erwartungen
- keine Tests, die nur zufällig grün werden

Wenn ein Bug, eine technische Schuld oder eine Dublette nicht testbar sichtbar gemacht werden kann, muss das Problem der Testbarkeit explizit benannt und minimal sauber gelöst werden.

### Phase 2 – GREEN
Danach:
- die minimal nötige, aber vollständige und saubere produktive Änderung implementieren
- die Red-Tests auf grün bringen
- die eigentliche Ursache beheben, nicht nur Symptome
- alle relevanten Codepfade konsistent anpassen
- betroffene Entry Points, Projection-Pfade, Result-Modelle, Statusmodelle und Fehlerpfade vollständig mitziehen

WICHTIG:
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
- Naming verbessern, wenn es zur fachlichen Klarheit nötig ist
- kleine testbarkeitsfördernde Refactors durchführen, wenn nötig
- aber keine unnötigen Massenumbauten ausserhalb des betroffenen Problems

---

## Was zusätzlich immer mitgeprüft werden muss

Bei jedem umgesetzten Finding muss zusätzlich geprüft werden:

### 1. Parität
- Preview / Execute / Report konsistent?
- GUI / CLI / API / Reports konsistent?
- Projection / Result / Dashboard / Output konsistent?

### 2. Determinismus
- gleiche Inputs -> gleiche Outputs?
- stabile Tie-Breaker?
- keine reihenfolgeabhängigen Zufallseffekte?
- keine versteckten OrderBy-/First()-Probleme?

### 3. Datenintegrität
- kein Datenverlust?
- Source-Dateien nie vor erfolgreicher Verifikation entfernt?
- partielle Outputs sauber behandelt?
- Rollback / Audit / Sidecars vollständig und korrekt?

### 4. Sicherheit
- Path Traversal nicht aufgeweicht?
- Root Containment weiter korrekt?
- Reparse / Zip-Slip / temp / cleanup weiter sauber?
- Tool-Invocation sicher?

### 5. Hygiene
- keine neue Dublette entstanden?
- keine verwaisten Reste hinterlassen?
- keine halbfertige Übergangslösung eingebaut?
- keine neue tote Logik entstanden?

### 6. Tests
- Regressionstest vorhanden?
- Fehlerpfad-Test vorhanden?
- Invariantentest vorhanden, wenn fachlich relevant?
- Schutzwert der Tests hoch genug?

---

## Vollständigkeitsregel

Ein Finding gilt nur dann als erledigt, wenn alle folgenden Punkte erfüllt sind:
- Ursache klar identifiziert
- Red-Test vorhanden
- Green-Fix vollständig implementiert
- Refactor / Cleanup im Scope durchgeführt
- keine konkurrierenden Restpfade mehr vorhanden
- keine Dublette des Problems mehr vorhanden
- keine relevante Paritätsabweichung mehr vorhanden
- keine neue Schattenlogik entstanden
- Regressionstests vorhanden
- Bereich intern erneut überprüft
- keine offensichtlichen Restfehler mehr offen

Wenn einer dieser Punkte fehlt, ist das Finding nicht abgeschlossen.

---

## Mehr-Runden-Umsetzung

Wenn nach der Umsetzung noch eines der folgenden Dinge auftritt:
- neue Bugs
- Restfehler
- unvollständige Pfade
- Paritätsabweichungen
- Hygieneprobleme
- neue Dubletten
- neue Testlücken
- lokale Fixes ohne zentrale Konsolidierung

dann muss sofort eine weitere Umsetzungsrunde gestartet werden.

Es gilt:

### Runde A
- Findings aus der Analyse umsetzen

### Runde B
- Restfehler der Umsetzung finden
- unvollständige Stellen nachziehen
- Hygiene und Dubletten bereinigen

### Runde C+
- solange weitere Umsetzungsrunden machen, bis in einer Runde keine neuen relevanten Bugs, Restfehler, Dubletten, Hygieneprobleme oder Testlücken mehr gefunden werden

---

## Keine künstliche Verkleinerung des Scopes

Der Scope darf nicht künstlich verkleinert werden, nur um schneller “fertig” zu wirken.

Wenn ein Problem technisch mehrere Schichten betrifft, dann müssen diese auch vollständig mitgezogen werden, zum Beispiel:
- Core
- Infrastructure
- CLI
- API
- GUI
- Reports
- Audit
- Tests
- Mappings
- Projections
- Settings / Defaults / Validation

Wenn ein Fix nur in einer Schicht passiert, obwohl das Problem fachlich mehrere Schichten betrifft, ist die Umsetzung unvollständig und nicht akzeptabel.

---

## Ausgabeformat für die Umsetzung

Nach den Findings muss die Ausgabe zwingend um einen vollständigen Umsetzungsblock erweitert werden.

## X. Vollständige Umsetzung aller Findings

### 1. Priorisierte Umsetzungsblöcke
Für jeden Block:
- Titel
- Priorität
- betroffene Dateien
- welche Findings damit vollständig erledigt werden

### 2. RED
Für jeden Block:
- welche Red-Tests neu geschrieben oder verschärft werden
- welche Fehler, Invarianten und Edge Cases damit sichtbar gemacht werden

### 3. GREEN
Für jeden Block:
- welche produktiven Änderungen vollständig umgesetzt werden
- warum diese die Ursache vollständig beheben
- welche Schichten / Pfade mitgezogen werden

### 4. REFACTOR / CLEANUP
Für jeden Block:
- welche Dubletten entfernt wurden
- welche Schattenlogik entfernt wurde
- welche Hygieneprobleme bereinigt wurden
- welche verwaisten Reste entfernt wurden

### 5. Verifikation
Für jeden Block:
- welche Tests laufen
- welche Parität geprüft wurde
- welche Invarianten jetzt abgesichert sind
- warum das Finding jetzt wirklich geschlossen ist

### 6. Restpunkte
Nur echte, unvermeidbare Restpunkte aufführen.
Keine künstlichen „später machen“-Ausreden.
Wenn etwas offen bleibt, muss klar begründet werden:
- warum es nicht im aktuellen Block gelöst werden konnte
- warum es kein halber Fix ist
- welche nächste zwingende Umsetzungsrunde folgt

---

## Abschlussregel

Das Ziel ist nicht:
- ein schöner Bericht
- eine teilweise Verbesserung
- ein grünerer Zustand
- ein kosmetisch besserer Eindruck

Das Ziel ist:
- vollständige, kompromisslose, technisch saubere Sanierung des gefundenen Problemraums

Wenn die Findings zeigen, dass ein Bereich konzeptionell kaputt ist, dann muss die Umsetzung auch konzeptionell sauber erfolgen.
Nicht flicken, wenn Neuaufsetzung nötig ist.

Wenn etwas nur mit einer echten Neuaufsetzung sauber lösbar ist, dann muss das klar gesagt und entsprechend vollständig geplant und umgesetzt werden.

---

# 1. Deep Dive – Recognition / Classification / Sorting

Du arbeitest als Principal Bug Hunter und Domain Auditor für den Bereich Recognition / Classification / Sorting in Romulus.

## Auftrag
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

## Rundenlogik
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

## Verbotene Ausweichbewegungen
- keine offenen Restbereiche als „späterer Audit“
- keine „dedizierter Audit wäre sinnvoll“-Ausrede
- keine oberflächliche Arcade-/BIOS-/Multi-Disc-/DAT-Abdeckung
- keine künstliche Auslagerung von Randfällen, solange sie logisch in diesen Bereich gehören

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
- Schweregrad (P0/P1/P2/P3)
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
- Priorisierte Umsetzungsblöcke
- RED
- GREEN
- REFACTOR / CLEANUP
- Verifikation
- echte Restpunkte

## 8. Schlussurteil
- ist der Bereich jetzt wirklich ausgeschöpft?
- falls nein: warum nicht?
- falls noch Restbereiche auftauchen, sofort nächste Runde starten

---

# 2. Deep Dive – DAT Matching / Verification

Du arbeitest als Principal Bug Hunter und Verification Auditor für den Bereich DAT Matching / Verification in Romulus.

## Auftrag
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
- wertlosen oder zu schwachen DAT-Tests

## Besonders prüfen
- DatSourceService
- Dat-Matcher / Resolver / Loader
- ArchiveHashService
- CHD-Hash-Pfade
- LookupAny-/Fallback-Pfade
- alle Result-/Projection-Felder mit DAT-Bezug
- Reports/Audit-Konsistenz mit Resolver-Output, soweit logisch in diesem Bereich

## Rundenlogik
Suche in mehreren Runden weiter, bis eine Runde keine neuen Findings bringt.

## Verbotene Ausweichbewegungen
- keine DAT-Source-spezifische Match-Level-Wahl als separater Audit auslagern
- keine Resolver-/Output-Konsistenz wegdelegieren
- keine offenen Match-Level-Randfälle verschieben, solange sie im Scope dieses Bereichs liegen

## Ausgabeformat
# Deep Dive Audit – DAT Matching / Verification

## 1. Executive Verdict

## 2. Rundenzusammenfassung
Für jede Runde:
- was geprüft wurde
- neue Findings
- ob weitere Runde nötig ist

## 3. Findings
Für jedes Finding:
- Titel
- Schweregrad (P0/P1/P2/P3)
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
- Priorisierte Umsetzungsblöcke
- RED
- GREEN
- REFACTOR / CLEANUP
- Verifikation
- echte Restpunkte

## 8. Schlussurteil
- ist der Bereich jetzt wirklich ausgeschöpft?
- falls nein: warum nicht?
- falls noch Restbereiche auftauchen, sofort nächste Runde starten

---

# 3. Deep Dive – Conversion Engine

Du arbeitest als Principal Bug Hunter und Conversion Reliability Auditor für den Bereich Conversion Engine in Romulus.

## Auftrag
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

## Verbotene Ausweichbewegungen
- keine Multi-File-/Multi-Disc-Sonderfälle als später auslagern
- keine Verifikationsschwächen als „kann später härter werden“ stehen lassen
- keine Counter-/Projection-Probleme nur lokal beheben

## Ausgabeformat
# Deep Dive Audit – Conversion Engine

## 1. Executive Verdict

## 2. Rundenzusammenfassung
Für jede Runde:
- was geprüft wurde
- neue Findings
- ob weitere Runde nötig ist

## 3. Findings
Für jedes Finding:
- Titel
- Schweregrad (P0/P1/P2/P3)
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
- Priorisierte Umsetzungsblöcke
- RED
- GREEN
- REFACTOR / CLEANUP
- Verifikation
- echte Restpunkte

## 8. Schlussurteil
- ist der Bereich jetzt wirklich ausgeschöpft?
- falls nein: warum nicht?
- falls noch Restbereiche auftauchen, sofort nächste Runde starten

---

# 4. Deep Dive – Orchestration / Run Lifecycle / Phase Planning

Du arbeitest als Principal Bug Hunter und Run Lifecycle Auditor für Orchestration / Run Lifecycle / Phase Planning in Romulus.

## Auftrag
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

## Verbotene Ausweichbewegungen
- keine State-/Outcome-Probleme als GUI- oder Report-Einzelthema auslagern
- keine hasErrors-/Projection-Themen später verschieben
- keine DryRun-/Planungs-Probleme nur dokumentieren statt vollständig analysieren

## Ausgabeformat
# Deep Dive Audit – Orchestration / Run Lifecycle

## 1. Executive Verdict

## 2. Rundenzusammenfassung
Für jede Runde:
- was geprüft wurde
- neue Findings
- ob weitere Runde nötig ist

## 3. Findings
Für jedes Finding:
- Titel
- Schweregrad (P0/P1/P2/P3)
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
- Priorisierte Umsetzungsblöcke
- RED
- GREEN
- REFACTOR / CLEANUP
- Verifikation
- echte Restpunkte

## 8. Schlussurteil
- ist der Bereich jetzt wirklich ausgeschöpft?
- falls nein: warum nicht?
- falls noch Restbereiche auftauchen, sofort nächste Runde starten

---

# 5. Deep Dive – Reports / Audit / Rollback / Metrics

Du arbeitest als Principal Bug Hunter und Forensic Reliability Auditor für Reports / Audit / Rollback / Metrics in Romulus.

## Auftrag
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
- Reports/Audit-Konsistenz mit Resolver-Output

## Besonders prüfen
- RunReportWriter
- AuditSigningService
- AuditCsvStore / Audit Store
- HealthScorer
- Metrics-/Projection-Aggregation
- Rollback-Trails / Sidecars
- Resolver-/Output-Konsistenz, soweit logisch in diesem Bereich

## Verbotene Ausweichbewegungen
- keine Report-/Audit-Konsistenz als späterer Spezialaudit
- keine KPI-Abweichungen nur dokumentieren
- keine DryRun-/MOVE-Diskrepanzen nur oberflächlich behandeln

## Ausgabeformat
# Deep Dive Audit – Reports / Audit / Rollback / Metrics

## 1. Executive Verdict

## 2. Rundenzusammenfassung
Für jede Runde:
- was geprüft wurde
- neue Findings
- ob weitere Runde nötig ist

## 3. Findings
Für jedes Finding:
- Titel
- Schweregrad (P0/P1/P2/P3)
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
- Priorisierte Umsetzungsblöcke
- RED
- GREEN
- REFACTOR / CLEANUP
- Verifikation
- echte Restpunkte

## 8. Schlussurteil
- ist der Bereich jetzt wirklich ausgeschöpft?
- falls nein: warum nicht?
- falls noch Restbereiche auftauchen, sofort nächste Runde starten

---

# 6. Deep Dive – Safety / FileSystem / Security

Du arbeitest als Principal Bug Hunter und Security Auditor für Safety / FileSystem / Security in Romulus.

## Auftrag
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

## Verbotene Ausweichbewegungen
- keine Safety-Randfälle später schieben
- keine „nur theoretischen“ Traversal-/Cleanup-/Rollback-Themen kleinreden
- keine Security- oder Datenintegritätslücken nur als Debt labeln, wenn sie echte Risiken sind

## Ausgabeformat
# Deep Dive Audit – Safety / FileSystem / Security

## 1. Executive Verdict

## 2. Rundenzusammenfassung
Für jede Runde:
- was geprüft wurde
- neue Findings
- ob weitere Runde nötig ist

## 3. Findings
Für jedes Finding:
- Titel
- Schweregrad (P0/P1/P2/P3)
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
- Priorisierte Umsetzungsblöcke
- RED
- GREEN
- REFACTOR / CLEANUP
- Verifikation
- echte Restpunkte

## 8. Schlussurteil
- ist der Bereich jetzt wirklich ausgeschöpft?
- falls nein: warum nicht?
- falls noch Restbereiche auftauchen, sofort nächste Runde starten

---

# 7. Deep Dive – GUI / WPF / ViewModels / UX States

Du arbeitest als Principal Bug Hunter und UX State Auditor für GUI / WPF / ViewModels / UX States in Romulus.

## Auftrag
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

## Verbotene Ausweichbewegungen
- keine GUI-State-Probleme als reines UX-Thema verharmlosen
- keine lokalen Dashboard-/Status-Abweichungen später verschieben
- keine Fake-Features oder Tool-Katalog-Probleme ohne Tiefenprüfung stehen lassen

## Ausgabeformat
# Deep Dive Audit – GUI / WPF / ViewModels / UX States

## 1. Executive Verdict

## 2. Rundenzusammenfassung
Für jede Runde:
- was geprüft wurde
- neue Findings
- ob weitere Runde nötig ist

## 3. Findings
Für jedes Finding:
- Titel
- Schweregrad (P0/P1/P2/P3)
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
- Priorisierte Umsetzungsblöcke
- RED
- GREEN
- REFACTOR / CLEANUP
- Verifikation
- echte Restpunkte

## 8. Schlussurteil
- ist der Bereich jetzt wirklich ausgeschöpft?
- falls nein: warum nicht?
- falls noch Restbereiche auftauchen, sofort nächste Runde starten

---

# 8. Deep Dive – CLI / API / Entry-Point Parity

Du arbeitest als Principal Bug Hunter und Entry-Point Parity Auditor für CLI / API / Entry-Point Parity in Romulus.

## Auftrag
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

## Verbotene Ausweichbewegungen
- keine Default-/Normalize-/Mapping-Probleme auf andere Bereiche abwälzen
- keine Entry-Point-Abweichungen nur dokumentieren
- keine CLI/API/GUI-Divergenz als tolerierbar darstellen

## Ausgabeformat
# Deep Dive Audit – CLI / API / Entry-Point Parity

## 1. Executive Verdict

## 2. Rundenzusammenfassung
Für jede Runde:
- was geprüft wurde
- neue Findings
- ob weitere Runde nötig ist

## 3. Findings
Für jedes Finding:
- Titel
- Schweregrad (P0/P1/P2/P3)
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
- Priorisierte Umsetzungsblöcke
- RED
- GREEN
- REFACTOR / CLEANUP
- Verifikation
- echte Restpunkte

## 8. Schlussurteil
- ist der Bereich jetzt wirklich ausgeschöpft?
- falls nein: warum nicht?
- falls noch Restbereiche auftauchen, sofort nächste Runde starten

---

# 9. Deep Dive – Tool-Katalog / Features / Integrationen

Du arbeitest als Principal Bug Hunter und Feature Catalog Auditor für Tool-Katalog / Features / Integrationen in Romulus.

## Auftrag
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

## Verbotene Ausweichbewegungen
- keine sichtbaren Fake-Features auf später verschieben
- keine redundanten Werkzeuge nur dokumentieren
- keine falschen Namen / irreführenden Karten tolerieren, wenn sie Release-Risiko oder User-Irritation erzeugen

## Ausgabeformat
# Deep Dive Audit – Tool-Katalog / Features / Integrationen

## 1. Executive Verdict

## 2. Rundenzusammenfassung
Für jede Runde:
- was geprüft wurde
- neue Findings
- ob weitere Runde nötig ist

## 3. Findings
Für jedes Finding:
- Titel
- Schweregrad (P0/P1/P2/P3)
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
- Priorisierte Umsetzungsblöcke
- RED
- GREEN
- REFACTOR / CLEANUP
- Verifikation
- echte Restpunkte

## 8. Schlussurteil
- ist der Bereich jetzt wirklich ausgeschöpft?
- falls nein: warum nicht?
- falls noch Restbereiche auftauchen, sofort nächste Runde starten

---

# 10. Deep Dive – Testsuite / QA / Schutzwert

Du arbeitest als Principal Bug Hunter und Test Value Auditor für die gesamte Testsuite / QA-Architektur von Romulus.

## Auftrag
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

## Verbotene Ausweichbewegungen
- keine Coverage-Schönfärberei
- keine grüne, aber wertlose Testsuite als akzeptabel behandeln
- keine Low-Value-Tests aus Schonung stehen lassen

## Ausgabeformat
# Deep Dive Audit – Testsuite / QA / Schutzwert

## 1. Executive Verdict

## 2. Rundenzusammenfassung
Für jede Runde:
- was geprüft wurde
- neue Findings
- ob weitere Runde nötig ist

## 3. Findings
Für jedes Finding:
- Titel
- Schweregrad (P0/P1/P2/P3)
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
- Priorisierte Umsetzungsblöcke
- RED
- GREEN
- REFACTOR / CLEANUP
- Verifikation
- echte Restpunkte

## 8. Schlussurteil
- ist der Bereich jetzt wirklich ausgeschöpft?
- falls nein: warum nicht?
- falls noch Restbereiche auftauchen, sofort nächste Runde starten

---

# 11. Final Verification – Bereichsübergreifend prüfen, dass nichts Relevantes mehr gefunden wird

Du arbeitest als Final Verification Auditor für Romulus.

## Kontext
Für die folgenden Bereiche wurden bereits kompromisslose Deep-Dive-Audits durchgeführt und Sanierungen umgesetzt:
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

## Ziel
Führe jetzt eine bereichsübergreifende Gesamtverifikation durch, um zu prüfen, ob:
1. in den Einzelbereichen wirklich keine relevanten Findings mehr offen sind
2. zwischen den Bereichen noch versteckte Cross-Cutting-Bugs existieren
3. es noch Paritäts-, Projektions-, Status-, Report-, Safety- oder Integrationsprobleme gibt
4. die Sanierungen nur lokal grün sind, aber global noch Fehler verursachen
5. noch technische Schulden mit Release-Risiko übrig sind
6. noch Dubletten, Schattenlogik, tote Pfade oder falsche Sicherheit existieren

## WICHTIG
Das ist kein normaler Abschlussbericht.

Das Ziel ist:
- alles nochmals gegen den Strich bürsten
- Cross-Bereich-Risiken finden
- letzte konkurrierende Wahrheiten finden
- prüfen, ob wirklich nichts mehr Relevantes gefunden wurde

Wenn du doch noch Findings findest:
- muss der jeweilige Bereich wieder geöffnet werden
- und es gilt NICHT als abgeschlossen

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

## Verifikationslogik
Führe die Gesamtverifikation ebenfalls in Runden durch.

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

## 7. Verpflichtende vollständige Umsetzung aller neuen Findings
- Priorisierte Umsetzungsblöcke
- RED
- GREEN
- REFACTOR / CLEANUP
- Verifikation
- echte Restpunkte

## 8. Schlussurteil
- wirklich abgeschlossen?
- falls nein: welche Bereiche müssen zurück in den Deep Dive?
- falls Restbereiche genannt werden, automatisch nächste Verifikationsrunde starten

---

# Empfohlene Reihenfolge der Ausführung

1. Deep Dive – Recognition / Classification / Sorting
2. Deep Dive – DAT Matching / Verification
3. Deep Dive – Conversion Engine
4. Deep Dive – Orchestration / Run Lifecycle / Phase Planning
5. Deep Dive – Reports / Audit / Rollback / Metrics
6. Deep Dive – Safety / FileSystem / Security
7. Deep Dive – GUI / WPF / ViewModels / UX States
8. Deep Dive – CLI / API / Entry-Point Parity
9. Deep Dive – Tool-Katalog / Features / Integrationen
10. Deep Dive – Testsuite / QA / Schutzwert
11. Final Verification – Bereichsübergreifend prüfen, dass nichts Relevantes mehr gefunden wird