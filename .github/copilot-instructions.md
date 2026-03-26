# Romulus – Copilot Instructions

## Zweck

Romulus (intern: RomCleanup) ist ein produktionsnahes C# .NET 10 Tool zur Verwaltung und Bereinigung von ROM-Sammlungen mit drei Entry Points:

- **GUI**: WPF/XAML
- **CLI**: headless / automation / CI
- **REST API**: ASP.NET Core Minimal API

Kernfunktionen:
- regionsbasierte Deduplizierung
- Junk-Erkennung (Demo/Beta/Homebrew/Hack etc.)
- Formatkonvertierung
- DAT-basierte Verifizierung
- konsolenbezogene Klassifikation und Sortierung

Aktive Entwicklung erfolgt ausschließlich in `src/`.  
`archive/powershell/` ist nur Legacy-Referenz und darf nicht als aktive Implementierungsbasis behandelt werden.

---

## Nicht verhandelbare Regeln

### 1) Release-Fähigkeit geht vor
Bevorzuge immer:
- Korrektheit
- Determinismus
- Sicherheit
- Testbarkeit
- Wartbarkeit

gegenüber:
- Feature-Hype
- unnötiger Abstraktion
- kosmetischem Refactoring
- UI-Spielerei

### 2) Kein Datenverlust
- Standardverhalten ist **Move to Trash / Audit / Undo-fähiges Verhalten**
- Kein direktes Löschen ohne explizite, klar abgesicherte Benutzerentscheidung
- Riskante Operationen brauchen Summary, Schutzmechanismus und Audit

### 3) Determinismus ist Pflicht
Gleiche Inputs müssen gleiche Outputs erzeugen.  
Das gilt insbesondere für:
- GameKey-Bildung
- Region-Erkennung
- Score-Berechnung
- Winner-Selection
- Preview vs Execute
- GUI / CLI / API / Report-Parität

### 4) Keine doppelte Logik
- Geschäftslogik darf nicht in mehreren Entry Points separat nachgebaut werden
- Gemeinsame Regeln gehören in Core oder klar definierte Services
- Duplikate aktiv vermeiden und bei Gelegenheit abbauen

### 5) Keine halben Lösungen
Erzeuge keinen:
- Pseudocode
- Platzhaltercode
- TODO-only-Code
- Scheincode ohne echte Integration
- erfundene APIs oder Klassen ohne klare Projektpassung

Wenn Code erwartet ist, liefere **kompilierbaren, konsistenten und integrierbaren Code**.

---

## Architekturregeln

Dependency-Richtung:

**Entry Points → Infrastructure → Core → Contracts**

Nie umgekehrt.

### Schichten
- **Contracts**: Interfaces, Models, Fehlerverträge
- **Core**: pure Domänenlogik, keine I/O-Abhängigkeiten
- **Infrastructure**: Datei-, Tool-, Report-, Hash-, DAT-, Logging- und Orchestrierungsadapter
- **CLI / API / GUI**: Entry Points, Komposition, Benutzerinteraktion

### Pflichtregeln
- Keine I/O-Logik in `RomCleanup.Core`
- Keine Businesslogik in WPF Code-Behind
- MVVM in der GUI einhalten
- Services per Constructor Injection
- Harte Umgebungsabhängigkeiten kapseln
- Zeit, Prozesse, Dateisystem, externe Tools und Umgebungswerte hinter testbaren Abstraktionen halten

---

## GUI / WPF Regeln

Die GUI muss:
- verständlich
- luftig
- robust
- nicht überladen
- fehlbedienungssicher

sein.

### Pflicht
- Standardablauf ist:
  **DryRun / Preview → Summary → Bestätigung → Apply / Move → Report / Undo**
- Lange Operationen nicht auf dem UI-Thread ausführen
- UI-Updates sauber über Dispatcher / async Patterns
- Kein `DoEvents`-ähnliches Muster
- Styles, Farben und Spacing zentral in `ResourceDictionary`
- Zwei Bedienmodi unterstützen:
  - einfach
  - experte

### Verboten
- Businesslogik im Code-Behind
- unklare Danger Actions
- überladene Screens ohne klare Priorisierung
- unkontrollierte Converter-Logik für fachliche Regeln

---

## Sicherheitsregeln

Diese Regeln sind zwingend:

- **Path Traversal blockieren**
  - vor Move/Copy/Delete immer Root-validierte Pfadauflösung verwenden

- **Zip-Slip blockieren**
  - Archivpfade vor Extraktion validieren

- **Reparse Points nicht transparent folgen**
  - explizit blockieren oder sicher definieren

- **CSV-Injection verhindern**
  - keine ungesicherten Formel-Präfixe in Exportfeldern

- **HTML-Encoding konsequent anwenden**
  - alle HTML-Reports müssen sauber escapen

- **Externe Tools absichern**
  - Tool-Hash-Verifizierung
  - korrektes Argument-Quoting
  - Exit-Code-Prüfung
  - Timeout / Retry / Cleanup

---

## Kernlogik-Regeln

### GameKey / Region / Winner Selection
Diese Bereiche sind besonders kritisch und dürfen nicht still verändert werden:

- `GameKeyNormalizer`
- Region-Erkennung
- `FormatScore`
- `VersionScore`
- `DeduplicationEngine.SelectWinner`

Änderungen in diesen Bereichen müssen:
- deterministisch bleiben
- bestehende Invarianten respektieren
- durch gezielte Tests abgesichert werden

### Preview / Run / Report Parität
Bei jeder Änderung, die Runs oder Ergebnisse beeinflusst, muss sichergestellt werden:

- Preview zeigt dieselben fachlichen Entscheidungen wie Execute
- Reports zählen dieselben Entscheidungen korrekt
- GUI, CLI und API widersprechen sich nicht

---

## Tests

Jede relevante Änderung an Core, Safety, Run-Verhalten, Reports, DAT oder Toolintegration braucht passende Tests.

### Pflicht-Testarten
- Unit
- Integration
- Regression
- Negative / Edge

### Kritische Invarianten
Tests müssen prüfen, dass:
- kein Move außerhalb erlaubter Roots möglich ist
- keine leeren oder inkonsistenten Keys entstehen
- Winner-Selection deterministisch bleibt
- Preview / Execute / Report konsistent sind
- fehlerhafte Archive und Sonderfälle sauber behandelt werden

### Keine Alibi-Tests
Ein Test ist nur wertvoll, wenn er einen realen Fehler finden kann.

---

## Arbeitsweise bei Änderungen

Wenn du Änderungen vorschlägst oder erzeugst:

1. ändere nur die wirklich betroffenen Teile
2. halte Architekturgrenzen ein
3. benenne betroffene Dateien klar
4. erkläre funktionale Auswirkungen
5. ergänze oder aktualisiere passende Tests
6. vermeide unnötige Stil- oder Strukturänderungen

Wenn eine Änderung mehrere Schichten betrifft, liefere die Änderung konsistent über alle nötigen Dateien hinweg statt nur Teilfragmente.

---

## Arbeitsweise bei Reviews / Analysen

Wenn du Code analysierst oder reviewst, priorisiere in dieser Reihenfolge:

### Priorität 1 – Release-Blocker
- Datenverlust
- Security-Probleme
- falsche Winner-Selection
- falsches Grouping / Scoring
- Preview / Execute / Report Divergenz
- GUI-Fehlverhalten mit Fehlbedienungsrisiko
- Deadlocks, Hänger, nicht abbrechbare Prozesse

### Priorität 2 – Hohe Risiken
- doppelte Logik
- fragile Tool-Integration
- schlechte Testbarkeit
- versteckte Seiteneffekte
- inkonsistente Fehlerbehandlung

### Priorität 3 – Wartbarkeit
- unnötige Komplexität
- Naming- oder Strukturprobleme
- Erweiterbarkeitsprobleme
- UI-Unklarheiten ohne unmittelbares Fehlverhalten

### Review-Format
Wenn du Findings lieferst, verwende wenn möglich dieses Format:

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
3. konkrete Änderungen
4. betroffene Dateien
5. Testbedarf
6. offene Annahmen

Wenn Code erzeugt wird, liefere vollständige, zusammenhängende Änderungen statt lose Fragmente.

---

## Wichtige Projektfakten

### Plattform
- Windows 10/11
- .NET 10
- `net10.0`
- `net10.0-windows` für WPF
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
- `%APPDATA%\RomCleanupRegionDedupe\settings.json`

### Wichtige Entry Points
- `src/RomCleanup.CLI`
- `src/RomCleanup.Api`
- `src/RomCleanup.UI.Wpf`

### Wichtige Kernbereiche
- `RomCleanup.Contracts` (Interfaces, Models, Fehlerverträge)
- `RomCleanup.Core/GameKeys`
- `RomCleanup.Core/Regions`
- `RomCleanup.Core/Scoring`
- `RomCleanup.Core/Deduplication`
- `RomCleanup.Core/Classification`
- `RomCleanup.Core/Conversion`
- `RomCleanup.Infrastructure/Orchestration`
- `RomCleanup.Infrastructure/FileSystem`
- `RomCleanup.Infrastructure/Tools`
- `RomCleanup.Infrastructure/Reporting`
- `RomCleanup.Infrastructure/Dat`
- `RomCleanup.Infrastructure/Conversion`
- `RomCleanup.Infrastructure/Hashing`
- `RomCleanup.Infrastructure/Safety`
- `RomCleanup.Infrastructure/Sorting`

---

## Was du vermeiden musst

- keine stillen Verhaltensänderungen
- keine Logikduplikation zwischen GUI, CLI und API
- keine Businesslogik im WPF Code-Behind
- keine Umgehung von Safety-Checks
- keine Refactors ohne Verifikation
- keine kosmetischen Änderungen, wenn funktionale Risiken offen sind
- keine Annahmen, die Projektstruktur oder bestehende Verträge verletzen