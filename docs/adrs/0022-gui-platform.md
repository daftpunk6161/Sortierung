# ADR-0022: GUI-Plattform — WPF behalten, Avalonia archivieren

> **Status:** Accepted
> **Datum:** 2026-04-28
> **Owner:** Romulus-Maintainer (siehe AGENTS.md)
> **Scope:** Wahl der GUI-Plattform fuer das ausgelieferte Romulus-Produkt
> **Kontext:** Strategischer Reduktionsplan 2026 (`docs/plan/strategic-reduction-2026/plan.yaml` -> `resolved_decisions`)
> **Vorgaenger:** ADR-0006 (GUI / UI / UX Zielarchitektur)

---

## 1. Entscheidung

Romulus liefert genau **eine GUI** aus: **WPF** (`src/Romulus.UI.Wpf`).
Das parallel gepflegte Avalonia-Projekt (`src/Romulus.UI.Avalonia`) wird nach
`archive/avalonia-spike/` verschoben, aus `src/Romulus.sln` entfernt und nicht
mehr gebaut, getestet oder als Release-Artefakt gefuehrt.

Diese Entscheidung ist verbindlich. Welle 2 des strategischen Reduktionsplans
darf nicht starten, bevor Status hier `Accepted` ist und der Archivierungs-Task
`T-W1-AV-ARCHIVE` durchgelaufen ist.

## 2. Begruendung

### Pro WPF
- WPF ist der heutige Produktivpfad: alle stabilen Features (Dashboard,
  Wizard, Audit-Sidecar-Anzeige, Safety-Lane-Projection, RunViewModel,
  WebView2-Report-Preview) liegen ausschliesslich dort.
- Das Test-Sicherheitsnetz (`src/Romulus.Tests`) verifiziert ueber 700 Pins
  ausschliesslich gegen WPF-Typen (HardRegressionInvariantTests,
  GuiViewModelTests, WpfFingerprintAndI18nTests, GuiDashboardRedPhaseTests,
  Wave2RefactorRegressionTests).
- WPF auf Windows ist die einzige aktuell unterstuetzte Zielplattform
  (Persona "Windows-Power-User mit Retroarch / EmuDeck / LaunchBox").
- Romulus.CLI deckt headless / Linux / macOS-Bedarfe ohne zweite GUI ab.

### Contra Avalonia (Stand 2026-04-28)
- Es existieren keine belegten Linux- oder macOS-Nutzer mit konkreter
  Wartungsanforderung an eine native Romulus-GUI.
- Das Avalonia-Projekt ist ein Spike-Stand; alle Wave-2-Refactors aus
  `repo-memories` (RunViewModel.ApplyDashboard, IApiProcessHost,
  IResultExportService, SafetyLaneProjection, ToolRunnerAdapter-Cache,
  WizardView-Reflection-Bindings) sind nur in WPF realisiert.
- Doppelte GUI-Pflege widerspricht "Eine fachliche Wahrheit"
  (siehe AGENTS.md, "Nicht verhandelbare Regeln" Abschnitt 5).
- Doppelte GUI-Pflege widerspricht "Release-Faehigkeit geht vor"
  (siehe AGENTS.md, Abschnitt 1).

### Persona-Bezug
- Hauptpersona "Windows-Power-User" arbeitet ausschliesslich auf Windows 10/11
  und erwartet ein klassisches Win32-Look-and-Feel - WPF deckt das ohne
  zusaetzliche Abhaengigkeiten.
- Cross-Platform-Power-User-Persona wird durch die CLI bedient
  (`docs/plan/strategic-reduction-2026/plan.yaml` -> `T-W11-CROSS-PLATFORM-DECISION`).

## 3. Konsequenzen

### Sofort (Welle 1)
- `T-W1-AV-ARCHIVE` verschiebt `src/Romulus.UI.Avalonia/` nach
  `archive/avalonia-spike/`, entfernt das Projekt aus `src/Romulus.sln`,
  loescht die ProjectReference aus `src/Romulus.Tests/Romulus.Tests.csproj`
  und entfernt Avalonia-bezogene CI-Schritte (sofern vorhanden).
- README, `docs/architecture/*`, `docs/product/competitive-analysis.md`
  werden auf "eine GUI" reduziert.
- Git-Tag `avalonia-spike-archived` markiert den letzten gemeinsamen Stand.

### Mittelfristig
- Alle GUI-Refactors der Wellen 4-7 wirken nur auf
  `src/Romulus.UI.Wpf/`.
- Tests, die gegen Avalonia-Typen gepinnt waren (falls vorhanden), werden
  geloescht oder auf WPF migriert.
- `feature-cull-list.md` (`T-W1-FEATURE-CULL`) listet keine
  Avalonia-Wiederbelebung als Bonus-Feature.

### Langfristig
- Welle 11 enthaelt eine bewusste, periodische Re-Evaluierung
  (`T-W11-CROSS-PLATFORM-DECISION`), nicht als Implementierungs-Trigger,
  sondern als Sichtcheck der Reaktivierungs-Bedingung.

## 4. Reaktivierungs-Bedingung

Avalonia darf **nur** dann reaktiviert werden, wenn **alle** folgenden Punkte
gleichzeitig erfuellt sind:

1. **Drei oder mehr** real verifizierbare Linux- oder macOS-Nutzer
   benennen schriftlich (Issue / Sponsor-Channel) konkrete Use-Cases,
   die die CLI nicht abdeckt.
2. **WPF wird seitens Microsoft als End-of-Life** klassifiziert ODER eine
   konkrete Sicherheits-/Plattform-Regression macht WPF unbrauchbar.
3. Es existiert **keine vergleichbare Cross-Platform-Loesung** auf .NET-Basis
   (z. B. .NET MAUI Desktop, Uno Platform), die schneller ans Ziel fuehrt.

Trifft auch nur einer der Punkte nicht zu, bleibt diese Entscheidung in Kraft.

Reaktivierung ist **kein** Subsystem-PR, sondern ein neuer ADR (`0023-...` oder
hoeher), der ADR-0022 explizit superseded und eine eigene Migrations-Strategie
beschreibt (`archive/avalonia-spike/` ist Spike, kein produktiver Stand).

## 5. Nicht-Optionen

- "Beide GUIs parallel weiterpflegen" wurde verworfen wegen Verstoss gegen
  AGENTS.md Abschnitt 4 ("Keine doppelte Logik") und Abschnitt 5
  ("Eine fachliche Wahrheit").
- "Avalonia komplett loeschen" wurde verworfen, um den Lerneffekt des Spikes
  und seine Doku-Spuren erhalten zu lassen (Archivierung statt Loeschung).
- "Eine dritte GUI evaluieren (MAUI/Uno)" ist explizit kein Bestandteil
  dieses ADRs; siehe Reaktivierungs-Bedingung Punkt 3.

## 6. Verifizierung

- `Get-ChildItem src/Romulus.UI.Avalonia` liefert nichts mehr (Pfad
  existiert nicht in `src/`).
- `Select-String -Path src/Romulus.sln -Pattern Avalonia` liefert keine
  Treffer.
- `Select-String -Path src/**/*.csproj -Pattern Avalonia` liefert keine
  Treffer.
- README und `docs/architecture/*` enthalten keine Erwaehnung einer zweiten
  GUI mehr.
- `archive/avalonia-spike/` enthaelt das verschobene Projekt mit lesbarem
  Verzeichnisbaum.

## 7. Referenzen

- `docs/plan/strategic-reduction-2026/plan.yaml` -> `resolved_decisions`,
  `T-W1-AV-WPF-ADR`, `T-W1-AV-ARCHIVE`, `T-W11-CROSS-PLATFORM-DECISION`
- AGENTS.md (Nicht verhandelbare Regeln 1, 4, 5)
- ADR-0006 GUI / UI / UX Zielarchitektur (bleibt in Kraft, bezieht sich
  ausschliesslich auf WPF)
