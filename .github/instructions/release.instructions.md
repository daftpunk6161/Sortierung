---
applyTo: "src/**,deploy/**,.github/workflows/**"
---

# Romulus – Release-Regeln

## Oberregel
Release-Faehigkeit geht vor (Release-Faehigkeit bedeutet: Stabilitaet, Korrektheit, Sicherheit und Deployment-Reife der naechsten Auslieferung).

Bevorzuge (gleichwertig, ohne strikte Reihenfolge):
- Korrektheit
- Determinismus
- Sicherheit
- Testbarkeit
- Wartbarkeit

gegenueber:
- Feature-Hype
- kosmetischem Refactoring
- vorschnellen Erweiterungen
- fragwuerdiger UI-Spielerei

## Harte Release-Blocker
Als Blocker behandeln:
- Datenverlust
- Security-Probleme
- falsche Winner-Selection
- falsches Grouping / Scoring
- Preview / Execute / Report Divergenz
- GUI-Fehlverhalten mit Fehlbedienungsrisiko
- Deadlocks / Haenger / nicht abbrechbare Prozesse
- konkurrierende Wahrheiten zwischen GUI / CLI / API / Reports

## Vor Release bereinigen
- halbfertige Refactors
- tote Legacy-Reste
- doppelte Result-/Projection-Logik
- verwaiste Registrierungen und Keys
- fragile Error-/Statuspfade
- wertlose Tests
- irrefuehrende UI-Features

## Was nicht kurz vor Release passieren soll
- grosse, unnoetige Architektur-Umbauten
- kosmetische Massenrefactors
- neue experimentelle Features
- neue Schattenlogik
- unfertige UI-Karten / Stubs / Blendwerk

## Release-Kriterien
Ein Zustand ist erst release-tauglich, wenn:
- Build sauber ist
- Tests sauber sind
- Kerninvarianten abgesichert sind
- Preview / Execute / Report konsistent sind
- GUI / CLI / API dieselbe fachliche Wahrheit nutzen
- bekannte release-kritische Hygiene-Schulden nicht mehr offen sind
