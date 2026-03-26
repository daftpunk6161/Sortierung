---
name: Romulus-Workspace-Standards
description: Projektweite Workspace-Instructions für durchgängige Qualität: Architektur, GUI/UX (WPF/XAML), Sicherheit, Performance, Test-Qualität (keine Alibi-Tests), Refactoring-Disziplin, Release-Readiness, Tracking-Checklists
argument-hint: Optional: Repo-Root, Haupt-Entry-Points, Zielplattform(en), .NET-Version, UI-Technologie, Packaging/Release-Setup, Constraints.
agent: agent
tools:
  - agent
  - search
  - read
  - execute/getTerminalOutput
  - execute/testFailure
  - web
  - vscode/askQuestions
---

Du arbeitest im **Romulus**-Projekt (intern: RomCleanup). Diese Workspace-Instructions gelten **immer** (nicht task-spezifisch) und müssen bei jeder Analyse, Planung, Änderungsvorbereitung und Qualitätssicherung eingehalten werden.

**Projektziel (immer gültig):**  
Ein ROM-Cleanup/Sort/Dedupe/Convert Tool, das **zuverlässig, sicher, performant, wartbar und testbar** ist – mit einem **modernen, retro-stylischen, klaren und selbsterklärenden GUI/UX** (primär **WPF/XAML**, falls UI-Strategie geändert wird: nur kontrolliert, nachvollziehbar, ohne Funktionsverlust).

Deine Aufgabe ist es, bei jeder Arbeit im Projekt:
- Risiken, Bugs, Datenverlust-Footguns und UX-Probleme **früh** zu identifizieren,
- ein sauberes, zukunftssicheres Design zu fördern,
- und sicherzustellen, dass Tests **Bugs finden** (keine “Alibi-Tests”).

<rules>
- Handle dieses Projekt als **Release-kritisch**: Jede Änderung muss sauber begründet, testbar und rückverfolgbar sein.
- **Keine impliziten Annahmen**: unklare Anforderungen via #tool:vscode/askQuestions klären.
- **Datenverlust vermeiden** ist oberste Leitlinie: Moves/Deletes/Trash/Undo/Audit immer mit Sicherheitsdenken.
- **GUI ist 1st-class**: UX/Informationsarchitektur/Flows/Spacing/Responsiveness sind nicht “nice-to-have”.
- **Keine Alibi-Tests**: Tests müssen Negativfälle/Edge-Cases/Regressionen abdecken und realistisch scheitern können.
- **Keine Dubletten / toter Code / Rumgebastel**: alles muss begründet, konsistent, modular, auffindbar sein.
- **Alles trackbar** machen: Für größere Arbeiten am Ende immer eine **Markdown-Checklist mit Checkboxen** erstellen/aktualisieren.
</rules>

<always_on_quality_bar>
## A) Architektur & Code-Organisation (immer)
- Clean Architecture (Ports & Adapters): **Contracts** (Ports/Models) → **Core** (pure Domain) → **Infrastructure** (I/O-Adapter) → **Entry Points** (CLI/API/WPF).
- Bevorzugte Richtung: **Core-Logik "pure"** (deterministisch, keine I/O-Deps in `RomCleanup.Core`), damit sie testbar ist.
- **Dependency Injection** über Konstruktor-Injection, Interfaces aus `Contracts/Ports/`.
- Keine doppelten Implementierungen: gleiche Logik = ein Ort, gut getestet.
- DTOs als C# Records/Models in `Contracts/Models/`.

## B) GUI/UX Standards (WPF/XAML-first)
- Ziel: **selbsterklärend, nicht überladen, klare Flows, klare Prioritäten**.
- “Progressive Disclosure”: **Basic** (häufig) vs **Advanced** (selten) sauber trennen.
- Layout-Regeln:
  - großzügige Abstände, klare Gruppierung, keine Overlaps
  - responsive resizing (Grid, DockPanel, ScrollViewer wo nötig)
  - klare Visual Hierarchy (Headings, Sections, Cards, Dividers)
  - konsistente Buttons/Labels/Icons/Tooltips
- Flow-Regeln:
  - standardmäßig **DryRun/Preview-first**
  - “Move/Apply” nur mit klarer Zusammenfassung, Warnung und expliziter Bestätigung
  - Status/Phasen/Progress nicht nur Log-Textwall: klare UI-Elemente (Progress, Phase, Summary)
- Design-System:
  - Retro-modern Theme (Dark + Neon accents möglich), aber **lesbar** und **ruhig**
  - Theme-fähig (später): Farben/Styles zentral (ResourceDictionary)

## C) Sicherheit & Datenintegrität (immer)
- Schutz vor Path Traversal / Zip-Slip / Reparse Points / Symlinks/Junctions.
- Moves nur innerhalb validierter Roots; niemals außerhalb ohne explizite Freigabe.
- Archive Handling robust: fehlerhafte/zu große Archive sauber skippen, klare Meldungen.
- CSV/HTML Output: Injection-sicher (CSV formula injection, HTML encoding).
- “Trash statt Delete”: Default ist verschieben; Deletes nur wenn wirklich nötig und bewusst.

## D) Performance & Skalierung (immer)
- Große Libraries: Enumeration iterativ, Caching wo sinnvoll (FileInfo, Archive entries, Hash results).
- Regex: compiled & sparsam; Hotspots messen (Stopwatch/Profiling).
- Hashing: Streaming/Thresholds; UI darf nicht freezen (async/await, Dispatcher.Invoke bei WPF).
- UI Responsiveness: keine DoEvents; lange Tasks off-UI-thread, klare Progress/Cancel Mechanik.

## E) Tests & Qualität (keine Alibi-Tests)
- Test-Pyramide:
  - **Unit**: Parser/Normalisierung/Scoring/Winner-Selection/Region detection/GameKey
  - **Integration**: Temp-Dirs, Moves in Trash, Report Output, Archive handling (ZIP/7Z), Tool-invocations (mocked)
  - **Regression**: bekannte Bug-Fälle als Fixtures
  - **Property/Fuzz**: zufällige Filenames/Tags/Regions/Edge Strings
- Tests müssen “Bug Finder” sein:
  - negative tests (invalid inputs, missing tracks, corrupted archives, weird filenames)
  - invariants (z.B. niemals außerhalb Root; niemals leere Keys gruppieren; deterministisches Winner-Result)
  - realistische Daten: große Mengen, gemischte Extensions
- CI/Automation: `dotnet test src/RomCleanup.sln`, Coverage ≥ 50%, fail-fast.

## F) Refactoring-Disziplin
- Kein Refactor “auf Verdacht”: immer mit Ziel (Bug, UX, Wartbarkeit, Performance, Sicherheit).
- Kleine, sichere Schritte: “strangle”/Migration-Plan, Feature-Parität sichern.
- Jede größere Umstrukturierung braucht:
  - Migrationspfad
  - Risikoanalyse
  - Verifikation (Tests + manuelle Checks)
  - Rollback-Strategie (mind. konzeptionell)

## G) Feature-Management & Zukunftssicherheit
- Neue Features müssen:
  - klaren User-Value haben
  - in die Informationsarchitektur passen (Tab/Section/Advanced)
  - testbar sein
  - keine Kernstabilität gefährden
- Erweiterbarkeit: neue Tabs/Module sollen ohne UI-Chaos integrierbar sein.
</always_on_quality_bar>

<workflow>
Diese Phasen gelten projektweit für jede nicht-triviale Arbeit (auch kleine Fixes, wenn Risiko besteht).

## 1. Discovery
- Sammle Kontext (Dateien, Entry-Points, zentrale Flows, Abhängigkeiten).
- Identifiziere Risiken: Datenverlust, UX-Footguns, Performance, Security, Regression.
- Dokumentiere Findings kurz, priorisiert (Release-Blocker zuerst).

## 2. Alignment
- Wenn Unklarheiten bestehen: #tool:vscode/askQuestions nutzen.
- Constraints festnageln: .NET-Version, Windows-only, UI-Tech (WPF), Packaging, Kompatibilität.
- Entscheide “Minimal Risk Path” vs “Strategischer Umbau”.

## 3. Design (Plan)
- Erstelle einen ausführbaren Plan mit klaren Schritten und Verifikation.
- Bei GUI-Arbeit: IA + Flow + Layout + Styling + Zukunftsfähigkeit explizit beschreiben.
- Bei Änderungen an Kernlogik: Test-Seams + Regression-Schutz einplanen.

## 4. Verification
- Definiere: welche Tests, welche manuellen Checks, welche Sample-Daten.
- Prüfe: deterministisch, idempotent (wo sinnvoll), keine Seiteneffekte außerhalb Root.

## 5. Tracking
- Erzeuge/aktualisiere eine **Markdown-Checklist mit Checkboxen** für:
  - Bugs/Risiken
  - GUI/UX Tasks
  - Refactoring Tasks
  - Tests/QA
  - Security/Performance
  - Feature Backlog
</workflow>

<tracking_checklist_template>
Wenn eine Arbeit mehr als “trivial” ist: füge am Ende immer eine Checklist hinzu oder update sie.

```markdown id="q5m7cv"
## Tracking Checklist (RomCleanup)

### Release-Blocker
- [ ] …

### GUI/UX (WPF/XAML)
- [ ] Informationsarchitektur/Navigation klar
- [ ] Spacing/Overlaps/Resize sauber
- [ ] DryRun/Preview/Confirm Flow verständlich
- [ ] Status/Progress/Cancel sauber
- [ ] Retro-modern Theme (lesbar) umgesetzt

### Core/Engine
- [ ] Duplikate entfernt / vereinheitlichte Helpers
- [ ] Pure functions testbar gemacht
- [ ] Determinismus & Idempotenz geprüft

### IO/Safety
- [ ] Path traversal / Zip-slip / Reparse Points abgesichert
- [ ] Trash/Undo/Audit konsistent

### Performance
- [ ] Enumeration/Hashing/Regex Hotspots geprüft
- [ ] UI bleibt responsiv

### Tests (keine Alibi-Tests)
- [ ] Unit Tests: Parser/Scoring/Winner/Keys
- [ ] Integration: Move/Trash/Reports/Archives
- [ ] Regression: bekannte Fälle als Fixtures
- [ ] Negative/Edge/Fuzz Tests

### Feature Backlog
- [ ] …