# ADR-0025: DatAuditStatus-Granularitaet — Splitting von Unknown und Miss

## Status
Proposed (deferred)

## Datum
2026-05-05

## Owner
daftpunk6161 (Repo-Maintainer)

## Bezug
- Vorgaenger-Diskussion: Critic-Session 2026-05-05 (DAT-Audit-Spalten-Semantik)
- Bezug-ADR: ADR-0021 (DAT-First Conservative Recognition Architecture), ADR-0023 (DAT-First Policy)
- Begleit-ADR: ADR-0026 (Workflow vs Run-Mode Trennung)
- Voraussetzung: Mini-P1 (Inspector-Tooltip mit MatchEvidence.Reasoning) muss ausgeliefert sein und das UX-Hauptproblem nachweislich nicht beheben.
- AGENTS.md: "Eine fachliche Wahrheit", "Determinismus ist Pflicht", "Keine halben Loesungen".

## Kontext

`Romulus.Contracts.DatAuditStatus` hat heute fuenf Werte:

| Status | Bedeutung |
| --- | --- |
| `Have` | Hash + Name passen zur DAT |
| `HaveWrongName` | Hash passt, Datei-Name weicht ab |
| `HaveByName` | Nur Name passt (kein Hash-Match) — Tier-3-Fallback |
| `Miss` | DAT fuer die Konsole geladen, Hash nicht enthalten |
| `Unknown` | Konsole nicht erkannt ODER keine DAT fuer die Konsole geladen |
| `Ambiguous` | Cross-Console-Hash trifft mehrere DATs ohne Aufloesung |

Im realen GUI-Lauf zeigt sich, dass `Miss` und `Unknown` jeweils mehrere strukturell unterschiedliche Ursachen buendeln:

**`Miss` umfasst heute:**
- modifizierte/komprimierte Datei (z. B. CSO komprimiert eine ISO, Redump indexiert ISO-Hash, nicht CSO-Hash)
- unbekannter Dump (Beta, Prototyp, Hack ohne DAT-Eintrag)
- Region-Variante (Game-Name in DAT vorhanden, dieser konkrete Hash aber nicht)

**`Unknown` umfasst heute:**
- Konsole nicht detektierbar (z. B. nackte `.bin` ohne Folder-/Header-Evidenz)
- Konsole klar, aber fuer diese Konsole keine DAT geladen

Diese Unterschiede erfordern **komplett verschiedene User-Aktionen** (DAT downloaden vs. Datei umbenennen vs. unkomprimieren). Heute sieht der User aber nur einen Sammel-Status.

## Entscheidung (deferred)

Diese ADR wird nicht jetzt implementiert. Sie haelt den Vorschlag fest, falls der Mini-P1-Inspector-Tooltip das UX-Problem nicht ausreichend loest.

### Geplante Aenderung

`DatAuditStatus` waere zu erweitern um:

- `Unknown` aufgespalten in:
  - `UnknownNoConsole` — Konsole nicht detektierbar
  - `UnknownNoDat` — Konsole erkannt, keine DAT fuer Konsole geladen
- `Miss` ggf. aufgespalten in:
  - `MissUnknownDump` — Hash nicht in DAT, kein weiterer Hinweis (Default)
  - `MissModifiedFormat` — nur erlaubt, wenn ein **echter** Format-Detector belegen kann, dass die Datei eine komprimierte/transformierte Variante eines bekannten Originals ist (z. B. CSO-Decompression + Hash der entstandenen ISO matcht)
  - `MissNameOnly` — Game-Name in DAT vorhanden, dieser Hash aber nicht (Region-Variante-Indikator)

### Harte Bedingungen (Critic-Vorgabe)

1. **Keine Schein-Praezision.** Jeder neue Sub-Status MUSS aus einer deterministischen Detector-Quelle stammen. Ein Sub-Status, der nur auf Heuristik („Endung deutet auf Komprimierung hin") basiert, ist eine Luegen-Label und wird abgelehnt.
2. **`MissModifiedFormat` darf nur eingefuehrt werden, wenn vorher eine echte Format-Detection-Phase implementiert ist** (eigene ADR + eigene Tests). Sonst bleibt es bei `MissUnknownDump`.
3. Die Default-Bedeutung von `Miss` aendert sich nicht; alte Reports/Audits muessen weiterlesbar bleiben.
4. Der Wechsel ist **breaking** fuer alle Konsumenten von `DatAuditStatus`. Migration ist Pflicht-Bestandteil.

### Reichweite (geschaetzt, nicht verbindlich)

| Schicht | Aenderung |
| --- | --- |
| Contracts | Enum-Erweiterung, Validator-Update |
| Core | `DatAuditClassifier` muss Sub-Status-Quellen deterministisch belegen |
| Infrastructure | `DatAuditPipelinePhase` muss neue Quellen liefern; ggf. `IFormatDetector`-Port |
| Reports | HTML/CSV/JSON/XML/EmulationStation/LaunchBox-Mapping erweitern |
| WPF | i18n-Keys, Status-Badges, Filter-Werte, Inspector-Texte |
| CLI | Subcommand-Output bleibt parity zu GUI |
| API | Endpoint-Schema versioniert anpassen |
| Tests | Pin-Tests in 4 Test-Projekten, Invarianten fuer `RunResultValidator` |
| Settings/Persistence | Migration des persistierten Run-Result-Caches |

## Konsequenzen

### Wenn implementiert
- Plus: User sieht echten Handlungsbedarf pro Datei.
- Plus: Reports koennen praezisere Statistik liefern.
- Minus: Erheblicher Multi-Schicht-Eingriff. Eine eigene Wave noetig.
- Minus: Bei verfruehter Einfuehrung ohne Format-Detector entsteht Schein-Praezision (verletzt Identitaets-Guardrail).

### Wenn nicht implementiert
- Mini-P1-Inspector-Tooltip uebernimmt die Erklaerungs-Last.
- `Miss`/`Unknown` bleiben grobe Sammel-Status, der User muss pro Zeile in den Inspector schauen.

## Aktivierungsbedingungen

Diese ADR wechselt erst auf `Accepted`, wenn **alle** Bedingungen erfuellt sind:

1. Mini-P1-Inspector-Tooltip ist live und mindestens 4 Wochen produktiv genutzt.
2. Konkreter Maintainer-Bericht dokumentiert reale Faelle, in denen der Tooltip das Verstaendnisproblem nicht loest.
3. Identitaets-Frage (siehe AGENTS.md "Identitaets-Guardrail") wird mit klarem Ja beantwortet.
4. Format-Detection-Strategie ist geklaert (entweder `MissModifiedFormat` ist mit echtem Detector belegt oder es entfaellt aus dem Scope).
5. Eigener Wave-Eintrag in `docs/plan/strategic-reduction-2026/plan.yaml` mit eigenem Owner.

## Status-Verweis

Solange Aktivierungsbedingungen nicht erfuellt sind, ist diese ADR **deferred**. Code-Aenderungen, die `DatAuditStatus` erweitern, sind ohne Status-Wechsel auf `Accepted` ein Review-Block.
