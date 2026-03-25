# Flow Specification: Config / Filtering / Regions Redesign

> **Version**: 1.0
> **Datum**: 2026-03-24
> **Referenz**: config-filtering-redesign-jtbd.md, config-filtering-redesign-journey.md
> **Zielgruppe**: Figma-Design-Team + WPF-Implementierung

---

## 1. Informationsarchitektur (NEU)

### Vorher (IST)

```
Mission Control (StartView)
├── Intent-Cards (3 Presets)          ← REDUNDANT mit SortView
├── ROM-Verzeichnisse                 ← REDUNDANT mit SortView
└── Drop-Zone

Config (SortView)
├── Quick-Start Presets               ← REDUNDANT mit StartView
├── Schnellstart (Region + Aktionen)  ← REDUNDANT mit SettingsView
├── ROM-Verzeichnisse                 ← REDUNDANT mit StartView
└── Optionen (DryRun, Convert)

Config > Filtering (ConfigFiltersView)
├── Dateityp-Checkboxen (3 Gruppen)
└── Konsolen-Checkboxen (65+, 4 Hersteller-Gruppen)

Config > Advanced (SettingsView)
├── 8 Kategorien in Sidebar
└── Region-Booleans (nur 4: EU, US, JP, WORLD)
```

### Nachher (SOLL)

```
Mission Control (Home)
├── Intent-Cards (3 Presets)          ← EINZIGER Ort für Quick-Start
├── ROM-Verzeichnisse                 ← EINZIGER Ort für Quellen
├── Health Overview
└── Recent Runs

Config
├── Regionen (Region Priority Ranker) ← NEU: Alle 20+ Regionen, Drag-Reihenfolge
├── Filtering                         ← VERBESSERT: Smart-Picker
│   ├── Konsolen-Picker (Suche + Chips + Akkordeon)
│   └── Dateityp-Picker (gruppiert, mit Presets)
├── Optionen                          ← KONSOLIDIERT von Workflow + Advanced
│   ├── Deduplizierung (ja/nein)
│   ├── Junk-Entfernung (ja/nein)
│   ├── Konsolen-Sortierung (ja/nein)
│   ├── Namenskollision (Strategie)
│   └── Konvertierung (ja/nein + Format)
├── Profile (Save/Load/Share)
└── Advanced (Safety, Conflict-Policy, Expert-Optionen)
```

**Eliminiert**: Workflow-Tab (SortView als eigenständiger Tab)
**Verschoben**: Presets + ROM-Dirs → nur Mission Control
**Konsolidiert**: Region + Aktionen + Optionen → Config

---

## 2. Screen-Spezifikationen

### Screen A: Config > Regionen (NEU)

**Entry Point**: Nav-Rail → Config → Regionen

#### Layout

```
┌─────────────────────────────────────────────────────┐
│ REGIONEN & PRIORITÄT                                │
│                                                     │
│ ℹ️ Höhere Position = bevorzugt bei Duplikaten.      │
│    Ziehe Regionen zum Umsortieren.                  │
│                                                     │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Aktive Regionen (Prioritätsreihenfolge)         │ │
│ │                                                 │ │
│ │  ≡  1. 🇪🇺 EU — Europe              [↑] [↓] [✕]│ │
│ │  ≡  2. 🇺🇸 US — United States       [↑] [↓] [✕]│ │
│ │  ≡  3. 🌍 WORLD — Region-Free       [↑] [↓] [✕]│ │
│ │  ≡  4. 🇯🇵 JP — Japan               [↑] [↓] [✕]│ │
│ │                                                 │ │
│ │  [+ Region hinzufügen ▾]                        │ │
│ │                                                 │ │
│ └─────────────────────────────────────────────────┘ │
│                                                     │
│ Schnell-Presets:                                    │
│  [EU-Fokus]  [US-Fokus]  [Multi-Region]  [Alle]    │
│                                                     │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Verfügbare Regionen                             │ │
│ │                                                 │ │
│ │ ▸ Europa (8)                                    │ │
│ │   EU, UK, DE, FR, ES, IT, NL, SE               │ │
│ │ ▸ Nordamerika (2)                               │ │
│ │   US, CA                                        │ │
│ │ ▸ Asien (6)                                     │ │
│ │   JP, KR, CN, TW, HK, ASIA                     │ │
│ │ ▸ Sonstige (4)                                  │ │
│ │   AU, BR, RU, WORLD                             │ │
│ │                                                 │ │
│ └─────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
```

#### Verhalten

| Element | Interaktion |
|---------|-------------|
| Drag-Handle (≡) | Drag & Drop zum Umsortieren |
| [↑] [↓] | Keyboard-Alternative zum Drag |
| [✕] | Region entfernen (zurück in "Verfügbar") |
| [+ Region] | Dropdown mit verfügbaren Regionen (nur nicht-aktive) |
| Presets | 1-Klick-Voreinstellungen, überschreiben aktuelle Liste |
| "EU-Fokus" | EU > DE > UK > US > WORLD |
| "US-Fokus" | US > CA > EU > WORLD |
| "Multi-Region" | EU > US > JP > WORLD |
| "Alle" | Alle Regionen alphabetisch, kein Score-Vorteil |

#### Mapping auf Core-Logik

- Position in der Liste → Score-Gewicht: Position 1 = höchster RegionScore
- Entfernte Regionen → Score 0 (nicht blockiert, nur nicht bevorzugt)
- Ergebnis fließt in `preferredRegions` Array (settings.json)
- `GetPreferredRegions()` im ViewModel liefert geordnetes Array

---

### Screen B: Config > Filtering > Konsolen-Picker (ÜBERARBEITET)

**Entry Point**: Nav-Rail → Config → Filtering

#### Layout

```
┌─────────────────────────────────────────────────────┐
│ KONSOLEN-FILTER                                     │
│                                                     │
│ ℹ️ Keine Auswahl = alle Konsolen werden gescannt   │
│                                                     │
│ 🔍 [Konsole suchen...                           ]  │
│                                                     │
│ Ausgewählt (3):                                     │
│ [SNES ✕] [PS1 ✕] [N64 ✕]                           │
│                                                     │
│ ─────────────────────────────────────────────────── │
│                                                     │
│ Schnellauswahl:                                     │
│ [Top 10] [Disc-basiert] [Handhelds] [Retro]        │
│                                                     │
│ ─────────────────────────────────────────────────── │
│                                                     │
│ ▾ Nintendo (12)                      [Alle] [Keine] │
│   ☑ SNES / Super Famicom                           │
│   ☑ N64 / Nintendo 64                              │
│   ☐ NES / Nintendo Entertainment System             │
│   ☐ GBA / Game Boy Advance                         │
│   ☐ GB / Game Boy                                   │
│   ☐ GBC / Game Boy Color                           │
│   ☐ NDS / Nintendo DS                              │
│   ☐ 3DS / Nintendo 3DS                             │
│   ☐ GC / GameCube                                  │
│   ☐ WII / Nintendo Wii                             │
│   ☐ WIIU / Nintendo Wii U                          │
│   ☐ SWITCH / Nintendo Switch                       │
│                                                     │
│ ▸ Sony (5)                           [Alle] [Keine] │
│ ▸ Sega (7)                           [Alle] [Keine] │
│ ▸ Atari (8)                          [Alle] [Keine] │
│ ▸ Commodore (3)                      [Alle] [Keine] │
│ ▸ Andere (20+)                       [Alle] [Keine] │
│                                                     │
│ ─────────────────────────────────────────────────── │
│ [Alle auswählen]  [Alle abwählen]  3 / 65 gewählt  │
└─────────────────────────────────────────────────────┘
```

#### Verhalten

| Element | Interaktion |
|---------|-------------|
| Suchfeld | Live-Filter: Tippt "pla" → zeigt nur PlayStation-Treffer |
| Chips | Klick auf ✕ entfernt Konsole aus Auswahl |
| Hersteller-Akkordeon | Default: alle zugeklappt. Aufklappen zeigt Konsolen |
| [Alle] / [Keine] per Gruppe | Setzt alle Konsolen eines Herstellers |
| Schnellauswahl-Buttons | Vordefinierte Sets: |
| — "Top 10" | SNES, NES, N64, PS1, PS2, GBA, MD, GB, GBC, PSP |
| — "Disc-basiert" | PS1, PS2, PS3, PSP, GC, WII, DC, SAT, SCD, 3DO... |
| — "Handhelds" | GB, GBC, GBA, NDS, 3DS, PSP, VITA, GG, NGP, WS... |
| — "Retro (<1995)" | NES, SNES, GB, GBC, MD, SMS, GG, A26, A78, C64... |
| Counter | "3 / 65 gewählt" – immer sichtbar unten |
| Leer-Zustand | Expliziter Hinweis: "Keine Auswahl = alle gescannt" |

#### Such-Algorithmus

```
Suche matcht gegen:
- Key ("SNES", "PS1", "N64")
- DisplayName ("Super Nintendo", "PlayStation", "Nintendo 64")
- FolderAliases ("snes", "super nintendo", "sfc")
```

---

### Screen C: Config > Filtering > Dateityp-Filter (LEICHT VERBESSERT)

**IST**: Checkboxen in 3 Spalten (Disc-Images, Archive, Cartridge/Modern) – funktioniert grundsätzlich.

**SOLL**: Gleiche Struktur behalten, aber ergänzen:

```
┌─────────────────────────────────────────────────────┐
│ DATEITYP-FILTER                                     │
│                                                     │
│ ℹ️ Keine Auswahl = alle Dateitypen werden gescannt  │
│                                                     │
│ Schnellauswahl:                                     │
│ [Disc-Images] [Archive] [Cartridge] [Alle] [Keine]  │
│                                                     │
│ Disc-Images     Archive       Cartridge / Modern    │
│ ☐ .chd          ☐ .zip        ☐ .nes               │
│ ☐ .iso          ☐ .7z         ☐ .gba               │
│ ☐ .cue          ☐ .rar        ☐ .nds               │
│ ☐ .gdi                        ☐ .nsp               │
│ ☐ .img                        ☐ .xci               │
│ ☐ .bin                        ☐ .wbfs              │
│ ☐ .cso                        ☐ .rvz               │
│ ☐ .pbp                                             │
│                                                     │
│                               4 / 22 gewählt        │
└─────────────────────────────────────────────────────┘
```

**Änderungen**:
- Counter "X / Y gewählt" hinzufügen
- Gruppen-Buttons [Disc-Images] etc. für 1-Klick-Auswahl
- Hinweis "Keine Auswahl = alle" prominent

---

## 3. Design-Prinzipien für diesen Flow

### Progressive Disclosure

- **Simple Mode**: Region-Presets (EU/US/Multi) + Top-10-Konsolen → 2 Klicks
- **Expert Mode**: Alle 20+ Regionen mit Drag-Reihenfolge + alle 65 Konsolen mit Suche

### Single Source of Truth

- Quick-Start / Presets: NUR in Mission Control
- ROM-Verzeichnisse: NUR in Mission Control
- Region-Setup: NUR in Config > Regionen
- Konsolen-Filter: NUR in Config > Filtering
- Optionen (Dedupe/Junk/Sort): NUR in Config > Optionen

### Feedback & Sichtbarkeit

- Immer Counter zeigen ("X von Y")
- Immer Hinweis bei leerem Filter ("Keine Auswahl = alle")
- Chips/Tags für aktive Auswahl – sofort sichtbar
- Action-Bar IMMER sichtbar (fixed bottom, MinHeight garantiert)

### Keyboard-Navigation

| Taste | Aktion |
|-------|--------|
| Tab | Durch Suchfeld → Chips → Gruppen → Checkboxen |
| Space | Toggle Checkbox / Chip-Remove |
| Enter | Gruppe aufklappen/zuklappen |
| Ctrl+A | Alle auswählen |
| Ctrl+Shift+A | Alle abwählen |
| ↑↓ in Region-Liste | Region verschieben |
| Delete auf Chip | Region/Konsole entfernen |

---

## 4. Accessibility-Anforderungen

### Keyboard-Navigation

- [ ] Alle interaktiven Elemente per Tab erreichbar
- [ ] Logische Tab-Reihenfolge (Suche → Chips → Gruppen → Items)
- [ ] Sichtbare Fokus-Indikatoren (3px Neon-Border)
- [ ] Enter/Space aktivieren Checkboxen und Buttons
- [ ] Escape schließt Dropdowns

### Screen Reader

- [ ] Suchfeld: `AutomationProperties.Name="Konsole suchen"`
- [ ] Chips: "SNES ausgewählt. Drücken zum Entfernen"
- [ ] Counter: LiveRegion, wird bei Änderung angesagt
- [ ] Akkordeon-Status: "Nintendo, 12 Konsolen, zugeklappt"
- [ ] Region-Position: "EU, Position 1 von 4"

### Visuelle Accessibility

- [ ] Kontrast mindestens 4.5:1 (WCAG AA) für alle Themes
- [ ] Mindest-Touch-Target: 44×44px (auch Checkboxen)
- [ ] Keine Information nur über Farbe (Icons + Text)
- [ ] Text bis 200% Zoom ohne Layout-Bruch

---

## 5. Mapping auf bestehende ViewModel-Architektur

### Region Priority Ranker

| Neues UI-Element | Bestehendes ViewModel-Property | Änderungsbedarf |
|-----------------|-------------------------------|-----------------|
| Sortierte Regionsliste | `PreferEU`, `PreferUS`, `PreferJP`, `PreferWORLD` (Booleans) | **Ersetzen** durch `ObservableCollection<RegionPriorityItem>` |
| Region-Presets | — | **Neu**: `ApplyRegionPreset(string presetName)` Command |
| Drag-Reihenfolge | — | **Neu**: `MoveRegionUp/Down` Commands |
| "+ Region hinzufügen" | — | **Neu**: `AvailableRegions` (nicht-aktive) + `AddRegion` Command |

### Konsolen Smart-Picker

| Neues UI-Element | Bestehendes ViewModel-Property | Änderungsbedarf |
|-----------------|-------------------------------|-----------------|
| Suchfeld | `ConsoleFilterText` (existiert!) | Behalten, prominenter machen |
| Chip-Ansicht | — | **Neu**: Abgeleitet aus `ConsoleFilters.Where(x => x.IsChecked)` |
| Akkordeon | `ConsoleFiltersView` mit GroupDescription | Behalten, XAML-Template ändern |
| Schnellauswahl | — | **Neu**: `ApplyConsolePreset(string presetName)` Command |
| Counter | — | **Neu**: `SelectedConsoleCount` Computed Property |
| Alle/Keine per Gruppe | — | **Neu**: `SelectAllInGroup(string group)` / `DeselectAllInGroup(string group)` |

### Workflow-Tab → Elimination

| Bisheriges Element | Neuer Ort | Migration |
|-------------------|-----------|-----------|
| Quick-Start Presets (SortView) | Mission Control (StartView) | XAML entfernen aus SortView |
| Region-Checkboxen (SortView) | Config > Regionen | XAML + ViewModel refactoren |
| Aktions-Checkboxen (SortView) | Config > Optionen | XAML verschieben |
| ROM-Verzeichnisse (SortView) | Mission Control | XAML entfernen aus SortView |
| DryRun-Checkbox (SortView) | Globaler Run-Mode (Header) | Refactoring zu Setter/Getter |

---

## 6. Handoff an Design-Team

**Research-Artefakte bereit:**
- Jobs-to-be-Done: [config-filtering-redesign-jtbd.md](config-filtering-redesign-jtbd.md)
- User Journey: [config-filtering-redesign-journey.md](config-filtering-redesign-journey.md)
- Flow Specification: dieses Dokument

**Nächste Schritte:**
1. User Journey durchlesen → emotionale Pain Points verstehen
2. Flow Specification als Basis für Figma-Wireframes nutzen
3. Region Priority Ranker als Prototyp bauen (Drag & Drop interaktion)
4. Konsolen Smart-Picker als Prototyp bauen (Suche + Chips)
5. Accessibility-Checklist abarbeiten
6. Gegen JTBD-Erfolgsmetriken validieren:
   - Erster Scan in <60 Sekunden
   - Konsolen-Auswahl in <10 Sekunden
   - Region-Setup reflektiert exakte Nutzerpräferenz

**Kern-Erfolgsmetrik**: Kein UI-Element existiert an mehr als einem Ort.
