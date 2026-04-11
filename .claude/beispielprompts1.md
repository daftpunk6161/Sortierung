# Romulus – Prompt-Sammlung fuer GitHub Copilot

Diese Sammlung ist auf **Romulus** zugeschnitten und fuer die Arbeit mit **GitHub Copilot in VS Code** gedacht.

Ziel:

* bessere Analysen
* haertere Bughunts
* sauberere Refactors
* strengere Release-Pruefungen
* sinnvollere Feature-Planung
* bessere Testabdeckung

---

## Nutzungshinweise

### Empfohlene Grundregel

Bei groesseren Aufgaben immer zuerst:

1. **Analyse / Plan**
2. dann **Umsetzung**
3. dann **Review / Verifikation**

### Gute Praxis

* relevante Dateien in VS Code offen haben
* betroffene Datei oder Codeblock markieren
* bei komplexen Themen zunaechst nur einen Plan anfordern
* danach denselben Chat fuer die Umsetzung weiterverwenden
* Copilot moeglichst mit klaren Dateien, Zielen und Constraints fuehren

### Wichtige Prioritaeten fuer Romulus

1. Korrektheit
2. Determinismus
3. Sicherheit
4. Testbarkeit
5. Wartbarkeit

---

# 1. Architektur- und Bereichsanalyse

## 1.1 Allgemeine Analyse eines Bereichs

```text
Analysiere diesen Bereich tief und konkret.

Ziel:
- Architektur verstehen
- Hauptverantwortungen identifizieren
- Risiken, Schwachstellen und technische Schulden finden
- sagen, was stark ist und was problematisch ist

Achte besonders auf:
- doppelte Logik
- Schattenlogik
- falsche Verantwortlichkeiten
- fragile Fehlerpfade
- Determinismus
- Testbarkeit
- Preview / Execute / Report / GUI / CLI / API Paritaet

Ausgabeformat:
1. Kurzfazit
2. Staerken
3. Probleme / Risiken
4. betroffene Dateien
5. konkrete Verbesserungen
6. Testbedarf
```

## 1.2 Allgemeine Tool-Analyse als Produkt

```text
Analysiere Romulus als Gesamtprodukt.

Ziel:
- verstehen, was das Tool heute ist
- Staerken und Schwaechen identifizieren
- Produktluecken erkennen
- sinnvolle Moeglichkeiten und Erweiterungen aufzeigen

Beruecksichtige:
- GUI, CLI, API
- Deduplizierung
- Recognition / Classification
- DAT / Verification
- Sorting
- Conversion
- Reports / Audit / Rollback
- Workflow / Automation
- Safety / Quality

Ausgabeformat:
1. Executive Summary
2. Was Romulus aktuell ist
3. Funktions- und Bereichsanalyse
4. Staerken des Tools
5. Schwaechen und Luecken
6. Moeglichkeiten und Entwicklungspotenziale
7. konkrete Feature-Vorschlaege
8. Top 10 Ausbauideen
9. was bewusst nicht verfolgt werden sollte
10. empfohlene naechste Schritte
```

---

# 2. Deep Bughunts

## 2.1 Master Deep Bughunt – gesamtes Tool

```text
Fuehre einen intensiven Deep Bughunt ueber das gesamte Tool durch.

WICHTIG:
Das ist kein oberflaechliches Review.
Suche aktiv nach echten Bugs, Edge Cases, stillen Fehlern, Statusfehlern, KPI-Fehlern, Sicherheitsproblemen, Race Conditions, Cancel-/Retry-/Rollback-Problemen und Paritaetsfehlern.

Prioritaet:
1. Datenverlust
2. Security
3. falsche Winner-Selection / Grouping / Scoring
4. Preview / Execute / Report Divergenz
5. GUI / CLI / API Paritaetsfehler
6. Conversion-Fehler
7. Rollback / Audit / Report Fehler
8. Status- und KPI-Fehler
9. Performance / Stabilitaet / Cancellation
10. UI-Fehlverhalten mit Fehlbedienungsrisiko

Fuer jeden Fund:
- Titel
- Schweregrad
- Reproduktion
- erwartetes Verhalten
- tatsaechliches Verhalten
- Ursache
- Fix
- Regressionstests
```

## 2.2 Deep Bughunt – Recognition / Classification / Sorting

```text
Fuehre einen intensiven Deep Bughunt fuer Recognition, Classification, Detection, SortDecision und Sorting durch.

Suche nach:
- falscher Konsolenerkennung
- falscher Kategorie
- falscher Region
- falscher Confidence
- BIOS/Game-Verwechslung
- Arcade Parent/Clone/BIOS Problemen
- Multi-Disc / Multi-File Zuordnungsfehlern
- CrossRoot-Duplikaten
- nicht-deterministischen Tie-Breakern
- falscher Winner-Selection
- falscher Folder-/Extension-/Header-Prioritaet
- Non-ROM faelschlich als ROM
- Preview/Execute-Divergenz bei Sortentscheidungen

Pruefe besonders:
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

Ausgabeformat:
1. Executive Verdict
2. kritische Fehlermuster
3. Findings
4. Determinismus-Risiken
5. False-Positive / False-Negative Hauptursachen
6. Top 10 Fixes
```

## 2.3 Deep Bughunt – Conversion Engine

```text
Fuehre einen intensiven Deep Bughunt fuer die Conversion Engine durch.

Suche nach:
- Source wird zu frueh geloescht
- Verify-Failure erzeugt falsche Counter
- Conversion + Error gleichzeitig gezaehlt
- Lossy->Lossy nicht blockiert
- falscher ConversionPolicy
- falscher Format-Prioritaet
- falscher Toolwahl
- CUE/BIN falsch behandelt
- Multi-Disc / M3U nicht atomisch
- CHD / RVZ / NKit / CSO / ISO / WBFS / PBP Sonderfaellen
- Exit-Codes / Timeouts / Tool-Failures falsch behandelt
- partielle Outputs bleiben liegen
- Verify ist zu schwach
- deterministische Dateiauswahl fehlt
- ConversionPlan stimmt nicht mit Ausfuehrung ueberein
- SavedBytes / Converted / Errors / Skipped falsch gezaehlt

Pruefe besonders:
- FormatConverterAdapter
- ConversionPlanner
- SourceIntegrityClassifier
- Tool-Invoker
- ConversionPolicy / ConversionRegistry
- RunProjection Conversion-Metriken
```

## 2.4 Deep Bughunt – GUI / UX / WPF

```text
Fuehre einen intensiven Deep Bughunt fuer GUI / UX / WPF durch.

Suche nach:
- Businesslogik im Code-Behind
- RunState inkonsistent
- Smart Action Bar falsche Zustaende
- Move/Execute zu frueh sichtbar oder aktiv
- Config-Aenderung nach DryRun nicht sauber behandelt
- Dashboard zeigt Plan statt Ist
- Cancel / Failed / Partial / Completed falsch dargestellt
- UNKNOWN / Review / Blocked schlecht kommuniziert
- Settings werden still zurueckgesetzt
- Rollback ohne Trash-Integritaetspruefung
- falschen Bindings
- kaputten Commands
- Dispatcher-/Threading-Problemen
- UI friert ein
- Fortschritt falsch
- fehlenden Warnbannern
- ViewModels rechnen fachliche Logik lokal anders als Core/Projection
```

## 2.5 Deep Bughunt – CLI / API / Output-Paritaet

```text
Fuehre einen intensiven Deep Bughunt fuer CLI, API und Output-Paritaet durch.

Suche nach:
- CLI/API verwenden andere Defaults
- PreferRegions-Divergenz
- ConvertFormat wird ueberschrieben
- OnlyGames-Guard nicht zentral
- RunOptionsBuilder normalisiert nicht vollstaendig
- RunRecord fehlt Felder
- ApiRunResult / CLI Output stimmen nicht mit RunProjection ueberein
- Exit Codes falsch
- Sidecar / Report / JSON divergieren
- DryRun-Warnungen fehlen
- incompatible options werden still ignoriert
- cancelled/completed/failure falsch modelliert
- %APPDATA%-Settings in API problematisch
- Entry-Point-Schattenlogik
```

## 2.6 Deep Bughunt – Reports / Audit / Metrics / Rollback

```text
Fuehre einen intensiven Deep Bughunt fuer Reports, Audit, Metrics, Sidecars, Rollback und Forensik durch.

Suche nach:
- Sidecar-Status falsch
- Report zaehlt anders als Projection
- MOVE wird markiert obwohl DryRun
- HealthScore ignoriert Fehler
- Audit-Row fehlt nach physischem Move
- Move-then-Audit nicht atomisch
- Rollback ohne Sidecar / ohne Trash-Integritaet
- HMAC-Key / Verifikation unzuverlaessig
- 7z-Hashing nicht deterministisch
- CUE-Auswahl nicht deterministisch
- DatRename / Sort-Fehler fehlen in hasErrors
- KPI Drift zwischen Dashboard, Report, API, CLI
- Partial Failure wird schoengerechnet
```

## 2.7 Deep Bughunt – Safety / FileSystem / Security

```text
Fuehre einen intensiven Deep Bughunt fuer Safety, FileSystem, Pfadlogik und Security durch.

Suche nach:
- Path Traversal
- ADS
- Extended-Length Prefix
- Reparse Points
- Zip-Slip
- Zip-Bomb
- DTD / XML Parser
- Root Containment
- trailing dot / windows normalization
- locked files / read-only handling
- unsafe rollback
- temp file handling
- external tool argument handling
- timeout/retry/cleanup
- unsafe delete
- hidden data loss paths
```

## 2.8 Deep Bughunt – Tool-Katalog / sichtbare Features

```text
Fuehre einen intensiven Deep Bughunt fuer den sichtbaren Tool-Katalog und die einzelnen Werkzeuge durch.

Suche nach:
- sichtbare Kachel, aber keine echte Funktion
- Stub / Coming Soon / geplanter Dialog
- falscher Name
- redundantes Werkzeug
- Handler kaputt
- Tool-Registrierung ohne saubere Integration
- i18n-/Pinned-/Lookup-Leichen
- Tool tut fachlich nicht, was der Name verspricht
- nur schoener Report-Starter ohne echten Mehrwert
- Tool ist kaputt oder irrefuehrend sichtbar
- Tool sollte Unterfunktion statt eigene Karte sein
- sichtbare Kachel nicht release-tauglich
```

## 2.9 Deep Bughunt – Tests / Invarianten / QA-Luecken

```text
Fuehre einen intensiven Deep Bughunt fuer die Testsuite selbst durch.

Suche nach:
- tautologischen Tests
- no-crash-only Tests
- fehlenden Regressionen
- wichtigen kritischen Pfaden ohne Tests
- Determinismus nicht ausreichend abgesichert
- Cross-Output-Paritaet nicht vollstaendig geprueft
- Snapshot-Luecken
- Benchmark-Gates unzureichend
- Testnamen irrefuehrend
- toten oder veralteten Tests
- Tests, die falsche Sicherheit geben
- fehlenden Edge/Negative Tests
- ungetesteten kritischen Infrastrukturklassen
```

---

# 3. Refactoring und Umsetzung

## 3.1 Refactor mit Plan zuerst

```text
Bitte zuerst nur einen Umsetzungsplan erstellen, noch keinen Code aendern.

Ziel:
Diesen Bereich sauber refactoren, ohne Verhaltensaenderungen einzufuehren.

Der Plan soll enthalten:
1. Ist-Zustand
2. Hauptprobleme
3. Zielstruktur
4. betroffene Dateien
5. Refactor-Schritte in sinnvoller Reihenfolge
6. Risiken
7. Tests, die vor und nach dem Refactor laufen muessen

Wichtig:
- keine neue Schattenlogik
- keine unnoetige Abstraktion
- bestehende Architekturgrenzen respektieren
```

## 3.2 Konkrete Umsetzung

```text
Setze jetzt den Plan um.

Wichtig:
- aendere nur die wirklich betroffenen Dateien
- liefere vollstaendige, integrierbare Aenderungen
- keine halben Loesungen
- keine Pseudocode-Platzhalter
- ergaenze oder aktualisiere passende Tests
- nenne zuerst die betroffenen Dateien

Ausgabeformat:
1. Kurzfazit
2. betroffene Dateien
3. konkrete Aenderungen
4. Tests
5. Verifikation
6. offene Restpunkte
```

## 3.3 Umsetzung nach Bughunt

```text
Auf Basis des Deep Bughunt-Reports sollst du jetzt die priorisierten Bugs technisch beheben.

WICHTIG:
- behebe zuerst P0/P1
- liefere echten Code
- liefere passende Regressionstests
- lasse keine halben Fixes stehen
- wenn etwas blockiert ist, weise es explizit aus
- keine neue Schattenlogik
- keine kosmetischen Nebenbaustellen

Ausgabeformat:
1. Executive Approach
2. Mapping der priorisierten Bugs auf Aenderungen
3. konkrete Codeaenderungen
4. Tests
5. Verifikation
6. Restpunkte
```

---

# 4. Code Review und Hygiene

## 4.1 Strenges Code Review

```text
Mache ein strenges Code Review fuer diese Aenderungen.

Pruefe:
- Korrektheit
- Determinismus
- Sicherheit
- Testbarkeit
- Wartbarkeit
- doppelte Logik
- falsche Layer-Zuordnung
- fehlende Fehlerbehandlung
- fehlende Tests
- stille Verhaltensaenderungen

Ausgabeformat:
- Titel
- Schweregrad
- Impact
- betroffene Datei
- Ursache
- Fix
- Testabsicherung
```

## 4.2 Release-Clean Hygiene Review

```text
Fuehre ein Release-Clean Hygiene Review durch.

Suche gezielt nach:
- totem Code
- verwaisten Registrierungen
- verwaisten i18n-Keys
- doppelten Handlern
- halbfertigen Refactors
- Legacy-Resten
- unnoetigen Utilities
- konkurrierenden Result-/Projection-/Statusmodellen
- wertlosen Tests

Danach:
- priorisieren
- konkrete Bereinigungsschritte vorschlagen
- sagen, was sofort vor Release weg muss
```

## 4.3 Release-Clean Hygiene Umsetzung

```text
Setze jetzt die priorisierten Hygiene-Bereinigungen technisch um.

WICHTIG:
- entferne tote Pfade
- bereinige verwaiste Referenzen
- konsolidiere doppelte Logik
- beseitige Schattenlogik
- verbessere Test-Hygiene
- keine Kernfunktionalitaet beschaedigen
- keine kosmetischen Massenrefactors

Ausgabeformat:
1. Executive Approach
2. Mapping der Findings auf Aenderungen
3. konkrete Codeaenderungen
4. Cleanup-Nachweis
5. Tests
6. Verifikation
7. Restpunkte
```

---

# 5. Tests und QA

## 5.1 Fehlende Tests ergaenzen

```text
Ergaenze sinnvolle Tests fuer diesen Bereich.

Pflicht:
- keine Alibi-Tests
- keine tautologischen Assertions
- echte Fehlerfaelle absichern
- Regressionen fuer behobene Bugs abdecken
- kritische Invarianten absichern

Bitte zuerst sagen:
1. welche Tests fehlen
2. warum sie wichtig sind
3. welche Datei(en) ergaenzt werden
Dann die Tests implementieren.
```

## 5.2 Test-Suite selbst analysieren

```text
Analysiere die Testsuite dieses Bereichs auf Qualitaet und Schutzwert.

Pruefe:
- finden die Tests echte Fehler?
- gibt es tote oder wertlose Tests?
- gibt es tauschbare / doppelte Tests?
- fehlen Determinismus- oder Invariantentests?
- welche kritischen Pfade sind nicht ausreichend abgesichert?

Ausgabeformat:
1. Kurzfazit
2. starke Tests
3. schwache / problematische Tests
4. fehlende Tests
5. priorisierter Test-Sanierungsplan
```

---

# 6. Recognition / DAT / RomVault-nahe Strategie

## 6.1 DAT-first Redesign Analyse

```text
Analysiere die aktuelle Recognition-/DAT-Logik und schlage einen konservativeren DAT-first Umbau vor.

Ziel:
- weniger False Positives
- mehr sichere Erkennung
- Family-based Pipelines
- Sort / Review / Blocked / Unknown sauber trennen

Bitte liefern:
1. Ist-Zustand
2. Hauptursachen fuer Misses und False Positives
3. Zielarchitektur
4. phasenweisen Umsetzungsplan
5. betroffene Dateien
6. noetige Tests und Benchmarks
```

## 6.2 DAT-first Umbauplan fuer Romulus

```text
Analysiere die aktuelle Erkennungs-, Klassifikations- und DAT-Architektur von Romulus und entwerfe einen konkreten DAT-first Umbauplan.

Zielzustand:
1. DAT-first Exact Matching als hoechste Vertrauensstufe
2. Family-based Pipelines statt einer monolithischen Gesamt-Erkennung
3. Match-Level statt nur ja/nein
4. DecisionClass = Sort / Review / Blocked / Unknown
5. Heuristik nur als zweite Linie
6. weniger False Positives
7. starke Trennung von sicheren Welten und unsicheren Welten
8. Preview / Execute / Report / GUI / CLI / API Paritaet

Ausgabeformat:
1. Executive Summary
2. Analyse des Ist-Zustands
3. Zielarchitektur
4. konkrete Architekturänderungen
5. phasenweiser Umsetzungsplan
6. Test- und Benchmark-Strategie
7. Top 15 konkrete Massnahmen
8. Risiken / offene Punkte
```

## 6.3 Phase-1 Umsetzung DAT-first

```text
Setze jetzt den priorisierten DAT-first Umbauplan fuer Romulus technisch um.

WICHTIG:
- Beginne mit Phase 1 und nur Phase 1
- liefere echten, integrierbaren Code
- fuehre keine neue Schattenlogik ein
- respektiere bestehende Architekturgrenzen
- ergaenze passende Tests
- sichere Preview / Execute / Report / GUI / CLI / API Paritaet
- wenn etwas blockiert ist, explizit als Restpunkt ausweisen

AUSGABEFORMAT
1. Executive Approach
2. Mapping der Phase-1-Massnahmen auf Dateien
3. konkrete Codeaenderungen
4. Tests
5. Verifikation
6. Restpunkte
```

---

# 7. Features und Produktideen

## 7.1 Allgemeine Produktanalyse inkl. Chancen

```text
Analysiere Romulus als Gesamtprodukt.

Die Analyse soll untersuchen:
- was Romulus aktuell ist
- was es heute schon gut kann
- wo Produktluecken bestehen
- welche Moeglichkeiten sich aus dem heutigen Stand ergeben
- welche Features echten Mehrwert bringen koennten

Beruecksichtige:
- Einsteiger
- Power-User
- Sammler
- Archivare
- Arcade-/Redump-/No-Intro-Nutzer
- Frontend-/LaunchBox-Nutzer
- Nutzer grosser Netzlaufwerke

Ausgabeformat:
1. Executive Summary
2. Was Romulus aktuell ist
3. Funktions- und Bereichsanalyse
4. Staerken des Tools
5. Schwaechen und Luecken
6. Moeglichkeiten und Entwicklungspotenziale
7. konkrete Feature-Vorschlaege
8. Top 10 Ausbauideen
9. was bewusst nicht verfolgt werden sollte
10. empfohlene naechste Schritte
```

## 7.2 Neue Features mit Backlog

```text
Analysiere Romulus tief und entwickle konkrete Feature-Vorschlaege.

Du sollst:
1. den aktuellen Funktionsumfang grob einordnen
2. erkennen, wo dem Tool echter Mehrwert fehlt
3. typische Probleme der ROM-Community identifizieren
4. neue Features und Erweiterungen vorschlagen
5. auch Ideen nennen, die so in anderen Tools kaum oder gar nicht vorhanden sind
6. jede Idee auf Nutzen, Umsetzbarkeit, Risiko und Differenzierung pruefen
7. das Ergebnis als priorisierbares, bearbeitbares Backlog strukturieren

Ausgabeformat:
1. Executive Summary
2. Analyse der Produktluecken
3. Neue Feature-Vorschlaege
4. Ideen mit moeglichem Alleinstellungsmerkmal
5. Was bewusst nicht gemacht werden sollte
6. priorisiertes Backlog
7. empfohlene Roadmap
```

## 7.3 Wirklich neue / seltene Ideen

```text
Entwickle innovative, ungewoehnliche und differenzierende Feature-Ideen fuer Romulus.

Fokus:
- keine generischen Standardideen
- keine reine Kosmetik
- echte Community-Probleme loesen
- Funktionen finden, die bestehende ROM-Tools so kaum haben

Ausgabeformat:
1. Executive Summary
2. ungeloeste oder schlecht geloeste Community-Probleme
3. konkrete innovative Feature-Ideen
4. Ideen mit echtem Alleinstellungsmerkmal
5. was bewusst nicht gemacht werden sollte
6. priorisiertes Innovations-Backlog
7. Top 10
```

---

# 8. Tool-Katalog / sichtbare Kacheln / Features

## 8.1 Kachelweise Tool-Pruefung

```text
Pruefe die gesamte sichtbare Werkzeugseite von Romulus vollstaendig, kachelweise, nummeriert und ohne Auslassung.

Fuer jede Kachel pruefen:
- Name
- Bereich
- Sichtbarkeit
- Zweck
- wird wirklich gebraucht?
- Implementierungsstand
- fachliche Korrektheit
- technische Korrektheit
- Integrationsqualitaet
- Architekturqualitaet
- Testlage
- Hauptprobleme
- Entscheidung: behalten / reparieren / umbenennen / konsolidieren / verstecken / deaktivieren / entfernen / Epic

WICHTIG:
Keine Kachel auslassen.
Jede Kachel einzeln bewerten.
```

## 8.2 Tool-Katalog umsetzen

```text
Setze jetzt die priorisierten Sanierungen aus dem Tool-Katalog-Audit technisch um.

WICHTIG:
- entferne oder verstecke nicht release-taugliche Kacheln
- repariere kaputte Kacheln
- konsolidiere redundante Kacheln
- benenne irrefuehrende Kacheln korrekt um
- entferne verwaiste Registrierungen, Commands, Handler, i18n-Keys und Pinned Keys
- keine neue Schattenlogik

Ausgabeformat:
1. Executive Approach
2. Mapping der priorisierten Kachel-Probleme auf Aenderungen
3. konkrete Codeaenderungen
4. Tests
5. Verifikation
6. Restpunkte
```

---

# 9. Release-Audits

## 9.1 Final Release Audit

```text
Pruefe Romulus vor dem Release auf Herz und Nieren.

WICHTIG:
- wirklich alles kritisch pruefen
- jede relevante Kernfunktion auf Korrektheit pruefen
- Bugs, Fehlverhalten, Paritaetsfehler, Datenintegritaetsrisiken, UX-Fallen und Testluecken offenlegen
- keine Schoenfaerberei

Prueffelder:
- Recognition / Classification / Sorting
- DAT / Verification
- Conversion
- Reports / Audit / Rollback
- GUI / CLI / API
- Tool-Katalog
- Safety / Security
- Tests / Invarianten
- Code Hygiene

Ausgabeformat:
1. Executive Verdict
2. Release-Blocker
3. P1/P2/P3 Findings
4. Test- und Verifikationsluecken
5. Top 20 Massnahmen vor Release
6. Schlussurteil
```

## 9.2 Release-Clean Abschlussrunde

```text
Fuehre eine letzte release-orientierte Abschlusspruefung durch.

Suche nur nach Dingen, die kurz vor Release noch peinlich, gefaehrlich oder vertrauensschaedlich waeren:
- sichtbare halbfertige Features
- irrefuehrende Bezeichnungen
- tote UI-Pfade
- inkonsistente Status
- falsche Reports / KPI-Anzeigen
- stiller Datenverlust
- fehlende Warnungen
- falsche Defaults
- verwaiste Registrierungen / i18n / Commands
- ungetestete kritische Sonderfaelle

Danach priorisiert sagen:
- was noch zwingend vor Release gemacht werden muss
- was tolerierbar waere
- was auf spaeter verschoben werden kann
```

---

# 10. Praktische Kurzprompts fuer den Alltag

## 10.1 Schnellanalyse

```text
Analysiere diesen Bereich kurz, aber praezise: Staerken, Risiken, betroffene Dateien, wichtigste naechste Schritte.
```

## 10.2 Nur Plan, noch kein Code

```text
Erstelle zuerst nur einen technischen Umsetzungsplan. Noch keinen Code aendern.
```

## 10.3 Nur Bugfix

```text
Finde den eigentlichen Fehler, erklaere ihn kurz und behebe ihn mit minimal noetigen Aenderungen plus passenden Regressionstests.
```

## 10.4 Nur Tests

```text
Ergaenze nur sinnvolle Tests fuer diesen Bereich. Keine Alibi-Tests. Absichern, was wirklich kritisch ist.
```

## 10.5 Nur Review

```text
Mache ein hartes Code Review fuer diese Aenderung. Fokus auf Korrektheit, Determinismus, Sicherheit, Paritaet und Testluecken.
```

---

# 11. Empfohlene Reihenfolge fuer grosse Themen

## Wenn du ein kritisches Thema angehst

1. Analyse / Deep Bughunt
2. Plan
3. Umsetzung
4. Tests
5. Review
6. Verifikation / Release-Check

## Fuer Recognition / DAT

1. Deep Bughunt Recognition
2. DAT-first Analyse
3. DAT-first Umbauplan
4. Phase-1 Umsetzung
5. Benchmark / QA

## Fuer GUI

1. Deep Bughunt GUI
2. UX-/State-Analyse
3. Plan
4. Umsetzung
5. ViewModel-/State-Tests

## Fuer Release-Cleanup

1. Hygiene Review
2. Release-Clean Umsetzung
3. Final Release Audit

---

# 12. Hinweis fuer die Arbeit mit Copilot

Bei komplexen Aufgaben in VS Code:

* relevante Dateien offen halten
* wenn moeglich Code markieren
* zuerst Plan verlangen
* danach denselben Chat fuer die Umsetzung nutzen
* bei Architekturfragen keine zu grossen Umbauten auf einmal verlangen
* lieber phasenweise arbeiten
* Copilot moeglichst mit klaren Dateien, Zielen und Constraints fuehren
