# User Journey: Config & Filtering Setup in Romulus

> **Version**: 1.0  
> **Datum**: 2026-03-24  
> **Referenz**: config-filtering-redesign-jtbd.md

---

## User Persona

- **Wer**: Stefan – Hobbyist ROM-Collector, 500-2000 ROMs, 5-15 Konsolen
- **Ziel**: ROM-Sammlung aufräumen (Duplikate, Junk, Sortierung)
- **Kontext**: Abends am Desktop, will in max. 5 Minuten konfiguriert haben und Scan starten
- **Erfolgsmetrik**: Korrekte Konfiguration in unter 60 Sekunden, kein Zweifel welche Einstellung wo gilt

---

## Journey A: "Schnell loslegen" (IST-Zustand)

### Stage 1: App starten, Orientierung finden

**Was der Nutzer tut**: App öffnet, sieht Mission Control / StartView  
**Was der Nutzer denkt**: "OK, ich muss meinen ROM-Ordner angeben und loslegen"  
**Was der Nutzer fühlt**: 😊 Motiviert, klar  
**Pain Points**: 
- Intent-Cards (Preset-Auswahl) sind selbsterklärend – gut
- Drop-Zone für ROM-Ordner ist klar  
**Chancen**: Dieser Einstieg funktioniert

### Stage 2: Konfiguration anpassen wollen

**Was der Nutzer tut**: Will nur bestimmte Konsolen scannen → sucht den Ort dafür  
**Was der Nutzer denkt**: "Wo stelle ich ein, dass nur SNES und PS1 gescannt werden?"  
**Was der Nutzer fühlt**: 😕 Unsicher – Config? Filtering? Workflow?  
**Pain Points**:
- 3 mögliche Orte: Workflow-Tab, Filtering-Tab, Advanced-Tab
- Workflow-Tab zeigt Quick-Start + Regionen + ROM-Ordner → Mischung aus Setup und Config
- Filtering-Tab zeigt Dateitypen + Konsolen → aber als Checkbox-Wand
**Chancen**: Klare Trennung: Setup (Mission Control) vs. Konfiguration (Config)

### Stage 3: Konsolen-Filter setzen

**Was der Nutzer tut**: Klickt auf "Filtering"-Tab, scrollt durch Konsolen-Liste  
**Was der Nutzer denkt**: "So viele… wo ist SNES? Ah, unter Nintendo. Und PS1 ist unter Sony. Muss ich jetzt alles andere deaktivieren?"  
**Was der Nutzer fühlt**: 😤 Frustriert – zu viel Scrollen, keine Übersicht  
**Pain Points**:
- 65 Checkboxen, alle initially unchecked (= alle gescannt)
- Semantik "keine Auswahl = alles" ist nicht dokumentiert im UI
- Kein Counter "X von Y ausgewählt"
- Herstellergruppen klappen nicht zu
- Suchfeld ist kaum zu sehen
**Chancen**: Smart-Picker mit Suche, Chips, Counter, Akkordeon

### Stage 4: Region setzen

**Was der Nutzer tut**: Geht zum Workflow-Tab, sieht 4 Checkboxen (EU, US, WORLD, JP)  
**Was der Nutzer denkt**: "Ich bin aus Deutschland, ich will EU-Versionen bevorzugen. Aber was ist mit DE-spezifischen ROMs?"  
**Was der Nutzer fühlt**: 😐 Akzeptiert es, aber weiß dass es ungenau ist  
**Pain Points**:
- Nur 4 Regionen, obwohl 20+ existieren
- Keine Prioritätsreihenfolge (EU vor US? Oder gleichwertig?)
- Sub-Regionen (DE, FR, UK) nicht wählbar
- Region-Auswahl ist im Workflow-Tab, nicht im "eigentlichen" Config-Bereich
**Chancen**: Region-Priority-Ranker mit allen Regionen + Drag-Reihenfolge

### Stage 5: Verwirrung durch doppelte Optionen

**Was der Nutzer tut**: Sieht "Nur Vorschau (Dry Run)" im Workflow-Tab + identische Presets in Mission Control  
**Was der Nutzer denkt**: "Ich hab doch schon 'Sichere Vorschau' in Mission Control gewählt – gilt das jetzt noch oder hat der Workflow-Tab das überschrieben?"  
**Was der Nutzer fühlt**: 😟 Verunsichert – welche Source of Truth gilt?  
**Pain Points**:
- Quick-Start Presets in StartView UND SortView
- ROM-Verzeichnisse in StartView UND SortView
- Unklar ob Änderungen in einem Tab den anderen beeinflussen
**Chancen**: Eine Stelle für Presets (Mission Control), eine für Config (Config-Bereich)

### Stage 6: Scan starten

**Was der Nutzer tut**: Sucht den Run-Button unten links  
**Was der Nutzer denkt**: "Wo ist der Button? Ist der abgeschnitten?"  
**Was der Nutzer fühlt**: 😤 Genervt – wichtigster Button nicht sichtbar  
**Pain Points**:
- Buttons sind teilweise abgeschnitten (SmartActionBar-Layout)
- Bei kleinen Fenstergrößen noch schlimmer
**Chancen**: Fixed Bottom-Bar mit garantierter Sichtbarkeit

---

## Journey B: "Nur bestimmte Konsolen scannen" (SOLL-Zustand)

### Stage 1: App starten → Mission Control

**Was der Nutzer tut**: Sieht Hero-Screen mit Intent-Cards und ROM-Ordner  
**Was der Nutzer denkt**: "Ich kenne die App, will direkt zu den Filtern"  
**Was der Nutzer fühlt**: 😊 Klar – weiß wo er hin muss  
**Design**: Quick-Start bleibt als Einstieg, Link zu Config ist prominent

### Stage 2: Config > Filtering öffnen

**Was der Nutzer tut**: Nav-Rail → Config → Filtering  
**Was der Nutzer denkt**: "Da sind die Konsolen"  
**Was der Nutzer fühlt**: 😊 Gefunden  
**Design**: Config ist der einzige Ort für Filter und Regionen

### Stage 3: Konsolen mit Smart-Picker auswählen

**Was der Nutzer tut**: 
1. Tippt "SNES" in die Suchleiste → sieht sofort "SNES / Super Famicom"
2. Klickt zum Auswählen → Chip erscheint: `[SNES ✕]`
3. Tippt "PS1" → "PlayStation" → klickt → `[SNES ✕] [PS1 ✕]`
4. Sieht: "2 von 65 Konsolen ausgewählt"

**Was der Nutzer denkt**: "Wie bei einem Tag-Picker. Schnell."  
**Was der Nutzer fühlt**: 😊 Effizient, unter 10 Sekunden  
**Design**: 
- Suchfeld oben, prominent
- Chips für Auswahl
- Counter-Badge
- Alternativ: Hersteller aufklappen für Browse-Modus

### Stage 4: Region-Priorität setzen

**Was der Nutzer tut**: 
1. Config → Regionen
2. Sieht voreingestellte Liste: EU, US, WORLD, JP
3. Klickt "+ Region hinzufügen" → wählt "DE (Germany)"
4. Zieht DE auf Position 2 (nach EU, vor US)
5. Ergebnis: EU > DE > US > WORLD

**Was der Nutzer denkt**: "Jetzt weiß das Tool, dass ich deutsche ROMs bevorzuge, aber EU auch OK ist"  
**Was der Nutzer fühlt**: 😊 Überzeugt – meine Präferenz ist exakt abgebildet  
**Design**: Drag-Sortier-Liste mit allen verfügbaren Regionen

### Stage 5: Quick-Start / Scan starten

**Was der Nutzer tut**: Klickt Run in der stets sichtbaren Action-Bar  
**Was der Nutzer denkt**: "Der Button ist immer da, klar und groß"  
**Was der Nutzer fühlt**: 😊 Sicher  
**Design**: Action-Bar fixed am unteren Rand, nie abgeschnitten

---

## Emotionskurve: IST vs. SOLL

```
        IST-Zustand                    SOLL-Zustand
        
😊  Start ─────────┐              😊  Start ──────────────
                    │                                       \
😕          Config suchen           😊    Config direkt      \
                    │                                         \
😤     65 Checkboxen│              😊  Smart-Picker           \
                    │                                          \
😐       4 Regionen │              😊  Region-Ranker           \
                    │                                           \
😟  Doppelte Optionen              😊  Keine Redundanz          \
                    │                                            \
😤   Button abgeschnitten          😊  Sichtbare Action-Bar      ──→ Scan
```

---

## Kritische Momente (Moments of Truth)

| Moment | IST-Ergebnis | SOLL-Ergebnis |
|--------|-------------|---------------|
| Nutzer sucht Config-Ort | 3 mögliche Tabs → Verwirrung | 1 Config-Bereich → Klarheit |
| Nutzer wählt Konsolen | 65 Checkboxen → Frustration | Smart-Picker → 10 Sekunden |
| Nutzer setzt Region | 4 starre Optionen → Kompromiss | Prioritätsliste → Exaktheit |
| Nutzer will starten | Button ggf. abgeschnitten → Hemmung | Fixed Bar → Sofort sichtbar |
| Nutzer sieht Presets | 2x am gleichen Ort → "Was gilt?" | 1x in Mission Control → Klar |
