# UI / UX Layout Redesign – RomCleanup (Romulus)

---

## 1. Executive Verdict

### Kurzbewertung des aktuellen Layouts

Das aktuelle GUI hat eine **solide funktionale Basis**: Synthwave-Theming, vertikale Icon-Sidebar, Status-Leiste, Footer-Action-Bar, Wizard-Overlay, Accessibility-Annotations und ein klares MVVM-Pattern. Die technische Grundlage ist deutlich über dem Niveau eines typischen Hobby-Tools.

**Aber:** Das Layout leidet unter **strukturellen Problemen**, die bei steigender Komplexität exponentiell schmerzhafter werden:

### Hauptprobleme (Kurzfassung)

1. **Flache Navigation mit versteckter Tiefe**: 5 Top-Level-Tabs (Start, Analyse, Setup, Tools, Log) klingen einfach, verbergen aber massiv tiefe Sub-Strukturen — Settings hat 8 Sections, Analyse hat 3, Tools hat 3. Der Nutzer muss sich in verschachtelte Sidebar-Navigationen klicken.

2. **Redundante Bedienelemente**: Run-Button, Cancel, Rollback, ConvertOnly erscheinen im Footer UND in verschiedenen Views. Presets sind auf Start UND Setup dupliziert. Move-CTA taucht auf Start, Setup (DryRun-Banner) UND Analyse auf.

3. **Kein klarer Workflow-Flow**: Ein Anfänger weiss nicht, ob er auf "Start", "Setup" oder den Footer-Run-Button klicken soll. Die Startseite präsentiert einen 6-Schritt-Workflow-Guide, aber die Navigation dazu zeigt auf verschiedene Tabs — das ist verwirrend.

4. **Danger-Actions zu leicht erreichbar**: Der Move-Button (destruktiv!) sitzt im Footer-Bar neben dem Run-Button. Ein versehentlicher Klick kann Dateien verschieben. Obwohl DangerConfirmDialog existiert, ist die räumliche Nähe problematisch.

5. **Post-Run-Erlebnis ist fragmentiert**: Nach einem Run müssen Nutzer aktiv zur "Analyse"-Seite navigieren. Dashboard-KPIs, Dedupe-Entscheidungen, Charts und Move-CTA sind auf einer einzigen Scroll-Seite — ohne klare Priorisierung, was zuerst beachtet werden sollte.

6. **Settings-Überlastung**: 8 Settings-Kategorien (Sortieren, Filter, Sicherheit, Tools, DAT, Profile, Allgemein, System) = zu viele gleichwertige Optionen für Anfänger. Ein Power-User verliert sich in der Linearität.

### Zielrichtung für das Redesign

Romulus sollte wie ein **professionelles Analyse- und Cleaning-Studio** wirken — nicht wie ein Utility mit Optionsseiten. Der Hauptflow muss glasklar sein: **Konfigurieren → Analysieren → Reviewen → Entscheiden → Ausführen → Verifizieren**.

---

## 2. Grösste Layout- und UX-Probleme (Priorisiert)

### Prio 1 – Strukturelle Blocker

| # | Problem | Impact | Betroffene Dateien |
|---|---------|--------|-------------------|
| P1.1 | **Kein linearer Workflow erkennbar** – Start/Setup/Analyse sind gleichwertige Tabs, obwohl sie eine Reihenfolge haben | Anfänger verlieren sich, Power-User müssen ständig hin- und herspringen | MainWindow.xaml, StartView.xaml, SortView.xaml |
| P1.2 | **Footer-Action-Bar enthält destruktive Actions ohne Gate** – Move, Rollback und Run sind direkt nebeneinander, permanente sichtbar | Versehentliches Move ohne vorherige Vorschau möglich | MainWindow.xaml (Footer-Bereich) |
| P1.3 | **Move-CTA auf 3 verschiedenen Seiten** – Start (FlowStep5-Button), Setup (DryRun-Banner), Analyse (Danger-Zone-Border) | Inkonsistenter Einstiegspunkt für die gefährlichste Aktion | StartView.xaml, SortView.xaml, ResultView.xaml |
| P1.4 | **Post-Run-Auto-Navigation fehlt** – nach Run bleibt man auf der ProgressView, muss manuell zur Analyse wechseln | Nutzer wissen nicht, wo die Ergebnisse sind | MainWindow.xaml (IsBusy-Toggle) |

### Prio 2 – Informationsarchitektur

| # | Problem | Impact |
|---|---------|--------|
| P2.1 | **8 Settings-Sections gleichwertig** – Anfänger sehen Sortieroptionen, Filter, Sicherheit, Tools, DAT, Profile, Allgemein, System ohne Priorisierung | Kognitive Überlastung |
| P2.2 | **Dashboard-Metriken mit Charts auf einer Scroll-Seite** – KPIs, Before/After-Chart, Console-Distribution-Bars+Pie, Dedupe-Browser, Move-CTA = ~2000px Scroll | Wichtige Entscheidungen unter dem Fold |
| P2.3 | **Presets-Duplikation** – Intent-Cards auf Start UND Preset-Bar auf Setup sind funktional identisch | Verwirrend: welcher Preset zählt? |
| P2.4 | **Log als Top-Level-Navigation** – Log ist ein Tab gleichwertig zu Analyse/Start, obwohl es ein Debug-Feature ist | Falsche Hierarchie für >90% der Nutzer |

### Prio 3 – Visuelle Hierarchie

| # | Problem | Impact |
|---|---------|--------|
| P3.1 | **Status-Dots in der Top-Bar sind zu subtil** – 12px Ellipsen mit Farbe als einzigem Indikator | Schwer erkennbar, besonders im Light-Theme |
| P3.2 | **Sidebar-Navigation 84px schmal** – Icon + 10px Label darunter, kein Text-Only-Modus, begrenzt auf 6 Items | Erweiterbarkeit eingeschränkt |
| P3.3 | **Footer-Bar zu dominant** – Run/Cancel/Rollback/ConvertOnly + ProgressBar = ~52px hohe permanente Leiste mit 4 grossen Buttons | Nimmt Platz weg, wenn kein Run aktiv |
| P3.4 | **Pie-Chart + Bar-Chart nebeneinander = 620px+ breite Box** – Console-Distribution wird bei <1200px-Fenster abgeschnitten | Responsive-Problem |

---

## 3. Zielbild für das Tool

### Wie das Tool wirken soll

**Romulus soll wirken wie ein „ROM Collection Studio" — ein professionelles, vertrauenswürdiges Analyse- und Management-Werkzeug.**

Nicht wie:
- ❌ ein Utility mit Checkboxen (Cleaner-Feeling)
- ❌ ein Wizard, der den User bevormundet
- ❌ eine IDE mit frei konfigurierbaren Panels (zu geek)

Sondern wie:
- ✅ ein **Medical-Grade Scanner**: Analysiert, zeigt Befunde, gibt Empfehlungen, fragt explizit vor Eingriffen
- ✅ ein **Rechenzentrum-Dashboard**: Status, Health, KPIs auf einen Blick
- ✅ ein **Audit-Tool**: Nachvollziehbar, transparent, reversibel

### UX-Prinzipien

1. **Trust by Default** – Jede Aktion ist standardmässig DryRun/Preview. Destruktive Aktionen erfordern explizite Entscheidung nach Review.
2. **Progressive Disclosure** – Anfänger sehen das Wichtige. Expert-Optionen sind zugänglich, aber nicht im Weg.
3. **Workflow-Klarheit** – Der Nutzer weiss immer: Wo bin ich? Was kommt als Nächstes? Was passiert, wenn ich klicke?
4. **Post-Run-Fokus** – Nach einem Run gehört der gesamte Bildschirm dem Ergebnis. Keine Navigation nötig.
5. **Danger-Zone-Isolation** – Destruktive Aktionen sind nie „nebenbei". Sie haben eigene Bestätigungsschritte mit Kontext.
6. **Information Density Control** – Power-User können Density hochdrehen. Anfänger bekommen Whitespace und Erklärungen.

---

## 4. Empfohlene Informationsarchitektur

### Hauptbereiche (4 Kernzonen)

```
┌────────────────────────────────────────────────────────────┐
│ 1. COMMAND CENTER (Top)                                     │
│    Globaler Status, Health-Indicator, Quick-Profile         │
├─────────┬──────────────────────────────────────────────────┤
│ 2. NAV  │ 3. WORKSPACE (Zentral)                           │
│ (Links) │    Der aktive Arbeitsbereich                      │
│         │    - je nach Phase unterschiedlich                │
│         │    - scrollbar, fokussiert                        │
│         │                                                   │
│         │                                                   │
│         │                                                   │
├─────────┴──────────────────────────────────────────────────┤
│ 4. ACTION DOCK (Bottom, kontextsensitiv)                    │
│    Primäre Aktion des aktuellen Kontexts + Fortschritt      │
└────────────────────────────────────────────────────────────┘
```

### Navigation (empfohlen)

Statt 5 gleichwertiger Tabs → **3 Phasen-Gruppen + 1 Utility-Bereich**:

| Gruppe | Seiten | Zweck |
|--------|--------|-------|
| **Vorbereitung** | Startseite, Konfiguration | Roots, Regionen, Optionen, Profile |
| **Analyse** | Dashboard, Dedupe-Browser, Reports | Post-Run-Ergebnisse, Entscheidungen |
| **Aktionen** | Move/Apply, Rollback, Export | Destruktive Operationen |
| **System** | Tools, Settings, Log, Audit | Erweiterte Konfiguration |

### Welche Infos gehören wohin

| Information | Platzierung | Begründung |
|-------------|-------------|------------|
| Health-Score, Ready-Status | Top-Bar | Immer sichtbar |
| ROM-Roots | Startseite / Konfiguration | Basis-Setup |
| Region-Wahl, Presets | Konfiguration | Wiederkehrende Einstellung |
| KPIs (Games, Dupes, Junk, Winners) | Dashboard (Post-Run) | Erst nach Analyse relevant |
| Dedupe-Entscheidungen (TreeView) | Eigene Sub-View unter Analyse | Detailansicht, kein Dashboard-Element |
| Move-CTA | NUR in dedizierter Danger-Zone-Section | Isolation von normalen Aktionen |
| Charts (Console Distribution) | Dashboard, aber collapsible | Sekundär, optisch nett, nicht entscheidungsrelevant |
| Error-Summary | Prominent über Dashboard-KPIs | Erste Info nach Run: „Gab es Probleme?" |
| Log | Unterseite / Sidebar-Panel | Debug-Zweck, nicht für Normalnutzer |
| Tool-Pfade, DAT-Config | System-Settings | Einmalige Konfiguration |

---

## 5. Design-Mockup A: „Guided Studio"

### Name
**Guided Studio** – Workflow-first mit Dashboard-Kern

### Zielgruppe
Anfänger bis Fortgeschrittene. Nutzer, die sich nicht in ROM-Cleanup eingelesen haben und dem Tool vertrauen wollen.

### Layout-Struktur

```
┌────────────────────────────────────────────────────────────────────┐
│ TOP: Logo │ Health●Ready●Tools●DAT │ Profile-Switcher │ ⚙Settings │
├────────┬───────────────────────────────────────────────────────────┤
│ PHASES │                                                           │
│ (left) │                                                           │
│        │            ZENTRALE WORKSPACE-FLÄCHE                      │
│ ① Home │                                                           │
│ ② Conf │     Inhalt wechselt je nach ausgewählter Phase            │
│ ③ Run  │                                                           │
│ ④ Res  │                                                           │
│ ⑤ Act  │                                                           │
│        │                                                           │
│ ───    │                                                           │
│ ⚒Tools│                                                           │
│ 📋Log  │                                                           │
├────────┴───────────────────────────────────────────────────────────┤
│ FOOTER: [▶ Preview] [Progress ████░░ 67%] [Phase: Dedupe]         │
└────────────────────────────────────────────────────────────────────┘
```

### Navigationsmodell
- **Vertikale Sidebar mit Phasen-Nummerierung**: ① Home → ② Config → ③ Run → ④ Results → ⑤ Actions
- Phasen werden progressiv freigeschaltet: Config erst nach mindestens 1 Root, Results erst nach Run
- Unter dem Divider: Utility-Links (Tools, Log) — optisch abgestuft
- Settings öffnet als Overlay/Flyout (nicht als eigene Seite)

### Hauptseiten

**① Home (Startseite)**
- Hero-Drop-Zone für Roots (wie jetzt, aber mit Size-Estimation: „~1200 ROMs erkannt")
- Intent-Cards (Aufräumen / Sortieren / Konvertieren) — rufen Preset auf UND navigieren zu Config
- Letzter-Run-Summary-Card (klein, kompakt: „Vor 2h: 450 Games, 120 Dupes gefunden")
- Keine doppelten Buttons, kein Flow-Guide

**② Config (Konfiguration)**
- Presets prominent oben (Segmented Control)
- Simple/Expert-Toggle
- Simple: Region, Aktionen (3 Checkboxen), fertig
- Expert: Region-Details, Filter, DAT, Sortieroptionen
- Run-Button am unteren Rand dieses Panels: „Preview starten (F5)"

**③ Run (Live-Fortschritt)**
- Automatisch angezeigt während Run (wie jetzt ProgressView)
- Pipeline-Stepper horizontal
- Live-Metrics: Files scanned, Current file, ETA
- Live-Ticker (Warnings/Events)
- Cancel-Button prominent

**④ Results (Dashboard)**
- Auto-Navigation nach Run-Ende direkt hierhin
- Oben: Run-Status-Banner (Success/Warning/Error)
- 4 KPI-Karten (Games, Dupes, Junk, Health)
- Darunter Tabs: „Übersicht" / „Dedupe-Details" / „Report"
  - Übersicht: Secondary Metrics + Console-Distribution
  - Dedupe-Details: TreeView-Browser (Vollbreite)
  - Report: WebView2-Vorschau

**⑤ Actions (Danger Zone)**
- NUR erreichbar nach erfolgreichem Preview-Run
- Move/Apply-CTA mit rotem Banner + Konsequenz-Text
- Rollback-Button mit letztem Sidecar-Info
- Export-Buttons (HTML, CSV, JSON)

### Run-Flow
Config → F5 → Live-Progress → Auto-Switch zu Results → Optional: Actions

### Danger-Zones / Confirm-Flows
- Move/Apply NUR auf ⑤ Actions, nie im Footer
- Rollback-Button NUR auf ⑤ Actions (nach Move) oder im Results-Banner (nach Move)
- Footer enthält NUR: Run/Cancel + Fortschritt. Keine destruktiven Aktionen.
- Danger-Confirm-Dialog bleibt, aber kontextreicher: zeigt Move-Count, Ziel-Trash, Rollback-Info

### Vorteile
- Klarer linearer Workflow: unmissverständlich, was als Nächstes kommt
- Absolute Trennung von Analyse und Aktion
- Anfänger können nicht in Danger-Zones „reinrutschen"
- Post-Run-Auto-Navigation eliminiert „Wo ist mein Ergebnis?"
- Settings als Flyout statt als eigene Seite → weniger Tab-Clutter

### Nachteile
- Power-User empfinden Gating ggf. als einschränkend
- Phasen-Nummerierung kann bei wiederkehrenden Runs etwas steif wirken
- Settings-Flyout muss sauber implementiert werden (kein leeres Sheet)
- Mehr State-Management für Phase-Gating nötig

### Empfehlung
**Am besten für: Erste Nutzung, gelegentliche Nutzer, sicherheitskritische Umgebungen.**

---

## 6. Design-Mockup B: „Power Workspace"

### Name
**Power Workspace** – Mehrspaltig, Inspector-getrieben, Batch-fähig

### Zielgruppe
Power-User, ROM-Collector-Veteranen, Nutzer mit >10.000 ROMs, Nutzer die regelmässig Cleanup-Runs machen.

### Layout-Struktur

```
┌─────────────────────────────────────────────────────────────────────┐
│ TOP: Logo │ Status●● │ Mode: [Simple|Expert] │ Profile │ 🔍Search │
├──────┬──────────────────────────────┬───────────────────────────────┤
│ NAV  │  MAIN PANEL (Liste/Grid)     │  INSPECTOR (Detail)           │
│      │                              │                               │
│ 🏠   │  ┌──────────────────────┐    │  Game: Super Mario World     │
│ ⚙    │  │ ROM-Liste / DataGrid │    │  Key: SuperMarioWorld        │
│ 📊   │  │ mit Sortierung +     │    │  Region: EU (Score: 95)      │
│ 🔧   │  │ Filterzeile          │    │  Format: .zip (Score: 80)    │
│      │  │                      │    │  Version: Rev1 (Score: 10)   │
│      │  │ [✓] Mario (EU).zip   │    │  DAT: ✓ Verified             │
│      │  │ [✗] Mario (JP).zip   │    │  Decision: KEEP              │
│      │  │ [✗] Mario (US).zip   │    │                               │
│      │  └──────────────────────┘    │  [Override: Keep this instead]│
│      │                              │  [Exclude from Run]           │
├──────┴──────────────────────────────┴───────────────────────────────┤
│ FOOTER: [▶ Run] [████████░░ 83% Dedupe] │ 4500 files │ 12.4 GB    │
└─────────────────────────────────────────────────────────────────────┘
```

### Navigationsmodell
- Schmale Sidebar (Icons only, ~48px), 4 Bereiche: Home, Config, Analysis, Tools
- Kein Phase-Gating — alles immer zugänglich
- Hauptfokus liegt auf dem **3-Column-Layout**: Nav | Main | Inspector
- Inspector zeigt Kontext zum selektierten Element (ROM, Gruppe, Konsole)

### Hauptseiten

**Home/Dashboard**
- Kompakte KPIs in Cards (2 Zeilen, 4 Karten)
- Console-Distribution als horizontale Mini-Bars (kein Pie-Chart)
- Last-Run-Info kompakt
- Quick-Action-Buttons: Run, ConvertOnly, Open Report

**Config**
- 2-Column-Layout: Links Quick-Config (Roots, Preset, Region), Rechts Detail-Config
- Kein scrollende Einzelseite, sondern thematische Tabs innerhalb der Config

**Analysis (Post-Run)**
- DataGrid als Haupt-Element: Alle ROMs, sortierbar nach GameKey, Region, Score, Decision, Console
- Filter-Bar über dem Grid (nach Console, Region, Decision, DAT-Status)
- Inspector-Panel rechts: Details zum selektierten ROM/Gruppe
- Dedupe-Gruppen als gruppierte Rows im Grid (expandable)
- Move-Decision kann pro Gruppe im Inspector überschrieben werden

**Tools**
- Command-Palette-Style: Suchfeld + Kategorien
- Jedes Tool als Card mit Status + Execute-Button

### Run-Flow
Kein linearer Zwang. User konfiguriert, drückt Run, sieht Ergebnis im Analysis-Grid.
Move-Aktion über Button im Analysis-Footer oder im Inspector für selektierte Items.

### Ergebnisdarstellung
- DataGrid mit ~15 Spalten (togglebar): FileName, GameKey, Console, Region, Score, Decision, DatMatch, Size, Format
- Gruppierung nach GameKey (expandable rows)
- Sorting + Filtering inline
- Inspector zeigt Score-Breakdown

### Status/Progress
- Footer-Bar zeigt Phase + Prozent + Dateiname
- Bei Run: Grid wird live gefüllt (Streaming-UI)
- Kein Fullscreen-Progress-Overlay, sondern inline

### Danger-Zones / Confirm-Flows
- Move nur über expliziten Button im Analysis-Footer (nicht im NAV)
- Button erscheint erst, wenn Run-Result da ist
- Inline-Confirmation: „Move 234 files to trash? [Cancel] [Move]" als Banner über dem Grid
- Rollback-Button erscheint nach Move im gleichen Banner

### Vorteile
- Maximale Effizienz für erfahrene Nutzer
- DataGrid + Inspector = bekanntes Pattern (wie Explorer, Outlook, IDE)
- Schnelle Batch-Arbeit: Filter → Select → Override → Execute
- Kein Seiten-Hopping nötig
- Responsive: Inspector kann collapsed werden

### Nachteile
- **Hohe Einstiegshürde für Anfänger** — DataGrid mit 15 Spalten ist einschüchternd
- Komplexere Implementierung (Virtualized DataGrid, Inline-Editing, Inspector-Binding)
- Simple/Expert-Toggle schwieriger: eher Spalten-Sichtbarkeit als Layout-Switch
- Wizard-Flow passt nicht in dieses Modell

### Empfehlung
**Am besten für: Wiederkehrende Power-User, grosse ROM-Collections, Batch-Workflows.**

---

## 7. Design-Mockup C: „Hybrid Dashboard"

### Name
**Hybrid Dashboard** – Dashboard-first mit kontextabhängiger Rechte-Spalte

### Zielgruppe
Alle Nutzertypen. Kompromiss zwischen Guided Studio und Power Workspace.

### Layout-Struktur

```
┌──────────────────────────────────────────────────────────────────────────┐
│ TOP: Logo │ ●Ready ●Tools ●DAT │ Profile │ [Simple ◉ Expert] │ F1 Help │
├──────┬─────────────────────────────────────────┬─────────────────────────┤
│ NAV  │  MAIN CONTENT                           │  CONTEXT PANEL          │
│      │                                         │  (kontextabhängig)      │
│ 🏠   │  Vor Run:                               │                         │
│ 📊   │   Welcome + Drop Zone + Intent Cards    │  Quick Config           │
│ ⚙   │                                         │  Region, Options        │
│ 🔧   │  Nach Run:                              │  Preset Buttons         │
│      │   Dashboard: KPI-Cards + Charts         │                         │
│      │   + Dedupe-Accordion                    │  Nach Run:              │
│      │                                         │  Score-Details          │
│      │  Während Run:                           │  File Inspector         │
│      │   Progress-Stepper + Live-Log           │  Export Actions         │
│      │                                         │                         │
├──────┴─────────────────────────────────────────┴─────────────────────────┤
│ SMART ACTION BAR: [▶ Preview F5] │ Progress │ Phase                      │
│ nach Run: [📊 Report] [⚠ Move 234 Files → Trash] │ [↩ Rollback]         │
└──────────────────────────────────────────────────────────────────────────┘
```

### Navigationsmodell
- **4 Sidebar-Tabs**: Home, Analysis, Config, Tools
- Home und Analysis teilen sich den gleichen Workspace-Bereich: vor Run = Home-Zustand, nach Run = Dashboard-Zustand
- Config als eigene Seite (vereinfacht gegenüber dem IST: 4 Sections statt 8)
- **Context Panel (rechts)** zeigt kontextabhängige Details
- Action Bar passt sich dem Zustand an

### Hauptseiten

**Home → Dashboard (Zustandswechsel)**
- *Vor Run*: Welcome-Screen mit Drop-Zone, Intent-Cards, Quick-Config im Context-Panel
- *Nach Run*: Automatischer Switch zu Dashboard-Ansicht: KPIs oben, Console-Distribution, Error-Summary
- *Transition*: Sanfte Animation von Welcome zu Dashboard

**Analysis (Deep Dive)**
- Dedupe-Browser (TreeView, Vollbreite)
- Report-Preview (WebView2)
- Error-Details
- Context Panel zeigt Inspector für selektiertes Element

**Config (Vereinfacht)**
- 4 Hauptkategorien statt 8:
  - **Basis** (Roots, Region, Presets, Mode)
  - **Erweitert** (Filter, DAT, Sicherheit, Sort-Options)
  - **Tools** (Pfade, Auto-Detect)
  - **System** (Profils, Import/Export, Logging, Scheduler)
- Horizontal-Tabs statt Sidebar in Settings

**Tools**
- Wie Mockup A, aber kompakter: Quick-Access oben, All-Tools unten

### Run-Flow
1. Home/Welcome: Roots konfigurieren → Intent wählen → F5 Preview
2. Inline-Progress (Home wird zum Progress-View)
3. Automatischer Switch zu Dashboard-Zustand
4. Review im Dashboard + Inspector
5. Move über Smart Action Bar (kontextuelle CTA)
6. Report/Rollback über Action Bar oder Analysis-Seite

### Ergebnisdarstellung
- Dashboard auf Home: 4 KPI-Cards + Error-Banner + Console-Distribution-Bars
- Detail-Analyse: Dedupe-Browser + Inspector im Context Panel
- Smart Action Bar zeigt nach Run kontextuelle Aktionen: Report, Move, Rollback

### Status/Progress
- Home wird inline zum Progress-View (wie jetzt, aber ohne Seiten-Switch)
- Pipeline-Stepper im Main-Panel
- Footer Action Bar zeigt Progress + Phase
- Nach Completion: Dashboard blendet ein, Stepper zeigt ✓

### Danger-Zones / Confirm-Flows
- Smart Action Bar zeigt Move-Button nur nach DryRun mit orange/rotem Styling
- Move-Button im Footer ist kleiner als Preview-Button und hat ⚠-Icon
- Klick auf Move → Inline-Expansion im Footer: „234 Dateien → Trash verschieben? [Abbrechen] [▶ Verschieben]"
- Nach Move: Footer zeigt Rollback-Button + Success-Banner
- Kein Move-Button auf anderen Seiten

### Vorteile
- **Kontextabhängiger Workspace**: Gleicher Platz zeigt das Richtige zur richtigen Zeit
- Home→Dashboard-Transition vermeidet Tab-Überladung
- Context Panel als universeller Inspector
- Smart Action Bar eliminiert Redundanz
- Funktioniert für Anfänger (guided Home) UND Power-User (Analysis + Inspector)
- Weniger Top-Level-Tabs (4 statt 5+)

### Nachteile
- Zustandsabhängiges Main-Panel braucht solides State-Management
- „Home wird zum Dashboard" könnte bei erstem Mal überraschen
- Context Panel + Main Panel auf <1200px schwierig
- Smart Action Bar muss sorgfältig kontextuell gesteuert werden

### Empfehlung
**Am besten für: Generell empfehlenswertes Modell — Kompromiss zwischen Klarheit und Power.**

---

## 8. Vergleich der Mockups

### Matrix

| Kriterium | A: Guided Studio | B: Power Workspace | C: Hybrid Dashboard |
|-----------|-------------------|---------------------|---------------------|
| **Anfänger-tauglich** | ★★★★★ | ★★☆☆☆ | ★★★★☆ |
| **Power-User** | ★★★☆☆ | ★★★★★ | ★★★★☆ |
| **Workflow-Klarheit** | ★★★★★ | ★★★☆☆ | ★★★★☆ |
| **Danger-Zone-Sicherheit** | ★★★★★ | ★★★☆☆ | ★★★★☆ |
| **Post-Run-Erlebnis** | ★★★★☆ | ★★★★★ | ★★★★★ |
| **Informationsdichte** | ★★☆☆☆ | ★★★★★ | ★★★★☆ |
| **Implementierungsaufwand** | ★★★☆☆ (mittel) | ★★☆☆☆ (hoch) | ★★★☆☆ (mittel-hoch) |
| **Nutzung des vorhandenen Codes** | ★★★★☆ | ★★☆☆☆ | ★★★★☆ |

### Welches für Anfänger am besten
**Mockup A (Guided Studio)** – klarer linearer Flow, Phasen-Gating verhindert Fehler

### Welches für Power-User am besten
**Mockup B (Power Workspace)** – DataGrid + Inspector = maximale Kontrolle

### Welches insgesamt am besten passt
**Mockup C (Hybrid Dashboard)** – vereint Stärken beider Ansätze

### Empfehlung

**→ Mockup C (Hybrid Dashboard) als Hauptrichtung implementieren.**

Gründe:
1. Das bestehende Layout lässt sich am organischsten in Richtung C transformieren
2. Context Panel + Smart Action Bar lösen die 3 grössten Probleme (Redundanz, Danger-Zone, Post-Run-Fragmentierung)
3. Simple/Expert-Toggle kontrolliert die Rechte-Spalte: Simple = Quick-Config, Expert = Full Inspector
4. Die bestehende Synthwave-Ästhetik passt perfekt zum Dashboard-Look

**Ergänzende Elemente aus A und B einfliessen lassen:**
- Aus A: Phase-Gating für den allerersten Run (First-Run-Wizard bleibt)
- Aus A: Settings als 4 vereinfachte Sections statt 8
- Aus B: DataGrid-View als optionale Ansicht im "Analysis"-Tab für Power-User

---

## 9. Konkrete Refactoring-Empfehlungen für WPF

### 9.1 Layout-Struktur

| Aktuell | Empfohlen | Migration |
|---------|-----------|-----------|
| `MainWindow.xaml` mit DockPanel + Grid(2col) | `MainWindow.xaml` mit DockPanel + Grid(3col): Nav \| Main \| ContextPanel | ContextPanel-Column hinzufügen, Width="280", collapsible |
| Footer mit 4 grossen Buttons | **Smart Action Bar**: Kontextuelle Buttons, kleiner, Inline-Confirm | Footer-Section refactoren: Buttons per Visibility-Bindings steuern |
| 5 Views via String-Visibility | 4 Views: HomeView, AnalysisView, ConfigView, ToolsView | StartView→HomeView umbenennen, SortView→ConfigView |
| SettingsView als eigene Seite mit 8 Sections | **ConfigView** mit 4 Tabs (Basis, Erweitert, Tools, System) | SettingsView-Inhalte in ConfigView integrieren |
| ResultView mit Sidebar (Dashboard/Log/Report) | **AnalysisView** mit Tabs (Dashboard/Dedupe/Report) + Inspector | Log wird zur Bottom-Panel-Section oder eigener Tab |

### 9.2 Views / UserControls (neu zu erstellen)

| UserControl | Zweck | Ersetzt |
|-------------|-------|---------|
| `HomeView.xaml` | Vor-Run: Welcome + Drop-Zone + Intent-Cards; Nach-Run: Dashboard-KPIs | StartView.xaml |
| `AnalysisView.xaml` | Deep-Dive: Dedupe-Browser, Report-Preview, Error-Details | ResultView.xaml |
| `ConfigView.xaml` | Vereinigte Konfiguration: Roots + Options + Filters + DAT | SortView.xaml + SettingsView.xaml |
| `ContextPanel.xaml` | Rechte Spalte: Quick-Config / Score-Inspector / Details | Neu |
| `SmartActionBar.xaml` | Footer: kontextuelle Actions + Progress | Footer in MainWindow.xaml |
| `InlineConfirmBanner.xaml` | Inline-Bestätigung für Move/Rollback in der Action Bar | DangerConfirmDialog.xaml (teilweise) |

### 9.3 ViewModels

| Aktuell | Problem | Empfohlen |
|---------|---------|-----------|
| `MainViewModel.cs` (1 Datei + 4 Partials) | Monolith: ~120 Properties + ~30 Commands | Aufteilen: `HomeViewModel`, `AnalysisViewModel`, `ConfigViewModel` als Child-VMs |
| `MainViewModel.Settings.cs` | 8 Settings-Sections in einem Partial | In `ConfigViewModel` konsolidieren |
| `MainViewModel.Filters.cs` | Filter-Logik nahe am Settings-Code | In `ConfigViewModel.Filters.cs` verschieben |
| `MainViewModel.RunPipeline.cs` | Run-Logik ist bereits gut isoliert | Bleibt als `RunPipelineViewModel` oder in `MainViewModel.RunPipeline.cs` |
| `SetupViewModel`, `ToolsViewModel`, `RunViewModel` | Existieren, sind aber dünn | Erweitern: SetupVM→ConfigVM, RunVM bleibt |
| Fehlt: `ContextPanelViewModel` | Inspector/Context hat kein eigenes VM | Neu erstellen: Shows details of selected item |

### 9.4 Styles / Themes

| Empfehlung | Detail |
|------------|--------|
| **Smart Action Bar Style** | Neues Style-Template für kontextuelle Footer-Buttons: kleiner, kompakter, Inline-Confirm |
| **Context Panel Styles** | Inspector-Styles: Label/Value pairs, Score-Bars, Decision-Badges |
| **Dashboard KPI Cards** verbessern | Hover-State, Click-to-expand, Tooltip mit Formel/Details |
| **DataGrid-Styles** | Alternating rows, Sorting-Indicators, Filter-Row — für Analysis-View |
| **Inline-Confirm-Banner** | Error-Bg oder Danger-Bg mit Slide-in-Animation, Buttons rechts |
| **Tab-Control-Style** | Horizontale Tabs für Config-Sections (statt Settings-Sidebar) |
| **Density-Token** | `SpacingDensity` = Compact/Normal/Comfortable, kontrolliert globale Margins |

### 9.5 Navigation

| Aktuell | Empfohlen |
|---------|-----------|
| `RadioButton GroupName="Nav"` mit String-Converter | Enum-basierte Navigation mit `NavigationService` |
| `SelectedNavTag` (string) | `ActivePage` (enum: Home, Analysis, Config, Tools) |
| Auto-Switch via IsBusy→ProgressView | Auto-Switch via `RunState`→ProgressOverlay, dann → Analysis on Complete |
| Keine Breadcrumb/Back-History | `NavBackCommand` + `NavForwardCommand` bereits vorhanden, aber ohne UI-Anzeige |

### 9.6 Panels / Grids / Controls

| Element | Aktuell | Empfohlen |
|---------|---------|-----------|
| Dedupe-Browser | TreeView (max 400px) | DataGrid mit gruppierten Rows ODER TreeView Fullwidth |
| Console Distribution | Bars + 620px Pie nebeneinander | Bars only (collapsible), Pie on click/expand |
| Settings | 8 StackPanels behind Visibility-Switches | TabControl mit 4 Tabs |
| Root-Management | ListBox + Add/Remove Buttons | Drop-Zone + Compact-List mit ×-Button je Root |
| Region-Checkboxen | WrapPanel mit 4 + Expander für 12 mehr | Segmented-Control für Haupt-Regionen + Expander |

### 9.7 Was entfernt / vereinfacht / zusammengeführt werden sollte

| Element | Aktion | Grund |
|---------|--------|-------|
| **Footer-Run/Cancel/Move/ConvertOnly/Rollback** | → Smart Action Bar (kontextuell) | Zu viele gleichzeitige Buttons |
| **Intent-Cards auf StartView** | → Behalten, aber NICHT gleichzeitig Presets auf SortView | Duplikation entfernen |
| **6-Schritt-Flow-Guide auf StartView** | → Entfernen oder in Wizard integrieren | Redundant zum Wizard + Navigation |
| **Log als Top-Level-Nav-Item** | → Unter Analysis oder als Toggle-Panel | Falsche Hierarchie |
| **Settings-Sections: 8 → 4** | Zusammenführen: Sortierung+Filter=Basis, Sicherheit+DAT=Erweitert | Kognitive Entlastung |
| **Move-CTA auf 3 Seiten** | → NUR in Action Bar | Eindeutiger Einstiegspunkt |
| **Presets doppelt (Start + Setup)** | → Nur in Config, Intent-Cards rufen Config auf | Keine widersprüchlichen Presets |
| **Quick-Profile-Dropdown in Top-Bar** | Behalten, aber Position prüfen — rechts neben Health-Dots passt | OK |

---

## 10. Empfohlene nächste Schritte

### Phase 1: Foundation (Architektur-Vorbereitung)
1. **ContextPanel-Infrastructure schaffen**: Neues `ContextPanel.xaml` + `ContextPanelViewModel`. Grid-Column in MainWindow hinzufügen (initially collapsed).
2. **Navigation refactoren**: `SelectedNavTag` (string) → `ActivePage` (enum). Seitenanzahl von 5 auf 4 reduzieren.
3. **Footer zur Smart Action Bar**: Kontextabhängige Buttons. Move/Rollback nur sichtbar wenn relevant.

### Phase 2: View-Konsolidierung
4. **StartView + ResultView → HomeView**: Zustandsabhängiger Content (vor/nach Run).
5. **SortView + SettingsView → ConfigView**: Tabs statt 2 separate Views mit doppelter Settings-Sidebar.
6. **Settings von 8 auf 4 Sections** zusammenführen.

### Phase 3: Post-Run-Experience
7. **Auto-Navigation nach Run**: RunState.Completed → ActivePage = Home (Dashboard-Zustand).
8. **Inline-Confirm-Banner** für Move in der Smart Action Bar.
9. **Inspector/Context-Panel** für Dedupe-Details befüllen.

### Phase 4: Polish
10. **Density-Toggle** (Compact/Normal) als globalen Token.
11. **Responsive Context-Panel** (Collapse unter 1200px Fensterbreite).
12. **Keyboard-Navigation** für Smart Action Bar + Context Panel.
13. **Accessibility-Review** des neuen Layouts (Screen-Reader-Flow, Focus-Order).

### Risiken
- **Rollback-Fähigkeit**: Alle Layout-Änderungen sollten inkrementell sein → nie das aktuelle funktionierende GUI zerstören, bevor das neue steht
- **Test-Parität**: Nach jedem Schritt müssen die bestehenden >2500 Tests weiterhin grün sein
- **MVVM-Disziplin**: Context Panel braucht saubere Bindings, keine Code-Behind-Hacks

---

## Anhang: Konkrete Antworten auf die Fragen

### Welche Informationen gehören auf die Startseite?
- Drop-Zone für Roots
- Intent-Selection (Aufräumen/Sortieren/Konvertieren)
- Health/Ready-Status
- Letzter-Run-Summary (kompakt)
- Quick-Start-Button (Preview)

### Welche Informationen gehören nur in Detailansichten?
- Dedupe-Entscheidungen (TreeView/DataGrid)
- Score-Breakdowns (Region/Format/Version einzeln)
- DAT-Match-Details
- Console-Distribution-Charts
- Error-Details + Stack-Traces
- GameKey-Preview-Tool

### Welche KPIs sollten prominent sein?
- **Games** (erkannte Spiele) — primär
- **Dupes** (erkannte Duplikate) — primär
- **Junk** (erkannter Junk) — primär
- **Health Score** (%) — primär
- **Ready-Status** (●) — immer in Top-Bar

### Welche KPIs sollten nur sekundär sichtbar sein?
- Winners (abgeleiteter Wert von Games - Dupes)
- DAT-Hits (nur relevant wenn DAT aktiv)
- Dedupe-Rate (%)
- Duration
- Files scanned
- Saved Bytes

### Welche Aktionen gehören ganz oben?
- Preview/Run (F5)
- Cancel (Escape)
- Profile-Switch

### Welche Aktionen gehören absichtlich tiefer?
- **Move/Apply** → ausschliesslich in der Smart Action Bar nach Preview, mit Inline-Confirm
- **Rollback** → nur nach Move sichtbar
- **Profile-Delete** → nur in Config/System
- **Config-Import/Export** → nur in Config/System
- **Log-Clear** → nur in Log-Panel

### Wie sollten ConvertOnly, Repair, Dedupe, Restore und Report visuell getrennt sein?
- **ConvertOnly**: Eigener Intent auf der Home-Seite (grüne Karte), eigener Modus im Footer
- **Dedupe**: Kernfunktion, immer aktiv (ausser ConvertOnly), Results im Dashboard
- **Repair**: Unter Tools (Command-Palette-Style)
- **Restore/Rollback**: Nur nach Move/Apply sichtbar, in Smart Action Bar
- **Report**: Button in Smart Action Bar nach Run + eigener Tab in Analysis

### Sollte das Tool eher wirken wie ein...
**→ Studio / Analyst-Tool**

- Nicht wie ein „Cleaner" (zu simpel, schafft kein Vertrauen)
- Nicht wie ein „Wizard" (zu einschränkend für Power-User)
- Nicht wie eine „IDE" (zu generisch, zu viel Customization)
- Nicht wie eine „Repair Workbench" (nur ein Aspekt)

**Romulus sollte wirken wie ein „ROM Collection Health Studio"**: Man öffnet es, sieht den Zustand seiner Sammlung, analysiert, reviewt Entscheidungen, und führt gezielt aus — mit voller Transparenz und Reversibilität.
