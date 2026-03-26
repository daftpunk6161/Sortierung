# Conversion Product Model – Romulus

**Stand:** 2026-03-21  
**Typ:** Fachmodell (Product Model)  
**Basis:** [CONVERSION_DOMAIN_AUDIT.md](CONVERSION_DOMAIN_AUDIT.md), Ist-Zustand in `FormatConverterAdapter`, `IFormatConverter`, `ConversionModels`, Pipeline-Phasen  
**Zielgruppe:** Engineering, Product, QA

---

## 1. Ziel des Conversion-Features

### Problem
ROM-Sammlungen enthalten dieselben Spiele in vielen unterschiedlichen Formaten – manche gross und unkomprimiert (.iso, .bin), manche veraltet oder proprietär (.rar, .nrg, .mdf), manche suboptimal für die Zielemulation (.wbfs statt .rvz). Nutzer wollen ihre Bibliothek in ein **einheitliches, platzsparendes, emulator-kompatibles Format** überführen, ohne Daten zu verlieren.

### Lösung
Romulus bietet pro System ein **klar definiertes Zielformat** an und konvertiert dorthin – verlustfrei, verifiziert, auditiert, rückgängig machbar. Romulus entscheidet dabei nicht still im Hintergrund, sondern gibt dem Nutzer **volle Transparenz** über das Was, Warum und Wie jeder Konvertierungs-Entscheidung.

### Nicht-Ziel
Romulus ist kein generischer Format-Konverter. Es konvertiert **nur in sichere, emulator-kompatible Zielformate** und blockiert aktiv Konvertierungen, die Daten zerstören, inkonsistent oder unsicher wären.

### Nutzer-Personas

| Persona | Bedarf | Romulus-Feature |
|---|---|---|
| **Sammler** (gross, viele Systeme) | Einheitliches Format pro System, maximale Platzersparnis | Auto-Conversion mit Preview, Batch-Modus |
| **Emulations-Einsteiger** | Verständnis, welches Format das beste ist | Format-Empfehlung pro System, Emulator-Kompatibilitäts-Anzeige |
| **Power-User / Archivar** | Kontrolle über jede Entscheidung, kein stiller Datenverlust | ManualOnly-Policy, Lossy-Warnings, Verification-Reports |
| **CI/Automation** | Headless Batch-Conversion mit Exit-Codes | CLI `--convert-format auto`, API `convertFormat: "auto"` |

### Erfolgskriterien

| KPI | Ziel | Messung |
|---|---|---|
| Format-Abdeckung | 100% der 65 Systeme haben eine explizite ConversionPolicy | `consoles.json` Audit |
| Conversion-Korrektheit | 0 fehlerhafte Konvertierungen ohne Warning/Block | Verification-Pass-Rate |
| Transparency | Jede Conversion-Entscheidung ist im Report/Audit nachvollziehbar | Report-Coverage |
| Reversibilität | 100% der Auto-Conversions rückgängig machbar (Source in Trash) | Undo-Audit |

---

## 2. Fachliche Definitionen

### 2.1 Glossar

| Begriff | Definition |
|---|---|
| **Conversion** | Überführung einer ROM-Datei von einem Quellformat in ein definiertes Zielformat für ein bestimmtes System. Umfasst Image-Transformationen, Archiv-Normalisierung und Zwischenschritte. |
| **ConversionPolicy** | Systemweite Regel, die festlegt, ob und wie ein System konvertiert werden darf. Eines von: `Auto`, `ManualOnly`, `ArchiveOnly`, `None`. |
| **ConversionTarget** | Das technische Ziel einer Konvertierung: Extension + Tool + Command. |
| **ConversionPlan** | Die vor einer Konvertierung berechnete Abfolge von Schritten (ggf. mit Intermediate Steps), die den Weg von Source zu Target beschreibt. |
| **ConversionChain** | Eine Sequenz von Einzelkonvertierungen, wenn der direkte Weg nicht möglich ist. Bsp: `.cso → .iso → .chd` |
| **SourceIntegrity** | Klassifizierung, ob die Quelldatei verlustfreie Rohdaten enthält (`Lossless`) oder bereits Informationsverlust aufweist (`Lossy`, `Unknown`). |
| **VerificationStatus** | Ergebnis der Post-Conversion-Prüfung: `Verified`, `VerifyFailed`, `VerifyNotAvailable`. |
| **ConversionOutcome** | Ergebnis einer einzelnen Konvertierung: `Success`, `Skipped`, `Error`, `Blocked`. |
| **ArchiveNormalization** | Sonderform der Conversion: Verpackung eines unveränderten ROMs in ein standardisiertes Archivformat (ZIP). Kein Inhalts-Eingriff. |
| **DiscImageTransformation** | Echte Format-Konvertierung eines Disc-Images (cue/bin → CHD, ISO → RVZ). Verändert den Container, nicht die Daten. |
| **SetProtected** | Eigenschaft eines Systems, dessen ROMs Set-basiert und versionsgebunden sind (Arcade, Neo Geo). Konvertierung ist verboten. |

### 2.2 ConversionPolicy – Enumeration

```
Auto          → Romulus konvertiert automatisch im Standard-Flow.
                Voraussetzung: Tool verfügbar, Policy erfüllt, Verify bestanden.

ArchiveOnly   → Romulus packt die Datei nur in ZIP.
                KEIN Inhalts-Eingriff.
                Äquivalent zu "Repackaging".

ManualOnly    → Konvertierung ist technisch möglich, aber riskant.
                Nur auf explizite Nutzerbestätigung mit Warning.
                Beispiel: Xbox → CHD (kaum Emulator-Support), NKit → RVZ (lossy source).

None          → Konvertierung ist bewusst blockiert.
                System ist entweder set-basiert, verschlüsselt, oder es gibt keinen
                sicheren Zielformat-Pfad.
                Wird dem Nutzer transparent als "Nicht konvertierbar" angezeigt.
```

### 2.3 SourceIntegrity – Klassifizierung der Quelle

| Klasse | Bedeutung | Beispiele |
|---|---|---|
| `Lossless` | Quelldatei enthält vollständige, unveränderte Rohdaten | .cue/.bin, .iso, .gdi, .gcm, .wbfs, .gcz, .wia, ungepackte ROMs |
| `Lossy` | Quelldatei hat irreversiblen Informationsverlust | .nkit.iso, .nkit.gcz, .cso (Padding stripped), .pbp (re-encoded), .cdi (gekürzt) |
| `Unknown` | Integrität kann nicht sicher bestimmt werden | Unbekannte Container, beschädigte Dateien |

### 2.4 ConversionSafety – Bewertung eines Conversion-Pfads

| Stufe | Bedeutung | Bedingung |
|---|---|---|
| `Safe` | Verlustfrei, verifizierbar, reversibel | SourceIntegrity = Lossless UND Zielformat verlustfrei UND Verify = vorhanden |
| `Acceptable` | Funktional korrekt, aber Source war bereits lossy | SourceIntegrity = Lossy UND Conversion technisch korrekt |
| `Risky` | Ergebnis kann unvollständig oder inkompatibel sein | .cdi-Input, ManualOnly-Systeme, fehlende Verifizierung |
| `Blocked` | Nicht erlaubt | Policy = None, SetProtected, UNKNOWN ConsoleKey |

---

## 3. Conversion-Arten

### 3.1 Taxonomie

| Conversion-Art | Beschreibung | Source-Typ | Target-Typ | Inhalts-Eingriff | Reversibel |
|---|---|---|---|---|---|
| **DiscImageTransformation** | Disc-Image in komprimierten, verlustfreien Container | .cue/.bin, .iso, .gdi, .img | .chd, .rvz | Nein (lossless Recompression) | ✅ Ja |
| **ArchiveNormalization** | Unveränderte Datei in Standardarchiv packen | beliebiges ROM | .zip | Nein | ✅ Ja |
| **ArchiveRepackaging** | Archiv-Container wechseln (RAR→ZIP, 7z→ZIP) | .rar, .7z | .zip | Nein | ✅ Ja |
| **IntermediateDecompression** | Lossy/komprimierten Container in Rohformat entpacken als Zwischenschritt | .cso, .pbp | .iso, .cue/.bin | Nein (Decompression) | Teils (Padding fehlt) |
| **ChainedConversion** | Mehrere Schritte: Decompress → Transform | .cso → .iso → .chd | .chd | Nein (Kette) | ✅ (bis auf Padding) |
| **MultiFileAssembly** | Mehrere Dateien zu einem Container zusammenfügen | .cue + .bin-Tracks, .gdi + Tracks | .chd | Nein (Packaging) | ✅ Ja |
| **ArchiveExtractAndConvert** | ZIP/7z extrahieren, Disc-Image finden, konvertieren | .zip/.7z (enthält .cue/.bin) | .chd | Nein (Extract + Transform) | ✅ Ja |

### 3.2 Conversion-Graph (Erlaubte Pfade)

```
┌──────────────────────────────────────────────────────────────────┐
│  DISC-BASIERTE SYSTEME                                           │
│                                                                  │
│  .cue/.bin ──────────→ .chd  (chdman createcd)                  │
│  .gdi + tracks ──────→ .chd  (chdman createcd)                  │
│  .iso ───────────────→ .chd  (chdman createcd/createdvd)        │
│  .img ───────────────→ .chd  (chdman createcd/createdvd)        │
│  .cdi ──── ⚠️ ───────→ .chd  (chdman createcd)   [ManualOnly]   │
│                                                                  │
│  .cso ──→ .iso ──────→ .chd  (ciso + chdman)                   │
│  .pbp ──→ .cue/.bin ─→ .chd  (psxtract + chdman)               │
│                                                                  │
│  .zip/.7z (disc) ──extract──→ .cue/.bin/.iso ──→ .chd           │
│                                                                  │
│  .iso/.gcm ──────────→ .rvz  (dolphintool)          [GC/Wii]   │
│  .wbfs ──────────────→ .rvz  (dolphintool)          [Wii]      │
│  .gcz ───────────────→ .rvz  (dolphintool)          [GC/Wii]   │
│  .wia ───────────────→ .rvz  (dolphintool)          [GC/Wii]   │
│  .nkit.* ── ⚠️ ──────→ .rvz  (dolphintool)  [Lossy-Warning]    │
│                                                                  │
├──────────────────────────────────────────────────────────────────┤
│  CARTRIDGE / COMPUTER (ArchiveOnly)                              │
│                                                                  │
│  .nes/.sfc/.gba/.gb/etc. ──→ .zip  (7z a -tzip)                │
│  .adf/.d64/.tzx/.st/etc. ──→ .zip  (7z a -tzip)                │
│  .rar (beliebig) ──extract──→ .zip  (7z a -tzip)               │
│  .7z (beliebig) ──extract───→ .zip  (7z a -tzip)               │
│                                                                  │
├──────────────────────────────────────────────────────────────────┤
│  BLOCKIERT                                                       │
│                                                                  │
│  ARCADE .zip / .7z               ──→ ❌ (SetProtected)          │
│  NEOGEO .zip / .7z               ──→ ❌ (SetProtected)          │
│  SWITCH .nsp / .xci / .nsz       ──→ ❌ (None)                  │
│  PS3 .iso / .pkg / folder        ──→ ❌ (None)                  │
│  3DS .3ds / .cia                  ──→ ❌ (None)                  │
│  VITA .vpk                        ──→ ❌ (None)                  │
│  UNKNOWN ConsoleKey               ──→ ❌ (Blocked)              │
└──────────────────────────────────────────────────────────────────┘
```

### 3.3 Verbotene Konvertierungen (Explizit)

| Von | Nach | Grund |
|---|---|---|
| .chd | .iso / .cue/.bin | Rückkonvertierung – kein Feature von Romulus, nur Archiv-Tools |
| .rvz | .iso | Rückkonvertierung – nicht Romulus-Scope |
| .zip (Arcade-Set) | .zip (repack) | Set-Integrität würde gebrochen |
| .nkit.iso | .iso | NKit→ISO-Rebuild erfordert Spezial-Tool, kein Romulus-Feature |
| ROM → anderer ROM-Typ | z.B. .smc → .sfc, .v64 → .z64 | Byte-Order- oder Header-Manipulation ist kein Conversion-Feature |
| Irgendwas | Irgendein lossy Format | Romulus konvertiert NIE in ein lossy Zielformat (CSO, NKit, PBP) |

---

## 4. Zielbild pro Plattformfamilie

### 4.1 Zielformat-Tabelle (normativ)

Jedes System hat exakt **eine ConversionPolicy** und maximal **ein bevorzugtes Zielformat**.

#### CD-basierte Disc-Systeme → CHD

| System | Policy | Bevorzugt | Alternate | chdman-Modus |
|---|---|---|---|---|
| PS1 | Auto | .chd | – | createcd |
| SAT | Auto | .chd | – | createcd |
| DC | Auto | .chd | – | createcd |
| SCD | Auto | .chd | – | createcd |
| PCECD | Auto | .chd | – | createcd |
| NEOCD | Auto | .chd | – | createcd |
| 3DO | Auto | .chd | – | createcd |
| JAGCD | Auto | .chd | – | createcd |
| PCFX | Auto | .chd | – | createcd |
| CD32 | Auto | .chd | – | createcd |
| CDI (Philips) | Auto | .chd | – | createcd |
| FMTOWNS | Auto | .chd | – | createcd |

#### DVD/UMD-basierte Disc-Systeme → CHD

| System | Policy | Bevorzugt | chdman-Modus | Anmerkung |
|---|---|---|---|---|
| PS2 | Auto | .chd | **createcd ODER createdvd** | Abhängig von Image-Grösse (<700 MB = CD) |
| PSP | Auto | .chd | createcd | UMD-Sektorgrösse ist CD-kompatibel |

#### GameCube / Wii → RVZ

| System | Policy | Bevorzugt | Tool | Anmerkung |
|---|---|---|---|---|
| GC | Auto | .rvz | dolphintool | NKit-Quellen: Lossy-Warning |
| WII | Auto | .rvz | dolphintool | WAD-Dateien: Skip (nicht konvertierbar) |

#### Cartridge-Systeme → ZIP (ArchiveOnly)

Betrifft: NES, SNES, N64, GB, GBC, GBA, NDS, MD, SMS, GG, PCE, 32X, A26, A52, A78, JAG, LYNX, COLECO, INTV, VB, WS, WSC, NGP, NGPC, SG1000, POKEMINI, VECTREX, CHANNELF, ODYSSEY2, SUPERVISION

| Policy | Bevorzugt | Alternate | Anmerkung |
|---|---|---|---|
| ArchiveOnly | .zip | – | Kein Inhalts-Eingriff. ROM bleibt unverändert im Archiv. |

#### Computer → ZIP (ArchiveOnly)

Betrifft: AMIGA, C64, A800, ATARIST, ZX, MSX, CPC

| Policy | Bevorzugt | Anmerkung |
|---|---|---|
| ArchiveOnly | .zip | Disk/Tape-Formate (.adf, .d64, .tzx, .st) NICHT transformieren |

#### ManualOnly-Systeme

| System | Policy | Potentielles Ziel | Grund für ManualOnly |
|---|---|---|---|
| XBOX | ManualOnly | .chd (createdvd) | Kaum Emulator-Support für CHD |
| X360 | ManualOnly | .chd (createdvd) | Kaum Emulator-Support für CHD |
| WIIU | ManualOnly | – | Kein standardisiertes Zielformat |
| PC98 | ManualOnly | .zip | HDD-Images können sehr gross sein |
| X68K | ManualOnly | .zip | Heterogene Formate |

#### None-Systeme (blockiert)

| System | Grund |
|---|---|
| ARCADE | Set-basiert, versioniert (SetProtected) |
| NEOGEO | Set-basiert, versioniert (SetProtected) |
| SWITCH | Kein Open-Source-Tool, Decrypt-Keys nötig |
| PS3 | Encrypted/signed, kein portables Zielformat |
| 3DS | Decrypt-Keys nötig |
| VITA | App-Container, kein Standard-Conversion-Pfad |
| DOS | Directory-basiert, keine standardisierte Konvertierung |

### 4.2 Ziel-Zustand aus Nutzersicht

Nach einem vollständigen Conversion-Run soll die Bibliothek so aussehen:

| Plattformfamilie | Idealzustand |
|---|---|
| CD-Disc-Systeme | Alle Disc-Images als .chd, verifiziert via `chdman verify` |
| GC/Wii | Alle Images als .rvz, verifiziert via Magic-Byte (+künftig dolphintool verify) |
| Cartridge-Systeme | Alle ROMs in .zip, verifiziert via `7z t` |
| Computer-Systeme | Alle Disk/Tape-Images in .zip, unverändert |
| Arcade / Set-basiert | Unberührt |
| Blocked-Systeme | Unberührt, im Report als "keine Konvertierung" ausgewiesen |

---

## 5. Conversion-Regeln

### 5.1 Vorbedingungen für jede Konvertierung

| Regel | Beschreibung | Prüfzeitpunkt |
|---|---|---|
| **R-01 ConsoleKey required** | Kein Conversion ohne verifiziertes ConsoleKey. UNKNOWN → Block. | Vor Plan-Erstellung |
| **R-02 Policy check** | ConversionPolicy des Systems muss ≠ None sein. | Vor Plan-Erstellung |
| **R-03 Tool available** | Benötigtes Tool muss gefunden und Hash-verifiziert sein. | Vor Execution |
| **R-04 No self-conversion** | Wenn Source-Extension = Target-Extension → Skip. | Vor Execution |
| **R-05 No overwrite** | Wenn Zieldatei bereits existiert → Skip. | Vor Execution |
| **R-06 ManualOnly confirmation** | Policy = ManualOnly → explizite Nutzerbestätigung nötig. | Vor Execution (GUI: Dialog, CLI: `--confirm`, API: `"confirmed": true`) |
| **R-07 Lossy-Source warning** | SourceIntegrity = Lossy → Warning-Flag im ConversionResult + UI-Hinweis. | Bei Plan-Erstellung |
| **R-08 Set-Protection** | ARCADE / NEOGEO → immer Block, unabhängig von Policy-Override. | Hardcoded Rule |
| **R-09 Multi-File atomicity** | m3u-Sets / cue+bin-Paare → alle Dateien als Einheit konvertieren oder keine. | Plan-Phase |
| **R-10 Post-Verify mandatory** | Jede erfolgreiche Konvertierung MUSS verifiziert werden. Kein Verify = kein Move-to-Trash. | Nach Execution |

### 5.2 Conversion-Plan-Erstellung

Bevor konvertiert wird, muss Romulus einen **ConversionPlan** erstellen:

```
ConversionPlan:
  ├── FileId
  ├── SourcePath
  ├── SourceFormat             (extension)
  ├── SourceIntegrity          (Lossless | Lossy | Unknown)
  ├── ConsoleKey
  ├── ConversionPolicy         (Auto | ArchiveOnly | ManualOnly | None)
  ├── TargetFormat             (extension)
  ├── ConversionType           (DiscImageTransformation | ArchiveNormalization | ...)
  ├── ConversionSafety         (Safe | Acceptable | Risky | Blocked)
  ├── IntermediateSteps[]      (optional: Decompression, Extraction, ...)
  │   ├── Step 1: CSO → ISO   (tool: ciso, temp file)
  │   └── Step 2: ISO → CHD   (tool: chdman createcd)
  ├── RequiredTools[]          (tool names + availability status)
  ├── EstimatedTargetSize      (Bytes, basierend auf Compression-Ratio)
  ├── Warnings[]               (Lossy-Source, CDI-Incomplete, NKit-PaddingLost, ...)
  ├── Decision                 (Convert | Skip | Block | NeedsConfirmation)
  └── DecisionReason           (human-readable string)
```

Der Plan wird **vor der Execution** erstellt und dem Nutzer als **Preview** gezeigt (DryRun / Preview-Ansicht).

### 5.3 Erlaubte Conversion-Chains

| Chain | Schritte | Bedingung | Safety |
|---|---|---|---|
| Direct Disc→CHD | Source → chdman → .chd | Source ist .cue/.bin, .gdi, .iso, .img | Safe |
| Direct Disc→RVZ | Source → dolphintool → .rvz | Source ist .iso, .gcm, .wbfs, .gcz, .wia | Safe |
| CSO→CHD | .cso → ciso → .iso → chdman → .chd | ciso verfügbar | Acceptable |
| PBP→CHD | .pbp → psxtract → .cue/.bin → chdman → .chd | psxtract verfügbar | Acceptable |
| Archive→CHD | .zip/.7z → extract → .cue/.bin/.iso → chdman → .chd | 7z verfügbar | Safe |
| NKit→RVZ | .nkit.* → dolphintool → .rvz | dolphintool verfügbar | Risky (Lossy-Warning) |
| ROM→ZIP | Source → 7z a -tzip → .zip | 7z verfügbar | Safe |
| RAR→ZIP | .rar → extract → re-zip → .zip | 7z verfügbar | Safe |
| 7z→ZIP | .7z → extract → re-zip → .zip | 7z verfügbar | Safe |

### 5.4 Conversion-Entscheidungsbaum

```
Input: (File, ConsoleKey)
│
├─ ConsoleKey == UNKNOWN?
│  └─ YES → Decision: Block("unknown-system")
│
├─ GetConversionPolicy(ConsoleKey)
│  ├─ None     → Decision: Block("policy-none")
│  ├─ ManualOnly → UserConfirmed?
│  │  ├─ NO   → Decision: NeedsConfirmation
│  │  └─ YES  → continue
│  ├─ ArchiveOnly → goto ARCHIVE_PATH
│  └─ Auto → goto CONVERT_PATH
│
├─ ARCHIVE_PATH:
│  ├─ Already .zip? → Skip("already-target")
│  └─ → Plan: ArchiveNormalization → .zip
│
├─ CONVERT_PATH:
│  ├─ GetConversionTarget(ConsoleKey, SourceExt)
│  │  └─ null? → Skip("no-target-defined")
│  ├─ SourceExt == TargetExt? → Skip("already-target")
│  ├─ TargetFile exists? → Skip("target-exists")
│  ├─ ClassifySourceIntegrity(SourceExt)
│  │  ├─ Lossy → add Warning
│  │  └─ Lossless/Unknown → continue
│  ├─ NeedsIntermediateSteps(SourceExt)?
│  │  ├─ .cso → add Step(ciso decompress)
│  │  ├─ .pbp → add Step(psxtract extract)
│  │  └─ .zip/.7z → add Step(7z extract)
│  ├─ RequiredTools available?
│  │  ├─ NO → Skip("tool-not-found")
│  │  └─ YES → continue
│  └─ → Decision: Convert
```

---

## 6. Safety-/Review-Regeln

### 6.1 Safety-Matrix

| Situation | ConversionSafety | Nutzer-Interaktion | Report-Flag |
|---|---|---|---|
| Lossless Source → Standard-Ziel (CHD/RVZ/ZIP) | Safe | Keine (Auto) | – |
| Lossy Source (.nkit, .cso, .pbp) → Zielformat | Acceptable | Warning in Preview | ⚠️ `lossy-source` |
| .cdi (Dreamcast) → .chd | Risky | ManualOnly-Confirmation | ⚠️ `cdi-incomplete-risk` |
| ManualOnly-System (Xbox, Wii U) | Risky | Explizite Bestätigung | ⚠️ `manual-only` |
| ArchiveOnly-System, aber Datei ist schon .zip | Skip | – | ℹ️ `already-target` |
| UNKNOWN ConsoleKey | Blocked | Nicht möglich | 🛑 `unknown-system` |
| SetProtected (ARCADE/NEOGEO) | Blocked | Nicht möglich | 🛑 `set-protected` |
| Policy = None (Switch, PS3, 3DS, Vita) | Blocked | Nicht möglich | 🛑 `policy-none` |
| Tool fehlt | Blocked (temporär) | Tool-Install-Hinweis | 🛑 `tool-missing` |
| Verify fehlgeschlagen | Error | Source NICHT in Trash, korrupte Zieldatei gelöscht | 🔴 `verify-failed` |

### 6.2 Verify-Pflichten

| Zielformat | Verifikations-Methode | Implementiert? | Stärke |
|---|---|---|---|
| .chd | `chdman verify -i <file>` | ✅ Ja | Stark (prüft Checksummen + Integrität) |
| .rvz | Magic-Byte-Check (RVZ\x01) + Dateigrösse >4B | ✅ Ja | Schwach (nur Header-Plausibilität) |
| .zip | `7z t -y <file>` | ✅ Ja | Mittel (prüft CRC32 pro Entry) |

**Offener Bedarf:** RVZ-Verifikation muss verstärkt werden (Issue nötig).

### 6.3 Rollback / Undo-Vertrag

| Schritt | Verhalten |
|---|---|
| Conversion erfolgreich + Verify bestanden | Original-Datei → Trash (mit Audit-Eintrag) |
| Conversion erfolgreich + Verify fehlgeschlagen | Zieldatei löschen, Original bleibt unverändert |
| Conversion fehlgeschlagen | Partielle Zieldatei löschen (best-effort), Original bleibt unverändert |
| Nutzer will Undo | Trash-Restore des Originals + Löschung der konvertierten Datei |

---

## 7. Reports / Status / UX-relevante Outputs

### 7.1 ConversionPlan-Preview (Pre-Run)

**Zweck:** Nutzer sieht vor der Ausführung, was Romulus konvertieren, skippen und blockieren wird.

| Feld | Beschreibung |
|---|---|
| Gesamtanzahl Dateien | Im Conversion-Scope |
| Konvertierbar (Auto) | Dateien, die ohne Interaktion konvertiert werden |
| Konvertierbar (ManualOnly) | Dateien, die Bestätigung brauchen |
| Lossy-Warnung | Dateien mit SourceIntegrity = Lossy |
| Blockiert | Dateien, die aus Policy-Gründen nicht konvertiert werden |
| übersprungen | Dateien, die bereits im Zielformat sind |
| Geschätzte Platzersparnis | SummeQuellgrösse − SummeGeschätzteZielgrösse |
| Fehlende Tools | Tools, die für geplante Konvertierungen nötig wären |

### 7.2 ConversionResult-Summary (Post-Run)

| Metrik | Beschreibung | Bereits in RunProjection? |
|---|---|---|
| `ConvertedCount` | Erfolgreich konvertierte Dateien | ✅ Ja |
| `ConvertErrorCount` | Fehlgeschlagene Konvertierungen | ✅ Ja |
| `ConvertSkippedCount` | Übersprungene Dateien | ✅ Ja |
| `ConvertBlockedCount` | Aus Policy blockierte Dateien | ❌ **Fehlt** |
| `ConvertLossyWarningCount` | Dateien mit Lossy-Source-Warning | ❌ **Fehlt** |
| `ConvertVerifyPassedCount` | Dateien, die Verify bestanden | ❌ **Fehlt** |
| `ConvertVerifyFailedCount` | Dateien mit fehlgeschlagenem Verify | ❌ **Fehlt** |
| `ConvertSavedBytes` | Eingesparter Platz nach Conversion | ❌ **Fehlt** |

### 7.3 ConversionDetail-Report (pro Datei)

| Feld | Beschreibung |
|---|---|
| SourcePath | Pfad der Quelldatei |
| SourceFormat | Extension der Quelle |
| SourceIntegrity | Lossless / Lossy / Unknown |
| TargetFormat | Extension des Ziels |
| ConversionType | DiscImageTransformation / ArchiveNormalization / … |
| ConversionSafety | Safe / Acceptable / Risky / Blocked |
| IntermediateSteps | Liste der Zwischenschritte (falls vorhanden) |
| Outcome | Success / Skipped / Error / Blocked |
| VerificationStatus | Verified / VerifyFailed / VerifyNotAvailable |
| Warnings | Liste aller Warnungen |
| Reason | Menschenlesbarer Grund bei Skip/Block/Error |
| SourceSizeBytes | Grösse der Quelldatei |
| TargetSizeBytes | Grösse der Zieldatei (nach Conversion) |
| SavedBytes | Differenz |

### 7.4 UX-Guidelines für GUI

| Bereich | Anforderung |
|---|---|
| **Preview-Tab** | Vor jedem Conversion-Run: Tabelle mit allen geplanten Konvertierungen, Warnings prominent hervorgehoben |
| **Lossy-Badge** | Dateien mit SourceIntegrity = Lossy bekommen ein sichtbares ⚠️ Badge |
| **Policy-Transparenz** | Für blockierte Systeme: klarer Hinweis "Dieses System unterstützt keine automatische Konvertierung" mit Erklärung |
| **Progress** | Fortschrittsbalken mit aktueller Datei, geschätzter Restzeit, bisheriger Platzersparnis |
| **Post-Run Dashboard** | Conversion-Zusammenfassung: Konvertiert / Fehler / Übersprungen / Blockiert als Kacheln |
| **Verify-Status** | Pro konvertierter Datei: grünes ✓ (Verified), rotes ✗ (VerifyFailed), graues ? (VerifyNotAvailable) |
| **Tool-Status** | Im Settings-Bereich: Übersicht aller nötigen Tools mit Status (Gefunden/Fehlt/Version) |

### 7.5 CLI-Output-Anforderungen

```
Conversion Summary:
  Converted:      42
  Errors:          2
  Skipped:        15  (already target: 10, tool missing: 3, no target: 2)
  Blocked:         8  (policy-none: 5, set-protected: 2, unknown: 1)
  Lossy Sources:   3  (nkit: 2, cso: 1)
  Verified:       42/42
  Space Saved:    12.4 GB
```

### 7.6 API-Response-Anforderungen

```json
{
  "convertedCount": 42,
  "convertErrorCount": 2,
  "convertSkippedCount": 15,
  "convertBlockedCount": 8,
  "convertLossyWarningCount": 3,
  "convertVerifyPassedCount": 42,
  "convertVerifyFailedCount": 0,
  "convertSavedBytes": 13314398208
}
```

---

## 8. Empfohlene Issue-/Epic-Struktur

### Epic: Conversion Model Hardening

**Labels:** `epic`, `size: large`, `backend`, `phase-2-enhanced`

#### Sub-Issues nach Phase

---

### Phase 1: Datenmodell & Policy (Must-Have)

**Issue 1: ConversionPolicy in consoles.json + Contracts**  
**Labels:** `backend`, `size: medium`, `priority: high`, `phase-2-enhanced`

```markdown
## User Story
Als Romulus-Nutzer
möchte ich für jedes System sehen, ob Konvertierung möglich ist,
damit ich verstehe, warum bestimmte Dateien nicht konvertiert werden.

## Acceptance Criteria
- [ ] consoles.json enthält für jedes der 65 Systeme ein `conversionPolicy`-Feld
  mit Wert `auto`, `archiveOnly`, `manualOnly` oder `none`
- [ ] Neues ConversionPolicy-Enum in Contracts
- [ ] FormatConverterAdapter liest Policy und blockiert bei `none` / erfordert Bestätigung bei `manualOnly`
- [ ] ARCADE und NEOGEO sind als `none` markiert (aktiver Bugfix L7)
- [ ] Alle 38 bisher nicht abgedeckten Systeme haben eine explizite Policy (L8)
- [ ] Preview-Ansicht zeigt Policy-Status pro Datei

## Technical Requirements
- Contracts: neues Enum `ConversionPolicy { Auto, ArchiveOnly, ManualOnly, None }`
- ConsoleDefinition erweitern: `ConversionPolicy` Feld
- FormatConverterAdapter.GetTargetFormat(): Policy prüfen
- Test: jedes System in consoles.json hat eine gültige Policy
```

**Est:** 3-4 Tage

---

**Issue 2: ConversionTarget-Registry aus consoles.json**  
**Labels:** `backend`, `size: medium`, `priority: high`, `phase-2-enhanced`

```markdown
## User Story
Als Entwickler
möchte ich Conversion-Targets nicht hardcoded haben,
damit Änderungen ohne Code-Deployment möglich sind.

## Acceptance Criteria
- [ ] consoles.json enthält optionales `conversionTarget`-Objekt pro System
  mit `extension`, `toolName`, `command`
- [ ] FormatConverterAdapter.DefaultBestFormats wird aus consoles.json geladen
- [ ] FeatureService.Conversion.GetTargetFormat() delegiert an FormatConverterAdapter (L10, L12)
- [ ] Kein doppeltes Format-Mapping mehr (Single Source of Truth)
- [ ] Bestehende Tests bleiben grün

## Technical Requirements
- ConsoleDefinition erweitern: optionales `ConversionTarget`
- FormatConverterAdapter: JSON-gesteuerte Registry statt hardcoded Dictionary
- FeatureService.Conversion: GetTargetFormat() und ConsoleFormatPriority → aus gemeinsamer Quelle
```

**Est:** 3-4 Tage

---

**Issue 3: SourceIntegrity-Klassifizierung + Lossy-Warnings**  
**Labels:** `backend`, `size: small`, `priority: high`, `phase-2-enhanced`

```markdown
## User Story
Als Sammler mit NKit- und CSO-Dateien
möchte ich vor der Konvertierung gewarnt werden, dass meine Quelldateien lossy sind,
damit ich eine informierte Entscheidung treffen kann.

## Acceptance Criteria
- [ ] SourceIntegrity-Enum: Lossless, Lossy, Unknown
- [ ] Classifier erkennt: .nkit.* → Lossy, .cso → Lossy, .pbp → Lossy, .cdi → Lossy
- [ ] ConversionResult enthält SourceIntegrity + Warnings[]
- [ ] ConvertLossyWarningCount in RunProjection
- [ ] GUI zeigt ⚠️ Badge bei Lossy-Quellen im Preview
- [ ] CLI zeigt "Lossy Sources: N" in Summary
```

**Est:** 2-3 Tage

---

### Phase 2: Pipeline-Korrekturen (Must-Have)

**Issue 4: PS2 CD/DVD-Unterscheidung**  
**Labels:** `backend`, `size: small`, `priority: high`, `phase-2-enhanced`

```markdown
## User Story
Als Nutzer mit PS2-Sammlung
möchte ich, dass CD-basierte PS2-Spiele korrekt mit createcd konvertiert werden,
damit keine fehlerhaften CHD-Dateien entstehen.

## Acceptance Criteria
- [ ] Image-Grösse <700 MB → chdman createcd, ≥700 MB → chdman createdvd
- [ ] ConversionTarget für PS2 wird dynamisch erstellt basierend auf Dateigrösse
- [ ] Test: kleines PS2-ISO (<700 MB) → createcd-Aufruf
- [ ] Test: grosses PS2-ISO (>700 MB) → createdvd-Aufruf
```

**Est:** 1-2 Tage

---

**Issue 5: CSO→CHD Pipeline mit Zwischenschritt**  
**Labels:** `backend`, `size: medium`, `priority: high`, `phase-2-enhanced`

```markdown
## User Story
Als Nutzer mit PSP-CSO-Dateien
möchte ich, dass CSO automatisch über ISO zu CHD konvertiert wird,
damit ich meine PSP-Bibliothek vereinheitlichen kann.

## Acceptance Criteria
- [ ] CSO-Erkennung im ConversionPlan: IntermediateStep = ciso/maxcso decompress
- [ ] Temp-ISO wird in ExtractDir erstellt, nach Conversion aufgeräumt
- [ ] ciso/maxcso als neues Tool in ToolRunner + tool-hashes.json
- [ ] Test: CSO → ISO → CHD Kette erfolgreich
- [ ] Test: Cleanup bei Fehler im Zwischenschritt
```

**Est:** 3-4 Tage

---

**Issue 6: Multi-File-Conversion-Atomicity**  
**Labels:** `backend`, `size: medium`, `priority: medium`, `phase-2-enhanced`

```markdown
## User Story
Als Nutzer mit cue/bin-Paaren in ZIP-Archiven
möchte ich, dass alle Dateien eines Sets gemeinsam konvertiert werden,
damit keine inkonsistenten Teilkonvertierungen entstehen.

## Acceptance Criteria
- [ ] ZIP-Extraktion findet ALLE .cue-Dateien (nicht nur die erste)
- [ ] m3u-referenzierte Disc-Dateien werden als atomische Einheit erkannt
- [ ] Bei Fehler in einem Disc eines Sets: gesamtes Set wird als Error markiert
- [ ] Test: ZIP mit 2 .cue/.bin-Paaren → beide konvertiert
- [ ] Test: m3u-Set mit 3 CHD-Referenzen → atomische Behandlung
```

**Est:** 3-4 Tage

---

### Phase 3: Reporting & Transparenz

**Issue 7: ConversionPlan-Preview für DryRun**  
**Labels:** `backend`, `frontend`, `size: medium`, `priority: medium`, `phase-2-enhanced`

```markdown
## User Story
Als Nutzer
möchte ich vor dem Conversion-Run eine vollständige Vorschau sehen,
damit ich weiss, was passieren wird und was blockiert ist.

## Acceptance Criteria
- [ ] ConversionPlan-Modell mit allen Feldern aus §5.2 implementiert
- [ ] DryRun erstellt ConversionPlan[] für alle Dateien
- [ ] GUI zeigt: konvertierbar/skip/blocked/warnings in tabellarischer Preview
- [ ] CLI: `--dry-run --convert-format auto` gibt Plan als JSON aus
- [ ] API: GET /runs/{id}/conversion-plan liefert Plan
```

**Est:** 4-5 Tage

---

**Issue 8: Erweiterte Conversion-Metriken in RunProjection**  
**Labels:** `backend`, `size: small`, `priority: medium`, `phase-2-enhanced`

```markdown
## User Story
Als Nutzer
möchte ich nach dem Run detaillierte Conversion-Statistiken sehen,
damit ich den Erfolg der Konvertierung beurteilen kann.

## Acceptance Criteria
- [ ] RunProjection erhält: ConvertBlockedCount, ConvertLossyWarningCount,
      ConvertVerifyPassedCount, ConvertVerifyFailedCount, ConvertSavedBytes
- [ ] CLI zeigt erweiterte Summary (§7.5)
- [ ] API Response enthält neue Felder (§7.6)
- [ ] GUI Dashboard zeigt Conversion-Kacheln
```

**Est:** 2-3 Tage

---

**Issue 9: RVZ-Verifizierung härten**  
**Labels:** `backend`, `size: small`, `priority: medium`, `phase-2-enhanced`

```markdown
## User Story
Als Archivar
möchte ich sicher sein, dass meine RVZ-Konvertierungen intakt sind,
damit ich keine korrupten Dateien in meiner Bibliothek habe.

## Acceptance Criteria
- [ ] RVZ-Verifikation prüft zusätzlich: Header-Grösse, Disc-Type-Feld, ZSTD-Frame-Integrität
- [ ] Falls dolphintool >= 5.0-21264 vorhanden: `dolphintool verify` nutzen
- [ ] VerificationStatus-Enum: Verified, VerifyFailed, VerifyNotAvailable
- [ ] Test: korrumpiertes RVZ (gültiger Header, defekte Daten) → VerifyFailed
```

**Est:** 2-3 Tage

---

### Zusammenfassung der Epic-Struktur

```
[EPIC] Conversion Model Hardening
├── Phase 1: Datenmodell & Policy
│   ├── #1  ConversionPolicy in consoles.json    (3-4d)  ← P1, must-have
│   ├── #2  ConversionTarget-Registry aus JSON    (3-4d)  ← P1, must-have
│   └── #3  SourceIntegrity + Lossy-Warnings      (2-3d)  ← P1, must-have
├── Phase 2: Pipeline-Korrekturen
│   ├── #4  PS2 CD/DVD-Unterscheidung             (1-2d)  ← P1, must-have
│   ├── #5  CSO→CHD Pipeline                      (3-4d)  ← P1, must-have
│   └── #6  Multi-File Atomicity                   (3-4d)  ← P2, high
├── Phase 3: Reporting & Transparenz
│   ├── #7  ConversionPlan-Preview                 (4-5d)  ← P2, medium
│   ├── #8  Erweiterte Metriken in RunProjection   (2-3d)  ← P2, medium
│   └── #9  RVZ-Verifizierung härten              (2-3d)  ← P2, medium
└── Total: ~24-32 Tage
```

---

## Anhang: Abgrenzung (Out of Scope)

| Thema | Begründung |
|---|---|
| Rück-Konvertierung (CHD→ISO, RVZ→ISO) | Romulus ist kein Archiv-Extractor. Rückkonvertierung ist ein separates Feature. |
| ROM-Header-Manipulation (.smc↔.sfc, .v64↔.z64) | Byte-Order-Normalisierung ist kein Conversion-Feature. Absichtlich ausgeschlossen. |
| Encrypted-Content-Handling (Switch-Keys, PS3-Decryption) | Rechtlich und technisch ausserhalb des Scope. |
| NKit-Rebuild (NKit→ISO-Fullrebuild) | Spezial-Tool mit eigener Komplexität. Nicht Romulus-Scope. |
| Custom User-defined Conversion-Targets | Phase 3+ Feature. Erst nach stabilem Policy-Modell. |
| GPU-beschleunigte Hashing-Integration | Orthogonales Feature, gehört nicht zum Conversion-Modell. |
