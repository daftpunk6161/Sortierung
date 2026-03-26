# Conversion Domain Audit – Romulus

**Stand:** 2026-03-21  
**Scope:** Vollständige Analyse der Conversion-Domäne für alle in `consoles.json` definierten Systeme + Arcade + Computer  
**Basis:** Ist-Zustand in `FormatConverterAdapter`, `FeatureService.Conversion`, `ToolRunnerAdapter`, `consoles.json`

---

## 1. Executive Verdict

### Hauptproblem
Die aktuelle Conversion-Implementierung deckt **nur 20 von 65 Systemen** ab (DefaultBestFormats). Für dutzende Systeme – darunter PS3, Wii U, Xbox, Xbox 360, Switch, 3DS, Vita, alle Computer-Plattformen und sämtliche Arcade-CHD-Fälle – fehlt eine konversionsbasierte Strategie oder ein explizites "nicht konvertierbar"-Flag. Der Code behandelt unbekannte Systeme stillschweigend mit `null` (= Skip), ohne dem Nutzer Transparenz zu geben.

### Was klar definierbar ist
- **CD-basierte Systeme → CHD** ist ein gut verstandener, verlustfreier, verifizierbarer Standard-Pfad.
- **GameCube/Wii → RVZ** ist verlustfrei, verifizierbar und mit einem einzigen Tool lösbar.
- **Cartridge → ZIP** ist trivial und reversibel.

### Wo grosse Risiken liegen
1. **chdman createcd vs. createdvd** – falsche Wahl zerstört die Konvertierung (CD-Systeme brauchen createcd, DVD-Systeme createdvd). PSP nutzt derzeit createcd, was für UMD-Images korrekt ist, aber undokumentiert.
2. **Multi-File-Sets** (cue/bin-Paare, gdi+track-Verzeichnisse, m3u-Playlisten) – die aktuelle Archiv-Extraktion findet nur die erste .cue/.gdi/.iso, bei Multi-Disc-Sets gehen Discs verloren.
3. **Lossy-Formate** (CSO, NKit, GCZ) – Konvertierung in ein anderes Lossy-Format ist doppelter Qualitätsverlust; nur der Pfad zum verlustfreien Ziel ist sicher.
4. **Systeme mit proprietären Containern** (NSP/XCI/NSZ, PKG, WBFS, WUX) – brauchen Spezialtools, die entweder rechtlich grenzwertig oder nicht portabel sind.
5. **Arcade** – ZIP-Romsets sind **keine Archivierung eines einzelnen ROMs**, sondern definierte Sets mit fester Struktur und Versionsbindung. Jede automatische Konvertierung zerstört Set-Integrität.

### Kurzfazit
Der sichere Kern (CD→CHD, GC/Wii→RVZ, Cartridge→ZIP) soll beibehalten und gehärtet werden. Für alle anderen Systeme muss Romulus entweder einen expliziten Conversion-Pfad definieren oder das System als **ConversionPolicy = None | ManualOnly** markieren. Stille Skips sind keine akzeptable Lösung.

---

## 2. Plattformfamilien

### A) Cartridge-basierte Systeme

**Conversion-Realität:** Einfach. Ein ROM = eine Datei. Kompression ist Archivierung, keine Transformation.

| Aspekt | Detail |
|---|---|
| Typische Formate | .nes, .sfc, .smc, .gba, .gb, .gbc, .nds, .n64, .z64, .v64, .md, .gen, .sms, .gg, .pce, .a26, .a52, .a78, .col, .int, .lnx, .vb, .ws, .wsc, .ngp, .sg, .sc, .32x, .3ds, .cia |
| Zielformat | .zip (Standard) oder .7z (höhere Kompression) |
| Conversion-Typ | Repackaging / Archive Normalization |
| Verlustfrei | ✅ Ja, immer |
| Risiken | Minimal. Nur: Hashed Archive vs. unhashed, DAT-Matching nach Konvertierung |
| Wichtigste Regel | Original-ROM innerhalb des Archivs NICHT umbenennen; CRC/SHA1 des ungepackten ROMs muss erhalten bleiben |

### B) Disc-basierte Systeme

**Conversion-Realität:** Komplex. Multi-File-Images (cue/bin/tracks), unterschiedliche Disc-Typen (CD vs. DVD vs. UMD vs. GD-ROM vs. BD), system-spezifische Container (RVZ, WBFS, CSO, PBP).

| Aspekt | Detail |
|---|---|
| Typische Formate | .cue/.bin, .iso, .gdi, .chd, .rvz, .wbfs, .gcz, .wia, .cso, .pbp, .cdi, .nrg, .mdf/.mds, .img, .nkit.iso, .nkit.gcz |
| Zielformate | .chd (CD/DVD/UMD), .rvz (GC/Wii) |
| Conversion-Typ | Disc Image Transformation |
| Verlustfrei | ✅ CHD (lossless LZMA/ZSTD + Hunk-Dedup), ✅ RVZ (lossless ZSTD) |
| Risiken | Falsche CD/DVD-Wahl, Multi-Track-Verlust, Sub-Channel-Verlust bei CDI→CHD, Audio-Track-Handling |
| Wichtigste Regel | chdman createcd für CD-Medien (<800 MB Sektor-Daten), createdvd für DVD-Medien (>700 MB, UMD, BD) |

### C) Arcade-Systeme

**Conversion-Realität:** **Keine automatische Konvertierung erlaubt.** Arcade-ROM-Sets sind versionierte, DAT-gebundene Sammlungen. Ein ZIP enthält nicht ein Spiel, sondern eine definierte Kombination von ROMs, PLDs, Disk-Images.

| Aspekt | Detail |
|---|---|
| Typische Formate | .zip (ROM-Set), .chd (Arcade-HDD/CD), .7z (komprimiertes Set) |
| Zielformat | Keins. Format ist MAME-/FBNeo-versionsgebunden |
| Conversion-Typ | ❌ Nicht anwendbar |
| Verlustfrei | N/A |
| Risiken | **Set-Integrität zerstört**, MAME-Version-Mismatch, Parent/Clone-Ketten brechen |
| Wichtigste Regel | Arcade-Sets dürfen NICHT automatisch konvertiert, umbenannt oder repackaged werden |

### D) Computer- / PC-Systeme

**Conversion-Realität:** Extrem heterogen. Floppy-Images, Tape-Images, HDD-Images, CD-Images – jedes System hat eigene Container-Formate.

| Aspekt | Detail |
|---|---|
| Typische Formate | .adf (Amiga), .d64/.t64 (C64), .atr/.xex/.xfd (Atari 800), .st/.stx (Atari ST), .tzx (ZX Spectrum), .dsk (CPC, MSX) |
| Zielformat | .zip (für Archivierung) – systemspezifische Formate NICHT konvertieren |
| Conversion-Typ | Archive Normalization (ZIP-Wrapping) |
| Verlustfrei | ✅ Ja, bei reinem ZIP-Wrapping |
| Risiken | Disk-Image-Formate sind emulator-spezifisch; Konvertierung zwischen ihnen (z.B. .st → .msa) kann Sektordaten verlieren |
| Wichtigste Regel | Interne Formate (.adf, .d64, .st, .tzx etc.) niemals transformieren, nur archivieren |

### E) Hybrid- / Sonderfälle

| System | Eigenart |
|---|---|
| **Nintendo Switch** | NSP (eShop-Dump), XCI (Cartridge-Dump), NSZ (komprimiertes NSP). Keine standardisierte Open-Source-Toolchain. **ConversionPolicy = None.** |
| **PlayStation 3** | ISO/Folder-basiert, PKG-Container, verschlüsselte Images. **ConversionPolicy = None.** |
| **PlayStation Vita** | VPK (App-Container), keine Standard-Conversion. **ConversionPolicy = None.** |
| **Xbox / Xbox 360** | Proprietäre ISO-Variante (XDVDFS). CHD möglich aber Emulator-Support minimal. **ConversionPolicy = ManualOnly.** |
| **Wii U** | WUX (komprimiertes WUD), RPX. Kein standardisiertes verlustfreies Zielformat. **ConversionPolicy = ManualOnly.** |
| **Nintendo 3DS** | .3ds, .cia. Konvertierung zwischen Formaten erfordert Decrypt-Schlüssel. **ConversionPolicy = None.** |
| **NKit-Formate** | .nkit.iso / .nkit.gcz sind irreversibel lossy (entfernte Padding-Daten). Nur Richtung RVZ sinnvoll, aber NKit→ISO→RVZ hat Padding-Verlust. **ConversionPolicy = ManualOnly mit Warning.** |

---

## 3. Conversion-Matrix pro System

### Legende
- **Auto**: Romulus darf automatisch konvertieren
- **Manual**: Nur auf explizite Nutzeranforderung mit Bestätigung
- **None**: Konvertierung nicht unterstützt / bewusst blockiert
- **🟢**: Verlustfrei  **🟡**: Potentially Lossy  **🔴**: Lossy oder riskant

---

### 3.1 CD-basierte Disc-Systeme

| System | Key | Eingangsformate | Zielformat | Bevorzugt | Zwischenschritte | Tool | Validierung | Auto | Lossless | Risiken |
|---|---|---|---|---|---|---|---|---|---|---|
| PlayStation | PS1 | .cue/.bin, .iso, .img, .pbp, .cso, .mdf/.mds | .chd | .chd | PBP→psxtract→cue/bin→chdman; CSO→decompress→ISO→chdman; ZIP→extract→cue/bin→chdman | chdman createcd, psxtract | `chdman verify` | ✅ Auto | 🟢 cue/bin→chd, 🟢 iso→chd, 🟡 pbp→chd (PBP ist lossy), 🟡 cso→chd | Multi-Track verlustfrei nur via cue/bin; Audio-Tracks in .iso fehlen |
| Sega Saturn | SAT | .cue/.bin, .iso, .img | .chd | .chd | ZIP→extract→cue/bin→chdman | chdman createcd | `chdman verify` | ✅ Auto | 🟢 | – |
| Sega Dreamcast | DC | .gdi+tracks, .cue/.bin, .iso, .img, .cdi | .chd | .chd | GDI→chdman createcd; CDI→Nicht empfohlen (Datenverlust möglich) | chdman createcd | `chdman verify` | ✅ Auto (excl. CDI) | 🟢 gdi→chd, 🟢 cue/bin→chd, 🟡 cdi→chd | CDI-Format hat oft abgeschnittene Tracks; GDI ist das einzige vollständige Dreamcast-Format |
| Sega CD | SCD | .cue/.bin, .iso, .img | .chd | .chd | – | chdman createcd | `chdman verify` | ✅ Auto | 🟢 | – |
| PC Engine CD | PCECD | .cue/.bin, .iso, .img | .chd | .chd | – | chdman createcd | `chdman verify` | ✅ Auto | 🟢 | – |
| Neo Geo CD | NEOCD | .cue/.bin, .iso, .img | .chd | .chd | – | chdman createcd | `chdman verify` | ✅ Auto | 🟢 | – |
| 3DO | 3DO | .cue/.bin, .iso, .img | .chd | .chd | – | chdman createcd | `chdman verify` | ✅ Auto | 🟢 | – |
| Atari Jaguar CD | JAGCD | .cue/.bin, .iso, .img | .chd | .chd | – | chdman createcd | `chdman verify` | ✅ Auto | 🟢 | – |
| PC-FX | PCFX | .cue/.bin, .iso, .img | .chd | .chd | – | chdman createcd | `chdman verify` | ✅ Auto | 🟢 | – |
| Amiga CD32 | CD32 | .cue/.bin, .iso, .img | .chd | .chd | – | chdman createcd | `chdman verify` | ✅ Auto | 🟢 | – |
| Philips CD-i | CDI | .cue/.bin, .iso, .img, .cdi | .chd | .chd | CDI→Nur wenn vollständig | chdman createcd | `chdman verify` | ✅ Auto (excl. .cdi input) | 🟢 cue/bin→chd, 🟡 cdi→chd | .cdi-Format kann abgeschnittene Daten enthalten |
| FM Towns | FMTOWNS | .cue/.bin, .iso, .img | .chd | .chd | – | chdman createcd | `chdman verify` | ✅ Auto | 🟢 | – |

### 3.2 DVD/UMD-basierte Disc-Systeme

| System | Key | Eingangsformate | Zielformat | Bevorzugt | Zwischenschritte | Tool | Validierung | Auto | Lossless | Risiken |
|---|---|---|---|---|---|---|---|---|---|---|
| PlayStation 2 | PS2 | .iso, .bin, .img, .cue/.bin (selten) | .chd | .chd | ZIP→extract→iso→chdman | chdman **createdvd** | `chdman verify` | ✅ Auto | 🟢 | CD-basierte PS2-Spiele existieren – Erkennung ob CD oder DVD nötig (Grösse <700 MB = CD) |
| PlayStation Portable | PSP | .iso, .cso, .pbp | .chd | .chd | CSO→decompress→ISO→chdman; PBP→psxtract→ISO→chdman | chdman **createcd** (UMD-Sektor = CD-kompatibel) | `chdman verify` | ✅ Auto (excl. PBP) | 🟢 iso→chd, 🟡 cso→chd (CSO ist lossy-Padding-stripped), 🟡 pbp→chd | maxcso/ciso für CSO-Decompression nötig wenn Zwischenschritt; PBP kann encrypted sein |

### 3.3 GameCube / Wii

| System | Key | Eingangsformate | Zielformat | Bevorzugt | Zwischenschritte | Tool | Validierung | Auto | Lossless | Risiken |
|---|---|---|---|---|---|---|---|---|---|---|
| GameCube | GC | .iso, .gcm, .gcz, .wia, .rvz, .nkit.iso, .nkit.gcz | .rvz | .rvz | – (DolphinTool konvertiert alle direkt) | dolphintool convert -f rvz -c zstd -l 5 -b 131072 | RVZ Magic-Byte-Check (RVZ\x01), Dateigrösse >4B | ✅ Auto (excl. NKit) | 🟢 iso→rvz, 🟢 gcz→rvz, 🟢 wia→rvz, 🔴 nkit→rvz (Padding bereits verloren) | NKit-Quellen haben irreversibel verlorenes Junk/Padding – RVZ-Output ist kleiner aber nicht bit-identisch zum Original-ISO |
| Wii | WII | .iso, .wbfs, .gcz, .wia, .rvz, .wad, .nkit.iso, .nkit.gcz | .rvz | .rvz | WBFS→DolphinTool direkt | dolphintool convert -f rvz -c zstd -l 5 -b 131072 | RVZ Magic-Byte-Check | ✅ Auto (excl. NKit, WAD) | 🟢 iso→rvz, 🟢 wbfs→rvz, 🟢 gcz→rvz, 🔴 nkit→rvz | WAD-Dateien (WiiWare/VC) sind NICHT konvertierbar → Skip |

### 3.4 Cartridge-Systeme

| System | Key | Eingangsformate | Zielformat | Bevorzugt | Tool | Validierung | Auto | Lossless | Risiken |
|---|---|---|---|---|---|---|---|---|---|
| NES | NES | .nes, .unf | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | Headered vs. Unheadered ROMs – ZIP ändert nichts am ROM selbst |
| SNES | SNES | .sfc, .smc | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | SMC hat 512-Byte-Copier-Header – nicht entfernen |
| Nintendo 64 | N64 | .z64, .v64, .n64 | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | Byte-Order-Varianten (z64=BE, v64=byteswap, n64=wordswap) – NICHT normalisieren, nur archivieren |
| Game Boy | GB | .gb | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Game Boy Color | GBC | .gbc | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Game Boy Advance | GBA | .gba | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Nintendo DS | NDS | .nds | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | Grosse NDS-ROMs (bis 512 MB) – ZIP-Kompression lohnt sich stark |
| Mega Drive / Genesis | MD | .md, .gen, .bin | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | .bin ist ambiguous (auch Disc-Image) – nur bei gesichertem ConsoleKey konvertieren |
| Master System | SMS | .sms | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Game Gear | GG | .gg | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| PC Engine (HuCard) | PCE | .pce | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Neo Geo AES/MVS | NEOGEO | .zip (ROM-Set) | – | – | – | ❌ None | – | **Wie Arcade – Set-basiert, nicht konvertieren** |
| Sega 32X | 32X | .32x | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Atari 2600 | A26 | .a26 | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Atari 5200 | A52 | .a52 | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Atari 7800 | A78 | .a78 | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Atari Jaguar | JAG | .j64 | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Atari Lynx | LYNX | .lnx | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| ColecoVision | COLECO | .col | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Intellivision | INTV | .int | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Virtual Boy | VB | .vb | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| WonderSwan | WS | .ws | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| WonderSwan Color | WSC | .wsc | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Neo Geo Pocket | NGP | .ngp | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Neo Geo Pocket Color | NGPC | (wie NGP) | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Sega SG-1000 | SG1000 | .sg, .sc | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Pokemon Mini | POKEMINI | .min | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Vectrex | VECTREX | .vec | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Fairchild Channel F | CHANNELF | (generic) | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Odyssey 2 | ODYSSEY2 | .o2 | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Watara Supervision | SUPERVISION | (generic) | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |

### 3.5 Computer-Systeme

| System | Key | Eingangsformate | Zielformat | Bevorzugt | Tool | Validierung | Auto | Lossless | Risiken |
|---|---|---|---|---|---|---|---|---|---|
| Commodore Amiga | AMIGA | .adf | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | ADF ist ein Floppy-Dump – NICHT in anderes Floppy-Format konvertieren |
| Commodore 64 | C64 | .d64, .t64, .prg, .crt | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | Verschiedene Formate (Disk, Tape, Programm, Cartridge) – alle nur archivieren |
| Atari 8-bit | A800 | .atr, .xex, .xfd | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Atari ST | ATARIST | .st, .stx | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | STX enthält Kopierschutz-Informationen – nicht stören |
| ZX Spectrum | ZX | .tzx, .tap, .z80, .sna | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | TZX ist ein Tape-Format mit Timing-Daten – NICHT nach TAP konvertieren |
| MSX | MSX | .mx1, .mx2, .rom, .dsk | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| Amstrad CPC | CPC | .dsk, .sna, .tap | .zip | 7z a -tzip | 7z t | ✅ | 🟢 | – |
| MS-DOS | DOS | (directory-based, .exe, .img) | – | – | – | ❌ None | – | DOS-Spiele sind oft Verzeichnisstrukturen – keine standardisierte Konvertierung |
| NEC PC-98 | PC98 | .hdi, .fdi, .d88 | .zip (nur kleine) | 7z a -tzip | 7z t | 🟡 Manual | 🟢 | HDD-Images können sehr gross sein |
| Sharp X68000 | X68K | .xdf, .dim, .hds | .zip (nur kleine) | 7z a -tzip | 7z t | 🟡 Manual | 🟢 | – |

### 3.6 Arcade

| System | Key | Eingangsformate | Zielformat | Auto | Risiken |
|---|---|---|---|---|---|
| Arcade (MAME/FBNeo) | ARCADE | .zip (ROM-Set), .7z (komprimiertes Set) | ❌ NICHT konvertieren | ❌ None | Set-Integrität, MAME-Versionsbindung, Parent/Clone-Ketten |
| Arcade CHD | (Teil von ARCADE) | .chd (HDD/CD für Arcade) | ❌ NICHT konvertieren | ❌ None | CHD ist bereits das Zielformat; Rückkonvertierung zerstört Integrität |

### 3.7 Sonderfälle / Nicht konvertierbar

| System | Key | Eingangsformate | ConversionPolicy | Grund |
|---|---|---|---|---|
| Nintendo Switch | SWITCH | .nsp, .xci, .nsz | **None** | Kein standardisiertes Open-Source-Tool; NSZ-Decompression braucht Schlüssel; rechtliche Grauzone |
| PlayStation 3 | PS3 | .iso, folder, .pkg | **None** | Encrypted/signed Content; kein verlustfreies portables Zielformat |
| PlayStation Vita | VITA | .vpk | **None** | App-Container; kein Standard-Conversion-Pfad |
| Xbox | XBOX | .iso (XDVDFS) | **ManualOnly** | CHD technisch möglich (createdvd), aber kaum Emulator-Support für CHD |
| Xbox 360 | X360 | .iso (XEX) | **ManualOnly** | Wie Xbox – CHD möglich, kaum genutzt |
| Wii U | WIIU | .wux, .rpx, .iso | **ManualOnly** | WUX ist bereits komprimiert; kein standardisiertes Zielformat |
| Nintendo 3DS | 3DS | .3ds, .cia | **None** | Formatwechsel erfordert Decrypt-Keys; geschlossenes Ökosystem |
| Neo Geo AES/MVS | NEOGEO | .zip (ROM-Set) | **None** | Set-basiert wie Arcade |

---

## 4. Globale Conversion-Regeln

### 4.1 Wann Conversion erlaubt ist

| Bedingung | Erforderlich |
|---|---|
| ConsoleKey ist bekannt und verifiziert | ✅ Pflicht |
| ConversionTarget für ConsoleKey ist definiert | ✅ Pflicht |
| Quelldatei ist NICHT bereits im Zielformat | ✅ Pflicht |
| Zieldatei existiert noch nicht | ✅ Pflicht |
| Tool ist verfügbar und Hash-verifiziert | ✅ Pflicht |
| ConversionPolicy ≠ None | ✅ Pflicht |
| ConversionPolicy = ManualOnly → explizite Nutzerbestätigung | ✅ Pflicht |

### 4.2 Wann Conversion blockiert werden muss

| Situation | Aktion |
|---|---|
| ConsoleKey = UNKNOWN | ❌ Block – kein Conversion ohne gesichertes System |
| ConsoleKey = ARCADE oder NEOGEO | ❌ Block – Set-basiert |
| ConversionPolicy = None | ❌ Block |
| Quelldatei ist Teil eines Multi-Disc-Sets (m3u-referenziert) | ⚠️ Nur als kompletter Set konvertieren |
| Quelldatei ist .nkit.iso oder .nkit.gcz | ⚠️ Warning: Lossy-Quelle, Ergebnis nicht bit-identisch mit Original |
| Quelldatei ist .cdi (Dreamcast) | ⚠️ Warning: CDI oft abgeschnitten, Ergebnis kann unvollständig sein |
| Quellformat ist bereits lossy (CSO) und Ziel ist ein anderes komprimiertes Format | ⚠️ Warning: Double-Lossy |
| Quelldatei ist verschlüsselt (encrypted PBP, PKG, NSP) | ❌ Block |

### 4.3 Wann Review nötig ist

- NKit → RVZ (Padding verloren, Nutzer muss wissen)
- CDI → CHD (Datenintegrität unsicher)
- CSO → CHD (Padding-Stripping ist irreversibel, aber CSO→ISO→CHD ist funktional korrekt)
- Jede Konvertierung von Dateien >8 GB (DVD9-Images, Wii-Discs)
- Erste Konvertierung eines bisher unkonvertierten Systems (neues ConversionTarget)

### 4.4 Wann nur Repackaging erlaubt ist

Alle Cartridge-Systeme, alle Computer-Disk-Images:
- Original-ROM/Image → ZIP (oder 7z)
- KEINE Transformation des Inhalts
- KEINE Headered-to-Unheadered-Konvertierung
- KEINE Byte-Order-Normalisierung (z.B. v64→z64)

---

## 5. Conversion-Typen

### 5.1 Lossless Disc Image Transformation
- **cue/bin → CHD**: Verlustfrei. chdman komprimiert mit LZMA/ZSTD + Hunk-Dedup. Vollständig reversibel via `chdman extractcd`.
- **iso → CHD**: Verlustfrei. Reversibel via `chdman extractraw` / `extracthd`.
- **gdi → CHD**: Verlustfrei. Alle Tracks werden eingebettet.
- **iso/gcm/wbfs/gcz/wia → RVZ**: Verlustfrei. DolphinTool mit ZSTD. Reversibel via `dolphintool convert -f iso`.

### 5.2 Lossy / Potentially Lossy
- **PBP → CHD**: PBP ist selbst ein lossy-komprimierter Container (PSP-Eboot). Conversion zu CHD ist technisch korrekt, aber das Ergebnis ist nicht bit-identisch mit dem Original-UMD-ISO.
- **CSO → CHD**: CSO (Compressed ISO) verwendet Sektor-Level-Kompression mit optionalem Padding-Stripping. Decompression + Re-Compression ist datenmässig korrekt, aber Padding kann fehlen.
- **NKit → RVZ**: NKit hat irreversibel Junk/Padding entfernt. RVZ-Ergebnis funktioniert, ist aber nicht das Original-ISO.
- **CDI → CHD**: CDI-Format hat oft abgeschnittene Tracks (DiscJuggler-Artefakte). CHD enthält dann auch das unvollständige Image.

### 5.3 Repackaging (Archive Normalization)
- **ROM → ZIP**: Unveränderte Datei in ZIP-Container. 100% reversibel.
- **ROM → 7z**: Wie ZIP, höhere Kompression. Einige Emulatoren unterstützen kein 7z direkt.
- **7z → ZIP**: Repackaging. Inhalt identisch. Nur Container-Wechsel.
- **RAR → ZIP**: Repackaging. RAR→Inhalt extrahieren→ZIP. RAR ist proprietär und sollte normalisiert werden.

### 5.4 Multi-File Assembly
- **m3u + .chd/.cue/.bin-Dateien**: Playlist-Dateien referenzieren einzelne Discs. Konvertierung muss alle referenzierten Dateien plus die m3u-Datei als Einheit betrachten.
- **cue + bin-Tracks**: Eine .cue + N .bin-Dateien → ein .chd. chdman verarbeitet das korrekt.
- **gdi + raw-Tracks**: Eine .gdi + Track-Dateien → ein .chd.

### 5.5 Metadata-Preserving Conversion
- CHD enthält SHA1 des Originals im Header (v5: Offset 0x40) → DAT-Matching nach Conversion möglich.
- RVZ enthält Disc-Hash im Container → DolphinTool verify prüft Integrität.
- ZIP CRC32 per Entry → schnelle Integritätsprüfung.

---

## 6. Tool-Landschaft

### 6.1 Benötigte Tools

| Tool | Zweck | Systeme | Lizenz | Romulus-Status |
|---|---|---|---|---|
| **chdman** (MAME) | CD/DVD/HDD → CHD, CHD verify | PS1, PS2, PSP, SAT, DC, SCD, PCECD, NEOCD, 3DO, JAGCD, PCFX, CD32, CDI, FMTOWNS | BSD-3 (MAME) | ✅ Integriert, Hash-verifiziert |
| **dolphintool** | ISO/GCM/WBFS/GCZ/WIA → RVZ | GC, WII | GPL-2 (Dolphin) | ✅ Integriert, Hash-verifiziert |
| **7z** (7-Zip) | Archivierung, Extraktion, Test | Alle Cartridge, Computer, Archiv-Normalisierung | LGPL-2.1 | ✅ Integriert, Hash-verifiziert |
| **psxtract** | PBP → CUE/BIN (PS1/PSP) | PS1, PSP | Open Source | ✅ Integriert, Hash-verifiziert |
| **ciso/maxcso** | CSO ↔ ISO | PSP | MIT/Open Source | ✅ ciso integriert (Hash-verifiziert), maxcso nicht |
| **wit/wwt** | WBFS ↔ ISO (Wii) | WII | GPL | ❌ Nicht integriert (DolphinTool deckt ab) |
| **nkit** | NKit → ISO (Rebuild) | GC, WII | Open Source | ❌ Nicht integriert – manueller Pfad |
| **nsz** | NSP ↔ NSZ (Switch) | SWITCH | – | ❌ Nicht integriert – ConversionPolicy = None |
| **extract-xiso** | Xbox ISO Extraktion | XBOX, X360 | Open Source | ❌ Nicht integriert |
| **wux** | WUD → WUX (Wii U) | WIIU | Open Source | ❌ Nicht integriert |

### 6.2 Tool-Risiken

| Risiko | Betroffene Tools | Mitigation |
|---|---|---|
| PATH-Poisoning | Alle | ✅ Bereits mitigiert: Hash-Verifizierung + feste Suchpfade |
| Argument Injection | Alle | ✅ Bereits mitigiert: ArgumentList statt String-Concatenation |
| Timeout / Hang | chdman (grosse DVD-Images) | ✅ 30-Minuten-Timeout, aber evtl. zu kurz für BD-Images |
| Pipe Deadlock | Alle | ✅ Async stdout/stderr Capture |
| Fehlende Tool-Binaries | Alle | ✅ Skip mit Reason, kein Crash |
| Veraltete Tool-Versionen | chdman, dolphintool | ⚠️ Kein Versions-Check, nur Hash. Neues MAME-Release = neuer Hash nötig |

### 6.3 Fehlende Tool-Integration (priorisiert)

1. **maxcso** – CSO-Decompression für PSP-Konvertierungs-Zwischenschritt. Ohne maxcso/ciso kann CSO→CHD nicht automatisch laufen.
2. **nkit** – NKit-Rebuild für GC/Wii. Niedrige Priorität (ManualOnly), aber für Nutzer mit NKit-Libraries hilfreich.
3. **extract-xiso** – Wenn Xbox-CHD-Support gewünscht wird.
4. **wux** – Wenn Wii-U-Support über ManualOnly hinaus gewünscht wird.

---

## 7. Grösste Lücken / Risiken

### Prio 1 – Release-Blocker

| # | Lücke | Impact | Betroffene Dateien |
|---|---|---|---|
| **L1** | **Keine ConversionPolicy-Taxonomie im Datenmodell.** Systeme ohne ConversionTarget werden stillschweigend geskippt. Es gibt kein explizites `ConversionPolicy = None` Feld in `consoles.json` oder `ConversionTarget`. | Nutzer versteht nicht, warum Switch/PS3/Arcade nicht konvertiert wird. Kein Unterschied zwischen "nicht unterstützt" und "Tool fehlt". | consoles.json, ConversionModels.cs, FormatConverterAdapter.cs |
| **L2** | **Multi-File-Konvertierung nicht atomisch.** Bei cue/bin-Paaren in ZIP: nur die erste .cue wird gefunden. Multi-Disc-m3u-Sets werden nicht als Einheit erkannt. | Inkonsistente Teilkonvertierungen möglich. | FormatConverterAdapter.cs (ConvertArchiveToChdman) |
| **L3** | **PS2 CD vs. DVD nicht unterschieden.** Alle PS2-Images gehen an `createdvd`. CD-basierte PS2-Spiele (~15% des Katalogs) werden damit falsch konvertiert. | Fehlerhafte CHD-Dateien oder `chdman`-Fehler bei CD-Images mit DVD-Modus. | FormatConverterAdapter.DefaultBestFormats |
| **L4** | **CSO→CHD Pipeline fehlt Zwischen-Decompression.** CSO wird an chdman übergeben, aber chdman akzeptiert kein CSO. Kein ciso/maxcso-Zwischenschritt implementiert. | PSP-CSO-Dateien werden geskippt oder fehlerhaft verarbeitet. | FormatConverterAdapter.ConvertWithChdman |

### Prio 2 – Hohe Risiken

| # | Lücke | Impact |
|---|---|---|
| **L5** | **NKit-Warning fehlt.** NKit-Dateien werden als normales GC/Wii-ISO behandelt, ohne Warnung über irreversiblen Datenverlust. | Nutzer glaubt, die RVZ-Konvertierung sei verlustfrei. |
| **L6** | **CDI-Input nicht speziell behandelt.** .cdi-Dateien (Dreamcast) werden wie normale ISOs an chdman übergeben, ohne Track-Vollständigkeitsprüfung. | Unvollständige CHD-Dateien möglich. |
| **L7** | **Arcade/NEOGEO in DefaultBestFormats eingetragen.** ARCADE und NEOGEO sind als ZIP-Ziel definiert, obwohl diese Systeme Set-basiert sind und nicht repackaged werden dürfen. **Dies ist ein aktiver Fehler.** | Romulus könnte Arcade-ROM-Sets falsch repackagen (z.B. verschachtelte ZIPs). |
| **L8** | **38 Systeme ohne ConversionTarget.** Darunter viele Cartridge-Systeme (32X, A26, A52, A78, LYNX, VB, WS, WSC, NGP, NGPC, SG1000, COLECO, INTV, POKEMINI, VECTREX, CHANNELF, ODYSSEY2, SUPERVISION) und alle Computer (AMIGA, C64, A800, ATARIST, ZX, MSX, CPC, PC98, X68K, DOS) und Sonderfälle (SWITCH, PS3, 3DS, VITA, XBOX, X360, WIIU). | Nicht abgedeckte Systeme werden stillschweigend geskippt. |
| **L9** | **RVZ-Verifizierung nur via Magic-Byte.** Kein echter Integritäts-Check wie bei CHD (chdman verify). Ein korrumpiertes RVZ mit gültigem Header wird als OK angesehen. | Potentiell defekte Konvertierungen unentdeckt. |

### Prio 3 – Wartbarkeit

| # | Lücke | Impact |
|---|---|---|
| **L10** | **Format-Prioritäten doppelt definiert** – einmal in FormatConverterAdapter.DefaultBestFormats, einmal in FeatureService.Conversion.ConsoleFormatPriority. Kein Single-Source-of-Truth. | Divergenz zwischen Conversion-Logik und UI-Anzeige möglich. |
| **L11** | **Compression-Ratios hardcoded** statt aus consoles.json oder einer dedizierten Conversion-Konfiguration. | Nicht konfigurierbar, nicht testbar gegen reale Daten. |
| **L12** | **FeatureService.Conversion.GetTargetFormat()** ist eine separate, vereinfachte Mapping-Logik, die nicht mit FormatConverterAdapter.GetTargetFormat() synchronisiert ist. | GUI-Estimation und tatsächliche Konvertierung können unterschiedliche Entscheidungen treffen. |

---

## 8. Empfohlene nächste Schritte

### Phase 1: Datenmodell-Härtung (Prio 1)

1. **ConversionPolicy in consoles.json aufnehmen**
   - Werte: `Auto`, `ManualOnly`, `None`, `ArchiveOnly`
   - Jedes System bekommt eine explizite Policy
   - FormatConverterAdapter liest die Policy und blockt entsprechend

2. **ConversionTarget-Registry aus consoles.json generieren**
   - Instead of hardcoded `DefaultBestFormats` Dictionary → JSON-gesteuert
   - Single Source of Truth für Conversion-Konfiguration
   - Testbar, konfigurierbar, versionierbar

3. **ARCADE und NEOGEO aus DefaultBestFormats entfernen** (L7 – aktiver Fehler)
   - ConversionPolicy = None für beide Systeme
   - Test: Arcade-ZIP wird nicht konvertiert

### Phase 2: Pipeline-Korrekturen (Prio 1-2)

4. **PS2 CD/DVD-Unterscheidung** (L3)
   - Heuristik: Image-Grösse <700 MB → createcd, ≥700 MB → createdvd
   - Oder: SYSTEM.CNF lesen, BOOT2-Eintrag prüfen

5. **CSO→CHD-Pipeline mit Zwischenschritt** (L4)
   - CSO → ciso/maxcso decompress → ISO → chdman createcd → CHD
   - Temp-ISO in extractDir, Cleanup in finally

6. **Multi-File-Awareness** (L2)
   - cue/bin-Erkennung verbessern: alle .cue-Dateien im Archiv, nicht nur die erste
   - m3u-Set-Erkennung: Konvertierung als atomische Einheit

### Phase 3: Absicherung und Transparenz (Prio 2)

7. **NKit-Warning** (L5)
   - Erkennung via Dateinamen (.nkit.iso/.nkit.gcz) oder Header
   - Warning-Flag im ConversionResult
   - UI zeigt "Lossy Source" Hinweis

8. **CDI-Sonderbehandlung** (L6)
   - .cdi-Input → ConversionPolicy = ManualOnly mit explizitem Warning
   - Oder: Track-Analyse vor Conversion (Grösse vs. erwartete Disc-Kapazität)

9. **Fehlende Cartridge/Computer-Systeme abdecken** (L8)
   - Alle fehlenden Systeme mit ConversionPolicy = ArchiveOnly + ZIP-Target eintragen
   - Oder explizit als ConversionPolicy = None markieren

### Phase 4: Qualität und Wartbarkeit (Prio 3)

10. **Single Source of Truth für Format-Mappings** (L10, L12)
    - FormatConverterAdapter und FeatureService.Conversion lesen dieselbe Konfiguration
    - FeatureService.GetTargetFormat() delegiert an FormatConverterAdapter.GetTargetFormat()

11. **RVZ-Verifizierung verbessern** (L9)
    - `dolphintool verify` (falls in neueren Versionen verfügbar) oder
    - dolphintool convert zu ISO in temporärer Datei + Grössen-/Hash-Vergleich

12. **Compression-Ratios aus Benchmark-Daten ableiten** (L11)
    - Reale Messwerte statt geschätzte Ratios
    - In einer dedizierten Konfigurationsdatei oder per System in consoles.json

---

## Anhang A: Vollständige System-Policy-Empfehlung

| Key | ConversionPolicy | Zielformat | Tool |
|---|---|---|---|
| 3DO | Auto | .chd | chdman createcd |
| 3DS | None | – | – |
| 32X | ArchiveOnly | .zip | 7z |
| A26 | ArchiveOnly | .zip | 7z |
| A52 | ArchiveOnly | .zip | 7z |
| A78 | ArchiveOnly | .zip | 7z |
| A800 | ArchiveOnly | .zip | 7z |
| AMIGA | ArchiveOnly | .zip | 7z |
| ARCADE | None | – | – |
| ATARIST | ArchiveOnly | .zip | 7z |
| C64 | ArchiveOnly | .zip | 7z |
| CD32 | Auto | .chd | chdman createcd |
| CDI | Auto | .chd | chdman createcd |
| CHANNELF | ArchiveOnly | .zip | 7z |
| COLECO | ArchiveOnly | .zip | 7z |
| CPC | ArchiveOnly | .zip | 7z |
| DC | Auto | .chd | chdman createcd |
| DOS | None | – | – |
| FMTOWNS | Auto | .chd | chdman createcd |
| GB | ArchiveOnly | .zip | 7z |
| GBA | ArchiveOnly | .zip | 7z |
| GBC | ArchiveOnly | .zip | 7z |
| GC | Auto | .rvz | dolphintool |
| GG | ArchiveOnly | .zip | 7z |
| INTV | ArchiveOnly | .zip | 7z |
| JAG | ArchiveOnly | .zip | 7z |
| JAGCD | Auto | .chd | chdman createcd |
| LYNX | ArchiveOnly | .zip | 7z |
| MD | ArchiveOnly | .zip | 7z |
| MSX | ArchiveOnly | .zip | 7z |
| N64 | ArchiveOnly | .zip | 7z |
| NDS | ArchiveOnly | .zip | 7z |
| NEOCD | Auto | .chd | chdman createcd |
| NEOGEO | None | – | – |
| NES | ArchiveOnly | .zip | 7z |
| NGP | ArchiveOnly | .zip | 7z |
| NGPC | ArchiveOnly | .zip | 7z |
| ODYSSEY2 | ArchiveOnly | .zip | 7z |
| PC98 | ManualOnly | .zip | 7z |
| PCE | ArchiveOnly | .zip | 7z |
| PCECD | Auto | .chd | chdman createcd |
| PCFX | Auto | .chd | chdman createcd |
| POKEMINI | ArchiveOnly | .zip | 7z |
| PS1 | Auto | .chd | chdman createcd |
| PS2 | Auto | .chd | chdman createdvd / createcd |
| PS3 | None | – | – |
| PSP | Auto | .chd | chdman createcd |
| SAT | Auto | .chd | chdman createcd |
| SCD | Auto | .chd | chdman createcd |
| SG1000 | ArchiveOnly | .zip | 7z |
| SMS | ArchiveOnly | .zip | 7z |
| SNES | ArchiveOnly | .zip | 7z |
| SUPERVISION | ArchiveOnly | .zip | 7z |
| SWITCH | None | – | – |
| VB | ArchiveOnly | .zip | 7z |
| VECTREX | ArchiveOnly | .zip | 7z |
| VITA | None | – | – |
| WII | Auto | .rvz | dolphintool |
| WIIU | ManualOnly | – | – |
| WS | ArchiveOnly | .zip | 7z |
| WSC | ArchiveOnly | .zip | 7z |
| X360 | ManualOnly | .chd | chdman createdvd |
| X68K | ManualOnly | .zip | 7z |
| XBOX | ManualOnly | .chd | chdman createdvd |
| ZX | ArchiveOnly | .zip | 7z |

---

## Anhang B: Format-Eigenschaftsmatrix

| Format | Typ | Verlustfrei | Reversibel | Multi-Track | Emulator-Support | Validierung |
|---|---|---|---|---|---|---|
| .chd | Disc-Image-Container | ✅ | ✅ (extractcd/extracthd) | ✅ (embedded) | Breit (RetroArch, MAME, DuckStation, PCSX2, Mednafen) | chdman verify |
| .rvz | Disc-Image-Container | ✅ | ✅ (dolphintool→ISO) | N/A (Single-Disc) | Dolphin | Magic-Byte + evtl. dolphintool verify |
| .cue/.bin | Disc Image | ✅ (Roh) | – (ist Roh-Format) | ✅ (Multi-Track via .cue) | Universell | Dateigrößen-Match mit .cue |
| .gdi | Disc Image (DC) | ✅ (Roh) | – (ist Roh-Format) | ✅ (Track-Verzeichnis) | DC-Emulatoren | Track-Datei-Existenz |
| .iso | Disc Image | ✅ (Roh) | – (ist Roh-Format) | ❌ (Single-Track) | Universell | Dateigrößen-Plausibilität |
| .cdi | Disc Image (DC) | 🟡 (oft gekürzt) | – | 🟡 | CDI-kompatible Emulatoren | Schwach (kein Standard-Verify) |
| .cso | Komprimiertes ISO | 🟡 (Padding stripped) | ✅ → ISO | ❌ | PPSSPP | Decompression + Größen-Check |
| .pbp | PSP-Container | 🟡 (re-encoded) | ⚠️ Eingeschränkt | ❌ | PPSSPP | PBP-Header-Check |
| .wbfs | Wii-Container | ✅ | ✅ → ISO | ❌ | Dolphin, USB-Loader | wit verify |
| .gcz | GC-Komprimierung | ✅ | ✅ → ISO | ❌ | Dolphin | dolphintool convert |
| .wia | GC/Wii-Komprimierung | ✅ | ✅ → ISO | ❌ | Dolphin | dolphintool convert |
| .nkit.iso/.nkit.gcz | GC/Wii (reduziert) | 🔴 (Padding entfernt) | ⚠️ Rebuild möglich, nicht identisch | ❌ | Dolphin (direkt), manche Emulatoren NICHT | NKit-Tool verify |
| .zip | Archiv | ✅ | ✅ (extract) | N/A | Breit (RetroArch, viele) | 7z t / CRC32 |
| .7z | Archiv | ✅ | ✅ (extract) | N/A | RetroArch (teilweise), einige Emulatoren NICHT | 7z t |
| .rar | Archiv (proprietär) | ✅ | ✅ (extract) | N/A | Kaum direkt unterstützt | unrar t |
| .nsp | Switch eShop | N/A | N/A | N/A | Yuzu/Ryujinx | – |
| .xci | Switch Cartridge | N/A | N/A | N/A | Yuzu/Ryujinx | – |
| .nsz | Switch komprimiert | 🟡 | ✅ → NSP | N/A | Yuzu (mit Plugin) | nsz verify |
| .vpk | Vita App | N/A | N/A | N/A | Vita3K | – |
| .wux | Wii U komprimiert | ✅ | ✅ → WUD | ❌ | Cemu | WUX-Header-Check |
| .adf | Amiga Floppy | ✅ (Roh) | – | N/A | WinUAE, FS-UAE | Dateigröße = 901120 |
| .d64 | C64 Floppy | ✅ (Roh) | – | N/A | VICE | Dateigröße |
| .tzx | ZX Tape | ✅ (Timing-preserving) | – | N/A | Fuse, ZXSpin | TZX-Header-Magic |
| .st | Atari ST Floppy | ✅ (Roh) | – | N/A | Hatari, Steem | Dateigröße |
| .atr | Atari 800 Floppy | ✅ (Roh) | – | N/A | Altirra, Atari800 | ATR-Header-Check |

---

## Anhang C: Conversion-Flussdiagramm (Pseudo)

```
Input: (FilePath, ConsoleKey)
│
├── ConsoleKey == UNKNOWN?  → BLOCK
├── ConsoleKey == ARCADE | NEOGEO?  → BLOCK
├── GetConversionPolicy(ConsoleKey)
│   ├── None → BLOCK
│   ├── ManualOnly → REQUIRE_CONFIRMATION
│   ├── ArchiveOnly → ARCHIVE_PATH
│   └── Auto → CONVERT_PATH
│
├── ARCHIVE_PATH:
│   ├── Already .zip? → SKIP
│   ├── 7z a -tzip → Target
│   └── 7z t Target → VERIFY
│
├── CONVERT_PATH:
│   ├── GetConversionTarget(ConsoleKey, SourceExt)
│   │   ├── null → SKIP ("no-target")
│   │   └── Target defined → continue
│   │
│   ├── SourceExt == TargetExt? → SKIP
│   ├── Target exists? → SKIP
│   │
│   ├── IsLossySource(SourceExt)?
│   │   ├── .nkit.* → WARNING("lossy-source-nkit")
│   │   ├── .cso → WARNING("lossy-source-cso")
│   │   ├── .cdi → WARNING("lossy-source-cdi")
│   │   └── .pbp → WARNING("lossy-source-pbp")
│   │
│   ├── IsArchive(SourceExt)?
│   │   ├── .zip/.7z → EXTRACT → FindDiscImage → CONVERT
│   │   └── Zip-Slip + Zip-Bomb guards
│   │
│   ├── Tool available + hash verified? → CONVERT
│   ├── CONVERT → VERIFY → SUCCESS | FAIL
│   └── FAIL → CLEANUP partial output
```
