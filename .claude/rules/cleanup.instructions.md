---
description: Projektweite Instructions für RomCleanup (PowerShell + WPF/XAML). Laden für alle Dateien im Repo, insbesondere Scripts, UI und Tests.
paths:
  - "**/*.ps1"
  - "**/*.psm1"
  - "**/*.psd1"
  - "**/*.xaml"
  - "**/*.cs"
  - "**/*.json"
  - "**/*.md"
  - "**/*.yml"
  - "**/*.yaml"
  - "tests/**/*"
  - "src/**/*"
---
Provide coding guidelines that AI should follow when generating code, answering questions, or reviewing changes.

# RomCleanup – Workspace Coding Guidelines (immer gültig)

Diese Regeln gelten **projektweit und dauerhaft**. Sie sind **nicht task-spezifisch**.  
Ziel ist ein **release-fähiges**, **sicheres**, **wartbares** und **testbares** Tool mit **modernem, retro-stylischem, selbsterklärendem GUI/UX** (primär **WPF/XAML**).

## 1) Grundprinzipien (Quality Bar)
- **Stabilität vor Feature-Hype:** Keine Änderung ohne klaren Nutzen, Risikoabschätzung und Verifikation.
- **Kein Datenverlust:** Default ist **verschieben** (Trash/Audit), nicht löschen. Wenn gelöscht werden muss: explizit, bestätigt, dokumentiert.
- **Keine Alibi-Tests:** Tests müssen **Fehler finden** (Negativfälle, Edge Cases, Regressionen). “Immer grün” ohne Aussagekraft ist wertlos.
- **Keine Dubletten / toter Code:** Doppelte Logik konsolidieren, ungenutzten Code entfernen, eine Quelle der Wahrheit.
- **Determinismus:** Gleiche Inputs ⇒ gleiche Outputs (besonders Winner-Selection, Sort-Entscheidungen, Reports).
- **Zukunftssicherheit:** Architektur so gestalten, dass neue Features ohne UI-Chaos und ohne Core-Spaghetti integrierbar sind.

## 2) Architekturregeln (Separation of Concerns)
Trenne konsequent in Schichten/Module:
- **UI (WPF/XAML)**: Darstellung, Interaktion, Bindings, Commands, Visual States.
- **App/Orchestration**: Ablaufsteuerung, Progress/Cancel, Konfiguration laden/speichern.
- **Core Engine (pure logic)**: Scoring, Keying, Region-Erkennung, Winner-Auswahl, Policies.
- **IO/Safety Layer**: File-Enum, Move/Trash, Path-Sicherheitschecks, Reparse-Point/Zip-Slip-Schutz.
- **Tools Integration**: chdman/dolphintool/7z/psxtract, quoting/args, exit codes, retries, timeouts.
- **Reports**: CSV/HTML, Encoding/Escaping, Injection-Schutz.
- **DAT/Hashing**: Indexing, Streaming XML, Hash Cache, Thresholds.

**Regeln:**
- Core-Logik möglichst **ohne UI/Globals/Side-Effects** (für Testbarkeit).
- “Seams” einbauen: IO/Process-Aufrufe so kapseln, dass sie testbar/mocking-fähig sind.
- Gemeinsame Helpers zentralisieren (Quoting, TempFiles, Logging, Encoding).

## 3) GUI/UX Regeln (WPF/XAML – höchste Priorität)
Ziel: **selbsterklärend, nicht überladen, klare Bereiche, viel Luft, keine Overlaps**.

### Informationsarchitektur & Navigation
- **Progressive Disclosure:** “Basic” vs “Advanced” sauber trennen.
- Häufige Aktionen nach vorne, seltene Optionen in “Erweitert”, “Tools”, “Maintenance”.
- Komplexe Abläufe als **Wizard/Stepper** (z.B. Roots → Optionen → Preview → Confirm → Run → Report/Undo).

### Layout/Design (WPF)
- Verwende **Grid**-Layouts mit klaren Rows/Columns, konsistenten Margins/Paddings.
- Keine überfüllten Screens: scrollbare Bereiche (ScrollViewer) wo nötig.
- Einheitliche Komponenten: Cards/GroupBoxes/Expander, konsistente Buttons/Icons/Labels/Tooltips.
- **Responsiveness:** sauberes Resize-Verhalten, keine überlappenden Panels, klare MinSizes.
- Status nicht als Textwall: dedizierte UI für **Phase/Progress/Warnings/Summary**.

### Styling (retro-modern, aber lesbar)
- Zentrales **ResourceDictionary** für Farben/Typografie/Spacing.
- Theme-fähig denken (später): Dark + Neon Accent möglich, aber Kontrast/Lesbarkeit sicherstellen.
- Einheitliche Visual Hierarchy: Headings, Subheadings, Dividers, Badges.

## 4) Sicherheit & Datenintegrität (nicht verhandelbar)
- **Path Traversal verhindern**: alle Moves innerhalb erlaubter Roots validieren.
- **Zip-Slip** erkennen und blocken (Archive Entry Paths).
- **Reparse Points** (Symlink/Junction) strikt behandeln (blocken oder klar definieren).
- **CSV Injection** verhindern (Excel-Formeln), **HTML Escaping** konsequent.
- Aktionen mit Risiko (Move/Delete/Convert) brauchen:
  - klare Summary vor Ausführung
  - bestätigte “Danger Zone”
  - Audit-Log

## 5) Performance & Skalierung
- Enumeration iterativ (keine unkontrollierte Rekursion), caching wo sinnvoll (FileInfo, Archive Entries).
- Hashing/Indexing streaming-basiert und mit Thresholds.
- Regex: compiled, Hotspots prüfen, unnötige mehrfach-Scans vermeiden.
- UI darf nicht frieren:
  - lange Tasks off-UI-thread
  - Progress/Cancel robust
  - keine DoEvents-Spaghetti (in WPF bevorzugt Dispatcher/Async Pattern)

## 6) Tests (Bug-Finder, keine Alibis)
Pflicht-Testarten:
- **Unit**: RegionTag, GameKey, VersionScore/FormatScore, Winner-Selection, Sanitizer.
- **Integration**: TempDirs, Move→Trash, Reports, Archive Handling (ZIP/7Z), DAT Index/Hits.
- **Regression**: reale Problemfälle als Fixtures.
- **Negative/Edge**: beschädigte Archive, fehlende Tracks (cue/gdi), Path Traversal, Reparse Points, Sonderzeichen.
- **Property/Fuzz**: random Filenames/Tags/Region-Kombos, Parser Robustheit.

Tests müssen:
- echte Fehlerfälle erzeugen können
- Invarianten prüfen (z.B. niemals außerhalb Root verschieben; nie leere Keys für Gruppierung)
- deterministische Ergebnisse sicherstellen

## 7) Refactoring-Regeln
- Kein Refactor “nur weil”: immer Ziel + Nutzen + Risiko + Verifikation.
- Schrittweise Migration (“Strangler Pattern”), Feature-Parität sichern.
- Jede größere Umstrukturierung braucht:
  - Migrationsplan
  - Testplan
  - Rollback-Strategie (mind. konzeptionell)

## 8) Reviews (wie du antworten sollst)
Wenn du Code reviewst oder Änderungen vorschlägst:
- Priorisiere **Release-Blocker**: Datenverlust, Security, falsche Gewinner-Auswahl, falsches Sorting, UI Overlaps/Unklarheit.
- Liste Findings mit **Impact**, **Repro/Beispiel**, **Fix-Strategie**, **Test-Absicherung**.
- Zeige auch “tote/duplizierte” Bereiche und nenne Konsolidierungsstrategie.
- UX-Kritik muss konkret sein: Was ist unklar? Wo sind Footguns? Was gehört wohin?

## 9) Tracking-Checklist Pflicht bei größerer Arbeit
Wenn eine Aufgabe mehr als trivial ist, erzeuge/aktualisiere am Ende eine Markdown-Checklist mit Checkboxen:

- Release-Blocker
- GUI/UX Tasks (IA, Layout, Styling, Flow, Accessibility)
- Refactoring Tasks
- IO/Safety Tasks
- Performance Tasks
- Tests/QA Tasks
- Feature-Backlog

Template:
```markdown
## Tracking Checklist (RomCleanup)

### Release-Blocker
- [ ] …

### GUI/UX (WPF/XAML)
- [ ] IA/Navigation klar
- [ ] Spacing/Overlaps/Resize sauber
- [ ] Wizard/Flow: Roots → Optionen → Preview → Confirm → Run → Report/Undo
- [ ] Phase/Progress/Cancel sauber
- [ ] Retro-modern Theme lesbar (ResourceDictionary)

### Core/Engine
- [ ] Duplikate entfernt / Helpers vereinheitlicht
- [ ] Pure functions testbar
- [ ] Determinismus geprüft

### IO/Safety
- [ ] Path traversal / Zip-slip / Reparse Points abgesichert
- [ ] Trash/Audit/Undo konsistent

### Performance
- [ ] Enumeration/Hashing/Regex Hotspots geprüft
- [ ] UI bleibt responsiv

### Tests (keine Alibi-Tests)
- [ ] Unit
- [ ] Integration
- [ ] Regression
- [ ] Negative/Edge/Fuzz

### Backlog
- [ ] …