# Recognition Quality Benchmark – Romulus

---

## 1. Executive Verdict

### Ist „95 % Erkennung" als Ziel brauchbar?

**Nein – nicht als einzelner Gesamtwert.**

Ein einzelner „95 % Accuracy"-Wert ist für Romulus **irreführend, gefährlich und nicht belastbar**.

**Warum:**

1. **Aggregation verschleiert Fehlertypen.** Ein System, das 99 % der NES-ROMs korrekt erkennt, aber 60 % der Saturn-ISOs falsch zuordnet, kann trotzdem „95 % overall" erreichen, wenn NES-ROMs den Datensatz dominieren. Das Ergebnis: Saturn-Sammlung wird zerstört.

2. **UNKNOWN ≠ WRONG.** Ein System, das bei Unsicherheit ehrlich „UNKNOWN" sagt, ist besser als eines, das aggressiv rät und dabei falsch liegt. Ein Gesamtwert unterscheidet nicht zwischen diesen Zuständen.

3. **False Confidence ist der eigentliche Feind.** Die gefährlichste Failure-Mode ist: „Erkennung falsch, Confidence hoch, Sorting ausgeführt." Das passiert unsichtbar im 95-%-Mittelwert.

4. **Die Domäne ist heterogen.** Cartridge-ROMs (NES, GBA) haben deterministische Header → >99 % sind realistisch. Disc-Images ohne PVD (BIN/CUE ohne Sektordaten) können prinzipiell nicht sicher erkannt werden → 95 % sind dort physikalisch unmöglich.

### Kurzfazit

Romulus braucht kein „95 % overall". Es braucht:

- **Pro-Ebene-Ziele** (System, Kategorie, DAT, Sorting)
- **Pro-Klasse-Metriken** (je Console, je Dateityp)
- **Harte Fehlergrenzen** statt weicher Durchschnitte
- **Separate UNKNOWN- und WRONG-Tracking**
- **Ein Freigabemodell**, das Sorting/Repair nur bei bewiesener Sicherheit erlaubt

---

## 2. Was „Erkennungsrate" überhaupt bedeuten muss

### Warum ein einzelner Prozentwert zu wenig ist

| Problem | Konsequenz |
|---------|-----------|
| Overall Accuracy blendet Klassenungleichgewicht aus | 50 NES + 5 Saturn → Saturn-Fehler unsichtbar |
| UNKNOWN und WRONG werden zusammengefasst | Konservatives Verhalten wird bestraft |
| Kein Unterschied zwischen harmlosem und destruktivem Fehler | Falsch sortierte ROM = Datenverlust; UNKNOWN = harmlos |
| Keine Trendaussage möglich | Verbesserung in A, Verschlechterung in B → gleiche „Accuracy" |

### Welche Teilmetriken nötig sind

Die Erkennungsleistung muss auf **sieben Ebenen** separat bewertet werden:

| Ebene | Frage | Warum separat |
|-------|-------|---------------|
| A. Datei-/Container-Erkennung | Wurde der Dateityp korrekt identifiziert? | Grundlage für alle weiteren Schritte |
| B. System-/Konsolenerkennung | Wurde das System korrekt erkannt? | Kernfunktion für Sorting |
| C. Kategorie-Erkennung | GAME/BIOS/JUNK/NONGAME/UNKNOWN korrekt? | Steuert Dedupe- und Junk-Remove |
| D. Spielidentität | Korrektes Spiel, korrekte Variante? | Steuert Dedupe-Grouping |
| E. DAT-Matching | Wurde der richtige DAT-Eintrag gefunden? | Verifizierungs-Qualität |
| F. Sorting-Entscheidung | Wurde korrekt sortiert/blockiert? | Finale Nutzerwirkung |
| G. Repair-Tauglichkeit | Wäre diese Erkennung sicher genug für Rename/Rebuild? | Zukünftige Feature-Freigabe |

### Welche Fehlinterpretationen vermieden werden müssen

1. **„93 % Accuracy" bedeutet „nur 7 % Fehler"** → Falsch. 7 % Fehler bei 10.000 ROMs = 700 potenziell falsch sortierte Dateien.
2. **„Recall ist hoch, also funktioniert es"** → Falsch. Hoher Recall ohne Precision = aggressive Fehlzuordnung.
3. **„UNKNOWN-Rate sinkt, also wird es besser"** → Falsch. UNKNOWN-Rate sinkt auch, wenn man aggressiver rät.
4. **„DAT-Match-Rate steigt"** → Kann auch steigen durch falsche Cross-System-Matches.

---

## 3. Qualitätsmodell

### Ebenen und Zustände

Für jede Erkennungsebene definiert das Modell exakt drei Ergebnisklassen:

```
┌─────────────────────────────────────────────────────────────┐
│                    Ergebnis-Taxonomie                        │
├──────────────┬──────────────────────────────────────────────┤
│ CORRECT      │ Erkennung stimmt mit Ground Truth überein    │
│ UNKNOWN      │ System hat korrekt „weiß nicht" geantwortet  │
│ WRONG        │ Erkennung weicht von Ground Truth ab          │
└──────────────┴──────────────────────────────────────────────┘
```

**Kritische Unterscheidung:** UNKNOWN ist kein Fehler. UNKNOWN ist eine korrekte Entscheidung bei unzureichender Evidenz. Nur WRONG ist ein echter Fehler.

### Ergebnismatrix pro Ebene

#### A) Datei-/Container-Erkennung

| Zustand | Beschreibung | Beispiel |
|---------|-------------|---------|
| CORRECT | Archive als Archive erkannt, ROM als ROM | .zip → ZIP erkannt, .nes → NES-ROM erkannt |
| WRONG-TYPE | Dateityp falsch erkannt | .bin als ISO statt raw Binary |
| WRONG-CONTAINER | Archiv nicht geöffnet / falsch interpretiert | 7z nicht extrahiert, korruptes ZIP nicht erkannt |

#### B) System-/Konsolenerkennung

| Zustand | Beschreibung | Schweregrad |
|---------|-------------|-------------|
| CORRECT | Richtiges System erkannt | ✓ |
| UNKNOWN | Kein System zugeordnet (korrekt unsicher) | Akzeptabel |
| WRONG-CONSOLE | Falsches System zugeordnet | **Kritisch** – führt zu falschem Sorting |
| AMBIGUOUS-CORRECT | Mehrdeutigkeit erkannt, richtiges System in Hypothesen enthalten | Akzeptabel |
| AMBIGUOUS-WRONG | Mehrdeutigkeit erkannt, aber richtiges System nicht in Hypothesen | Fehler |

#### C) Kategorie-Erkennung

| Zustand | Beschreibung | Schweregrad |
|---------|-------------|-------------|
| CORRECT | Kategorie korrekt (Game=Game, Bios=Bios, Junk=Junk) | ✓ |
| GAME-AS-JUNK | Echtes Spiel als Junk klassifiziert | **Kritisch** – Datenverlust |
| GAME-AS-BIOS | Echtes Spiel als BIOS klassifiziert | Hoch |
| BIOS-AS-GAME | BIOS als Spiel klassifiziert | Hoch – falsche Dedupe |
| JUNK-AS-GAME | Demo/Beta/Hack als Spiel | Mittel – Qualitätsverlust |
| UNKNOWN | Nicht klassifiziert | Akzeptabel |

#### D) Spielidentität

| Zustand | Beschreibung |
|---------|-------------|
| CORRECT | Richtiges Spiel, richtige Variante, richtige Region |
| WRONG-GAME | Falsches Spiel zugeordnet |
| WRONG-VARIANT | Richtiges Spiel, aber falsche Version/Region/Disc |
| CORRECT-GROUP | Im richtigen Dedupe-Cluster, aber nicht exakte Variante |
| UNKNOWN | Keine Zuordnung möglich |

#### E) DAT-Matching

| Zustand | Beschreibung |
|---------|-------------|
| EXACT | Hash-Match → richtiges Spiel im richtigen System |
| WRONG-SYSTEM | Hash-Match, aber falsches System (Cross-System-Collision) |
| WRONG-GAME | Hash-Match, aber falsches Spiel (Hash-Collision im DAT) |
| NONE-EXPECTED | Kein Match, und keiner erwartet (Homebrew, Unlicensed) |
| NONE-MISSED | Kein Match, obwohl DAT-Eintrag existiert |
| FALSE-MATCH | Match gefunden, aber falsch (z.B. durch Container- statt Content-Hash) |

#### F) Sorting-Entscheidung

| Zustand | Beschreibung | Schweregrad |
|---------|-------------|-------------|
| CORRECT-SORT | Richtig sortiert in richtigen Ordner | ✓ |
| CORRECT-BLOCK | Korrekt blockiert (UNKNOWN/low-confidence) | ✓ |
| WRONG-SORT | In falschen Ordner sortiert | **Kritisch** |
| WRONG-BLOCK | Fälschlich blockiert, obwohl korrekt erkannt | Mittel |
| UNSAFE-SORT | Sortiert trotz niedrigem Confidence | **Kritisch** |

#### G) Repair-Tauglichkeit

| Zustand | Beschreibung |
|---------|-------------|
| REPAIR-SAFE | Erkennung sicher genug für Rename/Rebuild |
| REPAIR-RISKY | Erkennung unsicher, Repair wäre gefährlich |
| REPAIR-BLOCKED | Erkennung explizit unzureichend |

---

## 4. Metriken

### 4.1 Primärmetriken (Pflicht)

#### M1: Console Precision (pro System)

| Feld | Wert |
|------|------|
| **Definition** | Von allen Dateien, die als System X erkannt wurden: wie viele sind tatsächlich System X? |
| **Formel** | `TP(X) / (TP(X) + FP(X))` |
| **Warum wichtig** | Niedrige Precision = User bekommt falsche ROMs im Ordner → Vertrauensverlust |
| **Fehlinterpretation** | Hohe Precision sagt nichts über Completeness – System könnte viele ROMs als UNKNOWN lassen |
| **Ziel** | ≥ 98 % pro System mit >10 Testfällen |

#### M2: Console Recall (pro System)

| Feld | Wert |
|------|------|
| **Definition** | Von allen Dateien, die tatsächlich System X sind: wie viele wurden als System X erkannt? |
| **Formel** | `TP(X) / (TP(X) + FN(X))` wobei FN = WRONG + UNKNOWN |
| **Warum wichtig** | Niedriger Recall = viele ROMs landen als UNKNOWN, werden nicht sortiert |
| **Fehlinterpretation** | Recall trennt nicht zwischen WRONG und UNKNOWN; daher separate Metriken nötig |
| **Ziel** | ≥ 90 % für Systeme mit Header-Detection; ≥ 75 % für andere |

#### M3: Console F1 (pro System)

| Feld | Wert |
|------|------|
| **Definition** | Harmonisches Mittel aus Precision und Recall pro System |
| **Formel** | `2 × (Precision × Recall) / (Precision + Recall)` |
| **Warum wichtig** | Balancierte Bewertung, verhindert einseitige Optimierung |
| **Fehlinterpretation** | Kann hoch sein bei Systemen mit wenig Testdaten → Minimum N erforderlich |
| **Ziel** | ≥ 92 % gewichteter Macro-F1 über alle Systeme |

#### M4: Wrong Match Rate (global und pro System)

| Feld | Wert |
|------|------|
| **Definition** | Anteil der Dateien, die einem **falschen** System zugeordnet wurden |
| **Formel** | `Σ WRONG / Σ TOTAL` |
| **Warum wichtig** | **Die kritischste Metrik.** Jeder Wrong Match ist ein potenzieller Datenverlust bei Sorting. |
| **Fehlinterpretation** | Kann niedrig aussehen bei hoher UNKNOWN-Rate. Muss relativ zu erkannten (nicht allen) Dateien gesehen werden. |
| **Ziel** | ≤ 0,5 % global; ≤ 1 % pro System |

#### M5: Unknown Rate (global und pro System)

| Feld | Wert |
|------|------|
| **Definition** | Anteil der Dateien, die als UNKNOWN verblieben |
| **Formel** | `Σ UNKNOWN / Σ TOTAL` |
| **Warum wichtig** | Zu hoch = System nutzlos; zu niedrig = System rät zu aggressiv |
| **Fehlinterpretation** | Sinkende UNKNOWN-Rate ist nur Fortschritt, wenn Wrong Match Rate nicht gleichzeitig steigt |
| **Ziel** | ≤ 15 % global (für sauber benannte Sammlungen); ≤ 30 % für Chaos-Sets |

#### M6: False Confidence Rate

| Feld | Wert |
|------|------|
| **Definition** | Anteil der WRONG-Matches, die mit Confidence ≥ 80 geliefert wurden |
| **Formel** | `|{f ∈ WRONG : Confidence(f) ≥ 80}| / |WRONG|` |
| **Warum wichtig** | **Die zweitkritischste Metrik.** False Confidence = System lügt mit Überzeugung → Sorting passiert → Datenverlust |
| **Fehlinterpretation** | Bezieht sich nur auf WRONG-Matches. Hohe False Confidence Rate bei wenig WRONG ist weniger schlimm als bei viel WRONG. |
| **Ziel** | ≤ 5 % |

#### M7: Unsafe Sort Rate

| Feld | Wert |
|------|------|
| **Definition** | Anteil der Sorting-Entscheidungen, die zu falschem Zielordner führen |
| **Formel** | `|WRONG-SORT| / |TOTAL-SORT-DECISIONS|` |
| **Warum wichtig** | Direkte Messung des User-sichtbaren Schadens |
| **Fehlinterpretation** | Bezieht sich nur auf tatsächlich sortierte Dateien, nicht auf blockierte |
| **Ziel** | ≤ 0,3 % – harter Release-Blocker |

#### M8: Safe Sort Coverage

| Feld | Wert |
|------|------|
| **Definition** | Anteil der Dateien, die korrekt und sicher sortiert wurden |
| **Formel** | `|CORRECT-SORT| / |TOTAL|` |
| **Warum wichtig** | Misst den praktischen Nutzen: wie viel erledigt das Tool korrekt? |
| **Fehlinterpretation** | Kann nur steigen, wenn sowohl Erkennung besser wird als auch Confidence-Gating das erlaubt |
| **Ziel** | ≥ 80 % bei Referenz-Sets; ≥ 60 % bei Chaos-Sets |

### 4.2 Sekundärmetriken (empfohlen)

#### M9: Category Confusion Rate

| Feld | Wert |
|------|------|
| **Definition** | Rate der Verwechslungen zwischen GAME/BIOS/JUNK/NONGAME |
| **Formel** | Confusion Matrix: `Σ off-diagonal / Σ total` |
| **Warum wichtig** | GAME-AS-JUNK = Datenverlust; BIOS-AS-GAME = falsche Dedupe |
| **Ziel** | GAME-AS-JUNK ≤ 0,1 %; BIOS-AS-GAME ≤ 0,5 % |

#### M10: Console Confusion Rate

| Feld | Wert |
|------|------|
| **Definition** | Pro System-Paar: wie oft wird System A als System B erkannt? |
| **Formel** | Confusion Matrix `C[A][B] / Σ C[A][*]` |
| **Warum wichtig** | Identifiziert systematische Verwechslungen (PS1↔PS2, GB↔GBC, Genesis↔32X) |
| **Ziel** | Keine Paarung > 2 % |

#### M11: DAT Exact Match Rate

| Feld | Wert |
|------|------|
| **Definition** | Anteil der DAT-verifizierbaren Dateien, die einen exakten Hash-Match haben |
| **Formel** | `|DAT-EXACT| / |DAT-VERIFIABLE|` |
| **Warum wichtig** | Misst Qualität der DAT-Integration |
| **Fehlinterpretation** | Nur sinnvoll auf Dateien, deren System-DAT geladen ist |
| **Ziel** | ≥ 90 % für No-Intro/Redump-verifizierbare Sets |

#### M12: DAT Weak Match Rate

| Feld | Wert |
|------|------|
| **Definition** | Anteil der DAT-Matches durch LookupAny (Cross-System-Fallback) |
| **Formel** | `|DAT-LOOKUPANY| / |DAT-TOTAL-MATCHES|` |
| **Warum wichtig** | Hoher Anteil = System verlässt sich zu stark auf Hash-Fallback statt primäre Erkennung |
| **Ziel** | ≤ 10 % der DAT-Matches |

#### M13: Ambiguous Match Rate

| Feld | Wert |
|------|------|
| **Definition** | Anteil der Erkennungen mit HasConflict=true |
| **Formel** | `|HasConflict=true| / |TOTAL|` |
| **Warum wichtig** | Hohe Rate = viele unsichere Entscheidungen; niedrige Rate kann auch bedeuten, dass Konflikte nicht erkannt werden |
| **Ziel** | ≤ 8 % global |

#### M14: Repair-Safe Match Rate

| Feld | Wert |
|------|------|
| **Definition** | Anteil der Erkennungen, deren Qualität für destruktive Operationen (Rename, Rebuild) ausreicht |
| **Formel** | `|{f : DAT-EXACT ∧ Confidence ≥ 95 ∧ ¬HasConflict}| / |TOTAL|` |
| **Warum wichtig** | Definiert die Grenze für zukünftige Features |
| **Ziel** | ≥ 70 % für Referenz-Sets |

### 4.3 Anti-Gaming-Metriken

Diese Metriken verhindern, dass die Hauptmetriken durch aggressive Matching-Strategien künstlich verbessert werden:

#### M15: UNKNOWN→WRONG Migration Rate

| Feld | Wert |
|------|------|
| **Definition** | Bei Build-Vergleich: Dateien, die vorher UNKNOWN waren und jetzt WRONG sind |
| **Formel** | `|{f : prev=UNKNOWN ∧ curr=WRONG}| / |{f : prev=UNKNOWN}|` |
| **Warum wichtig** | Erkennt aggressive Matching-Strategien, die UNKNOWN-Rate senken, aber Fehler einführen |
| **Ziel** | ≤ 2 % pro Build-Diff |

#### M16: Confidence Calibration Error

| Feld | Wert |
|------|------|
| **Definition** | Abweichung zwischen ausgewiesener Confidence und tatsächlicher Korrektheit pro Confidence-Bucket |
| **Formel** | `Σ |bucket_confidence - bucket_accuracy| / num_buckets` (Buckets: 0-39, 40-59, 60-79, 80-89, 90-100) |
| **Warum wichtig** | Prüft ob Confidence-Werte ehrlich sind. „90 % Confidence" sollte ≈ 90 % Korrektheit bedeuten. |
| **Ziel** | ≤ 10 % Calibration Error |

---

## 5. Benchmark-Datensatz

### 5.1 Datensatz-Architektur

```
benchmark/
├── ground-truth.jsonl              ← Wahrheitsdatei (eine Zeile pro Testfall)
├── manifest.json                   ← Datensatz-Metadaten und Versionsinfo
├── reference/                      ← Sauber benannte Referenz-ROMs
│   ├── cartridge/                  ← NES, SNES, N64, GBA, GB, GBC, MD, 32X, Lynx, 7800
│   ├── disc/                       ← PS1, PS2, PS3, PSP, Saturn, Dreamcast, 3DO, GC, Wii
│   ├── bios/                       ← BIOS-Sets aller Systeme
│   └── multi-disc/                 ← Multi-Disc-Sets (CUE+BIN, GDI, M3U)
├── chaos/                          ← Realistische Chaos-Sammlungen
│   ├── renamed/                    ← Falsch benannte Dateien
│   ├── mixed/                      ← Gemischte Sammlungen ohne Ordner-Struktur
│   ├── unicode/                    ← Unicode-Dateinamen, Sonderzeichen
│   ├── truncated/                  ← Gekürzte/verstümmelte Namen
│   └── dupes/                      ← Dubletten mit verschiedenen Namen
├── edge-cases/                     ← Schwierige Fälle
│   ├── cross-system/               ← Gleiche Spielnamen auf verschiedenen Systemen
│   ├── bios-like-games/            ← BIOS mit spielähnlichen Namen
│   ├── headerless/                 ← ROMs ohne Header
│   ├── wrong-extension/            ← Falsche Datei-Extensions
│   ├── corrupt-archives/           ← Kaputte ZIP/7z-Dateien
│   └── ambiguous/                  ← Absichtlich mehrdeutige Fälle
├── negative/                       ← Dateien, die NICHT erkannt werden sollten
│   ├── junk/                       ← Demo, Beta, Proto, Hack, Homebrew
│   ├── non-rom/                    ← TXT, JPG, PDF, EXE
│   ├── misleading/                 ← Irreführende Dateinamen
│   └── empty/                      ← Leere oder kaputte Dateien
└── dat-coverage/                   ← DAT-spezifische Testfälle
    ├── no-intro-verified/          ← Dateien mit bekanntem No-Intro-Hash
    ├── redump-verified/            ← Dateien mit bekanntem Redump-Hash
    ├── hash-collision/             ← Dateien mit identischem Hash in verschiedenen DATs
    └── no-dat-available/           ← Systeme ohne geladenes DAT
```

### 5.2 Datenklassen und Mindestgrösse

#### Referenz-Set (Pflicht)

| Klasse | Mindestanzahl | Systeme | Zweck |
|--------|--------------|---------|-------|
| Cartridge mit Header | 200 | NES, SNES, N64, GBA, GB, GBC, MD, 32X, Lynx, 7800 | Basis-Erkennung via CartridgeHeaderDetector |
| Disc-Images mit PVD | 100 | PS1, PS2, PSP, Saturn, DC, GC, Wii, 3DO | Basis-Erkennung via DiscHeaderDetector |
| BIOS-Dateien | 50 | Mixed | Kategorie-Trennschärfe |
| Folder-sortiert | 100 | Diverse | FolderName-Erkennung |
| Unique Extensions | 120 | Alle mit uniqueExt in consoles.json | Extension-Erkennung |
| DAT-verifiziert | 200 | Mixed | DAT-Matching-Qualität |

**Empfohlen: 1.000 Dateien im Referenz-Set**

#### Chaos-Set (Pflicht)

| Klasse | Mindestanzahl | Beschreibung |
|--------|--------------|-------------|
| Falsch benannt | 100 | „Final Fantasy VII.bin" (könnte PS1, PS2 oder Windows sein) |
| Ohne Ordner-Kontext | 100 | Alle ROMs in einem flachen Ordner |
| Gekürzte Namen | 50 | „FF7_D1.iso", „Pkmn_R.gba" |
| Unicode | 30 | „ファイナルファンタジー.iso", „Pokémon – Édition Rouge.gba" |
| Gemischte Archive | 50 | ZIP mit ROMs verschiedener Systeme |

**Empfohlen: 500 Dateien im Chaos-Set**

#### Edge-Case-Set (Pflicht)

| Klasse | Mindestanzahl | Konkrete Beispiele |
|--------|--------------|-------------------|
| Cross-System-Namenskollision | 30 | „Tetris" (GB, NES, MD, PS1…) |
| BIOS mit Spielnamen | 20 | „PlayStation BIOS (v3.0).bin" vs „PlayStation.rom" |
| Multi-Disc | 30 | CUE+BIN, GDI+Track, M3U+CHD |
| Headerless ROMs | 30 | NES ohne iNES, SNES ohne Header |
| Falsche Extension | 30 | .nes-Datei ist eigentlich MD-ROM |
| Korrupte Archive | 20 | Halb-kaputte ZIPs |
| PS1/PS2 Serial-Ambiguity | 20 | SLUS-Nummern mit 3-5 Digits |
| GB/GBC-Grenzfälle | 20 | CGB-Flag 0x80 vs 0xC0 |
| Genesis/32X-Ambiguity | 15 | Beide nutzen „SEGA" in Header |

**Empfohlen: 250 Dateien im Edge-Case-Set**

#### Negativ-Kontrollen (Pflicht)

| Klasse | Mindestanzahl | Beschreibung |
|--------|--------------|-------------|
| Non-ROM-Dateien | 50 | .txt, .jpg, .pdf, .exe, .dll |
| Junk/Demo/Beta | 50 | Erkannte Junk-Tags |
| Irreführend | 30 | „Nintendo 64 Game.exe", „SNES Classic.pdf" |
| Leere Dateien | 10 | 0 Bytes, nur Header, nur Nullen |

**Empfohlen: 150 Dateien im Negativ-Set**

### 5.3 Praktische Realisierung

**WICHTIG:** Der Benchmark-Datensatz kann NICHT aus echten ROMs bestehen (Copyright). Die Lösung:

1. **Synthetische Header-Stubs:** Dateien mit korrekten Magic Bytes, aber keinem lauffähigen Inhalt (80-512 Bytes pro Datei). Für CartridgeHeaderDetector und DiscHeaderDetector ausreichend.
2. **Echte Dateinamen aus DAT-Files:** No-Intro und Redump DAT-Dateien sind öffentlich verfügbar und enthalten kanonische Dateinamen + Hashes. Diese können als Fixture-Input verwendet werden.
3. **Verzeichnis-Struktur-Fixtures:** Für FolderName-Detection und ConsoleSorter reichen leere Dateien in benannten Ordnern.
4. **Archiv-Stubs:** ZIP-Dateien mit korrekten inneren Dateinamen, aber Minimal-Inhalt.

```
Reale Daten: KEINE echten ROMs im Repository.
Stattdessen: synthetische Stubs + DAT-basierte Metadaten + Fixture-Ordnerstrukturen.
```

### 5.4 Ground Truth Pflege

- **Versionierung:** Git-tracked als `benchmark/ground-truth.jsonl` im Repository
- **Review:** Jede Änderung an Ground Truth erfordert Code-Review
- **Erweiterung:** Neue Testfälle werden über ein standardisiertes Template hinzugefügt
- **Validierung:** CI prüft, dass Ground Truth syntaktisch korrekt ist und alle referenzierten Dateien existieren

---

## 6. Ground Truth Modell

### 6.1 Format: JSONL (eine Zeile pro Testfall)

```jsonl
{
  "id": "ref-cartridge-nes-001",
  "path": "reference/cartridge/Super Mario Bros. (World).nes",
  "set": "reference",
  "expected": {
    "fileType": "ROM",
    "containerType": "single",
    "consoleKey": "NES",
    "category": "Game",
    "gameIdentity": "Super Mario Bros.",
    "region": "WORLD",
    "datMatchLevel": "exact",
    "datGameName": "Super Mario Bros. (World)",
    "sortingDecision": "sort",
    "sortTarget": "NES",
    "repairSafe": true,
    "confidenceClass": "high"
  },
  "tags": ["header-detectable", "no-intro-verified", "unique-extension"],
  "difficulty": "easy",
  "addedVersion": "1.0.0",
  "notes": ""
}
```

### 6.2 Felder

| Feld | Typ | Beschreibung | Pflicht |
|------|-----|-------------|---------|
| `id` | string | Eindeutige, sprechende ID | Ja |
| `path` | string | Relativer Pfad im Benchmark-Verzeichnis | Ja |
| `set` | enum | `reference` / `chaos` / `edge-case` / `negative` / `dat-coverage` | Ja |
| `expected.fileType` | enum | `ROM` / `DiscImage` / `Archive` / `BIOS` / `NonROM` / `Unknown` | Ja |
| `expected.containerType` | enum | `single` / `zip` / `7z` / `multi-file-set` | Ja |
| `expected.consoleKey` | string? | Erwarteter ConsoleKey aus consoles.json oder `null` | Ja |
| `expected.category` | enum | `Game` / `Bios` / `Junk` / `NonGame` / `Unknown` | Ja |
| `expected.gameIdentity` | string? | Erwarteter Spielname oder `null` | Nein |
| `expected.region` | string? | Erwartete Region oder `null` | Nein |
| `expected.datMatchLevel` | enum | `exact` / `strong` / `none` / `not-applicable` | Ja |
| `expected.datGameName` | string? | Erwarteter DAT-Spielname | Nein |
| `expected.sortingDecision` | enum | `sort` / `block` / `not-applicable` | Ja |
| `expected.sortTarget` | string? | Erwarteter Zielordner (ConsoleKey) | Nur wenn sort |
| `expected.repairSafe` | bool | Wäre Repair mit dieser Erkennung sicher? | Ja |
| `expected.confidenceClass` | enum | `high` (≥80) / `medium` (50-79) / `low` (<50) / `any` | Ja |
| `tags` | string[] | Deskriptive Tags für Filterung | Ja |
| `difficulty` | enum | `easy` / `medium` / `hard` / `adversarial` | Ja |
| `addedVersion` | string | SemVer der Ground-Truth-Version | Ja |
| `notes` | string | Erklärung für schwierige Fälle | Nein |

### 6.3 Versionierung und Pflege

```
ground-truth.jsonl wird:
- in Git versioniert (jede Änderung = Commit mit Reason)
- per JSON-Schema validiert (CI-Check)
- NIE automatisch generiert (immer menschlich verifiziert)
- per Code-Review erweitert (kein ungeprüfter Bulk-Import)
```

**Schema-Validierung:** `data/schemas/ground-truth.schema.json` validiert jede Zeile.

**Erweiterungsprozess:**
1. Neuen Testfall als JSONL-Zeile hinzufügen
2. Synthetische Stub-Datei im Benchmark-Verzeichnis erstellen
3. Erwartung manuell verifizieren (Quelle: DAT-File, Header-Spec, manuelle Prüfung)
4. Pull Request mit Begründung

---

## 7. Evaluationspipeline

### 7.1 Technischer Ablauf

```
┌───────────────────────────────────────────────────────────────────────────┐
│                      EVALUATION PIPELINE                                  │
│                                                                           │
│  ┌──────────┐   ┌────────────────┐   ┌──────────────┐   ┌─────────────┐ │
│  │ Benchmark │──→│ Detection Run  │──→│   Comparator  │──→│   Reports   │ │
│  │ Dataset   │   │ (alle Ebenen)  │   │ (vs Ground    │   │ (JSON/HTML/ │ │
│  │ + Ground  │   │                │   │  Truth)       │   │  Console)   │ │
│  │ Truth     │   │                │   │               │   │             │ │
│  └──────────┘   └────────────────┘   └──────────────┘   └─────────────┘ │
│                                                                           │
│  Inputs:          Outputs:             Vergleich:        Artefakte:       │
│  - Stub-Files     - ConsoleKey         - pro Ebene       - Confusion Mx  │
│  - ground-truth   - Confidence         - pro System      - Metrik-Werte  │
│    .jsonl         - Category           - pro Difficulty   - Trend-Diffs  │
│  - DAT-Files      - DatMatch           - pro Set          - Fehler-Liste │
│  - consoles.json  - SortDecision       - aggregiert       - Dashboard    │
│                   - RepairSafe                                            │
└───────────────────────────────────────────────────────────────────────────┘
```

### 7.2 Komponenten

#### A) Benchmark Runner (`EvaluationRunner`)

```
Klasse: Romulus.Tests/Benchmark/EvaluationRunner.cs
Methode: RunBenchmark(benchmarkDir, groundTruthPath, datRoot?) → EvaluationResult

Ablauf:
1. Ground Truth laden und parsen
2. Für jeden Testfall:
   a. ConsoleDetector.DetectWithConfidence() → ConsoleKey, Confidence, HasConflict
   b. FileClassifier.Analyze() → Category, ClassificationConfidence
   c. DatIndex.Lookup/LookupAny() → DatMatch (wenn DAT verfügbar)
   d. Sorting-Entscheidung simulieren (Confidence ≥ 80 ∧ ¬Conflict ∧ Category=Game)
   e. Repair-Tauglichkeit berechnen (DAT-Exact ∧ Confidence ≥ 95 ∧ ¬Conflict)
3. Ergebnisse als EvaluationRecord[] speichern
```

#### B) Evaluation Record

```json
{
  "id": "ref-cartridge-nes-001",
  "actual": {
    "consoleKey": "NES",
    "confidence": 95,
    "hasConflict": false,
    "category": "Game",
    "classificationConfidence": 50,
    "datMatch": true,
    "datGameName": "Super Mario Bros. (World)",
    "sortDecision": "sort",
    "sortTarget": "NES",
    "repairSafe": true,
    "hypotheses": [
      {"console": "NES", "confidence": 95, "source": "UniqueExtension"},
      {"console": "NES", "confidence": 90, "source": "CartridgeHeader"}
    ]
  },
  "verdict": {
    "consoleCorrect": true,
    "categoryCorrect": true,
    "datMatchCorrect": true,
    "sortingCorrect": true,
    "repairSafeCorrect": true,
    "errorType": null
  }
}
```

#### C) Comparator (`GroundTruthComparator`)

```
Klasse: Romulus.Tests/Benchmark/GroundTruthComparator.cs
Methode: Compare(EvaluationRecord[], GroundTruth[]) → ComparisonResult

Vergleich pro Ebene:
- Console: actual.consoleKey vs expected.consoleKey → CORRECT/WRONG/UNKNOWN
- Category: actual.category vs expected.category → CORRECT/WRONG
- DAT: actual.datMatch vs expected.datMatchLevel → EXACT/NONE-MISSED/FALSE-MATCH
- Sort: actual.sortDecision vs expected.sortingDecision → CORRECT-SORT/WRONG-SORT/CORRECT-BLOCK/WRONG-BLOCK
- Repair: actual.repairSafe vs expected.repairSafe → MATCH/MISMATCH
- Confidence: actual.confidence vs expected.confidenceClass → CALIBRATED/OVER/UNDER
```

#### D) Report Generator (`BenchmarkHtmlReportWriter`)

```
Klasse: Romulus.Tests/Benchmark/BenchmarkHtmlReportWriter.cs

Outputs:
1. benchmark-results.json         ← Maschinen-lesbar, für CI/CD
2. benchmark-results.html         ← Menschlich-lesbar, für Review
3. confusion-console.csv          ← Console Confusion Matrix
4. confusion-category.csv         ← Category Confusion Matrix
5. metrics-summary.json           ← Alle M1-M16 Werte
6. error-details.jsonl            ← Jeder einzelne Fehler mit Context
7. trend-comparison.json          ← Diff gegen vorherigen Benchmark-Run
```

### 7.3 Regressionserkennung

```
CI-Integration:

1. Benchmark-Run wird bei jedem PR ausgeführt (als Test-Suite)
2. metrics-summary.json wird gegen baseline-metrics.json verglichen
3. Regressions-Regeln:
   - Wrong Match Rate steigt um > 0,1 % → FAIL
   - Unsafe Sort Rate steigt um > 0,1 % → FAIL
   - False Confidence Rate steigt um > 1 % → WARN
   - Safe Sort Coverage sinkt um > 2 % → WARN
   - Console Precision sinkt um > 1 % für ein System → WARN
   - UNKNOWN→WRONG Migration > 2 % → FAIL
4. baseline-metrics.json wird nur bei explizitem Baseline-Update aktualisiert
```

### 7.4 Trend-Vergleich zwischen Builds

```json
{
  "baseline": "v1.2.0",
  "current": "v1.3.0-dev",
  "changes": {
    "wrongMatchRate": {"baseline": 0.8, "current": 0.6, "delta": -0.2, "verdict": "improved"},
    "safeSortCoverage": {"baseline": 78.3, "current": 81.1, "delta": +2.8, "verdict": "improved"},
    "unknownRate": {"baseline": 12.5, "current": 10.1, "delta": -2.4, "verdict": "improved"},
    "falseConfidenceRate": {"baseline": 4.2, "current": 5.8, "delta": +1.6, "verdict": "REGRESSION"}
  },
  "regressions": ["falseConfidenceRate +1.6pp"],
  "improvements": ["wrongMatchRate -0.2pp", "safeSortCoverage +2.8pp"],
  "overallVerdict": "BLOCKED (regression in falseConfidenceRate)"
}
```

---

## 8. Qualitätsziele

### 8.1 Realistische Zielwerte pro Ebene

Die Ziele sind differenziert nach dem, was physikalisch und technisch erreichbar ist:

#### Konsolenerkennung

| Metrik | Referenz-Set | Chaos-Set | Edge-Case-Set | Begründung |
|--------|-------------|-----------|---------------|------------|
| **Console Precision** | ≥ 98 % | ≥ 93 % | ≥ 85 % | Falsche Zuordnung muss minimal sein |
| **Console Recall** | ≥ 92 % | ≥ 75 % | ≥ 60 % | Chaos hat prinzipielle Grenzen |
| **Wrong Match Rate** | ≤ 0,5 % | ≤ 2 % | ≤ 5 % | Härter bei Referenz, toleranter bei Chaos |
| **Unknown Rate** | ≤ 8 % | ≤ 25 % | ≤ 35 % | Chaos-UNKNOWN ist akzeptabel |

**Warum 95 % Gesamterkennung unrealistisch ist:**
- Systeme ohne Header-Detection (Atari 2600, Amiga, etc.) können nur über Extension/Folder/Keywords erkannt werden → prinzipiell unsicher
- Disc-Images ohne PVD (raw .bin ohne .cue) sind nicht sicher zuzuordnen
- Headerless ROMs (SNES ohne Copier-Header) haben keine deterministische Signatur

**Was „95 %" realistisch bedeuten könnte:**
- 95 % Console Precision im Referenz-Set: **erreichbar**
- 95 % Console Recall im Referenz-Set: **erreichbar (mit Header + Extension + DAT)**
- 95 % Safe Sort Coverage im Referenz-Set: **ambitioniert, aber möglich**
- 95 % über alle Sets und alle Ebenen: **nicht seriös erreichbar**

#### Systeme mit Header-Detection (höhere Ziele möglich)

| System-Typ | Erwartete Precision | Erwarteter Recall | Begründung |
|-----------|-------------------|------------------|------------|
| NES, N64, GBA, GB, GBC, Lynx, 7800 | ≥ 99 % | ≥ 97 % | Deterministische Magic Bytes (iNES, N64-BE, GBA-Logo) |
| SNES | ≥ 97 % | ≥ 90 % | Bimodal Header (LoROM/HiROM), Copier-Header-Varianten |
| Genesis/MD | ≥ 97 % | ≥ 92 % | ASCII „SEGA MEGA DRIVE" ist eindeutig |
| PS1/PS2/PSP | ≥ 96 % | ≥ 88 % | ISO9660 PVD zuverlässig, aber .bin-only schwierig |
| GameCube/Wii | ≥ 98 % | ≥ 95 % | Magic Bytes sind sehr spezifisch |
| Saturn/DC/SegaCD | ≥ 95 % | ≥ 85 % | IP.BIN-Erkennung gut, aber Sektor-Offsets variabel |

#### Systeme ohne Header-Detection (niedrigere Ziele)

| System-Typ | Erwartete Precision | Erwarteter Recall | Begründung |
|-----------|-------------------|------------------|------------|
| Atari 2600/5200/Jaguar | ≥ 90 % | ≥ 70 % | Nur Extension + Folder + DAT |
| Amiga | ≥ 88 % | ≥ 65 % | .adf + Folder, kein Standard-Header |
| MSX/TurboGrafx/PCE | ≥ 90 % | ≥ 70 % | Unique Extension hilft, aber Chaos ist schwierig |
| Diverse MAME-Systeme | ≥ 85 % | ≥ 60 % | Stark DAT-abhängig |

#### Kategorie-Erkennung

| Metrik | Zielwert | Begründung |
|--------|---------|------------|
| GAME-AS-JUNK Rate | ≤ 0,1 % | **Harter Release-Blocker.** Datenverlust-Risiko. |
| BIOS-AS-GAME Rate | ≤ 0,5 % | Verursacht falsche Dedupe-Gruppen |
| JUNK-AS-GAME Rate | ≤ 3 % | Qualitätsverlust, aber kein Datenverlust |
| Overall Category Accuracy | ≥ 95 % | Realistisch, weil Pattern-basiert und gut testbar |

#### DAT-Matching

| Metrik | Zielwert | Begründung |
|--------|---------|------------|
| DAT Exact Match Rate | ≥ 90 % | Für Dateien mit geladenem System-DAT |
| DAT False Match Rate | ≤ 0,5 % | Cross-System-Hash-Collisions |
| DAT None-Missed Rate | ≤ 10 % | Nicht alle Hash-Algorithmen/Formate passen |

#### Sorting

| Metrik | Zielwert | Begründung |
|--------|---------|------------|
| **Unsafe Sort Rate** | **≤ 0,3 %** | **Harter Release-Blocker.** Falsch sortiert = Datenverlust. |
| Safe Sort Coverage (Referenz) | ≥ 80 % | Ausreichend praktischer Nutzen |
| Safe Sort Coverage (Chaos) | ≥ 55 % | Realistische Erwartung bei schlechten Inputs |
| Correct Block Rate | ≥ 95 % | Unsichere Dates müssen blockiert bleiben |

### 8.2 Was „95 % Erkennung" konkret bedeuten könnte

Wenn das Projekt „95 %" als Kommunikationsziel verwenden will, dann nur so:

> **„95 % der ROMs in einer typischen, korrekt benannten Sammlung werden dem richtigen System zugeordnet und sicher sortiert – mit weniger als 0,5 % Fehlzuordnungen."**

Das impliziert:
- Referenz-Set = Maßstab (nicht Chaos)
- Safe Sort Coverage (M8) ≥ 95 % im Referenz-Set
- Wrong Match Rate (M4) ≤ 0,5 %
- Remaining 5 % sind UNKNOWN (korrekt blockiert), nicht WRONG

**Das ist ein ehrliches, erreichbares, messbares Ziel.**

---

## 9. Freigaberegeln

### 9.1 Sorting-Freigabe

Sorting darf nur ausgeführt werden, wenn **alle** folgenden Bedingungen erfüllt sind:

| Bedingung | Schwellenwert | Begründung |
|-----------|--------------|------------|
| DetectionConfidence | ≥ 80 | Bereits implementiert in RunOrchestrator |
| HasConflict | false | Bereits implementiert |
| Category | Game | Non-Game wird nicht sortiert |
| ConsoleKey | ≠ UNKNOWN, nicht leer | Kein Zielordner bestimmbar |

**Bereits implementiert:** ✓ (in `RunOrchestrator.StandardPhaseSteps.RunConsoleSortStep()`)

### 9.2 Repair-Freigabe (zukünftiges Feature)

Repair (Rename, Rebuild, Cross-Root-Sourcing) darf nur freigegeben werden, wenn:

| Bedingung | Schwellenwert | Begründung |
|-----------|--------------|------------|
| DatMatch | exact (Hash-verifiziert) | Rename ohne DAT-Beweis = Datenverlust-Risiko |
| DetectionConfidence | ≥ 95 | Höhere Schwelle als Sorting |
| HasConflict | false | Keine Ambiguität erlaubt |
| Category | Game oder Bios | Kein Junk reparieren |
| Console verifiziert | via DAT oder Header | Keine Folder-Only-Erkennung |

### 9.3 UNKNOWN ist Pflicht, wenn:

| Situation | Ergebnis | Beispiel |
|-----------|---------|---------|
| Confidence < 80 | UNKNOWN (Sort blockiert) | Extension .bin, kein Header, kein Folder |
| HasConflict = true | UNKNOWN (Sort blockiert) | Folder sagt PS1, Serial sagt PS2 |
| Nur AmbiguousExtension (Conf 40) | UNKNOWN | .bin ohne weitere Evidenz |
| Nur FilenameKeyword (Conf 75) | UNKNOWN | „[GBA]" im Dateinamen, aber kein Header |
| Keine Hypothese | UNKNOWN | Unbekannte Dateiendung, kein Header |

### 9.4 Ambiguous Review ist nötig, wenn:

| Situation | Aktion | Beispiel |
|-----------|--------|---------|
| Zwei Systeme mit Confidence ≥ 80 | Benutzer-Review | PS1 (95 via Serial) vs PS2 (92 via Folder) |
| DAT-Match in anderem System als Detection | Benutzer-Review | Detection sagt PS1, DAT sagt PS2 |
| DatMatch exact, aber Confidence < 80 | Automatisch DAT akzeptieren, aber markieren | Hash-Match trotz schlechter Erkennung |
| Mehrere DAT-Matches für gleichen Hash | Benutzer-Review | Hash existiert in PS1-DAT und PS2-DAT |

---

## 10. Konkrete nächste Schritte

### Phase 1: Grundlagen (Wochen 1-2)

| # | Schritt | Aufwand | Blocking? |
|---|---------|---------|-----------|
| 1 | **Ground Truth Schema definieren** (`data/schemas/ground-truth.schema.json`) | Klein | Ja – alles andere baut darauf auf |
| 2 | **50 synthetische Testfälle erstellen** (Header-Stubs für NES, SNES, N64, GBA, GB, GBC, MD, PS1, PS2, DC) | Mittel | Ja – Minimum für ersten Benchmark-Run |
| 3 | **EvaluationRunner als xUnit-Test** implementieren (liest Ground Truth, führt Detection aus, vergleicht) | Mittel | Ja – ohne Runner keine Messung |
| 4 | **Baseline-Metriken messen** (erster Benchmark-Run gegen initiale 50 Fälle) | Klein | Ja – definiert den Startpunkt |
| 5 | **metrics-summary.json als CI-Artefakt** erzeugen | Klein | Nein – aber schnell danach |

### Phase 2: Ausbau (Wochen 3-4)

| # | Schritt | Aufwand | Blocking? |
|---|---------|---------|-----------|
| 6 | **Datensatz auf 200+ Fälle erweitern** (Chaos-Set, Edge-Cases, Negative Controls) | Mittel | Nein – iterativ |
| 7 | **Confusion Matrix Generator** implementieren (Console × Console, Category × Category) | Mittel | Nein – aber hoher diagnostischer Wert |
| 8 | **Regressions-Gate in CI** einbauen (Wrong Match Rate ≤ Baseline + 0,1 %) | Klein | Nein – aber schützt vor Rückschritten |
| 9 | **HTML-Report für Benchmark-Ergebnisse** (menschlich lesbar, pro System aufgeschlüsselt) | Mittel | Nein |
| 10 | **Trend-Vergleich** implementieren (aktueller Run vs. gespeicherte Baseline) | Mittel | Nein |

### Phase 3: Reife (Wochen 5-8)

| # | Schritt | Aufwand |
|---|---------|---------|
| 11 | **Confidence Calibration** messen und visualisieren |
| 12 | **Pro-System-Dashboards** im HTML-Report |
| 13 | **Datensatz auf 500+ Fälle** erweitern (DAT-Coverage, Multi-Disc, Archive-Edge-Cases) |
| 14 | **Anti-Gaming-Metriken** (M15, M16) in CI-Gate aufnehmen |
| 15 | **Repair-Freigabe-Gate** als Feature-Flag vorbereiten |

### Prioritäten-Reihenfolge der ersten 10 Schritte

```
1. Ground Truth Schema            [MUSS – ohne Schema keine Daten]
2. 50 synthetische Testfälle      [MUSS – ohne Daten keine Messung]
3. EvaluationRunner               [MUSS – ohne Runner kein Benchmark]
4. Baseline messen                [MUSS – ohne Baseline kein Vergleich]
5. CI-Artefakt                    [SOLL – automatisiert den Prozess]
6. Datensatz erweitern            [SOLL – verbessert Aussagekraft]
7. Confusion Matrix               [SOLL – identifiziert systematische Fehler]
8. Regressions-Gate               [SOLL – verhindert Rückschritte]
9. HTML-Report                    [KANN – verbessert Sichtbarkeit]
10. Trend-Vergleich               [KANN – zeigt Fortschritt über Zeit]
```

---

## Anhang A: Confidence-Quellen und ihre theoretische Zuverlässigkeit

| Quelle | Confidence | Theoretische Korrektheit | Begründung |
|--------|-----------|------------------------|------------|
| DatHash | 100 | ≈99,99 % | SHA1-Verifikation, nur Hash-Collision als Fehlerquelle |
| UniqueExtension | 95 | ≈98 % | .nes ist fast immer NES, aber .nes-Datei könnte umbenannt sein |
| DiscHeader | 92 | ≈97 % | ISO9660 PVD ist zuverlässig, aber Sektor-Varianten können täuschen |
| CartridgeHeader | 90 | ≈96 % | Magic Bytes sind eindeutig, aber Headerless-ROMs fehlen |
| SerialNumber | 88 | ≈94 % | Redump-Serials sind zuverlässig, aber PS1/PS2-Overlap existiert |
| FolderName | 85 | ≈90 % | Vom Benutzer erstellt – kann falsch sein |
| ArchiveContent | 80 | ≈88 % | Interior-Extension ist ein Proxy – kann irreführen |
| FilenameKeyword | 75 | ≈80 % | User-Tags können falsch, veraltet oder irreführend sein |
| AmbiguousExtension | 40 | ≈55 % | .bin kann 15+ Systeme sein |

## Anhang B: Bekannte Cross-System-Verwechslungspaare

| Paar | Verwechslungsursache | Gegenmaßnahme |
|------|---------------------|---------------|
| PS1 ↔ PS2 | Serial-Overlap (SLUS-xxxxx) | Digit-Count Disambiguierung |
| GB ↔ GBC | CGB-Flag 0x80 (dual-mode) | Konvention: CGB=0x80 → GBC |
| Genesis ↔ 32X | Beide „SEGA" in Header | „SEGA 32X" vs „SEGA MEGA DRIVE" |
| SNES ↔ SFC | Gleiche Hardware, unterschiedliche Regionen | Region-Tag oder DAT-Zuordnung |
| NES ↔ FDS | Ähnliche Header-Signatur | FDS-spezifisches Magic Byte |
| DC ↔ Naomi | Gleiche GD-ROM-Basis | Sector-Content-Analyse |

## Anhang C: ADR-Referenz

Dieses Dokument sollte als Basis für folgende Architecture Decision Records dienen:

- **ADR-015: Recognition Quality Benchmark Framework** – Einführung des Evaluationsframeworks
- **ADR-016: Confidence Calibration and Sorting Gate** – Formalisierung der Freigaberegeln
- **ADR-017: Ground Truth Management** – Versionierung und Pflege der Benchmarkdaten
