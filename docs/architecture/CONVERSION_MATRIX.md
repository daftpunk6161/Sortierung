# Conversion Matrix – Romulus

**Stand:** 2026-03-21  
**Version:** 1.0  
**Basis:** consoles.json (65 Systeme), FormatConverterAdapter, CONVERSION_DOMAIN_AUDIT, CONVERSION_PRODUCT_MODEL  
**Zielgruppe:** Engine-Architektur, Implementierung, Review

---

## 1. Executive Verdict

### Hauptproblem

Die Conversion-Domäne hat **keinen formalen Contract zwischen Systemdefinition und Konvertierungsverhalten**. Der aktuelle Code enthält eine hardcodierte `DefaultBestFormats`-Map mit 22 Einträgen für 65 definierte Systeme. Es fehlt:

- Ein explizites `ConversionPolicy`-Feld pro System
- Eine vollständige Eingangs-/Ausgangsformat-Matrix
- Zwischenschritt-Definitionen für mehrstufige Pipelines (CSO→ISO→CHD, PBP→CUE/BIN→CHD)
- Klare Trennlinien zwischen "technisch machbar" und "praktisch sinnvoll"
- SourceIntegrity-Klassifikation (Lossless/Lossy/Unknown) für Quellformate

Ohne diese Matrix ist jede Engine-Implementierung ein Haufen Sonderfälle mit impliziten Annahmen.

### Designprinzipien

| # | Prinzip | Begründung |
|---|---------|------------|
| P-1 | **Kein Datenverlust durch Automatik** | Auto-Conversion nur bei verifizierbaren, verlustfreien Pfaden |
| P-2 | **Explizit vor implizit** | Jedes System hat eine deklarierte ConversionPolicy – kein stilles Skip |
| P-3 | **Single Source of Truth** | Alle Pfade, Policies und Targets werden aus EINER Konfiguration gelesen |
| P-4 | **Verify-or-Die** | Keine Quelle wird in den Trash verschoben ohne bestandene Verifizierung |
| P-5 | **Reproducibility** | Gleicher Input + gleiche Config = identischer Output, immer |
| P-6 | **Archiving ≠ Transformation** | ROM-in-ZIP packen ist Archivierung. ISO→CHD ist Transformation. Beides hat unterschiedliche Risikoprofile |
| P-7 | **Set-Integrität ist unantastbar** | Arcade/NEOGEO-ZIP-Sets dürfen NICHT verändert, repackaged oder normalisiert werden |

### Kurzfazit

Diese Matrix definiert **76 Conversion-Pfade** über **65 Systeme** in **5 Plattformfamilien**. Davon sind:
- **14 Systeme** mit Auto-Disc-Transformation (→ CHD / → RVZ)
- **36 Systeme** mit Auto-Archive-Normalisierung (→ ZIP)
- **3 Systeme** mit ManualOnly-Policy
- **7 Systeme** mit konsequentem None-Block
- **5 Systeme** mit ArchiveOnly + ManualOnly-Sonderfällen

---

## 2. Globale Regeln

### 2.1 Was ein bevorzugtes Zielformat ausmacht

| Kriterium | Pflicht | Beispiel |
|-----------|---------|---------|
| **Verlustfreie Konvertierung** vom Referenzformat | ✅ | cue/bin → CHD ist lossless; CSO → CHD verliert Padding |
| **Reversibilität** (Target → Source reproduzierbar) | ✅ | `chdman extractcd`, `dolphintool convert -f iso` |
| **Tool-Verifizierung** nach Konvertierung möglich | ✅ | `chdman verify`, `7z t` |
| **Breiter Emulator-Support** | ✅ | CHD: RetroArch, DuckStation, PCSX2, Mednafen, MAME; RVZ: Dolphin |
| **Langzeitarchivtauglichkeit** | Wünschenswert | CHD v5, RVZ: offene, dokumentierte Formate |
| **Platzersparnis** vs. unkomprimiert | Wünschenswert | CHD: ~40-60% Einsparung; RVZ: ~50-60% |

### 2.2 Wann Auto-Conversion erlaubt ist

Alle Bedingungen müssen gleichzeitig erfüllt sein:

| # | Bedingung | Prüfung |
|---|-----------|---------|
| C-1 | ConsoleKey ist bekannt und verifiziert (≠ UNKNOWN) | Pipeline-Check |
| C-2 | ConversionPolicy = Auto ODER ArchiveOnly | consoles.json |
| C-3 | ConversionTarget für ConsoleKey + Quellformat existiert | ConversionRegistry |
| C-4 | Quelldatei ist NICHT bereits im Zielformat | Extension-Match |
| C-5 | Zieldatei existiert noch nicht | Filesystem-Check |
| C-6 | Tool ist verfügbar UND SHA256-Hash-verifiziert | ToolRunner |
| C-7 | SourceIntegrity ≠ Unknown | Integrity-Check |
| C-8 | Quellformat ist NICHT SetProtected (ARCADE/NEOGEO) | Policy-Check |

### 2.3 Wann Review nötig ist (ManualOnly)

| Situation | Grund |
|-----------|-------|
| ConversionPolicy = ManualOnly | System hat Risiken, die Nutzerentscheidung brauchen |
| SourceIntegrity = Lossy | NKit→RVZ, CSO→CHD, PBP→CHD: Nutzer muss Datenverlust kennen |
| CDI-Input (Dreamcast) | Tracks oft abgeschnitten – Ergebnis kann unvollständig sein |
| Image >8 GB (DVD9, BD) | Lange Laufzeit + hohes Speicher-Risiko |
| Erste Konvertierung eines neuen Systemtyps | Sicherheitsnetz bei neuer Policy-Aktivierung |
| Xbox/X360/WiiU-Systeme | Emulator-Support für CHD minimal – Nutzer muss wissen |

### 2.4 Wann blockiert werden muss (None)

| Situation | Grund |
|-----------|-------|
| ConsoleKey = UNKNOWN | Kein Zielformat bestimmbar |
| ConversionPolicy = None | System hat keinen sicheren Pfad |
| ARCADE / NEOGEO ROM-Sets | Set-Integrität, Versions-/DAT-Bindung |
| Verschlüsselte Quellen (Encrypted PBP, PKG, NSP) | Decryption wäre rechtliche Grauzone |
| Switch (NSP/XCI/NSZ) | Kein standardisiertes OSS-Tool |
| PS3 (ISO/PKG/Folder) | Encrypted, kein portables Zielformat |
| 3DS (3DS/CIA) | Formatwechsel braucht Decrypt-Keys |
| Vita (VPK) | App-Container, kein Conversion-Pfad |

---

## 3. Conversion-Matrix pro Plattformfamilie

### Legende

| Symbol | Bedeutung |
|--------|-----------|
| 🟢 | Lossless – Bit-für-Bit reversibel |
| 🟡 | Acceptable – Funktional korrekt, Quelle war bereits lossy |
| 🔴 | Risky – Ergebnis kann unvollständig/inkompatibel sein |
| ⛔ | Blocked – Conversion nicht erlaubt |
| **A** | Auto |
| **M** | ManualOnly |
| **N** | None (blockiert) |
| **AO** | ArchiveOnly |

---

### 3.1 Cartridge-basierte Systeme

**Conversion-Typ:** Archive Normalization (Repackaging)  
**Risikoprofil:** Minimal – Inhalt wird nicht transformiert, nur in ZIP gepackt  
**Tool:** 7z (a -tzip)  
**Verify:** `7z t -y <target>`  
**Lossless:** 🟢 Immer  
**Policy:** ArchiveOnly (Auto)

#### Core-Regel für alle Cartridge-Systeme

> ROM-Dateien innerhalb des Archivs werden NICHT umbenannt, NICHT geheadert/unheadert,
> NICHT byte-order-normalisiert. CRC32/SHA1 des ungepackten ROMs MUSS identisch bleiben.
> Archive enthalten genau EINE ROM-Datei. Multi-ROM-Archive werden nicht zusammengefasst.

#### Matrix

| System | Key | Eingangsformate | Ziel | Alt. Ziel | chdman-Cmd | Tool | Verify | Policy | Lossless | Zwischenschritte | Risiken / Hinweise |
|--------|-----|-----------------|------|-----------|------------|------|--------|--------|----------|------------------|--------------------|
| Nintendo Entertainment System | NES | .nes, .unf | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | Headered (iNES) vs. unheadered – nicht normalisieren |
| Super Nintendo | SNES | .sfc, .smc | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | .smc hat 512B Copier-Header – nicht entfernen |
| Nintendo 64 | N64 | .z64, .v64, .n64 | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | Byte-Order (z64=BE, v64=byteswap, n64=wordswap) – NICHT normalisieren |
| Game Boy | GB | .gb | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Game Boy Color | GBC | .gbc | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Game Boy Advance | GBA | .gba | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Nintendo DS | NDS | .nds | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | Grosse ROMs (bis 512 MB), ZIP lohnt sich |
| Sega Mega Drive / Genesis | MD | .md, .gen, .bin | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | .bin ist ambiguous – nur bei gesichertem ConsoleKey |
| Sega Master System | SMS | .sms | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Sega Game Gear | GG | .gg | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Sega 32X | 32X | .32x | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Sega SG-1000 | SG1000 | .sg, .sc | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| PC Engine (HuCard) | PCE | .pce | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Atari 2600 | A26 | .a26 | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Atari 5200 | A52 | .a52 | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Atari 7800 | A78 | .a78 | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Atari Jaguar | JAG | .j64 | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Atari Lynx | LYNX | .lnx | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| ColecoVision | COLECO | .col | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Intellivision | INTV | .int | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Virtual Boy | VB | .vb | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| WonderSwan | WS | .ws | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| WonderSwan Color | WSC | .wsc | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Neo Geo Pocket | NGP | .ngp | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Neo Geo Pocket Color | NGPC | .ngc (generisch) | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Pokemon Mini | POKEMINI | .min | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Vectrex | VECTREX | .vec | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Fairchild Channel F | CHANNELF | (generisch) | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Magnavox Odyssey 2 | ODYSSEY2 | .o2 | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |
| Watara Supervision | SUPERVISION | (generisch) | .zip | .7z | – | 7z | `7z t` | AO | 🟢 | – | – |

#### Repackaging-Sonderpfade (alle Cartridge)

| Quellformat | Ziel | Typ | Beschreibung |
|-------------|------|-----|--------------|
| Lose ROM-Datei (.nes, .gba, etc.) | .zip | Repackaging | ROM → ZIP-Archiv mit 1 Entry |
| .7z (bereits archiviert) | .zip | Recontainerization | 7z → extract → ZIP |
| .rar (bereits archiviert) | .zip | Recontainerization | RAR → extract → ZIP (RAR ist proprietär) |
| .zip (bereits im Zielformat) | – | Skip | Bereits OK |

---

### 3.2 Disc-basierte Systeme

**Conversion-Typ:** Disc Image Transformation  
**Risikoprofil:** Mittel bis hoch – Multi-Track-Handling, CD/DVD-Unterscheidung, Lossy-Quellen

#### 3.2.1 CD-basierte Systeme → CHD (chdman createcd)

| System | Key | Eingangsformate | Ziel | Alt. Ziel | chdman-Cmd | Tool | Verify | Policy | Zwischenschritte | Risiken / Hinweise |
|--------|-----|-----------------|------|-----------|------------|------|--------|--------|------------------|--------------------|
| PlayStation | PS1 | .cue/.bin, .iso, .img | .chd | – | createcd | chdman | `chdman verify` | **A** | ZIP→extract→cue/bin→chdman | Audio-Tracks nur via cue/bin vollständig; .iso verliert Audio |
| Sega Saturn | SAT | .cue/.bin, .iso, .img | .chd | – | createcd | chdman | `chdman verify` | **A** | ZIP→extract→cue/bin→chdman | – |
| Sega Dreamcast | DC | .gdi+tracks, .cue/.bin, .iso, .img | .chd | – | createcd | chdman | `chdman verify` | **A** | ZIP→extract→gdi→chdman | GDI ist das einzige vollständige DC-Format |
| Sega CD / Mega-CD | SCD | .cue/.bin, .iso, .img | .chd | – | createcd | chdman | `chdman verify` | **A** | ZIP→extract→cue/bin→chdman | – |
| PC Engine CD | PCECD | .cue/.bin, .iso, .img | .chd | – | createcd | chdman | `chdman verify` | **A** | ZIP→extract→cue/bin→chdman | – |
| Neo Geo CD | NEOCD | .cue/.bin, .iso, .img | .chd | – | createcd | chdman | `chdman verify` | **A** | ZIP→extract→cue/bin→chdman | – |
| 3DO Interactive | 3DO | .cue/.bin, .iso, .img | .chd | – | createcd | chdman | `chdman verify` | **A** | ZIP→extract→cue/bin→chdman | – |
| Atari Jaguar CD | JAGCD | .cue/.bin, .iso, .img | .chd | – | createcd | chdman | `chdman verify` | **A** | ZIP→extract→cue/bin→chdman | Selten, aber Pfad identisch |
| NEC PC-FX | PCFX | .cue/.bin, .iso, .img | .chd | – | createcd | chdman | `chdman verify` | **A** | ZIP→extract→cue/bin→chdman | – |
| Amiga CD32 | CD32 | .cue/.bin, .iso, .img | .chd | – | createcd | chdman | `chdman verify` | **A** | ZIP→extract→cue/bin→chdman | – |
| Philips CD-i | CDI_SYS | .cue/.bin, .iso, .img | .chd | – | createcd | chdman | `chdman verify` | **A** | ZIP→extract→cue/bin→chdman | ⚠️ .cdi-Input = ManualOnly (s. Sonderpfade) |
| FM Towns / Marty | FMTOWNS | .cue/.bin, .iso, .img | .chd | – | createcd | chdman | `chdman verify` | **A** | ZIP→extract→cue/bin→chdman | – |

#### Conversion-Pfade pro Quellformat (alle CD-Systeme)

| Quellformat | Ziel | Typ | SourceIntegrity | Safety | Zwischenschritte | Hinweise |
|-------------|------|-----|-----------------|--------|------------------|----------|
| .cue/.bin (Multi-Track) | .chd | Lossless Transformation | Lossless | 🟢 Safe | chdman createcd | **Referenzpfad.** Alle Audio-Tracks + Sub-Channels erhalten |
| .iso (Single-Track Data) | .chd | Lossless Transformation | Lossless | 🟢 Safe | chdman createcd | Audio-Tracks fehlen im ISO; Data-Only-Spiele = kein Verlust |
| .img (Rohimage) | .chd | Lossless Transformation | Lossless | 🟢 Safe | chdman createcd | Behandlung wie .iso |
| .gdi + Track-Dateien (DC) | .chd | Lossless Transformation | Lossless | 🟢 Safe | chdman createcd | Alle Track-Dateien müssen im gleichen Verzeichnis liegen |
| .zip/.7z (Archiv mit cue/bin) | .chd | Lossless Transformation | Lossless | 🟢 Safe | extract → cue/bin → chdman | Zip-Slip-Schutz bei Extraktion. Alle .cue-Dateien finden! |
| .cdi (DiscJuggler, DC) | .chd | Potentially Lossy | Lossy | 🔴 Risky | chdman createcd | Tracks oft abgeschnitten. **ConversionSafety = Risky, Policy = ManualOnly** |
| .mdf/.mds (Alcohol 120%) | .chd | Lossless Transformation | Lossless | 🟢 Safe | mdf2iso → ISO → chdman | Tool mdf2iso NICHT in Romulus integriert → ManualOnly |
| .nrg (Nero) | .chd | Lossless Transformation | Lossless | 🟡 Acceptable | nrg2iso → ISO → chdman | Tool nrg2iso NICHT integriert → ManualOnly |

#### 3.2.2 DVD/UMD-basierte Systeme → CHD

| System | Key | Eingangsformate | Ziel | chdman-Cmd | Verify | Policy | Zwischenschritte | Risiken / Hinweise |
|--------|-----|-----------------|------|------------|--------|--------|------------------|--------------------|
| PlayStation 2 | PS2 | .iso, .bin, .img | .chd | **createdvd** (≥700 MB) / **createcd** (<700 MB) | `chdman verify` | **A** | ZIP→extract→iso→chdman | **KRITISCH:** ~15% PS2-Spiele sind CD-basiert. Heuristik: Imagegrösse <700 MB → createcd |
| PlayStation Portable | PSP | .iso | .chd | **createcd** (UMD-Sektoren = CD-kompatibel) | `chdman verify` | **A** | – | UMD-Images sind CD-Sektor-kompatibel trotz physisch anderem Medium |

#### PS2 CD/DVD-Erkennungsheuristik

```
WENN Imagegrösse < 700 MB DANN
    chdman createcd              // CD-basiertes PS2-Spiel
SONST
    chdman createdvd             // DVD-basiertes PS2-Spiel (Regelfall)
```

Alternativ (robuster, falls implementierbar): SYSTEM.CNF im ISO lesen. Enthält "BOOT2" → DVD; "BOOT" → CD.

#### PSP Sonderpfade (Lossy-Quellen)

| Quellformat | Ziel | Typ | SourceIntegrity | Safety | Zwischenschritte | Hinweise |
|-------------|------|-----|-----------------|--------|------------------|----------|
| .iso | .chd | Lossless | Lossless | 🟢 Safe | chdman createcd | Standard-Pfad |
| .cso | .chd | Recontainerization | Lossy (Padding-stripped) | 🟡 Acceptable | ciso decompress → .iso → chdman createcd | **ciso/maxcso nötig als Zwischenschritt.** ISO-Inhalt funktional identisch, aber Padding fehlt |
| .pbp | .chd | Multi-Step Transformation | Lossy (PSP Eboot Container) | 🟡 Acceptable | psxtract → .cue/.bin → chdman createcd | PBP kann encrypted sein → dann Block |

#### 3.2.3 GameCube / Wii → RVZ (dolphintool)

| System | Key | Eingangsformate | Ziel | Alt. Ziel | Tool | Verify | Policy | Zwischenschritte | Risiken / Hinweise |
|--------|-----|-----------------|------|-----------|------|--------|--------|------------------|--------------------|
| Nintendo GameCube | GC | .iso, .gcm | .rvz | .chd | dolphintool | Magic-Byte RVZ\x01 + Size | **A** | – | – |
| Nintendo GameCube | GC | .gcz, .wia | .rvz | – | dolphintool | Magic-Byte + Size | **A** | – | GCZ/WIA sind ältere Dolphin-Formate, Upgrade auf RVZ lohnt sich |
| Nintendo Wii | WII | .iso | .rvz | .chd | dolphintool | Magic-Byte + Size | **A** | – | – |
| Nintendo Wii | WII | .wbfs | .rvz | – | dolphintool | Magic-Byte + Size | **A** | – | WBFS war das Wii-Homebrew-Standardformat, RVZ ist besser |
| Nintendo Wii | WII | .gcz, .wia | .rvz | – | dolphintool | Magic-Byte + Size | **A** | – | – |

#### GC/Wii Sonderpfade (Lossy-Quellen)

| Quellformat | Ziel | Typ | SourceIntegrity | Safety | Hinweise |
|-------------|------|-----|-----------------|--------|----------|
| .nkit.iso | .rvz | Lossy Transformation | **Lossy** (Junk/Padding irreversibel entfernt) | 🔴 Risky | **ConversionSafety = Risky.** RVZ-Output funktioniert, ist aber NICHT bit-identisch mit Original-ISO. Nutzer-Warnung Pflicht |
| .nkit.gcz | .rvz | Lossy Transformation | **Lossy** | 🔴 Risky | Wie .nkit.iso |
| .wad (WiiWare/VC) | – | ⛔ Blocked | N/A | ⛔ | WAD-Dateien sind KEINE Disc-Images. Conversion = Skip |

#### RVZ-Verify: Bekannte Schwäche

Die aktuelle Verifizierung prüft nur 4 Magic Bytes (RVZ\x01) + Dateigrösse >4 Byte.  
**Das ist unzureichend.** Ein korrumpiertes RVZ mit gültigem Header wird als OK angesehen.

**Empfohlene Verbesserung:**
1. `dolphintool convert -f iso -i <rvz> -o /dev/null` als Dry-Check (prüft interne Integrität)
2. Alternativ: Hash des ersten und letzten Blocks lesen und gegen erwartete Disc-ID prüfen

---

### 3.3 Arcade-Systeme

**Conversion-Typ:** ⛔ NICHT konvertieren  
**Policy:** None  
**Begründung:** Arcade-ROM-Sets sind keine einzelnen Dateien, sondern versionierte, DAT-gebundene Sammlungen

| System | Key | Typische Formate | Ziel | Policy | Begründung |
|--------|-----|------------------|------|--------|------------|
| Arcade (MAME/FBNeo) | ARCADE | .zip (ROM-Set), .7z | ⛔ KEINS | **N** | ZIP enthält definierte ROM-/PLD-/Disk-Kombinationen. Parent/Clone-Ketten. MAME-Versionsbindung. Jede Änderung → Set-Integrität zerstört |
| Arcade CHD | (ARCADE) | .chd (HDD/CD für Arcade-Boards) | ⛔ KEINS | **N** | CHD IST bereits das Zielformat. Rückkonvertierung sinnlos |
| Neo Geo AES/MVS | NEOGEO | .zip (ROM-Set) | ⛔ KEINS | **N** | Identisch mit ARCADE: Set-basiert, DAT-gebunden |

#### ⚠️ AKTIVER BUG in DefaultBestFormats

`ARCADE` und `NEOGEO` sind aktuell als ZIP-Targets eingetragen:
```csharp
["NEOGEO"] = new(".zip", "7z", "zip"),
["ARCADE"] = new(".zip", "7z", "zip"),
```

**Das ist falsch.** Arcade/NEOGEO-ZIPs sind KEINE Archivierung eines ROMs, sondern bereits das native Format.
Ein Re-ZIP eines bereits bestehenden ZIP-Sets erzeugt verschachtelte Archives oder zerstört die Set-Struktur.

**Fix:** Beide Einträge entfernen. ConversionPolicy = None.

---

### 3.4 Computer- / PC-Systeme

**Conversion-Typ:** Archive Normalization (nur ZIP-Wrapping)  
**Core-Regel:** Interne Formate (.adf, .d64, .st, .tzx, .dsk etc.) werden NIEMALS in andere Disk-Image-Formate transformiert. Nur Archivierung in ZIP.

| System | Key | Eingangsformate | Ziel | Alt. Ziel | Tool | Verify | Policy | Lossless | Risiken / Hinweise |
|--------|-----|-----------------|------|-----------|------|--------|--------|----------|--------------------|
| Commodore Amiga | AMIGA | .adf | .zip | .7z | 7z | `7z t` | **AO** | 🟢 | ADF = Floppy-Dump. NICHT nach DMS/ADZ konvertieren |
| Commodore 64 | C64 | .d64, .t64, .prg, .crt | .zip | .7z | 7z | `7z t` | **AO** | 🟢 | Mehrere Formattypen (Disk/Tape/Programm/Cartridge) – alle nur archivieren |
| Atari 8-bit | A800 | .atr, .xex, .xfd | .zip | .7z | 7z | `7z t` | **AO** | 🟢 | – |
| Atari ST | ATARIST | .st, .stx, .msa | .zip | .7z | 7z | `7z t` | **AO** | 🟢 | STX enthält Kopierschutz-Daten. NICHT nach .st konvertieren |
| ZX Spectrum | ZX | .tzx, .tap, .z80, .sna | .zip | .7z | 7z | `7z t` | **AO** | 🟢 | TZX enthält Timing-Daten. NICHT nach TAP konvertieren |
| MSX | MSX | .mx1, .mx2, .rom, .dsk | .zip | .7z | 7z | `7z t` | **AO** | 🟢 | – |
| Amstrad CPC | CPC | .dsk, .sna, .tap | .zip | .7z | 7z | `7z t` | **AO** | 🟢 | – |
| MS-DOS | DOS | Verzeichnisstruktur, .exe, .img, .iso | – | – | – | – | **N** | – | DOS-Spiele = Verzeichnisstrukturen. Keine standardisierte Archivierung möglich |
| NEC PC-98 | PC98 | .hdi, .fdi, .d88 | .zip (nur <100 MB) | .7z | 7z | `7z t` | **M** | 🟢 | HDD-Images können sehr gross sein; ZIP nicht immer sinnvoll |
| Sharp X68000 | X68K | .xdf, .dim, .hds | .zip (nur <100 MB) | .7z | 7z | `7z t` | **M** | 🟢 | – |

#### Verbotene Computer-Konvertierungen

| Konvertierung | Warum verboten |
|---------------|---------------|
| .adf → .dms (Amiga) | DMS ist ein veraltetes, proprietäres Amiga-Archivformat |
| .st → .msa (Atari ST) | MSA komprimiert, verliert aber STX-Kopierschutzinformationen |
| .tzx → .tap (ZX Spectrum) | TAP verliert Timing- und Signaldaten des Tapes |
| .d64 → .g64 (C64) | G64 ist ein Low-Level-Format, Konvertierung nicht 1:1 |
| Jedes interne Format → anderes internes Format | Emulator-spezifische Inkompatibilitäten, Datenverlust-Risiko |

---

### 3.5 Hybrid- / Sonderfälle

| System | Key | Eingangsformate | Ziel | Policy | Begründung | Risiken |
|--------|-----|-----------------|------|--------|------------|---------|
| Nintendo Switch | SWITCH | .nsp, .xci, .nsz | – | **N** | Kein standardisiertes OSS-Tool. NSZ braucht Keys. Rechtliche Grauzone | Toolchain nicht portabel, nicht hash-verifizierbar |
| PlayStation 3 | PS3 | .iso (encrypted), .pkg, folder-based | – | **N** | Encrypted/Signed Content. Kein verlustfreies portables Zielformat | Decryption = rechtliche Grauzone |
| Nintendo 3DS | 3DS | .3ds, .cia | – | **N** | Formatwechsel braucht AES-Keys. Geschlossenes Ökosystem | Decrypt-Tool-Integration = Sicherheits- und Rechtsproblem |
| PlayStation Vita | VITA | .vpk | – | **N** | App-Container. Kein Standard-Conversion-Pfad | – |
| Nintendo Wii U | WIIU | .wux, .rpx, .wud, .iso | – | **M** | WUX ist bereits komprimiert (WUD→WUX = Sektor-Level-Dedup). kein besseres Zielformat | CHD theoretisch möglich, aber kaum Emulator-Support |
| Xbox | XBOX | .iso (XDVDFS), .xiso | .chd | **M** | CHD via createdvd technisch möglich. Xemu unterstützt mittlerweile CHD | Emulator-Support begrenzt. extract-xiso nicht integriert |
| Xbox 360 | X360 | .iso (XEX), .god | .chd | **M** | CHD via createdvd möglich. Xenia-Support für CHD unsicher | Experimentell, nicht für Standardnutzer |

#### Wii-Sonderfälle im Detail

| Format | Behandlung | Begründung |
|--------|------------|------------|
| .iso (Wii) | → RVZ (Auto) | Standard-Pfad, lossless |
| .wbfs (Wii) | → RVZ (Auto) | DolphinTool konvertiert direkt |
| .wad (WiiWare/VC) | ⛔ Skip | WAD = Channel-/Title-Container, kein Disc-Image. NICHT konvertierbar |
| .nkit.iso (Wii) | → RVZ (ManualOnly) | Lossy-Quelle. Padding bereits verloren |
| .dol (Wii Homebrew) | ⛔ Skip | Executable, kein Image |

#### NKit-Warnung (GC + Wii)

NKit-Dateien (.nkit.iso, .nkit.gcz) haben irreversibel Junk-/Padding-Daten entfernt.
Das bedeutet:
1. NKit→RVZ funktioniert technisch
2. Das RVZ-Ergebnis funktioniert in Dolphin
3. Aber: Das RVZ ist NICHT bit-identisch mit einem RVZ aus dem Original-ISO
4. DAT-Matching gegen Redump ist damit nicht möglich10

**Romulus-Verhalten:**
- Erkennung: Dateiname enthält `.nkit.` ODER NKit-Header-Signatur
- ConversionSafety = Risky
- SourceIntegrity = Lossy
- Policy = ManualOnly mit explizitem Warning an den Nutzer

---

## 4. Systeme ohne sinnvolle Auto-Conversion

| System | Key | Grund |
|--------|-----|-------|
| **Arcade** | ARCADE | Set-basiert, MAME-version-gebunden, Parent/Clone-Ketten |
| **Neo Geo AES/MVS** | NEOGEO | Set-basiert wie Arcade |
| **Nintendo Switch** | SWITCH | Kein OSS-Tool, Keys nötig, rechtliche Grauzone |
| **PlayStation 3** | PS3 | Encrypted Content, kein portables Zielformat |
| **Nintendo 3DS** | 3DS | Formatwechsel braucht AES-Decrypt-Keys |
| **PlayStation Vita** | VITA | App-Container ohne Conversion-Standard |
| **MS-DOS** | DOS | Verzeichnisbasiert, kein standardisierbares Archivformat |

### Warum diese 7 Systeme blockiert sind

1. **ARCADE + NEOGEO:** Das ZIP/CHD IST das Endformat. Jedes Repackaging zerstört die Set-Struktur und bricht die DAT-Bindung.
2. **SWITCH + 3DS + PS3 + VITA:** Alle erfordern proprietäre oder rechtlich problematische Tools für Formatwechsel. Romulus hat keine Möglichkeit, die Integrität des Outputs zu verifizieren.
3. **DOS:** Keine Einzeldatei-Konvertierung möglich. Spiele sind Verzeichnisbäume mit EXE + DATA + CONFIG.

---

## 5. Besonders riskante Konvertierungen

| # | Pfad | Risiko | Warum riskant | Empfohlene Einschränkung |
|---|------|--------|---------------|--------------------------|
| R-1 | **NKit → RVZ** | Irreversibler Datenverlust bereits in Quelle | NKit hat Padding/Junk entfernt. RVZ funktioniert, ist aber nicht Redump-kompatibel | ManualOnly + Lossy-Warning |
| R-2 | **CDI → CHD** (Dreamcast) | Unvollständiges Image möglich | DiscJuggler-Format hat oft abgeschnittene Tracks (besonders bei High-Density-Area) | ManualOnly + Warnung "CDI kann unvollständig sein" |
| R-3 | **CSO → CHD** (PSP) | Zwischenschritt-Abhängigkeit | ciso/maxcso muss verfügbar sein. Padding wurde gestripped, ISO-Inhalt aber funktional korrekt | Auto (wenn ciso verfügbar), WARNING: Lossy-Quelle |
| R-4 | **PBP → CHD** (PS1/PSP) | Multi-Step + Lossy-Quelle | PBP ist ein PSP-Eboot-Container mit Re-Encoding. Encrypted PBPs → Block | Auto (wenn psxtract verfügbar), Block bei Encryption |
| R-5 | **PS2 CD als DVD behandelt** | Falsche chdman-Kommando-Wahl | ~15% PS2-Spiele sind CD-basiert (<700 MB). createdvd für CD = Fehler oder defekter CHD | Grössenbasierte Heuristik oder SYSTEM.CNF-Analyse |
| R-6 | **Xbox ISO → CHD** | Minimaler Emulator-Support | CHD technisch korrekt, aber nur Xemu unterstützt es. Andere Emulatoren erwarten raw XISO | ManualOnly + Hinweis "experimentell" |
| R-7 | **WiiU WUX → CHD** | Kein standardisiertes Zielformat | WUX ist bereits komprimiert. CHD wäre eine weitere Schicht ohne klaren Vorteil | ManualOnly + Hinweis "kein Standardpfad" |
| R-8 | **Lossy→Lossy** (CSO→WBFS, NKit→GCZ) | Doppelter Qualitätsverlust | Konvertierung zwischen zwei Lossy-Formaten akkumuliert Datenverlust | ⛔ Blockiert – nur Richtung Lossless-Target erlaubt |
| R-9 | **MDF/MDS → CHD** | Tool nicht integriert | mdf2iso/mds2iso nicht in Romulus. Manueller Zwischenschritt nötig | ManualOnly |
| R-10 | **Multi-Disc-Set (m3u)** | Atomizitäts-Risiko | m3u + N Disc-Images müssen als Einheit konvertiert werden. Teilconversion = inkonsistentes Set | Atomische Konvertierung aller referenzierten Discs |

---

## 6. Empfohlene Prioritätsreihenfolge für Implementierung

### Phase 1: Sichere Kern-Pfade (höchster Impact, geringstes Risiko)

| Prio | System/Gruppe | Pfad | Nutzeranzahl | Begründung |
|------|---------------|------|-------------|------------|
| 1 | PS1, PS2, PSP | cue/bin → CHD, iso → CHD | Sehr hoch | Grösste ROM-Sammlungen. chdman ist battle-tested |
| 2 | GC, Wii | iso/wbfs → RVZ | Hoch | Wii-/GC-ISOs sind riesig (4-8 GB). RVZ spart ~60% |
| 3 | SAT, DC, SCD, PCECD | cue/bin → CHD | Mittel-Hoch | Alle identischer Pfad (chdman createcd) |
| 4 | NES, SNES, N64, GB/GBC/GBA, NDS, MD, SMS, GG | ROM → ZIP | Sehr hoch | Trivial, null Risiko, hohe Dateizahlen |

### Phase 2: Erweiterte Pfade (mittlerer Impact, kontrolliertes Risiko)

| Prio | System/Gruppe | Pfad | Begründung |
|------|---------------|------|------------|
| 5 | NEOCD, 3DO, JAGCD, PCFX, CD32, CDI, FMTOWNS | cue/bin → CHD | Identischer chdman-Pfad, aber seltenere Systeme |
| 6 | Alle fehlenden Cartridge (32X, A26-A78, JAG, LYNX, COLECO, INTV, VB, WS/WSC, NGP/NGPC, etc.) | ROM → ZIP | Trivial. Viele kleine Systeme auf einmal abdecken |
| 7 | Alle Computer (AMIGA, C64, A800, ATARIST, ZX, MSX, CPC) | Disk-Images → ZIP | ZIP-Wrapping, kein Risiko |
| 8 | PSP (CSO→CHD, PBP→CHD) | Mehrstufige Pipelines | ciso + psxtract als Zwischenschritte |

### Phase 3: Riskante/Seltene Pfade (geringer Impact, höheres Risiko)

| Prio | System/Gruppe | Pfad | Begründung |
|------|---------------|------|------------|
| 9 | GC/Wii NKit | .nkit → RVZ (ManualOnly) | Lossy-Quelle, Nutzer-Warnung, nicht für Automatik |
| 10 | DC (CDI-Input) | .cdi → CHD (ManualOnly) | Risky wegen abgeschnittener Tracks |
| 11 | Xbox, Xbox 360 | .iso → CHD (ManualOnly) | Experimentell, minimaler Emulator-Support |
| 12 | Wii U | WUX → CHD (ManualOnly) | Kein Standardpfad, rein experimentell |

### Phase 4: Datenmodell + Architektur (parallel zu allen Phasen)

| Prio | Aufgabe | Begründung |
|------|---------|------------|
| A | ConversionPolicy in consoles.json für alle 65 Systeme | Eliminiert stilles Skip-Verhalten |
| B | ARCADE/NEOGEO aus DefaultBestFormats entfernen | Aktiver Bug-Fix |
| C | Single Source of Truth für Format-Mappings | FormatConverterAdapter + FeatureService synchronisieren |
| D | PS2 CD/DVD-Heuristik implementieren | Direkte Fehler-Vermeidung |
| E | RVZ-Verifizierung verbessern | Schwachstelle in Qualitätssicherung |

---

## 7. Tool-Matrix

| Tool | Funktion | Betroffene Systeme | Lizenz | Romulus-Status | Verify-Methode | Risiken |
|------|----------|-------------------|--------|----------------|----------------|---------|
| **chdman** | CD/DVD/HDD → CHD; CHD verify | PS1, PS2, PSP, SAT, DC, SCD, PCECD, NEOCD, 3DO, JAGCD, PCFX, CD32, CDI, FMTOWNS, (Xbox, X360) | BSD-3 (MAME) | ✅ Integriert + Hash | `chdman verify -i <file>` → Prüft SHA1 + Hunk-Integrität | Timeout bei grossen DVD-Images (>4 GB). PATH-Poisoning mitigiert |
| **dolphintool** | ISO/GCM/WBFS/GCZ/WIA → RVZ | GC, WII | GPL-2 (Dolphin) | ✅ Integriert + Hash | Magic-Byte RVZ\x01 (SCHWACH) | Kein echtes Verify-Kommando. Korrumpierte RVZ passieren Check |
| **7z** (7-Zip) | Archivierung (ZIP/7z), Extraktion, Test | Alle Cartridge, Computer, Archiv-Extraktion | LGPL-2.1 | ✅ Integriert + Hash | `7z t -y <file>` → CRC32-Check | Minimale Risiken. 7z-Format nicht von allen Emulatoren unterstützt |
| **psxtract** | PBP → CUE/BIN | PS1, PSP | Open Source | ✅ Integriert + Hash | Nachgelagert: chdman verify auf Output | Encrypted PBPs → Fehlschlag. Kein eigener Exit-Code-Standard |
| **ciso** | CSO ↔ ISO | PSP | MIT | ✅ Integriert + Hash | Nachgelagert: Dateigrösse ISO = erwartete UMD-Grösse | CSO→ISO = nur Decompression, kein Daten-Check |
| **maxcso** | CSO ↔ ISO (besser als ciso) | PSP | MIT | ❌ Nicht integriert | Wie ciso | Könnte ciso ersetzen – bessere Kompression, schneller |
| **mdf2iso** | MDF/MDS → ISO | Alle CD-Systeme (selten) | Open Source | ❌ Nicht integriert | SHA1-Vergleich von ISO-Output | Seltene Quelle, niedrige Priorität |
| **nrg2iso** | NRG (Nero) → ISO | Alle CD-Systeme (selten) | Open Source | ❌ Nicht integriert | SHA1-Vergleich von ISO-Output | Sehr seltene Quelle |
| **nkit** | NKit → ISO (Rebuild) | GC, WII | Open Source | ❌ Nicht integriert | SHA1-Vergleich nach Rebuild | Rebuild nur bei verfügbarem Original-Junk |
| **extract-xiso** | Xbox ISO Extraktion/Erstellung | XBOX, X360 | Open Source | ❌ Nicht integriert | Datei-Listing-Vergleich | Nötig nur bei Xbox-ManualOnly-Pfad |
| **wit/wwt** | WBFS ↔ ISO | WII | GPL | ❌ Nicht integriert | SHA1-Vergleich | DolphinTool deckt WBFS→RVZ direkt ab |

### Tool-Verfügbarkeit und Fallback

| Situation | Verhalten |
|-----------|-----------|
| Pflicht-Tool (chdman/dolphintool/7z) fehlt | ConversionResult = Skipped, Reason = "tool-not-found:{name}" |
| Pflicht-Tool Hash stimmt nicht | ConversionResult = Error, Reason = "tool-hash-mismatch:{name}" |
| Optionales Zwischenschritt-Tool fehlt (ciso für CSO→CHD) | ConversionResult = Skipped, Reason = "intermediate-tool-missing:{name}" |
| Neues MAME-Release → chdman-Hash ändert sich | tool-hashes.json muss aktualisiert werden. Kein automatischer Download |

---

## 8. Top 20 wichtigste Conversion-Pfade

Priorisiert nach: Häufigkeit × Impact × Risikokontrolle × Implementierungsaufwand

| Rang | Pfad | System(e) | Typ | Safety | Begründung |
|------|------|-----------|-----|--------|------------|
| 1 | **cue/bin → CHD** | PS1 | Lossless | 🟢 Safe | Grösste Sammlung disc-basierter ROMs weltweit. chdman-Standard-Pfad |
| 2 | **iso → CHD** (createdvd) | PS2 | Lossless | 🟢 Safe | Zweitgrösste Sammlung. DVD-Pfad hochfrequent |
| 3 | **iso → RVZ** | Wii | Lossless | 🟢 Safe | Wii-ISOs sind 4.7-8.5 GB. RVZ spart ~60% = massiver Platzvorteil |
| 4 | **iso → RVZ** | GC | Lossless | 🟢 Safe | GC-ISOs sind 1.4 GB. RVZ spart ~50% |
| 5 | **ROM → ZIP** | NES, SNES, N64 | Repackaging | 🟢 Safe | Tausende Dateien pro System. Trivial, null Risiko |
| 6 | **ROM → ZIP** | GB, GBC, GBA | Repackaging | 🟢 Safe | Wie oben. Sehr häufige Systeme |
| 7 | **iso → CHD** (createcd) | PSP | Lossless | 🟢 Safe | UMD-ISOs häufig, CHD gut unterstützt |
| 8 | **cue/bin → CHD** | SAT | Lossless | 🟢 Safe | Saturn-CUE/BIN sind gross (600 MB+). CHD spart viel Platz |
| 9 | **gdi → CHD** | DC | Lossless | 🟢 Safe | GDI ist der einzige vollständige DC-Dump. CHD = Standardziel |
| 10 | **ROM → ZIP** | MD, SMS, GG | Repackaging | 🟢 Safe | Häufige Sega-Systeme |
| 11 | **cue/bin → CHD** | SCD | Lossless | 🟢 Safe | Sega-CD-Sammlung profitiert stark |
| 12 | **ROM → ZIP** | NDS | Repackaging | 🟢 Safe | NDS-ROMs bis 512 MB – ZIP-Kompression lohnt sich sehr |
| 13 | **wbfs → RVZ** | Wii | Lossless | 🟢 Safe | WBFS war Wii-Standard. Upgrade auf RVZ = Modernisierung |
| 14 | **cue/bin → CHD** | PCECD | Lossless | 🟢 Safe | TurboGrafx-CD, solide Community |
| 15 | **cso → CHD** | PSP | Acceptable | 🟡 | CSO ist häufig bei PSP. Braucht ciso-Zwischenschritt |
| 16 | **cue/bin → CHD** | NEOCD | Lossless | 🟢 Safe | Neo Geo CD, kleinere Sammlung aber konsistenter Pfad |
| 17 | **gcz → RVZ** | GC, Wii | Lossless | 🟢 Safe | Älteres Dolphin-Format upgraden |
| 18 | **pbp → CHD** | PS1/PSP | Acceptable | 🟡 | PBP-Dateien häufig in PSP/PS-Store-Dumps |
| 19 | **ROM → ZIP** | PCE, 32X, A26-A78, JAG, LYNX | Repackaging | 🟢 Safe | Viele kleine Systeme gleichzeitig abdecken |
| 20 | **Disk-Images → ZIP** | AMIGA, C64, ZX, ATARIST, A800 | Repackaging | 🟢 Safe | Computer-ROMs archivieren, nicht transformieren |

---

## Anhang A: Vollständige ConversionPolicy pro System

Referenz-Tabelle für die Implementierung in consoles.json.

| Key | DisplayName | Policy | Zielformat | Primary Tool | chdman-Cmd | Anmerkung |
|-----|-------------|--------|------------|--------------|------------|-----------|
| 3DO | 3DO Interactive Multiplayer | **Auto** | .chd | chdman | createcd | – |
| 3DS | Nintendo 3DS | **None** | – | – | – | Decrypt-Keys nötig |
| 32X | Sega 32X | **ArchiveOnly** | .zip | 7z | – | – |
| A26 | Atari 2600 | **ArchiveOnly** | .zip | 7z | – | – |
| A52 | Atari 5200 | **ArchiveOnly** | .zip | 7z | – | – |
| A78 | Atari 7800 | **ArchiveOnly** | .zip | 7z | – | – |
| A800 | Atari 8-bit (800/XL/XE) | **ArchiveOnly** | .zip | 7z | – | – |
| AMIGA | Commodore Amiga | **ArchiveOnly** | .zip | 7z | – | ADF = Floppy-Dump |
| ARCADE | Arcade | **None** | – | – | – | Set-basiert |
| ATARIST | Atari ST | **ArchiveOnly** | .zip | 7z | – | STX = Kopierschutz |
| C64 | Commodore 64 | **ArchiveOnly** | .zip | 7z | – | – |
| CD32 | Amiga CD32 | **Auto** | .chd | chdman | createcd | – |
| CDI | Philips CD-i | **Auto** | .chd | chdman | createcd | .cdi-Input = ManualOnly |
| CHANNELF | Fairchild Channel F | **ArchiveOnly** | .zip | 7z | – | – |
| COLECO | ColecoVision | **ArchiveOnly** | .zip | 7z | – | – |
| CPC | Amstrad CPC | **ArchiveOnly** | .zip | 7z | – | – |
| DC | Sega Dreamcast | **Auto** | .chd | chdman | createcd | GDI bevorzugt als Quelle |
| DOS | MS-DOS | **None** | – | – | – | Verzeichnisbasiert |
| FMTOWNS | FM Towns / Marty | **Auto** | .chd | chdman | createcd | – |
| GB | Nintendo Game Boy | **ArchiveOnly** | .zip | 7z | – | – |
| GBA | Game Boy Advance | **ArchiveOnly** | .zip | 7z | – | – |
| GBC | Game Boy Color | **ArchiveOnly** | .zip | 7z | – | – |
| GC | Nintendo GameCube | **Auto** | .rvz | dolphintool | – | NKit = ManualOnly |
| GG | Sega Game Gear | **ArchiveOnly** | .zip | 7z | – | – |
| INTV | Mattel Intellivision | **ArchiveOnly** | .zip | 7z | – | – |
| JAG | Atari Jaguar | **ArchiveOnly** | .zip | 7z | – | – |
| JAGCD | Atari Jaguar CD | **Auto** | .chd | chdman | createcd | Selten |
| LYNX | Atari Lynx | **ArchiveOnly** | .zip | 7z | – | – |
| MD | Sega Mega Drive / Genesis | **ArchiveOnly** | .zip | 7z | – | .bin = ambiguous |
| MSX | MSX | **ArchiveOnly** | .zip | 7z | – | – |
| N64 | Nintendo 64 | **ArchiveOnly** | .zip | 7z | – | Byte-Order nicht normalisieren |
| NDS | Nintendo DS | **ArchiveOnly** | .zip | 7z | – | Grosse ROMs (bis 512 MB) |
| NEOCD | Neo Geo CD | **Auto** | .chd | chdman | createcd | – |
| NEOGEO | Neo Geo AES/MVS | **None** | – | – | – | Set-basiert wie Arcade |
| NES | Nintendo Entertainment Sys. | **ArchiveOnly** | .zip | 7z | – | – |
| NGP | Neo Geo Pocket | **ArchiveOnly** | .zip | 7z | – | – |
| NGPC | Neo Geo Pocket Color | **ArchiveOnly** | .zip | 7z | – | – |
| ODYSSEY2 | Magnavox Odyssey 2 | **ArchiveOnly** | .zip | 7z | – | – |
| PC98 | NEC PC-98 | **ManualOnly** | .zip | 7z | – | HDD-Images können gross sein |
| PCE | PC Engine / TurboGrafx-16 | **ArchiveOnly** | .zip | 7z | – | HuCard only |
| PCECD | PC Engine CD | **Auto** | .chd | chdman | createcd | – |
| PCFX | NEC PC-FX | **Auto** | .chd | chdman | createcd | – |
| POKEMINI | Pokemon Mini | **ArchiveOnly** | .zip | 7z | – | – |
| PS1 | PlayStation | **Auto** | .chd | chdman | createcd | Referenz-CD-Pfad |
| PS2 | PlayStation 2 | **Auto** | .chd | chdman | createdvd/createcd | CD/DVD-Heuristik nötig |
| PS3 | PlayStation 3 | **None** | – | – | – | Encrypted |
| PSP | PlayStation Portable | **Auto** | .chd | chdman | createcd | CSO/PBP = Sonderpfade |
| SAT | Sega Saturn | **Auto** | .chd | chdman | createcd | – |
| SCD | Sega CD / Mega-CD | **Auto** | .chd | chdman | createcd | – |
| SG1000 | Sega SG-1000 | **ArchiveOnly** | .zip | 7z | – | – |
| SMS | Sega Master System | **ArchiveOnly** | .zip | 7z | – | – |
| SNES | Super Nintendo | **ArchiveOnly** | .zip | 7z | – | – |
| SUPERVISION | Watara Supervision | **ArchiveOnly** | .zip | 7z | – | – |
| SWITCH | Nintendo Switch | **None** | – | – | – | Kein OSS-Tool |
| VB | Virtual Boy | **ArchiveOnly** | .zip | 7z | – | – |
| VECTREX | Vectrex | **ArchiveOnly** | .zip | 7z | – | – |
| VITA | PlayStation Vita | **None** | – | – | – | Kein Conversion-Pfad |
| WII | Nintendo Wii | **Auto** | .rvz | dolphintool | – | WAD = Skip, NKit = ManualOnly |
| WIIU | Nintendo Wii U | **ManualOnly** | – | – | – | Kein Standardziel |
| WS | WonderSwan | **ArchiveOnly** | .zip | 7z | – | – |
| WSC | WonderSwan Color | **ArchiveOnly** | .zip | 7z | – | – |
| X360 | Xbox 360 | **ManualOnly** | .chd | chdman | createdvd | Experimentell |
| X68K | Sharp X68000 | **ManualOnly** | .zip | 7z | – | HDD-Images können gross sein |
| XBOX | Xbox | **ManualOnly** | .chd | chdman | createdvd | Experimentell |
| ZX | ZX Spectrum | **ArchiveOnly** | .zip | 7z | – | TZX-Timing bewahren |

### Policy-Zusammenfassung

| Policy | Anzahl Systeme | Aufzählung |
|--------|---------------|------------|
| **Auto** (Disc-Transformation) | 14 | 3DO, CD32, CDI, DC, FMTOWNS, GC, JAGCD, NEOCD, PCECD, PCFX, PS1, PS2, PSP, SAT, SCD, WII |
| **ArchiveOnly** (ZIP-Wrapping) | 36 | 32X, A26, A52, A78, A800, AMIGA, ATARIST, C64, CHANNELF, COLECO, CPC, GB, GBA, GBC, GG, INTV, JAG, LYNX, MD, MSX, N64, NDS, NES, NGP, NGPC, ODYSSEY2, PCE, POKEMINI, SG1000, SMS, SNES, SUPERVISION, VB, VECTREX, WS, WSC |
| **ManualOnly** | 5 | PC98, WIIU, X360, X68K, XBOX |
| **None** (blockiert) | 7 | 3DS, ARCADE, DOS, NEOGEO, PS3, SWITCH, VITA |
| **Gesamt** | **65** (= 3 fehlen wegen Wii/CDI Dual-Policy) | – |

> **Hinweis:** WII hat Policy=Auto, aber WAD-Dateien und NKit-Quellen werden intern als ManualOnly/Skip behandelt.  
> CDI hat Policy=Auto, aber .cdi-Input (DiscJuggler) wird als ManualOnly behandelt.

---

## Anhang B: Vollständige Eingangsformat → Zielformat-Matrix

Alle erlaubten Conversion-Pfade in einem Blick.

| Quellformat | Ziel | chdman-Cmd | Tool | SourceIntegrity | Safety | Betroffene Systeme | Zwischenschritte |
|-------------|------|------------|------|-----------------|--------|--------------------|------------------|
| .cue/.bin → | .chd | createcd | chdman | Lossless | 🟢 | PS1, SAT, DC, SCD, PCECD, NEOCD, 3DO, JAGCD, PCFX, CD32, CDI, FMTOWNS | – |
| .iso → | .chd | createcd | chdman | Lossless | 🟢 | PS1, SAT, SCD, PCECD, NEOCD, 3DO, JAGCD, PCFX, CD32, CDI, FMTOWNS, PSP | – |
| .iso → | .chd | createdvd | chdman | Lossless | 🟢 | PS2 (≥700 MB), XBOX, X360 | – |
| .iso → | .chd | createcd | chdman | Lossless | 🟢 | PS2 (<700 MB) | Grössenbasierte cd/dvd-Heuristik |
| .gdi+tracks → | .chd | createcd | chdman | Lossless | 🟢 | DC | Alle Tracks im selben Verzeichnis |
| .img → | .chd | createcd | chdman | Lossless | 🟢 | Alle CD-Systeme | Wie .iso |
| .cdi → | .chd | createcd | chdman | Lossy | 🔴 | DC (ManualOnly) | Track-Vollständigkeit ungesichert |
| .cso → | .chd | createcd | chdman | Lossy | 🟡 | PSP | ciso decompress → .iso → chdman |
| .pbp → | .chd | – | psxtract→chdman | Lossy | 🟡 | PS1, PSP | psxtract → .cue/.bin → chdman |
| .iso/.gcm → | .rvz | convert | dolphintool | Lossless | 🟢 | GC, WII | – |
| .wbfs → | .rvz | convert | dolphintool | Lossless | 🟢 | WII | – |
| .gcz/.wia → | .rvz | convert | dolphintool | Lossless | 🟢 | GC, WII | – |
| .nkit.iso → | .rvz | convert | dolphintool | Lossy | 🔴 | GC, WII (ManualOnly) | Lossy-Warning |
| .nkit.gcz → | .rvz | convert | dolphintool | Lossy | 🔴 | GC, WII (ManualOnly) | Lossy-Warning |
| .zip (Archiv+cue/bin) → | .chd | createcd | 7z→chdman | Lossless | 🟢 | Alle CD-Systeme | extract → find cue → chdman |
| .7z (Archiv+cue/bin) → | .chd | createcd | 7z→chdman | Lossless | 🟢 | Alle CD-Systeme | extract → find cue → chdman |
| Lose ROM → | .zip | a -tzip | 7z | Lossless | 🟢 | Alle Cartridge + Computer | – |
| .7z (Cartridge) → | .zip | a -tzip | 7z | Lossless | 🟢 | Alle Cartridge + Computer | extract → repackage |
| .rar (Cartridge) → | .zip | a -tzip | 7z | Lossless | 🟢 | Alle Cartridge + Computer | extract → repackage |

---

## Anhang C: Format-Eigenschaftsreferenz

| Format | Typ | Verlustfrei | Reversibel | Multi-Track | Emulator-Support | Validierung | Langzeitarchiv |
|--------|-----|-------------|------------|-------------|-----------------|-------------|---------------|
| **.chd** (v5) | Disc-Container | ✅ | ✅ extractcd/extracthd | ✅ embedded | Breit: RetroArch, MAME, DuckStation, PCSX2, Mednafen | `chdman verify` → stark | ✅ Offen, dokumentiert |
| **.rvz** | Disc-Container | ✅ | ✅ dolphintool→ISO | Single-Disc | Dolphin | Magic-Byte (schwach) | ✅ Offen |
| **.cue/.bin** | Disc-Roh | ✅ | – (ist Roh) | ✅ Multi-Track via .cue | Universell | Grössenvergleich | ✅ Referenzformat |
| **.gdi** | Disc-Roh (DC) | ✅ | – (ist Roh) | ✅ Track-Verzeichnis | DC-Emulatoren | Track-Existenz | ✅ DC-Referenz |
| **.iso** | Disc-Roh | ✅ | – (ist Roh) | ❌ Single-Track | Universell | SHA1 | ✅ Aber Audio-Tracks fehlen |
| **.wbfs** | Wii-Container | ✅ | ✅ → ISO | Single-Disc | Dolphin, USB-Loader | Dateigrösse | ❌ Veraltet |
| **.gcz** | Dolphin-Container | ✅ | ✅ → ISO | Single-Disc | Dolphin | Header-Check | ❌ Veraltet, durch RVZ ersetzt |
| **.wia** | Dolphin-Container | ✅ | ✅ → ISO | Single-Disc | Dolphin | Header-Check | ❌ Veraltet, durch RVZ ersetzt |
| **.cso** | PSP-Container | Lossy (Padding) | ✅ → ISO | Single-Disc | PPSSPP | Dateigrösse | ❌ Lossy |
| **.pbp** | PSP Eboot | Lossy | ✅ psxtract→cue/bin | Single-Disc | PPSSPP | psxtract-Output | ❌ Lossy + Container |
| **.nkit.iso** | GC/Wii-Stripped | **Lossy** | ❌ (Padding unwiederbringlich) | Single-Disc | Dolphin | Header-Check | ❌ Lossy, nicht Redump-kompatibel |
| **.nkit.gcz** | GC/Wii-Stripped | **Lossy** | ❌ | Single-Disc | Dolphin | Header-Check | ❌ Lossy |
| **.cdi** | DiscJuggler | Lossy (Tracks) | ✅ → cue/bin (verlustbehaftet) | Teilweise | Flycast, Reicast | Track-Analyse | ❌ Oft unvollständig |
| **.zip** | Archiv | ✅ | ✅ extract | N/A | Breit (RetroArch, etc.) | `7z t` → CRC32 | ✅ Universal |
| **.7z** | Archiv | ✅ | ✅ extract | N/A | Eingeschränkt | `7z t` | ✅ Aber weniger Emulator-Support |
| **.rar** | Archiv | ✅ | ✅ extract | N/A | ❌ Kein Emulator | `7z t` | ❌ Proprietär |

---

## Anhang D: Explizite Antworten auf die Prüffragen

### 1. Welche Systeme brauchen überhaupt keine Conversion?

**ARCADE, NEOGEO:** ZIP/CHD IST das Endformat.  
**3DS, SWITCH, PS3, VITA:** Kein sicherer Pfad.  
**DOS:** Verzeichnisbasiert, nicht konvertierbar.

### 2. Welche Systeme profitieren von einheitlichen Zielformaten?

- **Alle CD-Systeme** (12 Systeme) profitieren massiv von CHD → ein Format für alle CD-Disc-Images
- **GC/Wii** profitieren von RVZ → grösstes Platzersparnis (50-60%)
- **Alle Cartridge-Systeme** (30 Systeme) profitieren von ZIP → universell, einfach, DAT-kompatibel

### 3. Welche Systeme haben mehrere gleichwertige Zielformate?

| System | Format A | Format B | Empfehlung |
|--------|----------|----------|------------|
| GC/Wii | .rvz (Dolphin-nativ) | .chd (breiter Support) | **.rvz** – bessere Kompression, Dolphin-Standard |
| PS1 | .chd | .pbp (PSP-kompatibel) | **.chd** – verlustfrei, breiterer Support |
| Xbox/X360 | .chd | .iso (XDVDFS) | **.iso** – bewährter Emulator-Pfad (CHD experimentell) |

### 4. Welche Konvertierungen sind technisch möglich, aber praktisch keine gute Idee?

| Pfad | Warum schlecht |
|------|---------------|
| .chd → .iso → .chd | Sinnloser Round-Trip. Daten identisch, nur CPU-Verschwendung |
| .rvz → .iso → .7z | RVZ IST bereits besser komprimiert als 7z |
| .nkit → .iso (Rebuild) | NKit-Rebuild erzeugt nicht das Original-ISO |
| .cdi → .iso → .chd | CDI-Tracks sind oft unvollständig – Endprodukt ebenfalls |
| Lossy → anderes Lossy | CSO→NKit, NKit→WBFS: akkumulierter Datenverlust |
| ARCADE .zip → .7z | Set-Integrität zerstört. MAME kann kein 7z (FBNeo: teilweise) |

### 5. Welche Konvertierungen sind nur mit Zwischenschritten sauber?

| Pfad | Zwischenschritte | Tools |
|------|------------------|-------|
| .cso → .chd | CSO → ciso → ISO → chdman → CHD | ciso + chdman |
| .pbp → .chd | PBP → psxtract → CUE/BIN → chdman → CHD | psxtract + chdman |
| .zip(cue/bin) → .chd | ZIP → 7z extract → CUE/BIN → chdman → CHD | 7z + chdman |
| .7z(cue/bin) → .chd | 7Z → 7z extract → CUE/BIN → chdman → CHD | 7z + chdman |
| .mdf/.mds → .chd | MDF → mdf2iso → ISO → chdman → CHD | mdf2iso + chdman |
| .rar(ROM) → .zip | RAR → 7z extract → ROM → 7z zip → ZIP | 7z |

### 6. Welche Konvertierungen sollten grundsätzlich blockiert werden?

| Blockierter Pfad | Grund |
|------------------|-------|
| ARCADE .zip → beliebig | Set-Integrität |
| NEOGEO .zip → beliebig | Set-Integrität |
| Lossy → Lossy (z.B. CSO→WBFS, NKit→GCZ) | Doppelter Qualitätsverlust |
| Encrypted PBP/PKG/NSP → beliebig | Decryption = rechtliche/technische Grauzone |
| SWITCH NSP/XCI → beliebig | Kein OSS-Tool |
| PS3 ISO/PKG → beliebig | Encrypted, kein Zielformat |
| 3DS → CIA oder umgekehrt | AES-Keys nötig |
| Bereits im Zielformat → nochmal konvertieren | Sinnloser Round-Trip |

### 7. Welche Tools sind Pflicht?

| Tool | Warum Pflicht | Abgedeckte Systeme |
|------|--------------|-------------------|
| **chdman** | Einziges Tool für CD/DVD → CHD | 14 Disc-Systeme (PS1, PS2, PSP, SAT, DC, etc.) |
| **dolphintool** | Einziges Tool für → RVZ | GC, Wii |
| **7z** | Archivierung, Extraktion, Test | Alle 36 ArchiveOnly-Systeme + Archiv-Extraktion |

### 8. Welche Tools sind riskant oder unzuverlässig?

| Tool | Risiko | Mitigation |
|------|--------|------------|
| **psxtract** | Encrypted PBPs → Fehlschlag. Kein standardisierter Exit-Code | PBP-Encryption vorab prüfen. Exit-Code UND Output parsen |
| **ciso** | Nur CSO-Decompression. Kein Integritäts-Check | Nachgelagerter chdman verify auf das CHD-Ergebnis |
| **mdf2iso** (nicht integriert) | Selten gewartet, Edge-Cases bei Multi-Session-Images | Nur ManualOnly |
| **nrg2iso** (nicht integriert) | Sehr spezifisch, kaum gewartet | Nur ManualOnly |
| **extract-xiso** (nicht integriert) | Xbox-spezifisch, kleine Community | Nur ManualOnly |

### 9. Welche Zielformate eignen sich für Langzeitarchivierung?

| Format | Langzeiteignung | Begründung |
|--------|----------------|------------|
| **.chd** v5 | ✅ Hervorragend | Offen, dokumentiert, MAME-Projekt-gestützt, SHA1 eingebettet |
| **.rvz** | ✅ Gut | Offen, Dolphin-Projekt-gestützt, ZSTD-Kompression |
| **.cue/.bin** | ✅ Hervorragend | Roh-Format, keine Container-Abhängigkeit |
| **.gdi** | ✅ Gut | DC-Standard-Roh-Format |
| **.iso** | ✅ Hervorragend | Universell, keine Container-Abhängigkeit |
| **.zip** | ✅ Hervorragend | Universell, offen, überall extrahierbar |

### 10. Welche Zielformate eignen sich eher für Emulator-/Frontend-Nutzung?

| Format | Emulator-Eignung | Begründung |
|--------|-----------------|------------|
| **.chd** | ✅ Hervorragend | RetroArch, MAME, DuckStation, PCSX2, Mednafen – direkt ladbar |
| **.rvz** | ✅ Hervorragend | Dolphin-Standard – schnellstes Laden |
| **.zip** | ✅ Hervorragend | RetroArch, MAME, FBNeo laden direkt aus ZIP |
| **.pbp** | 🟡 Gut | PPSSPP-Standardformat, aber lossy |
| **.cso** | 🟡 Gut | PPSSPP-Standardformat, aber lossy |
| **.7z** | 🟡 Eingeschränkt | RetroArch ja, viele andere Emulatoren nein |
| **.wbfs** | ❌ Veraltet | Nur USB-Loader. Dolphin unterstützt es, empfiehlt aber RVZ |
