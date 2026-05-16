---
applyTo: "**"
---

# Romulus – GUI / WPF Regeln

## Zielbild
Die GUI muss:
- verstaendlich (klare Beschriftungen, intuitive Navigation, eindeutige Begriffe)
- luftig (ausreichend Abstand zwischen Elementen, keine ueberladenen Cluster)
- robust
- fehlbedienungssicher (riskante Aktionen brauchen Bestaetigung und sind nicht versehentlich ausloesbar)
- nicht ueberladen
sein.

## Standardablauf
**DryRun / Preview -> Summary -> Bestaetigung -> Apply / Move -> Report / Undo**

Davon nicht still abweichen.

## Pflichtregeln
- Keine Businesslogik im Code-Behind
- Lange Operationen nicht auf dem UI-Thread
- UI-Updates sauber ueber Dispatcher / async
- Keine DoEvents-aehnlichen Muster
- Styles, Farben und Spacing zentral in `ResourceDictionary`
- Zwei Bedienmodi ermoeglichen:
  - einfach (versteckt fortgeschrittene Optionen, zeigt nur den Standardablauf und die wichtigsten Aktionen)
  - experte (zeigt alle Konfigurations-, Diagnose- und Power-User-Optionen)

## Projection-driven UI
UI-Zustaende, KPI-Anzeigen, Run-Status und Summary-Werte sollen aus klaren ViewModels / Projections kommen.
Nicht an mehreren Stellen neu herleiten.

## Danger Actions
- keine unklaren Danger Actions
- riskante Aktionen brauchen Summary und sichtbare Bestaetigung
- Move / Convert / Repair / Rollback muessen fehlbedienungssicher bleiben

## Verboten
- Businesslogik im Code-Behind
- fachliche Regeln in ValueConverters
- konkurrierende GUI-Sonderlogik neben zentralen Services
- ueberladene Screens ohne Priorisierung
- “Coming Soon” oder “nicht implementiert” als sichtbare Standardfunktion ohne klaren Plan
