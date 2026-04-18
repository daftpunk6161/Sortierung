# Romulus – UI/UX Redesign Proposal v2

**Datum:** 2026-04-18
**Version:** 2.0
**Basis:** Tiefenanalyse aller 30+ WPF Views, 6 Themes, Shell-Navigation, ToolsView, DesignTokens, bestehender Proposal v1

---

## 1. Executive Summary

### Kurzfazit

Romulus hat ein starkes technisches Fundament: MVVM-Architektur, 6 Themes mit Token-System, NavigationRail, CommandPalette, Keyboard-Shortcuts. Aber die aktuelle GUI leidet an fünf strukturellen UX-Problemen, die das Tool **weniger sicher, schwerer scanbar und visuell chaotischer** machen als nötig.

### Die 5 Kernprobleme

| # | Problem | Schwere | Root Cause |
|---|---------|---------|------------|
| **P1** | **Kein geführter Workflow** – User müssen selbst wissen, was als nächstes kommt | Hoch | Flat Navigation ohne Workflow-Gravity |
| **P2** | **Visual Noise** – KPIs, Status-Chips, Badges, 5 Sektionen pro View gleichzeitig | Hoch | Keine Progressive Disclosure, alles sofort sichtbar |
| **P3** | **Danger Actions schwach differenziert** – Move, Convert, Rollback wirken alle ähnlich | Hoch | Einheitliche Button-Styles, kein Danger-Level-System |
| **P4** | **Unknown/Review/Blocked werden als Fehler wahrgenommen** | Mittel | Farb-only Encoding, fehlende Erklärungstexte |
| **P5** | **Tool-Katalog ist Kachelwand** – 20+ Features ohne Priorisierung | Mittel | Flache Gruppierung, kein kontextuelles Ranking |

### Zielbild

Romulus soll sich anfühlen wie ein **professionelles Mission Control für ROM-Sammlungen**:

> Die Klarheit von **VS Code** (Panel-Struktur, Command Palette, Themes) ×
> die Monitoring-Qualität von **Grafana** (Dashboard-KPIs, Status-System) ×
> der kulturelle Charme eines **Retro-Arcade-Terminals** (Phosphor, Monospace, Glow)

### Stärkste Designrichtung

**Professional Technical als Basis-Shell** mit modularen Retro-Akzenten über das Theme-System.
Die Struktur muss professionell und klar funktionieren – Retro kommt über Farbe, Typografie-Akzente und subtile Effekte, nicht über strukturelles Chaos.

---

## 2. Designziele

### Design-Pyramide (Prioritätsordnung)

```
                    ┌───────────────┐
                    │   DELIGHT     │  Retro-Charme, Themes,
                    │               │  Animation, Personality
                  ┌─┴───────────────┴─┐
                  │    EFFICIENCY      │  Power-User Shortcuts,
                  │                    │  Dichte, Batch-Ops
                ┌─┴────────────────────┴─┐
                │      CLARITY            │  Hierarchie, Flow,
                │                         │  States, Orientierung
              ┌─┴─────────────────────────┴─┐
              │         SAFETY               │  Danger-Absicherung,
              │                              │  Preview, Undo, Confirm
            ┌─┴──────────────────────────────┴─┐
            │          ACCESSIBILITY             │  WCAG AA, Keyboard,
            │                                    │  Kontrast, Fokus
            └────────────────────────────────────┘
```

### UX-Prinzipien

| # | Prinzip | Regel |
|---|---------|-------|
| 1 | **Safety First** | Destruktive Aktionen sind immer 2-stufig: Preview → Confirm → Execute |
| 2 | **Progressive Disclosure** | Einsteiger sehen Kern, Power-User decken Tiefe auf |
| 3 | **Single Source of Truth** | Jede KPI, jeder Status hat genau eine visuelle Quelle |
| 4 | **State Clarity** | Unknown, Review, Blocked sind keine Fehler, sondern dokumentierte Zustände |
| 5 | **Workflow Gravity** | Der natürliche Flow zieht den Nutzer in die richtige Richtung |
| 6 | **Keyboard First** | Alle Kernoperationen ohne Maus erreichbar |
| 7 | **Theme Consistency** | Alle Themes müssen funktional gleichwertig sein |
| 8 | **Triple Encoding** | Status nie nur über Farbe – immer Icon + Farbe + Text |
| 9 | **Density Modes** | Comfortable (Default) und Compact (Power-User) |
| 10 | **Context Awareness** | Rechtes Panel zeigt immer relevante Info zum aktiven View |

---

## 3. Informationsarchitektur

### 3.1 Ist-Analyse: Probleme der aktuellen Struktur

```
AKTUELL:
┌─────────┬────────────────────────────────────────┬──────────┐
│ NavRail │ Content (alles gleichzeitig sichtbar)  │ Context  │
│ (84px)  │  KPIs + Table + Status + Warnings +    │ Wing     │
│ 5 Items │  Actions + Filters + all at once       │ (320px)  │
│         │                                        │ oft leer │
├─────────┴────────────────────────────────────────┴──────────┤
│ SmartActionBar (Status-Redundanz, Move + Cancel + Run)      │
└─────────────────────────────────────────────────────────────┘

Probleme:
• 5 Status-Chips in CommandBar + Inspector + ContextPanel = dreifache Redundanz
• Kein visueller Fokuspunkt pro View
• ContextWing hat keine adaptive Content-Strategie
• SubTabs verschlucken Orientierung bei Navigation
```

### 3.2 Neue Informationsarchitektur

```
NEU: Drei-Zonen-Layout mit adaptivem Kontext

┌──────────────────────────────────────────────────────────────────────────┐
│  COMMAND BAR (48px) ─ App Title · Breadcrumb · Global Status · Ctrl+K  │
├──────┬───────────────────────────────────────────┬───────────────────────┤
│      │ SUB-TAB BAR (40px)                        │                       │
│  N   │ Tab · Tab · Tab · [Active]                │   CONTEXT WING (320px)│
│  A   ├───────────────────────────────────────────┤                       │
│  V   │                                           │   ┌─ Adaptive ──────┐ │
│      │   HERO ZONE (Primary Focus)               │   │ Status          │ │
│  R   │   Max 1 Focus-Element pro View            │   │ Next Steps      │ │
│  A   │                                           │   │ Detail on Select│ │
│  I   ├───────────────────────────────────────────┤   │ Recommendations │ │
│  L   │                                           │   │ Score Breakdown │ │
│      │   DETAIL ZONE (Secondary Content)         │   │                 │ │
│  72px│   Expandable, Scrollable                  │   │ Immer relevant  │ │
│      │                                           │   │ zum aktiven View│ │
│      │                                           │   └─────────────────┘ │
├──────┴───────────────────────────────────────────┴───────────────────────┤
│  SMART ACTION BAR (72px) ─ [Primary] [Secondary] [Danger]   Run Status  │
└──────────────────────────────────────────────────────────────────────────┘
```

### 3.3 Navigation Redesign (5 Bereiche, verfeinert)

| Position | Tag | Label | Beschreibung | Simple | Expert |
|----------|-----|-------|-------------|--------|--------|
| 1 | `MissionControl` | **Mission Control** | Start, Roots, Config, Dashboard | ✓ | ✓ |
| 2 | `Library` | **Library** | Ergebnisse, Entscheidungen, Safety, DAT Audit | ✓ | ✓ |
| 3 | `Pipeline` | **Pipeline** | Conversion, Sortierung, Batch | ✗ | ✓ |
| 4 | `Toolbox` | **Toolbox** | Feature-Katalog, Externe Tools, DAT Catalog | ✗ | ✓ |
| 5 | `System` | **System** | Appearance, Activity, About | ✓ | ✓ |

**Neuerungen gegenüber Ist-Zustand:**

| Änderung | Begründung |
|----------|-----------|
| NavRail von 84px → 72px | Mehr Content-Fläche, Icons bleiben scanbar |
| NavRail-Labels werden bei Compact-Mode ausgeblendet | → 48px Icon-only Rail |
| "Tools" → "Toolbox" | Klarerer Name, unterscheidet sich von "External Tools" Sub-Tab |
| Reports sind KEIN eigener Nav-Bereich | Reports sind Ergebnis-Ansichten innerhalb Library |
| Wizard bleibt als Overlay | Einsteiger-Flow ohne Shell-Umbau |

### 3.4 Sub-Tab-Struktur

**Mission Control:**
| Tab | Simple | Expert |
|-----|--------|--------|
| Dashboard | ✓ | ✓ |
| Roots | ✓ | ✓ |
| Regions | ✓ | ✓ |
| Options | ✓ | ✓ |
| Profiles | ✗ | ✓ |

**Library:**
| Tab | Simple | Expert |
|-----|--------|--------|
| Results | ✓ | ✓ |
| Decisions | ✗ | ✓ |
| Safety | ✓ | ✓ |
| DAT Audit | ✗ | ✓ |
| Report | ✓ | ✓ |

**Pipeline:**
| Tab | Simple | Expert |
|-----|--------|--------|
| Conversion | ✗ | ✓ |
| Sorting | ✗ | ✓ |
| Batch | ✗ | ✓ |

**Toolbox:**
| Tab | Simple | Expert |
|-----|--------|--------|
| Features | ✗ | ✓ |
| External Tools | ✗ | ✓ |
| DAT Management | ✗ | ✓ |

**System:**
| Tab | Simple | Expert |
|-----|--------|--------|
| Appearance | ✓ | ✓ |
| Activity | ✗ | ✓ |
| About | ✓ | ✓ |

### 3.5 Hub & Spoke Modell

```
                         ┌──────────────┐
                         │  MISSION     │
                         │  CONTROL     │
                         │  (Hub)       │
                         └──────┬───────┘
                                │
              ┌─────────────────┼─────────────────┐
              │                 │                  │
       ┌──────┴──────┐  ┌──────┴──────┐  ┌───────┴──────┐
       │   LIBRARY   │  │  PIPELINE   │  │   TOOLBOX    │
       │  (Analyse   │  │  (Aktion    │  │  (Utility    │
       │   + Lesen)  │  │   + Exec)   │  │   + Config)  │
       └─────────────┘  └─────────────┘  └──────────────┘

Mission Control = Startpunkt, Setup, Übersicht
Library         = Lesen, Verstehen, Entscheiden
Pipeline        = Ausführen, Konvertieren, Sortieren
Toolbox         = Werkzeuge, Erweiterungen, DAT-Verwaltung
System          = Meta: Theme, Log, Info
```

---

## 4. Kernscreens

### 4.1 Mission Control – Dashboard

**Ziel:** Schneller Überblick, Setup, Einstieg in den Workflow in < 5 Sekunden.

```
┌──────────────────────────────────────────────────────────────────┐
│  ⚡ Romulus   Mission Control / Dashboard        Ready ● 2 roots │
├──────┬────────────────────────────────────────────┬──────────────┤
│      │  Dashboard · Roots · Regions · Options     │              │
│  MC  │────────────────────────────────────────────│   STATUS     │
│  LB  │                                            │   ● Ready    │
│  PL  │  ┌─ QUICK START ─────────────────────────┐│   ● 2 Roots  │
│  TB  │  │                                       ││   ● DATs OK  │
│  SY  │  │   [▶ Safe Preview]  [▶▶ Full Sort]    ││   ● 14 Tools │
│      │  │                                       ││              │
│      │  │   Workflow: Safe Sort  Profile: Default││   ──────────│
│      │  └───────────────────────────────────────┘│   NÄCHSTER   │
│      │                                            │   SCHRITT    │
│      │  ┌─ LETZTE ERGEBNISSE ───────────────────┐│              │
│      │  │  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ││   → Preview  │
│      │  │  │ 1,247│ │   89 │ │  412 │ │  94% │ ││     starten  │
│      │  │  │ Games│ │Winner│ │ Dupes│ │Health│ ││              │
│      │  │  └──────┘ └──────┘ └──────┘ └──────┘ ││   → Regions  │
│      │  │                                       ││     prüfen   │
│      │  │  23 items need review · 5 blocked     ││              │
│      │  └───────────────────────────────────────┘│   → DATs     │
│      │                                            │     updaten  │
├──────┴────────────────────────────────────────────┴──────────────┤
│  [▶ Safe Preview]            [▶▶ Full Sort]           Ready ●   │
└──────────────────────────────────────────────────────────────────┘
```

| Aspekt | Detail |
|--------|--------|
| **Hero Zone** | Quick Start mit Workflow/Profil + Primär-Buttons |
| **Detail Zone** | Letzte Run-Ergebnisse (kompakte KPIs), Review-Hinweis |
| **Context Wing** | System Status + Empfohlener nächster Schritt |
| **Primäre Aktion** | Preview starten (F5) |
| **Sekundäre Aktion** | Workflow/Profil wechseln, letzte Ergebnisse ansehen |
| **Safety** | Kein Execute/Move von hier – nur Preview |
| **KPIs** | Max 4 Primär (Games, Winner, Dupes, Health), Rest collapsible |
| **Fehlerzustände** | Offline-Roots mit ⚠-Badge, fehlende DATs als Info-Banner |

### 4.2 Mission Control – Roots

**Ziel:** Root-Verzeichnisse verwalten, Drag&Drop, Zustand sehen.

```
┌──────────────────────────────────────────────────────────────────┐
│  ⚡ Romulus   Mission Control / Roots                             │
├──────┬────────────────────────────────────────────┬──────────────┤
│      │  Dashboard · [Roots] · Regions · Options   │              │
│  MC  │────────────────────────────────────────────│   ROOT-INFO  │
│  LB  │                                            │              │
│  PL  │  ┌─ ROOTS ──────────────────────────────┐ │   Ausgewählt:│
│  TB  │  │                                      │ │   D:\ROMs    │
│  SY  │  │  ┌────────────────────────────────┐  │ │              │
│      │  │  │  📁 Drop folders here           │  │ │   2,341 Files│
│      │  │  │     or [Browse] to add roots    │  │ │   14 Konsolen│
│      │  │  └────────────────────────────────┘  │ │   12.4 GB    │
│      │  │                                      │ │              │
│      │  │  ✓ D:\ROMs\Nintendo   2,341 files   │ │   Letzte     │
│      │  │  ✓ D:\ROMs\Sega        891 files    │ │   Analyse:   │
│      │  │  ⚠ E:\Archive         (offline)     │ │   vor 2h     │
│      │  │                                      │ │              │
│      │  │  [+ Add Root]  [Remove Selected]     │ │              │
│      │  └──────────────────────────────────────┘ │              │
├──────┴────────────────────────────────────────────┴──────────────┤
│  [▶ Safe Preview]                                     Ready ●   │
└──────────────────────────────────────────────────────────────────┘
```

| Aspekt | Detail |
|--------|--------|
| **Hero Zone** | Drop Zone + Root-Liste |
| **Context Wing** | Details zum ausgewählten Root |
| **Safety** | Offline-Roots blockieren Execute (Warning-Banner) |
| **Fehlerzustände** | Offline = ⚠ orange, Leer = Info blau, Nicht existent = ✗ rot |

### 4.3 Library – Results

**Ziel:** Run-Ergebnisse verstehen, schnell scannen.

```
┌──────────────────────────────────────────────────────────────────┐
│  ⚡ Romulus   Library / Results                  Run complete ●  │
├──────┬────────────────────────────────────────────┬──────────────┤
│      │  [Results] · Decisions · Safety · DAT Audit│              │
│  MC  │────────────────────────────────────────────│   SELECTED   │
│  LB  │                                            │   ITEM       │
│  PL  │  ┌─ RUN SUMMARY ────────────────────────┐ │              │
│  TB  │  │  ✅ Analysis complete                  │ │   Mario Kart │
│  SY  │  │  2,341 files · 14 consoles · 2:34    │ │   SNES       │
│      │  │  ⚠ 23 need review · 🔒 5 blocked     │ │              │
│      │  └──────────────────────────────────────┘ │   Score: 94  │
│      │                                            │   Region: US │
│      │  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐    │   DAT: ✅    │
│      │  │ 1,247│ │  412 │ │   23 │ │  94% │    │   Format: zip│
│      │  │ Games│ │ Dupes│ │ Junk │ │Health│    │              │
│      │  └──────┘ └──────┘ └──────┘ └──────┘    │   Winner von │
│      │                                            │   3 Kandidaten│
│      │  ┌─ CONSOLE BREAKDOWN ──────────────────┐ │              │
│      │  │  ████████████ SNES  342               │ │   Alternativen│
│      │  │  ████████     GBA   215               │ │   (E).sfc 87│
│      │  │  ██████       NES   189               │ │   (J).sfc 72│
│      │  │  ████         GB    134               │ │              │
│      │  └──────────────────────────────────────┘ │              │
├──────┴────────────────────────────────────────────┴──────────────┤
│  [📄 Report]  [↩️ Rollback]              [▶ Execute Move ⚠]    │
└──────────────────────────────────────────────────────────────────┘
```

| Aspekt | Detail |
|--------|--------|
| **Hero Zone** | Run Summary Banner + KPI-Karten (max 4) |
| **Detail Zone** | Console Breakdown Chart + Scrollbare Detailliste |
| **Context Wing** | Score-Breakdown des ausgewählten Items, Winner-Erklärung |
| **Primäre Aktion** | Report öffnen |
| **Danger Aktion** | Execute Move (rot, 2-stufig) |
| **Safety** | Move nur über Danger-Confirm-Dialog mit Typing-Bestätigung |
| **KPIs** | Games, Dupes, Junk, Health – Max 4 primär |
| **Fehlerzustände** | "No run data" Placeholder, Partial-Run Warning |

### 4.4 Library – Decisions

**Ziel:** Scoring-Entscheidungen transparent machen, Unknown/Review/Blocked erklären.

```
┌──────────────────────────────────────────────────────────────────┐
│  ⚡ Romulus   Library / Decisions                                 │
├──────┬────────────────────────────────────────────┬──────────────┤
│      │  Results · [Decisions] · Safety · DAT Audit│              │
│  MC  │────────────────────────────────────────────│   SCORE      │
│  LB  │                                            │   BREAKDOWN  │
│  PL  │  ┌─ FILTER ────────────────────────────┐  │              │
│  TB  │  │ [All ▾] [Console ▾] [Status ▾] 🔍  │  │   Region: 20│
│  SY  │  └────────────────────────────────────┘  │   Format: 15│
│      │                                            │   Version:10│
│      │  ┌─ STATUS SUMMARY ────────────────────┐  │   DAT:    30│
│      │  │ ✅ 1,189 Clear  ⚠ 23 Review         │  │   Language:5│
│      │  │ 🔒 5 Blocked   ❓ 30 Unknown         │  │   ─────────│
│      │  └────────────────────────────────────┘  │   Total:  80│
│      │                                            │              │
│      │  ┌─────────────────────────────────────┐  │   ──────────│
│      │  │ Game Key       │Winner  │Score│Status│  │   STATUS-   │
│      │  ├────────────────┼────────┼─────┼──────│  │   LEGENDE   │
│      │  │ Super Mario W. │(U).sfc │ 94  │ ✅   │  │              │
│      │  │ Zelda ALTTP    │(E).sfc │ 91  │ ⚠    │  │   ✅ Clear  │
│      │  │ Metroid        │ —      │ —   │ 🔒   │  │   Eindeutig │
│      │  │ unknown_rom    │ —      │ —   │ ❓   │  │              │
│      │  └─────────────────────────────────────┘  │   ⚠ Review  │
│      │                                            │   Prüfen     │
│      │                                            │              │
│      │                                            │   🔒 Blocked │
│      │                                            │   Manuell    │
│      │                                            │              │
│      │                                            │   ❓ Unknown │
│      │                                            │   Unbekannt  │
├──────┴────────────────────────────────────────────┴──────────────┤
│  [📄 Report]  [🔍 Only Review]                    Run complete ●│
└──────────────────────────────────────────────────────────────────┘
```

| Aspekt | Detail |
|--------|--------|
| **Status-Kommunikation** | Triple Encoding: Icon + Farbe + Text (nie nur Farbe) |
| **Status-Legende** | Permanent sichtbar im Context Wing |
| **Tooltip** | Hover auf Status zeigt Erklärung + empfohlene Aktion |
| **Context Wing** | Score-Breakdown bei Auswahl, permanente Status-Legende |
| **Safety** | Kein direkter Execute von hier – nur Inspection |

**Wichtig: Status-Semantik**

| Status | Icon | Farbe | Bedeutung | Ist KEIN Fehler |
|--------|------|-------|-----------|-----------------|
| **Clear** | ✅ | Success/Grün | Eindeutiger Winner, keine Konflikte | — |
| **Review** | 👁 | Warning/Amber | Automatisch entschieden, Prüfung empfohlen | Bewusster Zustand |
| **Blocked** | 🔒 | Danger/Rot | Keine automatische Entscheidung möglich | Bewusster Zustand |
| **Unknown** | ❓ | Info/Cyan | Kein Match in Regeln/DAT | Bewusster Zustand |

### 4.5 Library – Safety

**Ziel:** Alle Warnungen, Konflikte, Risiken auf einen Blick.

```
┌──────────────────────────────────────────────────────────────────┐
│  ⚡ Romulus   Library / Safety Review                             │
├──────┬────────────────────────────────────────────┬──────────────┤
│      │  Results · Decisions · [Safety] · DAT Audit│              │
│  MC  │────────────────────────────────────────────│   DETAIL     │
│  LB  │                                            │              │
│  PL  │  ┌─ SEVERITY OVERVIEW ──────────────────┐ │   Path       │
│  TB  │  │  🔴 2 Critical  🟡 12 Warning  ℹ 45  │ │   Collision  │
│  SY  │  └──────────────────────────────────────┘ │              │
│      │                                            │   2 Files    │
│      │  ┌─ FINDINGS ──────────────────────────┐  │   zielen auf │
│      │  │ 🔴 Path Collision                    │  │   denselben  │
│      │  │    2 files → same target path        │  │   Pfad:      │
│      │  │    Mario Kart (U) + Mario Kart (E)  │  │              │
│      │  ├──────────────────────────────────────│  │   SNES/      │
│      │  │ 🟡 Missing DAT: Wonderswan           │  │   Mario      │
│      │  │    12 files cannot be verified       │  │   Kart.zip   │
│      │  ├──────────────────────────────────────│  │              │
│      │  │ ℹ Region fallback: 45 files          │  │   Empfehlung:│
│      │  │    Primary region not available      │  │   Region-Tag │
│      │  └──────────────────────────────────────┘  │   im Datei-  │
│      │                                            │   namen       │
│      │                                            │   beibehalten │
├──────┴────────────────────────────────────────────┴──────────────┤
│  [▶ Fix Critical]                        ⚠ 2 Critical open     │
└──────────────────────────────────────────────────────────────────┘
```

| Aspekt | Detail |
|--------|--------|
| **Severity-Levels** | Critical (🔴), Warning (🟡), Info (ℹ) |
| **Safety** | Critical Items blockieren Execute (visuell verknüpft) |
| **Context Wing** | Detail + Empfehlung zur ausgewählten Warnung |
| **Wichtig** | Safety View ist kein "Fehler-Screen" sondern ein proaktiver Review |

### 4.6 Pipeline – Conversion

**Ziel:** Conversion-Plans anzeigen, reviewen, ausführen mit klarem Stepper.

```
┌──────────────────────────────────────────────────────────────────┐
│  ⚡ Romulus   Pipeline / Conversion                               │
├──────┬────────────────────────────────────────────┬──────────────┤
│      │  [Conversion] · Sorting · Batch            │              │
│  MC  │────────────────────────────────────────────│   FORMAT-    │
│  LB  │                                            │   INFO       │
│  PL  │  ┌─ STEP INDICATOR ────────────────────┐  │              │
│  TB  │  │  ① Preview ──▶ ② Review ──▶ ③ Exec │  │   CSO → CHD  │
│  SY  │  │  [current]     [next]      [locked] │  │              │
│      │  └────────────────────────────────────┘  │   Tool:      │
│      │                                            │   chdman     │
│      │  ┌─ PLAN SUMMARY ──────────────────────┐  │              │
│      │  │  📦 142 conversions planned           │  │   Lossless: │
│      │  │  💾 2.3 GB → 1.8 GB (−22%)          │  │   ✅ Ja     │
│      │  │  ⏱ Estimated: ~8 min                 │  │              │
│      │  └──────────────────────────────────────┘  │   Cost:     │
│      │                                            │   Niedrig    │
│      │  ┌──────────────────────────────────────┐  │              │
│      │  │Source        │Target  │Tool    │Status│  │   Risiken:  │
│      │  │game.cso      │game.chd│chdman  │ ⏳   │  │   Keine     │
│      │  │rom.nkit.iso  │rom.rvz │dolphin │ ⏳   │  │              │
│      │  └──────────────────────────────────────┘  │              │
├──────┴────────────────────────────────────────────┴──────────────┤
│  [← Back]                         [▶ Start Conversion ⚠]       │
└──────────────────────────────────────────────────────────────────┘
```

| Aspekt | Detail |
|--------|--------|
| **Stepper** | 3-stufig: Preview → Review → Execute (visuell als Fortschrittsleiste) |
| **Safety** | Execute erst nach Review freigeschaltet ("locked" Badge) |
| **Progress** | Live-Fortschrittsbalken pro File UND gesamt während Execute |
| **Context Wing** | Tool-Info, Format-Details, Lossless/Lossy, Risiken |

### 4.7 Reports / Audit / Rollback

```
┌──────────────────────────────────────────────────────────────────┐
│  ⚡ Romulus   Library / Report                                    │
├──────┬────────────────────────────────────────────┬──────────────┤
│      │  Results · Decisions · Safety · [Report]   │              │
│  MC  │────────────────────────────────────────────│   EXPORT     │
│  LB  │                                            │   OPTIONEN   │
│  PL  │  ┌─ REPORT VIEWER ─────────────────────┐  │              │
│  TB  │  │                                     │  │   [📋 Copy]  │
│  SY  │  │   Embedded HTML Report              │  │   [💾 CSV]   │
│      │  │   (WebView2 oder RichText)          │  │   [📂 HTML]  │
│      │  │                                     │  │              │
│      │  │   • Run Summary                     │  │   ──────────│
│      │  │   • Console Breakdown               │  │   ROLLBACK   │
│      │  │   • Decision Details                │  │              │
│      │  │   • Warnings & Conflicts            │  │   Letzter Run│
│      │  │                                     │  │   vor 5 min  │
│      │  └─────────────────────────────────────┘  │              │
│      │                                            │   [↩ Rollback│
│      │                                            │    ⚠]        │
├──────┴────────────────────────────────────────────┴──────────────┤
│  [📄 Open in Browser]                              Run complete ●│
└──────────────────────────────────────────────────────────────────┘
```

### 4.8 Toolbox – Feature Catalog

Siehe Abschnitt 6 für das vollständige Redesign.

### 4.9 System – Appearance

```
┌──────────────────────────────────────────────────────────────────┐
│  ⚡ Romulus   System / Appearance                                 │
├──────┬────────────────────────────────────────────┬──────────────┤
│      │  [Appearance] · Activity · About           │              │
│  MC  │────────────────────────────────────────────│   PREVIEW    │
│  LB  │                                            │              │
│  PL  │  ┌─ THEME ─────────────────────────────┐  │   ┌────────┐│
│  TB  │  │  ┌──────────┐ ┌──────────┐          │  │   │ Live   ││
│  SY  │  │  │Synthwave │ │CleanDark │          │  │   │ Mini   ││
│      │  │  │ Dusk ◉   │ │ Pro  ○   │          │  │   │ Preview││
│      │  │  └──────────┘ └──────────┘          │  │   │        ││
│      │  │  ┌──────────┐ ┌──────────┐          │  │   │ Buttons││
│      │  │  │ Phosphor │ │  Arcade  │          │  │   │ Cards  ││
│      │  │  │Terminal ○│ │ Neon  ○  │          │  │   │ Status ││
│      │  │  └──────────┘ └──────────┘          │  │   │ KPIs   ││
│      │  │  ┌──────────┐ ┌──────────┐          │  │   │        ││
│      │  │  │  Clean   │ │  Stark   │          │  │   └────────┘│
│      │  │  │Daylight ○│ │Contrast ○│          │  │              │
│      │  │  └──────────┘ └──────────┘          │  │              │
│      │  └─────────────────────────────────────┘  │              │
│      │                                            │              │
│      │  ┌─ MODE ──────────────────────────────┐  │              │
│      │  │  ○ Simple    ◉ Expert               │  │              │
│      │  └─────────────────────────────────────┘  │              │
│      │                                            │              │
│      │  ┌─ DENSITY ───────────────────────────┐  │              │
│      │  │  ○ Comfortable  ◉ Compact           │  │              │
│      │  └─────────────────────────────────────┘  │              │
│      │                                            │              │
│      │  ┌─ ACCESSIBILITY ─────────────────────┐  │              │
│      │  │  ☐ Reduce Motion                    │  │              │
│      │  │  ☐ High Contrast Focus Rings        │  │              │
│      │  └─────────────────────────────────────┘  │              │
├──────┴────────────────────────────────────────────┴──────────────┤
│                                                    Settings saved│
└──────────────────────────────────────────────────────────────────┘
```

### 4.10 Progress View (während Run)

```
┌──────────────────────────────────────────────────────────────────┐
│  ⚡ Romulus   Library / Results                   Running... 67% │
├──────┬────────────────────────────────────────────┬──────────────┤
│      │                                            │              │
│  MC  │  ┌─ RUN PROGRESS ──────────────────────┐  │   PHASE      │
│  LB  │  │                                     │  │   DETAIL     │
│  PL  │  │   ████████████████░░░░░░  67%       │  │              │
│  TB  │  │   1,571 / 2,341 files               │  │   Dedup-     │
│  SY  │  │   Phase: Deduplication              │  │   lication   │
│      │  │   Elapsed: 1:34 · Remaining: ~0:47  │  │              │
│      │  └─────────────────────────────────────┘  │   412 Dupes  │
│      │                                            │   gefunden   │
│      │  ┌─ PHASE PIPELINE ────────────────────┐  │              │
│      │  │  ✅ Scan       ✅ Classify           │  │   89 Winner  │
│      │  │  ✅ Enrich     ● Dedup (running)     │  │   selektiert │
│      │  │  ○ Score       ○ Report              │  │              │
│      │  └─────────────────────────────────────┘  │   23 Junk    │
│      │                                            │   markiert   │
│      │  ┌─ LIVE FEED ─────────────────────────┐  │              │
│      │  │  Processing: Mario Kart (U).zip     │  │              │
│      │  │  → SNES detected · Score: 94        │  │              │
│      │  │  → Winner of 3 candidates           │  │              │
│      │  └─────────────────────────────────────┘  │              │
├──────┴────────────────────────────────────────────┴──────────────┤
│  [⏹ Cancel Run]                                   Running... ●  │
└──────────────────────────────────────────────────────────────────┘
```

---

## 5. Workflow-Konzept

### 5.1 Standard-Workflow (Happy Path)

```
 ①              ②              ③              ④              ⑤
┌──────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐
│ ADD      │──▶│ PREVIEW  │──▶│ REVIEW   │──▶│ EXECUTE  │──▶│ RESULT   │
│ ROOTS    │   │ ANALYSE  │   │ DECIDE   │   │ MOVE     │   │ REPORT   │
│          │   │          │   │          │   │          │   │          │
│ Ordner   │   │ DryRun   │   │ Ergebnis │   │ Confirm  │   │ Report   │
│ wählen   │   │ starten  │   │ prüfen   │   │ + Apply  │   │ + Undo   │
└──────────┘   └──────────┘   └──────────┘   └──────────┘   └──────────┘
     │               │               │               │              │
     ▼               ▼               ▼               ▼              ▼
 Mission          Mission         Library         Library        Library
 Control          Control         > Results       > Results      > Report
 > Roots          > Dashboard     > Decisions     (Confirm       (Report +
                  (auto-nav)      > Safety         Dialog)        Rollback)
```

### 5.2 Flow-Regeln

| Schritt | Regel |
|---------|-------|
| ① Add Roots | Pflicht. Ohne Roots kein Preview möglich. Button disabled. |
| ② Preview | F5 oder Button. Auto-Navigation zu Library > Results nach Abschluss. |
| ③ Review | Empfohlen bei Review/Blocked/Unknown Items. Context Wing zeigt Hinweis. |
| ④ Execute | Nur über Danger-Confirm-Dialog. Zeigt Summary VOR Ausführung. |
| ⑤ Result | Auto-Navigation zu Report. Rollback-Button sichtbar. |

### 5.3 Review-Gate (Erzwingung vs Empfehlung)

```
┌─────────────────────────────────────────────────────────┐
│  REVIEW GATE                                            │
│                                                         │
│  Nach Preview:                                          │
│                                                         │
│  ≥1 Critical Safety  → Execute GESPERRT 🔒              │
│                         "Resolve critical issues first" │
│                                                         │
│  ≥1 Blocked Item     → Execute GESPERRT 🔒              │
│                         "Resolve blocked items first"   │
│                                                         │
│  ≥1 Review Item      → Execute MÖGLICH, aber WARNING    │
│                         im Confirm-Dialog:              │
│                         "23 items not reviewed"         │
│                                                         │
│  Alles Clear         → Execute NORMAL möglich           │
│                                                         │
│  Im Confirm-Dialog bei unreviewed Items:                │
│  ┌─────────────────────────────────────────────────┐   │
│  │  "23 items marked for review were not inspected"│   │
│  │  [Cancel]  [Review Now]  [Proceed Anyway ⚠]    │   │
│  └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

### 5.4 Danger-Level-System

| Level | Beispiele | Absicherung |
|-------|-----------|-------------|
| **None** | Report öffnen, CSV Export, Theme wechseln | Kein Dialog |
| **Low** | Rollback anzeigen, Configuration ändern | Inline-Confirmation |
| **Medium** | Rollback ausführen, DAT Auto-Update | Warning-Dialog mit Summary |
| **High** | Move Files, Convert Files | Danger-Dialog + Typing-Bestätigung |
| **Critical** | Batch-Delete, Cleanup All Junk | Danger-Dialog + Typing + 3s Countdown |

### 5.5 Danger-Confirm-Dialog (High Level)

```
┌──────────────────────────────────────────────────────┐
│              ⚠ MOVE CONFIRMATION                      │
│                                                      │
│  ┌──────────────────────────────────────────────┐   │
│  │  You are about to MOVE 412 files.            │   │
│  │                                              │   │
│  │  Summary:                                    │   │
│  │  ✅ 89 winners kept                          │   │
│  │  📦 412 duplicates → Trash                   │   │
│  │  🗑 23 junk files → Junk folder              │   │
│  │  🔒 5 blocked → SKIPPED (not moved)          │   │
│  │                                              │   │
│  │  ⚠ 23 items marked for review not inspected  │   │
│  │                                              │   │
│  │  💡 This action can be undone via Rollback.  │   │
│  └──────────────────────────────────────────────┘   │
│                                                      │
│  Type "MOVE" to confirm: [____________]              │
│                                                      │
│  [Cancel]                         [Execute Move]     │
│                                    ↑ Red, enabled     │
│                                    only after typing  │
└──────────────────────────────────────────────────────┘
```

### 5.6 Unknown / Review / Blocked Kommunikation

**Design-Grundsatz:** Diese Zustände sind **keine Fehler**. Sie sind bewusste, erwartbare Ergebnisse des Analyse-Prozesses.

**Kommunikationsstrategie:**

| Element | Umsetzung |
|---------|-----------|
| **In Tabellen** | Icon + Farb-Badge + Text-Label (Triple Encoding) |
| **In Summary** | "23 items need review" (neutral, nicht alarmierend) |
| **Tooltip** | Erklärt Zustand + empfiehlt nächste Aktion |
| **Context Wing** | Permanente Legende mit Erklärung aller Zustände |
| **Onboarding** | Wizard erklärt Zustände beim ersten Mal |

**Tonalität der Kommunikation:**

| Status | ✗ Schlecht | ✓ Gut |
|--------|-----------|-------|
| Unknown | "ERROR: Unknown ROM" | "Not recognized – no DAT or rule match" |
| Review | "WARNING: Review needed" | "Auto-decided – manual check recommended" |
| Blocked | "FAILED: Cannot process" | "Manual decision needed – multiple candidates" |

---

## 6. Tool-Katalog Redesign

### 6.1 Ist-Problem

Der aktuelle Tool-Katalog ist eine flache Kachelwand mit 8 Kategorien × N Tools. Bei 20+ Features:
- schwer zu scannen
- keine Priorisierung erkennbar
- seltene Features dominieren gleichwertig
- "Empfohlen" ist kontextfrei
- Badge-System (Production/Guided/Experimental) visuell zu gleich

### 6.2 Neues Konzept: Layered Command Center

```
┌──────────────────────────────────────────────────────────────────┐
│  TOOLBOX / Features                                              │
├──────────┬───────────────────────────────────────────────────────┤
│          │                                                       │
│ SIDEBAR  │  ┌─ 🔍 Search... ──────────── [Grid ▪] [List ≡] ┐  │
│          │  └────────────────────────────────────────────────┘  │
│ Empfohlen│                                                       │
│ Pinned   │  ┌─ CORE WORKFLOWS ──── Tägliche Standardoperationen │
│ Recent   │  │                                                   │
│ ─────── │  │  ┌──────────────┐ ┌──────────────┐ ┌──────────┐  │
│ Alle:    │  │  │ 🔍 Analyse  │ │ 🎯 Dedupe    │ │ ✅ Verify │  │
│  Core    │  │  │ Safe Sort   │ │ Full Sort    │ │ DAT Check│  │
│  Mainten.│  │  │ ⭐ Empfohlen│ │ 🟢 Stabil   │ │ 🟢 Stabil│  │
│  Advanced│  │  └──────────────┘ └──────────────┘ └──────────┘  │
│          │  │                                                   │
│ ─────── │  │  ┌──────────────┐ ┌──────────────┐               │
│ Filter:  │  │  │ 🔄 Convert  │ │ 📊 Health    │               │
│ [Stabil] │  │  │ Formate     │ │ Score Report │               │
│ [Guided] │  │  │ 🟡 Guided  │ │ 🟢 Stabil   │               │
│ [Experi.]│  │  └──────────────┘ └──────────────┘               │
│          │  └───────────────────────────────────────────────────│
│          │                                                       │
│          │  ┌─ MAINTENANCE ──── Wartung & Wiederherstellung ──┐ │
│          │  │  ┌──────────────┐ ┌──────────────┐              │ │
│          │  │  │ ↩ Rollback   │ │ 🧹 Cleanup   │              │ │
│          │  │  │ Undo Last    │ │ Junk Removal │              │ │
│          │  │  │ 🟢 Stabil   │ │ ⚠ Advanced  │              │ │
│          │  │  └──────────────┘ └──────────────┘              │ │
│          │  └─────────────────────────────────────────────────│ │
│          │                                                       │
│          │  ┌─ ADVANCED ─── Expert Only ─────────────────────┐ │
│          │  │  🔬 Hash Verify │ 📊 Collection Diff │ ...     │ │
│          │  │  🧪 Experimental│ 🔧 DAT Rename      │         │ │
│          │  └─────────────────────────────────────────────────┘ │
└──────────┴───────────────────────────────────────────────────────┘
```

### 6.3 Gruppierung

| Gruppe | Sichtbarkeit | Inhalt |
|--------|-------------|--------|
| **Core Workflows** | Immer, prominent | Analyse, Dedupe, Convert, Verify, Health Score |
| **Maintenance** | Immer, sekundär | Rollback, Cleanup, Repair, Integrity Monitor |
| **Reporting** | Immer, sekundär | Reports, Export, Audit Log |
| **Advanced** | Nur Expert-Mode | Hash Verify, Collection Diff, DAT Rename, Pipeline Builder |
| **Experimental** | Nur Expert-Mode, Badge | Features in Erprobung |
| **Planned** | Deaktiviert, Badge | Geplant mit Roadmap-Link |

### 6.4 Badge-System (vereinheitlicht)

| Badge | Visuell | Bedeutung |
|-------|---------|-----------|
| ⭐ **Recommended** | Gold-Stern, TopRight | Kontextuell empfohlen |
| 🟢 **Production** | Grüner Dot (kein extra Text) | Stabil, produktionsreif |
| 🟡 **Guided** | Amber Dot + "Guided" | Funktioniert mit Wizard-Flow |
| 🔬 **Experimental** | Lila Badge | Beta, Ergebnisse prüfen |
| 📋 **Planned** | Grauer Badge, Card disabled | Geplant, nicht implementiert |
| 🔒 **Requires Run** | Lock-Icon overlay | Braucht aktive Run-Daten |
| ⚠ **Advanced** | Amber Badge | Nur für erfahrene Nutzer |

### 6.5 Ansichtsmodi

| Modus | Beschreibung | Zielgruppe |
|-------|-------------|------------|
| **Grid** (Default) | Kacheln in Kategorien mit Badges | Visuell orientierte User |
| **List** | Kompakte Zeilen mit Icon + Name + Beschreibung | Power-User |
| **Search** | Fuzzy-Filter (Ctrl+K Integration) | Keyboard-User |

### 6.6 Context Wing für Tools

Wenn ein Tool selektiert ist, zeigt das Context Wing:
- Tool-Beschreibung (ausführlich)
- Parameter/Optionen
- Maturity-Status + Erklärung
- Letzte Nutzung
- Quick-Link zu Doku
- "Run Tool" Button

---

## 7. Designsystem

### 7.1 Farbsystem – Semantische Rollen

**Theme-unabhängige Rollendefinitionen:**

```
SURFACES
├── Background        Haupthintergrund der App
├── Surface           Karten, Panels, modale Container
├── SurfaceAlt        Sekundäre Panels, NavRail, Sub-Sektionen
├── SurfaceLight      Hover-States, erhöhte Flächen, Active-Rows
└── ScrimOverlay      Modal-Overlay (70-80% Opacity)

TEXT
├── TextPrimary       Haupttext (WCAG AA ≥ 4.5:1 auf Background)
├── TextMuted         Sekundärtext (WCAG AA ≥ 4.5:1 auf Surface)
├── TextOnAccent      Text auf AccentPrimary Backgrounds
└── TextDisabled      Disabled-Zustände (40% Opacity von TextPrimary)

ACCENTS
├── AccentPrimary     Hauptakzent: Focus, Links, Primary CTAs
└── AccentSecondary   Sekundärakzent: Badges, Tags, Highlights

SEMANTIC STATUS
├── Success           ✅ Bestätigung, Clear, OK, Stabil
├── SuccessBg         Hintergrund für Success-Bereiche
├── Warning           ⚠ Achtung, Review, empfohlene Prüfung
├── WarningBg         Hintergrund für Warning-Bereiche
├── Danger            🔴 Fehler, Blocked, destruktive Aktion
├── DangerBg          Hintergrund für Danger-Bereiche
├── Info              ℹ Information, Unknown, neutral
└── InfoBg            Hintergrund für Info-Bereiche

INTERACTION
├── BorderDefault     Standard-Border (1px)
├── BorderHover       Hover-Border
├── BorderFocus       Focus-Ring (= AccentPrimary, 2px)
├── ButtonPressed     Press-State (AccentPrimary @ 12% Opacity)
└── InputSelection    Text-Selektion (AccentPrimary @ 25% Opacity)
```

### 7.2 Typografie

```
FONT STACK
├── UI:       Segoe UI (Windows System Font)
├── Mono:     Cascadia Code → Consolas (Fallback)
└── Display:  Segoe UI Semibold

TYPE SCALE
├── Display:   22px / 28px line / SemiBold    → Page Titles
├── Heading:   18px / 24px line / SemiBold    → Section Headers
├── Subhead:   16px / 22px line / Medium      → Sub-Section Headers
├── Body:      14px / 20px line / Regular     → Standard Text
├── Caption:   12px / 16px line / Regular     → Labels, Metadata
├── Micro:     11px / 14px line / Regular     → Badges, Footnotes
└── Mono:      12px / 16px line / Regular     → Paths, Hashes, Codes

RETRO-AKZENT: Mono-Font für:
• Pfade und Dateinamen
• Hashes und Checksums
• GameKeys
• KPI-Zahlenwerte (große Zahlen)
• Tabellen-Datenwerte
• Status-Codes
```

### 7.3 Spacing-System

```
BASIS: 4px Grid (alle Werte Vielfache von 4)

SCALE
├── XS:    4px   Inline-Abstände, Icon-Margins
├── SM:    8px   Enge Abstände, grouped Elements
├── MD:   12px   Standard-Padding inner
├── LG:   16px   Card-Padding, Section-Margins
├── XL:   24px   Bereichsabstände
├── XXL:  32px   Hero-Abstände, große Sektionen
└── 3XL:  48px   Page-Level Spacing

ANWENDUNG
├── Card-Padding:     16px (LG)
├── Section-Gap:      24px (XL)
├── Panel-Padding:    16px (LG)
├── Button-Padding:   12px 16px (MD × LG)
├── Input-Padding:    8px 12px (SM × MD)
├── NavRail-Gap:      8px (SM)
└── ActionBar-Height: 72px
```

### 7.4 Komponentensystem

#### Buttons

```
HIERARCHY
├── Primary     AccentPrimary bg, TextOnAccent text, filled
│               → Hauptaktion pro Screen (max 1)
│
├── Secondary   AccentPrimary border, transparent bg
│               → Alternative Aktionen
│
├── Ghost       Transparent, AccentPrimary text
│               → Tertiäre Aktionen, Toolbar
│
├── Danger      DangerBg bg, Danger border+text
│               → Destruktive Aktionen (2-Step)
│
└── Warning     WarningBg bg, Warning border+text
                → Rollback, Rücknahme

STATES
├── Default   → Base styling
├── Hover     → Lighten bg (10%), border highlight
├── Active    → Darken bg (15%), scale(0.97)
├── Focus     → 2px AccentPrimary ring (3px in HighContrast)
├── Disabled  → 40% opacity, cursor: not-allowed
└── Loading   → Spinner + disabled

SIZING
├── Standard:  Height 36px, Padding 12×16
├── Small:     Height 28px, Padding 6×12
└── Large:     Height 44px, Padding 16×24 (Touch-Target)
```

#### Karten

```
VARIANTS
├── MetricCard     Große Mono-Zahl + Caption + optionaler Trend
│                  Surface bg, BorderDefault, RadiusLG
│
├── ToolCard       Icon + Titel + Beschreibung + Badge
│                  Surface bg, Hover→SurfaceLight, RadiusLG
│
├── StatusCard     Severity-Farbe links (4px bar) + Text + Aktion
│                  Surface bg, RadiusLG
│
└── InfoCard       Icon + Text (mehrzeilig), kein Button
                   SurfaceAlt bg, RadiusMD

CARD BASE
├── Padding:       16px
├── BorderRadius:  10px (RadiusLG)
├── Border:        1px BorderDefault
├── Background:    Surface
├── Hover:         SurfaceLight bg + AccentPrimary border
└── Focus:         2px AccentPrimary ring
```

#### Tabellen

```
TABLE SYSTEM
├── Header:     SurfaceAlt bg, TextMuted, Caption-Size, SemiBold
├── Row:        Surface bg, TextPrimary, Body-Size
├── Row Alt:    SurfaceAlt bg (subtle striping, optional)
├── Row Hover:  SurfaceLight bg
├── Row Select: AccentPrimary border-left (3px)
├── Cell:       Padding 8×12
├── Border:     1px BorderDefault zwischen Rows
│
├── Mono-Font:  Pfade, Hashes, Scores, Dateigrößen
└── Regular:    Namen, Beschreibungen, Status

VIRTUALISIERUNG: Ab 50 Rows VirtualizingStackPanel nutzen
```

#### Status-Indikatoren

```
TRIPLE ENCODING (immer Icon + Farbe + Text)

├── StatusDot      8px Kreis, semantische Farbe
├── StatusBadge    Pill-Form, bg + text, 12px font, Padding 4×8
├── StatusBanner   Full-width, icon + text + action-button
└── StatusChip     Kompakt, icon + short-text, für Tabellen

STATUS MAP
├── ✅ Success    Success-Farbe  + Checkmark   + "OK"/"Clear"
├── ⚠ Warning    Warning-Farbe  + AlertTriangle+ "Review"
├── 🔴 Error      Danger-Farbe   + XCircle      + "Error"/"Blocked"
├── ℹ Info       AccentPrimary  + InfoCircle   + "Info"/"Unknown"
├── 👁 Review     Warning-Farbe  + Eye          + "Review"
├── 🔒 Blocked    Danger-Farbe   + Lock         + "Blocked"
├── ❓ Unknown    Info-Farbe     + QuestionMark + "Unknown"
├── ⏳ Pending    TextMuted      + Clock        + "Pending"
└── 🔄 Running    AccentPrimary  + Spinner      + "Running"
```

### 7.5 Design Tokens (WPF ResourceDictionary)

**Konsolidierung:** Aktuell gibt es _DesignTokens.xaml UND _Tokens.xaml mit widersprüchlichen Werten.

**Empfehlung: Einheitliche Token-Datei**

```
_DesignTokens.xaml     → Spacing, Typography, Sizing, Animation (theme-unabhängig)
{ThemeName}.xaml        → Color Tokens (theme-spezifisch)
_ControlTemplates.xaml  → Shared Templates (nutzt DynamicResource für beide)
```

**Widersprüche auflösen:**

| Token | _DesignTokens | _Tokens | Empfehlung |
|-------|--------------|---------|-----------|
| RadiusMD | 8px | 6px | **8px** (moderner) |
| RadiusLG | 10px | 8px | **10px** |
| RadiusXL | 14px | 12px | **14px** |
| TypeHeading | 18px | 16px | **18px** (besser scanbar) |
| TypeBody | 14px | 13px | **14px** (besser lesbar) |
| TypeCaption | 12px | 11px | **12px** (WCAG-freundlicher) |

---

## 8. Theme-Vorschläge

### Theme 1: Synthwave Dusk (Standard / Default)

| Eigenschaft | Wert |
|------------|------|
| **Charakter** | Professionelles Dunkel-Theme mit subtilen Neon-Akzenten. Die Default-Identität von Romulus. |
| **Basis** | Bestehendes SynthwaveDark, leicht verfeinert |
| **Farbwelt** | Navy-Background (#0D0D1F), Cyan-Akzent (leicht gedämpft zu #00E5EE), Purple-Sekundär (#A855F7) |
| **Wann sinnvoll** | Default für alle Nutzer. Die "Romulus-Identität". |
| **Risiko** | Cyan nur für interaktive Elemente, nie als Fläche → Übernutzung vermeiden |
| **Besonderheit** | Subtiler Glow-Effekt auf ProgressBar und aktivem NavRail-Button |

```
Key Colors:
Background:    #0D0D1F   Deep Navy
Surface:       #1A1A3A   Dark Purple
AccentPrimary: #00E5EE   Gedämpftes Cyan (wärmer als #00F5FF)
AccentSecond:  #A855F7   Sanftes Purple
TextPrimary:   #E8E8F8   Lavender White
TextMuted:     #A0A0D0   WCAG AA auf Surface
Success:       #34D399   Mint Green
Warning:       #FBBF24   Warm Amber
Danger:        #F43F5E   Rose Red
```

### Theme 2: Phosphor Terminal (Retro CRT)

| Eigenschaft | Wert |
|------------|------|
| **Charakter** | Authentisches CRT-Terminal. Phosphor-Grün auf Schwarz. Monospace-dominant. |
| **Basis** | Bestehendes RetroCRT, verfeinert |
| **Farbwelt** | CRT-Black (#050A05), Phosphor-Green (#33FF33), Amber-Secondary (#FFAA00) |
| **Wann sinnvoll** | Retro-Enthusiasten, Sammler, Nostalgie-Faktor |
| **Risiko** | Monochrom ermüdet bei langen Sessions → Amber als Entlastung |
| **Besonderheit** | KPI-Zahlen in Phosphor-Mono, Tabellen-Headers in Amber |

```
Key Colors:
Background:    #050A05   CRT Black
Surface:       #0C140C   Dark Terminal
AccentPrimary: #33FF33   Phosphor Green
AccentSecond:  #FFAA00   Amber
TextPrimary:   #B8FFB8   Soft Green (WCAG-verbessert)
TextMuted:     #5A9A5A   Dim Green (WCAG AA)
Success:       #33FF33   Bright Green
Warning:       #FFAA00   Amber
Danger:        #FF4444   Alert Red
TextOnAccent:  #000000   Black on Green
```

### Theme 3: Clean Daylight (Helle Variante)

| Eigenschaft | Wert |
|------------|------|
| **Charakter** | Helles, sauberes Theme für Tageslicht. Professionell, fast Enterprise-artig. |
| **Basis** | Bestehendes CleanDaylight, verfeinert |
| **Farbwelt** | Near-White (#F8F9FA), Professional Blue (#2563EB), Deep Purple Sekundär |
| **Wann sinnvoll** | Helle Umgebungen, Tageslicht, Präsentationen |
| **Risiko** | Kann "langweilig" wirken → Akzentfarbe aktiver einsetzen |
| **Besonderheit** | Drop-Shadows statt Borders für Tiefe. Subtile Gradient-Headers. |

```
Key Colors:
Background:    #F8F9FA   Near White
Surface:       #FFFFFF   Pure White
AccentPrimary: #2563EB   Professional Blue
AccentSecond:  #7C3AED   Deep Purple
TextPrimary:   #1A1A2E   Near Black
TextMuted:     #6B7280   Medium Gray (WCAG AAA)
Success:       #059669   Deep Green
Warning:       #D97706   Deep Amber
Danger:        #DC2626   Deep Red
Border:        #E5E7EB   Light Gray
```

### Theme 4: Stark Contrast (High-Contrast / Accessibility)

| Eigenschaft | Wert |
|------------|------|
| **Charakter** | WCAG AAA konformes Theme. Maximaler Kontrast, klare Grenzen. |
| **Basis** | Bestehendes HighContrast, massiv verbessert |
| **Farbwelt** | True-Black (#000000), Pure White Text, Strong Blue Accent, Gold Focus |
| **Wann sinnvoll** | Sehbehinderungen, maximale Lesbarkeit, systemweiter High-Contrast |
| **Risiko** | Visuell hart → ist beabsichtigt |
| **Besonderheit** | 3px Focus-Ring (Gold), 2px Borders, keine Alpha-Transparenzen |

```
Key Colors:
Background:    #000000   True Black
Surface:       #1A1A1A   Dark Gray
AccentPrimary: #3B82F6   Strong Blue
TextPrimary:   #FFFFFF   Pure White
TextMuted:     #D1D5DB   Light Gray (WCAG AAA ≥ 7:1)
Success:       #22C55E   Vivid Green
Warning:       #F59E0B   Vivid Amber
Danger:        #EF4444   Vivid Red
FocusRing:     #FBBF24   Gold (3px!)
Border:        #6B7280   Visible Gray

Special Overrides:
• Focus Ring:   3px solid Gold
• All Borders:  2px (statt 1px)
• Buttons:      Solid Fill, kein transparentes bg
• No Alpha:     Keine transparenten Hintergründe
```

### Theme 5: Arcade Neon (Spielerisch)

| Eigenschaft | Wert |
|------------|------|
| **Charakter** | Retro-Arcade-Ästhetik. Leuchtende Farben, Spielhallen-Vibe. |
| **Basis** | Bestehendes ArcadeNeon |
| **Farbwelt** | Deep Navy (#0A0A1E), Hot Pink (#FF6EC7), Electric Teal (#00FFCC) |
| **Wann sinnvoll** | Community-Events, Showcase, Fun |
| **Risiko** | Hot Pink Kontrast-Risiko → nur für interaktive Elemente |
| **Besonderheit** | Dual-Accent (Pink + Teal), lebhafteste Farbpalette |

```
Key Colors:
Background:    #0A0A1E   Arcade Dark
Surface:       #14142E   Cabinet Blue
AccentPrimary: #FF6EC7   Hot Pink
AccentSecond:  #00FFCC   Electric Teal
TextPrimary:   #F0E6FF   Soft Lavender
TextMuted:     #9988CC   Muted Purple (WCAG AA verbessern!)
Success:       #00FF66   Neon Green
Warning:       #FFD700   Gold
Danger:        #FF2222   Alarm Red
```

### Theme 6: Clean Dark Pro (Professionell-Neutral)

| Eigenschaft | Wert |
|------------|------|
| **Charakter** | Neutrales Dunkel-Theme. Kein Neon, kein Retro. Rein professionell. |
| **Basis** | Bestehendes CleanDarkPro |
| **Farbwelt** | Neutral Dark (#16181D), Muted Blue (#6CA6E8), Muted Purple (#9B7BDB) |
| **Wann sinnvoll** | Nutzer die Neon/Retro nicht mögen. "VS Code-artig". |
| **Risiko** | Wenig Charakter → bewusst neutral für professionellen Einsatz |
| **Fix nötig** | TextMuted von #8A8EA0 auf #9BA0B8 anheben (WCAG AA) |

---

## 9. Mockup-Konzepte

### Konzept A: Professional Technical

```
┌──────────────────────────────────────────────────────────────────┐
│                     PROFESSIONAL TECHNICAL                       │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Grundidee:                                                      │
│  VS Code meets Grafana. Klare Panels, scharfe Hierarchie,       │
│  monospace Daten-Akzente, muted Farben mit präzisen              │
│  Akzentpunkten. Business-tauglich, aber nicht langweilig.        │
│                                                                  │
│  Visuell:                                                        │
│  ┌─────┬─────────────────────────────────────────┬──────────┐   │
│  │     │ Command Bar (slim, 48px)                │          │   │
│  │ Nav │─────────────────────────────────────────│ Context  │   │
│  │ 72px│ SubTabs (compact, 36px)                 │ Wing     │   │
│  │     │─────────────────────────────────────────│ 280px    │   │
│  │ ■MC │                                         │          │   │
│  │ □LB │  Hero Zone: Fokussiert, 1 Aufgabe       │ Adaptive │   │
│  │ □PL │                                         │ Content  │   │
│  │ □TB │─────────────────────────────────────────│          │   │
│  │ □SY │  Detail Zone: Scrollbar, expandierbar   │          │   │
│  │     │                                         │          │   │
│  ├─────┴─────────────────────────────────────────┴──────────┤   │
│  │ Action Bar: [Primary] [Secondary]    [Danger]    Status  │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  Stilmittel:                                                     │
│  • Dunkel, neutral, wenig Farbe                                  │
│  • Farbe NUR für: Status, Aktionen, Focus                        │
│  • Monospace für Daten-Werte (KPIs, Pfade, Hashes)              │
│  • Klare 1px Trennlinien statt Schatten                          │
│  • Kompakt aber nicht gedrängt                                   │
│  • Keine Glow-Effekte, keine Animationen                         │
│                                                                  │
│  Stärken:                                                        │
│  ✓ Maximale Klarheit und Lesbarkeit                              │
│  ✓ Skaliert gut bei vielen Features                              │
│  ✓ Professional für Demo/Showcase                                │
│  ✓ Ermüdungsarm bei langen Sessions                              │
│  ✓ Beste Basis für alle Themes                                   │
│                                                                  │
│  Schwächen:                                                      │
│  ✗ Kann bei wenig Daten "leer" wirken                            │
│  ✗ Weniger emotionaler Charme als Retro-Varianten               │
│                                                                  │
│  Zielgruppe: Power-User, Archivare, CI-Nutzer, Professionals    │
└──────────────────────────────────────────────────────────────────┘
```

### Konzept B: Retro Neon

```
┌──────────────────────────────────────────────────────────────────┐
│                        RETRO NEON                                │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Grundidee:                                                      │
│  Synthwave-Ästhetik als Core Identity. Romulus IST Retro-        │
│  ROM-Kultur, das darf man sehen und spüren. Neon auf dunklem     │
│  Grund, Glow auf aktiven Elementen, Phosphor-KPIs.              │
│                                                                  │
│  Visuell:                                                        │
│  ╔══════╦═══════════════════════════════════════╦════════════╗   │
│  ║      ║ ═══ COMMAND BAR ═══     ◉ ◉ ◉        ║            ║   │
│  ║ ╔══╗ ╠═══════════════════════════════════════╣ ┌────────┐ ║   │
│  ║ ║MC║ ║ SubTabs                               ║ │Context │ ║   │
│  ║ ╠══╣ ╠═══════════════════════════════════════╣ │Wing    │ ║   │
│  ║ ║LB║ ║                                       ║ │        │ ║   │
│  ║ ╠══╣ ║  ╔══════╗ ╔══════╗ ╔══════╗ ╔══════╗ ║ │ Status │ ║   │
│  ║ ║PL║ ║  ║ 1247 ║ ║  89  ║ ║ 412  ║ ║ 94%  ║ ║ │ Glow  │ ║   │
│  ║ ╠══╣ ║  ║ GAME ║ ║ WIN  ║ ║ DUPE ║ ║ HLTH ║ ║ │ Active │ ║   │
│  ║ ║TB║ ║  ╚══════╝ ╚══════╝ ╚══════╝ ╚══════╝ ║ │        │ ║   │
│  ║ ╠══╣ ║                                       ║ └────────┘ ║   │
│  ║ ║SY║ ╠═══════════════════════════════════════╣            ║   │
│  ║ ╚══╝ ║  Detail Zone (mit subtilen Scanlines) ║            ║   │
│  ╠══════╩═══════════════════════════════════════╩════════════╣   │
│  ║ [▶ PREVIEW ◈]  [▶▶ SORT ◈]          ● READY              ║   │
│  ╚══════════════════════════════════════════════════════════════╝   │
│                                                                  │
│  Stilmittel:                                                     │
│  • Deep Navy/Purple als Basis                                    │
│  • Cyan + Purple Neon-Akzente                                    │
│  • Glow auf: Focus, Progress, Active NavRail                     │
│  • KPI-Zahlen in Monospace mit Glow-Effekt                       │
│  • Double-Border (╔══╗) für prominente Panels                    │
│  • Subtile Scanline-Textur (optional, ReduceMotion-aware)       │
│                                                                  │
│  Stärken:                                                        │
│  ✓ Stärkste visuelle Identität aller Konzepte                   │
│  ✓ Perfekt für ROM-Kultur und Community                          │
│  ✓ Hoher "Cool-Faktor"                                          │
│  ✓ Unterscheidet sich von jedem anderen Utility-Tool             │
│                                                                  │
│  Schwächen:                                                      │
│  ✗ Glow-Effekte ermüden bei langen Sessions                     │
│  ✗ Neon-Farben brauchen sorgfältige Kontrast-Prüfung            │
│  ✗ Bei viel Daten kann es unruhig wirken                         │
│                                                                  │
│  Zielgruppe: Retro-Sammler, Community, Showcase, Marketing      │
└──────────────────────────────────────────────────────────────────┘
```

### Konzept C: Minimal Power Dashboard

```
┌──────────────────────────────────────────────────────────────────┐
│                   MINIMAL POWER DASHBOARD                        │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Grundidee:                                                      │
│  Maximale Informationsdichte bei minimaler visueller Last.       │
│  Trading-Dashboard meets DevOps-Monitoring. Alles ist Daten,     │
│  alles ist scanbar, nichts ist dekorativ.                        │
│                                                                  │
│  Visuell:                                                        │
│  ┌──┬────────────────────────────────────────────────────────┐  │
│  │R │ MC > Roots      [Ctrl+K ⌕]    Theme ◐  Expert        │  │
│  │  │────────────────────────────────────────────────────────│  │
│  │MC│ Roots(2) Config  Dash                                  │  │
│  │LB│────────────────────────────────────────────────────────│  │
│  │PL│ D:\ROMs\Nintendo   2,341  ✓   │ GAME WIN  DUP  HLTH  │  │
│  │TB│ D:\ROMs\Sega         891  ✓   │ 1247  89  412   94%  │  │
│  │SY│                   [+ Root]     │                       │  │
│  │  │────────────────────────────────────────────────────────│  │
│  │  │ ████████ SNES 342  ████ GBA 215  ██ NES 189           │  │
│  ├──┴────────────────────────────────────────────────────────┤  │
│  │ [▶ Preview] [▶▶ Sort] [⚠ Convert]              Ready ●  │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
│  Stilmittel:                                                     │
│  • NavRail auf 48px (Icon-only)                                  │
│  • Kein Context Wing (oder optional collapsible)                 │
│  • Tabellen-first statt Karten                                   │
│  • Inline-KPIs statt MetricCards                                 │
│  • Mono-Font dominant                                            │
│  • 1px Borders, keine Shadows                                    │
│  • Maximale Pixel-Effizienz                                      │
│                                                                  │
│  Stärken:                                                        │
│  ✓ Maximale Daten pro Pixel                                      │
│  ✓ Schnellstes Scannen für Experten                              │
│  ✓ Funktioniert auf kleineren Screens                            │
│  ✓ Kein visuelles Rauschen                                       │
│                                                                  │
│  Schwächen:                                                      │
│  ✗ Für Einsteiger überfordernd                                   │
│  ✗ Kein Charme, rein funktional                                  │
│  ✗ Wenig visuelle Hierarchie bei gleich-großen Daten             │
│                                                                  │
│  Zielgruppe: Power-User, CI/Batch, Keyboard-Only-Nutzer         │
└──────────────────────────────────────────────────────────────────┘
```

### Konzept D: Guided Safety Workflow

```
┌──────────────────────────────────────────────────────────────────┐
│                   GUIDED SAFETY WORKFLOW                          │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Grundidee:                                                      │
│  Wizard-geführtes UI mit klarem Stepper. Jeder Schritt ist       │
│  eine Seite. Der Nutzer wird durch den Prozess geführt.          │
│  Maximum Safety, minimum Risiko für Fehlbedienung.               │
│                                                                  │
│  Visuell:                                                        │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ ⚡ Romulus                    Step 2 of 5      [Exit ✕]  │   │
│  │──────────────────────────────────────────────────────────│   │
│  │                                                          │   │
│  │  ①───②───③───④───⑤                                     │   │
│  │  Add  Analyse  Review  Execute  Result                   │   │
│  │  ───●────○──────○───────○───────○                       │   │
│  │       ↑ current                                          │   │
│  │                                                          │   │
│  │  ┌──────────────────────────────────────────────────┐   │   │
│  │  │                                                  │   │   │
│  │  │   🔍 ANALYSING YOUR COLLECTION...                │   │   │
│  │  │                                                  │   │   │
│  │  │   ████████████████░░░░░░  67%                    │   │   │
│  │  │   Found: 2,341 files across 14 consoles         │   │   │
│  │  │   Processing: Deduplication phase...             │   │   │
│  │  │                                                  │   │   │
│  │  └──────────────────────────────────────────────────┘   │   │
│  │                                                          │   │
│  │       [← Back]                         [Next →]         │   │
│  │                                                          │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  Stilmittel:                                                     │
│  • Zentrierte Content-Box (max 800px)                            │
│  • Stepper-Navigation oben als primäre Orientierung              │
│  • Ein Fokuspunkt pro Schritt                                    │
│  • Große, klare Buttons (min 44px)                               │
│  • Wizard als Overlay über Shell                                 │
│  • [Exit] bricht Wizard ab → normal Shell                        │
│                                                                  │
│  Stärken:                                                        │
│  ✓ Maximale Sicherheit für Einsteiger                            │
│  ✓ Kein "was mach ich jetzt?" Moment                             │
│  ✓ Danger Actions sind durch Stepper kontrolliert                │
│  ✓ Perfekt für First-Time-Use und Onboarding                    │
│                                                                  │
│  Schwächen:                                                      │
│  ✗ Power-User fühlen sich eingesperrt                            │
│  ✗ Kein schneller Sprung zwischen Bereichen                      │
│  ✗ Bei Wiederholung repetitiv                                    │
│                                                                  │
│  Lösung: Wizard als OVERLAY. Power-User drücken [Exit] und       │
│  landen sofort in der freien Shell. Einsteiger bleiben im         │
│  geführten Flow.                                                 │
│                                                                  │
│  Zielgruppe: Einsteiger, Gelegenheitsnutzer, First-Time-Use     │
└──────────────────────────────────────────────────────────────────┘
```

### Empfehlung: Hybridansatz

**Basis = Konzept A (Professional Technical)** als Shell-Struktur für alle Nutzer.

Angereichert mit:
- **Konzept B** über das Theme-System (Synthwave, CRT, Arcade als Themes)
- **Konzept C** über den Density-Mode "Compact" (NavRail Icon-only, KPIs inline)
- **Konzept D** als WizardView-Overlay (bereits vorhanden, ausbauen)

Jeder Nutzer bekommt seinen optimalen Modus durch Kombination:

| Nutzertyp | Mode | Theme | Density | Wizard |
|-----------|------|-------|---------|--------|
| Einsteiger | Simple | Synthwave | Comfortable | Wizard ON |
| Power-User | Expert | CleanDark | Compact | Wizard OFF |
| Retro-Fan | Expert | Phosphor CRT | Comfortable | Wizard OFF |
| Archivar | Expert | CleanDaylight | Comfortable | Wizard OFF |
| Accessibility | Simple | Stark Contrast | Comfortable | Wizard ON |

---

## 10. Konkrete UI-Verbesserungen

### 10.1 Tool-/Kachel-Logik

| # | Problem | Verbesserung | Priorität |
|---|---------|-------------|-----------|
| T1 | Alle Kacheln gleichwertig | Gruppierung in Core/Maintenance/Advanced | Hoch |
| T2 | Kacheln als einzige Ansicht | Grid + List + Search Modi | Mittel |
| T3 | Maturity Badges nur Text | Visuelle Dot-Badges (Grün/Amber/Lila) | Mittel |
| T4 | "Coming Soon" ohne Plan | "Planned" Badge nur mit Roadmap-Link | Niedrig |
| T5 | Pin-System versteckt | Pin-Button prominenter, Quick-Access-Sektion oben | Mittel |
| T6 | Empfehlungen kontextfrei | Kontextbasiert: vor Run andere als nach Run | Hoch |

### 10.2 Panel-Verteilung

| # | Problem | Verbesserung | Priorität |
|---|---------|-------------|-----------|
| P1 | Context Wing oft leer | Adaptive Content je nach aktivem View | Hoch |
| P2 | Status-Redundanz (3×) | Single Status Source in CommandBar, Rest entfernen | Hoch |
| P3 | Detail Drawer ungenutzt | Optional als Diagnostics/Log-Panel (wie VS Code Terminal) | Mittel |
| P4 | SubTabs über voller Breite | SubTabs links, Breadcrumb/Status rechts | Niedrig |
| P5 | ActionBar zu hoch (88px) | Auf 72px reduzieren, Buttons kompakter | Niedrig |

**Context Wing Adaptive Content:**

| Aktiver View | Context Wing zeigt |
|-------------|-------------------|
| MC > Dashboard | System Health, Empfohlener nächster Schritt |
| MC > Roots | Detail zum ausgewählten Root |
| Library > Results | Score-Breakdown des ausgewählten Items |
| Library > Decisions | Winner-Vergleich, Status-Legende |
| Library > Safety | Detail + Empfehlung zur ausgewählten Warnung |
| Pipeline > Conversion | Tool-Info, Format-Details, Risiken |
| Toolbox > Features | Tool-Doku, Parameter, Quick-Run |

### 10.3 Informationsdichte reduzieren

| # | Maßnahme | Detail | Priorität |
|---|----------|--------|-----------|
| D1 | KPI-Priorisierung | Max 4 Primär-KPIs prominent, Rest in Expander | Hoch |
| D2 | Progressive Disclosure | Details bei Klick/Expand, nicht sofort | Hoch |
| D3 | Visual Grouping | Zusammengehörige KPIs in Cards | Mittel |
| D4 | Whitespace erhöhen | Section-Gap 24px statt 16px | Mittel |
| D5 | Phase-Cards entlasten | Max 6 Phase-Chips statt 7 Card-Kacheln | Niedrig |

### 10.4 Kritische Aktionen hervorheben

| # | Maßnahme | Detail | Priorität |
|---|----------|--------|-----------|
| A1 | Danger-Level-System | 4 Stufen: None/Low/Medium/High/Critical | Hoch |
| A2 | Move-Confirm redesignen | Modale Confirm-Dialog statt Inline-Pattern | Hoch |
| A3 | Typing-Bestätigung | "MOVE" tippen für High-Level Actions | Hoch |
| A4 | Countdown für Critical | 3-Sekunden-Countdown bei Batch-Delete | Mittel |
| A5 | Visual Weight | Danger-Buttons: Rot-Filled + Icon, klar unterscheidbar | Hoch |

### 10.5 Default / Advanced Trennung

| # | Maßnahme | Detail | Priorität |
|---|----------|--------|-----------|
| M1 | Simple Mode | Nur MC, Library, System sichtbar | Vorhanden ✓ |
| M2 | Expert Mode | Alle 5 Nav-Bereiche + Advanced Tools | Vorhanden ✓ |
| M3 | Density Comfortable | Standard-Spacing, große Touch-Targets | Vorhanden ✓ |
| M4 | Density Compact | Reduziertes Spacing, Icon-only Nav | Verfeinern |
| M5 | Feature-Badges | Advanced-Features klar markiert | Umsetzen |

### 10.6 UI-Elemente konsolidieren

| # | Element | Problem | Lösung | Priorität |
|---|---------|---------|--------|-----------|
| C1 | StatusChips | In CommandBar + ContextPanel + Inspector | Nur CommandBar + Context Wing | Hoch |
| C2 | Move-Buttons | ShowActionBarMoveButton + ShowResultMoveButton | Ein Button, ein Ort (ActionBar) | Hoch |
| C3 | Config Warning | Banner + separater State | Einheitlicher Config-Changed-Banner | Mittel |
| C4 | KPI-Karten | In StartView + ResultView + ProgressView dupliziert | Shared MetricCard Template | Mittel |
| C5 | Token-Dateien | _DesignTokens.xaml vs _Tokens.xaml | Vereinigen, Widersprüche auflösen | Hoch |

### 10.7 Überflüssiges identifizieren

| # | Element | Bewertung | Empfehlung |
|---|---------|-----------|-----------|
| O1 | Conversion Sub-Tab in Toolbox | Doppelt mit Pipeline > Conversion | Entfernen |
| O2 | GameKey Lab Sub-Tab | Unused, nie implementiert | Entfernen oder als Experimental markieren |
| O3 | QuickStart Sub-Tab | Nie sichtbar (weder Simple noch Expert) | In Dashboard integrieren oder entfernen |
| O4 | Activity Log in System | Nie sichtbar (weder Simple noch Expert) | Expert-Mode aktivieren oder entfernen |

### 10.8 Auslagern in Tabs/Stepper/Accordion/Drawer

| # | Inhalt | Aktuell | Besser |
|---|--------|---------|--------|
| S1 | Phase-Pipeline Detail | Card-Grid im View | Collapsible Accordion-Section |
| S2 | Conversion Plan Details | Inline-Tabelle | Stepper-Flow (Preview→Review→Exec) |
| S3 | Advanced Settings | Alles auf einer Seite | Tabs: General · Advanced · Dangerous |
| S4 | Tool-Doku | Nicht vorhanden | Drawer/Flyout beim Tool-Klick |
| S5 | Score-Breakdown | Im ContextWing | Expandable Panel mit Chart |

---

## 11. Accessibility / Usability

### 11.1 WCAG AA Pflicht (Minimum)

| Anforderung | Status | Handlungsbedarf |
|------------|--------|-----------------|
| **Kontrast Text** ≥ 4.5:1 | ⚠ 2 Themes mit Problemen | ArcadeNeon TextMuted, SynthwaveDark TextMuted prüfen |
| **Kontrast Large Text** ≥ 3:1 | ✅ OK in allen Themes | — |
| **Focus Indicators** | ⚠ NavRail fehlt | Focus-Ring für alle interaktiven Elemente |
| **Keyboard Navigation** | ⚠ Partiell | TabIndex-Reihenfolge prüfen, NavRail-Traversierung |
| **Screen Reader** | ⚠ AutomationProperties teilweise | Alle Buttons brauchen Name + Description |
| **Touch Targets** ≥ 44×44px | ✅ MinTouchTarget definiert | In HighContrast auf 48×44 erhöht |
| **Status Triple-Encoding** | ⚠ Teilweise nur Farbe | Alle Status: Icon + Farbe + Text |
| **Reduce Motion** | ✅ ReduceMotion-Preference | Storyboards respektieren Setting |

### 11.2 Kontrast-Fixes nötig

| Theme | Token | Aktuell | Fix | Ratio |
|-------|-------|---------|-----|-------|
| ArcadeNeon | TextMuted | #9988CC | #A89FD4 | 4.5:1 auf #0A0A1E |
| SynthwaveDark | TextMuted | #9999CC | #A0A0D0 | 4.5:1 auf #0D0D1F |
| RetroCRT | TextMuted | #66AA66 | #5A9A5A | Prüfen auf #050A05 |

### 11.3 Keyboard-Navigation

| Bereich | Anforderung |
|---------|------------|
| NavRail | Tab durch alle Items, Enter/Space zum Aktivieren |
| SubTabs | Tab/Arrow-Keys durch Tabs |
| ToolCards | Tab durch Cards, Enter zum Öffnen |
| DataGrids | Arrow-Keys für Row-Navigation, Enter für Detail |
| Dialoge | Tab-Trap, Escape zum Schließen, Focus auf Primary Button |
| ActionBar | Tab-Reihenfolge: Primary → Secondary → Danger |

### 11.4 Screen Reader Support

```
Alle interaktiven Elemente brauchen:
├── AutomationProperties.Name       → Kurzer Label
├── AutomationProperties.HelpText   → Beschreibung der Aktion
└── AutomationProperties.LiveSetting → Polite für Status-Updates

Status-Änderungen als Live Regions:
├── Run Progress  → Polite ("67% complete")
├── Run Complete  → Assertive ("Analysis complete")
├── Danger Confirm→ Assertive ("Confirm move action")
└── Notifications → Polite ("3 new notifications")
```

### 11.5 Density Modes

| Mode | NavRail | Spacing | Font-Base | Touch-Target | Zielgruppe |
|------|---------|---------|-----------|-------------|------------|
| **Comfortable** | 72px mit Labels | Standard (LG) | 14px Body | 44×44px | Default, Touch |
| **Compact** | 48px Icon-only | Reduziert (SM/MD) | 13px Body | 36×36px | Power-User, kleine Screens |

---

## 12. Umsetzungsplan

### Phase 1: Quick Wins (1-2 Wochen)

| # | Maßnahme | Impact | Aufwand |
|---|----------|--------|---------|
| Q1 | Token-Dateien konsolidieren (_Tokens.xaml → _DesignTokens.xaml) | Konsistenz | Niedrig |
| Q2 | TextMuted WCAG-Fixes in 3 Themes | Accessibility | Niedrig |
| Q3 | Status Triple-Encoding (Icon+Farbe+Text) überall | Accessibility | Mittel |
| Q4 | Status-Chip-Redundanz entfernen (nur CommandBar + ContextWing) | Klarheit | Mittel |
| Q5 | Move-Button konsolidieren (nur ActionBar) | Konsistenz | Niedrig |
| Q6 | NavRail Focus-Ring hinzufügen | Accessibility | Niedrig |
| Q7 | Unused Sub-Tabs entfernen (GameKeyLab, leere Conversion) | Hygiene | Niedrig |

### Phase 2: Mittlere Umbauten (2-4 Wochen)

| # | Maßnahme | Impact | Aufwand |
|---|----------|--------|---------|
| M1 | Context Wing Adaptive Content | Relevanz | Mittel |
| M2 | Danger-Level-System + Typing-Confirm Dialog | Safety | Mittel |
| M3 | Tool-Katalog Gruppierung (Core/Maintenance/Advanced) | Navigation | Mittel |
| M4 | KPI-Progressive-Disclosure (max 4 primär, Rest Expander) | Dichte | Mittel |
| M5 | Stepper-Flow für Conversion (Preview→Review→Execute) | Workflow | Hoch |
| M6 | Review-Gate bei Execute (Warning/Block je nach Status) | Safety | Mittel |
| M7 | Tool-List-View als Alternative zu Grid | Power-User | Niedrig |
| M8 | Compact-Density-Mode verfeinern | Density | Mittel |

### Phase 3: Design-System-Vertiefung (4-6 Wochen)

| # | Maßnahme | Impact | Aufwand |
|---|----------|--------|---------|
| D1 | Shared MetricCard Component (StartView, ResultView, ProgressView) | Code-Qualität | Mittel |
| D2 | Theme Live-Preview in Appearance-Settings | UX | Hoch |
| D3 | Wizard-Overlay verbessern (Stepper, Progress, Skip) | Onboarding | Hoch |
| D4 | CommandBar slimmen (48px statt 56px) | Platz | Niedrig |
| D5 | NavRail responsive (72px → 48px bei schmalen Fenstern) | Responsive | Mittel |
| D6 | Glow-Effekte für Retro-Themes (optional, ReduceMotion-aware) | Delight | Mittel |
| D7 | Tool-Documentation-Drawer | Power-User | Hoch |
| D8 | Settings-Tabs (General/Advanced/Dangerous) | Organisation | Mittel |

### Priorisierungs-Matrix

```
                    IMPACT
              Low        High
         ┌──────────┬──────────┐
    Low  │  Q7, D4  │ Q1,Q2,Q5 │  ← Do first (Quick Wins)
EFFORT   │          │ Q6       │
         ├──────────┼──────────┤
    High │  M7, D6  │ M1,M2,M3 │  ← Do next (High Impact)
         │  D8      │ M5,M6   │
         └──────────┴──────────┘

Empfohlene Reihenfolge:
1. Token-Konsolidierung + WCAG-Fixes (Q1-Q2)
2. Status-Redundanz bereinigen (Q3-Q5)
3. Safety: Danger-Level + Review-Gate (M2, M6)
4. Context Wing Adaptive (M1)
5. Tool-Katalog Redesign (M3)
6. Stepper-Flows (M5)
7. Design-System-Vertiefung (D1-D8)
```

---

## Appendix A: Bestehende Assets und was beibehalten wird

| Asset | Status | Empfehlung |
|-------|--------|-----------|
| NavigationRail.xaml | Gut, leicht anpassen | Rail-Width 72px, Focus-Ring |
| SmartActionBar.xaml | Gut, Action-Height 72px | Danger-Buttons stärker differenzieren |
| CommandBar.xaml | Gut, 48px Höhe | Status-Chips konsolidieren |
| ContextPanel.xaml | Grundstruktur OK | Adaptive Content implementieren |
| _DesignTokens.xaml | Behalten als Single Source | _Tokens.xaml Werte übernehmen |
| _ControlTemplates.xaml | Behalten | Focus-Ring ergänzen |
| 6 Theme-Dateien | Alle behalten | WCAG-Fixes, Token-Alignment |
| WizardView.xaml | Behalten als Overlay | Stepper-UX verbessern |
| CommandPalette | Behalten | — |
| Keyboard Shortcuts (22) | Behalten | Dokumentation verbessern |

## Appendix B: WPF-Umsetzbarkeit

| Feature | WPF-Machbarkeit | Anmerkung |
|---------|-----------------|-----------|
| Theme-System | ✅ Vorhanden | DynamicResource funktioniert |
| Glow-Effekte | ✅ DropShadowEffect | Performance bei vielen Elementen beachten |
| Scanline-Overlay | ⚠ OpacityMask + Tiled Image | Nur als optionaler Effekt |
| Stepper-Control | ✅ Custom UserControl | ItemsControl mit DataTemplate |
| Adaptive Context Wing | ✅ ContentControl + DataTemplate | ViewModel steuert Template |
| Density Modes | ✅ DynamicResource für Spacing | Zwei ResourceDictionaries |
| Typing-Confirm | ✅ TextBox + Button-Binding | IsEnabled an Text-Match binden |
| Responsive NavRail | ✅ Width-Trigger + VisualState | WindowWidth-basierter Trigger |
| Virtual Tables | ✅ VirtualizingStackPanel | Bereits in DecisionsView |
| Focus-Ring | ✅ ControlTemplate FocusVisual | Standard WPF FocusVisualStyle |
| Live Regions | ✅ AutomationProperties | .LiveSetting = "Polite" |
| Gradient Headers | ✅ LinearGradientBrush | Subtil einsetzen |
| Drag & Drop Roots | ✅ AllowDrop + Drop-Event | Bereits implementiert |

## Appendix C: Metriken für Erfolg

| Metrik | Ziel | Messung |
|--------|------|---------|
| Time to First Preview | < 30 Sekunden (nach Root-Add) | User Testing |
| Time to Understand Results | < 10 Sekunden (nach Run) | Eye-Tracking/Feedback |
| Fehlbedienungs-Rate bei Move | 0% unbeabsichtigte Moves | Audit-Log |
| WCAG AA Compliance | 100% aller Themes | Automated Contrast Check |
| Keyboard-Only Workflow | 100% erreichbar | Manual Testing |
| Status-Verständnis | >90% korrekte Zuordnung | User Survey |
