# ADR-0026: Workflow vs Run-Mode Trennung — Audit-Profile vs Action-Workflows

## Status
Proposed (deferred)

## Datum
2026-05-05

## Owner
daftpunk6161 (Repo-Maintainer)

## Bezug
- Vorgaenger-Diskussion: Critic-Session 2026-05-05 (Workflow-Selector zeigt DAT-Verification ohne Apply-Pfad)
- Bezug-ADR: ADR-0006 (GUI/UI/UX Target-Architecture), ADR-0021 (DAT-First), ADR-0023 (DAT-First Policy)
- Begleit-ADR: ADR-0025 (DatAuditStatus-Granularitaet)
- Voraussetzung: Mini-P1 (Audit-Only-Banner + persistente Next-Action-Zone) muss ausgeliefert sein und das UX-Hauptproblem nachweislich nicht beheben.
- AGENTS.md: "Eine fachliche Wahrheit", "Keine doppelte Logik", "Keine halben Loesungen", Identitaets-Guardrail.

## Kontext

Der GUI-Selector "Workflow" mischt heute zwei semantisch unterschiedliche Konzepte:

- **Audit-Profile** (read-only): DAT Verification, Inbox Check, Safety Audit. Diese fuehren keinen Move/Convert/Sort durch — sie liefern nur Reports.
- **Action-Workflows** (Preview -> Apply): Sort/Cleanup. Diese haben einen klaren Pfad `DryRun -> Summary -> Bestaetigung -> Move/Convert -> Report`.

Der Nutzer kann das visuell nicht unterscheiden. Beobachtetes Symptom: nach einem DAT-Verification-Run sucht der Nutzer einen "scharfen Lauf", der per Definition nicht existiert.

Gleichzeitig sind im Code bereits beide Pfade implementiert:

- `MainViewModel.RunCommand` + `StartMoveCommand` decken den Action-Workflow ab (mit `RunState.CompletedDryRun -> ShowStartMoveButton`).
- DAT-Verification ist read-only und endet ohne Folgeschritt.

Der Mismatch ist also **rein UX-seitig**, nicht logisch. Das ADR-0006 dokumentiert den Action-Workflow, aber **nicht** die Trennung Audit-Profile vs Action-Workflows.

## Entscheidung (deferred)

Diese ADR wird nicht jetzt implementiert. Sie haelt den Vorschlag fest, falls Mini-P1 (Banner + Next-Action-Zone) das UX-Problem nicht ausreichend loest.

### Geplante Aenderung

#### Konzeptionelle Trennung
- **Audit-Profile** (read-only Klasse): DAT Verification, Inbox Check, Safety Audit. Werden im Selector mit eigenem visuellen Marker (z. B. `Audit (read-only)`-Gruppe) gefuehrt. Apply-Buttons sind ausgeblendet, nicht nur disabled.
- **Action-Workflows** (Preview -> Apply Klasse): Sort/Cleanup. Werden im Selector mit eigenem visuellen Marker (z. B. `Cleanup (mit Apply)`-Gruppe) gefuehrt. Apply-Button ist sichtbar mit klarem State (`Locked` / `Unlocked`).

#### Run-Modus orthogonal zu Profil
Heute ist `Workflow` ein Mischbegriff. Sauber waere:

- `AuditProfile` (read-only Workflows + zukuenftige read-only Funktionen)
- `RunMode` (Preview/Move/ConvertOnly) — nur fuer Action-Workflows

Damit haengt der Apply-Button **immer am gleichen RunMode-Wechsel** (DryRun -> Move) und das UI-Verhalten ist je Profil deterministisch ableitbar.

#### Persistente Next-Action-Zone
Egal welcher Tab aktiv ist, ein fester Bereich (z. B. Header) zeigt **immer den naechsten erwarteten Schritt** basierend auf `CurrentRunState` und gewaehltem Profil.

| State | Profil | Next-Action-Zone |
| --- | --- | --- |
| `Idle, no preview` | Action | "Erst Preview machen: [Preview starten]" |
| `CompletedDryRun` | Action | "Bereit zum Anwenden: [Move ausfuehren] [Bericht] [Verwerfen]" |
| `Completed` | Action | "Lauf erfolgreich: [Rollback] [Bericht]" |
| `Completed` | Audit | "Audit abgeschlossen: [Bericht] [Folge-Workflow: Sort starten]" |
| `Failed`/`Cancelled` | beide | "Fehler/Abbruch: [Bericht] [Retry]" |

### Harte Bedingungen (Critic-Vorgabe)

1. **Eine fachliche Wahrheit.** GUI, CLI und API muessen das gleiche Profil-/Modus-Konzept verwenden. Keine WPF-eigene Sonderlogik. ADR-0006 ist anzupassen, nicht parallel zu fuehren.
2. **Keine halbe Migration.** Wenn `Workflow` umstrukturiert wird, muessen Settings (`%APPDATA%\Romulus\settings.json`), CLI-Subcommand-Flags, API-Endpunkte und Reports konsistent migriert sein. Eine Welle `feat/workflow-split` ist Pflicht.
3. **Identitaets-Frage.** Diese Aenderung muss die Sichere-Cleanup-Identitaet schaerfen (klarere Apply-Trennung, klarere Audit-Sicherheit). Ohne Beleg ist sie ein UX-Refactor ohne Identitaetsgewinn — dann gilt sie als abgelehnt.
4. **Keine neuen Features.** Wenn aus dem Refactor neue Profile/Modi entstehen, ist das ein eigener ADR-Eintrag, kein Beifang.

### Reichweite (geschaetzt, nicht verbindlich)

| Schicht | Aenderung |
| --- | --- |
| Contracts | neues Modell `AuditProfile`/`RunMode`-Trennung; Run-Optionen umstrukturieren |
| Core | Konsumenten von `RunOptions` anpassen |
| Infrastructure | Orchestration-Pfade, Serialisierung, Settings-Migration |
| WPF | Workflow-Selector, MainViewModel, XAML, Tab-Sichtbarkeit, Action-Bar, Next-Action-Zone |
| CLI | Subcommand-Mapping (parity!), Flag-Migration, Rueckwaertskompatibilitaets-Strategie |
| API | Endpoint-Vertraege, Versionierung |
| Reports | Mapping anpassen |
| Settings | Migration der `%APPDATA%\Romulus\settings.json` (Profil + Modus) |
| i18n | de/en/fr neue Keys, alte Keys deprecaten |
| Tests | Wpf+Core+Api+CLI Pin-Tests anpassen, neue Invarianten fuer Profil-Modus-Trennung |

## Konsequenzen

### Wenn implementiert
- Plus: User versteht sofort, ob ein Workflow Apply hat oder nicht.
- Plus: Action-Bar/Next-Action-Zone konsistent ueber alle Profile.
- Plus: ADR-0006 wird sauberer (heute mischt es Mode und Workflow implizit).
- Minus: Multi-Schicht-Eingriff (~20-40 Dateien geschaetzt).
- Minus: Settings-/CLI-/API-Migration mit Rueckwaertskompatibilitaets-Aufwand.

### Wenn nicht implementiert
- Mini-P1-Banner + Next-Action-Zone uebernehmen die UX-Klaerung.
- Workflow-Selector bleibt semantisch ueberladen, aber die Verwirrung wird durch UI-Hinweise abgefedert.

## Aktivierungsbedingungen

Diese ADR wechselt erst auf `Accepted`, wenn **alle** Bedingungen erfuellt sind:

1. Mini-P1 (Audit-Only-Banner im DAT-Verification-Workflow + persistente Next-Action-Zone) ist live und mindestens 4 Wochen produktiv genutzt.
2. Konkreter Maintainer-Bericht dokumentiert reale Faelle, in denen der Banner + Next-Action-Zone die Verwirrung nicht beseitigen.
3. Identitaets-Frage wird mit klarem Ja beantwortet (Sichere-Cleanup-Identitaet wird durch Profil/Modus-Trennung geschaerft).
4. Aktualisierung von ADR-0006 ist im Scope mitgeplant (kein paralleles Modell).
5. Eigener Wave-Eintrag in `docs/plan/strategic-reduction-2026/plan.yaml` mit eigenem Owner und Test-Plan.

## Status-Verweis

Solange Aktivierungsbedingungen nicht erfuellt sind, ist diese ADR **deferred**. Code-Aenderungen, die `Workflow` umstrukturieren oder eine `AuditProfile`/`RunMode`-Trennung einfuehren, sind ohne Status-Wechsel auf `Accepted` ein Review-Block.
