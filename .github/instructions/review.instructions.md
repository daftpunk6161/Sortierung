---
applyTo: "src/**,docs/**"
---

# Romulus – Review-Regeln

## Ziel
Reviews sollen Release-Risiken frueh finden und sauber priorisieren.

## Priorisierung

### Prioritaet 1 – Release-Blocker
- Datenverlust
- Security-Probleme
- falsche Winner-Selection
- falsches Grouping / Scoring
- Preview / Execute / Report Divergenz
- GUI-Fehlverhalten mit Fehlbedienungsrisiko (fuehrt zu falschen Aktionen oder Datenverlust)
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
- UI-Unklarheiten ohne unmittelbares Fehlverhalten (geringe Usability-Probleme ohne Risiko falscher Aktionen)
- Hygiene-Probleme und tote Pfade

## Review-Format
Wenn moeglich Findings so liefern:
- **Titel**
- **Schweregrad**
- **Impact**
- **Betroffene Datei(en)**
- **Beispiel / Reproduktion**
- **Ursache**
- **Fix**
- **Testabsicherung**

## Review-Hinweise
- Keine Schonung bei Release-Risiken
- Keine rein aesthetischen, nicht-funktionalen Nebenpunkte (z. B. Naming, Formatierung, Layout) vor Korrektheitsproblemen priorisieren
- Fehlende Testbarkeit ist selbst ein Risiko
- Fehlende Invarianten sind selbst ein Risiko
- Schattenlogik immer benennen
