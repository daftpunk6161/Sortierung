# Jobs-to-be-Done Analyse: Config / Filtering / Regions / Workflow-Redundanz

> **Version**: 1.0  
> **Datum**: 2026-03-24  
> **Scope**: 5 zusammenhängende UX-Probleme im Config-Bereich  
> **Status**: Discovery → bereit für Figma-Umsetzung

---

## 1. Identifizierte Probleme (Ist-Zustand)

### Problem 1: Konsolen-Filter skaliert nicht (Filtering-Tab)

**Screenshot-Befund**: 65+ Konsolen als flache Checkbox-Liste, gruppiert nach Hersteller.

**Warum das nicht funktioniert**:
- Bei 65 Konsolen und wachsender Liste wird die Seite unüberschaubar
- Die häufigste Aktion ("ich will nur SNES + N64 + PS1") erfordert Scrollen durch ~60 Einträge
- Kein visuelles Feedback, wie viele Konsolen selektiert sind
- Kein Shortcut für "beliebteste Konsolen" oder "nur Disc-basierte"
- Suchfeld existiert im ViewModel, ist aber in der aktuellen UI kaum prominent

**Impact**: Nutzer vermeiden den Filter oder setzen ihn falsch → entweder zu viele oder zu wenig Konsolen gescannt.

### Problem 2: Buttons unten links abgeschnitten

**Screenshot-Befund**: SmartActionBar-Buttons (Run, Cancel, Convert Only) sind am unteren linken Rand visuell abgeschnitten/nicht vollständig sichtbar.

**Ursache**: Vermutlich `MinHeight`/`Padding`-Problem in SmartActionBar.xaml oder Window-Sizing.

**Impact**: Hauptaktions-Buttons – die wichtigsten interaktiven Elemente – sind nicht zuverlässig sichtbar.

### Problem 3: Workflow-Tab hat redundante Funktionen

**Screenshot-Befund**: Der Workflow-Tab (SortView) enthält:
- Quick-Start Presets (Safe DryRun / Full Sort / Convert)
- Region-Auswahl (EU, US, WORLD, JP)
- Aktions-Checkboxen (Dupes / Junk / Sort)
- ROM-Verzeichnisse (Drag & Drop + Add/Remove)
- Optionen (DryRun, Convert, Namenskollision)

Gleichzeitig existiert:
- **Mission Control / StartView**: Intent-Cards (identisch zu Presets) + Drop-Zone + ROM-Verzeichnisse
- **Config / SettingsView**: Filter, Regionen, Optionen

**Job-Overlap**: Mindestens 3 Orte zeigen teilweise identische Konfiguration.

### Problem 4: Region-Auswahl unvollständig

**Ist-Zustand**: Simple Mode zeigt nur 4 Regionen: EU, US, WORLD, JP.

**Realität**: Das System kennt 20+ Regionen (UK, DE, FR, ES, IT, NL, SE, AU, CA, BR, KR, CN, ASIA, RU, SCAN, PL, TW, HK...). Im Expert-Modus gibt es nur Boolean-Properties (`PreferEU`, `PreferUS`, `PreferJP`, `PreferWORLD`) – die Sub-Regionen werden intern gemappt, sind aber weder sichtbar noch beeinflussbar.

**Impact**: Nutzer aus z.B. Brasilien, Korea oder Australien können ihre Region nicht als Priorität setzen. Wer EU+DE spezifisch bevorzugen will, hat keine Möglichkeit.

### Problem 5: Quick-Start / Schnellstart an mehreren Orten

**Ist-Zustand**:
- **StartView (Mission Control)**: Intent-Cards (3 Presets)
- **SortView (Config > Workflow-Tab)**: Quick-Start Presets (3 Radio Buttons)
- **SortView**: Simple-Mode-Panel (vereinfachte Config)

**Problem**: Derselbe Nutzer-Job ("schnell loslegen") wird an 2-3 Stellen gelöst, mit leicht unterschiedlicher Darstellung. Das erzeugt Verwirrung, nicht Geschwindigkeit.

---

## 2. Jobs-to-be-Done Analyse

### Job 1: "Sammlung schnell aufräumen"

```
Wenn ich eine ROM-Sammlung aufräumen will,
möchte ich in wenigen Klicks den Scan starten,
damit ich schnell sehe, was bereinigt werden kann – ohne erst alles konfigurieren zu müssen.
```

**Incumbent**: Manuelle Ordner-Strukturierung oder externes Tool.  
**Schmerz**: Zu viele Optionen an zu vielen Stellen. Wo starte ich? Was muss ich einstellen?  
**Erfolgsmetrik**: Erster Scan innerhalb von 60 Sekunden nach App-Start.

### Job 2: "Gezielt bestimmte Konsolen scannen"

```
Wenn ich nur meine SNES- und PS1-Sammlung aufräumen will,
möchte ich schnell genau diese Konsolen auswählen,
damit der Scan fokussiert ist und ich keine irrelevanten Ergebnisse bekomme.
```

**Incumbent**: Manuell ROM-Ordner eingrenzen.  
**Schmerz**: 65 Checkboxen durchscrollen, um 2 zu finden.  
**Erfolgsmetrik**: Konsolenauswahl in <10 Sekunden.

### Job 3: "Meine regionalen Präferenzen richtig setzen"

```
Wenn ich eine Sammlung dedupliziere,
möchte ich meine echte Regionspräferenz exakt angeben (z.B. EU > DE > US),
damit der Winner-Selection-Algorithmus die richtige Version behält.
```

**Incumbent**: Defaults akzeptieren und hoffen.  
**Schmerz**: Nur 4 Regions-Checkboxen, keine Reihenfolge, keine Sub-Regionen.  
**Erfolgsmetrik**: Region-Setup reflektiert exakte Nutzerpräferenz inkl. Prioritätsreihenfolge.

### Job 4: "Konfiguration verstehen, nicht dreimal suchen"

```
Wenn ich Einstellungen anpassen will,
möchte ich genau eine Stelle haben, wo alle Config-Optionen sind,
damit ich nicht zwischen Mission Control, Workflow und Settings hin- und herspringen muss.
```

**Incumbent**: Trial-and-Error durchklicken.  
**Schmerz**: Identische Optionen an verschiedenen Orten. Welcher Wert gilt?  
**Erfolgsmetrik**: Jede Einstellung existiert an genau einem Ort.

---

## 3. Persona

### Primär-Persona: "Stefan – der Sammler"

- **Rolle**: Hobbyist ROM-Collector, 500-2000 ROMs, 5-15 Konsolen
- **Technik-Level**: Mittel – kann CLI bedienen, bevorzugt aber GUI
- **Kontext**: Abends am Desktop, will Sammlung in 30 Min aufräumen
- **Kritische Konsolen**: Top 10 (SNES, NES, N64, PS1, PS2, GBA, MD, GB, GBC, PSP)
- **Region**: EU-basiert, will EU > US > World, nie JP
- **Frustration**: "Warum gibt es 3 verschiedene Stellen mit den gleichen Optionen?"
- **Risikotoleranz**: Mittel – DryRun zuerst, dann Ausführen nach Prüfung

### Sekundär-Persona: "Alex – der Power-User"

- **Rolle**: Erfahrener Collector, 10.000+ ROMs, 30+ Konsolen
- **Technik-Level**: Hoch – nutzt CLI-Profile, will granulare Kontrolle
- **Kontext**: Regelmäßige Batches, hat feste Profile
- **Kritische Features**: Sub-Regionen, Konsolen-Filter per Profil, Keyboard-Navigation
- **Frustration**: "Zeig mir alle Optionen, aber organisiert"

---

## 4. Design-Empfehlungen (Lösungsrichtungen)

### E1: Konsolen-Filter → Smart-Picker statt Checkbox-Wand

**Statt**: 65 Checkboxen in einer langen Liste  
**Besser**: Multi-Select-Picker mit diesen Mechaniken:

| Feature | Beschreibung |
|---------|-------------|
| **Suchfeld (prominent)** | Tippen filtert live: "pla" → PlayStation, PlayStation 2, PlayStation 3 |
| **Chip/Tag-Ansicht** | Ausgewählte Konsolen als Chips oben anzeigen (wie E-Mail-Empfänger) |
| **Smart-Gruppen** | "Beliebte" (Top 10), "Disc-basierte", "Handhelds", "Retro (<1995)" |
| **Hersteller-Akkordeon** | Nur aufklappen was nötig ist, statt alles gleichzeitig zu zeigen |
| **Alle/Keine-Buttons** | Pro Hersteller + Global |
| **Counter-Badge** | "5 von 65 Konsolen ausgewählt" |
| **Leer = Alle** | Expliziter Hinweis: "Keine Auswahl = alle Konsolen werden gescannt" |

**Vorbild**: VS Code Extension-Picker, Jira Label-Selector, macOS Finder Tag-Picker.

### E2: Region-Auswahl → Prioritäts-Ranker

**Statt**: 4 gleichwertige Checkboxen (EU, US, WORLD, JP)  
**Besser**: Drag-and-Drop Region Priority List:

```
Meine Region-Priorität (ziehen zum Umsortieren):
┌───────────────────────────────────┐
│ 1. 🇪🇺 EU (Europe)              │  ↕
│ 2. 🇩🇪 DE (Germany)             │  ↕
│ 3. 🇺🇸 US (United States)       │  ↕
│ 4. 🌍 WORLD (Region-Free)        │  ↕
│                                   │
│ + Region hinzufügen...            │
│ ─────────────────────────────────│
│ Nicht gewünscht:                  │
│ 🇯🇵 JP (Japan)                  │  ✕
└───────────────────────────────────┘

ℹ️ Höhere Position = bevorzugt bei Duplikaten.
   Regionen unter "Nicht gewünscht" werden beim Dedup deprioritisiert.
```

| Feature | Beschreibung |
|---------|-------------|
| **Reihenfolge = Priorität** | Position 1 hat höchsten Score |
| **Alle Regionen verfügbar** | 20+ Regionen auswählbar, nicht nur 4 |
| **Gruppen** | "Europa" aufklappbar → EU, UK, DE, FR, ES, IT, NL, SE |
| **Auto-Detect** | OS-Locale erkennen und als Vorschlag anbieten |
| **Simple Mode** | Preset-Buttons ("EU-Fokus", "US-Fokus", "Multi-Region") |
| **Drag & Drop** | Reihenfolge setzt sich in Score-Berechnung um |

### E3: Workflow-Tab eliminieren → Config konsolidieren

**Problem**: Workflow-Tab, Mission Control und Settings haben überlappende Inhalte.

**Empfehlung**:

```
MISSION CONTROL (Home)
├── Quick-Start (einziger Ort für Presets/Intent-Cards)
├── ROM-Verzeichnisse (einziger Ort für Quellen)
├── Health Overview
└── Recent Runs

CONFIG (Konfiguration)
├── Regionen (Priority-Ranker) ← NEU
├── Filtering (Konsolen-Picker + Extension-Filter) ← VERBESSERT
├── Optionen (Sort, DryRun, Convert, Naming) ← konsolidiert
├── Profile (Save/Load)
└── Advanced (Safety, Conflict-Policy)
```

**Workflow-Tab wird aufgelöst**:
- Quick-Start Presets → nur in Mission Control
- Region-Auswahl → Config > Regionen
- Aktions-Checkboxen → Config > Optionen
- ROM-Verzeichnisse → nur in Mission Control
- DryRun-Hinweis → globaler Run-Status in Header

**Vorteil**: Jede Einstellung hat genau einen Ort. Kein "Source of Truth"-Konflikt mehr.

### E4: Quick-Start → einmal, prominent, in Mission Control

**Statt**: 3x Presets an verschiedenen Orten  
**Besser**: 1x Presets auf Mission Control, klar als Einstiegspunkt:

```
MISSION CONTROL
┌─────────────────────────────────────────────────┐
│  🎯 WAS WILLST DU TUN?                         │
│                                                  │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐      │
│  │ 🔍       │  │ 🗂️       │  │ ⚡       │      │
│  │ Prüfen   │  │ Volle    │  │ Konver-  │      │
│  │ Nur      │  │ Sortie-  │  │ tierung  │      │
│  │ Vorschau │  │ rung     │  │          │      │
│  │          │  │          │  │          │      │
│  │ Preview  │  │ Dedupe + │  │ CHD/RVZ  │      │
│  │ only     │  │ Sort +   │  │ ZIP      │      │
│  │          │  │ Junk     │  │          │      │
│  └──────────┘  └──────────┘  └──────────┘      │
│                                                  │
│  → Config anpassen (Link zu Config)              │
└─────────────────────────────────────────────────┘
```

Wer detailliertere Einstellungen will, geht von hier zu Config – nicht umgekehrt.

### E5: Buttons unten links – MinHeight + Padding Fix

**Kurzfristig**:
- `SmartActionBar`: `MinHeight="52"` statt aktuell vermutlich zu knapp
- Window `MinHeight` prüfen (sollte Buttons nie abschneiden)
- Padding-Bottom am Hauptfenster sicherstellen

**Langfristig** (im Redesign): 
- ActionBar als fixed Bottom-Element mit eigenem Container, nie im Scrollbereich
- Responsive: Buttons umbrechen statt abschneiden

---

## 5. Priorisierung der Änderungen

| # | Empfehlung | Aufwand | Impact | Priorität |
|---|-----------|---------|--------|-----------|
| E5 | Button-Abschneidung fixen | Klein | Hoch (Blocker) | **P0** |
| E3 | Workflow-Tab eliminieren | Mittel | Hoch | **P1** |
| E4 | Quick-Start konsolidieren | Klein | Mittel | **P1** |
| E1 | Konsolen Smart-Picker | Groß | Hoch | **P2** |
| E2 | Region Priority-Ranker | Groß | Hoch | **P2** |

---

## 6. Offene Annahmen

- Die 65 Konsolen wachsen weiter (z.B. historische Computer, Homebrew-Plattformen)
- Sub-Regionen (DE, FR, UK etc.) sollen langfristig im Score berücksichtigt werden
- Profile speichern auch Konsolen-Filter und Region-Priorität
- Expert-Mode soll alle Regionen und Optionen zeigen, Simple-Mode nur die häufigsten
