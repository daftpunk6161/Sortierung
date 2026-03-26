---
description: Projektweite Instructions für Romulus / RomCleanup (C# .NET 10 + WPF/XAML). Gilt für alle Dateien im Repo, insbesondere C#-Code, UI, Tests, Build- und Konfigurationsdateien.
paths:
  - "**/*.cs"
  - "**/*.xaml"
  - "**/*.csproj"
  - "**/*.sln"
  - "**/*.json"
  - "**/*.md"
  - "**/*.yml"
  - "**/*.yaml"
  - "src/**/*"
---

# Romulus (RomCleanup) – Workspace Coding Guidelines

Diese Regeln gelten **projektweit, dauerhaft und unabhängig vom konkreten Task**.  
Sie sind die verbindliche Qualitätsbasis für **Code-Generierung, Refactoring, Reviews, Bugfixing, UI-Arbeit und Tests**.

Ziel ist ein **release-fähiges**, **sicheres**, **deterministisches**, **wartbares** und **testbares** Tool mit **moderner, retro-inspirierter, klar verständlicher GUI/UX** auf Basis von **C# .NET 10 + WPF/XAML**.

> Die frühere PowerShell-Version ist archiviert und dient nur als historische Referenz.  
> Aktive Entwicklung erfolgt ausschließlich in `src/`.  
> Neue Features, Fixes und Refactorings dürfen nicht auf der archivierten PowerShell-Logik aufbauen, ohne sie zuerst kritisch gegen die aktuelle C#-Architektur zu validieren.

---

## 1) Nicht verhandelbare Grundprinzipien

- **Stabilität vor Feature-Hype.**  
  Jede Änderung muss einen klaren Nutzen haben und gegen Risiko, Testaufwand und Wartbarkeit abgewogen werden.

- **Kein Datenverlust.**  
  Standardverhalten ist **Move to Trash / Audit / Undo-fähig**, nicht permanentes Löschen.  
  Permanentes Löschen ist nur zulässig, wenn es explizit angefordert, klar bestätigt und sauber protokolliert wird.

- **Determinismus ist Pflicht.**  
  Gleiche Inputs müssen zu gleichen Outputs führen.  
  Das gilt insbesondere für:
  - Grouping / GameKey
  - Region-Erkennung
  - Score-Berechnung
  - Winner Selection
  - Report-Zahlen
  - Preview vs. Execute
  - CLI/API/GUI-Parität

- **Keine doppelte Logik.**  
  Es muss eine klare Source of Truth geben.  
  Duplikate, parallele Implementierungen und toter Code sind aktiv zu identifizieren und zu entfernen.

- **Keine Scheinlösungen.**  
  Keine Platzhalter, kein Pseudocode, keine “TODO-only”-Antworten, keine halbfertigen Snippets, wenn produktionsreifer Code erwartet ist.

- **Keine Alibi-Tests.**  
  Tests müssen reale Fehler finden können.  
  Ein grüner Test ohne Aussagekraft ist wertlos.

- **Release-Fokus.**  
  Entscheidungen müssen sich daran orientieren, ob sie das Produkt sicherer, stabiler, verständlicher und release-fähiger machen.

---

## 2) Architekturregeln

Das System muss klar in Schichten getrennt bleiben:

- **UI (WPF/XAML)**  
  Darstellung, Bindings, Commands, Visual States, Styles, Templates

- **ViewModels / Presentation Logic**  
  Zustandslogik der Oberfläche, Commands, UI-orientierte Validierung, orchestration-nahe Darstellung

- **Application / Orchestration Layer**  
  Ablaufsteuerung, Use Cases, Progress, Cancellation, Config laden/speichern, Koordination von Services

- **Core Engine**  
  Pure logic ohne UI-Abhängigkeiten: Scoring, Policies, GameKey, Region Detection, Winner Selection, Sortierentscheidungen

- **IO / Safety Layer**  
  Dateisystemzugriffe, Enumerierung, Move/Trash, Root-Validierung, Reparse-Point-Schutz, Archive-Schutz

- **External Tool Integration**  
  `chdman`, `dolphintool`, `7z`, `psxtract` etc.; Argumentaufbau, Quoting, Exit-Code-Handling, Timeouts, Retries

- **Reports / Export**  
  CSV/HTML/JSON-Ausgabe, Escaping, Encoding, Injection-Schutz, konsistente Kennzahlen

- **DAT / Hashing / Indexing**  
  Streaming XML, Cache, Lookup, Thresholds, Matching

- **Conversion**  
  Formatkonvertierung (chdman, dolphintool, 7z, psxtract etc.), ConversionRegistry, ConversionExecutor, Tool-Validierung, Safety-Einstufung

### Verbindliche Architekturregeln
- Core-Logik muss **ohne UI, Globals und unnötige Side Effects** testbar sein.
- Code-Behind in WPF nur für echte View-spezifische Fälle, nicht für Businesslogik.
- MVVM ist der Standard. Logik gehört in ViewModels/Services, nicht in XAML-Hacks oder Code-Behind.
- IO, Prozessaufrufe, Zeit, Zufall und Umgebungszugriffe müssen hinter testbaren Abstraktionen liegen.
- Gemeinsame Infrastruktur wie Logging, Quoting, Temp-Dateien, Sanitizing, Encoding und Error Handling muss zentralisiert sein.
- Neue Features dürfen die Trennung der Schichten nicht aufweichen.

---

## 3) GUI / UX Regeln (höchste Priorität für WPF/XAML)

Ziel ist eine Oberfläche, die **selbsterklärend, luftig, robust, nicht überladen und ohne Footguns** ist.

### Informationsarchitektur
- Häufige Aktionen nach vorne, seltene Optionen in klar getrennte Advanced-Bereiche.
- Komplexe Workflows als **Wizard / Stepper / klar geführter Ablauf** gestalten.
- Typischer Flow soll klar erkennbar sein:  
  **Roots → Optionen → Preview → Confirm → Run → Report / Undo**

### Layout
- Verwende bevorzugt `Grid` mit klaren Rows/Columns.
- Konsistente Margins, Padding, Section-Abstände und MinSizes.
- Keine überlappenden Controls, keine abgeschnittenen Inhalte, kein unvorhersehbares Resize-Verhalten.
- Scrollbare Bereiche nur dort, wo sie fachlich sinnvoll sind.
- Statusinformationen nicht als Textwüste darstellen, sondern als klar getrennte Bereiche für:
  - aktuelle Phase
  - Fortschritt
  - Warnungen
  - Zusammenfassung
  - nächste sinnvolle Aktion

### Interaktion
- Gefährliche Aktionen müssen visuell und logisch klar getrennt sein.
- Preview und Execute dürfen nie missverständlich wirken.
- Buttons, Labels, Tooltips und Warntexte müssen eindeutig und nicht doppeldeutig sein.
- Abbruch, Retry, Undo und Fehlerzustände müssen für Benutzer klar nachvollziehbar sein.

### Styling
- Farben, Typografie, Spacing und Control-Styles zentral über `ResourceDictionary`.
- Retro-modern ist erlaubt, aber **Lesbarkeit und Kontrast haben immer Vorrang**.
- Theme-Fähigkeit mitdenken, ohne Accessibility zu opfern.
- Keine visuelle Spielerei, die die Informationsklarheit verschlechtert.

### Accessibility / Usability
- Ausreichender Kontrast
- Saubere Fokus-Reihenfolge
- Klare Tastaturbedienbarkeit
- Sinnvolle Fehlermeldungen
- Keine rein farbbasierte Zustandskommunikation

---

## 4) Sicherheit und Datenintegrität

Diese Regeln sind zwingend:

- **Path Traversal verhindern**  
  Jeder Zielpfad muss normalisiert und gegen erlaubte Roots validiert werden.

- **Zip-Slip verhindern**  
  Archive Entries dürfen nie außerhalb des erlaubten Zielpfads extrahieren.

- **Reparse Points strikt behandeln**  
  Symlinks/Junctions entweder blockieren oder explizit und sicher definieren.  
  Niemals implizit folgen.

- **CSV Injection verhindern**  
  Zellen mit potenziellen Formeln absichern.

- **HTML Escaping konsequent durchführen**  
  Reports dürfen keine unescaped Nutzdaten rendern.

- **Externe Tools nie blind vertrauen**  
  Exit Codes, stderr/stdout, Timeouts, Pfad-Quoting und Fehlerbilder sauber behandeln.

- **Risky Operations absichern**  
  Move/Delete/Convert nur mit:
  - klarer Summary vor Ausführung
  - nachvollziehbarer Benutzerbestätigung
  - Audit-Log
  - soweit möglich Undo/Recovery-Pfad

---

## 5) Performance und Skalierung

- Keine unkontrollierte Rekursion über grosse Dateibäume.
- Dateisystem-Enumeration iterativ und ressourcenschonend gestalten.
- Hashing und Parsing streaming-basiert umsetzen.
- Hotspots identifizieren und unnötige Mehrfachscans vermeiden.
- Regex nur gezielt einsetzen; Hotspot-Regex prüfen und bei Bedarf kompilieren.
- UI darf niemals durch lange Operationen blockieren.
- Lange Tasks gehören off the UI thread.
- Progress, Cancellation und Fehlerzustände müssen robust sein.
- Keine DoEvents-ähnlichen Workarounds; nutze saubere Async-/Dispatcher-Muster.

---

## 6) Testregeln

Tests sind Pflicht für jede relevante Änderung an Core-Logik, IO-Verhalten, Safety, Reports oder UI-naher Ablaufsteuerung.

### Erforderliche Testarten
- **Unit Tests**
  - RegionTag
  - GameKey
  - VersionScore
  - FormatScore
  - Winner Selection
  - Sanitizer
  - Path Guards
  - Escaping / Encoding
  - Policy-Entscheidungen

- **Integration Tests**
  - TempDirs
  - Move → Trash
  - Report-Erzeugung
  - ZIP/7Z Handling
  - DAT Index / Matching
  - Tool Integration mit kontrollierten Testdoubles oder sicheren Fixtures

- **Regression Tests**
  - Reale Bugfälle
  - Früher fehlerhafte Dateinamen
  - Problematische Regionen/Tags
  - Archiv-Sonderfälle
  - Preview/Run-Paritätsprobleme

- **Negative / Edge Tests**
  - beschädigte Archive
  - fehlende Tracks
  - leere Dateien
  - ungültige Namen
  - Unicode / Sonderzeichen
  - Path Traversal
  - Reparse Points
  - doppelte Inputs
  - fehlende Metadaten

- **Property / Fuzz Tests**, wo sinnvoll
  - zufällige Filenamen
  - Tag-Kombinationen
  - Parser-Robustheit
  - Invarianten der Gruppierung und Gewinnerauswahl

### Test-Invarianten
Tests müssen explizit prüfen, dass:
- nie außerhalb erlaubter Roots geschrieben/verschoben wird
- keine leeren oder inkonsistenten Gruppierungs-Keys entstehen
- Winner Selection deterministisch bleibt
- Preview, Execute und Reports konsistente Zahlen und Entscheidungen liefern
- Fehler sauber signalisiert und nicht still geschluckt werden

---

## 7) Refactoring-Regeln

- Refactoring nur mit klarem Ziel, Nutzen und Verifikation.
- Kein “grosses Umschreiben”, wenn ein gezielter Fix sicherer ist.
- Grössere Umbauten nur schrittweise.
- Bestehende Feature-Parität muss abgesichert werden.
- Jede grössere Umstrukturierung braucht mindestens:
  - Zielbild
  - Risikoabschätzung
  - betroffene Komponenten
  - Testplan
  - konzeptionelle Rollback-Strategie

---

## 8) Regeln für generierten Code

Wenn du Code erzeugst oder änderst:

- Liefere **konkreten, kompilierbaren, konsistenten Code**.
- Verwende bestehende Patterns des Projekts, sofern sie nicht nachweislich fehlerhaft sind.
- Ändere nicht unnötig Stil, Namenskonventionen oder Architektur.
- Erfinde keine APIs, Klassen oder Properties, die im Projektkontext nicht plausibel sind.
- Hinterlasse keine unverbundenen Fragmente.
- Zeige klar:
  - welche Dateien neu sind
  - welche Dateien geändert werden
  - was fachlich geändert wurde
  - welche Tests ergänzt oder angepasst werden müssen

Wenn eine Änderung mehrere Dateien betrifft, arbeite vollständig und konsistent über alle betroffenen Schichten hinweg statt nur einen Teil anzudeuten.

---

## 9) Regeln für Code Reviews und Analysen

Wenn du reviewst, audittest oder Probleme analysierst, priorisiere nach Schweregrad:

### Priorität 1 – Release-Blocker
- Datenverlust
- Security-Lücken
- falsche Winner-Auswahl
- falsches Grouping / Sorting
- Preview/Execute/Report-Divergenz
- UI-Fehlverhalten mit Fehlbedienungsrisiko
- Abstürze / Hänger / Deadlocks / nicht abbrechbare Läufe

### Priorität 2 – Hohe Risiken
- schlechte Testbarkeit
- doppelte Logik
- versteckte Seiteneffekte
- fragile Tool-Integration
- inkonsistente Reports
- schlecht verständliche UX in kritischen Abläufen

### Priorität 3 – Wartbarkeit / Qualität
- unnötige Komplexität
- Naming-Probleme
- Strukturprobleme
- geringe Erweiterbarkeit
- visuelle Unruhe
- technische Schulden

### Review-Ausgabeformat
Jedes Finding soll möglichst dieses Format haben:

- **Titel**
- **Schweregrad**
- **Impact**
- **Betroffene Datei(en) / Komponente(n)**
- **Reproduktion oder Beispiel**
- **Ursache**
- **Fix-Strategie**
- **Erforderliche Testabsicherung**

Wenn keine Probleme gefunden werden, nicht nur pauschal “sieht gut aus” sagen, sondern kurz benennen, welche risikoreichen Bereiche geprüft wurden und warum sie aktuell tragfähig wirken.

---

## 10) Regeln für Antworten bei grösseren Aufgaben

Bei nicht-trivialen Aufgaben muss die Antwort strukturiert sein und enthalten:

1. **Kurzfazit**
2. **Risiken / Blocker**
3. **konkrete Änderungen**
4. **betroffene Dateien**
5. **Testauswirkungen**
6. **offene Punkte / Annahmen**
7. **aktualisierte Tracking Checklist**, falls relevant

---

## 11) Tracking Checklist Pflicht bei grösserer Arbeit

Wenn die Aufgabe mehr als trivial ist, erzeuge oder aktualisiere am Ende eine Markdown-Checklist.

```markdown
## Tracking Checklist (RomCleanup)

### Release-Blocker
- [ ] …

### GUI/UX (WPF/XAML)
- [ ] IA/Navigation klar
- [ ] Spacing/Overlaps/Resize sauber
- [ ] Wizard/Flow: Roots → Optionen → Preview → Confirm → Run → Report/Undo
- [ ] Phase/Progress/Cancel sauber
- [ ] Retro-modern Theme lesbar (ResourceDictionary)

### Core/Engine
- [ ] Duplikate entfernt / Helpers vereinheitlicht
- [ ] Pure functions testbar
- [ ] Determinismus geprüft
- [ ] Preview/Execute/Report-Parität geprüft

### IO/Safety
- [ ] Path traversal / Zip-slip / Reparse Points abgesichert
- [ ] Trash/Audit/Undo konsistent
- [ ] Externe Tool-Aufrufe robust abgesichert

### Performance
- [ ] Enumeration/Hashing/Regex Hotspots geprüft
- [ ] UI bleibt responsiv
- [ ] Progress/Cancel robust

### Tests (keine Alibi-Tests)
- [ ] Unit
- [ ] Integration
- [ ] Regression
- [ ] Negative/Edge/Fuzz

### Backlog
- [ ] …