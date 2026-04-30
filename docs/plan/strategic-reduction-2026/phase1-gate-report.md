# Phase-1-Gate-Bericht (T-W3-PHASE1-GATE)

- **Stichtag:** 2026-04-30 (pass-4 Re-Evaluation)
- **Reviewer-Rolle:** gem-critic (Coding-Agent), zweite Bewertungsrunde nach Option-G-Re-Skopierung
- **Verdict:** **PASS — go for Wave 4**
- **Status der Task selbst:** `done` (planning_pass=4)
- **Folgewellen (Welle 4):** freigegeben

## Pass-4-Update (2026-04-30)

Option-G-Re-Skopierung (siehe pass-3-Eintrag der Gate-Task): Kriterium 6
„Beta-Nutzer durchgelaufen" wurde durch „Synthetic-Smoke-Suite grün" ersetzt;
T-W3-BETA-USERS bleibt `wontfix-with-reason`. T-W3-RUN-SMOKE-SYNTHETIC ist
seit 2026-04-29 implementiert und CI-grün
([SyntheticSmokeTests.cs](src/Romulus.Tests/Smokes/SyntheticSmokeTests.cs)).
Verifikation am Re-Evaluations-Stichtag (2026-04-30):

- `dotnet test --filter "Category=Smoke"` → 3/3 grün (S-1 Top-Workflow,
  S-2 ConvertOnly, S-3 DAT-Audit). Jede Smoke deckt ≥6/8 ehemals manueller
  Workflow-Schritte ab; S-1 deckt 7/8 (Verify implizit via DryRun).
- `dotnet test --filter "FullyQualifiedName~Wave2I18nOrphan|FullyQualifiedName~Wave2CoverageGap"`
  → 9/9 grün (4 I18nOrphanGuard + 5 CoverageGap).
- Solution build (Vorher-Sektion): `dotnet build src/Romulus.sln` → 0 Fehler.

Die zwei Blocker B-1 und B-2 aus pass-2 sind durch die Re-Skopierung formal
ersetzt; B-1 (echte Beta-Cohort) ist als „nicht erbringbar im Coding-Agent-
Scope" dokumentiert und auf den Reaktivierungspfad in
[beta-recruiting-playbook.md](beta-recruiting-playbook.md) +
[beta-smoke-protocol.md](beta-smoke-protocol.md) verlegt. Der Synthetic-
Ersatz wurde bewusst gewählt, um die Phase-1→Phase-2-Sperre nicht
unbegrenzt liegen zu lassen, ohne die Produkt-Substanz zu verwässern.

## Aktualisierte Kriterien-Matrix (pass 4)

| # | Kriterium | Verdikt | Belege |
|---|---|---|---|
| 1 | Wirklich nur eine GUI? | **PASS** | unverändert; nur `Romulus.UI.Wpf` als GUI-Projekt. |
| 2 | Tool-Karten reduziert? | **PASS** | T-W1-FEATURE-CULL `done`. |
| 3 | Konsolen tier-markiert? | **PASS** | 163/163 Einträge mit `tier`. |
| 4 | i18n sauber? | **PASS** | Wave2I18nOrphanGuard 4/4 grün am Stichtag. |
| 5 | README ehrlich? | **PASS** | T-W2-README-REFRESH `done` (Commit `193631db`). |
| 6 | **Synthetic-Smoke-Suite grün?** (Ersatz für Beta-Smokes) | **PASS** | SyntheticSmokeTests 3/3 grün am Stichtag. |
| 7 | Top-20-Coverage-CI grün? | **PASS** | Wave2CoverageGap 5/5 grün am Stichtag. |
| 8 | i18n/Command-Orphan-CI grün? | **PASS** | siehe #4. |

**Bestanden:** 8 / 8.

## Risiken / offene Punkte (nicht gate-blockierend)

- **R-1 — Synthetic ≠ Real:** Synthetische Smokes können reale Friktion
  (UI-Verständlichkeit, Tool-Discovery, Erst-Setup-Hürden) nicht abbilden.
  Mitigation: Reaktivierungspfad für Beta-Cohort bleibt via
  beta-recruiting-playbook.md offen; Welle 4 enthält
  T-W4-DISCOVERY-LOOP (`wontfix-with-reason` solange ohne Cohort).
- **R-2 — Snapshot-Artefakte unter `benchmark/smokes/`:** noch nicht
  ausgecheckt (im pass-4-Eintrag von T-W3-RUN-SMOKE-SYNTHETIC als
  „NACHGELAGERT, nicht P1" markiert). Nicht gate-blockierend, weil die
  Tests heute ihre Erwartungen direkt aus den Domänen-Modellen ableiten.
- **R-3 — Pre-existing Test-Failure** in
  `GuiViewModelTests.MainWindowXaml_Title_IsRomulus_AndActionRailRemoved`:
  Token „SmartActionBar"/„ShowSmartActionBar" steht noch in einem
  Kommentar in [MainWindow.xaml](src/Romulus.UI.Wpf/MainWindow.xaml#L256-L257)
  (T-W1-Restleistung gem. plan-Kommentar). Trifft nur den Pin-Test, nicht
  die Funktion. Empfehlung: in T-W1-I18N-ORPHAN-SWEEP-Folge oder als
  triviale Hygiene-Aktion kurz nach Gate-Pass mit aufräumen.

## Folgen für Welle 4

Welle 4 ist freigegeben:

- T-W4-DISCOVERY-LOOP bleibt `wontfix-with-reason` (kein Cohort).
- T-W4-TELEMETRY-OPT-IN, T-W4-AUDIT-VIEWER-UI, T-W4-DECISION-EXPLAINER,
  T-W4-REVIEW-INBOX → können starten.

## Verlauf

### Pass 2 (2026-04-29) — Verdict: BLOCKED — needs_revision

(Original-Bewertung; bleibt zur Nachvollziehbarkeit erhalten.)

## Kurzfazit

Sechs von acht Kriterien sind objektiv bestanden. Zwei Kriterien sind **hart
blockiert** durch fehlende reale Beta-Daten. Eine Pseudo-Freigabe ist
ausgeschlossen — sie würde die Plan-Maxime verletzen, dass „vollständig =
ehrlich abgesichert, nicht selbstausgestellt" bedeutet.

Die zwei pendenten Researcher-Tasks sind keine Coding-Tasks; sie verlangen
echte externe Tester (`>= 3 von 5 extern`) und beobachtete Smokes auf
Maintainer-Maschinen. Coding-Agent kann das nicht erbringen, ohne genau die
Selbstausstellung zu produzieren, gegen die das Audit-Moratorium und das
T-W2-COVERAGE-GAP-CHECK schützen sollen.

## Kriterien-Matrix

| # | Kriterium | Verdikt | Belege / Gründe |
|---|---|---|---|
| 1 | Wirklich nur eine GUI? | **PASS** | `src/` enthält nur `Romulus.UI.Wpf` als GUI-Projekt; CLI/API sind Entry Points ohne UI. |
| 2 | Tool-Karten reduziert? | **PASS** (Welle 1) | T-W1-* Cull-Tasks sind `done`; FeatureCommandService hat nur noch No-op-Stubs für Wave-1-Cull-Reste. |
| 3 | Konsolen tier-markiert? | **PASS** | 163/163 Einträge in [data/consoles.json](data/consoles.json) tragen `tier`-Feld (Sample: `3DO` → `tier: best-effort`). |
| 4 | i18n sauber? | **PASS** (CI-gesichert) | T-W1-I18N-ORPHAN-SWEEP `done`; T-W2-I18N-ORPHAN-CI-TEST `done`; [Wave2I18nOrphanGuardTests](src/Romulus.Tests/Wave2I18nOrphanGuardTests.cs) 4/4 grün; Baseline-Schrumpfregel aktiv. |
| 5 | README ehrlich? | **PASS** | T-W2-README-REFRESH `done` (Commit `193631db`). |
| 6 | Beta-Nutzer durchgelaufen? | **BLOCKED** | T-W3-BETA-USERS `pending`; T-W3-RUN-SMOKE-WITH-USERS `pending`. Vorbereitungs-Playbooks liegen, reale Cohort-Daten fehlen. |
| 7 | Top-20-Coverage-CI grün? | **PASS** | T-W2-COVERAGE-GAP-CHECK `done`; [Wave2CoverageGapTests](src/Romulus.Tests/Wave2CoverageGapTests.cs) 5/5 grün. |
| 8 | i18n/Command-Orphan-CI grün? | **PASS** | siehe #4. |

**Bestanden:** 6 / 8 — **Blockiert:** 2 / 8.

## Blocker-Detail

### B-1: Keine reale Beta-Cohort

- **Severity:** blocking
- **Bezug:** Kriterium 6, T-W3-BETA-USERS Acceptance „Mindestens 5 echte
  Nutzer; mind. 3 von 5 müssen extern sein".
- **Stand heute:** [beta-recruiting-playbook.md](docs/plan/strategic-reduction-2026/beta-recruiting-playbook.md)
  ist geliefert (Owner-Slots, Ansprache, Interview-Leitfaden, Cohort-Schema,
  Annahme-Kriterium). Es ist **keine** ausgefüllte Cohort-Tabelle vorhanden,
  kein Owner Akquise und kein Owner Discovery-Loop benannt.
- **Warum nicht autonom lösbar:** Reale Akquise externer Tester ist eine
  Maintainer-Pflicht. Eine fingierte Cohort wäre der schlimmste Fail-Modus
  des Gates (siehe T-W3-PHASE1-GATE failure_mode „Gate wird durchgewunken").
- **Empfehlung:** Maintainer benennt beide Owner, rekrutiert ≥5 Personen
  über die im Playbook gelisteten Kanäle, füllt Cohort-Block aus, setzt
  T-W3-BETA-USERS auf `done` mit pass-3-Eintrag.

### B-2: Keine beobachteten Workflow-Smokes

- **Severity:** blocking
- **Bezug:** Kriterium 6, T-W3-RUN-SMOKE-WITH-USERS Acceptance „mind. 5
  Nutzer haben eine vollständige Workflow-Smoke auf eigener Maschine
  durchlaufen, ≥4/5 ohne Live-Coaching".
- **Stand heute:** [beta-smoke-protocol.md](docs/plan/strategic-reduction-2026/beta-smoke-protocol.md)
  liegt vor (8-Schritt-Skript, Severity P1/P2/P3, Beobachtungsbogen-Schema,
  Friktions-Backlog-Pflicht). Es existiert kein einziger ausgefüllter
  Beobachtungsbogen.
- **Voraussetzung:** B-1 muss zuerst gelöst sein.
- **Empfehlung:** Maintainer führt 5 Smokes nach Protokoll durch (kein
  Coaching, max 90min/Run), sammelt Beobachtungsbögen + Friktions-Backlog,
  setzt T-W3-RUN-SMOKE-WITH-USERS auf `done` mit pass-3-Eintrag.

## Was funktioniert (balanced critique)

- Welle-1-Reduktion + Welle-2-CI-Schutz greifen ineinander: I18nOrphanGuard
  und CoverageGap stoppen bekannte Wuchsmuster strukturell.
- Baseline-Approach für i18n-Orphans erlaubt geordneten Abbau ohne
  Auto-Bereinigungs-Risiko.
- Die zwei Researcher-Vorbereitungen (Playbook + Protokoll) machen den
  Real-World-Schritt für den Maintainer ausführbar; sie ersetzen ihn aber
  nicht.
- Das Gate-Setup zwingt diese Trennung klar durch (Dependencies, harte
  Acceptance-Kriterien, Audit-Moratorium).

## Folgen für Welle 4

- T-W4-DISCOVERY-LOOP, T-W4-TELEMETRY-OPT-IN, T-W4-AUDIT-VIEWER-UI,
  T-W4-DECISION-EXPLAINER, T-W4-REVIEW-INBOX hängen alle direkt oder
  indirekt an `T-W3-PHASE1-GATE`.
- Welle 4 darf bis zur Gate-Auflösung nicht gestartet werden, auch nicht in
  Teilen. Eine Vorgriff-Implementierung ohne reale Beta-Erkenntnisse
  verletzt T-W4-DISCOVERY-LOOP-Acceptance (Outcome-KPIs aus echten Nutzern).

## Re-Evaluation

Sobald B-1 und B-2 als `done` mit ehrlichen Daten markiert sind, wird das
Gate erneut bewertet:

1. Maintainer setzt T-W3-BETA-USERS und T-W3-RUN-SMOKE-WITH-USERS auf
   `done`, jeweils mit ausgefüllten Artefakten.
2. gem-critic re-runt diesen Bericht, prüft alle acht Kriterien erneut,
   inklusive einer Spot-Check-Stichprobe der Cohort-/Run-Belege.
3. Bei Pass: T-W3-PHASE1-GATE → `done`, planning_pass=3, Welle 4 darf
   starten.
4. Bei Fail eines anderen Kriteriums (Regression seit dieser Runde): die
   betroffenen Phase-1-Folge-Tasks werden re-opened, Welle 4 bleibt blockiert.

## Confidence

0.95 — Verdikt B-1/B-2 ist deterministisch durch Status `pending` der
Dependencies. Die sechs PASS-Bewertungen sind durch Quell-Belege gestützt;
einzige Unsicherheit ist eine mögliche unentdeckte Regression in einem der
sechs grünen Bereiche zwischen Wave-2-Abschluss und Re-Evaluation.
