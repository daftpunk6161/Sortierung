# AGENTS.md

## Repository expectations

Dieses Repository gehoert zum Projekt **Romulus**.

Romulus ist ein produktionsnahes C# .NET 10 Tool zur Verwaltung und Bereinigung von ROM-Sammlungen mit drei Entry Points:

- GUI: WPF/XAML
- CLI: headless / automation / CI
- REST API: ASP.NET Core Minimal API

Kernfunktionen:
- regionsbasierte Deduplizierung
- Junk-Erkennung
- DAT-basierte Verifizierung
- konsolenbezogene Klassifikation und Sortierung
- Formatkonvertierung
- Audit / Undo / Rollback / Reports

Aktive Entwicklung erfolgt ausschliesslich in `src/`.
`archive/powershell/` ist nur Legacy-Referenz und darf nicht als aktive Implementierungsbasis behandelt werden.

---

## Projektname und Bestandskontext

Der Produktname ist **Romulus**.

Der bestehende Code, die Projekte, Namespaces, Pfade, Settings und Dateinamen koennen weiterhin den Legacy-Namen **Romulus** tragen.
Bei allen Aenderungen muss immer die **tatsaechliche bestehende Repo-Struktur** respektiert werden.

Wichtig:
- nichts blind global umbenennen
- keine grossflaechigen Rename-Aktionen ohne expliziten Auftrag
- bestehende Projektnamen und Pfade nur dann aendern, wenn dies Teil eines klaren, abgesicherten Refactors ist

---

## Nicht verhandelbare Regeln

### 1) Release-Faehigkeit geht vor
Bevorzuge immer:
- Korrektheit
- Determinismus
- Sicherheit
- Testbarkeit
- Wartbarkeit

gegenueber:
- Feature-Hype
- unnoetiger Abstraktion
- kosmetischem Refactoring
- UI-Spielerei

### 2) Kein Datenverlust
- Standardverhalten ist **Move to Trash / Audit / Undo-faehiges Verhalten**
- Kein direktes Loeschen ohne explizite, klar abgesicherte Benutzerentscheidung
- Riskante Operationen brauchen Summary, Schutzmechanismus und Audit
- Source-Dateien nie vor erfolgreicher Verifikation entfernen
- Partielle Outputs bei Fehlern sauber behandeln

### 3) Determinismus ist Pflicht
Gleiche Inputs muessen gleiche Outputs erzeugen.
Das gilt insbesondere fuer:
- GameKey-Bildung
- Region-Erkennung
- Score-Berechnung
- Winner-Selection
- Preview vs Execute
- GUI / CLI / API / Report-Paritaet

### 4) Keine doppelte Logik
- Geschaeftslogik darf nicht in mehreren Entry Points separat nachgebaut werden
- Gemeinsame Regeln gehoeren in Core oder klar definierte Services
- Duplikate aktiv vermeiden und bei Gelegenheit abbauen

Keine doppelte Logik bedeutet auch:
- keine konkurrierenden Statusmodelle
- keine konkurrierenden Result-/Projection-Berechnungen
- keine konkurrierenden Mapping-Pfade zwischen GUI, CLI, API und Reports
- keine lokalen Neuberechnungen, wenn bereits ein zentrales Modell existiert

### 5) Eine fachliche Wahrheit
Fuer jede relevante Entscheidung muss gelten:
- dieselbe Eingabe erzeugt dieselbe fachliche Entscheidung in GUI, CLI, API und Reports
- Preview und Execute duerfen fachlich nicht divergieren
- KPI- und Ergebnisdarstellungen duerfen nicht an mehreren Stellen separat hergeleitet werden, wenn bereits ein zentrales Modell existiert

### 6) Keine halben Loesungen
Erzeuge keinen:
- Pseudocode
- Platzhaltercode
- TODO-only-Code
- Scheincode ohne echte Integration
- erfundene APIs oder Klassen ohne klare Projektpassung

Wenn Code erwartet ist, liefere **kompilierbaren, konsistenten und integrierbaren Code**.

Zusaetzlich gilt:
- keine isolierten neuen Klassen ohne echte Verdrahtung
- keine "future-proof" Abstraktionen ohne aktuellen Bedarf
- keine neue Architektur-Schicht ohne klaren Nutzen
- keine neuen Services, wenn bestehende sauber erweitert werden koennen

---

## Architekturregeln

Dependency-Richtung:

**Entry Points -> Infrastructure -> Core -> Contracts**

Nie umgekehrt.

### Schichten
- **Contracts**: Interfaces, Models, Fehlervertraege
- **Core**: pure Domaenenlogik, keine I/O-Abhaengigkeiten
- **Infrastructure**: Datei-, Tool-, Report-, Hash-, DAT-, Logging- und Orchestrierungsadapter
- **CLI / API / GUI**: Entry Points, Komposition, Benutzerinteraktion

### Pflichtregeln
- Keine I/O-Logik in `Romulus.Core`
- Keine Businesslogik in WPF Code-Behind
- MVVM in der GUI einhalten
- Services per Constructor Injection
- Harte Umgebungsabhaengigkeiten kapseln
- Zeit, Prozesse, Dateisystem, externe Tools und Umgebungswerte hinter testbaren Abstraktionen halten
- Keine Schattenlogik in GUI, CLI oder API, wenn bereits eine zentrale fachliche Implementierung existiert

---

## GUI / WPF Regeln

Die GUI muss:
- verstaendlich
- luftig
- robust
- nicht ueberladen
- fehlbedienungssicher

sein.

### Pflicht
- Standardablauf ist:
  **DryRun / Preview -> Summary -> Bestaetigung -> Apply / Move -> Report / Undo**
- Lange Operationen nicht auf dem UI-Thread ausfuehren
- UI-Updates sauber ueber Dispatcher / async Patterns
- Kein `DoEvents`-aehnliches Muster
- Styles, Farben und Spacing zentral in `ResourceDictionary`
- Zwei Bedienmodi unterstuetzen:
  - einfach
  - experte

### Zusatzregel
UI-Zustaende, KPI-Anzeigen, Run-Status und Summary-Werte sollen nach Moeglichkeit aus klaren Projektionen / ViewModels stammen und nicht an mehreren Stellen separat hergeleitet werden.

### Verboten
- Businesslogik im Code-Behind
- unklare Danger Actions
- ueberladene Screens ohne klare Priorisierung
- unkontrollierte Converter-Logik fuer fachliche Regeln

---

## Sicherheitsregeln

Diese Regeln sind zwingend:

- **Path Traversal blockieren**
  - vor Move/Copy/Delete immer Root-validierte Pfadauflosung verwenden
- **Zip-Slip blockieren**
  - Archivpfade vor Extraktion validieren
- **Reparse Points nicht transparent folgen**
  - explizit blockieren oder sicher definieren
- **CSV-Injection verhindern**
  - keine ungesicherten Formel-Praefixe in Exportfeldern
- **HTML-Encoding konsequent anwenden**
  - alle HTML-Reports muessen sauber escapen
- **Externe Tools absichern**
  - Tool-Hash-Verifizierung
  - korrektes Argument-Quoting
  - Exit-Code-Pruefung
  - Timeout / Retry / Cleanup
  - Tool-Ausgabe nicht blind vertrauen
  - Output validieren, wenn Folgeentscheidungen davon abhaengen
  - partielle Outputs bei Fehlern sauber behandeln
  - Source-Dateien nie vor erfolgreicher Verifikation entfernen

---

## Kernlogik-Regeln

### Kritische Bereiche
Diese Bereiche duerfen nicht still veraendert werden:
- `GameKeyNormalizer`
- Region-Erkennung
- `FormatScore`
- `VersionScore`
- `DeduplicationEngine.SelectWinner`

Aenderungen in diesen Bereichen muessen:
- deterministisch bleiben
- bestehende Invarianten respektieren
- durch gezielte Tests abgesichert werden

### Preview / Run / Report Paritaet
Bei jeder Aenderung, die Runs oder Ergebnisse beeinflusst, muss sichergestellt werden:
- Preview zeigt dieselben fachlichen Entscheidungen wie Execute
- Reports zaehlen dieselben Entscheidungen korrekt
- GUI, CLI und API widersprechen sich nicht
- KPI-Werte stammen aus derselben fachlichen Wahrheit

---

## Tests

Jede relevante Aenderung an Core, Safety, Run-Verhalten, Reports, DAT, Toolintegration, Conversion, Sorting, API-Output oder GUI-State braucht passende Tests.

### Pflicht-Testarten
- Unit
- Integration
- Regression
- Negative / Edge

### Test-Pflicht
Bei Aenderungen an Core, Safety, Orchestration, Conversion, Sorting, Reports, DAT, API-Output oder GUI-State gilt:
- bestehende Tests aktualisieren, wenn Verhalten bewusst geaendert wird
- neue Tests ergaenzen, wenn neue Logik entsteht
- Regressionstests ergaenzen, wenn ein Bug gefixt wird
- Invariantentests ergaenzen, wenn zentrale Domaenenregeln betroffen sind

Eine Aenderung ist nicht fertig, wenn sie nicht sinnvoll testbar abgesichert ist.

### Kritische Invarianten
Tests muessen pruefen, dass:
- kein Move ausserhalb erlaubter Roots moeglich ist
- keine leeren oder inkonsistenten Keys entstehen
- Winner-Selection deterministisch bleibt
- Preview / Execute / Report konsistent sind
- GUI / CLI / API / Reports dieselbe fachliche Wahrheit abbilden
- fehlerhafte Archive und Sonderfaelle sauber behandelt werden

### Keine Alibi-Tests
Ein Test ist nur wertvoll, wenn er einen realen Fehler finden kann.

Vermeide insbesondere:
- no-crash-only Tests
- tautologische Assertions
- Tests ohne echte Verifikationsaussage
- Pseudo-Abdeckung ohne fachlichen Schutzwert

---

## Code Hygiene

Achte aktiv auf:
- toten Code
- verwaiste Registrierungen
- verwaiste i18n-Keys
- doppelte Handler
- doppelte Services
- Copy-Paste-Logik
- halbfertige Refactors
- leere oder veraltete Verzeichnisse
- ungenutzte Models / DTOs / Commands
- Magic Strings / Magic Numbers an kritischen Stellen
- doppelte Projection-/Result-/Policy-Wege
- Legacy-Reste, die still weiterleben

Bereinige solche Probleme, wenn sie klar im Scope liegen und keine unnoetige Nebenbaustelle erzeugen.

---

## Arbeitsweise bei Aenderungen

Wenn du Aenderungen vorschlaegst oder erzeugst:
1. aendere nur die wirklich betroffenen Teile
2. halte Architekturgrenzen ein
3. benenne betroffene Dateien klar
4. erklaere funktionale Auswirkungen
5. ergaenze oder aktualisiere passende Tests
6. vermeide unnoetige Stil- oder Strukturaenderungen

Wenn eine Aenderung mehrere Schichten betrifft, liefere die Aenderung konsistent ueber alle noetigen Dateien hinweg statt nur Teilfragmente.

Wenn konkrete Code-Aenderungen gefragt sind:
- liefere direkt umsetzbare Aenderungen
- nenne betroffene Dateien zuerst
- vermeide lange Vorreden
- bevorzuge vollstaendige, zusammenhaengende Patches statt loser Einzelideen

---

## Arbeitsweise bei Reviews / Analysen

Priorisierung:
### Prioritaet 1 – Release-Blocker
- Datenverlust
- Security-Probleme
- falsche Winner-Selection
- falsches Grouping / Scoring
- Preview / Execute / Report Divergenz
- GUI-Fehlverhalten mit Fehlbedienungsrisiko
- Deadlocks, Haenger, nicht abbrechbare Prozesse

### Prioritaet 2 – Hohe Risiken
- doppelte Logik
- fragile Tool-Integration
- schlechte Testbarkeit
- versteckte Seiteneffekte
- inkonsistente Fehlerbehandlung
- Schattenlogik
- konkurrierende Wahrheiten

### Prioritaet 3 – Wartbarkeit
- unnoetige Komplexitaet
- Naming- oder Strukturprobleme
- Erweiterbarkeitsprobleme
- UI-Unklarheiten ohne unmittelbares Fehlverhalten
- Hygiene-Probleme und tote Pfade

### Review-Format
Wenn moeglich:
- **Titel**
- **Schweregrad**
- **Impact**
- **Betroffene Datei(en)**
- **Beispiel / Reproduktion**
- **Ursache**
- **Fix**
- **Testabsicherung**

---

## Antwortverhalten

Wenn du nicht-triviale Hilfe gibst, strukturiere die Antwort bevorzugt so:
1. Kurzfazit
2. Risiken / Blocker
3. konkrete Aenderungen
4. betroffene Dateien
5. Testbedarf
6. offene Annahmen

Wenn Code erzeugt wird, liefere vollstaendige, zusammenhaengende Aenderungen statt loser Fragmente.

---

## Wichtige Projektfakten

### Plattform
- Windows 10/11
- .NET 10
- `net10.0`
- `net10.0-windows` fuer WPF
- C# LangVersion 14

### Wichtige Datenquellen
- `data/consoles.json`
- `data/console-maps.json`
- `data/rules.json`
- `data/dat-catalog.json`
- `data/defaults.json`
- `data/tool-hashes.json`
- `data/conversion-registry.json`
- `data/ui-lookups.json`

### User Settings
- `%APPDATA%\\Romulus\\settings.json`

### Wichtige Entry Points
- `src/Romulus.CLI`
- `src/Romulus.Api`
- `src/Romulus.UI.Wpf`

### Wichtige Kernbereiche
- `Romulus.Contracts`
- `Romulus.Core/GameKeys`
- `Romulus.Core/Regions`
- `Romulus.Core/Scoring`
- `Romulus.Core/Deduplication`
- `Romulus.Core/Classification`
- `Romulus.Core/Conversion`
- `Romulus.Infrastructure/Orchestration`
- `Romulus.Infrastructure/FileSystem`
- `Romulus.Infrastructure/Tools`
- `Romulus.Infrastructure/Reporting`
- `Romulus.Infrastructure/Dat`
- `Romulus.Infrastructure/Conversion`
- `Romulus.Infrastructure/Hashing`
- `Romulus.Infrastructure/Safety`
- `Romulus.Infrastructure/Sorting`

---

## Solo-Mode (aktiv ab 2026-05-04)

Romulus ist ab 2026-05-04 bewusst im **Solo-Mode**: einzige aktive Nutzerin/Nutzer ist der Repo-Maintainer (daftpunk6161). Externalisierung (Beta-Cohort, Discovery-Loop, oeffentlicher Release) ist deferred bis Maintainer-Greenlight gemaess `docs/plan/strategic-reduction-2026/discovery-loop-playbook.md` Sektion "Solo-Mode und Externalisierungs-Greenlight".

Konsequenzen fuer alle Aenderungen:
- Identitaets-Guardrail (siehe unten) gilt im Solo-Mode **strenger**, nicht lockerer. Scope-Creep ist die Hauptgefahr in dieser Phase.
- Keine Output-Sprints "weil-ich-Lust-habe". Jede neue Funktion muss die Identitaets-Frage klar mit Ja beantworten.
- Keine Self-Validierung als Discovery-Loop-Ersatz. Maintainer-Nutzung ist nicht Beta-Validierung.
- Vor jedem groesseren Feature-Vorschlag pruefen, ob er der Externalisierungs-Greenlight-Kriterien naeher bringt oder nur Komfort fuer Solo-Use.

Sanktion: PRs, die diese Regeln verletzen, werden im Review abgelehnt.

Owner: Repo-Maintainer (daftpunk6161). Aenderung dieser Regel nur per neuem ADR.

---

## Audit-Moratorium (Welle 1-3) — abgeloest durch Solo-Mode

**Status ab 2026-05-04:** abgeloest. Das urspruenglich bis 2026-05-28 befristete Audit-Moratorium ist mit Eintritt in den Solo-Mode (siehe oben) **dauerhaft fortgesetzt**, aber inhaltlich angepasst:

- Solo-Mode bedeutet: nur Maintainer arbeitet aktiv am Repo, daher gibt es keine Audit-Wucherungs-Dynamik mehr durch parallele Subagents.
- Die Verbots-Liste unten gilt weiter als Default-Haltung (keine Sammel-Audits, keine Findings-Tracker auf Vorrat).
- Bei Externalisierungs-Greenlight (Welle 12) wird die Moratoriums-Regel neu bewertet, NICHT automatisch reaktiviert.

Urspruengliche Moratoriums-Regel (bleibt als Default-Haltung gueltig):

**Verboten:**
- neue Deep-Dive-Audits oder Repo-wide Audit-Berichte (`*audit*.md`, `*deep-dive*.md`, `*findings-tracker*.md`)
- neue Findings-Tracker, Re-Audit-Round-Dokumente, Bug-Hunt-Reports
- neue Dateien unter `archive/` mit Audit-/Findings-/Tracker-Charakter
- Auftraege an Subagents wie "fuehre einen weiteren Deep-Dive durch" oder "audite das Repo erneut"

**Erlaubt:**
- konkrete P1-Sicherheits- oder Datenintegritaets-Befunde, die zu einem direkten Fix-PR fuehren (kein Sammeldokument)
- Pin-Tests / Regressionstests, die einen einzelnen konkreten Bug absichern
- Code-Reviews auf konkreten PRs (gem-reviewer / gem-critic auf eine Aenderung, nicht auf das ganze Repo)

**Sanktion:** PRs, die das Moratorium verletzen, werden im Review abgelehnt. Reviewer pruefen die Moratorium-Checkbox im PR-Template.

**Owner:** Repo-Maintainer (daftpunk6161).

**Begruendung:** Vor Welle 1 lagen ueber zehn parallele Audit-Dokumente im Repo, ohne dass ihre Findings systematisch abgearbeitet wurden. Das Moratorium zwingt das Team, die Welle 1-3 Reduktion abzuschliessen, bevor neue Audit-Schichten oben drauf kommen. Nach Abschluss von Welle 3 wird der Status neu bewertet.

---

## Identitaets-Guardrail (dauerhaft, ab 2026-05-04)

Romulus ist ein **Safe ROM Library Cleanup & Verification Tool mit Audit Trail**. Diese Identitaet ist die Grenze fuer jede neue Funktion.

**Pflichtfrage fuer jede neue Funktion / jeden neuen PR:**

> Schaerft diese Aenderung die Sichere-Cleanup-Identitaet (Cleanup, Verifikation, Audit, Determinismus, Sicherheit) — oder verschiebt sie Romulus in fremdes Revier?

Ist die Antwort nicht klar **Ja**, wird die Aenderung im Review abgelehnt.

**Harte Streichliste (kein Pfad zurueck ohne ausdruecklichen ADR-Beschluss):**

- Frontend-Builder / Mapping-Wizards fuer LaunchBox / RetroBat / EmulationStation / RetroArch
- Scraper / ScreenScraper-Integration
- Patching-Workflows (IPS / BPS / xdelta) als Hauptfunktion
- MAME-Set-Builder / Rom-Manager-Funktionalitaet
- Plugin-System / Marketplace
- Telemetrie ohne explizites Opt-in
- "Coming Soon"-Features ohne aktiven Bedarfs-Beleg
- BONUS-Tasks aus `docs/plan/strategic-reduction-2026/plan.yaml`, deren Aktivierungsbedingung nicht erfuellt ist

**PR-Template-Pflicht:** Jede PR beantwortet die Identitaets-Frage explizit (siehe Abschnitt "Identitaets-Check" in `.github/PULL_REQUEST_TEMPLATE.md`). Reviewer pruefen die Antwort.

**Owner:** Repo-Maintainer (daftpunk6161). Aenderung dieser Regel nur per neuem ADR.

---

## Was du vermeiden musst
- keine stillen Verhaltensaenderungen
- keine Logikduplikation zwischen GUI, CLI und API
- keine Businesslogik im WPF Code-Behind
- keine Umgehung von Safety-Checks
- keine Refactors ohne Verifikation
- keine kosmetischen Aenderungen, wenn funktionale Risiken offen sind
- keine Annahmen, die Projektstruktur oder bestehende Vertraege verletzen
- keine neue Schattenlogik
- keine konkurrierenden Wahrheiten fuer Status, KPIs, Results oder Reports