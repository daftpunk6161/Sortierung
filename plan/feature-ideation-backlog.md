# Romulus – Feature Ideation & Backlog

> **Erstellt:** 2026-03-29  
> **Status:** Living Document  
> **Zielgruppe:** Produktentscheidung, Priorisierung, Sprint-Planung

---

## 1. Executive Summary

### Kurzfazit

Romulus hat bereits ein aussergewoehnlich breites Feature-Set: Deduplizierung mit Multi-Factor-Scoring, DAT-basierte Verifizierung, automatische Format-Konvertierung mit Integritaetspruefung, dreifachen Entry Point (GUI/CLI/API), Audit-Trail mit Rollback. Das ist mehr als jedes andere Einzel-Tool bietet.

Die **groessten Produktluecken** liegen nicht in fehlender Kernfunktionalitaet, sondern in:

1. **Kein Metadata-/Artwork-Enrichment** — Romulus weiss alles ueber Datei-Integritaet, aber nichts ueber das Spiel selbst (Cover, Beschreibung, Genre, Screenshots)
2. **Keine Echtzeit-Sammlungsgesundheit** — nach einem Run ist die Information statisch; es fehlt ein "Living Dashboard" das die Sammlung kontinuierlich ueberwacht
3. **Kein IPS/BPS/UPS Patching** — ROM-Patching ist ein taeglicher Community-Workflow, der komplett fehlt
4. **Keine Multi-Device/Frontend-Sync** — die Bruecke zwischen "Sammlung bereinigt" und "laeuft auf meinem RetroArch/MiSTer/Handheld" ist manuell
5. **Keine Collection-Diff/Merge** — wenn man 2 Sammlungen zusammenfuehren oder vergleichen will, fehlt das komplett

### Top-5 spannendste neue Ideen

| # | Idee | Warum spannend |
|---|------|----------------|
| 1 | **Collection Health Monitor** | Kein anderes Tool ueberwacht eine Sammlung kontinuierlich nach DAT-Updates, neuen Dumps, Format-Verbesserungen |
| 2 | **Smart Patch Pipeline** | IPS/BPS/UPS-Patching integriert in den Run-Flow mit DAT-Verifikation des Patch-Outputs — existiert nirgends so |
| 3 | **Frontend Sync Engine** | One-Click-Export fuer RetroArch/ES/LaunchBox/MiSTer/Analogue Pocket mit Playlists, Artwork-Pfaden, Core-Mapping |
| 4 | **Collection Diff & Merge** | Zwei Sammlungen vergleichen, Luecken zeigen, intelligent zusammenfuehren — kein Desktop-Tool kann das |
| 5 | **Metadata Enrichment via IGDB/ScreenScraper** | Genre, Spielzeit, Rating, Beschreibung zu jedem Match — macht die Sammlung lebendig |

### Community-Mehrwert

Die Features mit dem hoechsten Community-Impact sind: Smart Patch Pipeline (taeglich gebraucht), Collection Health Monitor (spart staendig manuelle DAT-Arbeit), Frontend Sync Engine (schliesst die letzte Meile), und Missing/Wishlist-Tracker (gibt Sammlern ein klares Ziel).

---

## 2. Analyse der Produktluecken

### Aktuelle Staerken

- **Deduplizierung mit Multi-Factor-Scoring**: Region + Format + Version + Header + Completeness + Size — granularer als jedes andere Tool
- **Conversion Pipeline mit Verify**: CUE→CHD, ISO→RVZ, ZIP-Normalisierung — automatisch, mit Source-Backup und Hash-Verify
- **Drei Entry Points** (GUI, CLI, API): Kein anderes ROM-Tool bietet eine REST API mit SSE-Streaming
- **Audit/Rollback mit SHA256-Signatur**: Nachvollziehbarkeit und Undo-Faehigkeit auf Enterprise-Niveau
- **DAT-Ecosystem**: Automatischer DAT-Download, Pack-Import, Recursive Discovery, Multi-Source (Redump, No-Intro, Non-Redump, TOSEC, FBNEO)
- **69 Konsolen** mit differenzierter ConversionPolicy pro System
- **5400+ Tests**, TDD-Workflow, Benchmark-Suite mit Ground-Truth

### Aktuelle Luecken

| Luecke | Schwere | Was fehlt |
|--------|---------|-----------|
| **Metadata/Artwork** | Hoch | Kein Spiel-Cover, Genre, Rating, Beschreibung — die Sammlung besteht nur aus Dateien und Hashes |
| **Patching (IPS/BPS/UPS)** | Hoch | ROM-Patching ist ein Kern-Workflow der Community, Romulus ignoriert ihn komplett |
| **Frontend-Export nur stubbed** | Mittel | RetroArch/ES/LaunchBox XML-Code existiert, ist aber nicht in GUI/CLI verdrahtet |
| **Keine Collection-Uebersicht** | Mittel | Kein Dashboard: "Wie vollstaendig ist meine Sammlung pro System?" |
| **Keine Multi-Source-Merge** | Mittel | 2 HDDs mit ROMs zusammenfuehren ist manuell |
| **Kein Watch/Continuous Mode** | Mittel | Romulus ist Run-basiert; neue Dateien erfordern kompletten Re-Run |
| **Kein Playlist-Generator** | Niedrig | Multi-Disc-Games brauchen .m3u/.cue Playlists fuer Emulatoren |
| **Kein Header-Management** | Niedrig | No-Intro headered/unheadered — Romulus kann nicht umschalten |
| **Kein Set-Merge/Split fuer Arcade** | Niedrig | MAME merged/split/non-merged Set-Transformation fehlt |

### Pain Points pro Nutzergruppe

| Nutzer | Groesstens Problem |
|--------|--------------------|
| **Einsteiger** | "Ich hab 50.000 ROMs auf einer HDD, was jetzt?" — Kein geführter Ersteinrichtungs-Wizard mit Empfehlungen |
| **Power-User** | "Ich will meine PS1-Sammlung gegen Redump auf 100% bringen" — kein Completionist-Tracker |
| **Sammler** | "Meine 2 NAS-Pfade sollen merged werden ohne Duplikate" — kein Cross-Source-Merge |
| **Archivar** | "Was hat sich seit letztem DAT-Update geaendert?" — kein DAT-Diff/Changelog |
| **Handheld-User** | "Ich will genau diese ROMs auf mein Miyoo Mini" — kein Device-Profile-Export |
| **Frontend-User** | "Romulus hat sortiert, aber LaunchBox weiss nichts davon" — Export ist Stub |

---

## 3. Neue Feature-Vorschlaege

---

### FEAT-001: Collection Health Monitor (FileSystemWatcher + scheduled DAT-Diff)

**Kurzbeschreibung:** Ein Hintergrund-Service, der die Sammlung kontinuierlich ueberwacht. Erkennt neue/geloeschte/veraenderte Dateien, vergleicht bei jedem DAT-Update automatisch den Ist-Stand mit dem Soll-Stand und zeigt proaktiv: "3 neue Dumps verfuegbar", "12 Dateien haben falschen Hash seit letztem Scan", "NES-Sammlung jetzt 98,2% komplett".

**Problem:** Heute muss der User nach jedem DAT-Update und jeder manuellen Datei-Aenderung einen vollstaendigen Re-Run starten, um den Status zu kennen. Das ist bei grossen Sammlungen (100k+ ROMs) frustrierend.

**Zielgruppe:** Sammler, Archivare, Power-User

**Warum Mehrwert:** Kein einziges Desktop-ROM-Tool bietet proaktive Sammlungs-Ueberwachung. RomVault scannt manuell, clrmamepro scannt manuell, Igir ist einmalig. Das waere ein klarer Paradigmenwechsel.

**Gibt es aehnlich?** Nein. RomM (Web) zeigt den Scan-Status, aber nicht als Hintergrund-Ueberwachung. Kein Desktop-Tool hat das.

**Neu/besser:** Voellig neu fuer Desktop-ROM-Tools. Vergleichbar mit "Health Monitoring" in DevOps-Tools.

**Aufwand:** Gross  
**Risiko:** Mittel (Performance bei vielen Dateien, FileSystemWatcher-Limits auf NAS)  
**Prioritaet:** P2  
**Empfehlung:** Spaeter — nach Kern-Release, als starkes Update-Feature  
**Tags:** `Differenzierungsmerkmal`, `Power-User Win`

---

### FEAT-002: Smart Patch Pipeline (IPS/BPS/UPS/xdelta)

**Kurzbeschreibung:** Romulus erkennt Patch-Dateien neben ROMs (oder in einem Patch-Ordner), wendet sie automatisch an, verifiziert das Ergebnis gegen eine DAT (falls vorhanden) und sortiert das gepatcht ROM korrekt ein. Unterstuetzt IPS, BPS, UPS und xdelta. Reversibel durch Audit-Trail.

**Problem:** ROM-Patching (Uebersetzungen, Bugfixes, Hacks) ist ein taeglicher Community-Workflow. Heute braucht man separate Tools (Floating IPS, beat, MultiPatch) und muss manuell patchen, umbenennen, verifizieren, einsortieren.

**Zielgruppe:** Alle — besonders Uebersetzungs-Community, Hack-Spieler, Einsteiger

**Warum Mehrwert:** Patchinng + Verifizierung + Sortierung als integrierter Schritt eliminiert 4-5 manuelle Schritte. Igir kann Patches anwenden, aber ohne DAT-Verifizierung des Outputs.

**Gibt es aehnlich?** Igir hat `--patch` Support (IPS/BPS/UPS/xdelta). Aber: keine Post-Patch DAT-Verifikation, kein Audit-Trail, keine GUI.

**Neu/besser:** Post-Patch-Verifikation gegen Custom-DATs + Audit-Trail + Undo waere einzigartig.

**Aufwand:** Mittel  
**Risiko:** Niedrig (Patch-Formate sind gut dokumentiert)  
**Prioritaet:** P1  
**Empfehlung:** Jetzt umsetzen — hoher Community-Bedarf, mittlerer Aufwand  
**Tags:** `Community Win`, `Differenzierungsmerkmal`

---

### FEAT-003: Frontend Sync Engine

**Kurzbeschreibung:** Nach einem Run oder Conversion kann der User per Knopfdruck Export-Pakete fuer spezifische Frontends/Devices generieren: RetroArch (.lpl), EmulationStation (gamelist.xml), LaunchBox (launchbox.xml), MiSTer FPGA (korrekte Ordnerstruktur + core-mapping), Analogue Pocket (Assets/), Batocera, OnionOS etc. Inklusive Playlists fuer Multi-Disc-Games.

**Problem:** Die "letzte Meile" zwischen bereinigter Sammlung und spielbarem Setup ist heute komplett manuell. Die XML-Export-Stubs existieren in Romulus schon, sind aber nicht verdrahtet.

**Zielgruppe:** Alle Frontend-User, Handheld-Besitzer (Miyoo Mini, RP2S, Steam Deck)

**Warum Mehrwert:** Igir hat Output-Path-Tokens fuer viele Devices ({batocera}, {onion}, {pocket} etc.), aber kein integriertes Playlist/Artwork-Management. Der Schritt "Sammlung → laeuft auf meinem Geraet" ist ueberall manuell.

**Gibt es aehnlich?** Igir hat Device-Tokens. Steam ROM Manager exportiert zu Steam. Aber: kein Tool kombiniert DAT-verified Sorting + Conversion + Frontend-Export in einem Flow.

**Neu/besser:** Integration in den bestehenden Run-Flow waere einzigartig: Sort → Convert → Export → Playlists → fertig.

**Aufwand:** Mittel (Stubs existieren schon)  
**Risiko:** Niedrig  
**Prioritaet:** P1  
**Empfehlung:** Jetzt umsetzen — die Stubs existieren, Community-Bedarf ist hoch  
**Tags:** `Community Win`, `Power-User Win`

---

### FEAT-004: Collection Diff & Merge

**Kurzbeschreibung:** Zwei oder mehr ROM-Verzeichnisse/Sammlungen vergleichen: "Was hat A, was B nicht hat? Was ist in beiden, aber unterschiedlich? Welche Version ist besser?" Mit Merge-Funktion: die beste Version behalten, Duplikate entfernen, alles in ein Target-Verzeichnis zusammenfuehren.

**Problem:** Nutzer mit mehreren HDDs, NAS-Laufwerken oder Backups muessen heute manuell vergleichen. Oder beide als Roots angeben und hoffen, dass Dedup greift — aber ohne klare Diff-Ansicht.

**Zielgruppe:** Sammler mit grossen/verteilten Sammlungen, NAS-User

**Warum Mehrwert:** Spart Stunden manueller Vergleichsarbeit. Reduziert Duplikat-Speicher erheblich.

**Gibt es aehnlich?** Nein. RomVault vergleicht gegen DATs, nicht gegen andere Sammlungen. clrmamepro ebenso. Kein Tool hat "Collection A vs Collection B".

**Neu/besser:** Voellig neu. Die naechste logische Erweiterung der Dedup-Engine.

**Aufwand:** Mittel  
**Risiko:** Niedrig (nutzt bestehende Hash-/Dedup-Infrastruktur)  
**Prioritaet:** P2  
**Empfehlung:** Spaeter — stark, aber nicht release-kritisch  
**Tags:** `Differenzierungsmerkmal`, `Power-User Win`

---

### FEAT-005: Completionist Tracker / Missing ROM Dashboard

**Kurzbeschreibung:** Pro Konsole zeigen: "Du hast 2.847 von 3.012 verifizierten ROMs (94,5%). 165 fehlen. Hier die Liste." Mit Filter: nur fehlende Retail-Games, nur bestimmte Regionen, MIA-Status (DAT hat Eintrag, aber Dump existiert weltweit nicht). Exportierbar als Wunschliste.

**Problem:** "Wie komplett ist meine Sammlung?" ist die haeufigste Frage von Sammlern. RomVault zeigt Prozentwerte, aber kein anderes Tool prueft aktiv gegen den vollstaendigen DAT-Katalog und zeigt fehlende Titel.

**Zielgruppe:** Sammler, Archivare, Completionists

**Warum Mehrwert:** Gibt der Sammlung ein Ziel. Motiviert. Zeigt MIA-Status (ROM existiert nirgends vs. fehlt nur in meiner Sammlung).

**Gibt es aehnlich?** RomVault zeigt "Missing"-Count in der Tree-Ansicht. clrmamepro kann FixDATs generieren. Aber: kein Dashboard, kein MIA-Filter, keine Wunschlisten-Export-Funktion.

**Neu/besser:** Dashboard-Ansicht mit MIA-Awareness + Export waere neu.

**Aufwand:** Mittel  
**Risiko:** Niedrig  
**Prioritaet:** P1  
**Empfehlung:** Jetzt umsetzen — hoher Sammlermehrwert, nutzt vorhandene DAT-Audit-Infrastruktur  
**Tags:** `Community Win`, `Differenzierungsmerkmal`

---

### FEAT-006: Metadata Enrichment (IGDB / ScreenScraper / MobyGames)

**Kurzbeschreibung:** Nach DAT-Match optional Spiel-Metadaten abrufen: Genre, Publisher, Erscheinungsjahr, Rating, Beschreibung, Cover-Art-URL. Gespeichert in einer lokalen Metadaten-DB. Vorschau im GUI: bei Klick auf ein ROM das Cover und Details sehen.

**Problem:** Romulus weiss alles ueber Datei-Integritaet, aber nichts ueber das Spiel. Die Sammlung ist "leblos" — nur Dateinamen und Hashes.

**Zielgruppe:** Einsteiger (wollen wissen was sie haben), Frontend-User (brauchen Metadata fuer Export)

**Warum Mehrwert:** Macht die Sammlung visuell erlebbar. Verbessert die Frontend-Export-Qualitaet drastisch.

**Gibt es aehnlich?** RomM (Web) macht genau das — IGDB, ScreenScraper, MobyGames, SteamGridDB. Aber: kein Desktop-ROM-Manager integriert das.

**Neu/besser:** Waere das erste Desktop-Tool das DAT-Verifizierung + Metadata-Enrichment kombiniert.

**Aufwand:** Gross (API-Keys, Rate-Limiting, lokale DB, Artwork-Caching)  
**Risiko:** Mittel (API-Abhaengigkeiten, Scraping-Policies)  
**Prioritaet:** P2  
**Empfehlung:** Spaeter — als grosses Post-Release Feature  
**Tags:** `Community Win`, `Spaeteres Epic`

---

### FEAT-007: Multi-Disc Playlist Generator (.m3u / .cue Smart-Linker)

**Kurzbeschreibung:** Automatische Erkennung von Multi-Disc-Games (Disc 1, Disc 2, etc.) und Generierung von .m3u Playlists. Geschuetzte Benennung. Integration in Run-Flow. Frontend-kompatibel.

**Problem:** Multi-Disc-Games (PS1, Saturn, PCECD) brauchen .m3u-Dateien damit Emulatoren Disc-Wechsel unterstuetzen. Heute muss man das manuell erstellen.

**Zielgruppe:** Emulations-Nutzer, Frontend-User

**Warum Mehrwert:** Spart manuelle Arbeit bei jedem PS1/Saturn/Dreamcast-Setup.

**Gibt es aehnlich?** Igir hat `playlist`-Command. Kein anderer Desktop-Manager.

**Neu/besser:** Integration in Sort + Convert Pipeline waere besser als Igirs separater Schritt.

**Aufwand:** Klein  
**Risiko:** Niedrig  
**Prioritaet:** P1  
**Empfehlung:** Jetzt umsetzen — Quick Win, klein, hoher Nutzen  
**Tags:** `Community Win`, `Quick Win`

---

### FEAT-008: DAT Changelog / Diff Engine

**Kurzbeschreibung:** Bei DAT-Updates zeigen: "Was hat sich geaendert? 5 neue Dumps, 2 Renames, 1 Removed." Diff zwischen alter und neuer DAT. Proaktive Benachrichtigung: "Deine NES-DAT ist 45 Tage alt, neue Version verfuegbar."

**Problem:** DATs aendern sich staendig (No-Intro taeglich, Redump woechemtlich). Nutzer wissen nie, was sich geaendert hat, und ob ein Re-Scan lohnt.

**Zielgruppe:** Power-User, Archivare

**Warum Mehrwert:** Spart unnoetige Re-Scans. Gibt Transparenz ueber DAT-Evolution.

**Gibt es aehnlich?** Nein. DatVault (RomVault's Abo-Service) updated DATs, zeigt aber keinen Diff. SabreTools kann DATs diffen, aber als separates CLI-Tool, nicht integriert.

**Neu/besser:** Integriert in GUI als "Was hat sich geaendert?"-Panel waere echte Innovation.

**Aufwand:** Mittel  
**Risiko:** Niedrig  
**Prioritaet:** P2  
**Empfehlung:** Spaeter — sehr nuetzlich, aber nicht release-kritisch  
**Tags:** `Power-User Win`, `Differenzierungsmerkmal`

---

### FEAT-009: Smart First-Run Wizard ("Scan & Recommend")

**Kurzbeschreibung:** Beim ersten Start: "Zeig mir deine ROM-Ordner." Romulus scannt, erkennt Konsolen, zeigt: "Du hast 23.000 Dateien, davon ~18.000 ROMs fuer 12 Systeme. Empfehlung: Region US>EU>JP, CHD-Konvertierung fuer PS1/Saturn (spart ~40GB), 2.100 Junk-Dateien." One-Click-Start fuer den empfohlenen DryRun.

**Problem:** Einsteiger sind ueberfordert. Die Setup-Optionen sind maechtig, aber ohne Fuehrung.

**Zielgruppe:** Einsteiger

**Warum Mehrwert:** Reduziert die Einstiegshuerde massiv. "Das Tool hat mir in 30 Sekunden gesagt, was zu tun ist."

**Gibt es aehnlich?** Nein. Alle ROM-Manager erwarten Konfiguration vor dem ersten Scan.

**Neu/besser:** Analyse-first statt Config-first waere ein Paradigmenwechsel.

**Aufwand:** Mittel  
**Risiko:** Niedrig  
**Prioritaet:** P1  
**Empfehlung:** Jetzt umsetzen — differenzierend, hoher Einsteiger-Nutzen  
**Tags:** `Community Win`, `Differenzierungsmerkmal`

---

### FEAT-010: Space Savings Estimator & Conversion Advisor

**Kurzbeschreibung:** Vor einem Run: "Wenn du PS1 zu CHD konvertierst, sparst du ~38GB. GameCube zu RVZ spart ~120GB. Hier die Aufschluesselung pro Konsole." Als interaktives Dashboard mit Tortendiagramm.

**Problem:** Nutzer wissen nicht, wie viel Platz sie durch Conversion sparen koennten. Die Entscheidung ob konvertiert werden soll ist uninformiert.

**Zielgruppe:** Alle — besonders Nutzer mit begrenztem Speicher

**Warum Mehrwert:** Macht den Wert der Konvertierung greifbar. Motiviert zum Handeln.

**Gibt es aehnlich?** Nein. Kein Tool zeigt eine vorausschauende Platzersparnis pro System.

**Neu/besser:** Voellig neu. Datengetriebene Entscheidungshilfe statt "probier halt mal".

**Aufwand:** Klein (ConversionPolicy + durchschnittliche Kompressionsraten pro Format genuegen)  
**Risiko:** Niedrig  
**Prioritaet:** P1  
**Empfehlung:** Jetzt umsetzen — Quick Win, differenzierend  
**Tags:** `Community Win`, `Quick Win`

---

### FEAT-011: Collection Snapshot & Time Travel

**Kurzbeschreibung:** Vor jedem Run einen Snapshot des Sammlungsstatus erstellen (Dateiliste, Hashes, Struktur). Spaeter vergleichen: "Was hat sich zwischen Snapshot A und B geaendert? Welche Dateien kamen dazu, welche wurden entfernt?" Unabhaengig vom Audit-Trail — auch fuer manuelle Aenderungen.

**Problem:** Audit deckt nur Romulus-Runs ab. Manuelle Aenderungen (User loescht/verschiebt Dateien) sind unsichtbar.

**Zielgruppe:** Archivare, Power-User

**Warum Mehrwert:** Volle Nachvollziehbarkeit ueber die Zeit. "Wann ist diese Datei verschwunden?"

**Gibt es aehnlich?** Nein. Kein ROM-Tool hat Collection Snapshots.

**Neu/besser:** Time-Travel fuer Datei-Sammlungen waere ein Unikum.

**Aufwand:** Mittel  
**Risiko:** Niedrig  
**Prioritaet:** P3  
**Empfehlung:** Spaeter — als Epic nach Release  
**Tags:** `Differenzierungsmerkmal`, `Spaeteres Epic`

---

### FEAT-012: Arcade Set Manager (Merge/Split/Non-Merge Transformation)

**Kurzbeschreibung:** MAME/FBNeo-Sets zwischen merged, split und non-merged Modus transformieren. Parent/Clone-Handling. Device-ROM-Separation. Kompatibel mit verschiedenen MAME-Versionen.

**Problem:** Arcade-ROMs haben das komplexeste Set-System in der ROM-Welt. Heute braucht man clrmamepro oder RomVault dafuer.

**Zielgruppe:** Arcade-Nutzer, MAME-Community

**Warum Mehrwert:** Heute ist Romulus bei Arcade/NEOGEO bewusst vorsichtig (SetProtected). Ein Arcade Set Manager wuerde dieses Segment erschliessen.

**Gibt es aehnlich?** Ja — clrmamepro, RomVault, Igir koennen das. Es ist baseline, nicht differenzierend.

**Neu/besser:** Integration in Romulus' Pipeline waere komfortabler als separate Tools.

**Aufwand:** Gross  
**Risiko:** Hoch (MAME-Set-Logik ist komplex, viele Sonderfaelle)  
**Prioritaet:** P3  
**Empfehlung:** Nur als Epic — komplex, gut von bestehenden Tools abgedeckt  
**Tags:** `Spaeteres Epic`

---

### FEAT-013: ROM Header Manager (Add/Remove/Detect No-Intro Headers)

**Kurzbeschreibung:** Copier-Header erkennen (NES, SNES, Lynx, FDS etc.), entfernen oder hinzufuegen. Wichtig fuer DAT-Matching: No-Intro bietet headered und unheadered DATs, und der Hash aendert sich.

**Problem:** Ein ROM mit Header matcht nicht gegen eine unheadered DAT und umgekehrt. Heute muss man separate Tools nutzen (Headerer von SabreTools, NSRT fuer SNES).

**Zielgruppe:** No-Intro-Community, Power-User

**Warum Mehrwert:** Eliminiert falsche "MISS"-Ergebnisse im DAT-Audit durch Header-Mismatch.

**Gibt es aehnlich?** SabreTools Headerer, Igir `--remove-headers`. Kein anderes Desktop-Tool.

**Neu/besser:** Integration in DAT-Audit ("Header erkannt, Hash ohne Header matcht DAT") waere besser als separate Tools.

**Aufwand:** Mittel  
**Risiko:** Niedrig (Header-Formate sind gut dokumentiert)  
**Prioritaet:** P2  
**Empfehlung:** Spaeter — nuetzlich, mittlerer Aufwand  
**Tags:** `Power-User Win`

---

### FEAT-014: Rule-Based Smart Filter / Custom Junk Rules

**Kurzbeschreibung:** User-definierbare Filter-Regeln: "Behalte nur Retail-Releases fuer NES", "Alle Prototypen fuer PS1 sind Junk", "Homebrew fuer GBA ist kein Junk". Als GUI-Editor oder JSON-Regeldatei.

**Problem:** Die Junk-Erkennung ist hardcoded. Power-User wollen eigene Kriterien definieren, Einsteiger wollen "Nur lizenzierte Spiele behalten" als Preset.

**Zielgruppe:** Power-User, Einsteiger (ueber Presets)

**Warum Mehrwert:** Romulus' Junk-Engine wird flexibel statt starr. Verschiedene Sammlungsphilosophien werden unterstuetzt.

**Gibt es aehnlich?** Retool hat umfangreiche Filter (--no-demo, --no-beta, --no-prototype etc.). Igir ebenso. Aber: als rules.json mit GUI-Editor waere das komfortabler.

**Neu/besser:** GUI-basierter Regel-Editor mit Vorschau waere neu.

**Aufwand:** Mittel  
**Risiko:** Niedrig  
**Prioritaet:** P2  
**Empfehlung:** Spaeter — wichtig, aber nicht release-kritisch  
**Tags:** `Power-User Win`

---

### FEAT-015: Duplicate Visualization / Group Inspector

**Kurzbeschreibung:** Grafische Darstellung jeder Dedup-Gruppe: "Super Mario World hat 8 Varianten. USA v1.1 (CHD, 98/100 Score) ist Winner. Hier sind die 7 Losers mit ihren Scores und warum sie verloren haben." Side-by-side Vergleich.

**Problem:** Die Dedup-Entscheidung ist heute eine Black Box. Der User sieht "Winner: X" aber nicht warum, und nicht die Alternativen.

**Zielgruppe:** Power-User die Dedup-Entscheidungen verstehen und ggf. uebersteuern wollen

**Warum Mehrwert:** Transparenz und Vertrauen. Der User kann Fehlentscheidungen erkennen.

**Gibt es aehnlich?** Nein auf diesem Detailgrad. RomVault zeigt Status-Icons. clrmamepro zeigt Scan/Fix.

**Neu/besser:** Visueller Gruppen-Inspektor mit Score-Breakdown waere voellig neu.

**Aufwand:** Mittel  
**Risiko:** Niedrig  
**Prioritaet:** P2  
**Empfehlung:** Spaeter — UX-Enhancement nach Kern-Release  
**Tags:** `Power-User Win`, `Differenzierungsmerkmal`

---

### FEAT-016: Watch Mode / Incremental Processing

**Kurzbeschreibung:** Ein Daemon oder Background-Service der neue Dateien in den Root-Ordnern erkennt und automatisch klassifiziert, deduped und einsortiert. Oder zumindest: ein inkrementeller Scan, der nur geaenderte Dateien verarbeitet statt die ganze Sammlung.

**Problem:** Bei 100k+ ROMs dauert ein Full-Scan Minuten. Wenn nur 10 Dateien hinzukommen, ist das verschwenderisch.

**Zielgruppe:** Power-User mit grossen, wachsenden Sammlungen

**Warum Mehrwert:** Dramatisch schnellere Verarbeitung bei kleinen Aenderungen.

**Gibt es aehnlich?** Nein als Daemon. Igir und RomVault scannen immer komplett.

**Neu/besser:** Inkrementeller Modus waere einzigartig.

**Aufwand:** Gross (Cache-Invalidation, FileSystemWatcher-Limits, Correctness bei partiellem State)  
**Risiko:** Hoch (Determinismus bei partiellem State)  
**Prioritaet:** P3  
**Empfehlung:** Spaeter — als Epic nach Release  
**Tags:** `Differenzierungsmerkmal`, `Spaeteres Epic`

---

### FEAT-017: CLI Preset Profiles ("romulus quick", "romulus full", "romulus archive")

**Kurzbeschreibung:** Benannte CLI-Profile fuer haeufige Workflows: `romulus quick` (DryRun, nur Report), `romulus full` (Move + Convert + Sort + DAT-Audit), `romulus archive` (volle Verifikation, kein Move, nur Check + Report). User kann eigene Profile definieren.

**Problem:** CLI-Befehle sind lang und fehleranfaellig. Repeat-User tippen immer dasselbe.

**Zielgruppe:** CLI-User, CI/CD-Pipelines

**Warum Mehrwert:** Reduziert CLI-Komplexitaet. Ermoeglicht reproduzierbare Workflows.

**Gibt es aehnlich?** Nein als benannte Presets. Romulus hat Settings-Profiles in der GUI, aber nicht als CLI-Shortcut.

**Neu/besser:** `romulus full --roots C:\ROMs` statt 15 Flags.

**Aufwand:** Klein  
**Risiko:** Niedrig  
**Prioritaet:** P2  
**Empfehlung:** Jetzt umsetzen — Quick Win  
**Tags:** `Power-User Win`, `Quick Win`

---

### FEAT-018: Fixdat Generator

**Kurzbeschreibung:** Aus dem DAT-Audit automatisch eine Fixdat generieren: "Diese ROMs fehlen in deiner Sammlung." Im CLRMamePro/Logiqx XML Format, direkt nutzbar in anderen Tools oder als Download-Checkliste.

**Problem:** Der DAT-Audit zeigt MISS-Eintraege, aber es gibt keinen Export als Standard-Fixdat.

**Zielgruppe:** Power-User, Archivare, Cross-Tool-Nutzer

**Warum Mehrwert:** Standard in der Community. clrmamepro, RomVault, Igir und SabreTools koennen alle Fixdats generieren. Romulus fehlt das.

**Gibt es aehnlich?** Ja — Baseline-Feature aller Konkurrenten.

**Neu/besser:** Nicht differenzierend, aber erwartet.

**Aufwand:** Klein  
**Risiko:** Niedrig  
**Prioritaet:** P1  
**Empfehlung:** Jetzt umsetzen — erwartet, klein, Standardfeature  
**Tags:** `Community Win`, `Quick Win`

---

### FEAT-019: Local Name Support / Regionale Spielnamen

**Kurzbeschreibung:** Japanische Spiele mit ihrem Original-Titel anzeigen (z.B. "シャイニング·フォースⅡ" statt "Shining Force II"). Daten aus DATs oder Metadata-APIs.

**Problem:** Nicht-englische Sammlungen zeigen nur den International/US-Titel.

**Zielgruppe:** Nicht-englischsprachige Sammler, Archivare

**Warum Mehrwert:** Retool bietet lokale Titel als Feature. In Romulus fehlt das.

**Gibt es aehnlich?** Retool (jetzt deprecated) hatte das. Kein aktives Desktop-Tool bietet es.

**Neu/besser:** Romulus koennte die Luecke von Retool (deprecated) fuellen.

**Aufwand:** Mittel  
**Risiko:** Niedrig  
**Prioritaet:** P3  
**Empfehlung:** Spaeter  
**Tags:** `Community Win`

---

### FEAT-020: Export/Share Configuration ("Infrastructure as Code" fuer ROM-Sammlungen)

**Kurzbeschreibung:** Eine vollstaendige Romulus-Konfiguration (Regions, Filter, ConversionPolicy, Roots, Profile) als portierbare JSON/YAML-Datei exportieren und importieren. Community kann Konfigurationen teilen: "Hier ist mein Setup fuer eine optimale, platzsparende PS1+Saturn+Dreamcast-Sammlung."

**Problem:** Konfiguration ist heute pro Installation. Power-User koennen ihr Setup nicht teilen oder ueber mehrere Rechner synchronisieren.

**Zielgruppe:** Community, Power-User

**Warum Mehrwert:** Shareability. "Hier, nimm mein Config, deine Sammlung ist in 10 Minuten perfekt." Senkt Einstiegshuerde fuer Einsteiger.

**Gibt es aehnlich?** Nein als Sharing-Feature. CI/CD-Pipelines nutzen Config-Dateien, aber nicht als Community-Feature.

**Neu/besser:** Community-Sharing von Konfigurationen waere neu.

**Aufwand:** Klein  
**Risiko:** Niedrig  
**Prioritaet:** P2  
**Empfehlung:** Spaeter — kleiner Aufwand, aber nach Release  
**Tags:** `Community Win`, `Quick Win`

---

### FEAT-021: Parallel/Multithreaded Conversion

**Kurzbeschreibung:** Konvertierungen (chdman, dolphintool, 7z) parallel ausfuehren statt sequentiell. Konfigurierbare Thread-Anzahl.

**Problem:** Bei 500 PS1-ISOs zu CHD dauert die sequentielle Konvertierung Stunden ohne Grund — die CPU ist idle.

**Zielgruppe:** Power-User mit grossen Sammlungen

**Warum Mehrwert:** 3-4x Speedup bei Conversion-Runs.

**Gibt es aehnlich?** RomVault nutzt paralleles Scanning. Igir hat `--writer-threads`. Romulus ist aktuell sequentiell.

**Neu/besser:** Standard, nicht differenzierend, aber erwartet.

**Aufwand:** Mittel  
**Risiko:** Mittel (Thread-Safety bei Conversion-Verify-Audit-Kette)  
**Prioritaet:** P2  
**Empfehlung:** Jetzt umsetzen — Performance-Erwartung  
**Tags:** `Power-User Win`

---

### FEAT-022: Portable Mode / USB-Stick Deployment

**Kurzbeschreibung:** Romulus als Single-Executable auf USB-Stick mitnehmbar. Settings relativ zum Executable statt %APPDATA%. Ideal fuer: "Ich nehme mein ROM-Tool zum Freund mit und bereinige seine Sammlung."

**Problem:** Installation + %APPDATA%-Konfiguration machen das Tool nicht portierfaehig.

**Zielgruppe:** Community, LAN-Parties, mobile Power-User

**Warum Mehrwert:** Bequemlichkeit, Community-Sharing, kein Install noetig.

**Gibt es aehnlich?** clrmamepro hat portable Mode. RomVault nicht. Igir laeuft via npx.

**Neu/besser:** Nicht differenzierend, aber praktisch.

**Aufwand:** Klein  
**Risiko:** Niedrig  
**Prioritaet:** P3  
**Empfehlung:** Spaeter  
**Tags:** `Community Win`

---

### FEAT-023: RetroAchievements Integration / Hash Checker

**Kurzbeschreibung:** ROMs gegen die RetroAchievements-Hash-Datenbank pruefen: "Dieses ROM ist RetroAchievements-kompatibel" / "Dieses ROM wird nicht erkannt — falscher Hash." RA-kompatible ROMs bevorzugt behalten bei Dedup.

**Problem:** RetroAchievements braucht exakte ROM-Hashes. Nutzer wissen nicht ob ihre bereinigten ROMs kompatibel sind.

**Zielgruppe:** RetroAchievements-Community (>1M registrierte User)

**Warum Mehrwert:** Riesige Community. "Romulus stellt sicher dass meine ROMs Achievement-faehig sind."

**Gibt es aehnlich?** RomM zeigt RA-Achievements. RAHasher validiert Hashes. Kein ROM-Manager integriert RA-Kompatibilitaet in den Dedup-Score.

**Neu/besser:** RA-Hash als Score-Faktor bei Winner-Selection waere einzigartig.

**Aufwand:** Mittel (RA API ist dokumentiert)  
**Risiko:** Niedrig  
**Prioritaet:** P2  
**Empfehlung:** Spaeter — starker Community-Hook  
**Tags:** `Community Win`, `Differenzierungsmerkmal`

---

### FEAT-024: ZSTD Zip / Structured Archive Support

**Kurzbeschreibung:** Zstd-komprimierte ZIPs als Archivformat unterstuetzen (lesen und schreiben). TorrentZip und RV-ZSTD-Format-Kompatibilitaet. Deutlich bessere Kompression bei hoeherer Geschwindigkeit als deflate.

**Problem:** Die ROM-Community bewegt sich Richtung ZSTD (RomVault 3.7, MAME, 7-Zip 24+). Romulus unterstuetzt nur deflate-ZIPs.

**Zielgruppe:** Power-User, Cross-Tool-Nutzer

**Warum Mehrwert:** Zukunftssicherheit. Bessere Kompression. Kompatibilitaet mit RomVault-Output.

**Gibt es aehnlich?** Ja — RomVault 3.7 hat ZSTD Zip/7z. clrmame 0.6 hat ZSTD. Igir hat TorrentZip.

**Neu/besser:** Nicht differenzierend, aber wird zur Baseline.

**Aufwand:** Mittel (Zstd NuGet existiert, aber TorrentZip-Format-Spec muss implementiert werden)  
**Risiko:** Niedrig  
**Prioritaet:** P2  
**Empfehlung:** Spaeter — wichtig fuer Cross-Tool-Kompatibilitaet  
**Tags:** `Power-User Win`

---

### FEAT-025: Dir2DAT Generator

**Kurzbeschreibung:** Aus einem Dateiverzeichnis eine DAT-Datei generieren (Logiqx XML oder CLRMamePro-Format). Nuetzlich fuer: Custom-DAT-Erstellung, Sammlungs-Snapshot, Sharing.

**Problem:** Nutzer die eigene DATs erstellen wollen, brauchen clrmamepro oder SabreTools.

**Zielgruppe:** Power-User, DAT-Ersteller

**Warum Mehrwert:** Standard-Feature der Konkurrenz. Rundet das DAT-Ecosystem ab.

**Gibt es aehnlich?** Ja — SabreTools, Igir, clrmamepro, RomVault koennen alle Dir2DAT.

**Neu/besser:** Nicht differenzierend, erwartet.

**Aufwand:** Mittel  
**Risiko:** Niedrig  
**Prioritaet:** P3  
**Empfehlung:** Spaeter  
**Tags:** `Power-User Win`

---

## 4. Ideen mit moeglichem Alleinstellungsmerkmal

| # | Idee | Warum differenzierend | Worauf achten |
|---|------|----------------------|---------------|
| 1 | **Collection Health Monitor** (FEAT-001) | Kein Desktop-ROM-Tool ueberwacht proaktiv. Paradigmenwechsel von "punkt-basiert" zu "kontinuierlich". | Performance bei NAS/grossen Sammlungen. FileSystemWatcher-Grenzen. |
| 2 | **Smart First-Run Wizard** (FEAT-009) | Analyse-first statt Config-first. Kein Tool empfiehlt proaktiv Aktionen. | Darf nicht patronizing wirken. Empfehlungen muessen fundiert sein. |
| 3 | **Post-Patch DAT-Verifikation** (FEAT-002) | Patching + Verify + Sort als integrierter Flow existiert nirgends. | Patches koennen zu nicht-verifizierbaren ROMs fuehren (Custom-Hacks). |
| 4 | **Duplicate Group Inspector** (FEAT-015) | Visueller Score-Breakdown pro Dedup-Gruppe ist voellig neu. | Darf bei 50.000 Gruppen nicht die UI ueberlasten. |
| 5 | **RA-Hash als Score-Faktor** (FEAT-023) | Kein Tool priorisiert RA-kompatible ROMs bei Deduplizierung. | API Rate-Limits. Nicht alle ROMs haben RA-Hashes. |
| 6 | **Collection Diff & Merge** (FEAT-004) | "Collection A vs B" existiert nicht als Feature. | Edge Cases bei gleichen Dateinamen aber verschiedenen Hashes. |
| 7 | **DAT Changelog** (FEAT-008) | Keiner zeigt "Was hat sich in der DAT geaendert?" | DAT-Formate sind heterogen; Diff muss robust sein. |

---

## 5. Was bewusst nicht gemacht werden sollte

| Idee | Warum nicht |
|------|-------------|
| **Web-UI als Alternative zum WPF-GUI** | RomM deckt den Web-Bereich ab. Romulus' Staerke ist Desktop. Zwei GUI-Stacks waeren wartungstechnisch fatal. |
| **In-Browser ROM-Emulation** | Komplett anderes Produktsegment. RomM + EmulatorJS machen das. Passt nicht zum Tool-Charakter. |
| **Cloud-Sync / Remote-Storage** | Fuehrt zu Komplexitaet, Latenz-Problemen, Sicherheitsrisiken. ROM-Sammlungen sind lokal. |
| **Social Features / Community Hub** | Scope-Sprengung. Discord/Reddit existieren. |
| **Eigener Emulator / Core-System** | Nicht Romulus' Aufgabe. Fokus auf Verwaltung, nicht Ausfuehrung. |
| **Torrent/Download-Integration** | Rechtlich problematisch. Legal nicht vertretbar. |
| **ML-basierte Duplikat-Erkennung** | Hash-basierte Erkennung ist deterministisch und perfekt. ML wuerde Determinismus brechen. |
| **Mobile App (Android/iOS)** | ROM-Management ist ein Desktop-Workflow. Mobile Apps waeren Feature-beschnitten. |
| **Plugin-System (Phase 1)** | Aktuell zu frueh. Stabile API-Oberflaeche muss erst stehen. Spaetestens v2.0. |

---

## 6. Priorisiertes Backlog

| ID | Titel | Kategorie | Problem | Zielgruppe | Nutzen | Aufwand | Risiko | Prio | Status | Naechster Schritt |
|----|-------|-----------|---------|------------|--------|---------|--------|------|--------|-------------------|
| FEAT-007 | Multi-Disc Playlist Generator | Integration | Multi-Disc-Games brauchen .m3u manuell | Emulations-User | Hoch | Klein | Niedrig | P1 | Proposed | Spec: .m3u Format + Disc-Detection-Regeln |
| FEAT-010 | Space Savings Estimator | UX | Konvertierungsentscheidung uninformiert | Alle | Hoch | Klein | Niedrig | P1 | Proposed | Spec: Kompressionsraten-DB pro Format |
| FEAT-018 | Fixdat Generator | DAT | Fehlende ROMs nicht als Standard-DAT exportierbar | Power-User | Hoch | Klein | Niedrig | P1 | Proposed | Logiqx XML Writer implementieren |
| FEAT-017 | CLI Preset Profiles | CLI | CLI-Befehle lang und fehleranfaellig | CLI-User | Mittel | Klein | Niedrig | P2 | Proposed | profiles.json Schema definieren |
| FEAT-002 | Smart Patch Pipeline | Core | Patching manuell, 4-5 separate Schritte | Alle | Sehr hoch | Mittel | Niedrig | P1 | Proposed | IPS/BPS Parser evaluieren (NuGet) |
| FEAT-003 | Frontend Sync Engine | Integration | Export-Stubs nicht verdrahtet | Frontend-User | Sehr hoch | Mittel | Niedrig | P1 | Proposed | Bestehende Stubs verdrahten (RetroArch zuerst) |
| FEAT-005 | Completionist Tracker | UX | "Wie komplett bin ich?" unbeantwortet | Sammler | Sehr hoch | Mittel | Niedrig | P1 | Proposed | DAT-Audit-Ergebnis in Dashboard projizieren |
| FEAT-009 | Smart First-Run Wizard | UX | Einsteiger ueberfordert | Einsteiger | Hoch | Mittel | Niedrig | P1 | Proposed | Quick-Scan-Engine spezifizieren |
| FEAT-021 | Parallel Conversion | Performance | Conversion sequentiell = langsam | Power-User | Hoch | Mittel | Mittel | P2 | Proposed | Thread-Safety-Analyse der Conversion-Pipeline |
| FEAT-013 | ROM Header Manager | Core | Header-Mismatch → falsche DAT-Matches | No-Intro-User | Mittel | Mittel | Niedrig | P2 | Proposed | Header-Formate dokumentieren (NES, SNES, Lynx) |
| FEAT-014 | Custom Junk Rules Editor | UX | Junk-Regeln hardcoded | Power-User | Mittel | Mittel | Niedrig | P2 | Proposed | rules.json Schema erweitern |
| FEAT-015 | Duplicate Group Inspector | UX | Dedup-Entscheidung = Black Box | Power-User | Hoch | Mittel | Niedrig | P2 | Proposed | GroupDetails-ViewModel entwerfen |
| FEAT-008 | DAT Changelog Engine | DAT | DAT-Aenderungen unsichtbar | Power-User | Mittel | Mittel | Niedrig | P2 | Proposed | DAT-Diff-Algorithmus spezifizieren |
| FEAT-023 | RetroAchievements Integration | Integration | RA-Kompatibilitaet unbekannt | RA-Community | Hoch | Mittel | Niedrig | P2 | Proposed | RA API evaluieren + Rate-Limits |
| FEAT-024 | ZSTD Zip Support | Core | Nur deflate-ZIPs unterstuetzt | Cross-Tool-User | Mittel | Mittel | Niedrig | P2 | Proposed | ZstdNet NuGet evaluieren |
| FEAT-020 | Config Export/Share | Community | Config nicht portierfaehig | Community | Mittel | Klein | Niedrig | P2 | Proposed | Export-Schema definieren |
| FEAT-004 | Collection Diff & Merge | Core | Multi-Source-Merge manuell | Sammler | Hoch | Mittel | Niedrig | P2 | Selected for R5 | `docs/epics/C8-collection-diff-merge.md` + `plan/r5-collection-diff-merge-execution.md` |
| FEAT-006 | Metadata Enrichment | Integration | Sammlung "leblos" — nur Dateien | Einsteiger | Hoch | Gross | Mittel | P2 | Proposed | IGDB API Key + Schema evaluieren |
| FEAT-001 | Collection Health Monitor | Innovation | Keine proaktive Ueberwachung | Power-User | Sehr hoch | Gross | Mittel | P2 | Proposed | FileSystemWatcher-PoC |
| FEAT-011 | Collection Snapshot / Time Travel | Innovation | Manuelle Aenderungen unsichtbar | Archivare | Mittel | Mittel | Niedrig | P3 | Proposed | Snapshot-Format spezifizieren |
| FEAT-012 | Arcade Set Manager | Core | Arcade-Sets nicht transformierbar | Arcade-User | Mittel | Gross | Hoch | P3 | Proposed | MAME Parent/Clone Spec studieren |
| FEAT-016 | Watch Mode / Incremental | Performance | Full-Scan bei kleinen Aenderungen | Power-User | Hoch | Gross | Hoch | P3 | Proposed | Cache-Invalidation-Strategie |
| FEAT-019 | Local Name Support | i18n | Nur englische Spielnamen | Internationale User | Niedrig | Mittel | Niedrig | P3 | Proposed | Retool Clonelist-Daten evaluieren |
| FEAT-022 | Portable Mode | Distribution | Nicht portabel | Community | Niedrig | Klein | Niedrig | P3 | Proposed | Relative-Path-Settings-Fallback |
| FEAT-025 | Dir2DAT Generator | DAT | Kein DAT-Export aus Dateistruktur | DAT-Ersteller | Niedrig | Mittel | Niedrig | P3 | Proposed | Logiqx XML Format implementieren |

---

## 7. Empfohlene Roadmap

### Quick Wins (klein, sofort umsetzbar, hoher Impact)

| ID | Feature | Warum jetzt |
|----|---------|-------------|
| FEAT-007 | Multi-Disc Playlist Generator | Klein, .m3u ist trivial, grosser Emulator-Nutzen |
| FEAT-010 | Space Savings Estimator | Klein, nutzt vorhandene ConversionPolicy-Daten |
| FEAT-018 | Fixdat Generator | Standard-Feature, erwartet, klein |
| FEAT-017 | CLI Preset Profiles | Klein, verbessert CLI-Erlebnis sofort |
| FEAT-020 | Config Export/Share | Klein, foerdert Community-Engagement |

### Starke mittlere Features (mittel, hoher Nutzen, differenzierend)

| ID | Feature | Warum wichtig |
|----|---------|---------------|
| FEAT-003 | Frontend Sync Engine | Schliesst die "letzte Meile", Stubs existieren |
| FEAT-005 | Completionist Tracker | Kernmehrwert fuer Sammler, nutzt DAT-Audit |
| FEAT-002 | Smart Patch Pipeline | Hoher Community-Bedarf, differenzierend |
| FEAT-009 | Smart First-Run Wizard | Senkt Einstiegshuerde massiv |
| FEAT-015 | Duplicate Group Inspector | Macht Dedup transparent und vertrauenswuerdig |
| FEAT-021 | Parallel Conversion | Performance-Erwartung bei grossen Sammlungen |

### Grosse Differenzierungsmerkmale (hohes Innovationspotential)

| ID | Feature | USP |
|----|---------|-----|
| FEAT-001 | Collection Health Monitor | Proaktive Ueberwachung — Paradigmenwechsel |
| FEAT-004 | Collection Diff & Merge | "Collection A vs B" existiert nirgends |
| FEAT-023 | RetroAchievements Integration | RA-Hash als Dedup-Faktor — einzigartig |
| FEAT-008 | DAT Changelog Engine | "Was hat sich in der DAT geaendert?" — neu |

### Spaetere Epics (gross, komplex, nach Release)

| ID | Feature | Warum spaeter |
|----|---------|---------------|
| FEAT-006 | Metadata Enrichment | Gross, API-Abhaengigkeiten, nicht release-kritisch |
| FEAT-012 | Arcade Set Manager | Komplex, gut von bestehenden Tools abgedeckt |
| FEAT-016 | Watch Mode / Incremental | Technisch riskant, Determinismus-Herausforderung |
| FEAT-011 | Collection Snapshot / Time Travel | Innovativ, aber nicht dringend |
| FEAT-019 | Local Name Support | Nice-to-have, Retool deprecated = Chance |

---

## Top-10 der besten neuen Feature-Ideen fuer Romulus

| Rang | ID | Titel | Tags |
|------|----|-------|------|
| 🥇 1 | FEAT-005 | **Completionist Tracker / Missing ROM Dashboard** | `Community Win`, `Differenzierungsmerkmal` |
| 🥈 2 | FEAT-002 | **Smart Patch Pipeline** | `Community Win`, `Differenzierungsmerkmal` |
| 🥉 3 | FEAT-003 | **Frontend Sync Engine** | `Community Win`, `Power-User Win` |
| 4 | FEAT-009 | **Smart First-Run Wizard** | `Community Win`, `Differenzierungsmerkmal` |
| 5 | FEAT-001 | **Collection Health Monitor** | `Differenzierungsmerkmal`, `Power-User Win` |
| 6 | FEAT-010 | **Space Savings Estimator** | `Community Win`, `Quick Win` |
| 7 | FEAT-023 | **RetroAchievements Integration** | `Community Win`, `Differenzierungsmerkmal` |
| 8 | FEAT-015 | **Duplicate Group Inspector** | `Power-User Win`, `Differenzierungsmerkmal` |
| 9 | FEAT-004 | **Collection Diff & Merge** | `Differenzierungsmerkmal`, `Power-User Win` |
| 10 | FEAT-008 | **DAT Changelog Engine** | `Power-User Win`, `Differenzierungsmerkmal` |

---

> **Dieses Dokument ist ein Living Backlog.** Prioritaeten, Status und Bewertungen koennen bei jeder Sprint-Planung aktualisiert werden. Features sollen nur implementiert werden, wenn die bestehende Roadmap (Phasen 5-8) abgeschlossen oder parallel bearbeitbar ist.
