# Deep Dive Audit – Recognition / Classification / Sorting

**Datum:** 2026-04-16
**Modus:** Principal Bug Hunter / Domain Auditor (read-only)
**Status:** Round 1 abgeschlossen – Round 2 dringend empfohlen
**Scope:** ROM-Erkennung, Konsolenerkennung, Region/Format/Version-Scoring, GameKey-Normalisierung, Hypothesis-/Decision-Resolution, Winner-Selection, SortDecision-Routing (Sort/Review/Blocked/Unknown), BIOS/Game/Junk/NonGame Trennung, Cross-Root-Dedup.

---

## 1. Executive Verdict

**Nicht release-tauglich in der aktuellen Form.** Die Recognition/Classification/Sorting-Pipeline enthält mindestens **drei P0-Befunde** mit direktem Einfluss auf Determinismus und Korrektheit der Winner-Selection sowie auf die fachliche Wahrheit zwischen Preview/Execute/Report:

1. **GameKey-Berechnung ignoriert `consoleType` und `aliasEditionKeying` in der Produktion** → DOS-Metadata-Stripping ist dead code im Run-Pfad; ein in der GUI sichtbarer und persistierter User-Toggle (`AliasEditionKeying`) ist im tatsächlichen Run wirkungslos.
2. **Mehrfache, teils widersprüchliche Wege zur `DecisionClass`** (`DecisionResolver`, `CandidateFactory.effectiveDecisionClass`-Fallback, `HypothesisResolver.DetermineSortDecision`-Test-Overload) – verletzt direkt die "Eine fachliche Wahrheit"-Regel.
3. **`ConsoleSorter` und `DeduplicationEngine` validieren `ConsoleKey` mit unterschiedlichen Regeln** – ein in Core gültiger Key (`PLAYSTATION 2` mit Leerzeichen) wird im Sorter still als UNKNOWN behandelt → silent mis-routing.

Daneben mindestens fünf P1-Befunde (Schattenlogik in `CrossRootDeduplicator`, statische Regex-Caches in `VersionScorer` mit Reload-Bug, indeterminate `AMBIGUOUS` ohne Hash-Diskriminator in Dedup, etc.) und ein nicht-trivialer Bestand an Hygiene-/Test-Lücken.

Round 1 hat absichtlich nur die zentralen Pfade abgesucht; Round 2 (tiefere Branch-Pfade, ConsoleDetector-Feinheiten, DiscHeaderDetector, Set-Parsing, ConsoleSorter Unknown-/Set-/Hash-Konflikte) ist erforderlich, bevor das Verdict "ausgeschöpft" lauten kann.

---

## 2. Rundenzusammenfassung

### Runde 1 (abgeschlossen)

**Geprüft:**
- [GameKeyNormalizer.cs](src/Romulus.Core/GameKeys/GameKeyNormalizer.cs) – Normalize-Pfade, DOS-Familie, Empty-Key-Sentinel, Pattern-Registrierung
- [RegionDetector.cs](src/Romulus.Core/Regions/RegionDetector.cs) – Multi-Lang, Comma-Region, Token-Fallback, Normalize-Map
- [FormatScorer.cs](src/Romulus.Core/Scoring/FormatScorer.cs) – Format/Region/SizeTieBreak/Header-Scores, Caches, DOSDIR
- [VersionScorer.cs](src/Romulus.Core/Scoring/VersionScorer.cs) – Pure-letter/dotted/numeric/leading-digit Pfade, Lang-Bonus, Saturating Math, Pattern-Reload
- [DeduplicationEngine.cs](src/Romulus.Core/Deduplication/DeduplicationEngine.cs) – SelectWinner Tie-Breaker-Kette, BuildGroupKey, NormalizeConsoleKey, Cross-Group-Filter
- [HypothesisResolver.cs](src/Romulus.Core/Classification/HypothesisResolver.cs) – Resolve, AMBIGUOUS-Branch, Soft-Cap, Single-Source-Cap, Conflict-Penalty, ConflictType-Klassifikation
- [FileClassifier.cs](src/Romulus.Core/Classification/FileClassifier.cs) – BIOS-Erkennung (Compact-Names + Prefix), Junk/NonGame Patterns, Non-ROM-Extensions, Generic-Raw-Binary-Gate
- [CandidateFactory.cs](src/Romulus.Core/Classification/CandidateFactory.cs) – BIOS-Key-Isolation, DecisionClass-Fallback
- [ConsoleDetector.cs](src/Romulus.Core/Classification/ConsoleDetector.cs) – Folder/Keyword/Vendor-Stripping, LRU-Cache
- [ConsoleSorter.cs](src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs) – Phase-2 Single-Source-Of-Truth, Routing pro SortDecision
- [CrossRootDeduplicator.cs](src/Romulus.Infrastructure/Deduplication/CrossRootDeduplicator.cs) – `GetMergeAdvice` lokale Re-Derivation
- [EnrichmentPipelinePhase.cs](src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs) – `gameKey`-Bildung, DecisionResolver-Aufrufe

**Neue Findings Runde 1:** 19 (P0×3, P1×9, P2×4, P3×3)
**Weitere Runde nötig?** Ja. Round 2 muss insbesondere abdecken:
- DiscHeaderDetector / CartridgeHeaderDetector – Header-Magic-Konflikte
- ConsoleDetector vollständiger Detection-Stage-Flow (`DetectWithConfidence`)
- Set-Parsing (CUE/GDI/CCD/M3U/MDS) – Verlust von Disc-Mitgliedern bei Sort/Review/Blocked
- `ConsoleSorter` Conflict-Policy ("Overwrite") und Atomic-Set-Move bei Failure
- `RunOrchestrator.PreviewAndPipelineHelpers` Projection-Mapping (Preview vs Execute Parität)
- `EnrichmentPipelinePhase` ab Zeile 200 (DAT-Lookup-Stages, Family-Pipeline-Selektoren)

---

## 3. Findings

### F-RCS-001 — `aliasEditionKeying` und `consoleType` werden in der Produktion nie an `GameKeyNormalizer.Normalize` übergeben
- **Schweregrad:** P0
- **Typ:** Bug + Shadow Logic + Konkurrierende Wahrheit
- **Impact:** (a) Der UI-/Settings-Toggle `AliasEditionKeying` ist persistiert, validiert, in den ViewModels gebunden – aber **funktional wirkungslos** im Run. Das ist ein klassisches "Schein-Feature". (b) `GameKeyNormalizer.IsDosFamilyConsole(...)` und `RemoveMsDosMetadataTags(...)` sind sorgfältig entwickelt (50-Iter-Cap, mehrere Console-Key-Varianten) – aber im Produktions-Enrichment **nie erreichbar**: DOS-ROMs mit `[DOS]`-/`(MSDOS)`-Tags werden nicht normalisiert → unterschiedliche GameKeys für identische Spiele → falsche Dedup-Gruppen → potenzieller Datenverlust.
- **Datei(en):** [src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs:175-178](src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs#L175-L178), [src/Romulus.Core/GameKeys/GameKeyNormalizer.cs:233-282](src/Romulus.Core/GameKeys/GameKeyNormalizer.cs#L233-L282)
- **Reproduktion:**
  ```csharp
  // Die einzige Produktions-Normalisierung:
  var gameKey = GameKeyNormalizer.Normalize(
      fileName,
      GameKeyNormalizationProfile.TagPatterns ?? [],
      GameKeyNormalizationProfile.AlwaysAliasMap);
  // ↑ kein consoleType, kein editionAliasMap, kein aliasEditionKeying
  ```
  GameKey wird **vor** der Konsolen-Detektion gebildet (Zeile 175 vs. Detektion danach). Damit ist es architektonisch unmöglich, `consoleType` an dieser Stelle korrekt zu kennen.
- **Ursache:** Reihenfolge im Enrichment (GameKey vor Console-Detection); zusätzlich werden Settings (`AliasEditionKeying`, `EditionAliasMap`) nicht in `GameKeyNormalizationProfile` durchgereicht.
- **Fix:**
  1. GameKey nach Console-Detection berechnen, ODER zweistufig: Vor-Detection-Key plus Re-Key nach Detection wenn `consoleType` DOS-Familie.
  2. `GameKeyNormalizationProfile` um `EditionAliasMap` und `AliasEditionKeying` erweitern und im Enrichment durchreichen.
  3. Entweder das UI-Feature wirklich anschließen oder den Toggle entfernen (kein Schein-Feature zulassen).
- **Testabsicherung:**
  - Unit: GameKey für `"Game [DOS]"` mit `consoleType="DOS"` ≠ ohne consoleType (existiert in `GameKeyNormalizerCoverageTests`).
  - Integration: Enrichment einer DOS-Datei → resultierender `Candidate.GameKey` darf keine `[DOS]`-Reste enthalten (fehlt).
  - Integration: Enrichment mit `AliasEditionKeying=true` und Edition-Alias `"goty edition" → "base"` muss Edition-Alias anwenden (fehlt vollständig).

### F-RCS-002 — Drei konkurrierende Pfade zur `DecisionClass`/`SortDecision`
- **Schweregrad:** P0
- **Typ:** Duplication + Shadow Logic
- **Impact:** Verletzt die "Single Source of Truth"-Regel direkt. Inkonsistente Auflösung kann Preview vs. Execute divergieren lassen.
- **Datei(en):**
  - Pfad A (kanonisch): [src/Romulus.Core/Classification/HypothesisResolver.cs:264-266](src/Romulus.Core/Classification/HypothesisResolver.cs#L264-L266) – `DecisionResolver.Resolve(primaryTier, hasConflict, winnerConfidence, datAvailable, conflictType, hasHardEvidence)`
  - Pfad B (Test-Overload): [src/Romulus.Core/Classification/HypothesisResolver.cs:294-316](src/Romulus.Core/Classification/HypothesisResolver.cs#L294-L316) – `DetermineSortDecision(int confidence, bool conflict, bool hardEvidence, int sourceCount, bool hasDatEvidence = false)` rekonstruiert Tier aus Rohwerten – Logikduplikat mit eigenen Schwellwerten.
  - Pfad C (Factory-Fallback): [src/Romulus.Core/Classification/CandidateFactory.cs:48-50](src/Romulus.Core/Classification/CandidateFactory.cs#L48-L50) – `effectiveDecisionClass = decisionClass == DecisionClass.Unknown && sortDecision != SortDecision.Unknown ? sortDecision.ToDecisionClass() : decisionClass;` – impliziter Fallback der eigene Wahrheit erzeugt, wenn Caller `DecisionClass.Unknown` aber konkretes `SortDecision` übergibt.
- **Reproduktion:** Caller übergibt `decisionClass=Unknown, sortDecision=Sort` → Factory liefert `DecisionClass=Sort` ohne dass jemals `DecisionResolver` lief. In einem anderen Codepfad mit gleichen Eingangsdaten kann `DecisionResolver` aber zu `Review` kommen.
- **Ursache:** Halbfertiger Refactor – `DetermineSortDecision`-Overloads "for existing tests" stehen geblieben; CandidateFactory hat einen Fallback zur Vermeidung von Aufrufer-Nachlässigkeit eingebaut, statt den Aufrufer-Vertrag zu härten.
- **Fix:**
  1. Pfad B vollständig löschen oder als `[Obsolete]` markieren und Tests auf `DecisionResolver` umziehen.
  2. Pfad C entfernen; CandidateFactory soll bei inkonsistenter Eingabe **failen** (ArgumentException), nicht still derive.
- **Testabsicherung:** Invariant-Test: Für jede Input-Kombination liefern alle drei Pfade dasselbe Ergebnis (oder nur Pfad A ist erlaubt).

### F-RCS-003 — `ConsoleKey`-Validierung in Core und Sorting widersprechen sich
- **Schweregrad:** P0
- **Typ:** Bug + Konkurrierende Wahrheit
- **Impact:** Ein Console-Key, der Core/`DeduplicationEngine.NormalizeConsoleKey` passiert (erlaubt Buchstaben/Ziffern/Bindestrich/Unterstrich/**Leerzeichen**), wird im `ConsoleSorter` von `RxValidConsoleKey = ^[A-Z0-9_-]+$` (kein Leerzeichen!) verworfen → die Datei landet still in `_UNKNOWN/`, obwohl Core sie sauber zugeordnet hat. Silent mis-routing.
- **Datei(en):**
  - [src/Romulus.Core/Deduplication/DeduplicationEngine.cs:181-194](src/Romulus.Core/Deduplication/DeduplicationEngine.cs#L181-L194) – akzeptiert Leerzeichen
  - [src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs:18](src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs#L18) – verwirft Leerzeichen
- **Reproduktion:** `consoles.json` mit `"key": "PLAYSTATION 2"` (oder ein Alias-Pfad, der in der Enrichment als Leerzeichen-Key durchfließt). Core gruppiert korrekt → Sorter routet nach `_UNKNOWN/`.
- **Ursache:** Zwei separate, unkoordinierte Validierungen; keine zentrale `ConsoleKeyPolicy`.
- **Fix:** Eine zentrale `ConsoleKeyPolicy.IsValid()` / `Normalize()` in `Romulus.Core`, die Core und Sorter beide nutzen. Plus: Console-Loader (`ConsoleDetector.LoadFromJson`) bereits beim Laden validieren und Keys mit Leerzeichen entweder normalisieren (`PLAYSTATION_2`) oder als Schemafehler ablehnen.
- **Testabsicherung:** Invariant-Test: Für jeden geladenen Console-Key gilt `Sorter.IsValid(key) == Core.NormalizeConsoleKey(key) != "UNKNOWN"`.

### F-RCS-004 — `VersionScorer`-Instanzen "frieren" das Sprach-Pattern beim Konstruktor ein
- **Schweregrad:** P1
- **Typ:** Bug
- **Impact:** Wenn rules.json zur Laufzeit reloaded wird (z.B. via `RegisterLanguagePatternFactory`), nutzen vorhandene `VersionScorer`-Instanzen weiterhin das alte Pattern. In einem langlaufenden Run (GUI/API) entstehen so "alte" und "neue" Scores nebeneinander → silent Determinismus-Verlust zwischen Runs.
- **Datei(en):** [src/Romulus.Core/Scoring/VersionScorer.cs:36-45](src/Romulus.Core/Scoring/VersionScorer.cs#L36-L45) (Konstruktor liest `ResolveLanguagePattern()` einmalig)
- **Ursache:** Statische Pattern-Registry + instance-cached Regex.
- **Fix:** Entweder `VersionScorer` static machen (analog zu `FormatScorer`), oder Pattern-Cache invalidieren, wenn Factory neu registriert wird.
- **Testabsicherung:** Test der `RegisterLanguagePatternFactory` nach Konstruktion eines Scorers aufruft und prüft, dass nachfolgende `GetVersionScore`-Aufrufe das neue Pattern nutzen.

### F-RCS-005 — `CrossRootDeduplicator.GetMergeAdvice` re-derived GameKey ohne Profile
- **Schweregrad:** P1
- **Typ:** Shadow Logic + Konkurrierende Wahrheit
- **Impact:** Cross-Root-Merge-Empfehlungen können andere Winner liefern als die zentrale Dedup, weil GameKey hier ohne Tag-Patterns/Aliase berechnet wird.
- **Datei(en):** [src/Romulus.Infrastructure/Deduplication/CrossRootDeduplicator.cs:107](src/Romulus.Infrastructure/Deduplication/CrossRootDeduplicator.cs#L107) – `GameKey = GameKeyNormalizer.Normalize(Path.GetFileNameWithoutExtension(file.Path))`
- **Ursache:** Die Convenience-Overload-`Normalize(string)` wird verwendet; sie greift auf statisch registrierte Defaults zurück. Wenn diese in Tests nicht registriert sind, läuft die Logik mit leerem Pattern-Set – nicht-deterministisch je nach Test-Reihenfolge.
- **Fix:** GameKey aus `RomCandidate`/Projection übernehmen statt neu zu berechnen. CrossRoot soll den bereits gebildeten Key konsumieren, nicht nochmal normalisieren.
- **Testabsicherung:** Test ohne `RegisterDefaultPatterns` muss deterministisch identisches Verhalten liefern; ODER der Codepfad muss explizit auf den Enrichment-Key zugreifen.

### F-RCS-006 — `AMBIGUOUS` und `Conflict`-Console werden in Dedup nicht hash-diskriminiert
- **Schweregrad:** P1
- **Typ:** Bug
- **Impact:** `BuildGroupKey` fügt einen Hash-Diskriminator nur hinzu, wenn `consoleKey == "UNKNOWN"` ODER GameKey mit `__BIOS__UNKNOWN__` beginnt. Ein vom HypothesisResolver gelieferter `consoleKey == "AMBIGUOUS"` (siehe HypothesisResolver Zeile 158-167) wird **nicht** als unsicherer Konsolenstatus erkannt → zwei tatsächlich unterschiedliche AMBIGUOUS-Files können kollabiert werden.
- **Datei(en):** [src/Romulus.Core/Deduplication/DeduplicationEngine.cs:163-178](src/Romulus.Core/Deduplication/DeduplicationEngine.cs#L163-L178)
- **Ursache:** `needsContentDiscriminator`-Branch kennt nur "UNKNOWN", nicht "AMBIGUOUS".
- **Fix:** Liste der "indeterminate console states" zentralisieren (`ConsoleKeyPolicy.IsIndeterminate(key)`) und sowohl in HypothesisResolver als auch DeduplicationEngine konsumieren.
- **Testabsicherung:** Regression-Test: Zwei AMBIGUOUS-Files mit identischem GameKey aber unterschiedlichem Hash dürfen nicht in einer Dedup-Gruppe landen.

### F-RCS-007 — `ConsoleSorter`-Skip auf Root-Ebene maskiert per-File-Diagnose
- **Schweregrad:** P1
- **Typ:** Bug + Bedienbarkeit
- **Impact:** Wenn `enrichedConsoleKeys` oder `enrichedSortDecisions` für **einen Root** null ist, wird der **gesamte Root** geskippt mit einem einzigen Audit-Warning – aber `total += files.Count; skipped += files.Count;` zählt alle als skip ohne dem User pro Datei mitzuteilen, dass nichts passiert ist. Bei partiellem Enrichment-Failure (z.B. Permission-Probleme nur für eine Datei) wird die Diagnose auf "ganzer Root übersprungen" reduziert.
- **Datei(en):** [src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs:115-141](src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs#L115-L141)
- **Ursache:** Skip-Logik granular pro File ist im Loop weiter unten korrekt umgesetzt (Unknown-Branch bei `missing-sort-decision`), aber der äußere Pre-Check kürzt zu früh ab.
- **Fix:** Pre-Checks entfernen oder in eine reine Warnung wandeln; das per-File-Branching unten reicht aus.
- **Testabsicherung:** Test mit `enrichedSortDecisions` für 3 von 5 Dateien → 3 sortiert, 2 zu Unknown mit klarem Reason, **nicht** alle 5 geskippt.

### F-RCS-008 — `RegionDetector` Region-Normalisierungsmap inkomplett für Asien
- **Schweregrad:** P1
- **Typ:** Bug
- **Impact:** Korea (`KR`), China (`CN`), Taiwan (`TW`), Hong Kong (`HK`), Asia (`AS`) bleiben unnormalisiert und fließen als raw `rule.Key` in den Region-Score → unvorhersagbares Ranking je nachdem ob der Caller diese in `preferOrder` listet.
- **Datei(en):** [src/Romulus.Core/Regions/RegionDetector.cs:179-191](src/Romulus.Core/Regions/RegionDetector.cs#L179-L191) – switch ohne Asia-Codes; nur EU-Länder sind gefoldet.
- **Ursache:** Erweiterungen wurden für Europa-Länder ergänzt, Asien wurde übersehen.
- **Fix:** Entweder Asia-Codes als eigene Region (`AS`) folden, oder explizit in der Datenbasis (`rules.json`) als first-class Regions mit Score-Definition pflegen. Entscheidung dokumentieren.
- **Testabsicherung:** Test pro Asia-Code, dass der erkannte Region-Key konsistent in `FormatScorer.GetRegionScore(...)` einen definierten (nicht den 200-Default) Wert liefert.

### F-RCS-009 — `FormatScorer.GetSizeTieBreakScore` enthält Magic-String `"DOSDIR"`
- **Schweregrad:** P1
- **Typ:** Hygiene + Hidden Coupling
- **Impact:** Der String `"DOSDIR"` ist im Switch hardcodiert (Zeile 303) und wird nur in `DatasetExpander` (Test-Code) erzeugt. Es gibt weder eine zentrale Konstante noch eine Definition wo `DOSDIR` eigentlich entsteht. Wenn ein zukünftiger DOS-Set-Detektor einen anderen Key (`"DOSSET"`, `"DOSSETLIKE"`) liefert, fällt die Datei in den Cartridge-Branch (`-1 * sizeBytes`) → Set wird kleinstes statt größtes Element bevorzugen → falscher Winner.
- **Datei(en):** [src/Romulus.Core/Scoring/FormatScorer.cs:303](src/Romulus.Core/Scoring/FormatScorer.cs#L303)
- **Fix:** Eine `SetTypes`-Konstanten-Klasse in `Romulus.Contracts` mit allen Set-Typ-Strings; FormatScorer und Set-Parser konsumieren beide diese Konstanten.
- **Testabsicherung:** Bestehende `[InlineData("DOSDIR")]` deckt nur den Happy-Path. Negative Tests für unbekannte Set-Typen ergänzen.

### F-RCS-010 — `HypothesisResolver` Soft-Cap und Single-Source-Cap nacheinander – Reihenfolge intransparent
- **Schweregrad:** P1
- **Typ:** Logic Gap / Subtilität
- **Impact:** Im single-source + soft-only Fall greifen **beide** Caps. Welcher gewinnt, hängt von der Implementierungsreihenfolge ab (aktuell: erst Soft-Cap, dann Single-Source-Cap). Wenn einer der Caps in Zukunft geändert wird, kann sich die effektive Confidence still ändern.
- **Datei(en):** [src/Romulus.Core/Classification/HypothesisResolver.cs:236-250](src/Romulus.Core/Classification/HypothesisResolver.cs#L236-L250)
- **Fix:** Einen `EffectiveCap = Min(SoftOnlyCap, SingleSourceCap, AggregateBonusCap)` als expliziten Berechnungsschritt einführen statt sequentielle `Math.Min`-Anwendungen.
- **Testabsicherung:** Tabellen-Test (Theory) mit Source × isSoftOnly × Sources.Count → erwartete effektive Cap.

### F-RCS-011 — Empty-Key-Sentinel kollidiert in beiden Empty-Pfaden
- **Schweregrad:** P1
- **Typ:** Logic Gap
- **Impact:** Zwei unterschiedliche Eingaben können beide einen `__empty_key_<4-byte-hash>` produzieren. Das Sentinel-Suffix nutzt nur 4 Bytes (32 Bit) der SHA-256 → Geburtstagskollision bei ~65k Files möglich. Bei großen ROM-Sammlungen (Romulus-Zielsetzung: produktionsnah) realistisch.
- **Datei(en):** [src/Romulus.Core/GameKeys/GameKeyNormalizer.cs:265,287](src/Romulus.Core/GameKeys/GameKeyNormalizer.cs#L265)
- **Ursache:** Suffix-Länge zu kurz für die Skala der Anwendung.
- **Fix:** 8 Byte (64 Bit) verwenden, ODER full-hex SHA-1 (20 Byte). Performance-Impact vernachlässigbar.
- **Testabsicherung:** Stress-Test mit 100k synthetischen Inputs, deren Tag-Stripping zu leerem Key führt – darf keine Sentinel-Kollision erzeugen.

### F-RCS-012 — `FileClassifier`-BIOS-Erkennung false-positive-prone bei "BIOS Boot Disc"
- **Schweregrad:** P1
- **Typ:** Logic Gap
- **Impact:** `KnownBiosFalsePositivePrefixes` enthält `"bios boot disc"`, `"bios test rom"` etc. – aber diese Liste ist **manuell gepflegt** und race-condition-anfällig: Sobald `RxBios` (`\bboot[._ -]?rom\b`, `^\s*bios(?:\s|-|\.|\d|$)`) zuschlägt, wird die False-Positive-Liste nur via `ShouldTreatAsBios`-Vorprüfung beachtet. Aber `RxBios` matched z.B. `"BIOS Test ROM (USA)"` weil `\bboot[._ -]?rom\b` kein Anker hat – wait, `boot.rom` matched nicht "Test ROM". OK. Aber `^\s*bios(?:\s|...)` matched `"BIOS Test ROM"` direkt → würde als BIOS klassifiziert. Schutz: `KnownBiosFalsePositivePrefixes` hat `"bios test rom"` → Prefix-Check hält. ABER: Der NormalizeTokenStream entfernt Punktuation und collapsed Whitespace. `"BIOS_Test_Rom_v1"` wird zu `"bios test rom v1"`, prefix-check gegen `"bios test rom"` greift → korrekt False-Positive abgewehrt. Aber `"BIOS Tester (USA)"` collapsed zu `"bios tester usa"` → kein Prefix-Treffer → `RxBios` matched (`^\s*bios(?:\s|\d|...)`) → False-Positive **als BIOS klassifiziert**.
- **Datei(en):** [src/Romulus.Core/Classification/FileClassifier.cs:85-93](src/Romulus.Core/Classification/FileClassifier.cs#L85-L93)
- **Fix:** Strenger `RxBios`-Pattern (Token-Boundary statt loose `\s|\d`); ODER positiv-Liste statt negativ-Liste (BIOS nur, wenn explizit `(BIOS)`/`[BIOS]`-Tag oder bekannter Compact-Name).
- **Testabsicherung:** Negative Tests mit `"BIOS Tester"`, `"BIOSophy"`, `"Biosphere"`, `"BiosShock"` → muss `Game`, nicht `Bios`.

### F-RCS-013 — `DeduplicationEngine.NormalizeConsoleKey` produziert "UNKNOWN" bei jedem Sonderzeichen still
- **Schweregrad:** P2
- **Typ:** Bug + Hygiene
- **Impact:** Ein Console-Key mit Slash, Komma, Klammer (z.B. fehlerhaft gepflegtes `consoles.json`) wird still zu UNKNOWN konvertiert. Kein Logging, keine Audit-Spur. Bei ungetesteter JSON-Änderung verschwinden alle Files dieser Konsole in der UNKNOWN-Gruppe → silent Datenverlust durch Mis-Dedup.
- **Datei(en):** [src/Romulus.Core/Deduplication/DeduplicationEngine.cs:181-194](src/Romulus.Core/Deduplication/DeduplicationEngine.cs#L181-L194)
- **Fix:** `Trace.TraceWarning` bei Fallback auf UNKNOWN; ODER besser: schon im JSON-Loader (`ConsoleDetector.LoadFromJson`) Schema validieren und harte Fehler werfen.
- **Testabsicherung:** Loader-Test mit invalidem Key → Exception oder strukturierte Validation-Diagnose.

### F-RCS-014 — `EnrichmentPipelinePhase` berechnet GameKey vor Konsolen-Detektion
- **Schweregrad:** P0 (architektonisch; verstärkt F-RCS-001)
- **Typ:** Architektur-Schwäche
- **Impact:** Solange GameKey vor Console bekannt ist, ist konsole-spezifische Normalisierung (DOS, Arcade-Parent/Clone, MAME-Set-Naming) prinzipiell unmöglich.
- **Datei(en):** [src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs:175-195](src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs#L175-L195)
- **Fix:** Pipeline-Stage-Reihenfolge umstellen: Detection → Classification → GameKey. Aktuell: GameKey → Classification → Detection.
- **Testabsicherung:** Architektur-Test, der prüft, dass `gameKey` erst nach `consoleKey` zugewiesen wird (Sequence-Invariant).

### F-RCS-015 — `RegionScoreCache` LRU mit Hardcoded-Limit (100), kein Eviction-Logging
- **Schweregrad:** P3
- **Typ:** Hygiene
- **Impact:** Bei vielen unterschiedlichen `preferOrder`-Permutationen (theoretisch in Multi-Tenant-API möglich) Cache-Thrashing ohne Diagnose.
- **Datei(en):** [src/Romulus.Core/Scoring/FormatScorer.cs:24-26](src/Romulus.Core/Scoring/FormatScorer.cs#L24-L26)
- **Fix:** Limit als Konstante zentralisieren; optional Trace-Counter für Cache-Misses.
- **Testabsicherung:** Nicht erforderlich auf P3-Level.

### F-RCS-016 — `FormatScorer.FallbackFormatScores` enthält `.dmg` (mehrdeutig: GameBoy-Dump vs macOS DiskImage)
- **Schweregrad:** P3
- **Typ:** Hygiene
- **Impact:** Ein macOS-`.dmg`-File würde Format-Score 600 bekommen und als ROM gewertet werden, falls `.dmg` nicht als NonRomExtension geblockt ist. `FileClassifier.NonRomExtensions` listet `.dmg` **nicht** → tatsächlich erreichbar.
- **Datei(en):** [src/Romulus.Core/Scoring/FormatScorer.cs:108](src/Romulus.Core/Scoring/FormatScorer.cs#L108), [src/Romulus.Core/Classification/FileClassifier.cs:155-163](src/Romulus.Core/Classification/FileClassifier.cs#L155-L163)
- **Fix:** Disambiguation an Header/Magic-Bytes binden; `.dmg` nicht blind als ROM scoren.

### F-RCS-017 — `CandidateFactory.Create` mit 25+ Default-Parametern
- **Schweregrad:** P2
- **Typ:** Hygiene + Bug-Anfälligkeit
- **Impact:** Caller können wichtige Parameter still vergessen (`hasHardEvidence: false`, `isSoftOnly: true` als unsichere Defaults). In Kombination mit F-RCS-002 erzeugt ein vergessener `decisionClass`-Parameter den dort beschriebenen Fallback.
- **Datei(en):** [src/Romulus.Core/Classification/CandidateFactory.cs:13-43](src/Romulus.Core/Classification/CandidateFactory.cs#L13-L43)
- **Fix:** Builder-Pattern oder zwei strikt typisierte Sub-Records (`DetectionEvidence`, `ClassificationEvidence`) als Pflichtparameter.

### F-RCS-018 — `HypothesisResolver` Penalty-Cap-Floor `Math.Max(30, ...)`
- **Schweregrad:** P2
- **Typ:** Logic Gap
- **Impact:** Nach Conflict-Penalty wird `winnerConfidence` nie unter 30 fallen – auch dann nicht, wenn der Penalty-Berechnung eigentlich "Confidence vernichten" wollte. Das maskiert echte Konflikte mit einem Mindest-Score, der für Review-Threshold reichen kann.
- **Datei(en):** [src/Romulus.Core/Classification/HypothesisResolver.cs:225](src/Romulus.Core/Classification/HypothesisResolver.cs#L225)
- **Fix:** Floor entfernen oder dokumentieren warum 30 (nicht 0). Wenn 30 begründet ist (z.B. "wir wissen wenigstens dass *eine* Konsole existiert"), als benannte Konstante extrahieren.

### F-RCS-019 — `ConsoleSorter`-Diagnose-Reasons sind freie Strings ohne Konstante
- **Schweregrad:** P3
- **Typ:** Hygiene
- **Impact:** Reasons wie `"missing-enriched-console-keys"`, `"missing-enriched-sort-decisions"`, `"missing-sort-decision"` sind als Magic Strings verstreut. Reports/Locale-Files müssen sie kennen, ohne dass eine zentrale Definition existiert.
- **Datei(en):** [src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs:120,134,154](src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs#L120)
- **Fix:** `SortReasonCodes`-Konstanten-Klasse zentralisieren.

---

## 4. Dubletten / Schattenlogik

| # | Schattenlogik | Pfad A (kanonisch) | Pfad B/C (schatten) |
|---|---|---|---|
| 1 | DecisionClass-Ableitung | `DecisionResolver.Resolve` | `CandidateFactory.effectiveDecisionClass`, `HypothesisResolver.DetermineSortDecision` (test-only) |
| 2 | ConsoleKey-Validierung | `DeduplicationEngine.NormalizeConsoleKey` | `ConsoleSorter.RxValidConsoleKey` |
| 3 | GameKey-Normalisierung | `GameKeyNormalizer.Normalize(name, patterns, aliases)` | `CrossRootDeduplicator.GetMergeAdvice` Convenience-Overload, `SetupViewModel`/`MainViewModel.Settings` Preview-Calls |
| 4 | Region/Format/Version-Re-Scoring | `EnrichmentPipelinePhase` (Authoritative) | `CrossRootDeduplicator.GetMergeAdvice` (lokale Re-Berechnung wenn 0) |
| 5 | Set-Typ-Strings | (keiner – Magic Strings) | `FormatScorer` switch, `DatasetExpander`, ConsoleSorter |
| 6 | "Indeterminate Console State" | (keiner) | `DeduplicationEngine.BuildGroupKey` kennt nur "UNKNOWN", HypothesisResolver kennt zusätzlich "AMBIGUOUS" |

---

## 5. Hygiene-Probleme

- **Magic Strings an kritischen Stellen:** `"DOSDIR"`, `"M3USET"`, `"GDISET"`, `"CUESET"`, `"CCDSET"`, `"AMBIGUOUS"`, `"UNKNOWN"`, `"missing-enriched-*"` – siehe F-RCS-009, F-RCS-019.
- **Dead Code im Produktionspfad:** `GameKeyNormalizer.IsDosFamilyConsole` + `RemoveMsDosMetadataTags` – funktional unerreichbar (siehe F-RCS-001).
- **Schein-Feature im UI:** `AliasEditionKeying`-Toggle ohne Wirkung (siehe F-RCS-001).
- **Test-Only-Overload in Production-Class:** `HypothesisResolver.DetermineSortDecision(int, bool, bool, int, bool)` – `[Obsolete]` oder weg (siehe F-RCS-002).
- **Übergroße Default-Parameter-Liste:** `CandidateFactory.Create` 25+ Defaults (siehe F-RCS-017).
- **Manuell gepflegte BIOS-False-Positive-Liste** ohne Datenquellen-Verankerung (siehe F-RCS-012).
- **Mehrere Pattern-Registry-Singletons** (`GameKeyNormalizer`, `RegionDetector`, `VersionScorer`, `FormatScorer`) mit jeweils eigenem `ResetForTesting()` und Locking-Schema – Kandidat für eine zentrale `RuleProfileRegistry`.

---

## 6. Kritische Testlücken

1. **Kein Integrationstest** für `EnrichmentPipelinePhase` mit DOS-Familie + DOS-spezifischer GameKey-Normalisierung. Konsequenz: F-RCS-001 nicht erkannt.
2. **Kein Architektur-Invariant-Test**, dass `gameKey` in `EnrichmentPipelinePhase` erst nach `consoleKey` zugewiesen wird (F-RCS-014).
3. **Kein Invariant-Test**, dass alle in `consoles.json` definierten Keys von `ConsoleSorter.RxValidConsoleKey` und `DeduplicationEngine.NormalizeConsoleKey` einheitlich akzeptiert werden (F-RCS-003).
4. **Kein Reload-Test** für `VersionScorer` / `RegionDetector` / `GameKeyNormalizer` Pattern-Factories nach erstem Resolve (F-RCS-004).
5. **Keine Negativ-Tests** für BIOS-False-Positives wie `"BIOS Tester"`, `"BiosShock"`, `"Biosphere"` (F-RCS-012).
6. **Kein End-to-End-Test** Preview vs Execute auf Set-Members mit Sort=Review (Werden CUE-Tracks korrekt mitbewegt? Was passiert bei partial Move-Failure?).
7. **Kein Stress-Test** für Empty-Key-Sentinel Geburtstagskollisionen (F-RCS-011).
8. **Kein Test** für `enrichedConsoleKeys` partial null pro File (nicht pro Root) – ConsoleSorter Per-File-Routing mit gemischter Coverage (F-RCS-007).
9. **Keine `DetermineSortDecision`-Konsistenz-Tests** zwischen Pfad A/B/C (F-RCS-002).
10. **Keine `AMBIGUOUS`-vs-`UNKNOWN`-Dedup-Diskriminierungs-Tests** (F-RCS-006).

---

## 7. Schlussurteil

**Das Audit ist NICHT ausgeschöpft.** Round 1 hat den zentralen kalten Pfad (Normalize → Detect → Classify → Resolve → Decide → Score → SelectWinner → Sort) abgedeckt und 19 Findings identifiziert, davon 3 P0. Begründung für offene Round 2:

- **Nicht geprüft in Tiefe:** `DiscHeaderDetector`, `CartridgeHeaderDetector`, `ConsoleDetector.DetectWithConfidence` (komplette Stage-Abfolge mit Hash/Header/Folder/Keyword), Set-Parsing (`SetParsing/`), `RunOrchestrator.PreviewAndPipelineHelpers` (Preview-vs-Execute-Mapping), `ConsoleSorter`-Conflict-Policy, atomare Set-Move-Failure-Behandlung, `EnrichmentPipelinePhase` ab Zeile 200 (DAT-Lookup-Stages, Family-Pipeline-Selektoren, Header-Hash-Strategie).
- **Bekannte Risiko-Bereiche aus Memory:** `EnrichmentPipelinePhase.LookupDat` ignoriert `FamilyDatPolicy.EnableCrossConsoleLookup` (vorheriges Audit dokumentiert) – muss in Round 2 erneut auf Recognition-Auswirkung geprüft werden.
- **Noch nicht beleuchtet:** Race-Conditions in den statischen Pattern-Registries beim parallelen Erstellen mehrerer `VersionScorer`-Instanzen (Konstruktor + concurrent `RegisterDefaultLanguagePattern`).

**Empfehlung für nächste Schritte (in dieser Reihenfolge):**
1. **F-RCS-001 / F-RCS-014 zuerst fixen** – betrifft Determinismus und Schein-Feature.
2. **F-RCS-002, F-RCS-003 als nächstes** – konkurrierende Wahrheiten beseitigen, bevor weitere Logik draufgesetzt wird.
3. **Round 2 starten** mit Fokus auf DiscHeaderDetector, ConsoleDetector-Stage-Flow, Set-Parsing, RunOrchestrator-Preview-Mapping.
4. Erst nach Abschluss Round N (zero-finding-runde) Implementation-Phase planen.

**Confidence:** 0.78 – hoch für die identifizierten Findings (alle mit Datei:Zeile-Evidenz und Reproduktionspfad belegt), aber die Gesamtabdeckung ist bewusst nur ~50% des Scopes. Eine zweite Runde ist nicht optional.


---

## 6. Runde 2 (abgeschlossen)

**Datum:** 2026-04-16 (Folgesitzung)
**Geprüft (zusätzlich zu Runde 1):**
- [DiscHeaderDetector.cs](src/Romulus.Core/Classification/DiscHeaderDetector.cs) – ScanDiscImage, ScanChdMetadata, ResolveConsoleFromText, DetectBatch (reparse-point check), 18 Regex-Pattern, X360-vs-XBOX-Differenzierung
- [CartridgeHeaderDetector.cs](src/Romulus.Core/Classification/CartridgeHeaderDetector.cs) – iNES/N64/MD/GB/GBA/SNES/A78/Lynx Header-Magic, IsLikelySnesMapMode Allow-List, ScanSnesHeader Copier-Header-Heuristik
- [ConsoleDetector.cs](src/Romulus.Core/Classification/ConsoleDetector.cs#L344) – `DetectWithConfidence` Stage-Flow (Folder → UniqueExt → AmbigExt → DiscHeader → ArchiveContent → CartridgeHeader → SerialNumber → KeywordDynamic), PS1→PS2 Header-Downgrade, ArchiveEntry-Priorisierung
- [CueSetParser.cs](src/Romulus.Core/SetParsing/CueSetParser.cs), [GdiSetParser.cs](src/Romulus.Core/SetParsing/GdiSetParser.cs), [CcdSetParser.cs](src/Romulus.Core/SetParsing/CcdSetParser.cs), [M3uPlaylistParser.cs](src/Romulus.Core/SetParsing/M3uPlaylistParser.cs), [MdsSetParser.cs](src/Romulus.Core/SetParsing/MdsSetParser.cs), [SetDescriptorSupport.cs](src/Romulus.Core/SetParsing/SetDescriptorSupport.cs), [SetParserIoResolver.cs](src/Romulus.Core/SetParsing/SetParserIoResolver.cs)
- [EnrichmentPipelinePhase.cs](src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs#L200) ab Zeile 200 – DAT-Lookup-Stages (Archive-Inner → Headerless → Container → CHD-Sha1 → Name-only Stages 4a/4b), `IsDatAvailableForConsole`, `TryPolicyAwareDatLookup`, Hash-Ordering (`GetLookupHashTypeOrder`)
- [RunOrchestrator.PreviewAndPipelineHelpers.cs](src/Romulus.Infrastructure/Orchestration/RunOrchestrator.PreviewAndPipelineHelpers.cs#L350) – Partial-Metadata-Sidecar bei Cancel/Failure
- Aufrufer-Kette `PipelinePhaseHelpers.GetSetMembers` (5 Aufrufer)

**Neue Findings Runde 2:** 14 (P0×2, P1×7, P2×4, P3×1)
**Weitere Runde nötig?** Ja (Runde 3): Ein Komplettlauf produzierte erneut blockierende Befunde. Noch nicht in Tiefe: `HypothesisResolver.Resolve` Tie-Breaker bei gleicher Confidence, `FilenameConsoleAnalyzer.DetectBySerial` Pattern-Konflikte, `MovePipelinePhase`/`JunkRemovalPipelinePhase` Set-Member-Behandlung, `ConversionPhaseHelper` Set-Member-Re-Hash, `StreamingScanPipelinePhase` `includeM3uMembers:false`-Asymmetrie, `RunOrchestrator` Preview-vs-Execute-Decision-Replay (vollständig), Atomare Set-Move Failure-Pfade in `MoveSetAtomically`.

---

## 7. Findings (Runde 2)

### F-RCS-020 — Reparse-Point-Schutz fehlt im Produktions-Detektor-Pfad
- **Schweregrad:** P0
- **Typ:** Sicherheit / Datenintegritaet
- **Impact:** `DiscHeaderDetector.DetectFromDiscImage`, `DetectFromChd` und `CartridgeHeaderDetector.Detect` lesen Dateien ohne `FileAttributes.ReparsePoint`-Check. `ConsoleDetector.DetectWithConfidence` (Methods 4 + 6) ruft genau diese Methoden – der produktive Detektor folgt Symlinks/Junctions transparent. Direkter Verstoss gegen die Projektregel "Reparse Points nicht transparent folgen". Nur `DiscHeaderDetector.DetectBatch` hat den Guard – wird im Run aber nicht genutzt.
- **Datei(en):** [src/Romulus.Core/Classification/DiscHeaderDetector.cs:75,99](src/Romulus.Core/Classification/DiscHeaderDetector.cs#L75), [src/Romulus.Core/Classification/CartridgeHeaderDetector.cs:84](src/Romulus.Core/Classification/CartridgeHeaderDetector.cs#L84), [src/Romulus.Core/Classification/ConsoleDetector.cs:380,404](src/Romulus.Core/Classification/ConsoleDetector.cs#L380)
- **Reproduktion:** `mklink /J C:\rom-fake C:\Windows\System32` und `iso`-Datei dort hineinlegen → Detektor liest beliebige Pfade.
- **Ursache:** Reparse-Point-Check wurde nur fuer Batch-Detection implementiert; Single-File-Pfad kennt den Schutz nicht.
- **Fix:** `IClassificationIo.GetAttributes(path)` am Anfang aller Single-File-Probe-Methoden pruefen; bei `ReparsePoint` -> `null` zurueckgeben. Im `ConsoleDetector` zentral vor allen Header-/Archive-Methoden absichern, damit alle Detektor-Backends gleichzeitig geschuetzt sind.
- **Testabsicherung:** Unit-Test mit Mock-`IClassificationIo`, der `FileAttributes.ReparsePoint` zurueckgibt; verifiziert, dass `DetectFromDiscImage`/`DetectFromChd`/`CartridgeHeaderDetector.Detect`/`ConsoleDetector.DetectWithConfidence` `null`/`UNKNOWN` liefert ohne Stream-Read.

### F-RCS-021 — `Set`-Parser folgen ebenfalls Reparse Points
- **Schweregrad:** P0
- **Typ:** Sicherheit / Datenintegritaet
- **Impact:** `CueSetParser`, `GdiSetParser`, `CcdSetParser`, `M3uPlaylistParser`, `MdsSetParser` lesen Set-Beschreibungs-Dateien und referenzierte Member-Pfade ohne Reparse-Point-Check. Eine praeparierte CUE/M3U-Datei kann ueber Symlinks Pfade ausserhalb des Roots referenzieren – die existierenden Path-Traversal-Guards (`StartsWith(normalizedDir, ...)`) schuetzen NICHT vor Symlinks innerhalb des Roots, die auf externe Ziele zeigen. `MoveSetAtomically` und `JunkRemovalPipelinePhase` operieren danach auf den (resolvten) Memberpfaden.
- **Datei(en):** [src/Romulus.Core/SetParsing/CueSetParser.cs:38](src/Romulus.Core/SetParsing/CueSetParser.cs#L38), [src/Romulus.Core/SetParsing/M3uPlaylistParser.cs:54](src/Romulus.Core/SetParsing/M3uPlaylistParser.cs#L54), [src/Romulus.Core/SetParsing/GdiSetParser.cs:30](src/Romulus.Core/SetParsing/GdiSetParser.cs#L30), [src/Romulus.Core/SetParsing/CcdSetParser.cs:14](src/Romulus.Core/SetParsing/CcdSetParser.cs#L14), [src/Romulus.Core/SetParsing/MdsSetParser.cs:13](src/Romulus.Core/SetParsing/MdsSetParser.cs#L13)
- **Reproduktion:** CUE-Datei mit `FILE "..\..\sensitive\file" BINARY` haengt am Path-Traversal-Guard, aber `FILE "track01.bin"` mit `track01.bin` als Junction auf C:\Windows\... wird unbemerkt durchgelassen.
- **Ursache:** `ISetParserIo.Exists` und `ReadLines` haben keine Reparse-Point-Semantik.
- **Fix:** `ISetParserIo` um `IsReparsePoint(path)`-Methode erweitern, in jedem Parser nach `Exists` und vor `Add` zur Member-Liste pruefen.
- **Testabsicherung:** Integration-Test mit echtem Junction (Windows) der Pruefung gegen Member-Liste.

### F-RCS-022 — `RxPs2Boot = "BOOT2\s*=|cdrom0:"` triggert PS1->PS2-Falschklassifikation
- **Schweregrad:** P1
- **Typ:** Determinismus / Datenintegritaet
- **Impact:** `cdrom0:` ist KEIN PS2-eindeutiges Token – Sony-PS1-Discs nutzen oft `cdrom0:\\SLES_xxx.xx;1` und `cdrom:\\` in `SYSTEM.CNF`. `ResolveConsoleFromText` matcht `cdrom0:` und gibt **PS2** zurueck statt PS1, sobald gleichzeitig `PLAYSTATION` im Boot-Sektor steht (was bei jeder Sony-Disc der Fall ist). Resultat: PS1-ISOs werden mit `byHeader == "PS2"` klassifiziert; downstream wird der bereits in F-RCS-002 angesprochene `ShouldDowngradeGenericPs1Header`-Pfad NICHT greifen, weil die Richtung umgekehrt verlaeuft (PS2 ueber Folder-Hint downgrades aufs PS1 – aber hier ist nicht Folder=PS2 sondern Header=PS2).
- **Datei(en):** [src/Romulus.Core/Classification/DiscHeaderDetector.cs:39](src/Romulus.Core/Classification/DiscHeaderDetector.cs#L39), [src/Romulus.Core/Classification/DiscHeaderDetector.cs:178-188](src/Romulus.Core/Classification/DiscHeaderDetector.cs#L178)
- **Reproduktion:** Boot-Text `Sony Computer Entertainment Inc. cdrom0:\\SLES_001.45;1` -> `RxPlayStation` matcht -> dann `RxPs2Boot.IsMatch` matcht via `cdrom0:` -> Rueckgabe `PS2`.
- **Ursache:** Marker zu generisch. Korrekt waere PS2-Sentinel `BOOT2\s*=` (im PS2-spezifischen `SYSTEM.CNF`); `cdrom0:` ist kein Differenzierer.
- **Fix:** `cdrom0:` aus `RxPs2Boot` entfernen; auf `BOOT2\s*=` allein setzen oder zusaetzlich PS2-spezifisches `IOPRP\d+\.IMG` verlangen.
- **Testabsicherung:** Regressionstest mit Real-PS1-`SYSTEM.CNF`-String -> erwartet `PS1`, nicht `PS2`. Bestehende PS2-Tests muessen weiterhin gruen sein (mit echtem `BOOT2 =` Token).

### F-RCS-023 — X360-Differenzierung im Header-Probe-Fenster ist faktisch tot
- **Schweregrad:** P1
- **Typ:** Determinismus
- **Impact:** Nach `MICROSOFT*XBOX*MEDIA`-Treffer bei Offset `0x10000` sucht der Code `RxXbox360Marker` (`XBOX 360|XEX2|XGD2|XGD3`) NUR im ersten 8-KB-Header-Block. XEX2 (Executable-Magic), XGD2/XGD3-Marker liegen in echten X360-Dumps weit jenseits 8 KB (XGD3-Datapartition typischerweise ab `0xFD90000`). Die Faellung "X360 vs XBOX" basiert daher praktisch immer auf XBOX – X360-Dumps werden falsch als XBOX klassifiziert. Der Code-Kommentar behauptet das Gegenteil ("um false positives zu vermeiden") und ueberkompensiert.
- **Datei(en):** [src/Romulus.Core/Classification/DiscHeaderDetector.cs:251-260](src/Romulus.Core/Classification/DiscHeaderDetector.cs#L251)
- **Ursache:** Fehlinterpretation der Marker-Verteilung im X360-Image-Layout.
- **Fix:** Entweder den Probe-Bereich gezielt am XGD2/XGD3-Partition-Offset ansetzen (separater seek+read) oder die XBOX-vs-X360-Differenzierung an die XEX2-Erkennung im echten Dateistream auslagern (`fs.Seek(0xFD90000)`, `Read 4096`, suchen). Alternativ: nur dann X360 zurueckgeben, wenn die Datei-Groesse die fuer X360 typische Range trifft (>= ~6.8 GB DVD-DL) **und** Dateinamens-Heuristik X360 nahelegt.
- **Testabsicherung:** Test mit synthetischem 7-GB-Dummy-Stream (sparse) inkl. XGD3-Marker an realistischer Position; aktuell schlaegt der Test fehl, da der Marker nicht gefunden wird.

### F-RCS-024 — `IsLikelySnesMapMode`-Allow-List unvollstaendig
- **Schweregrad:** P1
- **Typ:** Datenintegritaet
- **Impact:** SNES Map-Mode-Bytes `0x36` (ExHiROM SuperFX), `0x37` (ExLoROM in einigen Varianten), `0x38`, `0x39`, `0x3B`, `0x3C` werden NICHT als gueltige SNES-Header erkannt. Folge: legitime SNES-ROMs (z. B. SuperFX-Titel, ExHiROM-Sondereditionen) fallen aus der Header-Detektion durch und landen unklassifiziert in der Pipeline. Dedup-Winner-Selection kann dann auf Folder-Hint-only zurueckfallen.
- **Datei(en):** [src/Romulus.Core/Classification/CartridgeHeaderDetector.cs:218-222](src/Romulus.Core/Classification/CartridgeHeaderDetector.cs#L218)
- **Ursache:** Allow-List wurde aus einer kleinen Sample-Menge abgeleitet, ohne offizielle SNES-DevKit-Tabelle abzubilden.
- **Fix:** Allow-List gegen die offizielle SNES-Spezifikation abgleichen (`0x20`-`0x3F` mit Bit-Maske `0x3X`) oder noch besser: Map-Mode-Whitelist durch positive Pruefung `(mapMode >= 0x20 && mapMode <= 0x3F)` plus zusaetzliche Checksum-Komplement-Validierung ersetzen (Komplement-Check liefert die eigentliche Sicherheit).
- **Testabsicherung:** Unit-Test mit ExHiROM-`0x36`-Header + gueltigem Komplement -> erwartet `SNES`. Aktuell gibt der Detektor `null`.

### F-RCS-025 — Cartridge-Header-Coverage-Luecken (SMS, GG, PC-Engine, VB, Pico)
- **Schweregrad:** P1
- **Typ:** Datenintegritaet / Coverage
- **Impact:** `CartridgeHeaderDetector` deckt NES, N64, Lynx, GBA, A78, MD/32X, GB/GBC, SNES ab. Es fehlen: Sega Master System (TMR-SEGA-Header bei `0x7FF0`), Game Gear (gleicher SDSC/TMR-Header), PC Engine HuCard (selten Header, aber Magic moeglich), Virtual Boy (`VUE-SYSTEMRAM\0` an `0xFFFFFDE0` der ROM-Bank), Sega Pico. Konsequenz: Cart-only-Sets dieser Konsolen verlassen sich auf Folder-Hint, was bei Folder-Hint-Konflikten zu UNKNOWN/AMBIGUOUS fuehrt. Bei Hash-DAT-Treffern unkritisch; bei reinen Sammlungen ohne DAT problematisch.
- **Datei(en):** [src/Romulus.Core/Classification/CartridgeHeaderDetector.cs](src/Romulus.Core/Classification/CartridgeHeaderDetector.cs)
- **Ursache:** Iterative Implementierung, keine vollstaendige Konsolen-Coverage-Matrix.
- **Fix:** Coverage-Matrix dokumentieren und SMS/GG/VB-Header-Magics ergaenzen.
- **Testabsicherung:** Pro neuer Konsole: Header-Probe-Test mit Magic-Bytes + Negativtest mit identischen Bytes an falschem Offset.

### F-RCS-026 — Path-Traversal-Guard in `CcdSetParser` fehlt
- **Schweregrad:** P2
- **Typ:** Hygiene / Schattenlogik
- **Impact:** `CueSetParser`, `GdiSetParser`, `M3uPlaylistParser` haben den `fullPath.StartsWith(normalizedDir, ...)`-Guard. `CcdSetParser` baut Companion-Pfade ueber `Path.Combine(dir, baseName + ext)` ohne `Path.GetFullPath`-Normalisierung des Eingangspfads und ohne Traversal-Guard. Der reale Angriffsvektor ist klein, da `baseName` aus dem CCD-Dateinamen selbst stammt – aber das Pattern weicht von den anderen Parsern ab und ist eine Inkonsistenz, die bei Refactor uebersehen werden kann.
- **Datei(en):** [src/Romulus.Core/SetParsing/CcdSetParser.cs:14-30](src/Romulus.Core/SetParsing/CcdSetParser.cs#L14)
- **Fix:** Konsistenter Aufbau wie in `MdsSetParser` (`Path.GetFullPath` des Inputs, Companion via `Path.Combine` + `GetFullPath`, optional `StartsWith`-Guard).
- **Testabsicherung:** Symmetrie-Test, der fuer alle Parser ueberprueft, dass relative Inputs identisch normalisiert werden.

### F-RCS-027 — `M3uPlaylistParser` fuegt nicht-existente Pfade in `visited` ein
- **Schweregrad:** P2
- **Typ:** Determinismus
- **Impact:** Im Pfad `existingOnly: false` wird `visited.Add(refPath)` auch fuer Pfade aufgerufen, die zum Parse-Zeitpunkt nicht existieren. Wenn dieselbe Datei spaeter im Lauf erstellt wird (z. B. nach Conversion), filtert `GetMissingFiles` sie ueber den nachgelagerten `Where(f => !parserIo.Exists(f))` zwar wieder raus – aber semantisch ist `visited` ueberladen (sowohl "schon prozessiert" als auch "schon im result"). Edge-Cases bei Mehrfach-M3U-Verschachtelung mit Soft-Race koennen zu fehlenden Members fuehren.
- **Datei(en):** [src/Romulus.Core/SetParsing/M3uPlaylistParser.cs:96-100](src/Romulus.Core/SetParsing/M3uPlaylistParser.cs#L96)
- **Fix:** `visited` strikt fuer Recursion-Cycle-Detection; result-Dedup ueber separates `HashSet`.
- **Testabsicherung:** Unit-Test fuer M3U mit duplizierten Eintraegen ueber Subplaylists.

### F-RCS-028 — Zwei Truths fuer Dreamcast-Detection
- **Schweregrad:** P2
- **Typ:** Schattenlogik
- **Impact:** `DiscHeaderDetector` hat ZWEI Regex fuer Dreamcast: `RxIpDreamcast = SEGA.SEGAKATANA|SEGA.DREAMCAST` (verwendet in `ScanDiscImage`) und `RxDreamcast = SEGA.SEGAKATANA|SEGA.?DREAMCAST|SEGA\s*KATANA|DREAMCAST` (verwendet in `ResolveConsoleFromText`). Der zweite Regex ist breiter. Eine Disc kann je nachdem, welcher Pfad anschlaegt, unterschiedliche Treffer-Wahrscheinlichkeiten haben. Mini-Schattenlogik. Identisches Muster fuer Saturn (`RxIpSaturn` vs `RxSaturn`) und Sega-CD (`RxIpSegaCd` vs `RxSegaCd`).
- **Datei(en):** [src/Romulus.Core/Classification/DiscHeaderDetector.cs:23,46-49](src/Romulus.Core/Classification/DiscHeaderDetector.cs#L23)
- **Fix:** Eine Pattern-Quelle pro Konsole. Wenn Boot-Sektor-Scan strenger sein soll, ueber separaten Confidence-Wert ausdruecken, nicht ueber zweites Regex-Pattern.
- **Testabsicherung:** Eine Disc-Text-Fixture, beide Pfade liefern dasselbe Ergebnis.

### F-RCS-029 — `IsDatAvailableForConsole` benutzt Hypothese, die nicht zur DAT-Lookup-Console passt
- **Schweregrad:** P1
- **Typ:** Determinismus / Schattenlogik
- **Impact:** Wenn `consoleKey` UNKNOWN/AMBIGUOUS ist, fragt die Methode die TOP-Hypothese ab und gibt `true` zurueck, sobald `datIndex.HasConsole(topHypothesis)` stimmt. Der nachfolgende `LookupDat` benutzt aber im Cross-Console-Pfad ALLE DATs und kann auf eine ANDERE Konsole landen als die Hypothese. `DecisionResolver.Resolve` bekommt `datAvailable=true` basierend auf einer Konsole, die nicht zwingend die ist, gegen die wirklich gematcht wurde. -> moegliche Inkonsistenz Decision vs Match.
- **Datei(en):** [src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs:734-754](src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs#L734)
- **Fix:** `IsDatAvailableForConsole` strikt gegen die FINAL aufgeloeste Konsole pruefen (nach DAT-Lookup), nicht praediktiv gegen Hypothesen.
- **Testabsicherung:** Test: detector-Hypothese=PS1, DAT enthaelt nur Saturn, Cross-Console-Lookup hittet Saturn -> `datAvailable` muss fuer Saturn pruefen, nicht PS1.

### F-RCS-030 — Doppelter Detector-Aufruf im DAT-Match-Pfad (Performance)
- **Schweregrad:** P3
- **Typ:** Hygiene / Performance
- **Impact:** `EnrichmentPipelinePhase.BuildEnrichedCandidate` ruft `DetectWithConfidence` zweimal in disjunkten Branches auf: einmal als "parity-detection" wenn `datResult.DatMatch && detectionResult is null` (Zeile 232), einmal im DAT-fallthrough (`!datResult.DatMatch`, Zeile 244). Die Branches schliessen sich aus – kein doppelter Call pro Datei. Aber: `_folderDetectCache` (LRU 65536) hilft nur fuer den Folder-Pfad; Disc/Cart-Header haben eigene Caches. Bei DAT-Match ist die Zweit-Detection oft unnoetig (Family-Information liesse sich aus DAT-Console plus `consoleDetector.GetPlatformFamily` ableiten).
- **Datei(en):** [src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs:232-242](src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs#L232)
- **Fix:** Bei DAT-Match Family direkt aus `consoleDetector.GetPlatformFamily(consoleKey)` ableiten und den parity-Detection-Pfad nur dann ausfuehren, wenn der ConflictType wirklich gebraucht wird (d. h. fuer cross-family-Validierung).
- **Testabsicherung:** Performance-Microbenchmark: DAT-only Run ohne Detector-Calls verifizieren.

### F-RCS-031 — `GdiSetParser` parts-Branch verliert Filenames mit Spaces
- **Schweregrad:** P1
- **Typ:** Datenintegritaet
- **Impact:** Im `else`-Zweig (kein Quote im GDI-Eintrag) wird via `trimmed.Split(' ', RemoveEmptyEntries)` zerlegt und `parts[4]` als Filename genommen. Filename mit Leerzeichen ohne Quotes -> Filename wird abgeschnitten -> Member-Datei wird nicht erkannt -> Set wird unvollstaendig erkannt -> CompletenessScore zu niedrig -> Winner-Selection kippt potentiell zu unvollstaendigem Set. GDI-Spezifikation verlangt zwar Quoten bei Spaces; nicht alle Tools beachten das.
- **Datei(en):** [src/Romulus.Core/SetParsing/GdiSetParser.cs:60-64](src/Romulus.Core/SetParsing/GdiSetParser.cs#L60)
- **Fix:** Bei Spaces immer Quoten annehmen oder ein Index-basierter Joiner ueber alle Tokens >= 4 bis zum letzten Token (Offset) als Filename interpretieren.
- **Testabsicherung:** Regressionstest mit GDI-Eintrag `1 0 4 2352 track 01.bin 0` -> Filename muss `track 01.bin` werden.

### F-RCS-032 — `M3uPlaylistParser.MaxDepth=20` ist Magic Number ohne zentrale Config
- **Schweregrad:** P2
- **Typ:** Hygiene
- **Impact:** Die Tiefenbegrenzung steht als `private const int MaxDepth = 20` direkt im Parser. Andere Parser haben keine eigene Tiefe. Werte sind nicht in `data/defaults.json` oder einer zentralen Konstante gepflegt. Ein Ops-relevanter Schwellwert sollte zentral konfigurierbar sein.
- **Datei(en):** [src/Romulus.Core/SetParsing/M3uPlaylistParser.cs:13](src/Romulus.Core/SetParsing/M3uPlaylistParser.cs#L13)
- **Fix:** In `data/defaults.json` als `setParsing.maxM3uDepth` aufnehmen oder mindestens als `internal const` in `SetDescriptorSupport` zentralisieren.
- **Testabsicherung:** Vorhandene M3U-Tiefen-Tests aktualisieren, damit sie den zentralen Wert beziehen.

### F-RCS-033 — `DetectByZipContent` swallowt `InvalidDataException` ohne Telemetrie
- **Schweregrad:** P2
- **Typ:** Datenintegritaet / Hygiene
- **Impact:** `DetectByZipContent` faengt `InvalidDataException`/`IOException`/`UnauthorizedAccessException` und gibt `null` zurueck. Kein `onProgress`/`AddLog`/Diagnose. Der User sieht im Run-Report eine Datei als UNKNOWN ohne Hinweis, dass das Zip korrupt war oder Berechtigungsfehler vorlagen. Ein corrupt-archive-Befund ist aus Datenintegritaets-Sicht relevant (User koennte das Set neu beschaffen wollen).
- **Datei(en):** [src/Romulus.Core/Classification/ConsoleDetector.cs:494-498](src/Romulus.Core/Classification/ConsoleDetector.cs#L494)
- **Fix:** Aufruferseitig (z. B. `ConsoleDetector.DetectWithConfidence`) optionalen `Action<string,Exception>` fuer Diagnose-Channel durchreichen und im catch-Block aufrufen. Im Run-Report dann als "archive-inspection-failed" zaehlen.
- **Testabsicherung:** Test mit absichtlich korruptem Zip -> Diagnose-Callback wird einmal aufgerufen.

---

## 8. Aktualisierte Schattenlogik-Tabelle (nach Runde 2)

| # | Schattenlogik | Pfad A (kanonisch) | Pfad B/C (schatten) |
|---|---|---|---|
| 1 | DecisionClass-Ableitung | `DecisionResolver.Resolve` | `CandidateFactory.effectiveDecisionClass`, `HypothesisResolver.DetermineSortDecision` (test-only) |
| 2 | ConsoleKey-Validierung | `DeduplicationEngine.NormalizeConsoleKey` | `ConsoleSorter.RxValidConsoleKey` |
| 3 | GameKey-Normalisierung | `GameKeyNormalizer.Normalize(name, patterns, aliases)` | `CrossRootDeduplicator.GetMergeAdvice` Convenience-Overload, `SetupViewModel`/`MainViewModel.Settings` Preview-Calls |
| 4 | Sega-Header-Patterns | `RxDreamcast`/`RxSaturn`/`RxSegaCd` (text scan) | `RxIpDreamcast`/`RxIpSaturn`/`RxIpSegaCd` (ISO scan, abweichende Patterns) – F-RCS-028 |
| 5 | Reparse-Point-Schutz | `DiscHeaderDetector.DetectBatch` | Single-File-Pfade (`DetectFromDiscImage`/`DetectFromChd`/`CartridgeHeaderDetector.Detect`/SetParser) – F-RCS-020/F-RCS-021 |
| 6 | DAT-Available-Predicate | `IsDatAvailableForConsole` (auf Hypothese) | tatsaechlicher `LookupDat`-Cross-Console-Pfad (Final-Console) – F-RCS-029 |

---

## 9. Aktualisiertes Schlussurteil

**Status nach Runde 2:** weiterhin nicht release-tauglich. Runde 2 hat zwei zusaetzliche P0 (Reparse Points) und sieben P1-Befunde produziert; das Audit ist daher **nicht abgeschlossen**.

**Headline-Risks aus Runde 2:**
1. **Reparse-Point-Bypass** im gesamten Detector- und SetParser-Pfad – Sicherheitsregel verletzt, real ausnutzbar mit harmloser User-Berechtigung.
2. **PS1->PS2-Falschklassifikation** durch `cdrom0:`-Marker – Winner-Selection und Sort-Routing direkt betroffen.
3. **X360-Differenzierung praktisch tot** – fachliche Wahrheit zwischen Reports und User-Erwartung divergiert.

**Round 3 Pflicht-Scope:**
- `HypothesisResolver.Resolve` Tie-Breaker bei numerisch gleicher Confidence (Determinismus-Kern – noch nicht in Tiefe geprueft)
- `MovePipelinePhase` und `JunkRemovalPipelinePhase` Set-Member-Behandlung (Set-Atomicity bei partial-failure)
- `MoveSetAtomically` Failure-Pfade (Audit-Schreibung, Rollback-Konsistenz)
- `ConversionPhaseHelper` Set-Member-Re-Hash und Source-Cleanup-Reihenfolge (Conversion-Regel "Source nie vor Verifikation entfernen")
- `StreamingScanPipelinePhase` `includeM3uMembers:false`-Asymmetrie vs. `EnrichmentPipelinePhase` `includeM3uMembers:true` (potentiell konkurrierende Set-Wahrheiten in Streaming-Pfad)
- `RunOrchestrator` Preview-vs-Execute-Decision-Replay vollstaendig (Preview/Execute-Paritaet)
- `FilenameConsoleAnalyzer.DetectBySerial` Pattern-Konflikte und Cross-Console-Serial-Kollisionen

**Confidence (gestiegen durch Runde 2):** 0.82.

**Was funktioniert (Runde 2):**
- Path-Traversal-Guards in `CueSetParser`/`GdiSetParser`/`M3uPlaylistParser` (Pattern korrekt umgesetzt, bis auf `CcdSetParser`).
- M3U-Recursion-Limit + Cycle-Detection ist solide implementiert.
- DiscHeader-Pre-Buffer (80 Bytes) fuer GC/Wii/Wii U/3DO ist effizient und korrekt.
- GBA/GB-Logo-Verifikation ueber vollstaendigen Logo-Bytestream ist robust und schwer zu spoofen.
- SNES-Komplement-Checksum-Verifikation `(complement ^ checksum) == 0xFFFF` ist die richtige Schicht-2-Validierung.
- DAT-Lookup-Stages haben klare Reihenfolge (archive-inner -> headerless -> container -> CHD-data-sha1 -> name-only) mit konsistenter `MatchKind`-Annotation.
- `IsCrossConsoleResolution` Logik ist sauber dokumentiert (F-DAT-02-Kommentar) und behandelt UNKNOWN/AMBIGUOUS/empty einheitlich.
- `GetLookupHashTypeOrder` ist deterministisch und behandelt SHA256-Reachability korrekt (F-DAT-13).

---
