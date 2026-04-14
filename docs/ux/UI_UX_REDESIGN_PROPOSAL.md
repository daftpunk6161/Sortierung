# Romulus – UI/UX Redesign Proposal

---

## 1. Executive Summary

### Kurzfazit

Romulus besitzt bereits eine solide technische Basis: saubere MVVM-Architektur, 6 Themes mit Design-Token-System, CommandPalette, Keyboard-Shortcuts und eine klare NavigationRail-Struktur. Das Fundament ist stark – aber das visuelle Erscheinungsbild, die Informationsarchitektur und die Workflow-Führung haben Potenzial für einen deutlichen Qualitätssprung.

### Wichtigste UX-Probleme

| # | Problem | Schwere |
|---|---------|---------|
| 1 | **Kein geführter Workflow** – Nutzer müssen selbst wissen, was als nächstes kommt | Hoch |
| 2 | **Flat Hierarchy** – alle Features/Kacheln gleichwertig, keine Priorisierung | Hoch |
| 3 | **Informationsdichte ohne Hierarchie** – KPI-Karten, Tabellen, Status alle auf gleicher Ebene | Mittel |
| 4 | **Danger Actions nicht ausreichend differenziert** – Move, Convert, Rollback wirken gleich | Hoch |
| 5 | **Unknown/Review/Blocked-Zustände unklar kommuniziert** – werden als Fehler wahrgenommen | Mittel |
| 6 | **Context Wing unternutzt** – oft leer oder generisch | Niedrig |
| 7 | **Accessibility-Lücken** – WCAG-Kontrast-Failures in 2 Themes, fehlende ARIA-Roles | Hoch |
| 8 | **Tool-Katalog als Kachelwand** – schwer navigierbar bei 20+ Features | Mittel |

### Zielbild

Romulus soll sich anfühlen wie ein **professionelles Mission Control für ROM-Sammlungen** – technisch, hochwertig, mit dem Charme einer Retro-Ästhetik, aber der Klarheit und Sicherheit eines modernen Developer-Tools. Denke an die Mischung aus:
- **VS Code** (klare Panelstruktur, Command Palette, Themes)
- **Grafana** (Dashboard-KPIs, Status-Monitoring)
- **Retro-Arcade-Terminal** (Phosphor-Glow, monospace Akzente, CRT-Charme)

### Stärkste Designrichtung

**Konzept A: "Professional Technical"** als Basis, mit modularen Retro-Akzenten über das Theme-System. Die Grundstruktur muss professionell und klar funktionieren – die Retro-Ästhetik kommt über Farbe, Typografie-Akzente und subtile Effekte, nicht über strukturelles Chaos.

---

## 2. Designziele

### Was die GUI leisten muss

```
┌─────────────────────────────────────────────────────────────┐
│                    DESIGN-PYRAMIDE                          │
│                                                             │
│                      ┌─────────┐                            │
│                      │ Delight │  Retro-Charme, Themes,     │
│                      │         │  Animation, Personality     │
│                    ┌─┴─────────┴─┐                          │
│                    │  Efficiency  │  Power-User Shortcuts,   │
│                    │              │  Dichte, Batch-Ops       │
│                  ┌─┴──────────────┴─┐                       │
│                  │    Clarity        │  Hierarchie, Flow,    │
│                  │                   │  States, Orientierung │
│                ┌─┴───────────────────┴─┐                    │
│                │       Safety           │  Danger-Abs.,      │
│                │                        │  Preview, Undo     │
│              ┌─┴────────────────────────┴─┐                 │
│              │        Accessibility         │  WCAG AA,      │
│              │                              │  Keyboard,     │
│              │                              │  Kontrast      │
│              └──────────────────────────────┘                │
└─────────────────────────────────────────────────────────────┘
```

### UX-Prinzipien

1. **Safety First** – Destruktive Aktionen sind immer 2-stufig (Preview → Confirm → Execute)
2. **Progressive Disclosure** – Einsteiger sehen Kern, Power-User decken Tiefe auf
3. **Single Source of Truth** – Jede KPI, jeder Status hat genau eine visuelle Quelle
4. **State Clarity** – Unknown, Review, Blocked sind keine Fehler, sondern dokumentierte Zustände mit klarer Visualisierung
5. **Workflow Gravity** – Der natürliche Flow zieht den Nutzer in die richtige Richtung
6. **Keyboard First** – Alle Kernoperationen ohne Maus erreichbar
7. **Theme Consistency** – Alle Themes müssen funktional gleichwertig sein (kein Feature geht in einem Theme verloren)

---

## 3. Informationsarchitektur

### Hauptbereiche (5-Zone Navigation – verfeinert)

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  ┌──────┐  ┌───────────────────────────────────┐  ┌──────────┐ │
│  │      │  │                                   │  │          │ │
│  │  N   │  │         CONTENT ZONE              │  │ CONTEXT  │ │
│  │  A   │  │                                   │  │  WING    │ │
│  │  V   │  │  ┌─────────────────────────────┐  │  │          │ │
│  │      │  │  │     SubTab / Breadcrumb     │  │  │ Adaptive │ │
│  │  R   │  │  ├─────────────────────────────┤  │  │ Inspector│ │
│  │  A   │  │  │                             │  │  │          │ │
│  │  I   │  │  │    Primary View Area        │  │  │ • Status │ │
│  │  L   │  │  │                             │  │  │ • Hints  │ │
│  │      │  │  │                             │  │  │ • Next   │ │
│  │  84px│  │  │                             │  │  │   Steps  │ │
│  │      │  │  │                             │  │  │ • Detail │ │
│  │      │  │  └─────────────────────────────┘  │  │          │ │
│  └──────┘  └───────────────────────────────────┘  └──────────┘ │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │              SMART ACTION BAR (88px)                     │   │
│  │  [Primary Action]  [Secondary]  [Danger]    [Status]    │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### Navigation Redesign

**NavigationRail – 5 Bereiche (neu strukturiert):**

| Icon | Label | Zweck | Sub-Tabs |
|------|-------|-------|----------|
| 🏠 | **Mission Control** | Start, Roots, Konfig, Dashboard | Roots · Config · Dashboard |
| 📊 | **Library** | Analyse, Ergebnisse, Entscheidungen | Results · Decisions · Safety · DAT Audit |
| 🔄 | **Pipeline** | Conversion, Sortierung, Batch-Ops | Conversion · Sorting · Batch |
| 🧰 | **Toolbox** | Feature-Katalog, Externe Tools, DAT | Features · External Tools · DAT Catalog |
| ⚙️ | **System** | Settings, Activity, Appearance, About | Appearance · Activity · About |

**Änderungen gegenüber Status Quo:**
- **Config** wird Teil von **Mission Control** (gehört zum Setup, nicht zu einer eigenen Top-Nav)
- **Pipeline** (NEU) – separater Bereich für Conversion/Sorting-Workflows, weil diese eigene States und Fortschritt haben
- **Toolbox** statt "Tools" – klarerer Name, enthält Feature-Katalog und DAT-Management
- **Reports** werden kontextbezogen eingeblendet, nicht als eigene Top-Nav (Reports sind Ergebnisse, nicht ein Bereich)

### Strukturprinzip: Hub & Spoke

```
                    ┌─────────────┐
                    │   Mission   │
                    │   Control   │
                    │  (Hub)      │
                    └──────┬──────┘
                           │
              ┌────────────┼────────────┐
              │            │            │
        ┌─────┴─────┐ ┌───┴────┐ ┌────┴─────┐
        │  Library   │ │Pipeline│ │  Toolbox  │
        │ (Analyse)  │ │(Action)│ │ (Utility) │
        └────────────┘ └────────┘ └──────────┘
```

- **Mission Control** = Startpunkt, Konfiguration, Übersicht
- **Library** = Lesen, Verstehen, Entscheiden
- **Pipeline** = Ausführen, Konvertieren, Sortieren
- **Toolbox** = Werkzeuge, Erweiterungen
- **System** = Meta-Einstellungen

---

## 4. Kernscreens

### 4.1 Mission Control (Start / Dashboard)

**Ziel:** Schneller Überblick, Setup, Einstieg in den Workflow.

```
┌─────────────────────────────────────────────────────────┐
│  MISSION CONTROL                                        │
│                                                         │
│  ┌─────────────────────────────────────────────┐        │
│  │  🎯 ROOTS                                   │        │
│  │  ┌─────────────────────────────┐             │        │
│  │  │  Drop folders here          │  [Browse]   │        │
│  │  │  or browse to add roots     │  [Recent ▾] │        │
│  │  └─────────────────────────────┘             │        │
│  │  ✓ D:\ROMs\Nintendo    (2,341 files)         │        │
│  │  ✓ D:\ROMs\Sega        (891 files)           │        │
│  │  ✗ E:\Archive          (offline)             │        │
│  └──────────────────────────────────────────────┘        │
│                                                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │  WORKFLOW     │  │  PROFILE     │  │  QUICK START │  │
│  │  Safe Sort    │  │  Default     │  │              │  │
│  │  [Change ▾]   │  │  [Change ▾]  │  │  [▶ Preview] │  │
│  │              │  │              │  │  [▶ Full Run] │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
│                                                         │
│  ─── LAST RUN ──────────────────────────────────────    │
│  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐        │
│  │ 1,247│ │   89 │ │  412 │ │   23 │ │  94% │        │
│  │Games │ │Winner│ │Dupes │ │ Junk │ │Health│        │
│  └──────┘ └──────┘ └──────┘ └──────┘ └──────┘        │
└─────────────────────────────────────────────────────────┘
```

| Aspekt | Detail |
|--------|--------|
| **Ziel** | Setup & Orientierung in < 5 Sekunden |
| **Primäre Aktion** | Root hinzufügen → Preview starten |
| **Sekundäre Aktion** | Workflow/Profil wechseln, letzte Ergebnisse ansehen |
| **Safety** | Kein direkter Execute von hier – nur Preview oder "Letzte Ergebnisse ansehen" |
| **KPIs** | Nur letzte Run-Ergebnisse (kompakt), kein Live-Update |
| **Fehlerzustände** | Offline-Roots rot markiert, fehlende DATs als Warning |
| **Context Wing** | Empfehlungen ("3 neue DATs verfügbar"), nächster empfohlener Schritt |

---

### 4.2 Library – Results View

**Ziel:** Ergebnisse der Analyse/des Runs verstehen.

```
┌─────────────────────────────────────────────────────────┐
│  LIBRARY > Results                                      │
│  ┌────────┬────────────┬─────────┬──────────┐          │
│  │Results │ Decisions  │ Safety  │ DAT Audit│          │
│  └────────┴────────────┴─────────┴──────────┘          │
│                                                         │
│  ┌─ RUN SUMMARY ────────────────────────────────────┐   │
│  │  ✅ Analysis complete · 2,341 files · 14 consoles│   │
│  │  ⚠️  23 items need review · 5 blocked            │   │
│  └──────────────────────────────────────────────────┘   │
│                                                         │
│  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐                  │
│  │ 1,247│ │  412 │ │   23 │ │  94% │                  │
│  │ Games│ │ Dupes│ │ Junk │ │Health│                  │
│  │      │ │      │ │      │ │      │                  │
│  └──────┘ └──────┘ └──────┘ └──────┘                  │
│                                                         │
│  ┌─ CONSOLE BREAKDOWN ─────────────────────────────┐   │
│  │  ████████████ SNES (342)                        │   │
│  │  ████████     GBA  (215)                        │   │
│  │  ██████       NES  (189)                        │   │
│  │  ████         GB   (134)                        │   │
│  │  ██           N64  ( 67)                        │   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  [📄 Open Report]  [↩️ Rollback]  [▶ Execute Move]     │
└─────────────────────────────────────────────────────────┘
```

| Aspekt | Detail |
|--------|--------|
| **Ziel** | Run-Ergebnisse verstehen, Entscheidungen nachvollziehen |
| **Primäre Aktion** | Report öffnen, Details ansehen |
| **Sekundäre Aktion** | Rollback, Execute Move |
| **Safety** | Execute Move ist **Danger-Button** (rot, 2-stufig), Rollback ist Warning-Button |
| **KPIs** | Games, Dupes, Junk, Health Score, DAT Hits, Console Distribution |
| **Fehlerzustände** | "No run data" Placeholder, Partial-Run Warning, Blocked-Items Highlight |
| **Context Wing** | Details zum ausgewählten Item, Winner-Erklärung, Score-Breakdown |

---

### 4.3 Library – Decisions View

**Ziel:** Scoring-Entscheidungen transparent machen.

```
┌─────────────────────────────────────────────────────────┐
│  LIBRARY > Decisions                                    │
│                                                         │
│  ┌─ FILTER ────────────────────────────────────────┐   │
│  │  [All ▾] [Console ▾] [Status ▾]  🔍 Search     │   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  ┌─────────────────────────────────────────────────┐   │
│  │ Game Key          │ Winner    │ Score │ Status   │   │
│  ├───────────────────┼───────────┼───────┼──────────┤   │
│  │ Super Mario World │ (U).sfc  │  94   │ ✅ Clear │   │
│  │ Zelda ALTTP       │ (E).sfc  │  91   │ ⚠️ Review│   │
│  │ Metroid           │ —        │  —    │ 🔒Blocked│   │
│  │ Unknown ROM xyz   │ —        │  —    │ ❓Unknown│   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  ─── STATUS LEGENDE ───                                 │
│  ✅ Clear    = Eindeutiger Winner, keine Konflikte      │
│  ⚠️ Review   = Manuelle Prüfung empfohlen               │
│  🔒 Blocked  = Automatische Entscheidung nicht möglich  │
│  ❓ Unknown  = Kein Match in Regeln/DAT gefunden        │
└─────────────────────────────────────────────────────────┘
```

| Aspekt | Detail |
|--------|--------|
| **Status-Kommunikation** | Unknown/Review/Blocked sind **keine Fehler**, sondern dokumentierte Zustände |
| **Visuelle Kodierung** | Farbe + Icon + Text (nie nur Farbe!) |
| **Context Wing** | Score-Breakdown, Region-Info, Alternativ-Kandidaten für ausgewähltes Item |

---

### 4.4 Library – Safety View

**Ziel:** Alle Warnungen, Konflikte, Risiken auf einen Blick.

```
┌─────────────────────────────────────────────────────────┐
│  LIBRARY > Safety                                       │
│                                                         │
│  ┌─ SEVERITY OVERVIEW ─────────────────────────────┐   │
│  │  🔴 Critical: 2    🟡 Warning: 12    🔵 Info: 45│   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  ┌─────────────────────────────────────────────────┐   │
│  │ 🔴 Path collision: 2 files target same path     │   │
│  │    └─ Mario Kart (U).zip → SNES/Mario Kart.zip │   │
│  │    └─ Mario Kart (E).zip → SNES/Mario Kart.zip │   │
│  ├─────────────────────────────────────────────────┤   │
│  │ 🟡 Missing DAT for console: Wonderswan          │   │
│  │    └─ 12 files cannot be verified               │   │
│  ├─────────────────────────────────────────────────┤   │
│  │ 🔵 Region fallback used for 45 files            │   │
│  │    └─ Primary region not available              │   │
│  └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

| Aspekt | Detail |
|--------|--------|
| **Severity-Levels** | Critical (🔴), Warning (🟡), Info (🔵) |
| **Gruppierung** | Nach Severity, dann nach Kategorie |
| **Safety** | Critical-Items blockieren Execute (sichtbar verknüpft) |
| **Context Wing** | Details zum ausgewählten Warnung, empfohlene Aktion |

---

### 4.5 Pipeline – Conversion View

**Ziel:** Conversion-Plans anzeigen, bestätigen, ausführen.

```
┌─────────────────────────────────────────────────────────┐
│  PIPELINE > Conversion                                  │
│                                                         │
│  ┌─ PLAN SUMMARY ──────────────────────────────────┐   │
│  │  📦  142 conversions planned                     │   │
│  │  💾  Estimated: 2.3 GB → 1.8 GB (−22%)         │   │
│  │  ⏱️  Estimated time: ~8 min                      │   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  ┌─ STEP INDICATOR ────────────────────────────────┐   │
│  │  ① Preview  ──▶  ② Review  ──▶  ③ Execute      │   │
│  │  [current]       [next]          [locked]       │   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  ┌─────────────────────────────────────────────────┐   │
│  │ Source           │ Target    │ Tool    │ Status  │   │
│  ├──────────────────┼───────────┼─────────┼─────────┤   │
│  │ game.cso         │ game.iso  │ maxcso  │ Pending │   │
│  │ rom.nkit.iso     │ rom.iso   │ nkit    │ Pending │   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  [▶ Start Conversion]  ← Danger-styled, requires Review│
└─────────────────────────────────────────────────────────┘
```

| Aspekt | Detail |
|--------|--------|
| **Step Indicator** | 3-stufig: Preview → Review → Execute (visuell als Stepper) |
| **Safety** | Execute erst nach Review freigeschaltet (visuell "locked") |
| **Progress** | Live-Fortschrittsbalken pro File und gesamt während Execute |
| **Context Wing** | Tool-Info, Format-Info, mögliche Risiken für ausgewähltes Item |

---

### 4.6 Reports / Audit / Rollback

**Ziel:** Ergebnisse dokumentieren, vergangene Runs einsehen, Rollback auslösen.

```
┌─────────────────────────────────────────────────────────┐
│  LIBRARY > Report                                       │
│                                                         │
│  ┌─ REPORT VIEWER ─────────────────────────────────┐   │
│  │  [HTML Report embedded via WebView2]            │   │
│  │                                                  │   │
│  │  ┌──────────────────────────────────────────┐   │   │
│  │  │  ← Live-rendered HTML Report              │   │   │
│  │  │     mit Interaktion und Suche            │   │   │
│  │  └──────────────────────────────────────────┘   │   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  ┌─ ACTIONS ───────────────────────────────────────┐   │
│  │  [📋 Copy to Clipboard]                         │   │
│  │  [💾 Export CSV]                                 │   │
│  │  [📂 Open in Browser]                           │   │
│  │  [↩️ Rollback Last Run]  ← Warning-Button       │   │
│  └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

---

### 4.7 Toolbox – Feature Catalog

Siehe Abschnitt 6 für das vollständige Redesign.

---

### 4.8 System – Appearance

**Ziel:** Theme-Wechsel, UI-Mode, Lokalisierung.

```
┌─────────────────────────────────────────────────────────┐
│  SYSTEM > Appearance                                    │
│                                                         │
│  ┌─ THEME ─────────────────────────────────────────┐   │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐        │   │
│  │  │Synthwave │ │Clean Dark│ │Retro CRT │        │   │
│  │  │  ◉       │ │  ○       │ │  ○       │        │   │
│  │  └──────────┘ └──────────┘ └──────────┘        │   │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐        │   │
│  │  │  Light   │ │  Hi-Con  │ │  Arcade  │        │   │
│  │  │  ○       │ │  ○       │ │  ○       │        │   │
│  │  └──────────┘ └──────────┘ └──────────┘        │   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  ┌─ MODE ──────────────────────────────────────────┐   │
│  │  ○ Simple     ◉ Expert                          │   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  ┌─ LIVE PREVIEW ──────────────────────────────────┐   │
│  │  [Mini-Vorschau des ausgewählten Themes         │   │
│  │   mit Beispiel-KPIs und Buttons]                │   │
│  └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

---

## 5. Workflow-Konzept

### 5.1 Standard-Workflow (Happy Path)

```
┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐
│  1. ADD  │───▶│2.PREVIEW │───▶│3. REVIEW │───▶│4.EXECUTE │───▶│5. RESULT │
│  ROOTS   │    │ / ANALYSE│    │/ DECIDE  │    │  / MOVE  │    │/ REPORT  │
│          │    │          │    │          │    │          │    │          │
│ Ordner   │    │ DryRun   │    │ Ergebnis │    │ Bestätig.│    │ Report   │
│ wählen   │    │ starten  │    │ prüfen   │    │ + Apply  │    │ + Undo   │
└──────────┘    └──────────┘    └──────────┘    └──────────┘    └──────────┘
     │                │                │                │               │
     ▼                ▼                ▼                ▼               ▼
  Mission          Mission          Library          Library         Library
  Control          Control          > Results        > Results       > Report
  > Roots          > Dashboard      > Decisions      (Confirm        (Report +
                   (auto-nav)       > Safety         Dialog)         Rollback)
```

**Flow-Regeln:**
1. **Add Roots** → Pflicht. Ohne Roots kein Preview möglich.
2. **Preview** → F5 oder Button. Automatische Navigation zu Library > Results nach Abschluss.
3. **Review** → Empfohlen bei Unknown/Blocked/Review-Items. Context Wing zeigt "X items need attention".
4. **Execute** → Nur über Danger-Confirm-Dialog. Zeigt Summary VOR Ausführung.
5. **Result** → Automatische Navigation zu Report. Rollback-Button sichtbar.

### 5.2 Review-Erzwingung

```
┌─────────────────────────────────────────────────────┐
│  REVIEW-GATE                                        │
│                                                     │
│  Wenn nach Preview:                                 │
│  • ≥ 1 Blocked Item    → Execute gesperrt 🔒       │
│  • ≥ 1 Review Item     → Execute möglich, aber     │
│                           Warnung im Confirm-Dialog │
│  • ≥ 1 Critical Safety → Execute gesperrt 🔒       │
│  • Alles Clear         → Execute normal möglich     │
│                                                     │
│  Im Confirm-Dialog:                                 │
│  "23 items marked for review were not inspected.    │
│   Do you want to proceed anyway?"                   │
│  [Cancel]  [Review Now]  [Proceed Anyway ⚠️]        │
└─────────────────────────────────────────────────────┘
```

### 5.3 Status-Kommunikation: Unknown / Review / Blocked

**Design-Grundsatz:** Diese Zustände sind **keine Fehler**. Sie sind bewusste, erwartbare Ergebnisse.

| Status | Icon | Farbe | Kommunikation |
|--------|------|-------|---------------|
| **Clear** | ✅ | `Success` (Grün) | "Eindeutig entschieden, keine Konflikte" |
| **Review** | 👁️ | `Warning` (Amber) | "Automatisch entschieden, manuelle Prüfung empfohlen" |
| **Blocked** | 🔒 | `Danger` (Rot) | "Keine automatische Entscheidung möglich, manuell nötig" |
| **Unknown** | ❓ | `Info` (Blau/Cyan) | "Nicht erkannt – kein DAT-Match, keine Regel anwendbar" |

**Immer:** Icon + Farbe + Text (nie nur Farbe!)

**Tooltip bei Hover:** Erklärt den Zustand in einem Satz + empfohlene nächste Aktion.

### 5.4 Danger-Absicherung

```
┌─────────────────────────────────────────────────────────┐
│              ⚠️ DANGER CONFIRM DIALOG                    │
│                                                         │
│  ┌─────────────────────────────────────────────────┐   │
│  │  You are about to MOVE 412 files.               │   │
│  │                                                  │   │
│  │  Summary:                                        │   │
│  │  • 89 winners kept                               │   │
│  │  • 412 duplicates → Trash                        │   │
│  │  • 23 junk files → Junk folder                   │   │
│  │  • 5 blocked items → SKIPPED                     │   │
│  │                                                  │   │
│  │  ⚠️ 23 items marked for review not inspected     │   │
│  │                                                  │   │
│  │  This action can be undone via Rollback.          │   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
│  Type "MOVE" to confirm: [____________]                 │
│                                                         │
│  [Cancel]                              [Execute Move]   │
│                                         ^^^^^^^^^^^^^^^^│
│                                         Red, only active│
│                                         after typing    │
└─────────────────────────────────────────────────────────┘
```

**Danger-Level-System:**

| Level | Trigger | Absicherung |
|-------|---------|-------------|
| **Low** | Report öffnen, CSV export | Kein Dialog nötig |
| **Medium** | Rollback | Warning-Dialog mit Summary |
| **High** | Move, Convert | Danger-Dialog + Typing-Bestätigung |
| **Critical** | Batch-Delete, Cleanup | Danger-Dialog + Typing + Countdown |

---

## 6. Tool-Katalog Redesign

### Ist-Problem

Der aktuelle Tool-Katalog ist eine **flache Kachelwand** mit gleichwertigen Karten. Bei 20+ Features ist das:
- schwer zu scannen
- keine Priorisierung erkennbar
- seltene Features dominieren gleichwertig mit Kernfunktionen

### Neues Konzept: **Categorized Command Center**

```
┌─────────────────────────────────────────────────────────┐
│  TOOLBOX > Features                                     │
│                                                         │
│  ┌─ CORE WORKFLOWS ────────────────────────────────┐   │
│  │  Diese Workflows decken Standardoperationen ab.  │   │
│  │                                                  │   │
│  │  ┌──────────────┐ ┌──────────────┐              │   │
│  │  │ 🔍 Analyse   │ │ 🎯 Dedupe    │              │   │
│  │  │ Safe Sort    │ │ Full Sort    │              │   │
│  │  │ ⭐ Empfohlen │ │ ⭐ Empfohlen │              │   │
│  │  └──────────────┘ └──────────────┘              │   │
│  │  ┌──────────────┐ ┌──────────────┐              │   │
│  │  │ 🔄 Convert   │ │ ✅ Verify    │              │   │
│  │  │ Format Conv. │ │ DAT Verify   │              │   │
│  │  │              │ │              │              │   │
│  │  └──────────────┘ └──────────────┘              │   │
│  └──────────────────────────────────────────────────┘   │
│                                                         │
│  ┌─ MAINTENANCE ───────────────────────────────────┐   │
│  │  ┌──────────────┐ ┌──────────────┐              │   │
│  │  │ 🧹 Cleanup   │ │ ↩️ Rollback  │              │   │
│  │  │ Junk Removal │ │ Undo Last    │              │   │
│  │  │ ⚠️ Advanced  │ │              │              │   │
│  │  └──────────────┘ └──────────────┘              │   │
│  └──────────────────────────────────────────────────┘   │
│                                                         │
│  ┌─ ADVANCED ─ [Expert Only] ──────────────────────┐   │
│  │  🔬 Hash Verify  │ 📊 Collection Diff │ ...     │   │
│  │  🧪 Experimental │ 🔧 DAT Rename     │         │   │
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

### Gruppierung

| Gruppe | Sichtbarkeit | Inhalt |
|--------|-------------|--------|
| **Core Workflows** | Immer sichtbar, prominent | Analyse, Dedupe, Convert, Verify |
| **Maintenance** | Immer sichtbar, sekundär | Cleanup, Rollback, Repair |
| **Reporting** | Immer sichtbar, sekundär | Reports, Export, Audit Log |
| **Advanced** | Nur im Expert-Mode | Hash Verify, Collection Diff, DAT Rename, Batch Ops |
| **Experimental** | Nur im Expert-Mode, mit Badge | Neue Features in Erprobung |
| **Planned** | Deaktiviert, mit Badge | Geplante Features (kein "Coming Soon" ohne Plan) |

### Badge-System

| Badge | Visuell | Bedeutung |
|-------|---------|-----------|
| ⭐ **Empfohlen** | Gold-Stern, Top-Ecke | Empfohlener Einstiegspunkt für diesen Use Case |
| 🟢 **Stabil** | Grüner Dot, kein extra Badge | Produktionsreif, keine Einschränkung |
| 🟡 **Guided** | Amber Dot + "Guided" | Funktioniert, aber empfiehlt geführten Workflow |
| 🔬 **Experimental** | Lila Badge | In Erprobung, Ergebnisse prüfen |
| 📋 **Planned** | Grauer Badge, deaktiviert | Geplant, noch nicht implementiert |
| 🔒 **Requires Run** | Lock-Icon | Braucht aktive Run-Daten |
| ⚠️ **Advanced** | Amber Badge | Nur für erfahrene Nutzer empfohlen |

### Ansichts-Modi

- **Grid** (Default): Kacheln in Kategorien, visual scanning
- **List**: Kompakte Liste mit Beschreibung, für Power-User
- **Search**: Fuzzy-Filter über alle Features (integriert mit CommandPalette Ctrl+K)

---

## 7. Designsystem

### 7.1 Farbsystem

**Semantische Farbrollen (Theme-unabhängig definiert):**

```
┌─ SURFACES ─────────────────────────────────────────┐
│  Background     = Haupthintergrund                  │
│  Surface        = Karten, Panels                    │
│  SurfaceAlt     = Sekundäre Panels, NavRail         │
│  SurfaceLight   = Hover-States, erhöhte Flächen     │
│  Scrim          = Modal-Overlay (80% opacity)        │
└─────────────────────────────────────────────────────┘

┌─ TEXT ─────────────────────────────────────────────┐
│  TextPrimary    = Haupttext (WCAG AA auf Background)│
│  TextMuted      = Sekundärtext (WCAG AA auf Surface)│
│  TextOnAccent   = Text auf Akzentfarben             │
│  TextOnDanger   = Text auf Danger-Backgrounds       │
└─────────────────────────────────────────────────────┘

┌─ ACCENTS ──────────────────────────────────────────┐
│  AccentPrimary  = Hauptakzent (Focus, Links, CTA)   │
│  AccentSecondary= Sekundärakzent (Badges, Tags)     │
└─────────────────────────────────────────────────────┘

┌─ SEMANTIC ─────────────────────────────────────────┐
│  Success        = Bestätigung, Clear, OK            │
│  SuccessBg      = Hintergrund für Success-Bereiche  │
│  Warning        = Achtung, Review, Hinweis          │
│  WarningBg      = Hintergrund für Warnings          │
│  Danger         = Fehler, Blocked, Danger Action    │
│  DangerBg       = Hintergrund für Danger-Bereiche   │
│  Info           = Information, Unknown, Neutral      │
│  InfoBg         = Hintergrund für Info-Bereiche     │
└─────────────────────────────────────────────────────┘

┌─ INTERACTION ──────────────────────────────────────┐
│  BorderDefault  = Standard-Border                   │
│  BorderHover    = Hover-Border                      │
│  BorderFocus    = Focus-Ring (= AccentPrimary)      │
│  ButtonPressed  = Press-State                       │
│  InputSelection = Text-Selektion                    │
└─────────────────────────────────────────────────────┘
```

### 7.2 Typografie

```
┌─ FONT STACK ────────────────────────────────────────┐
│                                                      │
│  UI Font:     Segoe UI (System)                      │
│  Mono Font:   Cascadia Code / Consolas (Fallback)    │
│  Display:     Segoe UI Semibold (Headings)           │
│                                                      │
│  ┌─ SCALE ────────────────────────────────────────┐ │
│  │  Display:  22px / 28px line  / SemiBold        │ │
│  │  Heading:  18px / 24px line  / SemiBold        │ │
│  │  Subhead:  16px / 22px line  / Medium          │ │
│  │  Body:     14px / 20px line  / Regular         │ │
│  │  Caption:  12px / 16px line  / Regular         │ │
│  │  Micro:    11px / 14px line  / Regular         │ │
│  │  Mono:     12px / 16px line  / Regular (Mono)  │ │
│  └────────────────────────────────────────────────┘ │
│                                                      │
│  Retro-Akzent: Mono-Font für:                        │
│  • Pfade, Hashes, GameKeys                           │
│  • KPI-Zahlen (große Werte)                          │
│  • Tabellen-Daten                                    │
│  • Status-Codes                                      │
└──────────────────────────────────────────────────────┘
```

### 7.3 Spacing-System (bestätigt, Status Quo gut)

```
┌─ SPACING SCALE ──────────────────────────────────────┐
│                                                       │
│  4px   (XS)  — Inline-Abstände, Icon-Margins         │
│  8px   (SM)  — Enge Abstände, grouped elements        │
│  12px  (MD)  — Standard-Padding inner                 │
│  16px  (LG)  — Card-Padding, Section-Margins          │
│  24px  (XL)  — Bereichsabstände                       │
│  32px  (XXL) — Hero-Abstände, große Sektionen         │
│  48px  (3XL) — Page-Level-Spacing (NEU)               │
│                                                       │
│  Basis: 4px Grid (alle Werte Vielfache von 4)         │
└───────────────────────────────────────────────────────┘
```

### 7.4 Komponentensystem

#### Buttons

```
┌─ BUTTON HIERARCHY ────────────────────────────────────┐
│                                                        │
│  ┌────────────┐  Primary     AccentPrimary bg          │
│  │  ▶ Action  │  Filled button, white text             │
│  └────────────┘  Für: Hauptaktion pro Screen           │
│                                                        │
│  ┌────────────┐  Secondary   Border only, AccentPrimary│
│  │    Action   │  Outlined button                       │
│  └────────────┘  Für: Alternative Aktionen              │
│                                                        │
│  ┌────────────┐  Ghost       Transparent, text only    │
│  │    Action   │  Minimal button                        │
│  └────────────┘  Für: Tertiäre Aktionen, Toolbar        │
│                                                        │
│  ┌────────────┐  Danger      Danger bg, urgent styling │
│  │  ⚠ DELETE  │  Red-toned, 2-step activation          │
│  └────────────┘  Für: Destruktive Aktionen              │
│                                                        │
│  ┌────────────┐  Warning     Warning border, amber     │
│  │  ↩ Undo    │  Amber-toned                           │
│  └────────────┘  Für: Rollback, Rücknahme               │
│                                                        │
│  States:                                               │
│  • Default → Hover (lighten) → Active (darken)         │
│  • Focus: 2px AccentPrimary ring                       │
│  • Disabled: 40% opacity + cursor: not-allowed         │
│  • Loading: Spinner + disabled                         │
└────────────────────────────────────────────────────────┘
```

#### Karten (Metric Cards, Tool Cards, Info Cards)

```
┌─ CARD SYSTEM ──────────────────────────────────────────┐
│                                                         │
│  ┌─────────────────────────┐  MetricCard                │
│  │  ▲ 1,247                │  • Große Mono-Zahl         │
│  │  Games                  │  • Label darunter (Caption) │
│  │  +12% vs last run       │  • Optionaler Trend        │
│  └─────────────────────────┘  • Surface bg, Border      │
│                                                         │
│  ┌─────────────────────────┐  ToolCard                  │
│  │  🔍 Analyse             │  • Icon + Titel            │
│  │  Safe Sort & Preview    │  • Beschreibung            │
│  │  ⭐ Empfohlen           │  • Badge (Maturity)        │
│  │  [Run]                  │  • Primäre Aktion          │
│  └─────────────────────────┘  • Hover: SurfaceLight     │
│                                                         │
│  ┌─────────────────────────┐  StatusCard                │
│  │  🟡 23 items to review  │  • Severity-Farbe links    │
│  │  Click to open review   │  • Beschreibung            │
│  └─────────────────────────┘  • Aktion bei Klick        │
│                                                         │
│  Card-Grundwerte:                                       │
│  • Padding: 16px                                        │
│  • BorderRadius: 10px (LG)                              │
│  • Border: 1px BorderDefault                            │
│  • Background: Surface                                  │
│  • Hover: SurfaceLight bg + AccentPrimary border        │
│  • Focus: 2px AccentPrimary ring                        │
└─────────────────────────────────────────────────────────┘
```

#### Tabellen

```
┌─ TABLE SYSTEM ──────────────────────────────────────────┐
│                                                          │
│  Header: SurfaceAlt bg, TextMuted, Caption-Size, Bold    │
│  Row:    Surface bg, TextPrimary, Body-Size              │
│  Row Alt: SurfaceAlt bg (subtle striping)                │
│  Row Hover: SurfaceLight bg                              │
│  Row Selected: AccentPrimary border-left (3px)           │
│  Cell Padding: 8px 12px                                  │
│  Border: 1px BorderDefault zwischen Rows                 │
│                                                          │
│  Mono-Font für: Pfade, Hashes, Scores, Dateigrößen       │
│  Regular-Font für: Namen, Beschreibungen, Status          │
│                                                          │
│  Responsive: Schmale Spalten collapsible in Expert-Mode   │
│              Weite Spalten prioritär (Name, Status)       │
└──────────────────────────────────────────────────────────┘
```

#### Status-System

```
┌─ STATUS INDICATORS ─────────────────────────────────────┐
│                                                          │
│  Immer: ICON + FARBE + TEXT (Triple-Encoding)            │
│                                                          │
│  ┌────┐                                                  │
│  │ ✅ │  Success   #00FF88 (Theme) + Check-Icon + "OK"   │
│  │ ⚠️ │  Warning   #FFB700 (Theme) + Alert-Icon + Text   │
│  │ 🔴 │  Error     #FF0044 (Theme) + X-Icon + Text       │
│  │ 🔵 │  Info      AccentPrimary + Info-Icon + Text       │
│  │ 👁️ │  Review    Warning + Eye-Icon + "Needs Review"    │
│  │ 🔒 │  Blocked   Danger + Lock-Icon + "Blocked"         │
│  │ ❓ │  Unknown   Info + Question-Icon + "Unknown"        │
│  │ ⏳ │  Pending   TextMuted + Clock-Icon + "Pending"      │
│  └────┘                                                  │
│                                                          │
│  StatusDot: 8px Kreis, semantische Farbe                 │
│  StatusBadge: Pill-Form, bg + text, 12px font            │
│  StatusBanner: Full-width, icon + text + action          │
└──────────────────────────────────────────────────────────┘
```

---

## 8. Theme-Vorschläge

### Theme 1: **Synthwave Dusk** (Standard / Professionell-Retro)

```
╔════════════════════════════════════════════════════════╗
║  SYNTHWAVE DUSK                                       ║
║                                                       ║
║  Charakter:  Professionelles Dunkel-Theme mit         ║
║              subtilen Neon-Akzenten. Die "Default-    ║
║              Identität" von Romulus.                   ║
║                                                       ║
║  Farbwelt:                                            ║
║  ┌─────────┬──────────┬─────────────────────────┐    ║
║  │ Bg      │ #0D0D1F  │ Deep Navy               │    ║
║  │ Surface │ #1A1A3A  │ Dark Purple              │    ║
║  │ Accent  │ #00E5EE  │ Gedämpftes Cyan (→leicht│    ║
║  │         │          │ wärmer als aktuell)      │    ║
║  │ Second. │ #A855F7  │ Sanftes Purple           │    ║
║  │ Text    │ #E8E8F8  │ Lavender White           │    ║
║  │ Muted   │ #A0A0D0  │ Heller als aktuell (WCAG)│    ║
║  │ Success │ #34D399  │ Mint Green               │    ║
║  │ Warning │ #FBBF24  │ Warm Amber               │    ║
║  │ Danger  │ #F43F5E  │ Rose Red                 │    ║
║  └─────────┴──────────┴─────────────────────────┘    ║
║                                                       ║
║  Wann sinnvoll: Default, für alle Nutzer.             ║
║  Die "Romulus-Identität".                             ║
║                                                       ║
║  Risiken: Neon-Akzente nicht übertreiben.             ║
║  Cyan nur für interaktive Elemente, nie als Fläche.   ║
║                                                       ║
║  Besonderheit: Subtiler Glow-Effekt auf ProgressBar   ║
║  und aktiver NavRail-Button. Sonst clean.             ║
╚════════════════════════════════════════════════════════╝
```

### Theme 2: **Phosphor Terminal** (Retro-Neon / CRT)

```
╔════════════════════════════════════════════════════════╗
║  PHOSPHOR TERMINAL                                    ║
║                                                       ║
║  Charakter:  Authentisches CRT-Terminal-Feeling.       ║
║              Phosphor-Grün auf Schwarz. Monospace-     ║
║              Akzente überall. Scanlines optional.      ║
║                                                       ║
║  Farbwelt:                                            ║
║  ┌─────────┬──────────┬─────────────────────────┐    ║
║  │ Bg      │ #050A05  │ CRT Black               │    ║
║  │ Surface │ #0C140C  │ Dark Terminal            │    ║
║  │ Accent  │ #33FF33  │ Phosphor Green           │    ║
║  │ Second. │ #FFAA00  │ Amber (Warnung/Sekundär) │    ║
║  │ Text    │ #B8FFB8  │ Soft Green (↑Kontrast)   │    ║
║  │ Muted   │ #5A9A5A  │ Dim Green (WCAG-fix)     │    ║
║  │ Success │ #33FF33  │ Bright Green             │    ║
║  │ Warning │ #FFAA00  │ Amber                    │    ║
║  │ Danger  │ #FF4444  │ Alert Red                │    ║
║  │OnAccent │ #000000  │ Black (auf grünem Bg)    │    ║
║  └─────────┴──────────┴─────────────────────────┘    ║
║                                                       ║
║  Wann sinnvoll: Für Retro-Enthusiasten, Sammler,     ║
║  die den Nostalgie-Faktor lieben.                     ║
║                                                       ║
║  Risiken:                                             ║
║  • TextOnAccent = Schwarz auf Grün → nur bei großen   ║
║    Touch-Targets/Buttons verwenden                    ║
║  • Monochrom-Grün ermüdet bei langen Sessions →       ║
║    Amber als Sekundärfarbe zur Entlastung             ║
║  • Scanline-Effekt optional, nicht default            ║
║                                                       ║
║  Besonderheit: KPI-Zahlen in Mono-Phosphor.           ║
║  Tabellen-Headers in Amber. Cursor-Blink auf Focus.   ║
╚════════════════════════════════════════════════════════╝
```

### Theme 3: **Clean Daylight** (Helle Variante)

```
╔════════════════════════════════════════════════════════╗
║  CLEAN DAYLIGHT                                       ║
║                                                       ║
║  Charakter:  Helles, sauberes Theme für Tageslicht-   ║
║              Nutzung. Professionell, fast Enterprise-  ║
║              artig. Keine Spielerei.                   ║
║                                                       ║
║  Farbwelt:                                            ║
║  ┌─────────┬──────────┬─────────────────────────┐    ║
║  │ Bg      │ #F8F9FA  │ Near White               │    ║
║  │ Surface │ #FFFFFF  │ Pure White               │    ║
║  │ SurfAlt │ #F0F1F3  │ Light Gray               │    ║
║  │ Accent  │ #2563EB  │ Professional Blue         │    ║
║  │ Second. │ #7C3AED  │ Deep Purple              │    ║
║  │ Text    │ #1A1A2E  │ Near Black               │    ║
║  │ Muted   │ #6B7280  │ Medium Gray (WCAG AAA)   │    ║
║  │ Success │ #059669  │ Deep Green               │    ║
║  │ Warning │ #D97706  │ Deep Amber               │    ║
║  │ Danger  │ #DC2626  │ Deep Red                 │    ║
║  │ Border  │ #E5E7EB  │ Light Gray Border        │    ║
║  └─────────┴──────────┴─────────────────────────┘    ║
║                                                       ║
║  Wann sinnvoll: Helle Umgebungen, Tageslicht,        ║
║  Nutzer die dunkle Themes nicht mögen, Präsentationen.║
║                                                       ║
║  Risiken: Kann "langweilig" wirken → Akzentfarbe     ║
║  muss aktiver eingesetzt werden (z.B. farbige KPI-   ║
║  Borders, subtile Gradient-Headers).                  ║
║                                                       ║
║  Besonderheit: Shadows statt Borders für Tiefe.       ║
║  Cards mit leichtem Drop-Shadow (0 2px 8px #0001).    ║
╚════════════════════════════════════════════════════════╝
```

### Theme 4: **Stark Contrast** (High-Contrast / Accessibility)

```
╔════════════════════════════════════════════════════════╗
║  STARK CONTRAST                                       ║
║                                                       ║
║  Charakter:  WCAG AAA konformes Theme. Maximaler       ║
║              Kontrast, klare Grenzen, keine subtilen   ║
║              Farbunterschiede. Funktional-first.       ║
║                                                       ║
║  Farbwelt:                                            ║
║  ┌─────────┬──────────┬─────────────────────────┐    ║
║  │ Bg      │ #000000  │ True Black               │    ║
║  │ Surface │ #1A1A1A  │ Dark Gray                │    ║
║  │ SurfAlt │ #0D0D0D  │ Near Black               │    ║
║  │ Accent  │ #3B82F6  │ Strong Blue              │    ║
║  │ Text    │ #FFFFFF  │ Pure White               │    ║
║  │ Muted   │ #D1D5DB  │ Light Gray (WCAG AAA!)   │    ║
║  │ Success │ #22C55E  │ Vivid Green              │    ║
║  │ Warning │ #F59E0B  │ Vivid Amber              │    ║
║  │ Danger  │ #EF4444  │ Vivid Red                │    ║
║  │ Border  │ #6B7280  │ Visible Gray Border      │    ║
║  │ Focus   │ #FBBF24  │ Gold Focus Ring (3px!)   │    ║
║  └─────────┴──────────┴─────────────────────────┘    ║
║                                                       ║
║  Wann sinnvoll: Sehbehinderte Nutzer, sehr helle      ║
║  Umgebungen, maximale Lesbarkeit gewünscht.           ║
║                                                       ║
║  Risiken: Wirkt visuell hart → ist Absicht.           ║
║  Keine subtilen Hover-Effekte, alles muss klar        ║
║  unterscheidbar sein.                                 ║
║                                                       ║
║  Besonderheiten:                                      ║
║  • Focus-Ring: 3px Gold (statt 2px Cyan)              ║
║  • Alle Borders 2px statt 1px                         ║
║  • Buttons: Deutlicher Fill statt transparente BGs    ║
║  • Status: Immer Icon + Farbe + Text-Label            ║
║  • Keine alpha-transparenten Hintergründe             ║
╚════════════════════════════════════════════════════════╝
```

### Theme 5 (Bonus): **Arcade Neon** (Spielerisch)

```
╔════════════════════════════════════════════════════════╗
║  ARCADE NEON                                          ║
║                                                       ║
║  Charakter:  Retro-Arcade-Ästhetik. Leuchtende         ║
║              Farben, Pixel-Charme, Spielhallen-Vibe.   ║
║                                                       ║
║  Farbwelt:                                            ║
║  ┌─────────┬──────────┬─────────────────────────┐    ║
║  │ Bg      │ #0A0A1E  │ Arcade Dark              │    ║
║  │ Surface │ #14142E  │ Cabinet Blue             │    ║
║  │ Accent  │ #FF6EC7  │ Hot Pink                 │    ║
║  │ Second. │ #00FFCC  │ Electric Teal            │    ║
║  │ Text    │ #F0E6FF  │ Soft Lavender            │    ║
║  │ Muted   │ #8888BB  │ Muted Purple             │    ║
║  │ Success │ #00FF66  │ Neon Green               │    ║
║  │ Warning │ #FFD700  │ Gold                     │    ║
║  │ Danger  │ #FF2222  │ Alarm Red                │    ║
║  └─────────┴──────────┴─────────────────────────┘    ║
║                                                       ║
║  Wann sinnvoll: Fan-Favorit, Community-Theme, Events. ║
║  Risiken: Hot Pink als Akzent kann Lesbarkeit mindern.║
║  → Nur für interaktive Elemente, nicht für Text.      ║
╚════════════════════════════════════════════════════════╝
```

### Theme 6 (Bonus): **Clean Dark Pro** (besteht bereits, verfeinern)

Beibehaltung des bestehenden CleanDarkPro mit einem Fix:
- **TextMuted von #8A8EA0 auf #9BA0B8 anheben** → WCAG AA auf #16181D erreichen
- Sonst: Beibehalten als "sicherer Dunkel-Modus" für Nutzer, die Neon nicht mögen

---

## 9. Mockup-Konzepte

### Konzept A: Professional Technical

```
╔══════════════════════════════════════════════════════════════╗
║  PROFESSIONAL TECHNICAL                                     ║
║                                                             ║
║  Grundidee:                                                 ║
║  VS Code meets Grafana. Klare Panels, scharfe Hierarchie,  ║
║  monospace Daten-Akzente, muted Farben mit präzisen         ║
║  Akzentpunkten. Business-tauglich, aber nicht langweilig.   ║
║                                                             ║
║  ┌──────────────────────────────────────────────────────┐   ║
║  │ ⚡ Romulus          MissionControl    Ready ● 2 roots│   ║
║  ├──────┬───────────────────────────────────┬───────────┤   ║
║  │      │ SubTab: Roots · Config · Dash     │           │   ║
║  │  🏠  │                                   │  STATUS   │   ║
║  │  📊  │  ┌─────────────────────────────┐  │  Ready    │   ║
║  │  🔄  │  │  ROOT DROP ZONE             │  │           │   ║
║  │  🧰  │  │  Minimal, clean border      │  │  NEXT     │   ║
║  │  ⚙️  │  └─────────────────────────────┘  │  → Run    │   ║
║  │      │                                   │    Preview │   ║
║  │      │  ┌────┐ ┌────┐ ┌────┐ ┌────┐    │           │   ║
║  │      │  │1247│ │ 89 │ │412 │ │94% │    │  TIPS     │   ║
║  │      │  │Game│ │Win │ │Dup │ │Hlth│    │  Configure│   ║
║  │      │  └────┘ └────┘ └────┘ └────┘    │  regions  │   ║
║  ├──────┴───────────────────────────────────┴───────────┤   ║
║  │  [▶ Safe Preview]  [▶▶ Full Sort]           Ready ●  │   ║
║  └──────────────────────────────────────────────────────┘   ║
║                                                             ║
║  Visuelle Richtung:                                         ║
║  • Dunkel, neutral, wenig Farbe                             ║
║  • Farbe nur für: Status, Aktionen, Focus                   ║
║  • Monospace für Daten-Werte                                ║
║  • Klare Trennlinien statt Schatten                         ║
║  • Kompakte Information, aber nie gedrängt                   ║
║                                                             ║
║  Stärken:                                                   ║
║  ✓ Maximale Klarheit und Lesbarkeit                         ║
║  ✓ Skaliert gut bei vielen Features                         ║
║  ✓ Professional enough für Demo/Showcase                    ║
║  ✓ Ermüdungsarm bei langen Sessions                         ║
║                                                             ║
║  Schwächen:                                                 ║
║  ✗ Kann bei kleinem Datenbestand "leer" wirken              ║
║  ✗ Weniger emotionaler Charme als Retro-Varianten           ║
║                                                             ║
║  Beste Zielgruppe: Power-User, Archivare, CI-Nutzer         ║
╚══════════════════════════════════════════════════════════════╝
```

### Konzept B: Retro Neon

```
╔══════════════════════════════════════════════════════════════╗
║  RETRO NEON                                                 ║
║                                                             ║
║  Grundidee:                                                 ║
║  Synthwave-Ästhetik als Core Identity. Romulus IST Retro-   ║
║  ROM-Kultur, das darf man spüren. Neon-Akzente auf dunklem ║
║  Grund, Glow-Effekte auf aktiven Elementen, Phosphor-       ║
║  Typography für KPIs.                                       ║
║                                                             ║
║  ┌──────────────────────────────────────────────────────┐   ║
║  │ ◈ ROMULUS          ═══ MISSION CONTROL ═══    ● ● ● │   ║
║  ├──────┬───────────────────────────────────────┬───────┤   ║
║  │      │                                       │       │   ║
║  │ ╔══╗ │  ╔═══════════════════════════════════╗│ ┌───┐ │   ║
║  │ ║🏠║ │  ║  ░░ DROP ROOTS HERE ░░            ║│ │STA│ │   ║
║  │ ╠══╣ │  ║  ┌─────────────────────────────┐  ║│ │TUS│ │   ║
║  │ ║📊║ │  ║  │  Neon-bordered drop zone    │  ║│ │   │ │   ║
║  │ ╠══╣ │  ║  │  Glow on hover              │  ║│ │RDY│ │   ║
║  │ ║🔄║ │  ║  └─────────────────────────────┘  ║│ │   │ │   ║
║  │ ╠══╣ │  ╚═══════════════════════════════════╝│ └───┘ │   ║
║  │ ║🧰║ │                                       │       │   ║
║  │ ╠══╣ │  ╔══════╗ ╔══════╗ ╔══════╗ ╔══════╗ │       │   ║
║  │ ║⚙️║ │  ║ 1247 ║ ║  89  ║ ║ 412  ║ ║ 94%  ║ │       │   ║
║  │ ╚══╝ │  ║ GAME ║ ║ WIN  ║ ║ DUPE ║ ║ HLTH ║ │       │   ║
║  │      │  ╚══════╝ ╚══════╝ ╚══════╝ ╚══════╝ │       │   ║
║  ├──────┴───────────────────────────────────────┴───────┤   ║
║  │  [▶ SAFE PREVIEW ◈]  [▶▶ FULL SORT ◈]         ● RDY │   ║
║  └──────────────────────────────────────────────────────┘   ║
║                                                             ║
║  Visuelle Richtung:                                         ║
║  • Tiefes Navy/Purple als Basis                             ║
║  • Cyan + Purple Neon-Akzente                               ║
║  • Glow-Effects auf: Focus, Progress, Active NavRail        ║
║  • KPI-Zahlen in Monospace mit leichtem Glow                ║
║  • Double-Border-Stil (╔══╗) für Panels                     ║
║  • Scanline-Overlay (optional, sehr subtil)                 ║
║                                                             ║
║  Stärken:                                                   ║
║  ✓ Starke visuelle Identität                                ║
║  ✓ Perfekt für die ROM-Kultur                               ║
║  ✓ "Cool-Faktor" sehr hoch                                  ║
║  ✓ Unterscheidet sich von jedem anderen Tool                ║
║                                                             ║
║  Schwächen:                                                 ║
║  ✗ Glow-Effekte können bei langen Sessions ermüden          ║
║  ✗ Neon-Farben → Kontrast-Risiko (muss sorgfältig geprüft)║
║  ✗ Kann bei viel Dateninhalt unruhig wirken                 ║
║                                                             ║
║  Beste Zielgruppe: Retro-Sammler, Community, Showcase       ║
╚══════════════════════════════════════════════════════════════╝
```

### Konzept C: Minimal Power Dashboard

```
╔══════════════════════════════════════════════════════════════╗
║  MINIMAL POWER DASHBOARD                                    ║
║                                                             ║
║  Grundidee:                                                 ║
║  Maximale Informationsdichte bei minimaler visueller Last.  ║
║  Inspiriert von Trading-Dashboards und DevOps-Monitoring.   ║
║  Alles ist Daten, alles ist scanbar, nichts ist dekorativ.  ║
║                                                             ║
║  ┌──────────────────────────────────────────────────────┐   ║
║  │ R  MC > Roots         [Ctrl+K ⌕]    Theme ◐  Expert │   ║
║  ├────┬─────────────────────────────────────────────────┤   ║
║  │ MC │ Roots(2)  Config  Dash                          │   ║
║  │ LB │─────────────────────────────────────────────────│   ║
║  │ PL │ D:\ROMs\Nintendo   2,341 files  ✓ Online       │   ║
║  │ TB │ D:\ROMs\Sega         891 files  ✓ Online       │   ║
║  │ SY │                     [+ Add Root]                │   ║
║  │    │─────────────────────────────────────────────────│   ║
║  │    │ GAME  WIN   DUP  JUNK  DAT%  HLTH  DUR         │   ║
║  │    │ 1247   89   412    23  87%   94%   2:34         │   ║
║  │    │─────────────────────────────────────────────────│   ║
║  │    │ ████████████ SNES 342  ████ GBA 215  ██ NES 189│   ║
║  ├────┴─────────────────────────────────────────────────┤   ║
║  │ [▶ Preview]  [▶▶ Sort]  [⚠ Convert]         Ready ● │   ║
║  └──────────────────────────────────────────────────────┘   ║
║                                                             ║
║  Visuelle Richtung:                                         ║
║  • Minimal Chrome (NavRail auf 48px als Icons-only)         ║
║  • Tabellen-first statt Karten                              ║
║  • Inline-KPIs statt große MetricCards                      ║
║  • Mono-Font dominant                                       ║
║  • Border statt Shadows, 1px Lines                          ║
║  • Kompakteste Darstellung aller Konzepte                   ║
║                                                             ║
║  Stärken:                                                   ║
║  ✓ Maximale Daten pro Pixel                                 ║
║  ✓ Schnellstes Scannen für erfahrene Nutzer                 ║
║  ✓ Kein visuelles Rauschen                                  ║
║  ✓ Funktioniert auch auf kleineren Screens                  ║
║                                                             ║
║  Schwächen:                                                 ║
║  ✗ Für Einsteiger überfordernd                              ║
║  ✗ Kein "Charme", rein funktional                           ║
║  ✗ Wenig visuelle Hierarchie bei gleich-großen Daten        ║
║                                                             ║
║  Beste Zielgruppe: Power-User, CI/Batch, Tastatur-Nutzer    ║
╚══════════════════════════════════════════════════════════════╝
```

### Konzept D: Guided Safety Workflow

```
╔══════════════════════════════════════════════════════════════╗
║  GUIDED SAFETY WORKFLOW                                     ║
║                                                             ║
║  Grundidee:                                                 ║
║  Wizard-geführtes UI mit klarem Stepper. Jeder Schritt ist  ║
║  eine Seite. Keine gleichzeitigen Informationsberge. Der    ║
║  Nutzer wird durch den Prozess geführt, Gefahren sind       ║
║  maximal abgesichert.                                       ║
║                                                             ║
║  ┌──────────────────────────────────────────────────────┐   ║
║  │ ⚡ Romulus                      Step 2 of 5  [Exit]  │   ║
║  ├──────────────────────────────────────────────────────┤   ║
║  │                                                      │   ║
║  │  ①───②───③───④───⑤                                  │   ║
║  │  Add  Analyze Review Execute Result                  │   ║
║  │  ─────●─────○─────○─────○─────○                     │   ║
║  │        ↑ current                                     │   ║
║  │                                                      │   ║
║  │  ┌──────────────────────────────────────────────┐   │   ║
║  │  │                                              │   │   ║
║  │  │   🔍 ANALYSING YOUR COLLECTION...            │   │   ║
║  │  │                                              │   │   ║
║  │  │   ████████████████░░░░░░  67%                │   │   ║
║  │  │                                              │   │   ║
║  │  │   Found: 2,341 files across 14 consoles     │   │   ║
║  │  │   Processing: Deduplication phase...         │   │   ║
║  │  │                                              │   │   ║
║  │  └──────────────────────────────────────────────┘   │   ║
║  │                                                      │   ║
║  │      [← Back]                        [Next →]       │   ║
║  │                                                      │   ║
║  └──────────────────────────────────────────────────────┘   ║
║                                                             ║
║  Visuelle Richtung:                                         ║
║  • Zentrierte Content-Box (max 800px)                       ║
║  • Stepper-Navigation oben                                  ║
║  • Ein Fokus pro Schritt                                    ║
║  • Große, klare Buttons                                     ║
║  • Wizard-Overlay über die normale Shell                    ║
║  • Nach Wizard-Abschluss: Normal-Modus mit Shell           ║
║                                                             ║
║  Stärken:                                                   ║
║  ✓ Maximale Sicherheit für Einsteiger                       ║
║  ✓ Kein "was mach ich jetzt?" Moment                        ║
║  ✓ Danger Actions sind durch den Stepper vorbereitet        ║
║  ✓ Perfekt für First-Time-Use                               ║
║                                                             ║
║  Schwächen:                                                 ║
║  ✗ Power-User fühlen sich eingesperrt                       ║
║  ✗ Kein schneller Sprung zwischen Bereichen                 ║
║  ✗ Bei Wiederholung nervig                                  ║
║                                                             ║
║  Lösung: Wizard als OVERLAY über Normal-Shell.              ║
║  Power-User können jederzeit [Exit] drücken und in die      ║
║  freie Shell wechseln. Einsteiger bleiben im Wizard.        ║
║                                                             ║
║  Beste Zielgruppe: Einsteiger, Gelegenheitsnutzer           ║
╚══════════════════════════════════════════════════════════════╝
```

### Empfehlung: Hybridansatz

**Basis = Konzept A (Professional Technical)** als Shell-Struktur.

Angereichert mit:
- **Konzept B (Retro Neon)** über das Theme-System (Synthwave, CRT, Arcade als Themes)
- **Konzept D (Guided Workflow)** als optionaler Wizard-Overlay (bereits als `WizardView.xaml` angelegt!)
- **Konzept C (Minimal Power)** als UI-Mode "Compact" (NavRail collapsed, KPIs inline)

So bekommen alle Nutzergruppen ihren optimalen Modus, ohne dass die Grundstruktur leidet.

---

## 10. Konkrete UI-Verbesserungen

### 10.1 Tool-/Kachel-Logik

| Problem | Verbesserung |
|---------|-------------|
| Alle Kacheln gleichwertig | **Gruppierung** in Core/Maintenance/Advanced/Experimental |
| Kacheln als einzige Ansicht | **Grid + List + Search** als Ansichtsmodi |
| Maturity Badges nur als Text | **Visuelle Badges** mit Farbe + Icon |
| "Coming Soon" ohne Plan | **Planned** Badge nur mit Link zu Roadmap/Issue |
| Kein Quick-Access personalisierbar | **Pin-System** (bereits vorhanden, stärker promoten) |

### 10.2 Panel-Verteilung

| Problem | Verbesserung |
|---------|-------------|
| Context Wing oft leer | **Adaptive Context**: Zeigt kontextbezogene Infos je nach aktivem View |
| Detail Drawer ungenutzt | **Log + Diagnostics** dauerhaft dort, á la VS Code Terminal-Panel |
| SubTab-Leiste über voller Breite | **SubTabs nur so breit wie nötig**, Rest = Breadcrumb/Status |

**Context Wing Adaptive Content:**

| Aktiver View | Context Wing Inhalt |
|-------------|---------------------|
| Mission Control | Empfehlungen, System Health, Quick Actions |
| Library > Results | Score-Breakdown des ausgewählten Items |
| Library > Decisions | Winner-Vergleich, Alternativ-Kandidaten |
| Library > Safety | Detail zur ausgewählten Warnung |
| Pipeline > Conversion | Tool-Info, Format-Details, Risiken |
| Toolbox > Features | Tool-Doku, Parameters, Beispiele |

### 10.3 Informationsdichte reduzieren

| Maßnahme | Detail |
|----------|--------|
| **KPI-Priorisierung** | Max 4 Primär-KPIs prominent, Rest in Expandable-Section |
| **Progressive Disclosure** | Details erst bei Klick/Expand, nicht alles sofort sichtbar |
| **Tabellen-Pagination** | Große Listen (>50 Items) paginieren oder virtualisieren |
| **Whitespace** | Zwischen Sektionen 24px statt 16px, Cards mit 16px Padding |
| **Visual Grouping** | Zusammengehörige KPIs in Cards, nicht als lose Einzelwerte |

### 10.4 Kritische Aktionen hervorheben

| Aktion | Verbesserung |
|--------|-------------|
| **Execute Move** | Roter Danger-Button, 2-stufig, Typing-Bestätigung |
| **Rollback** | Amber Warning-Button, Summary-Dialog |
| **Convert** | Amber Warning-Button, Plan-Review erzwungen |
| **Batch Delete** | Roter Danger-Button + Countdown + Typing |
| **Preview** | Grüner Primary-Button (kein Danger – ist sicher!) |
| **Report öffnen** | Ghost-Button (keine Bestätigung nötig) |

### 10.5 Default / Advanced Trennung

| Element | Simple Mode | Expert Mode |
|---------|-------------|-------------|
| **NavRail** | 3 Bereiche (MC, Library, System) | 5 Bereiche (+ Pipeline, Toolbox) |
| **KPIs** | 4 Haupt-KPIs | Alle KPIs + Trends |
| **Decisions Table** | Nur Status-Spalte | Score, Region, Format, alle Scores |
| **ToolBox** | Nur Core + Maintenance | + Advanced + Experimental |
| **Settings** | Basis-Settings | Alle Optionen |
| **Action Bar** | 2 Buttons (Preview, Sort) | Alle Buttons + Custom Commands |
| **Context Wing** | Empfehlungen + Next Step | Full Inspector + Debug Info |

### 10.6 Konsolidierung

| Was | Vorschlag |
|-----|-----------|
| **4 Dialog-Typen** (Message, Input, Danger, Result) | **1 BasDialog** mit Severity-Level statt 4 separate |
| **Config-Tabs in eigener Nav-Ebene** | Config wird Sub-Tab von Mission Control |
| **Separate FeatureService + FeatureCommandService** | Langfristig: Zusammenführen oder klar nach Domain splitten |
| **Report als eigene Sub-Tab** | Report als Overlay/Drawer, nicht als permanenter Tab |

### 10.7 Auslagerungen

| Element | Wohin |
|---------|-------|
| **Profil-Editor** | Drawer oder Modal (nicht eigener Sub-Tab) |
| **Theme-Picker** | Dropdown in CommandBar (schneller Zugang) + Detail in System > Appearance |
| **External Tools Config** | Accordion innerhalb Toolbox (kein eigener Sub-Tab) |
| **About** | Modal/Dialog statt eigener Sub-Tab |
| **Activity Log** | Detail Drawer (unteres Panel), dauerhaft verfügbar |
| **Shortcut Sheet** | Overlay (F1), bereits umgesetzt ✓ |

---

## 11. Accessibility / Usability

### WCAG AA Pflicht-Anforderungen

| Anforderung | Status | Maßnahme |
|-------------|--------|----------|
| **Kontrast 4.5:1** (normaler Text) | ⚠️ CleanDarkPro TextMuted zu dunkel | TextMuted auf #9BA0B8+ anheben |
| **Kontrast 3:1** (großer Text/UI) | ⚠️ RetroCRT TextOnAccent prüfen | OnAccent-Pattern korrigieren |
| **Fokus-Indikatoren** | ✓ FocusRing vorhanden | Auf ComboBox/CheckBox erweitern |
| **Keyboard Navigation** | ✓ Grundstruktur vorhanden | TabIndex auditieren, Arrow-Key-Nav |
| **Screen Reader** | ⚠️ Lücken bei ARIA-Roles | AutomationProperties.Role ergänzen |
| **Status nicht nur Farbe** | ⚠️ Teilweise | Triple-Encoding durchsetzen (Icon+Farbe+Text) |
| **Touch Targets ≥ 44px** | ✓ MinTouchTarget definiert | CheckBox-Padding prüfen |
| **Reduced Motion** | ✓ Umgesetzt | Toggle in System > Appearance vorhanden, wirkt auf Shell-Motion-Verhalten |
| **Live Regions** | ⚠️ Teilweise | Status-Änderungen als LiveRegion markieren |

### Konkrete Fixes (priorisiert)

1. **CleanDarkPro TextMuted**: `#8A8EA0` → `#9BA0B8` (WCAG AA auf #16181D)
2. **RetroCRT TextOnAccent**: Validieren, dass #000000 auf #33FF33 ≥ 4.5:1 ist (aktuell: ~1.8:1 → **FAIL**). Fix: Für Buttons/Badges wo AccentCyan Hintergrund ist, `TextOnAccent = #0A200A` (very dark green) verwenden
3. **ComboBox FocusVisualStyle**: `FocusVisualStyle="{StaticResource FocusRing}"` hinzufügen
4. **CheckBox Click-Area**: Minimum 44x44px Touch-Target (Padding erhöhen)
5. **Reduced Motion Setting**: Boolean in Settings, wenn aktiv → alle Storyboard-Animationen disabled
6. **Status Triple-Encoding**: Alle Status-Anzeigen müssen Icon + Farbe + Text haben

### Screen Density Modi

| Modus | Zielgruppe | Änderungen |
|-------|-----------|------------|
| **Compact** | Power-User, große Sammlungen | Body 13px, Spacing ×0.75, Tables dichter |
| **Normal** (Default) | Standard | Body 14px, normales Spacing |
| **Comfortable** | Einsteiger, Accessibility | Body 15px, Spacing ×1.25, größere Touch-Targets |

---

## 12. Umsetzungsplan

### Phase 1: Quick Wins (1-2 Wochen)

| # | Maßnahme | Impact | Aufwand |
|---|----------|--------|---------|
| 1 | **WCAG Kontrast-Fixes** in CleanDarkPro + RetroCRT | Hoch | Niedrig |
| 2 | **FocusRing auf ComboBox/CheckBox** erweitern | Hoch | Niedrig |
| 3 | **Danger-Button-Styling** differenzieren (Rot für Move, Amber für Rollback) | Hoch | Niedrig |
| 4 | **Context Wing Adaptive Content** – verschiedene Inhalte pro View | Mittel | Mittel |
| 5 | **Status Triple-Encoding** durchsetzen (Icon+Farbe+Text überall) | Hoch | Mittel |
| 6 | **TextMuted-Korrektur** in allen Themes WCAG AA sicherstellen | Hoch | Niedrig |

### Phase 2: Strukturelle Verbesserungen (2-4 Wochen)

| # | Maßnahme | Impact | Aufwand |
|---|----------|--------|---------|
| 7 | **Tool-Katalog Grouping** (Core/Maintenance/Advanced) | Hoch | Mittel |
| 8 | **Config in Mission Control integrieren** (statt eigene Nav-Ebene) | Mittel | Mittel |
| 9 | **Activity Log in Detail Drawer** verlagern | Mittel | Mittel |
| 10 | **KPI-Priorisierung** – 4 primär, Rest expandable | Mittel | Niedrig |
| 11 | **Danger Confirm Dialog** mit Typing-Bestätigung | Hoch | Mittel |
| 12 | **Simple/Expert Mode** durchziehen (NavRail, Tables, ToolBox) | Hoch | Hoch |

### Umsetzungsstatus (Stand 2026-04-14)

| Phase | Status | Nachweis |
|-------|--------|----------|
| Phase 1 | Umgesetzt | Theme-/Kontrast-Fixes, Focus-Ring, Danger-Styling, Context-Wing-Anpassungen in der WPF-Oberfläche integriert |
| Phase 2 | Umgesetzt | Tool-Grouping, Mission-Control-Integration für Setup/Config, Activity-Log-Entkopplung in den Detail-Drawer, KPI-Disclosure und Simple/Expert-Verhalten umgesetzt |
| Phase 3 | Umgesetzt (Scope dieser Iteration) | Pipeline als eigener Nav-Bereich, neue Appearance-Steuerungen (Reduced Motion, Density, Wizard-Startup), Dialog-Basisstil mit Severity-Tagging sowie Theme-Update auf Clean Daylight / Stark Contrast umgesetzt |
| Phase 4 | Umgesetzt (Scope dieser Iteration) | Glow-Strategie vereinheitlicht (keine theme-spezifischen Progress-Glows), Animation-Timings auf Tokens standardisiert, Theme-Preview + Tour-Start in Appearance ergänzt, Console-Distribution-Chart visuell aufgewertet |

#### Verifizierung (Pflicht)

- Build: `dotnet build src/Romulus.sln` erfolgreich (alle Projekte grün)
- Tests (Phase-2-relevanter Scope): 546 bestanden, 0 fehlgeschlagen
      - `ShellViewModelCoverageTests`
      - `FeatureCommandServiceTests`
      - `WpfProductizationTests`
      - `GuiViewModelTests.AccessibilityAndRedTests`
- Tests (Phase-3-Akzeptanz): 5 bestanden, 0 fehlgeschlagen
      - `dotnet test src/Romulus.sln --filter "FullyQualifiedName~Phase3_"`
- Tests (Phase-4-Akzeptanz): 4 bestanden, 0 fehlgeschlagen
      - `dotnet test src/Romulus.sln --filter "FullyQualifiedName~Phase4_"`
- Tests (Phase-3/4-Regression/Impact): 585 bestanden, 0 fehlgeschlagen
      - `WpfProductizationTests`
      - `ShellViewModelCoverageTests`
      - `GuiViewModelTests`

### Phase 3: Designsystem-Verfeinerung (4-8 Wochen)

| # | Maßnahme | Impact | Aufwand |
|---|----------|--------|---------|
| 13 | **Neue Themes** (Clean Daylight, Stark Contrast verbessern) | Mittel | Mittel |
| 14 | **Reduced Motion Setting** | Mittel | Niedrig |
| 15 | **Screen Density Modi** (Compact/Normal/Comfortable) | Mittel | Hoch |
| 16 | **Wizard-Flow verbessern** (Stepper-UI, First-Time-Experience) | Hoch | Hoch |
| 17 | **Dialog-Konsolidierung** (BaseDialog mit Severity) | Niedrig | Mittel |
| 18 | **Pipeline-Section** als eigener Nav-Bereich | Mittel | Hoch |

### Phase 4: Polish & Delight (ongoing)

| # | Maßnahme | Impact | Aufwand |
|---|----------|--------|---------|
| 19 | **Glow-Effekte konsistent** über alle Themes (oder gar nicht) | Niedrig | Niedrig |
| 20 | **Animation-Timings** standardisieren (Token-basiert) | Niedrig | Mittel |
| 21 | **Theme-Preview** in Appearance-Settings | Niedrig | Mittel |
| 22 | **Console-Distribution Chart** visuelle Aufwertung | Niedrig | Mittel |
| 23 | **Onboarding-Tour** für Erstnutzer | Mittel | Hoch |

---

## Anhang: WPF-Umsetzbarkeit

### Was direkt in WPF machbar ist

| Feature | WPF-Technik |
|---------|-------------|
| Theme-Switching | DynamicResource + MergedDictionaries (bereits umgesetzt ✓) |
| Design Tokens | ResourceDictionary mit Thickness/Double/CornerRadius (bereits umgesetzt ✓) |
| Glow-Effekte | DropShadowEffect (sparsam verwenden → Performance) |
| Stepper-UI | Custom UserControl mit ItemsControl + DataTemplate |
| Adaptive Context Wing | ContentControl + DataTemplateSelector nach SelectedNavTag |
| Typing-Bestätigung | TextBox in Dialog mit Button.IsEnabled Binding |
| Compact/Normal/Comfortable | 3 ResourceDictionaries für Spacing/Sizing die gemerged werden |
| Reduced Motion | Boolean Resource → Trigger auf alle Storyboards (Duration=0) |
| Badge-System | Custom Control mit DependencyProperty für Maturity-Level |
| Virtualized Tables | VirtualizingStackPanel (bereits WPF-Standard) |
| Progressive Disclosure | Expander + Visibility-Trigger |

### Was in WPF vermieden werden sollte

| Feature | Warum nicht |
|---------|------------|
| CSS Blur/Backdrop-Filter | Kein natives Äquivalent, Performance-Problem |
| WebView-basierte UI-Teile | Nur für Reports sinnvoll, nicht für Core-UI |
| Komplexe SVG-Animationen | DropShadow + Scale reichen, keine Lottie-Animationen |
| Infinite Scroll | VirtualizingPanel reicht, kein Web-Pattern nötig |
| Multi-Window-Layouts | Single-Window mit Panels (bereits umgesetzt ✓) |
| Transparente Fenster | AllowTransparency kostet Performance, nicht nötig |

---

*Erstellt: 2026-04-14*
*Für: Romulus UI/UX Redesign*
*Status: Phase 1 bis Phase 4 umgesetzt*
