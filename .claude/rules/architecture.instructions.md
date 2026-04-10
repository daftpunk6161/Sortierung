# Romulus – Architekturregeln

## Ziel
Architekturelle Aenderungen muessen die Kernregel respektieren:
**Stabilitaet, Determinismus und Testbarkeit vor Komfort oder Abstraktionslust.**

## Dependency-Richtung
**Entry Points -> Infrastructure -> Core -> Contracts**

Nie umkehren.

## Schichtregeln

### Contracts
- Interfaces
- Models
- Fehlervertraege
- keine I/O-Logik
- keine WPF-/ASP.NET-/Dateisystemdetails

### Core
- pure Domaenenlogik
- keine I/O-Abhaengigkeiten
- keine direkten Tool-/Process-/FileSystem-Zugriffe
- keine Environment-Abhaengigkeiten
- keine GUI-/CLI-/API-Sonderlogik

### Infrastructure
- Adapter fuer FileSystem, Tools, DAT, Reports, Hashing, Logging, Orchestration
- kapselt harte Umgebungsabhaengigkeiten
- keine GUI-Logik

### Entry Points
- GUI, CLI, API
- Komposition, Benutzerinteraktion, Mapping, Start/Stop
- keine fachliche Parallelwahrheit
- keine geschaeftslogische Sonderberechnung

## Pflichtregeln
- Keine I/O-Logik in `Romulus.Core`
- Keine Businesslogik in WPF Code-Behind
- Services per Constructor Injection
- Zeit, Prozesse, Dateisystem und externe Tools hinter testbaren Abstraktionen
- Keine fachliche Sonderlogik in Program.cs, ViewModels oder API-Endpunkten, wenn sie zentral gehoert

## Single Source of Truth
Diese Bereiche duerfen nicht mehrfach parallel modelliert werden:
- Run-Ergebnis
- KPI-/Projection-Logik
- Sort-/Review-/Blocked-Entscheidungen
- Statusmodell
- Preview vs Execute vs Report Entscheidungen

## Architektur-Hygiene
Aktiv achten auf:
- halbfertige Refactors
- doppelte Services
- konkurrierende Projection-/Result-Modelle
- Gott-Klassen
- ueberladene ViewModels
- unnoetige statische Utility-Sammlungen
- Altpfade neben neuer Architektur

## Erlaubte Refactors
Refactors sind nur sinnvoll, wenn sie mindestens einen klaren Nutzen bringen:
- weniger Duplikation
- bessere Testbarkeit
- klarere Verantwortlichkeiten
- deterministischere Logik
- weniger Schattenlogik
- bessere Release-Sicherheit

Keine Refactors nur aus Stilgruenden.
