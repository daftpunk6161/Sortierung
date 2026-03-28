# Coverage Gap Audit & Minimum Coverage Matrix – RomCleanup

**Datum**: 2026-03-28
**Autor**: Architecture Review (SE: Architect)
**Scope**: Benchmark Ground-Truth Dataset, Gates, Manifest
**Grundlage**: 2.073 Eintraege (exkl. 5.000 Performance-Scale), 65 definierte Systeme, 8 JSONL Sets

---

## 1. Executive Verdict

**Reicht die bisherige Abdeckung: NEIN.**

Das Schema ist strukturell stark. Die Ground-Truth-Architektur (JSONL, Tags, Schema, Gates) ist professionell aufgesetzt und besser als in 95% vergleichbarer Projekte. Aber die **tatsaechliche Abdeckung hinter dem Schema ist gefaehrlich duenn** in mehreren kritischen Bereichen.

### Hauptprobleme

| # | Problem | Schwere |
|---|---------|---------|
| 1 | **Disc-Format-Tiefe ist praktisch null** – kein einziger cue/bin, gdi, ccd/img, mds/mdf, m3u, CSO, WIA, RVZ, WBFS Testfall hat tatsaechlich format-spezifische Tags | KRITISCH |
| 2 | **Arcade ist zwar mengentechnisch stark (220 Eintraege), aber strukturell monoton** – 97 parent, 35 clone, aber nur 15 split+15 merged+15 non-merged und keine echte Split-vs-Merged Konfusionsmatrix | HOCH |
| 3 | **BIOS hat nur 60 Eintraege ueber 19 Systeme** – kein einziger BIOS-Konflikt, keine BIOS-Fehlbenennung, keine BIOS-in-falschem-Ordner Faelle | HOCH |
| 4 | **Computer/PC hat keine directory-based Games ausser DOS, WIIU, 3DS, SWITCH, VITA** – Amiga WHDLoad, C64 Disk-Directories, MSX Tape-Directories fehlen komplett | HOCH |
| 5 | **Golden-Core hat null hard/adversarial Eintraege** – 579 easy + 69 medium = 648 total, kein einziger harter Fall | HOCH |
| 6 | **Negative Controls sind zu generisch** – 80 Eintraege, davon 44 "expected-unknown" ohne Substanz, 6 homebrew, 30 "neg + fastrom + irrel" ohne erkennbare Fehlermodi-Tiefe | MITTEL |
| 7 | **NonGame hat nur 15 Eintraege** – Demo, Homebrew, Hack sind praktisch nicht getestet | MITTEL |
| 8 | **Serial-Number-basierte Erkennung hat nur 6 Eintraege** | MITTEL |
| 9 | **Container-Formate (CSO/WIA/RVZ/WBFS) haben 2-3 Eintraege je** | MITTEL |

### Kurzfazit

Das Testset ist **formal breit** (65 Systeme, 2.073 Eintraege, 20 Fallklassen definiert), aber **praktisch duenn** bei den Fehlermodi, die in Produktion die meisten Probleme verursachen. Eine falsche Sicherheit entsteht, weil die Gates gegen ein Dataset kalibriert sind, das selbst noch zu schwach ist. Die Gates werden "gruen" zeigen, obwohl mehrere kritische Erkennungspfade nicht getestet sind.

---

## 2. Wo die aktuelle Abdeckung zu schwach ist

### Prioritaet 1 – Kritische Luecken

| # | Bereich | IST | SOLL (Minimum) | Problem |
|---|---------|-----|-----------------|---------|
| 1 | **Disc-Format-Varianten** (cue/bin, gdi, ccd, mds, m3u) | 0 tatsaechliche Format-Tests | 60+ | Redump-Tag vorhanden, aber kein einziger Test prueft, ob cue/bin-Parsing, gdi-Track-Zuordnung, multi-track-Handling korrekt ist |
| 2 | **Cross-System Disc-Ambiguity** (PS1/PS2/PSP ISO, SAT/DC ISO) | 35 ps-disambiguation, 0 sat-dc, 0 pce-pcecd | 50+ | ISO-Dateien ohne Header/Folder-Kontext sind das haeufigste reale Fehlernzuweisung-Problem |
| 3 | **BIOS-Fehlermodi** | 0 wrong-name-bios, 0 wrong-folder-bios, 0 cross-system-bios | 25+ | BIOS wird nur als "sauber" getestet, nie als problematisch |
| 4 | **Golden-Core Schwierigkeitsbalance** | 0 hard, 0 adversarial | 80+ hard, 30+ adversarial | Ein Referenz-Core ohne harte Faelle ist wertlos fuer Regression |
| 5 | **Arcade Split/Merged/Non-Merged Konfusion** | je 15 pro Variante, keine Verwechslungsfaelle | 30+ Konfusionsfaelle | Die drei ROM-Set-Typen existieren isoliert, nie als Konfusionspaar |

### Prioritaet 2 – Hohe Luecken

| # | Bereich | IST | SOLL (Minimum) | Problem |
|---|---------|-----|-----------------|---------|
| 6 | **Directory-based Games ohne DOS/WIIU** | 0 Amiga-WHDLoad, 0 C64-Dir, 0 MSX-Dir | 20+ | Computer-Systeme mit Directory-Games sind ungetestet |
| 7 | **CHD-Varianten** | 10 chd-raw-sha1, 8 arcade-chd | 25+ | CHD v4 vs v5, CHD mit falscher SHA1, CHD ohne Parent fehlen |
| 8 | **NonGame/Junk-Differenzierung** | 15 NonGame, 152 Junk, kaum Ueberlappung | 40+ NonGame | Demo vs Homebrew vs Hack vs Utility vs BIOS-Tool Unterscheidung fehlt |
| 9 | **Headerless Cartridge Confusion** | 50 headerless-Tags, keine headerless-vs-headered Paare | 25+ Paare | NES iNES vs NES2.0, SNES LoROM/HiROM, Genesis Copier-Header fehlen |
| 10 | **Multi-File-Set Varianten** | 20 multi-file, keine CUE+BIN(multi), keine GDI+tracks(multi) | 30+ | Nur Tags, keine echte Struktur |

### Prioritaet 3 – Wartbarkeitsluecken

| # | Bereich | IST | SOLL | Problem |
|---|---------|-----|------|---------|
| 11 | **DAT TOSEC Tiefe** | 150 Tags (tosec-Ecosystem), meist Computer | 180+ | TOSEC hat andere Namensschemata als No-Intro/Redump |
| 12 | **Region-Varianten-Tiefe** | 68 region-variant Tags | 100+ | Wenige Faelle wo Region-Prioritaet entscheidend ist |
| 13 | **Revision-Varianten** | 27 revision-variant | 45+ | Rev0 vs Rev1 vs Rev2 Auswahl ist Kerndomain |
| 14 | **Corrupt/Truncated Tiefe** | 35 corrupt, 35 truncated | 50+ je | Verschiedene Korruptionsarten fehlen |
| 15 | **Keyword-Detection** | 8 keyword-detection | 20+ | Keyword-basierte Erkennung ist Fallback-kritisch |

---

## 3. Bewertung der aktuellen Zahl "65 Systeme" (gates.json sagt 69 target)

### Ist diese Zahl sinnvoll?

**Die nackte Systemzahl ist als Coverage-Metrik fast wertlos.**

- 65 Systeme sind in `consoles.json` definiert
- 65 Systeme erscheinen im Ground-Truth hat (exkl. NULL/UNKNOWN)
- **Aber**: 18 Systeme haben ≤12 Eintraege, 9 Systeme haben ≤9 Eintraege
- SUPERVISION, CHANNELF, NGPC haben je nur 9 Eintraege – das sind 3 saubere Referenzen + 3 DAT-Tests + 3 Randfaelle. Das reicht nicht fuer belastbare Aussagen.

### Was stattdessen gemessen werden muss

| Metrik | Warum |
|--------|-------|
| **Eintraege pro System mit difficulty >= medium** | Zeigt ob harte Faelle abgedeckt sind |
| **Fallklassen-Abdeckung pro System** | Ein System mit nur clean-reference und dat-exact ist nicht getestet |
| **Fehlermodi pro Plattformfamilie** | Split/Merged, Header/Headerless, Format-Varianten |
| **Cross-System-Paare** | PS1/PS2/PSP, GB/GBC, MD/32X, SAT/DC, PCE/PCECD |
| **Min. 3 Schwierigkeitsstufen pro Tier-1-System** | Easy+Medium+Hard minimum |
| **Negative Control Vielfalt** | Nicht "44x Unknown", sondern spezifische Fehlermodi |

### Die Zahl 69 ist nicht das Problem – der Inhalt hinter den Zahlen ist es

Man koennte 200 Systeme haben und trotzdem schlecht abgedeckt sein, wenn jedes System nur 3 saubere Referenzfaelle hat. Umgekehrt: 30 Systeme mit je 60+ Eintraegen ueber 8+ Fallklassen waeren deutlich belastbarer.

**Empfehlung**: Die 65 Systeme behalten (sie sind alle real), aber die **Mindesttiefe pro System strikt durchsetzen** und den Gate-Fokus von "Systemzahl" auf "Fallklassen × Systeme" verschieben.

---

## 4. Mindestabdeckung pro Plattformfamilie

### A) No-Intro / Cartridge-Systeme

**Warum kritisch**: Dies ist die groesste Familie mit den zuverlaessigsten Erkennungssignalen (Header, unique Extensions). Fehler hier sind besonders peinlich und deuten auf fundamentale Bugs hin.

**Systeme die Pflicht sein muessen** (20):
NES, SNES, N64, GB, GBC, GBA, MD, SMS, GG, 32X, A26, A52, A78, LYNX, NGP, NGPC, PCE, COLECO, INTV, VB

**IST**: Alle 20 vorhanden, aber GB/GBC/MD/32X-Ambiguity-Tiefe fehlt.

| Unter-Kategorie | Mindest-Eintraege | IST | Delta |
|-----------------|-------------------|-----|-------|
| Saubere Referenz (easy) | 250 | ~320 | OK |
| Header-basierte Erkennung | 80 | 118 (cartridge-header) | OK, aber nur Tags |
| Headerless-Varianten | 50 | 50 | GRENZWERTIG |
| Header-vs-Headerless Paare | 25 | 0 | **FEHLT KOMPLETT** |
| GB/GBC CGB-Modus | 18 | 10 (gb-gbc-ambiguity) | -8 |
| MD/32X Ambiguity | 14 | 8 (md-32x-ambiguity) | -6 |
| Falsch benannt in Cartridge-Kontext | 40 | ~30 (geschaetzt) | -10 |
| NES iNES vs NES2.0 vs raw | 8 | 0 | **FEHLT** |
| SNES LoROM/HiROM/ExHiROM | 6 | 0 | **FEHLT** |
| Copier-Header-Prefix ROMs | 8 | 0 | **FEHLT** |
| Region-Varianten-Gruppen (≥3 Regionen pro Spiel) | 15 | ~10 | -5 |
| **Gesamt-Minimum Cartridge** | **920** | **~770** | **-150** |

**Chaos-/Ambiguous-/Negative pro Familie**: Min. 80 (IST: ~50)

### B) Redump / Disc-Systeme

**Warum kritisch**: Disc-basierte Systeme sind die fehleranfaelligste Familie wegen Formatvielfalt (CUE/BIN, GDI, CHD, ISO, CCD/IMG, MDS/MDF), Multi-Disc, Serial-Erkennung und Cross-System-Ambiguity (PS1/PS2/PSP ISO).

**Systeme die Pflicht sein muessen** (18):
PS1, PS2, PS3, PSP, SAT, DC, GC, WII, WIIU, 3DO, CDI, CD32, JAGCD, SCD, PCECD, PCFX, FMTOWNS, NEOCD

**IST**: Alle 18 vorhanden, aber Format-Tiefe ist katastrophal.

| Unter-Kategorie | Mindest-Eintraege | IST | Delta |
|-----------------|-------------------|-----|-------|
| Saubere Referenz (easy) | 200 | ~180 | -20 |
| CUE/BIN korrekt | 15 | 0 (Tag vorhanden, aber 0 format-spezifische) | **-15** |
| CUE/BIN mit multi-track | 10 | 0 | **-10** |
| GDI + Tracks (DC) | 8 | 0 | **-8** |
| CCD/IMG | 5 | 0 | **-5** |
| MDS/MDF | 5 | 0 | **-5** |
| CHD single disc | 15 | ~10 (chd-raw-sha1 + arcade-chd) | -5 |
| CHD v4 vs v5 | 6 | 0 | **-6** |
| ISO ambiguous (welches System?) | 20 | 35 (ps-disambiguation) | OK |
| M3U Playlist | 8 | 0 | **-8** |
| Serial-Number-basierte Erkennung | 15 | 6 | **-9** |
| Multi-Disc-Sets (≥3 Disc-Varianten) | 38 | 34 | -4 |
| Container CSO/WIA/RVZ/WBFS | 12 | 7 (3+2+2) | -5 |
| Cross-System (SAT vs DC ISO) | 8 | 0 | **-8** |
| Cross-System (PCE vs PCECD) | 5 | 0 | **-5** |
| **Gesamt-Minimum Disc** | **490** | **~290** | **-200** |

**Dies ist die groesste einzelne Luecke im gesamten Testset.**

**Chaos-/Ambiguous-/Negative pro Familie**: Min. 60 (IST: ~35)

### C) Arcade

**Warum kritisch**: Arcade hat die komplexeste Set-Logik (Parent/Clone/BIOS/Device, Split/Merged/Non-Merged), und die meisten realen Sammlungen haben Arcade als groessten Einzelposten.

**Systeme die Pflicht sein muessen** (3 logisch, 1 Key):
ARCADE, NEOGEO (als AES/MVS-Sonderfall), NEOCD

**IST**: 220 Eintraege (159 ARCADE + 42 NEOGEO + 19 NEOCD) – mengentechnisch gut.

| Unter-Kategorie | Mindest-Eintraege | IST | Delta |
|-----------------|-------------------|-----|-------|
| Parent-Sets (mit DAT-Referenz) | 60 | 97 | OK |
| Clone-Sets (mit cloneOf-Referenz) | 35 | 35 | GRENZWERTIG |
| BIOS-ROMs (neogeo.zip, pgm.zip, etc.) | 12 | 7 (arcade-bios) + 10 device | OK |
| Split-Set ROM | 20 | 15 | -5 |
| Merged-Set ROM | 20 | 15 | -5 |
| Non-Merged-Set ROM | 20 | 15 | -5 |
| **Split-vs-Merged Konfusion** | **15** | **0** | **-15** |
| **Merged erkannt als Split (oder umgekehrt)** | **10** | **0** | **-10** |
| CHD-abhaengige Arcade | 10 | 8 | -2 |
| CHD-basierte Arcade (Spiel ist CHD) | 8 | 0 | **-8** |
| Neo Geo MVS vs AES Erkennung | 8 | ~5 (geschaetzt) | -3 |
| Neo Geo CD als Disc | 8 | 19 (NEOCD) | OK |
| Arcade-Junk (BIOS als Game misclassified) | 8 | 6 | -2 |
| Arcade Sub-Boards (CPS1, CPS2, CPS3, NeoGeo, Naomi, PGM) | 18 | ~7 (nur bioscps2/3, biospgm) | **-11** |
| **Gesamt-Minimum Arcade** | **240** | **220** | **-20** |

**Die Menge stimmt fast, aber die Struktur nicht.** Es fehlen echte Konfusionsfaelle.

**Chaos-/Ambiguous-/Negative pro Familie**: Min. 35 (IST: ~15)

### D) Computer / PC / Directory-based

**Warum kritisch**: Computer-Systeme haben die schwaechsten Erkennungssignale (keine starken Header, ambige Extensions, TOSEC-dominierte DATs), und sie sind die haeufigste Quelle fuer UNKNOWN-Ergebnisse.

**Systeme die Pflicht sein muessen** (11):
DOS, AMIGA, C64, ZX, MSX, ATARIST, PC98, X68K, CPC, A800, FMTOWNS

**IST**: Alle 11 vorhanden mit 237 Eintraegen.

| Unter-Kategorie | Mindest-Eintraege | IST | Delta |
|-----------------|-------------------|-----|-------|
| TOSEC-basierte Erkennung | 80 | 150 (tosec-Tag) | OK |
| Directory-based Games (nicht nur DOS/WIIU) | 30 | 30 (aber nur DOS, WIIU, 3DS, SWITCH, VITA) | **STRUKTURELL UNZUREICHEND** |
| Amiga WHDLoad-Directories | 8 | 0 | **-8** |
| Amiga ADF vs ADZ vs HDF | 6 | 0 | **-6** |
| C64 File-Typ-Vielfalt (d64, t64, crt, prg, tap) | 8 | ~3 | -5 |
| ZX Spectrum Formate (tzx, tap, z80, sna, trd) | 6 | ~2 | -4 |
| MSX Formate (rom, dsk, cas) | 5 | ~2 | -3 |
| Atari ST Formate (st, stx, msa) | 5 | ~2 | -3 |
| PC-98 Formate (fdi, hdi, d88) | 5 | ~2 | -3 |
| X68000 Formate (dim, xdf, hdf, 2hd) | 5 | ~2 | -3 |
| Keyword-only-Erkennung (kein Header, kein Unique-Ext) | 20 | 8 | **-12** |
| Folder-only-Erkennung | 15 | 8 | -7 |
| **Gesamt-Minimum Computer** | **225** | **237** | +12 (aber falsche Verteilung) |

**Die Gesamtzahl tarnt ein Verteilungsproblem.** Amiga und DOS sind gut vertreten, aber 7 von 11 Systemen haben zu wenig Format-Tiefe.

**Chaos-/Ambiguous-/Negative pro Familie**: Min. 30 (IST: ~15)

### E) Hybrid- und Sonderfaelle

**Warum kritisch**: Diese Systeme sind moderne oder spezielle Plattformen mit Container-Formaten, Directory-Strukturen oder Format-Varianten, die in keine Standardkategorie passen.

**Systeme die Pflicht sein muessen** (8):
3DS, SWITCH, PSP, VITA, WIIU, XBOX, X360, WII (Container-Aspekte)

**IST**: Alle vorhanden, aber Container-Tiefe fehlt.

| Unter-Kategorie | Mindest-Eintraege | IST | Delta |
|-----------------|-------------------|-----|-------|
| NSP/XCI (Switch) | 8 | ~8 | OK |
| CIA/3DS (3DS) | 6 | ~6 | OK |
| CSO/ISO (PSP) | 8 | 2 (container-cso) + ISOs | -4 |
| VPK (Vita) | 5 | ~3 | -2 |
| WBFS/ISO (Wii) | 8 | 2 (container-wbfs) + ISOs | -4 |
| WIA/RVZ/GCZ (GC/Wii compressed) | 10 | 3 (container-wia + container-rvz) | **-7** |
| XISO (Xbox/X360) | 6 | ~3 | -3 |
| Directory-based (WIIU, 3DS, Switch) | 15 | 18 | OK |
| WUX compressed (WIIU) | 3 | 0 | **-3** |
| **Gesamt-Minimum Hybrid** | **140** | **~100** | **-40** |

---

## 5. Mindestabdeckung pro Fallklasse

| FC | Fallklasse | Warum Pflicht | Min. Eintraege | IST | Delta | Betroffene Familien |
|----|------------|---------------|----------------|-----|-------|---------------------|
| FC-01 | Saubere Referenz | Baseline fuer jede Regression | 900 | 891 | -9 | ALLE |
| FC-02 | Falsch benannt | Haeufigster realer Fehler | 100 | 96 | -4 | ALLE |
| FC-03 | Header-Konflikt | Header sagt System A, Folder sagt B | 35 | 30 | -5 | Cartridge, Disc |
| FC-04 | Extension-Konflikt | .bin/.iso koennte 5 Systeme sein | 65 | ~61 (wrong-extension) | -4 | Disc, Computer |
| FC-05 | Folder-vs-Header | ROM in falschem Ordner | 60 | 50 | -10 | ALLE |
| FC-06 | DAT exact | Verifizierte DAT-Matches | 320 | 293 | -27 | ALLE |
| FC-07 | DAT weak/none | Kein oder schwacher Match | 70 | 65 (dat-none) | -5 | ALLE |
| FC-08 | BIOS | BIOS-Erkennung und Klassifikation | 55 | 60 | +5 (aber keine Fehlermodi) |
| FC-09 | Parent/Clone | Arcade Set-Beziehungen | 135 | 132 (97+35) | -3 | Arcade |
| FC-10 | Multi-Disc | Disc-Sets mit >1 Disc | 38 | 34 | -4 | Disc |
| FC-11 | Multi-File | CUE+BIN, GDI+tracks etc. | 25 | 20 | -5 | Disc |
| FC-12 | Archive-Inner | ZIP/7z Inhaltserkennung | 45 | 43 | -2 | ALLE |
| FC-13 | Directory-based | Folder-als-Game | 35 | 30 | -5 | Computer, Hybrid |
| FC-14 | UNKNOWN expected | Korrekt als unbekannt erkannt | 35 | 30 (expected-unknown) | -5 | ALLE |
| FC-15 | Ambiguous | Mehrere plausible Zuweisungen | 25 | 20 | -5 | Disc, Cartridge |
| FC-16 | Negative Control | Nicht-ROMs die nicht erkannt werden duerfen | 55 | 50 | -5 | ALLE |
| FC-17 | Repair/Sort-blocked | Unsichere Sortierung blockiert | 105 | 100 | -5 | ALLE |
| FC-18 | Cross-System | System-Verwechslungsgefahr | 100 | 95 | -5 | Disc, Cartridge |
| FC-19 | Junk/NonGame | Muell, Nicht-Spiele | 170 | 167 (152+15) | -3 | ALLE |
| FC-20 | Kaputte Sets | Korrupte/truncated/leere Dateien | 85 | 70 (35+35) | -15 | ALLE |

### Kritische Fallklassen-Luecken (nicht in Zahlen sichtbar)

Die Zahlen oben taeuschen bei mehreren Klassen, weil der **Fehlermodus** fehlt:

| FC | Problem | Was fehlt |
|----|---------|-----------|
| FC-03 | Header-Konflikte sind nur getaggt, nicht strukturell modelliert | Keine Paare "gleiche Datei, Header sagt NES, Extension sagt GB" |
| FC-06 | DAT-exact hat 293 Eintraege, aber keine DAT-Versionsabweichung | Alte DAT vs neue DAT ergibt unterschiedliche Ergebnisse |
| FC-08 | BIOS hat 60 Eintraege, aber alle sauber – Fehlermodi = 0 | Kein "BIOS in Game-Ordner", "BIOS falsch benannt", "BIOS falsch zugeordnet" |
| FC-11 | Multi-File hat 20 Tags, aber keine echte CUE/BIN/GDI Struktur | Tags allein testen nicht das Parsing |
| FC-16 | Negative Controls sind 44x generisches "Unknown" | Keine realen Nicht-ROM-Typen (PDF, JPG, TXT, NFO, SFV, CRC) |
| FC-18 | Cross-System hat 95 Tags, aber keine echten Konfusionspaare | Nicht "gleiche Datei, zwei plausible Systeme" |

---

## 6. BIOS-, Arcade- und Redump-Spezialmatrix

### 6.1 BIOS-Pflichtfaelle

| # | Falltyp | IST | SOLL | Prioritaet |
|---|---------|-----|------|------------|
| 1 | Saubere BIOS pro disc-basiertem System | 25 | 25 | OK |
| 2 | BIOS mit Region-Varianten | 15 | 15 | OK |
| 3 | Arcade-BIOS (neogeo.zip, pgm.zip etc.) | 7 | 10 | -3 |
| 4 | Arcade-Device ROMs | 10 | 10 | OK |
| 5 | **BIOS in falschem Ordner** | **0** | **5** | **FEHLT** |
| 6 | **BIOS falsch benannt** | **0** | **5** | **FEHLT** |
| 7 | **BIOS als Game klassifiziert (false positive)** | **0** | **5** | **FEHLT** |
| 8 | **Game als BIOS klassifiziert (false positive Gegenrichtung)** | **0** | **3** | **FEHLT** |
| 9 | **BIOS-negative (kein BIOS, obwohl Name es suggeriert)** | **2** | **5** | **-3** |
| 10 | **Shared BIOS (dient mehreren Systemen)** | **0** | **5** | **FEHLT** |
| 11 | **BIOS DAT-Match vs kein DAT-Match** | **0** | **4** | **FEHLT** |
| | **GESAMT** | **60** | **92** | **-32** |

**Warum uebergewichten**: BIOS-Fehlklassifikation ist ein Safety-Problem. Ein BIOS das als Game sortiert wird, wird ggf. dedupliziert und dann fehlt es. Ein Game das als BIOS blockiert wird, wird nie sortiert.

### 6.2 Arcade-Pflichtfaelle

| # | Falltyp | IST | SOLL | Prioritaet |
|---|---------|-----|------|------------|
| 1 | Parent-Sets mit DAT-Referenz | 97 | 80 | OK |
| 2 | Clone-Sets mit cloneOf | 35 | 35 | OK |
| 3 | BIOS mit biosSystemKeys | 7 | 10 | -3 |
| 4 | Device ROMs | 10 | 10 | OK |
| 5 | Split-Set (nur eigene ROMs) | 15 | 20 | -5 |
| 6 | Merged-Set (Clones eingebettet) | 15 | 20 | -5 |
| 7 | Non-Merged-Set (alles einzeln) | 15 | 20 | -5 |
| 8 | **Split als Merged misclassified** | **0** | **8** | **FEHLT** |
| 9 | **Merged als Non-Merged misclassified** | **0** | **8** | **FEHLT** |
| 10 | **CHD-basierte Arcade-Spiele** | **0** | **8** | **FEHLT** |
| 11 | CHD-Supplement zu ZIP | 8 | 10 | -2 |
| 12 | Sub-Board-spezifische BIOS (CPS2, PGM, SKNS, ST-V) | 7 | 12 | -5 |
| 13 | Naomi/Atomiswave Disc-Arcade | 0 | 5 | **FEHLT** |
| 14 | Arcade-Junk (Sample-ROMs, BIOS als Game) | 6 | 10 | -4 |
| 15 | Neo Geo MVS-spezifisch (vs AES) | ~3 | 8 | -5 |
| | **GESAMT** | **220** | **264** | **-44** |

**Warum uebergewichten**: Arcade ist die einzige Familie mit Set-Logik. Fehler bei Split/Merged/Non-Merged betreffen tausende Spiele gleichzeitig.

### 6.3 Redump-Pflichtfaelle

| # | Falltyp | IST | SOLL | Prioritaet |
|---|---------|-----|------|------------|
| 1 | Standard ISO (sauber) | ~100 | 80 | OK |
| 2 | CUE/BIN (single-track) | 0 | 10 | **FEHLT** |
| 3 | CUE/BIN (multi-track) | 0 | 10 | **FEHLT** |
| 4 | GDI + Tracks (DC) | 0 | 8 | **FEHLT** |
| 5 | CCD/IMG | 0 | 5 | **FEHLT** |
| 6 | MDS/MDF | 0 | 5 | **FEHLT** |
| 7 | CHD (von BIN/CUE konvertiert) | ~10 | 15 | -5 |
| 8 | CHD v4 vs v5 | 0 | 4 | **FEHLT** |
| 9 | Serial-Number Disc-Header | 6 | 15 | **-9** |
| 10 | M3U Playlist (Multi-Disc pointer) | 0 | 8 | **FEHLT** |
| 11 | PS1/PS2/PSP ISO Disambiguation | 35 | 38 | -3 |
| 12 | SAT/DC ISO Disambiguation | 0 | 8 | **FEHLT** |
| 13 | PCE-CD vs PC-FX Disambiguation | 0 | 5 | **FEHLT** |
| 14 | Multi-Disc (verschiedene Disc-Zahlen) | 34 | 38 | -4 |
| 15 | Redump DAT-Versionsabweichung | 0 | 5 | **FEHLT** |
| 16 | Container CSO | 2 | 5 | -3 |
| 17 | Container WIA | ~1 | 5 | -4 |
| 18 | Container RVZ | ~1 | 5 | -4 |
| 19 | Container WBFS | 2 | 5 | -3 |
| | **GESAMT** | **~195** | **274** | **-79** |

**Warum uebergewichten**: Disc-Formate sind die Hauptquelle fuer False-Positive-Zuweisungen im Realbetrieb. Ein ISO ohne Header/Serial ist das Worst-Case-Szenario – und genau diese Faelle sind unterrepresentiert.

---

## 7. Empfohlene Zielgroesse des Testsets

### S (kleine belastbare Version) — Sofort umsetzbar

| Metrik | Wert |
|--------|------|
| Eintraege (exkl. performance-scale) | **2.400** (+327 vs IST) |
| Systeme | 65 |
| Fallklassen mit ≥hardFail | 20/20 |
| Hard/Adversarial Anteil | ≥ 20% |
| Disc-Format-Varianten | ≥ 60 (neu) |
| BIOS-Fehlermodi | ≥ 25 (neu) |
| Arcade-Konfusion | ≥ 20 (neu) |
| Computer-Directory-Varianten | ≥ 20 (erweitert) |

**Aufwand**: ~330 neue Eintraege erstellen, hauptsaechlich in edge-cases und golden-realworld ergaenzen.

### M (mittlere realistische Version) — Ziel Q2/2026

| Metrik | Wert |
|--------|------|
| Eintraege (exkl. performance-scale) | **3.200** (+1.127 vs IST) |
| Systeme | 65 |
| Fallklassen mit ≥target | 20/20 |
| Hard/Adversarial Anteil | ≥ 25% |
| Min. Fallklassen pro Tier-1-System | 12/20 |
| Min. Fallklassen pro Tier-2-System | 8/20 |
| Konfusionspaare (cross-system) | ≥ 50 |
| Format-Varianten (disc) | ≥ 120 |
| BIOS komplett (mit Fehlermodi) | ≥ 90 |
| Arcade komplett (mit Konfusion) | ≥ 270 |

### L (grosse langfristige Version) — Ziel Q4/2026

| Metrik | Wert |
|--------|------|
| Eintraege (exkl. performance-scale) | **5.000** |
| Systeme | 65+ (neue Systeme bei Bedarf) |
| Fallklassen mit ≥target×1.5 | 20/20 |
| Hard/Adversarial Anteil | ≥ 30% |
| Holdout-Set | 500 (statt 200) |
| Volle Konfusionsmatrix alle Cross-System-Paare | Ja |
| Nightly Regression gegen alle Gates | Ja |

---

## 8. Die 20 wichtigsten Coverage-Luecken

| Rang | Luecke | IST | SOLL | Kritikalitaet | Aufwand |
|------|--------|-----|------|---------------|---------|
| 1 | **CUE/BIN Format-Tests** | 0 | 20 | KRITISCH | Mittel |
| 2 | **Arcade Split/Merged/Non-Merged Konfusion** | 0 | 25 | KRITISCH | Mittel |
| 3 | **BIOS-Fehlermodi** (falsch benannt/zugeordnet/klassifiziert) | 0 | 25 | KRITISCH | Gering |
| 4 | **GDI+Tracks (Dreamcast)** | 0 | 8 | HOCH | Mittel |
| 5 | **Golden-Core hard/adversarial Eintraege** | 0 | 110 | HOCH | Hoch |
| 6 | **Header-vs-Headerless Vergleichspaare** | 0 | 25 | HOCH | Mittel |
| 7 | **Serial-Number Disc-Erkennung** | 6 | 15 | HOCH | Mittel |
| 8 | **M3U Playlist-Parsing** | 0 | 8 | HOCH | Gering |
| 9 | **CCD/IMG + MDS/MDF** | 0 | 10 | HOCH | Gering |
| 10 | **Amiga WHDLoad Directories** | 0 | 8 | HOCH | Mittel |
| 11 | **CHD-basierte Arcade-Spiele (nicht Supplement)** | 0 | 8 | HOCH | Mittel |
| 12 | **SAT/DC ISO Cross-System** | 0 | 8 | HOCH | Gering |
| 13 | **NES iNES/NES2.0/raw Header-Varianten** | 0 | 8 | MITTEL | Mittel |
| 14 | **Negative Controls mit realen Nicht-ROM-Typen** | 0 | 15 | MITTEL | Gering |
| 15 | **Computer-Format-Tiefe** (C64, ZX, MSX, ST, PC98, X68K) | ~12 | 35 | MITTEL | Mittel |
| 16 | **WIA/RVZ/GCZ Container-Tests** | 3 | 10 | MITTEL | Gering |
| 17 | **Keyword-only Detection** | 8 | 20 | MITTEL | Gering |
| 18 | **Shared BIOS (multi-system)** | 0 | 5 | MITTEL | Gering |
| 19 | **SNES LoROM/HiROM/ExHiROM** | 0 | 6 | MITTEL | Mittel |
| 20 | **Naomi/Atomiswave Disc-Arcade** | 0 | 5 | MITTEL | Gering |

---

## 9. Empfohlene Reihenfolge zum Ausbau

### Phase 1 — Sofort (naechste 2 Wochen)
**Ziel**: Kritische Luecken schliessen, +150 Eintraege

1. **CUE/BIN Testfaelle** (+20): Single-track + Multi-track fuer PS1, SAT, DC, SCD, PCECD
2. **BIOS-Fehlermodi** (+25): falsch benannt, falsch zugeordnet, Game-als-BIOS, BIOS-als-Game
3. **Arcade-Konfusion** (+20): Split-als-Merged, Merged-als-Non-Merged Paare
4. **GDI + CCD + MDS** (+15): DC, SAT, PS1 Format-Varianten
5. **Header-vs-Headerless Paare** (+15): NES, SNES, MD, A78, LYNX
6. **Serial-Number** (+9): PS1/PS2/PSP/SAT/DC Disc-Header

**Gate-Update**: `hardFail` fuer FC-03, FC-11 erhoehen

### Phase 2 — Kurzfristig (Wochen 3-4)
**Ziel**: Tiefe ergaenzen, +100 Eintraege

7. **Golden-Core hard/adversarial** (+80): Mindestens 60 hard + 20 adversarial Eintraege in golden-core
8. **M3U Playlists** (+8): Multi-Disc pointer-Dateien
9. **SAT/DC + PCE/PCECD Cross-System** (+13)
10. **CHD v4/v5 + CHD-Arcade-Games** (+12)
11. **Negative Controls Upgrade** (+15): PDF, JPG, TXT, NFO, SFV, CRC, EXE, DLL

### Phase 3 — Mittelfristig (Monat 2)
**Ziel**: Computer-Tiefe + Container, +80 Eintraege

12. **Amiga WHDLoad** (+8)
13. **Computer Format-Varianten** (C64/ZX/MSX/ST/PC98/X68K) (+25)
14. **WIA/RVZ/GCZ/WUX Container** (+10)
15. **Keyword-only Detection erweitern** (+12)
16. **NES iNES/NES2.0 + SNES ROM-Type** (+14)
17. **Shared BIOS + Naomi/Atomiswave** (+10)

### Phase 4 — Langfristig (Quartal)
**Ziel**: Volle Konfusionsmatrix + Holdout erweitern

18. Konfusionsmatrix fuer alle 8 Cross-System-Paare
19. Holdout von 200 auf 500 erhoehen
20. DAT-Versionsabweichungs-Tests
21. Region-Varianten-Gruppen (≥3 Regionen pro Spiel) ausfuellen

---

## 10. Verbindliche Minimum Coverage Matrix

Diese Matrix ist als **Benchmark-Gate** verwendbar. Jeder Wert hat einen `hardFail` (Test faellt durch) und einen `target` (Test warnt).

### 10.1 System-Tier-Matrix

| Tier | Systeme | Min. Eintraege/System | Min. Fallklassen/System | Min. Hard+Adversarial/System |
|------|---------|----------------------|------------------------|------------------------------|
| **Tier 1** | NES, SNES, N64, GBA, GB, GBC, MD, PS1, PS2 | 50 (hardFail: 45) | 10 (hardFail: 8) | 8 (hardFail: 5) |
| **Tier 2** | 32X, PSP, SAT, DC, GC, WII, SMS, GG, PCE, LYNX, A78, A26, NDS, 3DS, SWITCH, AMIGA | 22 (hardFail: 18) | 6 (hardFail: 4) | 3 (hardFail: 2) |
| **Tier 3** | Alle anderen mit ≥10 Eintraegen | 10 (hardFail: 6) | 3 (hardFail: 2) | 1 (hardFail: 0) |
| **Tier 4** | Rest (CHANNELF, SUPERVISION, etc.) | 5 (hardFail: 3) | 2 (hardFail: 1) | 0 |

### 10.2 Plattformfamilien-Matrix

| Familie | Min. Eintraege | hardFail | Min. Systeme | Min. Hard% | Min. Fehlermodi |
|---------|---------------|----------|-------------|-----------|-----------------|
| **Cartridge** | 920 | 730 | 20 | 15% | Header/Headerless, GB/GBC, MD/32X, wrong-name, wrong-ext |
| **Disc** | 490 | 385 | 18 | 20% | CUE/BIN, GDI, CCD, Serial, CHD, ISO-ambig, Multi-disc |
| **Arcade** | 240 | 180 | 3 | 15% | Split/Merged/Non-merged, BIOS, Clone, CHD, Sub-boards |
| **Computer** | 225 | 175 | 11 | 10% | TOSEC, Directory, Keyword, Format-Vielfalt |
| **Hybrid** | 140 | 108 | 8 | 15% | Container-Varianten, Directory, Format-Vielfalt |

### 10.3 Fallklassen-Matrix (Verbindlich)

| FC-ID | Fallklasse | Target | hardFail | Min. Familien | Min. Hard% |
|-------|------------|--------|----------|---------------|-----------|
| FC-01 | Saubere Referenz | 900 | 710 | 5/5 | 0% |
| FC-02 | Falsch benannt | 100 | 76 | 4/5 | 40% |
| FC-03 | Header-Konflikt | **45** | **32** | 2/5 | 60% |
| FC-04 | Extension-Konflikt | 65 | 48 | 3/5 | 50% |
| FC-05 | Folder-vs-Header | 60 | 46 | 4/5 | 50% |
| FC-06 | DAT exact | 320 | 250 | 5/5 | 10% |
| FC-07 | DAT weak/none | 70 | 52 | 4/5 | 30% |
| FC-08 | BIOS | **75** | **55** | 4/5 | 30% |
| FC-09 | Parent/Clone | 135 | 105 | 1/5 (Arcade) | 20% |
| FC-10 | Multi-Disc | 38 | 27 | 1/5 (Disc) | 20% |
| FC-11 | Multi-File | **35** | **25** | 2/5 | 30% |
| FC-12 | Archive-Inner | 45 | 34 | 3/5 | 30% |
| FC-13 | Directory-based | **45** | **32** | 3/5 | 20% |
| FC-14 | UNKNOWN expected | 35 | 24 | 4/5 | 40% |
| FC-15 | Ambiguous | 25 | 16 | 3/5 | 60% |
| FC-16 | Negative Control | **70** | **50** | 4/5 | 50% |
| FC-17 | Repair/Sort-blocked | 105 | 80 | 5/5 | 40% |
| FC-18 | Cross-System | **120** | **90** | 3/5 | 60% |
| FC-19 | Junk/NonGame | **200** | **155** | 4/5 | 30% |
| FC-20 | Kaputte Sets | **100** | **75** | 4/5 | 50% |

**Fett** = geaendert gegenueber aktuellem gates.json

### 10.4 Spezialbereich-Matrix (Verbindlich)

| Bereich | Target | hardFail | Neue Gate-Bedingung |
|---------|--------|----------|---------------------|
| BIOS gesamt | **75** | **55** | ≥5 mit Fehlermodi-Tags |
| BIOS Systeme | 25 | 18 | |
| **BIOS Fehlermodi** | **25** | **18** | **NEU** |
| Arcade Parent | 100 | 77 | |
| Arcade Clone | 38 | 28 | |
| Arcade Split/Merged/Non-Merged | 48 | 36 | |
| **Arcade Konfusion** | **25** | **16** | **NEU** |
| Arcade BIOS | 10 | 5 | |
| Arcade CHD | **18** | **12** | erhoehen (von 10/6) |
| PS Disambiguation | 38 | 28 | |
| **SAT/DC Disambiguation** | **8** | **5** | **NEU** |
| **PCE/PCECD Disambiguation** | **5** | **3** | **NEU** |
| GB/GBC CGB | 18 | 12 | |
| MD/32X | 14 | 9 | |
| Multi-File Sets | **35** | **25** | erhoehen (von 25/16) |
| Multi-Disc | 38 | 27 | |
| CHD Raw SHA1 | 12 | 8 | |
| **CUE/BIN** | **20** | **14** | **NEU** |
| **GDI+Tracks** | **8** | **5** | **NEU** |
| **CCD/IMG + MDS/MDF** | **10** | **6** | **NEU** |
| **M3U Playlist** | **8** | **5** | **NEU** |
| **Serial-Number** | **15** | **10** | erhoehen (von implizit) |
| DAT No-Intro | 420 | 329 | |
| DAT Redump | 260 | 201 | |
| DAT MAME | 28 | 20 | |
| DAT TOSEC | 155 | 120 | |
| Directory-based | **45** | **32** | erhoehen (von 33/24) |
| Headerless | 55 | 40 | |
| **Header-vs-Headerless Paare** | **25** | **16** | **NEU** |
| **Container Varianten** | **20** | **12** | **NEU** (CSO+WIA+RVZ+WBFS+WUX) |
| **Keyword-only** | **20** | **12** | **NEU** |
| Holdout | 50 | 20 | |

### 10.5 Schwierigkeits-Matrix (Verbindlich)

| Difficulty | Aktueller Anteil | Mindest-Anteil | Target-Anteil |
|------------|-----------------|----------------|---------------|
| easy | 60.2% | ≤55% | 45-50% |
| medium | 23.3% | ≥20% | 25-30% |
| hard | 11.8% | ≥12% | 15-20% |
| adversarial | 4.7% | ≥5% | 8-10% |

**Golden-Core spezifisch**: Aktuell 89% easy / 11% medium / 0% hard / 0% adversarial → **INAKZEPTABEL**
→ Minimum: 60% easy / 20% medium / 15% hard / 5% adversarial

---

## Anhang: Zusammenfassung der gates.json Aenderungen

Folgende Aenderungen an `gates.json` sind aus dieser Analyse zwingend:

### Neue Gates (NEU)

```
"biosErrorModes":           { "target": 25, "hardFail": 18 }
"arcadeConfusion":          { "target": 25, "hardFail": 16 }
"satDcDisambiguation":      { "target": 8,  "hardFail": 5 }
"pcePcecdDisambiguation":   { "target": 5,  "hardFail": 3 }
"cueBin":                   { "target": 20, "hardFail": 14 }
"gdiTracks":                { "target": 8,  "hardFail": 5 }
"ccdMds":                   { "target": 10, "hardFail": 6 }
"m3uPlaylist":              { "target": 8,  "hardFail": 5 }
"serialNumber":             { "target": 15, "hardFail": 10 }
"headerVsHeaderlessPairs":  { "target": 25, "hardFail": 16 }
"containerVariants":        { "target": 20, "hardFail": 12 }
"keywordOnly":              { "target": 20, "hardFail": 12 }
```

### Erhoehte Gates

```
"biosTotal":       55 → 75 / 40 → 55
"arcadeChdSupplement": 10/6 → 18/12 (rename: arcadeChd)
"multiFileSets":   25/16 → 35/25
"directoryBased":  33/24 → 45/32

FC-03 (Header-Konflikt): 35/24 → 45/32
FC-08 (BIOS):            55/40 → 75/55
FC-11 (Multi-File):      25/16 → 35/25
FC-13 (Directory-based): 35/24 → 45/32
FC-16 (Negative Control):55/40 → 70/50
FC-18 (Cross-System):   100/76 → 120/90
FC-19 (Junk/NonGame):   170/133 → 200/155
FC-20 (Kaputte Sets):    85/64 → 100/75
```

### Neuer Gate-Typ: Schwierigkeitsverteilung

```
"difficultyDistribution": {
  "easyMax":          { "target": 0.50, "hardFail": 0.55 },
  "mediumMin":        { "target": 0.25, "hardFail": 0.20 },
  "hardMin":          { "target": 0.15, "hardFail": 0.12 },
  "adversarialMin":   { "target": 0.08, "hardFail": 0.05 }
}
```

### Neuer Gate-Typ: Fallklassen pro System

```
"fallklassenPerTier1System": { "target": 10, "hardFail": 8 }
"fallklassenPerTier2System": { "target": 6,  "hardFail": 4 }
```

---

**Ende des Coverage Gap Audit.**
Dieses Dokument ist als verbindliche Referenz fuer die Benchmark-Erweiterung zu verwenden.
