---
applyTo: "**"
---

# Romulus – Testregeln

## Ziel
Tests muessen reale Fehler finden koennen (logische Bugs im Code, Verletzungen domaenenspezifischer Regeln, Regressionen oder fehlerhaftes Laufzeitverhalten).

## Pflicht-Testarten
- Unit
- Integration
- Regression
- Negative / Edge

## Wann Tests Pflicht sind
Bei Aenderungen an:
- Core
- Safety
- Orchestration
- Conversion
- Sorting
- Reports
- DAT
- API-Output
- GUI-State
- Status-/Projection-/Result-Logik

## Erwartung
- bestehende Tests aktualisieren, wenn Verhalten bewusst geaendert wird
- neue Tests ergaenzen, wenn neue Logik entsteht
- Regressionstests ergaenzen, wenn Bugs gefixt werden
- Invariantentests ergaenzen, wenn zentrale Domaenenregeln betroffen sind

## Kritische Invarianten
Tests muessen absichern, dass:
- kein Move ausserhalb erlaubter Roots moeglich ist
- keine leeren oder inkonsistenten Keys entstehen
- Winner-Selection deterministisch bleibt
- Preview / Execute / Report konsistent sind
- GUI / CLI / API / Reports dieselbe fachliche Wahrheit abbilden
- fehlerhafte Archive und Sonderfaelle sauber behandelt werden

## Verbotene Testmuster
- no-crash-only Tests
- tautologische Assertions
- Pseudo-Abdeckung ohne echte Aussage
- Tests, die nur Oberflaeche (UI- oder API-Layer) beruehren, aber keine echte Fachregel (domaenenspezifische Geschaeftsregel) pruefen

## Test-Hygiene
Aktiv bereinigen:
- doppelte Tests
- veraltete Tests
- irrefuehrende Testnamen
- tote Fixtures
- zu komplexe Tests ohne Schutzwert
