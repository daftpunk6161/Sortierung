# 30-60-Tage Discovery-Loop Playbook (T-W4-DISCOVERY-LOOP)

> **Status:** Vorbereitung. Loop kann erst gestartet werden, wenn
> T-W3-PHASE1-GATE auf `done` flippt (Cohort + Smokes liegen real vor).
> Bis dahin bleibt T-W4-DISCOVERY-LOOP `pending`.

## Zweck

Outcome-KPIs ersetzen Output-KPIs (Plan-Maxime, Critic-Review-Befund #6).
Wir messen nicht „wie viele Features gebaut", sondern „kommen reale Nutzer
ehrlich durch den Workflow". Der Loop endet erst, wenn drei
aufeinanderfolgende Wochen stabile KPIs zeigen oder eine bewusste
Verlängerung mit Begründung dokumentiert ist.

## Vorbedingungen

- T-W3-PHASE1-GATE = `done` (Beta-Cohort + Smokes liegen real vor).
- Beta-Cohort aus [beta-recruiting-playbook.md](beta-recruiting-playbook.md)
  ist befüllt (≥5 Personen, ≥3 extern, beide Owner benannt).
- Mindestens ein Run-Smoke pro Person aus
  [beta-smoke-protocol.md](beta-smoke-protocol.md) liegt als
  Beobachtungsbogen vor.
- Friktions-Backlog (`docs/plan/strategic-reduction-2026/friction-backlog.md`)
  existiert; P1-Friktionen sind Owner-zugewiesen.

## Owner-Slots

| Rolle | Verantwortung | Owner |
|---|---|---|
| Owner Discovery-Loop | Wochenrhythmus, KPI-Pflege, Eskalation | TBD |
| Owner Issue-Triage | wandelt Friktions-Findings in Issues, weist zu | TBD |
| Owner Daten-Integrität | prüft KPI-Belege Stichproben-weise | TBD |

Eskalation-Pfad: Owner Discovery-Loop → Repo-Maintainer (daftpunk6161).

## Outcome-KPIs (verbindlich)

Vier KPIs. Jede misst pro Woche je Beta-Nutzer. Aggregation = Mittelwert
über alle Personen, die in der Woche aktiv waren.

### KPI-1: Aktivierungsrate

- **Definition:** Anteil der Beta-Nutzer, die in der Woche mindestens
  einen vollständigen Workflow (Add Library → Scan → Plan → Confirm →
  Execute → Report) gestartet haben.
- **Quelle:** Selbstbericht im Wochenkurzinterview + Audit-Trail-Stichprobe.
- **Stabilitäts-Schwelle:** ≥ 0.6 über 3 Wochen.
- **Anti-Pattern:** „Login-only" zählt nicht als Aktivierung.

### KPI-2: Vollständige-Workflow-Quote

- **Definition:** Anteil der gestarteten Workflows, die ohne Live-Coaching
  bis Report durchgelaufen sind.
- **Quelle:** Selbstbericht + spot-check Audit-Trail (run_id muss
  Phasen ConsoleSort/WinnerConversion/Move/DatRename oder
  äquivalente DryRun-Pendants aufweisen).
- **Stabilitäts-Schwelle:** ≥ 0.7 über 3 Wochen.
- **Anti-Pattern:** Workflows mit Coach-Eingriff zählen als gescheitert.

### KPI-3: Rollback-Nutzung

- **Definition:** Anteil der Nutzer, die in der Woche mindestens einmal
  bewusst Rollback geprüft (nicht zwingend ausgeführt) haben.
- **Quelle:** Selbstbericht. Frage „Wussten Sie, dass es Rollback gibt
  und wo?" mit Antwort-Skala.
- **Stabilitäts-Schwelle:** ≥ 0.6 über 3 Wochen (Wissen + Auffindbarkeit).
- **Anti-Pattern:** „Habe ich nie gebraucht" ohne dass Person weiß, wo
  es zu finden wäre, zählt als 0.

### KPI-4: Vertrauenswert (Selbsteinschätzung)

- **Definition:** Antwort auf „Würden Sie Romulus auf Ihre echte
  Hauptsammlung anwenden, ohne vorher eine Test-Kopie zu machen?" auf
  Skala 1-5 (1 = niemals, 5 = sofort).
- **Quelle:** Selbstbericht im Wochenkurzinterview.
- **Stabilitäts-Schwelle:** Mittelwert ≥ 3.5 über 3 Wochen, kein
  Einzelwert < 2 in der letzten Woche.
- **Anti-Pattern:** Antwort 5 ohne dass die Person den
  Audit-Trail je geprüft hat → Beobachter senkt auf 3 mit Begründung.

## Wochenrhythmus

Jede Woche, jede Beta-Person:

1. **Mo:** Owner Discovery-Loop versendet Slot-Anfrage (15min Window).
2. **Di–Do:** 15min Kurzinterview pro Person (Telefon / VoIP).
   - „Was haben Sie diese Woche mit Romulus gemacht?"
   - „Was war die größte Reibung?"
   - „KPI-3-Frage: Rollback-Wissen?"
   - „KPI-4-Frage: Vertrauenswert heute?"
3. **Fr:** Owner Discovery-Loop trägt KPIs in Tracker ein, weist neue
   P1/P2-Friktionen zu, schließt geschlossene Issues.
4. **Fr Abend:** Aggregat-KPI-Snapshot wird in
   `docs/plan/strategic-reduction-2026/discovery-loop-tracker.md`
   committet (eine neue Zeile pro Woche).

Maximal 30min Owner-Aufwand pro Person pro Woche. Wenn mehr nötig:
Eskalation, nicht Komprimierung.

## Issue-Triage-Regeln

- P1 (Datenverlust / Blocker): innerhalb 24h Issue mit Owner.
- P2 (schwere Verwirrung, kein Datenverlust): innerhalb 1 Woche Issue.
- P3 (Reibung, Verbesserung): in Friktions-Backlog, monatliche Sichtung.

Verboten:
- P1 als P3 herabstufen, weil „Workaround existiert".
- KPI-Berechnung mit fehlenden Wochen verschleiern (Lücken sichtbar
  lassen, nicht interpolieren).
- „Diese Woche keine Daten" als implizit „grün" werten.

## KPI-Tracker-Schema

Datei: `docs/plan/strategic-reduction-2026/discovery-loop-tracker.md`
(wird beim ersten echten Loop-Start angelegt). Pro Woche eine Zeile:

```yaml
weeks:
  - week_iso: "2026-W18"           # ISO-Wochen-Marker
    cohort_size_active: 5
    cohort_size_total: 5
    kpi_1_activation_rate: 0.0     # 0..1, leer wenn keine Daten
    kpi_2_complete_workflow: 0.0
    kpi_3_rollback_awareness: 0.0
    kpi_4_trust_mean: 0.0          # 1..5
    kpi_4_trust_min: 0
    p1_open: 0
    p1_closed: 0
    p2_open: 0
    p2_closed: 0
    notable_friction: |
      Freitext, max 5 Zeilen
    owner_signoff: "TBD"
    audit_spotcheck_done: false    # true wenn KPI-2 mit Audit-Trail belegt
```

## Stabilitätsregel

Loop endet, wenn alle vier Schwellen drei aufeinanderfolgende
ISO-Wochen erreicht sind UND in jeder dieser drei Wochen mindestens
eine Audit-Spot-Stichprobe (`audit_spotcheck_done: true`) gemacht wurde.

Kommt eine Verlängerung: pass-3-Eintrag mit Begründung im plan.yaml,
keine stille Fortsetzung.

## Annahme-Kriterium für T-W4-DISCOVERY-LOOP = `done`

Erst wenn alle Punkte erfüllt sind, darf Status `done` gesetzt werden:

- [ ] Beide Owner real benannt (nicht TBD).
- [ ] Mindestens 4 Wochen ISO-Daten im Tracker.
- [ ] Mindestens 3 aufeinanderfolgende Wochen mit allen 4 KPIs ≥ Schwelle.
- [ ] Pro stabiler Woche mindestens 1 Audit-Spot-Stichprobe dokumentiert.
- [ ] Kein offener P1 in der letzten stabilen Woche.
- [ ] Friktions-Backlog für die Periode hat Owner-Zuweisung pro P1/P2.
- [ ] pass-3-Eintrag mit Beleg-Verweis (Tracker-Commits).

Eine Pseudo-Erfüllung („wir haben den Loop ja definiert") ist explizit
verboten. Definition ≠ Durchführung.

## Anti-Patterns (zu vermeiden)

- KPI-Definition ändern, weil Werte nicht erreicht werden.
- Wochen ohne Antwort als „Antwort = OK" werten.
- Output-KPIs (Anzahl gebauter Features, geschriebene Tests, gefixte
  Bugs) als Discovery-Loop-Beleg ausgeben.
- Issue-Triage durch Backlog-Aufstauen ersetzen.
- Loop verkürzen mit Begründung „Welle 5 wartet". Phase-2-Gate hängt
  am Loop-Abschluss; Verkürzung verletzt Gate-Acceptance.

---

## Solo-Mode und Externalisierungs-Greenlight (ab 2026-05-04)

> **Status:** Solo-Mode aktiv. Discovery-Loop ist deferred (nicht
> won't-fix-permanent), bis der Maintainer den Externalisierungs-Greenlight
> erteilt. Siehe auch `AGENTS.md` Sektion „Solo-Mode".

### Warum Solo-Mode

Maintainer-Entscheidung 2026-05-04: Romulus wird vorlaeufig nur vom
Maintainer selbst genutzt. Externe Validierung (Beta-Cohort, Discovery-Loop,
Release fuer Dritte) startet erst, wenn die Greenlight-Kriterien unten
erfuellt sind. Bis dahin gilt:

- Beta-Akquise (T-W3-BETA-USERS) ist NICHT zu starten.
- Discovery-Loop-Tracker (`discovery-loop-tracker.md`) wird NICHT mit
  Maintainer-Selbstdaten gefuellt — Selbst-Validierung ist keine
  Validierung.
- Identitaets-Guardrail aus AGENTS.md gilt im Solo-Mode strenger als im
  Team-Mode (Scope-Creep ist die Solo-Hauptgefahr).

### Greenlight-Kriterien (alle muessen erfuellt sein)

Erst wenn ALLE folgenden Punkte vom Maintainer schriftlich bestaetigt
sind, darf eine Welle 12 (Externalisierungs-Welle) gestartet werden, die
T-W3-BETA-USERS, T-W3-RUN-SMOKE-WITH-USERS und T-W4-DISCOVERY-LOOP
reaktiviert:

- [ ] **G-1 — Eigeneinsatz-Reife:** Mindestens 8 aufeinanderfolgende
  Wochen produktiver Maintainer-Einsatz auf realer eigener Sammlung
  ohne neuen P1-Bug (Datenverlust, Sicherheits-Issue, falsche
  Winner-Selection, Preview/Execute-Divergenz).
- [ ] **G-2 — Workflow-Ehrlichkeit:** Voller Standardablauf
  (Add Library -> Scan -> Plan -> Confirm -> Execute -> Report -> Rollback)
  mindestens 5 Mal vollstaendig ohne improvisierte Workarounds
  durchlaufen. Jede gefundene Reibung ist entweder gefixt oder
  bewusst als „bleibt im Solo-Mode tolerierbar, fuer Externalisierung
  zu fixen" notiert.
- [ ] **G-3 — Doku-Stand-Alone:** Eine fremde Person kann mit
  README + GUI-Onboarding den Workflow starten, ohne Maintainer-Hilfe.
  Dieser Punkt wird durch genau eine Trockenlese durch den Maintainer
  validiert (kein Subagent-Audit).
- [ ] **G-4 — Vertrauenswert (Selbsteinschaetzung):** Maintainer
  beantwortet die KPI-4-Frage („Wuerden Sie Romulus auf Ihre echte
  Hauptsammlung anwenden, ohne vorher eine Test-Kopie zu machen?")
  ehrlich mit >= 4. Keine 5 ohne dass der Audit-Trail real geprueft
  wurde.
- [ ] **G-5 — Externalisierungs-Wille:** Maintainer hat die Kapazitaet
  und den Willen, den Wochenrhythmus aus Sektion „Wochenrhythmus" oben
  ueber mindestens 6 Wochen zu betreiben. Kein „start-and-pray".

### Anti-Patterns im Solo-Mode

- Solo-Mode als Begruendung fuer Output-Sprints („nur ich nutze es,
  also kann ich basteln").
- KPI-Schwellen aus Sektion „Outcome-KPIs" senken oder loeschen, weil
  aktuell nicht relevant. Sie sind die Latte fuer spaeter.
- Eigene Maintainer-Daten in `discovery-loop-tracker.md` eintragen.
- „Fuer-spaeter"-Features im Solo-Mode bauen ohne Identitaets-Check.
- Greenlight nachtraeglich aufweichen, weil ein Kriterium unbequem ist.

### Re-Bewertung

Greenlight-Status wird nicht regelmaessig geprueft. Maintainer
entscheidet aus eigenem Antrieb, wann er die Kriterien fuer erfuellt
haelt. Wenn der Solo-Mode laenger als 12 Monate ohne Greenlight-Pruefung
laeuft, ist eine bewusste Sunset-Entscheidung (Tool einfrieren,
Sicherheits-Patches only) zu pruefen — keine stille Fortsetzung.
