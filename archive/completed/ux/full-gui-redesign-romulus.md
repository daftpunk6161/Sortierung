# Full GUI Redesign – Romulus

> **Version**: 1.0  
> **Datum**: 2026-03-22  
> **Autor**: Lead UX / UI Architect  
> **Plattform**: WPF / .NET 10 / Windows Desktop  
> **Status**: Konzept – bereit für Figma-Umsetzung und WPF-Implementierung

---

## 1. Executive Verdict

### Hauptprobleme des aktuellen UI

| # | Problem | Schweregrad | Impact |
|---|---------|-------------|--------|
| 1 | **Flache 4-Tab-Navigation** verbirgt 8+ Settings-Kategorien, 3 Analyse-Unterseiten und Tools hinter einer einzigen Sidebar-Ebene | Hoch | Nutzer verlieren Orientierung, Features werden nicht entdeckt |
| 2 | **Danger Actions zu nah an Routine-Actions** – Move-Button sitzt im SmartActionBar direkt neben Preview | Hoch | Fehlbedienungsrisiko bei destruktiven Operationen |
| 3 | **Post-Run-Erlebnis fragmentiert** – nach einem Run muss der Nutzer manuell zur Analyse navigieren | Mittel | Ergebnisse werden nicht sofort wahrgenommen |
| 4 | **Settings-Überflutung** – 8 Kategorien in einer Sidebar-Liste ohne visuelle Hierarchie | Mittel | Konfiguration wirkt überfordernd |
| 5 | **Kein klares visuelles Leitsystem** – Run-Status, Safety-Gates, DAT-Status und Tool-Status nutzen nur kleine Dots im Header | Mittel | Systemzustand ist schwer ablesbar |
| 6 | **ContextPanel inhaltlich zu dünn** – zeigt nur 2-3 Zeilen je nach Tab, verschwendet 280px | Mittel | Wertvoller Platz nicht genutzt |
| 7 | **StartView, SortView und SettingsView inhaltlich teilweise redundant** – Roots werden in Start UND Setup gemanaged | Niedrig | Inkonsistente Source of Truth für Nutzer |
| 8 | **Theme-System funktional, aber nur 3 Themes** – kein Retro/CRT-Theme, kein Theme-Switcher in der UI prominent | Niedrig | Visuelle Identity nicht voll ausgeschöpft |

### Zielrichtung des Redesigns

Romulus wird von einem **Utility mit Optionen** zu einem **professionellen ROM-Management-Studio** transformiert:

- **Workflow-orientiert** statt Feature-orientiert
- **Visuell markant** durch Synthwave/Retro-futuristische Ästhetik
- **Safety-by-Design** mit klarer Trennung von Preview/Review/Execute
- **Skalierbar** für neue Features ohne Layout-Umbau
- **Theme-fähig** mit 5 umschaltbaren Themes als First-Class-Feature

### Kurzfazit

Das Redesign behält die solide technische Basis (MVVM, CommunityToolkit, Theme-Tokens), ersetzt aber Layout, Navigation, Informationsarchitektur und visuelles System vollständig. Alle bestehenden Funktionen bleiben erhalten. Kein Feature wird entfernt – aber alles wird besser organisiert, sicherer zugänglich und visuell hochwertiger präsentiert.

---

## 2. Designprinzipien

### Kernprinzipien für Romulus

| Prinzip | Beschreibung | Umsetzung |
|---------|-------------|-----------|
| **Safety First** | Destruktive Aktionen sind nie einfacher erreichbar als sichere | DryRun → Review → Confirm → Execute Pipeline; Danger Zone isoliert |
| **Progressive Disclosure** | Hauptfunktionen sofort sichtbar, Details erst bei Bedarf | Simple/Expert-Mode auf jeder Ebene, nicht nur global |
| **Workflow statt Feature-Menü** | Nutzer folgen einem Pfad, nicht einer Feature-Liste | Phase-System: Configure → Preview → Review → Act → Report |
| **Deterministic Feedback** | Systemzustand ist jederzeit klar und konsistent | Permanente Health-Bar, Live-Status-Panel, klar codierte Statusfarben |
| **Desktop-native Power** | Keyboard-first, Split-Views, Resizable Panels, Drag&Drop | Global Command Bar, Shortcuts, Splitter, Docking-Zones |
| **Retro-futuristische Eleganz** | Synthwave/Cyberdeck-Ästhetik ohne Kitsch | Neon-Akzente auf dunklem Grund, Glow-Effekte zurückhaltend, Grid-Lines als Stilmittel |
| **Kein visuelles Rauschen** | Jedes Element verdient seinen Platz | Whitespace als Strukturelement, konsistentes 8px-Grid, keine redundanten Borders |
| **Unmissverständliche Hierarchie** | Primary > Secondary > Tertiary Actions, immer | Button-Sizing, Farbcodierung, Position signalisieren Wichtigkeit |

### Accessibility-Prinzipien

- WCAG AA für alle Themes (AAA für High-Contrast)
- Alle interaktiven Elemente per Tab erreichbar
- Fokus-Ringe immer sichtbar (3px minimum)
- Keine Information nur über Farbe kommuniziert (immer Icon + Text)
- AutomationProperties für alle interaktiven Elemente
- Mindest-Touch-Target: 44×44px (auch Desktop, für Präzision)

---

## 3. Neue Informationsarchitektur

### 3.1 Hauptbereiche (5 statt 4)

```
┌──────────────────────────────────────────────────────────────────┐
│                    ROMULUS – ROM Management Studio                 │
├──────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌─────────┐  ┌───────────┐  ┌──────────┐  ┌───────┐  ┌──────┐ │
│  │ MISSION │  │  LIBRARY  │  │  CONFIG  │  │ TOOLS │  │ SYS  │ │
│  │ CONTROL │  │  & REVIEW │  │          │  │       │  │      │ │
│  └─────────┘  └───────────┘  └──────────┘  └───────┘  └──────┘ │
│                                                                    │
│  Home /       Ergebnisse /   Einstellungen  Werkzeuge   System /  │
│  Workflow     Analyse /      Regions /      DAT-Mgmt    Logs /    │
│  Dashboard    Reports        Profile        Tool-Pfade  Health    │
│                                                                    │
└──────────────────────────────────────────────────────────────────┘
```

| Bereich | Bisheriges Equivalent | Was sich ändert |
|---------|----------------------|----------------|
| **Mission Control** | StartView | Wird zu einem echten Dashboard + Workflow-Hub. Quellen, Intent, Quick-Actions, Health-Overview und Last-Run-Summary auf einer Fläche |
| **Library & Review** | ResultView (Analyse) | Wird zu einer vollwertigen Analyse-Workbench mit 4 Sub-Tabs: Dashboard, Decisions, Safety-Review, Report |
| **Config** | SortView + Teile von SettingsView | Konsolidiert alle Run-Konfiguration: Regionen, Filter, Optionen, Profile. Settings werden von 8 auf 4 Kategorien reduziert |
| **Tools** | ToolsView + Teile von SettingsView | Eigener Top-Level-Bereich für: Tool-Pfade, DAT-Management, Conversion-Registry, Tool-Health |
| **System** | Teile von SettingsView | Audit-Logs, Allgemeine Einstellungen, Theme, Locale, Scheduler, System-Health, About |

### 3.2 Seitenstruktur (Detailliert)

```
MISSION CONTROL
├── Dashboard (Quellen + Health + KPIs)
├── Quick Start (Intent-Cards + Guided Flow)
└── Recent Runs (Timeline / History)

LIBRARY & REVIEW
├── Results Dashboard (KPIs + Charts + Distribution)
├── Dedupe Decisions (TreeView + Inspector)
├── Safety Review (Blocked / Unknown / Risky Items)
└── Report Viewer (HTML + Export)

CONFIG
├── Workflow (Regionen + Optionen + DryRun)
├── Filtering (Extensions + Console-Filter)
├── Profiles (Save / Load / Share / CLI)
└── Advanced (Sort-Optionen, Safety-Strict, Conflict-Policy)

TOOLS
├── External Tools (Pfade + Status + Auto-Detect)
├── DAT Management (Mappings + Verify + Index)
├── Conversion (Registry + Matrix + Estimates)
└── GameKey Lab (Preview + Test + Debug)

SYSTEM
├── Activity Log (Live + Export + Filter)
├── Appearance (Theme + UI-Density + Locale)
├── Scheduler & Automation (Watch-Mode, Intervals)
└── About & Health (Version, Paths, Disk-Space)
```

### 3.3 Informationsfluss-Prinzip

```
CONFIGURE → PREVIEW → REVIEW → EXECUTE → REPORT
     ↑                                      │
     └──────────── ROLLBACK ←───────────────┘
```

Dieser Fluss ist **immer** im UI ablesbar – über eine Phase-Indicator-Leiste im Header.

---

## 4. Mockup-Konzept A: "Command Center"

### Name: **Command Center Layout**

### Zielgruppe
Power-User, erfahrene ROM-Collector, Nutzer die regelmäßig große Sammlungen bearbeiten.

### Layout

```
┌────────────────────────────────────────────────────────────────────────┐
│ ╔══════════════════════════════════════════════════════════════════╗   │
│ ║  ROMULUS    ⬤ ⬤ ⬤ ⬤    Phase: [▰▰▰▱▱] Preview    🎨 Theme  ║   │
│ ╚══════════════════════════════════════════════════════════════════╝   │
│ ┌──────┬──────────────────────────────────────┬───────────────────┐   │
│ │      │                                      │                   │   │
│ │  N   │         MAIN CONTENT AREA            │    INSPECTOR     │   │
│ │  A   │                                      │    PANEL         │   │
│ │  V   │  ┌──────────────────────────────┐    │                   │   │
│ │      │  │                              │    │  ┌─────────────┐ │   │
│ │  B   │  │     PRIMARY VIEW             │    │  │ Details     │ │   │
│ │  A   │  │     (full width, scrollable) │    │  │ Context     │ │   │
│ │  R   │  │                              │    │  │ Quick-Act   │ │   │
│ │      │  │                              │    │  │ Safety-Info │ │   │
│ │  72  │  └──────────────────────────────┘    │  └─────────────┘ │   │
│ │  px  │                                      │                   │   │
│ │      │  ┌──────────────────────────────┐    │  ┌─────────────┐ │   │
│ │  ☰   │  │  SECONDARY PANEL (collapsible)│   │  │ Status      │ │   │
│ │  📊  │  │  (Log / Charts / Sub-Grid)   │    │  │ HealthBar   │ │   │
│ │  ⚙   │  └──────────────────────────────┘    │  └─────────────┘ │   │
│ │  🔧  │                                      │                   │   │
│ │  💻  │                                      │    240–320px      │   │
│ └──────┴──────────────────────────────────────┴───────────────────┘   │
│ ╔══════════════════════════════════════════════════════════════════╗   │
│ ║  ▶ Preview   ⬚ Convert   💀 Execute…   ↩ Rollback   ████ 72%  ║   │
│ ╚══════════════════════════════════════════════════════════════════╝   │
└────────────────────────────────────────────────────────────────────────┘
```

### Struktur im Detail

**Header Bar (48px)**
- Logo-Mark links (animierbar per Theme)
- 4 Status-Orbs (Roots / Tools / DAT / Ready) mit Tooltip
- Phase-Indicator: 5-Step-Pipeline-Dots (`Configure → Preview → Review → Execute → Report`)
- Theme-Picker-Button rechts (Dropdown mit 5 Themes + Live-Preview)
- Quick-Profile-Selector

**Navigation Bar (72px, vertikal, links)**
- Icon-only mit Tooltip und Label bei Hover
- 5 Einträge: Mission Control (🏠), Library (📊), Config (⚙), Tools (🔧), System (💻)
- Active-Indicator: 3px linker Neon-Border + Glow
- Keyboard: Ctrl+1-5 für direkte Navigation

**Main Content Area (flex)**
- Voller Flex-Bereich für die aktive View
- Unterstützt optionales Secondary Panel (unten, collapsible via Splitter)
- Secondary Panel: Log-Stream, Sub-Chart, Detail-Grid – je nach Kontext

**Inspector Panel (240-320px, rechts, collapsible)**
- Kontextabhängig: zeigt Details zum selektierten Item
- In Library: Datei-Details, Scores, Region, DAT-Match
- In Config: Validation-Feedback, Preview des Effekts
- In Tools: Tool-Health, Version-Info, Hash-Status
- Collapsible per Toggle-Button oder Ctrl+I

**Action Bar (52px, unten)**
- Kontextabhängige Buttons (nur relevante Actions sichtbar)
- Links: Primary Action (Preview), Secondary Actions (Convert, Rollback)
- Rechts: Progress (Bar + Text + Cancel)
- Danger Actions (Execute/Move) werden erst nach Review in separatem Danger-State sichtbar
- Danger-State: ActionBar wechselt Hintergrundfarbe zu gedämpftem Rot

### Hauptscreens

**Mission Control**
```
┌─────────────────────────────────────────────────┐
│  Welcome to Romulus                              │
│                                                   │
│  ┌─ SOURCES ────────────────────────────────┐    │
│  │  📁 D:\ROMs\SNES          2,341 files    │    │
│  │  📁 E:\ROMs\Genesis          891 files    │    │
│  │  ┌─ DROP ZONE ──────────────────────┐    │    │
│  │  │   Drop folders here or [Browse]  │    │    │
│  │  └──────────────────────────────────┘    │    │
│  └──────────────────────────────────────────┘    │
│                                                   │
│  ┌─ INTENT ─────────┐  ┌─ HEALTH ────────────┐  │
│  │  ○ Clean & Dedup  │  │  Sources  ⬤ OK      │  │
│  │  ○ Sort           │  │  Tools    ⬤ 3/5     │  │
│  │  ○ Convert Only   │  │  DAT      ⬤ Ready   │  │
│  └──────────────────┘  │  Config   ⬤ Valid   │  │
│                         └──────────────────────┘  │
│  ┌─ LAST RUN ────────────────────────────────┐  │
│  │  🏆 234 Winners  🗑 89 Dupes  ⚠ 12 Junk   │  │
│  │  ⏱ 3.5s  Health: 95%   [View Report →]   │  │
│  └──────────────────────────────────────────────┘│
│                                                   │
│  [▶▶ Start Preview]                              │
└─────────────────────────────────────────────────┘
```

**Library & Review – Dashboard Sub-Tab**
```
┌──────────────────────────────────────────────────┐
│  ANALYSIS DASHBOARD                               │
│                                                    │
│  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐  │
│  │ 456  │ │  89  │ │  12  │ │ 95%  │ │  18  │  │
│  │Games │ │Dupes │ │Junk  │ │Health│ │Conv. │  │
│  └──────┘ └──────┘ └──────┘ └──────┘ └──────┘  │
│                                                    │
│  ┌─ CONSOLE DISTRIBUTION ─────────────────────┐  │
│  │  SNES    ████████████████     342 (28%)     │  │
│  │  Genesis ██████████           198 (16%)     │  │
│  │  GBA     ████████              156 (13%)    │  │
│  │  ... (Top 10 + Pie Chart)                   │  │
│  └─────────────────────────────────────────────┘  │
│                                                    │
│  ┌─ BEFORE / AFTER ──────────────────────────┐   │
│  │  [Bar Chart: Files Before vs After]        │   │
│  └────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────┘
```

### Stärken
- Maximaler Platz für Datenvisualisierung
- Inspector-Panel erlaubt Detail-on-Demand ohne View-Wechsel
- Phase-Indicator gibt jederzeit Orientierung
- Splitter-basiert → Power-User können Layout anpassen
- Keyboard-navigierbar (Ctrl+1-5, F5, Ctrl+I)

### Schwächen
- Kann für Anfänger initial komplex wirken (3-spaltig)
- Inspector-Panel braucht gute Inhalte, sonst wirkt es leer
- Minimum-Breite ~1100px für komfortables Arbeiten

### Geeignet für Romulus?
**Sehr gut.** Command Center passt ideal zur Power-Tool-Identität. Die Komplexität wird durch Progressive Disclosure (collapsible Panels, Phase-Indicator) aufgefangen.

---

## 5. Mockup-Konzept B: "Guided Mission"

### Name: **Guided Mission Layout**

### Zielgruppe
Einsteiger, Gelegenheitsnutzer, Nutzer die ROM-Management erstmals systematisch angehen.

### Layout

```
┌────────────────────────────────────────────────────────────────────────┐
│ ╔══════════════════════════════════════════════════════════════════╗   │
│ ║ ROMULUS   ① Sources  ② Config  ③ Preview  ④ Review  ⑤ Execute  ║   │
│ ╚══════════════════════════════════════════════════════════════════╝   │
│                                                                        │
│  ┌────────────────────────────────────────────────────────────────┐   │
│  │                                                                │   │
│  │                                                                │   │
│  │              FULL-WIDTH WIZARD / STEP VIEW                     │   │
│  │              (eine Phase pro Screen)                           │   │
│  │                                                                │   │
│  │                                                                │   │
│  │                                                                │   │
│  │                                                                │   │
│  │                                                                │   │
│  └────────────────────────────────────────────────────────────────┘   │
│                                                                        │
│    [← Back]                                          [Next Step →]    │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

### Struktur im Detail

**Phase Bar (56px, horizontal, oben)**
- Ersetzt die vertikale Sidebar komplett
- 5 nummerierte Phasen als horizontale Pipeline
- Aktive Phase: Neon-Cyan, Glow-Effekt
- Abgeschlossene Phasen: grün mit Häkchen
- Zukünftige Phasen: gedimmt
- Klickbar für direkte Navigation (wenn Phase schon besucht)

**Content Area (100% Breite)**
- Jede Phase ist ein Full-Width Screen
- Keine Sidebar, kein Inspector
- Maximale Klarheit pro Schritt
- Scrollbar wenn nötig

**Phase-Buttons (unten)**
- Links: Back
- Rechts: Next Step (Primary CTA)
- Danger-Phasen: Next wird zu "Review & Confirm" (Farbwechsel zu Orange/Rot)

### Die 5 Phasen

**Phase 1: Sources**
- Drag & Drop Zone (prominent, zentriert)
- Erkannte Quellen als Card-Liste
- Intent-Auswahl (3 Radio-Cards)
- Health-Check der Quellen (Dateizählung, Erreichbarkeit)

**Phase 2: Config**
- Region-Auswahl (Visual Region-Map mit klickbaren Regionen)
- Filter (Smart-Defaults + Expert-Expander)
- Optionen als Toggles (nicht als Checkbox-Wall)
- Live-Zusammenfassung unten: "Du wirst scannen: 3.232 Dateien, EU+US, DryRun"

**Phase 3: Preview**
- Automatisch gestartet bei Erreichen dieser Phase
- Progress-Animation (zentral, groß)
- Live-Ticker der erkannten Konsolen
- Bei Fertigstellung: Auto-Advance zu Phase 4

**Phase 4: Review**
- Ergebnis-Dashboard (KPIs, Charts, Decisions)
- Safety-Warnungen prominent
- Blocked/Unknown/Risky Items hervorgehoben
- Accept/Reject Mechanismus für kritische Entscheidungen
- "Alles OK" → grüner Banner

**Phase 5: Execute**
- Danger-Zone: roter Hintergrund-Tint
- Zusammenfassung aller Aktionen
- Confirmation-Input für destruktive Aktionen
- Execute-Button (groß, rot, mit Animation)
- Nach Ausführung: Report-Zusammenfassung + Rollback-Option

### Stärken
- Niedrigste Einstiegshürde
- Unmissverständlicher Workflow
- Danger-Aktionen durch Phase-Trennung maximal abgesichert
- Ideal für First-Time-Experience

### Schwächen
- Power-User fühlen sich eingeengt (kein Splitview, kein Inspector)
- Häufiges Hin-und-Her wenn man Settings nachjustieren will
- Tools/System müssen als Overlay oder Seitenpanel eingebaut werden
- Weniger effizient für Batch-Operationen

### Geeignet für Romulus?
**Gut für Onboarding, zu einschränkend als Hauptlayout.** Kann als "Guided Mode" neben dem Hauptlayout existieren.

---

## 6. Mockup-Konzept C: "Studio Hybrid"

### Name: **Studio Hybrid Layout**

### Zielgruppe
Alle Nutzer – vom Einsteiger bis zum Power-User. Adaptives Layout.

### Layout

```
┌────────────────────────────────────────────────────────────────────────┐
│ ╔══════════════════════════════════════════════════════════════════╗   │
│ ║ ROMULUS  [⌘ Command]  ⬤⬤⬤⬤  Phase:[▰▰▱▱▱]  🌓Theme  👤Prof ║   │
│ ╚══════════════════════════════════════════════════════════════════╝   │
│ ┌─────┬──────────────────────────────────┬────────────────────────┐   │
│ │     │ ┌── TAB BAR ──────────────────┐ │                        │   │
│ │  N  │ │ Dashboard │ Decisions │ Log  │ │    CONTEXT WING       │   │
│ │  A  │ └────────────────────────────── │ │                        │   │
│ │  V  │                                  │  ┌──────────────────┐  │   │
│ │     │  ┌────────────────────────────┐  │  │                  │  │   │
│ │  72  │  │                            │  │  │  Inspector /     │  │   │
│ │  px  │  │    MAIN VIEW               │  │  │  Quick Actions / │  │   │
│ │     │  │    (Tab-Content)            │  │  │  Safety Gate /   │  │   │
│ │ ── │  │                            │  │  │  Help Tips       │  │   │
│ │ 🏠  │  │                            │  │  │                  │  │   │
│ │ 📊  │  └────────────────────────────┘  │  └──────────────────┘  │   │
│ │ ⚙️  │                                  │                        │   │
│ │ 🔧  │  ┌── DETAIL DRAWER ──────────┐  │  ┌──────────────────┐  │   │
│ │ 💻  │  │  (collapsible, 200px max)  │  │  │  Status Cards    │  │   │
│ │     │  │  Log / SubGrid / Preview   │  │  │  Health Monitor  │  │   │
│ │     │  └────────────────────────────┘  │  └──────────────────┘  │   │
│ └─────┴──────────────────────────────────┴────────────────────────┘   │
│ ╔══════════════════════════════════════════════════════════════════╗   │
│ ║ ACTION RAIL: [▶ Preview F5] │ [Convert] │ [💀Execute…] │ ██ 0% ║   │
│ ╚══════════════════════════════════════════════════════════════════╝   │
└────────────────────────────────────────────────────────────────────────┘
```

### Struktur im Detail

**Command Bar (48px, oben)**
- Logo + App-Name (klickbar → Mission Control)
- **Command Palette Trigger** (Ctrl+K): Fuzzy-Search über alle Aktionen, Einstellungen und Navigation
- Status-Orbs (4 Dots mit Pulse-Animation bei Problemen)
- Phase-Indicator (5-Step Micro-Pipeline)
- Theme-Switcher (Dropdown mit Live-Preview-Swatches)
- Profile-Selector

**Navigation Rail (72px, links)**
- 5 Icon-Buttons mit Micro-Label
- Animated Active-Indicator (3px Border + Glow-Transition)
- Spacer → System/Settings am unteren Ende
- Collapsed-State bei Fensterbreite < 1000px (nur Icons)

**Tab Bar (32px, kontextabhängig)**
- Horizontale Tabs innerhalb jeder Navigation Section
- Mission Control: Dashboard | Quick Start | History
- Library: Overview | Decisions | Safety | Report
- Config: Workflow | Filters | Profiles | Advanced
- Tools: External | DAT | Conversion | GameKey Lab
- System: Activity | Appearance | Automation | About

**Main View (flex)**
- Tab-Content, scrollbar
- Kann von Detail Drawer ergänzt werden (unten, collapsible per Drag-Splitter)
- Detail Drawer: Log-Stream, Sub-Grid, Preview-Render – kontextabhängig

**Context Wing (260-320px, rechts, collapsible)**
- **Oberer Bereich**: Inspector / Quick-Actions / Safety-Gate
  - Adapts per View: Zeigt immer das Relevanteste
  - In Library/Decisions: Item-Detail mit Scores und Region
  - In Config: Live-Validierung und Effekt-Zusammenfassung
  - In Tools: Tool-Status und Quick-Links
- **Unterer Bereich**: Status-Cards
  - Permanentes Health-Widget (4 Mini-Cards: Sources, Tools, DAT, Config)
  - Pulse-Animation bei Status-Änderungen

**Action Rail (52px, unten)**
- Kontextabhängig – zeigt nur relevante Aktionen
- **Normal State**: Preview (Primary, Cyan), Convert (Secondary), Rollback (Tertiary)
- **Danger State**: Background-Tint wechselt zu gedämpftem Rot, Execute-Button dominant
- **Progress State**: Buttons werden durch Progress-Anzeige ersetzt
- Separator zwischen Safe und Danger Actions
- Progress: Animated Bar + Percentage + ETA + Cancel

### Hauptscreens (Detailliert)

**Mission Control – Dashboard**
```
┌─────────────────────────────────────────────┐
│  MISSION CONTROL                             │
│                                               │
│  ┌─ SOURCES ──────────────────────────────┐  │
│  │  📁 SNES (2341)  📁 Genesis (891)      │  │
│  │  [+ Add Source]  [Drop Zone ▾]          │  │
│  └─────────────────────────────────────────┘ │
│                                               │
│  ┌──────┐  ┌──────┐  ┌───────┐              │
│  │ 🧹   │  │ 📂   │  │  🔄   │   Intent     │
│  │Clean │  │Sort  │  │Convert│   Cards       │
│  │& Dup │  │      │  │ Only  │              │
│  └──────┘  └──────┘  └───────┘              │
│                                               │
│  ┌─ SYSTEM HEALTH ──────────────────────┐   │
│  │  ⬤ Sources OK   ⬤ Tools 3/5          │   │
│  │  ⬤ DAT Ready    ⬤ Config Valid       │   │
│  └───────────────────────────────────────┘   │
│                                               │
│  ┌─ LAST RUN ─────────────────────────────┐  │
│  │  🏆234  🗑89  ⚠12  ⏱3.5s  ❤95%       │  │
│  │  Convert: 18 OK, 3 blocked, 2 review   │  │
│  │  [View Full Report]  [Open Library →]   │  │
│  └─────────────────────────────────────────┘ │
│                                               │
│  ═══ GUIDED WORKFLOW ═══════════════════════  │
│  ① Add Sources  ② Configure  ③ Preview       │
│  ④ Review       ⑤ Execute    ⑥ Report        │
└─────────────────────────────────────────────┘
```

**Library – Decisions Sub-Tab**
```
┌──────────────────────────────────────────┬──────────────┐
│  DEDUPE DECISIONS                        │  INSPECTOR   │
│                                          │              │
│  ┌─ SEARCH / FILTER ──────────────┐     │  Selected:   │
│  │  🔍 [Filter by GameKey...    ] │     │  Super Mario │
│  │  Console: [All ▾] Status: [▾]  │     │  World.sfc   │
│  └────────────────────────────────┘     │              │
│                                          │  Region: EU  │
│  ┌─ DECISION TREE ────────────────┐     │  R:95 F:80   │
│  │  ★ Super Mario World           │     │  V:100       │
│  │    ├─ Super Mario World (E).sfc│     │              │
│  │    │  🏆 Winner  EU R:95 F:80  │     │  DAT: ✓ Hit  │
│  │    ├─ Super Mario World (U).sfc│     │  CRC: a4c2.. │
│  │    │  ✗ Dupe    US R:85 F:80  │     │  Size: 2.1MB │
│  │    └─ Super Mario World (J).sfc│     │              │
│  │       ✗ Dupe    JP R:60 F:80  │     │  Console:    │
│  │                                │     │  SNES        │
│  │  ★ Zelda ALttP                 │     │              │
│  │    ├─ ...                      │     │  ┌─────────┐ │
│  │    └─ ...                      │     │  │Open File│ │
│  └────────────────────────────────┘     │  │View DAT │ │
│                                          │  └─────────┘ │
└──────────────────────────────────────────┴──────────────┘
```

**Library – Safety Review Sub-Tab**
```
┌───────────────────────────────────────────────┐
│  SAFETY REVIEW                                 │
│                                                 │
│  ┌─ SUMMARY BAR ──────────────────────────┐   │
│  │ 🛡 3 Blocked  ⚠ 2 Review  ❓ 5 Unknown │   │
│  └─────────────────────────────────────────┘  │
│                                                 │
│  ┌─ BLOCKED ITEMS ─────────────────────────┐  │
│  │ ❌ corrupt.zip    Reason: CRC mismatch   │  │
│  │ ❌ bad_header.rom Reason: Invalid header  │  │
│  │ ❌ locked.7z      Reason: Passwort-Archiv │  │
│  └──────────────────────────────────────────┘ │
│                                                 │
│  ┌─ NEEDS REVIEW ──────────────────────────┐  │
│  │ ⚠ Hack_v2.smc    Safety: Risky convert  │  │
│  │ ⚠ Beta_Demo.nes  Safety: Risky convert   │  │
│  └──────────────────────────────────────────┘ │
│                                                 │
│  ┌─ UNKNOWN / AMBIGUOUS ───────────────────┐  │
│  │ ❓ weird_rom.bin     No DAT match        │  │
│  │ ❓ unlabeled_1.sfc   Multiple matches    │  │
│  │ ...                                       │  │
│  └──────────────────────────────────────────┘ │
└───────────────────────────────────────────────┘
```

**Config – Workflow Sub-Tab**
```
┌─────────────────────────────────────────────┐
│  RUN CONFIGURATION                           │
│                                               │
│  ┌─ PRESETS ─────────────────────────────┐   │
│  │  [◉ Safe Preview] [○ Full Sort]       │   │
│  │  [○ Convert Only]                     │   │
│  └────────────────────────────────────────┘  │
│                                               │
│  ┌─ REGIONS ─────────────────────────────┐   │
│  │  ☑ Europe    ☑ USA                    │   │
│  │  ☐ Japan     ☑ World                  │   │
│  │  [▾ More regions...]                  │   │
│  └────────────────────────────────────────┘  │
│                                               │
│  ┌─ OPTIONS ─────────────────────────────┐   │
│  │  ☑ DryRun (Preview only, no moves)    │   │
│  │  ☐ Enable Conversion                 │   │
│  │  Conflict: [Rename ▾]                │   │
│  └────────────────────────────────────────┘  │
│                                               │
│  ┌─ LIVE SUMMARY ────────────────────────┐   │
│  │  Will scan: 3,232 files               │   │
│  │  Regions: EU, US, World               │   │
│  │  Mode: DryRun (no files will be moved)│   │
│  └────────────────────────────────────────┘  │
└─────────────────────────────────────────────┘
```

### Stärken
- **Bestes aus beiden Welten**: Sidebar + Tabs + Context Wing + Detail Drawer
- **Skaliert** von 960px (Sidebar collapsed, Wing hidden) bis 1920px+ (alles offen)
- **Command Palette** (Ctrl+K) bringt Power-User-Effizienz
- **Phase-Indicator** gibt Orientierung ohne den Workflow aufzuzwingen
- **Adaptiv**: Simple-Mode blendet Advanced-Tabs aus, Context Wing wird schmaler
- **Inspector/Context Wing** erlaubt Detail-on-Demand
- **Tab-System** strukturiert Unterbereiche ohne neue Views

### Schwächen
- Komplexes Layout-System → mehr XAML-Arbeit
- Tab-Navigation innerhalb der Seiten könnte mit Sidebar-Navigation verwechselt werden (muss visuell klar getrennt sein)
- Context Wing Inhalte müssen für alle Views gut gefüllt sein

### Geeignet für Romulus?
**Optimal.** Studio Hybrid vereint die Stärken von Command Center und Guided Mission. Es wächst mit dem Nutzer mit und kann alle Features ohne Layout-Umbau tragen.

---

## 7. Vergleich der Konzepte

### Matrix

| Kriterium | A: Command Center | B: Guided Mission | C: Studio Hybrid |
|-----------|------------------|-------------------|-----------------|
| **Anfänger-tauglich** | ◐ Mittel | ● Sehr gut | ● Gut (mit Simple-Mode) |
| **Power-User-Effizienz** | ● Sehr gut | ◔ Eingeschränkt | ● Sehr gut |
| **Danger-Safety** | ◐ Gut | ● Sehr gut (Phase-Trennung) | ● Sehr gut (Phase + Separation) |
| **Feature-Vollständigkeit** | ● Vollständig | ◐ Eingeschränkt (Tools/System als Overlay) | ● Vollständig |
| **Skalierbarkeit** | ● Gut | ◐ Mäßig | ● Sehr gut |
| **Visuelle Klarheit** | ◐ Mittel (komplexe Splits) | ● Sehr gut (ein Screen, ein Fokus) | ● Gut |
| **WPF-Umsetzbarkeit** | ● Machbar | ● Einfach | ◐ Aufwändig |
| **Retro-Ästhetik-Potenzial** | ● Hoch (Cyberdeck/Workstation) | ◐ Mittel | ● Hoch (Studio/Arcade) |
| **Min. Fensterbreite** | 1100px | 800px | 960px |

### Empfehlung

| Frage | Antwort |
|-------|---------|
| **Bestes für Anfänger?** | B: Guided Mission |
| **Bestes für Power-User?** | A: Command Center |
| **Bestes für Romulus insgesamt?** | **C: Studio Hybrid** |

**Begründung**: Romulus ist ein Tool das sowohl Discovery (Einsteiger lernen ihr ROM-Set kennen) als auch Production (Power-User bereinigen regelmäßig) abdecken muss. Studio Hybrid liefert beides durch ein adaptives Layout, das durch Simple/Expert-Mode, collapsible Panels und Command Palette skaliert.

**Konzept B (Guided Mission)** sollte als **First-Run-Wizard** (bereits vorhanden!) weiterleben, nicht als Hauptlayout.

---

## 8. Theme-System

### 8.1 Architektur

```
Themes/
├── _DesignTokens.xaml          ← Spacing, Radien, Typo, Animationen (theme-agnostisch)
├── _SemanticTokens.xaml        ← Status, Safety, Action-Farben (referenziert Palette)
├── SynthwaveDark.xaml          ← Theme 1: Palette + Control-Templates
├── CleanDarkPro.xaml           ← Theme 2
├── RetroCRT.xaml               ← Theme 3
├── ArcadeNeon.xaml             ← Theme 4
├── LightStudio.xaml            ← Theme 5
└── HighContrast.xaml           ← Theme 6 (WCAG AAA)
```

**Token-Hierarchie**:
```
DesignTokens (constant)    →  SpacingXS=4, RadiusCard=8, FontBody=13
                    ↓
SemanticTokens (theme-aware) →  StatusSuccess={ThemePalette.Green}, DangerBg={ThemePalette.DangerBg}
                    ↓
ThemePalette (per theme)    →  Green=#00FF9F, DangerBg=#3D0000
```

### 8.2 Theme 1: Synthwave Dark (Default)

| Property | Wert | Beschreibung |
|----------|------|-------------|
| **Grundcharakter** | Neon auf Mitternacht – retro-futuristisch, warm-kühl | Die Signature-Identität von Romulus |
| **Background** | `#0D0F1A` | Tiefes Dunkelblau-Schwarz |
| **Surface** | `#161929` | Leicht aufgehellte Kartenfläche |
| **SurfaceAlt** | `#1C2038` | Alternate-Rows, Hover |
| **SurfaceLight** | `#232847` | Elevated-Surface |
| **AccentCyan** | `#00E5FF` | Primary-Aktionen, Links, aktive Nav |
| **AccentPurple** | `#B388FF` | Sekundäre Akzente, Rollback |
| **AccentPink** | `#FF6EB4` | Tertiary/Highlight (sparsam) |
| **TextPrimary** | `#E8ECFF` | Haupttext |
| **TextMuted** | `#6B7394` | Sekundärtext, Captions |
| **Border** | `#2A2F4A` | Subtile Grenzen |
| **StatusSuccess** | `#00FF9F` | Neon-Grün |
| **StatusWarning** | `#FFD740` | Neon-Gelb/Gold |
| **StatusDanger** | `#FF4466` | Neon-Rot/Pink |
| **StatusInfo** | `#40C4FF` | Helles Cyan |
| **DangerBg** | `#2D0A14` | Danger-Zone Hintergrund (gedämpft) |
| **SuccessBg** | `#0A2D1A` | Success-Hintergrund |
| **WarningBg** | `#2D2A0A` | Warning-Hintergrund |
| **GlowCyan** | `DropShadow(#00E5FF, 0, 0, 8)` | Dezenter Glow für aktive Elemente |
| **GlowPurple** | `DropShadow(#B388FF, 0, 0, 6)` | Glow für Purple-Akzente |
| **Look & Feel** | Cyberdeck-Workstation, Arcade-Cabinet. Grid-Lines und dezente Glow-Effekte. Schriften: Inter oder Segoe UI Variable. Code: JetBrains Mono oder Cascadia Code |
| **Geeignete Nutzung** | Default-Theme. Abends/nachts. Längere Sessions. Maximale Romulus-Identität |

### 8.3 Theme 2: Clean Dark Pro

| Property | Wert | Beschreibung |
|----------|------|-------------|
| **Grundcharakter** | Professionell, reduziert, modern. Wie VS Code Dark+ oder JetBrains Darcula | Für Nutzer die kein Neon wollen |
| **Background** | `#1E1E1E` | Neutrales Dunkelgrau |
| **Surface** | `#252526` | VS-Code-Surface |
| **SurfaceAlt** | `#2D2D30` | Alternate |
| **AccentCyan** | `#4FC3F7` | Sanftes Blau (kein Neon) |
| **AccentPurple** | `#9575CD` | Gedämpftes Violet |
| **TextPrimary** | `#D4D4D4` | Standard Light-Gray |
| **TextMuted** | `#808080` | Gray-500 |
| **Border** | `#3C3C3C` | Standard Dark Border |
| **StatusSuccess** | `#4CAF50` | Material Green |
| **StatusWarning** | `#FFA726` | Material Orange |
| **StatusDanger** | `#EF5350` | Material Red |
| **DangerBg** | `#3D1F1F` | Gedämpftes Rot |
| **GlowCyan** | Keiner | Kein Glow-Effekt |
| **Look & Feel** | IDE-inspiriert. Klare Kanten. Kein Glow, kein Neon. Funktional und ruhig |
| **Geeignete Nutzung** | Für Nutzer die ein "normales" Dark-Theme bevorzugen. Business-Kontext. Weniger Ablenkung |

### 8.4 Theme 3: Retro CRT

| Property | Wert | Beschreibung |
|----------|------|-------------|
| **Grundcharakter** | Terminal/CRT-Ästhetik. Phosphor-Grün auf Schwarz. Scanline-Overlay. Monospace überall | Nostalgie-Mode für Retro-Enthusiasten |
| **Background** | `#0A0A0A` | Nahezu Schwarz (CRT-Off) |
| **Surface** | `#0F0F0F` | Minimal aufgehellt |
| **SurfaceAlt** | `#141414` | Kaum sichtbarer Unterschied |
| **AccentCyan** | `#33FF33` | Phosphor-Grün (Hauptfarbe) |
| **AccentPurple** | `#33FFFF` | Cyan als Sekundär |
| **AccentPink** | `#FF3333` | Rot für Danger |
| **TextPrimary** | `#33FF33` | Phosphor-Grün (alles Grün) |
| **TextMuted** | `#1A8C1A` | Gedämpftes Grün |
| **Border** | `#1A331A` | Dunkelgrüne Borders |
| **StatusSuccess** | `#33FF33` | Grün (identisch mit Primary) |
| **StatusWarning** | `#FFFF33` | Phosphor-Gelb |
| **StatusDanger** | `#FF3333` | Phosphor-Rot |
| **DangerBg** | `#1A0A0A` | Dunkelrot |
| **Special: Scanlines** | `Repeating linear pattern, 2px, Opacity 0.05` | Subtiler CRT-Scanline-Overlay über gesamte App |
| **Special: Typography** | `FontFamily: "Cascadia Mono", "Consolas"` | Alles Monospace |
| **Special: Cursor** | Blink-Animation auf aktives Element | Terminal-Cursor-Feeling |
| **Look & Feel** | Wie ein Terminal aus den 80ern, aber modern bedienbar. Scanlines, Monospace, alles grün. Buttons als `[CONFIRM]` statt "Bestätigen" |
| **Geeignete Nutzung** | Retro-Enthusiasten. Demo-Mode. Streaming/Videos. Maximale Nostalgie |

### 8.5 Theme 4: Arcade Neon

| Property | Wert | Beschreibung |
|----------|------|-------------|
| **Grundcharakter** | Arcade-Cabinet. Kräftige Neon-Farben. Stärker als Synthwave. Mehr Pink/Magenta. Lebhafter | Party-Mode, Eye-Candy |
| **Background** | `#0A0014` | Tiefes Lila-Schwarz |
| **Surface** | `#140024` | Dunkles Violett |
| **SurfaceAlt** | `#1A0033` | Angehobenes Violett |
| **AccentCyan** | `#00FFFF` | Voll-Cyan |
| **AccentPurple** | `#FF00FF` | Voll-Magenta |
| **AccentPink** | `#FF69B4` | Hot Pink |
| **TextPrimary** | `#FFFFFF` | Reines Weiß |
| **TextMuted** | `#9966CC` | Lila-Grau |
| **Border** | `#330066` | Violette Borders |
| **StatusSuccess** | `#00FF66` | Neon-Grün |
| **StatusWarning** | `#FFFF00` | Neon-Gelb |
| **StatusDanger** | `#FF0033` | Neon-Rot |
| **GlowCyan** | `DropShadow(#00FFFF, 0, 0, 12)` | Stärkerer Glow als Synthwave |
| **GlowMagenta** | `DropShadow(#FF00FF, 0, 0, 10)` | Magenta-Glow für Aktionen |
| **Special: ChromaShift** | Gradient-Border auf Cards: `Cyan → Magenta → Pink` | Lebendige Card-Borders |
| **Look & Feel** | Arcade-Cabinet bei Nacht. Mehr Farbe als Synthwave. Gradient-Borders. Stärkere Glows. Pulsende Animationen |
| **Geeignete Nutzung** | Fun-Mode. Streaming. Screenshots. Wow-Effekt. Nicht für 8h-Sessions |

### 8.6 Theme 5: Light Studio

| Property | Wert | Beschreibung |
|----------|------|-------------|
| **Grundcharakter** | Helles, professionelles Studio-Theme. Wie Figma/Notion Light-Mode | Für helle Umgebungen und Print-Kontext |
| **Background** | `#F8F9FC` | Warmes Off-White |
| **Surface** | `#FFFFFF` | Reines Weiß |
| **SurfaceAlt** | `#F0F2F8` | Hellgrau-Blau |
| **SurfaceLight** | `#FAFBFF` | Fast-Weiß |
| **AccentCyan** | `#0066CC` | Professionelles Blau |
| **AccentPurple** | `#6E3FD6` | Kräftiges Violett |
| **TextPrimary** | `#1A1D2E` | Dunkelblaues Schwarz |
| **TextMuted** | `#6B7394` | Blau-Grau |
| **Border** | `#D8DCE8` | Helle Borders |
| **StatusSuccess** | `#1B8A5A` | Professionelles Grün |
| **StatusWarning** | `#B26A00` | Warmes Orange |
| **StatusDanger** | `#C62828` | Seriöses Rot |
| **DangerBg** | `#FFF0F0` | Pastell-Rot |
| **SuccessBg** | `#F0FFF4` | Pastell-Grün |
| **InfoBg** | `#F0F4FF` | Pastell-Blau |
| **Look & Feel** | Sauber, luftig, professionell. Subtile Schatten statt Borders. Keine Glow-Effekte. Klar lesbar |
| **Geeignete Nutzung** | Helle Arbeitsumgebungen. Nutzer die Dark-Mode nicht mögen. Presentations. Screenshots für Docs |

### 8.7 Theme 6: High Contrast (WCAG AAA)

| Property | Wert | Beschreibung |
|----------|------|-------------|
| **Grundcharakter** | Maximaler Kontrast. Schwarz + Weiß + Signal-Farben. Accessibility-First | Pflicht-Theme für Barrierefreiheit |
| **Background** | `#000000` | Reines Schwarz |
| **Surface** | `#1A1A1A` | Minimal aufgehellt |
| **AccentCyan** | `#FFFF00` | Gelb (höchster Kontrast auf Schwarz) |
| **TextPrimary** | `#FFFFFF` | Reines Weiß |
| **Border** | `#FFFFFF` | Weiße Borders (voll sichtbar) |
| **StatusSuccess** | `#33FF33` | Leuchtendes Grün |
| **StatusDanger** | `#FF3333` | Leuchtendes Rot |
| **Special: BorderThickness** | 2px statt 1px | Dickere Borders |
| **Special: FocusRing** | 3px statt 2px, Gelb | Stärkere Fokus-Ringe |
| **Look & Feel** | Funktional, maximal lesbar. Keine Schatten, keine Gradients, keine Transparenz |
| **Geeignete Nutzung** | Sehbehinderungen. Sonnenlicht/Blendung. Bildschirmlupen. Pflicht für Accessibility-Compliance |

### 8.8 Theme-Switching Mechanismus

```
┌──────────────────────────────────────┐
│  🌓  Theme                          │
│  ┌──────────────────────────────┐   │
│  │ ◉ Synthwave Dark    [■■■■]  │   │ ← Farbvorschau-Swatches
│  │ ○ Clean Dark Pro    [■■■■]  │   │
│  │ ○ Retro CRT         [■■■■]  │   │
│  │ ○ Arcade Neon       [■■■■]  │   │
│  │ ○ Light Studio      [■■■■]  │   │
│  │ ○ High Contrast     [■■■■]  │   │
│  └──────────────────────────────┘   │
│                                      │
│  ☐ Sync with Windows theme          │
│  ☐ Schedule (dark after 18:00)      │
└──────────────────────────────────────┘
```

- **Keyboard**: Ctrl+T cycles through themes
- **Command Palette**: "Switch Theme" → Liste
- **System Settings**: Appearance → Theme
- **API**: `IThemeService.SetTheme(string themeKey)`
- **Persistence**: `settings.json` → `"theme": "SynthwaveDark"`

---

## 9. Empfohlenes Zielbild

### 9.1 Hauptlayout: Studio Hybrid (Konzept C)

Das empfohlene Zielbild ist **Studio Hybrid** mit folgenden Anpassungen:

- **Default-Theme**: Synthwave Dark
- **Default-Mode**: Simple (Expert über Toggle in Status-Bar)
- **Default-Fenster**: 1100 × 780px (minimum 960 × 680)
- **Navigation**: 5-Item Vertical Rail + Horizontal Tab-Bar

### 9.2 Komponentenübersicht

```
SHELL
├── CommandBar          ← Header: Logo, CommandPalette, Status, Phase, Theme, Profile
├── NavigationRail      ← 72px links: 5 Icons (Mission/Library/Config/Tools/System)
├── TabBar              ← 32px: Sub-Tabs je nach aktiver NavSection
├── MainContent         ← Flex: ScrollViewer + aktive View
├── DetailDrawer        ← Collapsible unten: Log/SubGrid/Preview (200px max)
├── ContextWing         ← 260-320px rechts: Inspector/QuickActions/Status
├── ActionRail          ← 52px unten: kontextuelle Buttons + Progress
├── NotificationLayer   ← Toast-Popups (bottom-right)
├── WizardOverlay       ← First-Run Wizard (nur initial)
├── ShortcutOverlay     ← F1 Shortcut-Cheatsheet
├── CommandPalette      ← Ctrl+K Fuzzy-Search Overlay
└── DangerConfirmModal  ← Modale Bestätigung für destruktive Aktionen
```

### 9.3 Screen-Definitionen

#### Mission Control (Home)

| Element | Beschreibung |
|---------|-------------|
| **Sources Panel** | Drag&Drop Zone + Quellenliste mit Dateianzahl und Status-Dot pro Quelle |
| **Intent Cards** | 3 Radio-Cards (Clean&Dedupe, Sort, Convert Only) mit Icon und Kurzbeschreibung |
| **System Health Panel** | 4 Status-Zeilen (Sources, Tools, DAT, Config) mit farbcodiertem Status |
| **Last-Run Summary** | KPI-Row (Winners, Dupes, Junk, Duration, Health) + Quick-Links |
| **Guided Workflow** | 6-Schritt-Flow als horizontale Pipeline-Karte (nur im Simple-Mode prominent) |
| **CTA** | Großer "Start Preview" Button (Cyan, 56px) |

**Context Wing zeigt**: Quick-Links zu Config, Tool-Status, nächste empfohlene Aktion

#### Library & Review

| Sub-Tab | Inhalt |
|---------|--------|
| **Overview** | KPI-Cards (Games, Dupes, Junk, Health, Converted) + Console-Distribution (Bars + Pie) + Before/After-Chart |
| **Decisions** | Dedupe-TreeView mit Such-/Filter-Bar + Inspector für selektiertes Item |
| **Safety** | 3 Listen: Blocked (rot), Review Required (orange), Unknown (grau) – mit Aktions-Buttons |
| **Report** | HTML-Report-Viewer (WebView2) + Export-Buttons (HTML, CSV, JSON) + Error-Summary |

**Context Wing zeigt**: Details zum selektierten Item (Scores, Region, DAT, CRC, Console)

#### Config

| Sub-Tab | Inhalt |
|---------|--------|
| **Workflow** | Presets, Regionen (Simple: 4 Checkboxes, Expert: 20 mit Expander), DryRun-Toggle, Convert-Toggle, Conflict-Policy |
| **Filters** | Extension-Filter (ItemsControl mit Toggle), Console-Filter (gruppierte Checkboxes mit Suche) |
| **Profiles** | Profile-Selector, Save/Load/Delete/Import/Share/CLI-Export |
| **Advanced** | Sort-Console, Aggressive-Junk, Alias-Keying, Only-Games, Safety-Strict, Protected-Paths, Scheduler |

**Context Wing zeigt**: Live-Zusammenfassung der Config ("Will scan: 3.232 files, EU+US, DryRun"), Validation-Fehler

#### Tools

| Sub-Tab | Inhalt |
|---------|--------|
| **External** | Tool-Pfade Grid (chdman, dolphintool, 7z, psxtract, ciso) mit Status-Dots, Auto-Detect, Browse |
| **DAT** | DAT-Root, Hash-Type, Mapping-DataGrid, Verify-Button, Fallback-Toggle |
| **Conversion** | Conversion-Registry-Viewer, Matrix-Darstellung, Estimator-Ergebnisse |
| **GameKey Lab** | Input-Feld + Live-Ergebnis (GameKey, Console, Region, Scores) – für Testing/Debugging |

**Context Wing zeigt**: Tool-Versionen, Hash-Status, Registry-Details

#### System

| Sub-Tab | Inhalt |
|---------|--------|
| **Activity** | Live-Log (Consolas-Font, farbcodiert) mit Level-Filter und Export |
| **Appearance** | Theme-Switcher (mit Swatches), UI-Density (Compact/Normal/Comfortable), Locale-Selector |
| **Automation** | Watch-Mode Toggle, Scheduler-Intervall, Minimize-to-Tray |
| **About** | Version, Build, Paths (Trash, Audit, Settings), Disk-Space-Check, Links |

**Context Wing zeigt**: System-Health, aktuelle Session-Stats

### 9.4 Action Rail – Zustands-Logik

```
STATE: IDLE (kein Run, kein Ergebnis)
┌────────────────────────────────────────────────────────────┐
│  [▶ Preview  F5]                                    ██ 0%  │
└────────────────────────────────────────────────────────────┘

STATE: RUNNING (Preview oder Execute läuft)
┌────────────────────────────────────────────────────────────┐
│  [⏹ Cancel  ESC]   Scanning SNES files...    ████████ 72% │
└────────────────────────────────────────────────────────────┘

STATE: PREVIEW DONE (Ergebnis da, DryRun)
┌────────────────────────────────────────────────────────────┐
│  [▶ Preview F5] │ [🔄 Convert] │ [→ Review] │  ██████ ✓   │
└────────────────────────────────────────────────────────────┘

STATE: REVIEW DONE (Ergebnis reviewed, Move möglich)
┌────────────────────────────────────────────────────────────┐
│  [▶ Preview F5] │ [🔄 Convert] ║ [💀 Execute…] │ [↩ Undo]│
└────────────────────────────────────────────────────────────┘
  ↑ Safe Actions                  ↑ Danger Zone (rot getrennt)

STATE: EXECUTE REQUESTED (Danger Confirmation)
┌─── BG: gedämpftes Rot ────────────────────────────────────┐
│  ⚠ 234 files will be moved.  [Abbrechen] [✓ Ja, ausführen]│
└────────────────────────────────────────────────────────────┘

STATE: EXECUTED (Move abgeschlossen)
┌────────────────────────────────────────────────────────────┐
│  [▶ Preview F5] │ [📄 Report] │ [↩ Rollback Ctrl+Z]  ✓   │
└────────────────────────────────────────────────────────────┘
```

### 9.5 Command Palette (Ctrl+K)

```
┌──────────────────────────────────────────────┐
│  ⌘  Type a command...                   [ESC]│
│  ────────────────────────────────────────────│
│  > Start Preview                        F5   │
│  > Open Report                       Ctrl+R  │
│  > Switch Theme → Synthwave Dark      Ctrl+T │
│  > Go to Config                       Ctrl+2 │
│  > Add Source Folder                  Ctrl+O  │
│  > Toggle Expert Mode                        │
│  > Export Settings                           │
│  > GameKey Lab                               │
│  ────────────────────────────────────────────│
│  Recent: Preview (3min ago), Report (12m)    │
└──────────────────────────────────────────────┘
```

Fuzzy-Search über:
- Navigation (Go to Mission Control, Library, Config, Tools, System)
- Aktionen (Preview, Execute, Convert, Rollback, Export)
- Settings (Toggle-Werte, Tool-Pfade)
- Features (GameKey Lab, DAT Verify, Watch Mode)

---

## 10. Konkrete Refactoring-Empfehlungen für WPF

### 10.1 View-Struktur (Neu)

**Aktuelle Views → Neue Views**

| Aktuell | Neu | Änderung |
|---------|-----|---------|
| `MainWindow.xaml` | `ShellWindow.xaml` | Rename, neues Layout (CommandBar + NavRail + TabBar + Content + Wing + ActionRail) |
| `StartView.xaml` | `MissionControlView.xaml` | Rename + Redesign (Dashboard-Layout statt vertikaler Stack) |
| `ResultView.xaml` | `LibraryView.xaml` | Rename, wird Container für 4 Sub-Views (TabControl) |
| — (neu) | `LibraryOverviewView.xaml` | KPI-Cards + Charts (extrahiert aus ResultView Dashboard) |
| — (neu) | `LibraryDecisionsView.xaml` | TreeView + Filter (extrahiert aus ResultView) |
| — (neu) | `LibrarySafetyView.xaml` | Blocked/Review/Unknown Listen (neu) |
| — (neu) | `LibraryReportView.xaml` | Report-Viewer + Error-Summary (extrahiert aus ResultView) |
| `SortView.xaml` | `ConfigWorkflowView.xaml` | Rename, nur Workflow-Config (Presets, Regions, Options) |
| — (neu) | `ConfigFiltersView.xaml` | Extension + Console Filter (extrahiert aus SettingsView) |
| — (neu) | `ConfigProfilesView.xaml` | Profile-Management (extrahiert aus SettingsView) |
| — (neu) | `ConfigAdvancedView.xaml` | Sort-Optionen, Safety (extrahiert aus SettingsView) |
| `SettingsView.xaml` | Aufgelöst | → Inhalte verteilt auf Config + Tools + System |
| `ToolsView.xaml` | `ToolsExternalView.xaml` | Rename, nur External-Tools |
| — (neu) | `ToolsDatView.xaml` | DAT-Management (extrahiert aus SettingsView) |
| — (neu) | `ToolsConversionView.xaml` | Conversion-Registry + Matrix (neu) |
| — (neu) | `ToolsGameKeyLabView.xaml` | GameKey-Preview (extrahiert aus SettingsView) |
| — (neu) | `SystemActivityView.xaml` | Live-Log (extrahiert aus ResultView Protokoll) |
| — (neu) | `SystemAppearanceView.xaml` | Themes + Locale (extrahiert aus SettingsView) |
| — (neu) | `SystemAutomationView.xaml` | Watch-Mode + Scheduler (extrahiert aus SettingsView) |
| — (neu) | `SystemAboutView.xaml` | Version, Paths, Health (extrahiert aus SettingsView) |
| `ContextPanel.xaml` | `ContextWingView.xaml` | Rename + stark erweitert (Inspector-Logik) |
| `SmartActionBar.xaml` | `ActionRailView.xaml` | Rename + State-Machine-basiert |
| `ProgressView.xaml` | `ProgressOverlayView.xaml` | Wird Overlay statt View-Replacement |
| `WizardView.xaml` | `WizardOverlayView.xaml` | Bleibt als Overlay, Rename |
| — (neu) | `CommandPaletteView.xaml` | Fuzzy-Search Overlay (Ctrl+K) |

### 10.2 ViewModel-Struktur (Neu)

**Aktuelle ViewModels → Neue ViewModels**

| Aktuell | Neu | Änderung |
|---------|-----|---------|
| `MainViewModel.cs` (Partial, 5 Dateien) | `ShellViewModel.cs` | Stark schlankere Shell (nur Navigation, Theme, Phase-State) |
| `MainViewModel.Filters.cs` | → `ConfigViewModel.Filters.cs` | Verschoben in eigenes VM |
| `MainViewModel.RunPipeline.cs` | → `RunPipelineViewModel.cs` | Eigenes VM für Run-Logik |
| `MainViewModel.Settings.cs` | Aufgelöst | → ConfigViewModel + SystemViewModel |
| `MainViewModel.Validation.cs` | → `ConfigViewModel.Validation.cs` | Verschoben |
| `SetupViewModel.cs` | → `ConfigViewModel.cs` | Rename + erweitert |
| `RunViewModel.cs` | → `RunPipelineViewModel.cs` (merge) | Merge mit Pipeline-Logic |
| `ToolsViewModel.cs` | Bleibt | + Sub-Properties für DAT, Conversion, GameKey |
| `ConversionReviewViewModel.cs` | Bleibt | Keine Änderung nötig |
| — (neu) | `MissionControlViewModel.cs` | Dashboard-State, Sources, Intent, Health |
| — (neu) | `LibraryViewModel.cs` | Analyse-State, Sub-Tab-Selektion, Filter |
| — (neu) | `InspectorViewModel.cs` | Kontextabhängige Detail-Anzeige |
| — (neu) | `SystemViewModel.cs` | Log, Appearance, Automation, About |
| — (neu) | `CommandPaletteViewModel.cs` | Fuzzy-Search, Command-Registry |

### 10.3 Layout-Container

```xml
<!-- ShellWindow.xaml – Grundstruktur -->
<Window>
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="48"/>   <!-- CommandBar -->
      <RowDefinition Height="*"/>    <!-- Main Area -->
      <RowDefinition Height="52"/>   <!-- ActionRail -->
    </Grid.RowDefinitions>
    
    <!-- Row 0: CommandBar -->
    <views:CommandBarView Grid.Row="0"/>
    
    <!-- Row 1: Main Area (3 Columns) -->
    <Grid Grid.Row="1">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="72"/>          <!-- NavRail -->
        <ColumnDefinition Width="*"/>           <!-- Content + Drawer -->
        <ColumnDefinition Width="Auto"/>        <!-- Context Wing (collapsible) -->
      </Grid.ColumnDefinitions>
      
      <!-- Col 0: Navigation Rail -->
      <views:NavigationRailView Grid.Column="0"/>
      
      <!-- Col 1: Content Area -->
      <Grid Grid.Column="1">
        <Grid.RowDefinitions>
          <RowDefinition Height="32"/>          <!-- TabBar -->
          <RowDefinition Height="*"/>           <!-- Main View -->
          <RowDefinition Height="Auto"/>        <!-- Detail Drawer -->
        </Grid.RowDefinitions>
        
        <views:TabBarView Grid.Row="0"/>
        <ContentControl Grid.Row="1" Content="{Binding ActiveView}"/>
        <views:DetailDrawerView Grid.Row="2"/>
      </Grid>
      
      <!-- Col 2: Context Wing -->
      <views:ContextWingView Grid.Column="2" Width="280"
                             Visibility="{Binding IsContextWingOpen}"/>
    </Grid>
    
    <!-- Row 2: Action Rail -->
    <views:ActionRailView Grid.Row="2"/>
    
    <!-- Overlays -->
    <views:ProgressOverlayView Visibility="{Binding IsBusy}"/>
    <views:WizardOverlayView Visibility="{Binding ShowWizard}"/>
    <views:CommandPaletteView Visibility="{Binding ShowCommandPalette}"/>
    <views:ShortcutOverlayView Visibility="{Binding ShowShortcuts}"/>
    <ItemsControl ItemsSource="{Binding Notifications}"/>  <!-- Toasts -->
  </Grid>
</Window>
```

### 10.4 Navigation

```csharp
// NavigationService.cs
public enum NavSection { MissionControl, Library, Config, Tools, System }

public class NavigationService : ObservableObject
{
    public NavSection ActiveSection { get; set; }
    public string ActiveSubTab { get; set; }
    
    // History
    public void Navigate(NavSection section, string? subTab = null);
    public void GoBack();
    public void GoForward();
    
    // Command Palette integration
    public IEnumerable<CommandEntry> GetNavigationCommands();
}
```

### 10.5 Theme-Architektur

```
Themes/
├── _DesignTokens.xaml          ← Spacing, Radii, Type Scale (NICHT theme-abhängig)
│     SpacingXS: 4, SpacingS: 8, SpacingM: 12, SpacingL: 16, SpacingXL: 24
│     RadiusS: 4, RadiusM: 8, RadiusL: 12
│     FontCaption: 10, FontBody: 13, FontSubheader: 14, FontTitle: 18, FontHero: 24
│
├── _ControlTemplates.xaml      ← Button, TextBox, CheckBox etc. (referenziert nur DynamicResource)
│
├── SynthwaveDark.xaml          ← Nur SolidColorBrush + DropShadowEffect Definitionen
├── CleanDarkPro.xaml
├── RetroCRT.xaml
├── ArcadeNeon.xaml
├── LightStudio.xaml
└── HighContrast.xaml
```

**Theme-Loading in App.xaml.cs:**
```csharp
public void ApplyTheme(string themeKey)
{
    var themeUri = new Uri($"Themes/{themeKey}.xaml", UriKind.Relative);
    var themeDict = new ResourceDictionary { Source = themeUri };
    
    // Ersetze Theme-Dictionary (Index 0 ist immer das Theme)
    Resources.MergedDictionaries[0] = themeDict;
}
```

### 10.6 Styles / Resources / Tokens

**Token-Naming-Convention:**

| Ebene | Prefix | Beispiel | Beschreibung |
|-------|--------|---------|-------------|
| Palette | `Brush*` | `BrushAccentCyan` | Theme-spezifische Farbwerte |
| Semantic | `Status*` | `StatusSuccess`, `StatusDanger` | Bedeutungsbezogene Farben |
| Spacing | `Spacing*` | `SpacingM` (12px) | Layout-Abstände |
| Radius | `Radius*` | `RadiusCard` (8px) | Border-Radien |
| Font | `Font*` | `FontBody` (13) | Schriftgrößen |
| Padding | `Padding*` | `PaddingCard` (12,12,12,12) | Component-Padding |
| Style | Style-Key | `PrimaryButton`, `SectionCard` | Benannte Styles |

### 10.7 Was vereinfacht / entfernt / zusammengeführt werden sollte

| Was | Aktion | Begründung |
|-----|--------|-----------|
| `SettingsView.xaml` (520+ Zeilen, 8 Kategorien) | **Auflösen** in 7 spezialisierte Views | Zu monolithisch, zu viele Visibility-Switches |
| `ResultView.xaml` (380 Zeilen, 3 interne Sections) | **Auflösen** in 4 Sub-Views | Dashboard, Decisions, Safety, Report sind eigenständige Concerns |
| `MainViewModel` (5 Partial-Dateien) | **Aufteilen** in Shell + dedizierte Child-VMs | God-Object-Tendenz reduzieren |
| Roots-Management in Start UND Setup | **Konsolidieren** in MissionControl mit Referenz via Config | Eine Source-of-Truth |
| Status-Dots im Header (4×12px Ellipsen) | **Ersetzen** durch Health-Panel im Context Wing | Zu klein, zu wenig Information |
| Theme-Toggle per Ctrl+T (cycles) | **Erweitern** um Dropdown mit Preview-Swatches | Cycle ist blind, Dropdown zeigt alle Optionen |
| Log in ResultView.Protokoll | **Verschieben** nach System.Activity | Log ist System-Info, nicht Analyse-Content |

---

## 11. Migrationsplan

### Phase 0: Foundation (1-2 Wochen)

**Was**: Theme-System + Design-Tokens refactoren

| Task | Beschreibung |
|------|-------------|
| 0.1 | `_DesignTokens.xaml` erstellen mit Spacing, Radii, Type Scale (theme-agnostisch) |
| 0.2 | `_ControlTemplates.xaml` erstellen – alle Templates nutzen nur `DynamicResource` |
| 0.3 | Bestehende 3 Themes (Synthwave, Light, HighContrast) auf neues Token-System migrieren |
| 0.4 | 3 neue Themes erstellen: Clean Dark Pro, Retro CRT, Arcade Neon |
| 0.5 | `ThemeService` erweitern: Theme-Liste, Dropdown-Support, Persistence |
| 0.6 | Theme-Switcher-UI bauen (Dropdown mit Farbswatches) |

**Ergebnis**: 6 Themes funktionieren im bestehenden Layout. Kein Feature-Risiko.

### Phase 1: Shell-Umbau (2-3 Wochen)

**Was**: MainWindow → ShellWindow mit neuem Layout

| Task | Beschreibung |
|------|-------------|
| 1.1 | `ShellWindow.xaml` mit neuem 3-Zeilen × 3-Spalten Grid erstellen |
| 1.2 | `CommandBar` als eigene View extrahieren (Header) |
| 1.3 | `NavigationRail` als eigene View extrahieren (5 Items statt 4) |
| 1.4 | `ActionRail` refactoren (State-Machine-basiert) |
| 1.5 | `ContextWing` refactoren (erweiterte Inspector-Logik) |
| 1.6 | Tab-Bar-System implementieren (horizontal, kontextabhängig) |
| 1.7 | Navigation-Service mit History + SubTab-Support |
| 1.8 | Bestehende Views einbetten (ohne inhaltliche Änderung) |

**Ergebnis**: Neues Shell-Layout, bestehende Views unverändert eingebettet. Feature-Parität.

### Phase 2: View-Aufspaltung (2-3 Wochen)

**Was**: Monolithische Views aufteilen

| Task | Beschreibung |
|------|-------------|
| 2.1 | `SettingsView` auflösen → Config (Workflow, Filters, Profiles, Advanced) + Tools (External, DAT) + System (Activity, Appearance, Automation, About) |
| 2.2 | `ResultView` auflösen → Library (Overview, Decisions, Safety, Report) |
| 2.3 | `StartView` → `MissionControlView` umgestalten (Dashboard-Layout) |
| 2.4 | `SortView` → `ConfigWorkflowView` schlank machen |
| 2.5 | Log von ResultView nach System.Activity verschieben |
| 2.6 | GameKey-Preview von Settings nach Tools.GameKeyLab verschieben |

**Ergebnis**: Alle Views an ihrem neuen Platz. Information Architecture stimmt.

### Phase 3: ViewModel-Refactoring (1-2 Wochen)

**Was**: MainViewModel aufteilen

| Task | Beschreibung |
|------|-------------|
| 3.1 | `ShellViewModel` erstellen (nur Shell-State: Navigation, Theme, Phase) |
| 3.2 | `MissionControlViewModel` erstellen (Sources, Intent, Health, LastRun) |
| 3.3 | `LibraryViewModel` erstellen (Analyse-State, SubTabs, Filter) |
| 3.4 | `ConfigViewModel` erstellen (Merge von Setup + Settings-Teilen) |
| 3.5 | `SystemViewModel` erstellen (Log, Appearance, Automation) |
| 3.6 | `InspectorViewModel` erstellen (kontextabhängige Details) |
| 3.7 | `RunPipelineViewModel` konsolidieren |
| 3.8 | MainViewModel-Partial-Dateien entfernen |

**Ergebnis**: Saubere ViewModel-Trennung. Testbarkeit verbessert.

### Phase 4: New Features (1-2 Wochen)

**Was**: Neue UI-Features die im alten Layout nicht existierten

| Task | Beschreibung |
|------|-------------|
| 4.1 | Command Palette (Ctrl+K) implementieren |
| 4.2 | Safety Review View (Library.Safety) mit Blocked/Review/Unknown implementieren |
| 4.3 | Phase-Indicator in CommandBar implementieren (5-Step Pipeline) |
| 4.4 | Detail Drawer (collapsible bottom panel) implementieren |
| 4.5 | Conversion-Registry-Viewer in Tools bauen |
| 4.6 | System Health Widget im Context Wing |

**Ergebnis**: Alle neuen Features des Redesigns sind implementiert.

### Phase 5: Polish (1 Woche)

**Was**: Feinschliff

| Task | Beschreibung |
|------|-------------|
| 5.1 | Alle 6 Themes visuell polieren und auf Konsistenz prüfen |
| 5.2 | Keyboard Navigation vollständig testen (Tab-Order, Shortcuts, Focus) |
| 5.3 | Accessibility Audit (Screen Reader, High Contrast, Keyboard-only) |
| 5.4 | Animation/Transition Review (subtile Transitions, keine Blockaden) |
| 5.5 | Responsive-Verhalten testen (960px → 1920px+) |
| 5.6 | Empty-States für alle Views definieren (kein leerer Screen) |

**Ergebnis**: Produktionsreifes, poliertes UI.

### Prioritäts-Empfehlung

```
MUSS SOFORT      Phase 0 (Themes) + Phase 1 (Shell)
                  → Sichtbarster Impact, geringestes Feature-Risiko
                  
SOLLTE FOLGEN     Phase 2 (Views) + Phase 3 (ViewModels)
                  → Informationsarchitektur fertigstellen
                  
KANN FOLGEN       Phase 4 (New Features) + Phase 5 (Polish)
                  → Neue Capabilities, Qualitätssicherung
```

### Risikominimierung

- **Kein Big-Bang**: Jede Phase endet mit einem kompilierbaren, testbaren Zustand
- **Feature-Parität zuerst**: Alte Views werden initial 1:1 eingebettet, dann schrittweise umgebaut
- **Tests laufen durch**: Nach jeder Phase müssen alle bestehenden Tests grün sein
- **Rollback-fähig**: Alte Views bleiben verfügbar bis neue Views feature-komplett sind
- **Kein Datenverlust**: Keine Änderung an Core/Infrastructure – nur UI-Schicht

---

## Anhang A: Komponentenübersicht für Figma

```
COMPONENT LIBRARY

Atoms:
├── StatusDot (3 sizes: 8/12/16px, 6 colors)
├── Badge (Count/Label, 4 variants: Info/Warning/Danger/Success)
├── IconButton (48×48, hover/focus/active states)
├── ProgressBar (thin 4px, normal 8px, 6 theme colors)
├── GlowEffect (Cyan/Purple/Magenta, configurable blur)
├── Separator (horizontal/vertical, 1px)
├── Tooltip (dark surface, 8px radius, arrow)

Molecules:
├── MetricCard (Icon + Value + Label, optional trend arrow)
├── IntentCard (RadioButton + Icon + Title + Subtitle)
├── StatusRow (Label + Dot + Value)
├── NavItem (Icon + Label + ActiveIndicator)
├── TabItem (Label + ActiveBorder + Badge)
├── LogEntry (Timestamp + Level + Message, colored)
├── TreeRow (Expand/Collapse + Icon + Content + Badges)
├── ToolPathRow (Label + TextBox + Browse + StatusDot)
├── FilterChip (Label + Toggle, grouped)

Organisms:
├── CommandBar (Logo + CommandPalette + StatusOrbs + PhaseIndicator + ThemePicker)
├── NavigationRail (5 NavItems, vertical, with active glow)
├── TabBar (horizontal Tabs, context-dependent)
├── ActionRail (contextual Buttons + Progress, state-machine)
├── ContextWing (Inspector + QuickActions + StatusCards)
├── SourcesPanel (DropZone + SourceList + AddButton)
├── KpiGrid (4-5 MetricCards in UniformGrid)
├── ConsoleDistribution (Bar list + PieChart)
├── DedupeTree (Filterable TreeView with Winner/Loser rows)
├── SafetyReview (3 categorized lists: Blocked/Review/Unknown)
├── ThemeSwitcher (Dropdown with color swatches + preview)
├── CommandPalette (Search input + filtered command list)

Templates:
├── MissionControl (Dashboard layout: Sources + Intent + Health + LastRun)
├── LibraryOverview (KPIs + Charts + Distribution)
├── LibraryDecisions (TreeView + Inspector sidebar)
├── LibrarySafety (3 category lists)
├── LibraryReport (WebView + Controls)
├── ConfigWorkflow (Presets + Regions + Options + Summary)
├── ConfigFilters (Extension + Console filters)
├── ToolsExternal (Tool path grid + status)
├── SystemActivity (Log viewer + controls)
├── SystemAppearance (Theme picker + density + locale)

Pages:
├── MissionControlPage (Template + Context Wing content)
├── LibraryPage (4 Sub-Tabs, each with Template + Wing)
├── ConfigPage (4 Sub-Tabs)
├── ToolsPage (4 Sub-Tabs)
├── SystemPage (4 Sub-Tabs)
```

---

## Anhang B: Keyboard Shortcuts (Vollständig)

| Shortcut | Aktion | Kontext |
|----------|--------|---------|
| `F5` | Start Preview | Global |
| `ESC` | Cancel / Close Overlay | Global |
| `Ctrl+K` | Command Palette | Global |
| `Ctrl+T` | Theme Switcher öffnen | Global |
| `Ctrl+1` | Go to Mission Control | Global |
| `Ctrl+2` | Go to Library | Global |
| `Ctrl+3` | Go to Config | Global |
| `Ctrl+4` | Go to Tools | Global |
| `Ctrl+5` | Go to System | Global |
| `Ctrl+O` | Add Source Folder | Global |
| `Ctrl+R` | Open Report | Global |
| `Ctrl+Z` | Rollback | Post-Execute |
| `Ctrl+M` | Execute Move (mit Bestätigung) | Post-Preview |
| `Ctrl+W` | Toggle Watch Mode | Global |
| `Ctrl+I` | Toggle Context Wing | Global |
| `Ctrl+L` | Focus Log / Activity | Global |
| `Ctrl+←` | Navigate Back | Global |
| `Ctrl+→` | Navigate Forward | Global |
| `F1` | Shortcut Cheatsheet | Global |
| `Tab` | Next focusable element | Global |
| `Shift+Tab` | Previous focusable element | Global |

---

## Anhang C: Responsive Breakpoints

| Breite | Layout-Anpassung |
|--------|-----------------|
| < 960px | NavRail collapsed (nur Icons, kein Label), Context Wing hidden, TabBar scrollable |
| 960–1100px | NavRail compact (Icons + Micro-Label), Context Wing 240px |
| 1100–1400px | Standard-Layout (NavRail 72px, Content flex, Wing 280px) |
| 1400–1920px | Expanded (Wing 320px, Detail Drawer sichtbar) |
| > 1920px | Ultra-Wide (optionale 2. Content-Column oder breitere Charts) |
