---
applyTo: "src/**,docs/**,.github/**,.claude/**,scripts/**"
---

# Romulus – Cleanup- und Hygiene-Regeln

## Ziel
Cleanup bedeutet hier:
- tote Pfade entfernen
- verwaiste Referenzen bereinigen
- Duplikate abbauen
- halbfertige Refactors abschliessen
- die Codebasis release-sauberer machen, d. h. konkret: kein ungenutzter Code, keine verwaisten Referenzen, gruener Build und gruene Tests

Nicht gemeint sind:
- kosmetische Umbenennungen ohne Nutzen
- grosse Strukturumbauten ohne klaren Gewinn

## Aktiv suchen nach
- totem Code
- verwaisten Registrierungen
- verwaisten i18n-Keys
- verwaisten Tool-Keys
- verwaisten Commands
- ungenutzten Services / DTOs / Models
- leeren oder veralteten Verzeichnissen
- doppelten Handlern
- Copy-Paste-Logik
- doppelten Policies / Mappings / Projections
- halbfertigen Refactors
- Legacy-Resten
- Magic Strings / Magic Numbers an kritischen Stellen

## Pflichtregeln
- Entfernen heisst vollstaendig entfernen
- Wenn etwas entfernt wird, alle Referenzen mitbereinigen
- Keine halben Loeschungen
- Keine toten Registrierungen stehen lassen
- Keine Altpfade neben neuen Pfaden stehen lassen, wenn sie nur Verwirrung stiften

## Cleanup-Prioritaet
Bei Konflikten zwischen mehreren Prioritaeten gilt: in der genannten Reihenfolge abarbeiten. Hoehere Nummer wartet, bis niedrigere Nummer im aktuellen Scope adressiert ist.

1. release-kritische Altlasten
2. Schattenlogik
3. verwaiste Registrierungen / Commands / Keys
4. Status-/Result-/Projection-Duplikate
5. schwache oder tote Tests
6. restliche Wartbarkeit

## Verification
Nach Cleanup muss gelten:
- Build bleibt gruen
- Tests bleiben gruen
- keine sichtbaren UI-Regressionen und keine funktionalen Regressionen in End-User-Workflows (GUI-Aktionen, CLI-Kommandos, API-Endpunkte, Reports)
- keine neuen Schattenpfade
- keine verwaisten Referenzen
