---
applyTo: "src/Romulus.Core/Conversion/**,src/Romulus.Infrastructure/Conversion/**,src/Romulus.Tests/**Conversion**"
---

# Romulus – Conversion-Regeln

## Grundsatz
Conversion darf niemals auf Kosten von Datenintegritaet oder Nachvollziehbarkeit aggressiv werden. Aggressiv heisst hier konkret: Verify-Schritte ueberspringen, Source-Dateien vor erfolgreicher Verifikation entfernen, irreversible Re-Encodierung ohne Bestaetigung, oder stille Auto-Konvertierungen mit Datenverlust.

## Pflichtregeln
Reihenfolge bei Konflikten: Datenintegritaet (1) vor Tool-Sicherheit (2) vor Konsistenz (3).

### 1) Datenintegritaet
- Source-Dateien nie vor erfolgreicher Verifikation entfernen
- partielle Outputs bei Fehlern sauber behandeln
- keine riskanten stillen Auto-Konvertierungen (z. B. Datenverlust durch Format-Downgrades, irreversible Re-Encodierung, oder Konvertierungen ohne Verify-Schritt)
- Set-Integritaet respektieren

### 2) Tool-Sicherheit
- Tool-Hash-Verifizierung
- korrektes Argument-Quoting
- Exit-Code-Pruefung
- Timeout / Retry / Cleanup
- Tool-Ausgabe nicht blind vertrauen
- Output validieren, wenn Folgeentscheidungen davon abhaengen

### 3) Konsistenz
- Preview und Execute muessen dieselbe fachliche Entscheidung zeigen

## Architektur
- Conversion-Regeln nicht parallel in GUI, CLI und API modellieren
- Policies, Prioritaeten und Verify-Regeln zentral halten
- keine lokalen Sonderpfade, wenn es bereits eine zentrale Conversion-Logik gibt

## Tests
Aenderungen an Conversion brauchen:
- Unit-Tests
- Regressionstests
- Negative / Edge-Tests
- Invarianten fuer Verify / Cleanup / deterministisches Verhalten
