# Miss Analysis & Safe Recall Improvement

> **Benchmark-Stand:** 1152 Samples — 902 Correct, 0 Wrong, 146 FP, 60 Missed, 44 TN
> **Analysierte Buckets:** `ec` (edge-cases) → 36 Missed, `rs` (repair-safety) → 24 Missed
> **Constraint:** Kein Recall-Gewinn darf die Wrong Match Rate erhöhen. Misses werden erst NACH FP-Reduktion bearbeitet.

---

## 1 — Executive Verdict

**80 % der Misses (48 von 60) sind sicher und ohne FP-Risiko rückholbar.** Der überwiegende Anteil hat keine Ursache in fehlender Detektionslogik, sondern in drei Infrastruktur-Defiziten des Benchmark-Frameworks:

| Root Cause | Misses | Typ | FP-Risiko |
|---|---|---|---|
| RC-1: Filename-Kollision in ec-ambiguous | 20 | Infrastruktur-Bug | Null |
| RC-2: Fehlende `primaryMethod` in rs-Entries | 15 | Ground-Truth-Lücke | Null |
| RC-3: Fehlende Stub-Generatoren für 12 Disc-Systeme | ~13 | Infrastruktur-Lücke | Null |
| RC-4: Keine PS3-Erkennung im DiscHeaderDetector | 13 | Produktiv-Code-Lücke | Niedrig |
| RC-5: `.bin` ohne Kontext — inhärent nicht lösbar | 12 | Systemische Grenze | **Hoch** |

**Die ersten drei Root Causes (RC-1 bis RC-3) erfordern keinerlei Änderung am Produktiv-Code.** Es handelt sich ausschließlich um Benchmark-Infrastruktur- und Ground-Truth-Fixes. RC-4 (PS3) ist eine gezielte, sichere Erweiterung. RC-5 ist das einzige echte Detektionsproblem — und sollte bewusst **nicht** adressiert werden.

---

## 2 — Bucket-Analyse

### 2.1 — ec (edge-cases): 36 Missed

| Sub-Cluster | Entries | Root Cause | Details |
|---|---|---|---|
| `ambiguous` (20) | 20 Missed | **RC-1** Filename-Kollision | Alle 20 Entries schreiben `roms/Game (USA).iso`. StubGeneratorDispatch überschreibt sequenziell — nur der letzte Entry (XBOX) bleibt als ext-only Stub auf Disk. **Alle** 20 evaluieren gegen denselben headerlosen Stub → UNKNOWN. |
| `ps-disambiguation` PS3 (10) | 10 Missed | **RC-3 + RC-4** | PS3 hat keinen DiscMap-Eintrag → ext-only Stub. Selbst mit Stub: DiscHeaderDetector hat **keine** PS3-Erkennung. PVD-Fallback geht zu PS1, nicht PS3. |
| `cross-system` .iso (3) | 3 Missed | **RC-3** | PS3-xsps2, NEOCD-xsneogeo, PCECD-xspce — alle .iso im `roms/`-Verzeichnis. PS3/NEOCD/PCECD haben keinen DiscMap-Stub → ext-only → UNKNOWN. |
| `cross-system` .bin (3) | 3 Missed | **RC-5** | ARCADE-xsneogeo, NEOGEO-xsarcade, NEOGEO-xsneocd — `.bin` in `roms/`. Keine Folder-Hints, keine Header, kein Serial/Keyword. Inhärent nicht lösbar. |

**Betroffene Systeme (EC):**
- PS3: 11 Missed (1 ambig + 1 cross + 10 psdis) — **größter Einzelposten** (18.3 % aller Misses)
- NEOCD: 2 Missed (1 ambig + 1 cross)
- PCECD: 2 Missed (1 ambig + 1 cross)
- Plus 17 Systeme mit je 1 Miss aus ambiguous-Kollision
- ARCADE/NEOGEO: je 1–2 aus cross-system .bin

### 2.2 — rs (repair-safety): 24 Missed

| Sub-Cluster | Entries | Root Cause | Details |
|---|---|---|---|
| `.iso` ohne Header (15) | 15 Missed | **RC-2** + teils RC-3 | Alle 65 rs-Entries haben **kein** `detectionExpectations.primaryMethod`. StubGeneratorDispatch nutzt DiscMap nur bei `method == "DiscHeader"`. Da method null ist, erhalten **alle** rs-Entries ext-only Stubs — auch PS1, PS2, PSP, GC, SAT, SCD, die DiscMap-Generatoren hätten. |
| `.bin` ambiguous (9) | 9 Missed | **RC-5** | maybe_arcade_game.bin etc. in `unsorted/`. Kein Folder, kein Header, kein Serial. Ambiguous extension → viele Kandidaten → kein einzelner Treffer. |

**Betroffene rs-Systeme (.iso):** 3DO, CD32, FMTOWNS, GC, JAGCD, NEOCD, PCECD, PS1, PS2, PS3, PSP, SAT, SCD, X360, XBOX
**Betroffene rs-Systeme (.bin):** ARCADE, CHANNELF, CPC, DOS, NEOGEO, NGPC, PC98, SUPERVISION, X68K

---

## 3 — Sichere Recall-Gewinne

### 3.1 — Fix 1: Filename-Kollision in ec-ambiguous auflösen (20 Entries → 20 recovered)

**Typ:** Ground-Truth-Fix (keine Code-Änderung)
**FP-Risiko:** Null
**Aufwand:** Gering

**Problem:** Alle 20 ec-ambiguous Entries verwenden identisch `roms/Game (USA).iso`. `StubGeneratorDispatch.GenerateAll()` iteriert sequenziell → jeder schreibt dieselbe Datei → nur der letzte (XBOX, ext-only) bleibt.

**Fix:** Eindeutige Filenames pro Entry:
```
ec-3DO-ambig-001:     "3DO Ambiguous (USA).iso"
ec-CD32-ambig-001:    "CD32 Ambiguous (USA).iso"
ec-PS3-ambig-001:     "PS3 Ambiguous (USA).iso"
...
```

**Warum sicher:** Jeder Entry bekommt seinen eigenen Stub. Systeme mit DiscMap-Generatoren (DC, GC, PS1, PS2, PSP, SAT, SCD, WII) erhalten automatisch korrekte Header. Systeme **ohne** DiscMap-Generator (3DO, CD32, CDI, FMTOWNS, JAGCD, NEOCD, PCECD, PCFX, PS3, WIIU, X360, XBOX) bleiben nach Fix 1 allein noch Missed → werden durch Fix 2/3/4 adressiert.

**Recovery nach Fix 1 allein:** 8 von 20 (DC, GC, PS1, PS2, PSP, SAT, SCD, WII)
**Recovery nach Fix 1+2:** 18 von 20 (+ alle mit neuem Stub-Generator)
**Recovery nach Fix 1+2+4:** 19 von 20 (+ PS3)
**Recovery nach Fix 1+2+4+5:** 20 von 20

---

### 3.2 — Fix 2: Stub-Generatoren für sekundäre Disc-Systeme (≤18 Entries)

**Typ:** Test-Infrastruktur (keine Produktiv-Code-Änderung)
**FP-Risiko:** Null
**Aufwand:** Mittel

**Problem:** `StubGeneratorDispatch.DiscMap` enthält nur 8 von 20 Disc-Systemen. Fehlende 12 Systeme erhalten ext-only Stubs, obwohl `DiscHeaderDetector.ResolveConsoleFromText()` bereits fertige Regex-Patterns für sie hat.

**Fehlende DiscMap-Einträge + Matching-Pattern:**

| System | Existierendes Pattern in ResolveConsoleFromText | Stub-Typ |
|---|---|---|
| 3DO | — (Magic-Bytes in ScanDiscImage: Opera FS 0x01+5×0x5A) | Opera FS Header |
| CD32 | `RxCd32`: `AMIGA.*BOOT\|CDTV\|CD32` | Boot-Sektor mit "CD32" Text |
| FMTOWNS | `RxFmTowns`: `FM.*TOWNS` | PVD mit "FM TOWNS" System-ID |
| NEOCD | `RxNeoGeo`: `NEOGEO.*CD\|NEO.?GEO` | Boot-Sektor mit "NEO GEO CD" |
| PCECD | `RxPcEngine`: `PC.*Engine\|TURBOGRAFX` | Boot-Sektor mit "PC Engine" |
| PCFX | `RxPcFx`: `PC-FX` | Boot-Sektor mit "PC-FX" |
| JAGCD | `RxJaguar`: `ATARI.*JAGUAR` | Boot-Sektor mit "ATARI JAGUAR" |
| XBOX | `RxXbox`: `MICROSOFT\*XBOX\*MEDIA` (+ ScanDiscImage 0x10000) | XDVDFS Signatur |

**Implementierung:** Für jedes System einen neuen `IStubGenerator` erstellen, der die binäre Signatur an der korrekten Offset-Position platziert. Dann DiscMap-Einträge hinzufügen.

**Beispiel für 3DO-Stub:**
```csharp
// Opera FS: offset 0x00 = 0x01 + 5×0x5A, offset 0x06 = 0x01
data[0] = 0x01; data[1..5] = 0x5A; data[6] = 0x01;
```

**Beispiel für NEOCD-Stub (Boot-Sektor-Text):**
```csharp
// ResolveConsoleFromText matched "NEO GEO CD" within first 8KB
Encoding.ASCII.GetBytes("NEO GEO CD SYSTEM").CopyTo(data, 0);
```

---

### 3.3 — Fix 3: `primaryMethod` in rs Ground-Truth setzen (15 .iso-Entries → 15 recovered)

**Typ:** Ground-Truth-Fix (keine Code-Änderung)
**FP-Risiko:** Null
**Aufwand:** Gering

**Problem:** Alle 65 rs-Entries haben kein `detectionExpectations`-Feld. `StubGeneratorDispatch` prüft `primaryMethod == "DiscHeader"` um DiscMap zu nutzen → null → ext-only Stub → auch PS1/PS2/GC/SAT/SCD bekommen headerlosen Stub.

**Fix A (Ground-Truth):** Für alle 15 .iso rs-Entries `detectionExpectations.primaryMethod: "DiscHeader"` setzen.

**Fix B (StubGeneratorDispatch — besser):** Fallback-Logik ergänzen: wenn `primaryMethod` null ist UND `consoleKey` in DiscMap UND Extension `.iso/.bin/.chd` → DiscMap-Stub verwenden. Das ist robuster als Fix A, weil zukünftige Ground-Truth-Entries automatisch profitieren:

```csharp
// Nach Priority 2 "explicit primaryMethod", vor Priority 3 "ext-only":
// Infer disc-based system from consoleKey + extension
if (method is null && consoleKey is not null)
{
    var ext = entry.Source.Extension?.ToLowerInvariant();
    if (ext is ".iso" or ".bin" or ".chd" or ".img" or ".gcm" 
        && DiscMap.TryGetValue(consoleKey, out var inferred))
    {
        var gen = _registry.Get(inferred.GeneratorId);
        if (gen is not null) return gen.Generate(inferred.Variant);
    }
}
```

**Recovery:** Alle 15 .iso rs-Entries, die nach Fix 2 einen Stub-Generator haben, werden korrekt generiert und erkannt.

---

### 3.4 — Fix 4: PS3-Erkennung im DiscHeaderDetector (13 Entries → 13 recovered)

**Typ:** Produktiv-Code-Änderung
**FP-Risiko:** Niedrig (isolierte, präzise Prüfung)
**Aufwand:** Mittel

**Problem:** `DiscHeaderDetector` hat **keine** PS3-Erkennung. Die aktuelle PVD-Logik:
```
1. PVD System-ID matches "PLAYSTATION"?
2. → Check PSP_GAME → PSP
3. → Check BOOT2 → PS2
4. → Default → PS1  ← PS3 landet hier!
```

Reale PS3-ISOs wären "Wrong" (PS1 statt PS3), nicht "Missed". Aber da die Stubs ext-only sind (kein PVD), wird die PVD-Logik gar nicht erreicht → UNKNOWN → Missed.

**Lösung:** PS3-spezifische Disc-Erkennung:
1. **PS3_DISC.SFB**: Reale PS3-Discs haben diese Datei im Root des UDF-Dateisystems
2. **PS3_GAME Verzeichnis**: Layout-Konvention aller PS3-Spiele
3. **PVD-Erweiterung**: Nach PSP/PS2-Check → Scan nach "PS3" oder "Blu-ray" Markern → PS3 statt PS1-Fallback

**Implementierung (DiscHeaderDetector):**
```csharp
// In ScanDiscImage, nach PS2/PSP PVD-Check:
private static readonly Regex RxPs3Marker = new(@"PS3_DISC\.SFB|PS3_GAME|PS3VOLUME", RxOpts, RxTimeout);

// Im PLAYSTATION PVD-Branch, nach PSP + PS2 Checks:
if (RxPs3Marker.IsMatch(pvdText)) return "PS3";
return "PS1"; // Fallback nur wenn wirklich kein PS3-Marker
```

**Neuer Stub-Generator:**
```csharp
DiscMap["PS3"] = ("ps3-pvd", "standard");

// Ps3PvdGenerator: PVD mit "PLAYSTATION" + "PS3_DISC.SFB" marker
```

**Recovery:** 10 PS3-psdis + 1 PS3-ambig + 1 PS3-cross + 1 rs-PS3 = 13 Entries.

**Warum sicher:**
- Neuer Marker (`PS3_DISC.SFB`) ist PS3-exklusiv — kein anderes System verwendet ihn
- Prüfung kommt NACH PSP/PS2-Checks → kein Einfluss auf bestehende Erkennung
- PS1-Fallback bleibt für echte PS1-ISOs erhalten

---

### 3.5 — Fix 5: X360/WIIU-Erkennung (4 Entries)

**Typ:** Produktiv-Code-Änderung
**FP-Risiko:** Niedrig-Mittel
**Aufwand:** Mittel

**X360:** Xbox 360 verwendet eine Variante des XDVDFS-Formats. Aktuelle XBOX-Erkennung (`MICROSOFT*XBOX*MEDIA` bei 0x10000) kann ggf. erweitert werden. Alternative: XGD2/XGD3-Sektorstruktur prüfen.

**WIIU:** Wii U Discs verwenden proprietäres Format. WUX-Container haben eigene Magic Numbers. ISO-Form ist selten — die meisten WIIU-Entries verwenden `.wux` (unique extension) und sind bereits erkannt.

**Recovery:** 2 ec-ambig + 2 rs = 4 Entries.

---

## 4 — Gefährliche Recall-Gewinne (NICHT UMSETZEN)

### 4.1 — `.bin` ohne Kontext aggressiver erkennen (12 Entries, RC-5)

**Betroffene Entries:**
- ec: ARCADE-xsneogeo, NEOGEO-xsarcade, NEOGEO-xsneocd (3 Entries)
- rs: ARCADE, CHANNELF, CPC, DOS, NEOGEO, NGPC, PC98, SUPERVISION, X68K (9 Entries)

**Warum gefährlich:**
- `.bin` wird von **34+ Systemen** in `ambigExts` beansprucht
- Kein Folder, kein Header, kein Serial, kein Keyword → null Differenzierungssignal
- Jede Heuristik (z.B. Dateigröße) erzeugt quadratisch mehr False Positives als Recalls
- Benchmark-Filenames (`maybe_arcade_game.bin`) sind nicht repräsentativ — reale `.bin`-Dateien im `unsorted/`-Ordner haben beliebige Namen

**Empfehlung:** Diese 12 Entries als **systemische Grenze** akzeptieren. UNKNOWN ist hier die korrekte Antwort. `.bin` in `unsorted/` **soll** geblockt werden → das ist repair-safety by design.

### 4.2 — Keyword-Detection für Cross-System .bin

Die 3 ec-cross `.bin`-Entries haben Filenames wie "CrossTest (ARCADE vs NEOGEO).bin". Daraus könnte man Keywords extrahieren. Aber:
- Das Pattern ist Benchmark-spezifisch, nicht realworld
- Echte Nutzer benennen keine Dateien "CrossTest (X vs Y)"
- Keyword-Matching auf beliebige Klammerwerte → FP-Explosion

**Empfehlung:** Nicht umsetzen. Ground-Truth ggf. mit realistischeren Filenames aktualisieren.

### 4.3 — File-Size-Heuristik

PS3-ISOs sind typischerweise >20 GB. Aber:
- Blu-ray-ISOs anderer Systeme (WIIU, X360) haben ähnliche Größen
- Film-ISOs, Backup-Images etc. können gleich groß sein
- Size als Detection-Quelle generiert zwangsläufig FP

**Empfehlung:** Nur als Tie-Breaker in Kombination mit anderen Signalen, nie als primäre Quelle.

---

## 5 — Konkrete Refactoring-Ziele

### 5.1 — StubGeneratorDispatch (Test-Infrastruktur)

**Datei:** `src/RomCleanup.Tests/Benchmark/Generators/StubGeneratorDispatch.cs`

| Ziel | Beschreibung |
|---|---|
| Filename-Kollisions-Schutz | `GenerateAll()` soll Duplikat-Pfade erkennen und entweder warnen oder automatisch mit Entry-ID-Suffix de-duplizieren. |
| DiscMap erweitern | 8 neue Einträge: 3DO, CD32, FMTOWNS, JAGCD, NEOCD, PCECD, PCFX, XBOX (+ PS3 mit Fix 4). |
| Null-PrimaryMethod-Fallback | Wenn `primaryMethod` null UND `consoleKey` in DiscMap → DiscMap-Stub statt ext-only. |

### 5.2 — Neue Stub-Generatoren (Test-Infrastruktur)

**Verzeichnis:** `src/RomCleanup.Tests/Benchmark/Generators/Disc/`

| Generator | Stub-Signatur | Offset | Detector-Pattern |
|---|---|---|---|
| `OperaFsGenerator` (3DO) | 0x01 + 5×0x5A + Version-Byte | 0x0000 | Magic-Byte-Prüfung in ScanDiscImage |
| `BootSectorTextGenerator` | Konfigurierbar: Systemtext im Boot-Bereich (0-8KB) | 0x0000 | ResolveConsoleFromText-Regex |
| `XdvdfsGenerator` (XBOX) | "MICROSOFT*XBOX*MEDIA" | 0x10000 | ScanDiscImage XDVDFS-Check |
| `Ps3PvdGenerator` (PS3) | PVD "PLAYSTATION" + "PS3_DISC.SFB" | 0x8000 + 0x100 | Neues RxPs3Marker |

**BootSectorTextGenerator** ist ein generischer Generator, der für mehrere Systeme parametrisiert wird:
- NEOCD → "NEO GEO CD SYSTEM" bei Offset 0
- PCECD → "PC Engine CD-ROM SYSTEM" bei Offset 0
- PCFX → "PC-FX:Hu_CD-ROM" bei Offset 0
- JAGCD → "ATARI JAGUAR CD" bei Offset 0
- CD32 → "CD32" bei Offset 0
- FMTOWNS → PVD mit "FM TOWNS" System-ID bei 0x8000+8

### 5.3 — Ground-Truth edge-cases.jsonl

**Datei:** `benchmark/ground-truth/edge-cases.jsonl`

| Entry-Bereich | Änderung |
|---|---|
| 20 `ambiguous`-Entries | Eindeutige FileNames vergeben. Schema: `"{System} Header Test (USA).iso"` oder `"Game_{system_id} (USA).iso"` |

### 5.4 — Ground-Truth repair-safety.jsonl (optional)

**Datei:** `benchmark/ground-truth/repair-safety.jsonl`

| Entry-Bereich | Änderung |
|---|---|
| 15 `.iso`-Entries | Optional: `detectionExpectations.primaryMethod: "DiscHeader"` setzen. Wird durch Fix 3B (Dispatch-Fallback) überflüssig. |

### 5.5 — DiscHeaderDetector (Produktiv-Code)

**Datei:** `src/RomCleanup.Core/Classification/DiscHeaderDetector.cs`

| Ziel | Beschreibung |
|---|---|
| PS3-Detection | Neues `RxPs3Marker` Pattern + Check nach PS2/PSP im PLAYSTATION-PVD-Branch |
| PS1-Fallback-Guard | PS1-Return nur wenn weder PS2/PSP/PS3 matchen → defensive Absicherung |

---

## 6 — Top 10 Maßnahmen (priorisiert)

| # | Maßnahme | Typ | Entries | FP-Risiko | Priorität |
|---|---|---|---|---|---|
| **1** | ec-ambiguous Filenames eindeutig machen | Ground-Truth-Fix | 20 | Null | **P1** |
| **2** | StubGeneratorDispatch: Null-PrimaryMethod-Fallback | Test-Infra-Fix | 15 | Null | **P1** |
| **3** | BootSectorTextGenerator (NEOCD, PCECD, PCFX, JAGCD, CD32) | Test-Infra | ~10 | Null | **P1** |
| **4** | OperaFsGenerator (3DO) + DiscMap-Eintrag | Test-Infra | ~3 | Null | **P1** |
| **5** | XdvdfsGenerator (XBOX) + DiscMap-Eintrag | Test-Infra | ~2 | Null | **P1** |
| **6** | FmTowns PVD-Generator + DiscMap-Eintrag | Test-Infra | ~2 | Null | **P1** |
| **7** | PS3-Erkennung in DiscHeaderDetector + Ps3PvdGenerator | Produktiv-Code + Test | 13 | Niedrig | **P2** |
| **8** | Duplikat-Pfad-Warnung in StubGeneratorDispatch.GenerateAll() | Test-Infra | 0 (Regression-Guard) | Null | **P2** |
| **9** | X360/WIIU-Detection evaluieren | Produktiv-Code + Test | 4 | Mittel | **P3** |
| **10** | `.bin`-Entries als systemische Grenze dokumentieren | Dokumentation | 12 (akzeptiert) | Null | **P3** |

### Erwartetes Ergebnis nach Umsetzung (P1 + P2):

| Metrik | Vorher | Nachher (P1+P2) | Delta |
|---|---|---|---|
| Missed | 60 | 16 | **–44** |
| Correct | 902 | 946 | **+44** |
| Wrong | 0 | 0 | ±0 |
| FalsePositive | 146 | 146 | ±0 |
| Recovery-Rate | — | **73 %** | — |

### Erwartetes Ergebnis nach P1 + P2 + P3:

| Metrik | Vorher | Nachher (P1+P2+P3) | Delta |
|---|---|---|---|
| Missed | 60 | 12 | **–48** |
| Correct | 902 | 950 | **+48** |
| WMR | 12.67 % | 12.67 % | ±0 |
| Recovery-Rate | — | **80 %** | — |

**Die verbleibenden 12 Misses sind `.bin`-Dateien ohne Kontext — die korrekte Antwort ist UNKNOWN.**

---

## Appendix A: Vollständige Miss-Zuordnung

### ec (36 Missed)

| Cluster | IDs | Root Cause | Fix |
|---|---|---|---|
| ambiguous (20) | ec-{3DO,CD32,CDI,DC,FMTOWNS,GC,JAGCD,NEOCD,PCECD,PCFX,PS1,PS2,PS3,PSP,SAT,SCD,WII,WIIU,X360,XBOX}-ambig-001 | RC-1 (Kollision) + RC-3 (fehlende Stubs) | Fix 1+2 |
| cross-system .iso (3) | ec-PS3-xsps2-001, ec-NEOCD-xsneogeo-001, ec-PCECD-xspce-001 | RC-3 + RC-4 | Fix 2+4 |
| cross-system .bin (3) | ec-ARCADE-xsneogeo-001, ec-NEOGEO-xsarcade-001, ec-NEOGEO-xsneocd-001 | RC-5 | **Kein sicherer Fix** |
| ps-disambiguation PS3 (10) | ec-PS3-psdis-001 bis -010 | RC-3 + RC-4 | Fix 4 |

### rs (24 Missed)

| Cluster | IDs | Root Cause | Fix |
|---|---|---|---|
| .iso (15) | rs-{3DO,CD32,FMTOWNS,GC,JAGCD,NEOCD,PCECD,PS1,PS2,PS3,PSP,SAT,SCD,X360,XBOX}-lowconf-001 | RC-2 (null primaryMethod) | Fix 3 |
| .bin (9) | rs-{ARCADE,CHANNELF,CPC,DOS,NEOGEO,NGPC,PC98,SUPERVISION,X68K}-lowconf-001 | RC-5 | **Kein sicherer Fix** |

---

## Appendix B: Confusion-Matrix (Missed-Entries)

| System | Count | Herkunft (ec/rs) | Root Cause |
|---|---|---|---|
| PS3 | 13 | 11 ec + 1 rs + 1 ec-cross | RC-3 + RC-4 |
| NEOCD | 3 | 1 ec-ambig + 1 ec-cross + 1 rs | RC-1 + RC-3 |
| PCECD | 3 | 1 ec-ambig + 1 ec-cross + 1 rs | RC-1 + RC-3 |
| NEOGEO | 3 | 2 ec-cross(.bin) + 1 rs(.bin) | RC-5 |
| 3DO | 2 | 1 ec-ambig + 1 rs | RC-1 + RC-2 + RC-3 |
| CD32 | 2 | 1 ec-ambig + 1 rs | RC-1 + RC-2 + RC-3 |
| FMTOWNS | 2 | 1 ec-ambig + 1 rs | RC-1 + RC-2 + RC-3 |
| GC | 2 | 1 ec-ambig + 1 rs | RC-1 + RC-2 |
| JAGCD | 2 | 1 ec-ambig + 1 rs | RC-1 + RC-2 + RC-3 |
| PS1 | 2 | 1 ec-ambig + 1 rs | RC-1 + RC-2 |
| PS2 | 2 | 1 ec-ambig + 1 rs | RC-1 + RC-2 |
| PSP | 2 | 1 ec-ambig + 1 rs | RC-1 + RC-2 |
| SAT | 2 | 1 ec-ambig + 1 rs | RC-1 + RC-2 |
| SCD | 2 | 1 ec-ambig + 1 rs | RC-1 + RC-2 |
| X360 | 2 | 1 ec-ambig + 1 rs | RC-1 + RC-2 + RC-3 |
| XBOX | 2 | 1 ec-ambig + 1 rs | RC-1 + RC-2 + RC-3 |
| ARCADE | 2 | 1 ec-cross(.bin) + 1 rs(.bin) | RC-5 |
| CDI | 1 | 1 ec-ambig | RC-1 + RC-3 |
| DC | 1 | 1 ec-ambig | RC-1 |
| PCFX | 1 | 1 ec-ambig | RC-1 + RC-3 |
| WII | 1 | 1 ec-ambig | RC-1 |
| WIIU | 1 | 1 ec-ambig | RC-1 + RC-3 |
| CHANNELF | 1 | 1 rs(.bin) | RC-5 |
| CPC | 1 | 1 rs(.bin) | RC-5 |
| DOS | 1 | 1 rs(.bin) | RC-5 |
| NGPC | 1 | 1 rs(.bin) | RC-5 |
| PC98 | 1 | 1 rs(.bin) | RC-5 |
| SUPERVISION | 1 | 1 rs(.bin) | RC-5 |
| X68K | 1 | 1 rs(.bin) | RC-5 |
| **Total** | **60** | **36 ec + 24 rs** | |
