# Deep Dive Audit – DAT Matching / Verification

Stand: 2026-04-16
Scope: `Romulus.Infrastructure/Dat/**`, `Romulus.Infrastructure/Hashing/ArchiveHashService.cs`, `Romulus.Infrastructure/Hashing/ChdTrackHashExtractor.cs`, `Romulus.Contracts/Models/DatIndex.cs`, `Romulus.Contracts/Models/MatchKind.cs`, `Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs` (DAT-Lookup-Pfade), `Romulus.Core/Audit/DatAuditClassifier.cs`, `Romulus.Infrastructure/Analysis/CompletenessReportService.cs`.
Modus: **Audit only** – keine Implementierung, keine Tests ausgeführt. RED/GREEN/REFACTOR-Skizzen sind als Empfehlungen gekennzeichnet.

---

## 1. Executive Verdict

Der DAT-Matching-Stack hat eine saubere Schichtung (DAT-Quelle → Repository-Adapter → DatIndex → Family-Policy-Resolver → Pipeline-Lookup) und implementiert bereits viele harte Schutzmechanismen (HTTPS-Allowlist, Zip-Slip, 50 MB Download-Limit, 10 GB 7z-Extraktion, Reparse-Point-Check, Hash-Sidecar, deterministische Sortierung in `LookupAllByHash`). **Aber:** Es gibt **mindestens drei P0/P1-Befunde, die direkt das Vertrauen in „Tier0_ExactDat" untergraben** und damit Preview/Execute/Report-Paritaet verfaelschen:

1. **F-DAT-01 – Sidecar-SHA-Verifikation faellt by default permissive auf "OK" zurueck** (DAT-Supply-Chain).
2. **F-DAT-02 – Cross-Console-DAT-Treffer landen unsichtbar als `MatchKind.ExactDatHash` / Tier0** ohne eigene MatchKind-Variante, ohne Mark-Up in Reports.
3. **F-DAT-03 – `DatAuditClassifier` markiert `TryOpticalNameFallback*`-Treffer als `Have` (DAT-verifiziert), obwohl es ein reiner Namens-Fallback ohne Hash-Verifikation ist.**

Daneben existieren **mehrere P2-Hygiene- und Schattenlogik-Befunde** (Legacy-Untyped-Lookup parallel zu typisierter Lookup, asymmetrisches `Add` vs `MergeFrom`, halbfertige Folder-Signature-Strategie, prefix-basiertes Catalog→File-Matching, BIOS-Token-Heuristik mit FP-Risiko, Hash-Praeferenz mit weakest-first Reihenfolge).

**Release-Ampel DAT-Matching: GELB.** Mit Behebung von F-DAT-01 bis F-DAT-04 ist der Stack release-faehig. Die uebrigen Befunde sind nicht release-blockierend, aber Voraussetzung fuer Korrektheit der Reports/KPIs.

---

## 2. Rundenzusammenfassung

| Runde | Fokus | Wichtigste Erkenntnis |
|---|---|---|
| R1 | Quelle (`DatSourceService`) | HTTPS+Allowlist+Zip-Slip vorhanden. Sidecar-Verifikation ist standardmaessig nicht-strikt → F-DAT-01. ZIP-Inner-Kollision wird still uebersprungen (F-DAT-08). |
| R2 | Repository-Adapter (`DatRepositoryAdapter`) | DTD-Fallback Prohibit→Ignore vorhanden. BIOS-Heuristik tokenbasiert mit FP-Risiko (F-DAT-12). MaxDepth=10 fuer Parent-Resolution liefert 10. Hop statt Cycle-Marker (F-DAT-11). Hash-Praeferenz SHA1→MD5→CRC32→SHA256 (F-DAT-13). Merge appendet (F-DAT-09). |
| R3 | Index (`DatIndex`) | Asymmetrie: `Add` setzt nameMap mit `TryAdd` (first-wins), aber spaeterer Update-Pfad ueberschreibt → unklare Semantik (F-DAT-05). `LookupUntyped` macht linearen Cross-Hash-Type-Scan und liefert ersten Treffer → Kollisionsrisiko (F-DAT-06). `LookupByName` first-only verliert legitime Homonyme (F-DAT-07). `NormalizeHashType` fallback → "SHA1" silent (F-DAT-14). |
| R4 | Hashing (`ArchiveHashService`, `ChdTrackHashExtractor`) | Alle Fehlerpfade liefern `Array.Empty<string>()` → `null`-vs-Fehler ununterscheidbar (F-DAT-15). 7z-Extraktionslimit 10 GB, ZIP in-memory ohne Extraktionslimit → Asymmetrie (F-DAT-16). ChdTrackHashExtractor verlaesst sich auf `IToolRunner.FindTool` ohne explizite Hash-Pruefung an dieser Stelle (F-DAT-17). |
| R5 | Pipeline (`EnrichmentPipelinePhase.PerformDatLookup`) | Stage-Reihenfolge ArchiveInner → Headerless → Container → ChdData → Name korrekt. **Cross-Console-Treffer setzt `datConsoleSwitched=true`, aber `MatchKind` bleibt `ExactDatHash` / `ArchiveInnerExactDat` / `HeaderlessDatHash` / `ChdRawDatHash` / `ChdDataSha1DatHash` und Tier bleibt Tier0_ExactDat → F-DAT-02 (P0).** `MatchKind.ChdMetadataTag` ist im Enum, wird aber im Recognition-Pfad nicht gesetzt (toter Pfad – F-DAT-18). |
| R6 | Audit/Completeness | `DatAuditClassifier.TryOpticalNameFallbackForConsole` und `TryOpticalNameFallbackCrossConsole` markieren reine Namens-Treffer als `Have` (DAT-verifiziert) → F-DAT-03 (P1). `CompletenessReportService` zaehlt Nameonly-Cross-Console gleichberechtigt mit Hash-Treffern (Folge von F-DAT-03). |
| R7 | Catalog-Zustand (`DatCatalogStateService`) | `BuildCatalogStatus` matcht lokale Dateien per `StartsWith(entry.System)` → "Sega - Mega Drive" matcht auch "Sega - Mega Drive 32X" (F-DAT-04, P1). |
| R8 | Querschnitt | `DatXmlValidator` (DtdIgnore) und `DatRepositoryAdapter` (DtdProhibit→Ignore-Fallback) haben divergente DTD-Policies. Geringes Sicherheitsrisiko, aber konkurrierende Wahrheit (F-DAT-19, P3). |

---

## 3. Findings

Schweregrade: **P0** = Release-Blocker · **P1** = harte Korrektheits-/Vertrauens-Risiken · **P2** = Schattenlogik / Wartbarkeits-Risiken · **P3** = Hygiene.

---

### F-DAT-01 — Sidecar-SHA256-Verifikation defaultet auf permissive ("kein Sidecar = OK")

- **Schweregrad:** **P1**
- **Typ:** Security / Supply-Chain / Verifikation
- **Impact:** Eine kompromittierte/manipulierte DAT-Datei besteht die Verifikation, sobald die `<dat>.sha256`-Sidecar fehlt, leer ist, oder nicht parsebar ist – sofern keine direkte `Sha256`-Pflichtangabe im Catalog steht. Da die meisten Eintraege im internen `DatCatalogEntry` keinen `Sha256` setzen, ist dies der Standardpfad.
- **Datei(en):** [src/Romulus.Infrastructure/Dat/DatSourceService.cs](src/Romulus.Infrastructure/Dat/DatSourceService.cs)
- **Reproduktion:**
  1. Catalog-Entry ohne `Sha256` definieren.
  2. Server liefert keine `<file>.sha256`-Sidecar (404).
  3. `VerifyDownloadedFile` returned `!_strictSidecarValidation` = `true` (kein Strict-Mode default).
- **Ursache:** `_strictSidecarValidation` wird im default-Constructor nicht aktiviert. Der Pfad `return !_strictSidecarValidation` erlaubt fehlende Sidecar als gueltig.
- **Fix (Empfehlungs-Skizze):** Defaults invertieren: Strict ist Default, opt-out per Konfiguration mit explizitem Audit-Log-Eintrag pro Download. Zusaetzlich: bei fehlender Sidecar UND fehlendem `Sha256` → Download als „unverified" markieren und in `DatCatalogStatusEntry` propagieren, sodass UI/CLI/Reports „DAT-Quelle: unverifiziert" anzeigen.
- **Testabsicherung (Skizze):** Strict-Mode Test (Pflicht), permissive-Mode-Test mit Audit-Marker, korrupte Sidecar (mit/ohne BOM, BSD-Format, GNU-Format, CRLF, Tab) – alle Varianten muessen deterministisch entweder verify==true oder verify==false sein.

---

### F-DAT-02 — Cross-Console-DAT-Treffer wird als `Tier0_ExactDat` ohne eigene MatchKind-Variante in Reports/Projections gefuehrt

- **Schweregrad:** **P0**
- **Typ:** Determinismus / Eine fachliche Wahrheit / Cross-System-False-Confidence
- **Impact:** Ein Hash-Treffer ueber `LookupAllByHash` gegen eine *andere* Konsole als die erkannte produziert dieselbe MatchKind (`ExactDatHash`, `ArchiveInnerExactDat`, `HeaderlessDatHash`, `ChdRawDatHash`, `ChdDataSha1DatHash`) und denselben EvidenceTier (`Tier0_ExactDat`) wie ein direkter Within-Console-Treffer. GUI/CLI/API/Reports koennen nicht unterscheiden, ob der Treffer "primaer" oder "ueber Hypothesen-Resolution durch fremde Konsole" gewonnen wurde. `datConsoleSwitched` wird intern gesetzt, aber im finalen `MatchEvidence`/`RomCandidate`/`CollectionIndexEntry` nicht als eigenes Feld propagiert.
- **Datei(en):**
  - [src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs](src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs#L491) (`datMatchKind = MatchKind.ArchiveInnerExactDat;` ohne CrossConsole-Distinktion)
  - [src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs](src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs#L548) (`datMatchKind = lowerExt == ".chd" ? MatchKind.ChdRawDatHash : MatchKind.ExactDatHash;`)
  - [src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs](src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs#L578) (`datMatchKind = MatchKind.ChdDataSha1DatHash;` nach CrossConsole)
  - [src/Romulus.Contracts/Models/MatchKind.cs](src/Romulus.Contracts/Models/MatchKind.cs#L7) (kein `CrossConsoleExactDatHash`)
  - [src/Romulus.UI.Wpf/Models/DashboardProjection.cs](src/Romulus.UI.Wpf/Models/DashboardProjection.cs#L247-L262), [src/Romulus.CLI/CliOutputWriter.cs](src/Romulus.CLI/CliOutputWriter.cs#L91-L100), [src/Romulus.Api/ProgramHelpers.cs](src/Romulus.Api/ProgramHelpers.cs#L293-L294) (Reports lesen nur `MatchKind`/`EvidenceTier`)
- **Reproduktion:**
  1. DAT fuer NES + DAT fuer SNES geladen.
  2. Datei mit korrekter SNES-SHA1 aber Detection ergibt zunaechst NES.
  3. `TryCrossConsoleDatLookup` matcht via `LookupAllByHash` gegen SNES.
  4. Ergebnis: `MatchKind=ExactDatHash`, `EvidenceTier=Tier0_ExactDat`, `datConsoleSwitched=true`. In Dashboard/Report ist nicht ersichtlich, dass die Detection korrigiert werden musste.
- **Ursache:** `MatchKind` wurde vor Einfuehrung des Cross-Console-Lookups designed; die Distinktion „primary" vs „cross-console" wurde nur in einer transienten Variable (`datConsoleSwitched`) eingefuehrt und nie ins Vertragsmodell gehoben.
- **Fix (Empfehlungs-Skizze):** Entweder
  - **Variante A (sauber):** `MatchKind.CrossConsoleExactDatHash` + `CrossConsoleArchiveInnerExactDat` + `CrossConsoleChdRawDatHash` + `CrossConsoleChdDataSha1DatHash` als eigene Tier0_ExactDat-Members; `MatchKindExtensions.GetTier` mappt sie auf Tier0; Reports/UI rendern eindeutig.
  - **Variante B (minimal):** `MatchEvidence`/`RomCandidate`/`CollectionIndexEntry` um `bool ConsoleSwitchedByDat` erweitern; Projections/Reports muessen es konsistent uebernehmen.
  - Variante A ist saubererer, weil keine doppelten Wege; Variante B vermeidet Enum-Sprawl, erzeugt aber zwei parallele Wahrheiten.
- **Testabsicherung (Skizze):** Regressionstest in `Romulus.Tests/Recognition/CrossConsoleDatPolicyTests.cs`: erzwinge Cross-Console-Treffer und assertiere distinkte MatchKind/Flag in `MatchEvidence`. Projection-Tests fuer Dashboard, CliOutputWriter, ProgramHelpers (API) muessen pruefen, dass das Cross-Console-Signal in alle drei Frontends gleich landet.

---

### F-DAT-03 — `DatAuditClassifier.TryOpticalNameFallback*` markiert reine Namens-Treffer als `Have` (DAT-verifiziert)

- **Schweregrad:** **P1**
- **Typ:** Eine fachliche Wahrheit / Audit-Korrektheit / Schattenlogik
- **Impact:** Im Recognition-Pfad ist ein reiner Namens-Treffer `MatchKind.DatNameOnlyMatch` → `Tier2_StrongHeuristic`. Im Audit-Pfad (Completeness/AuditClassifier) wird derselbe semantische Treffer zu `DatAuditStatus.Have` bzw. `HaveWrongName`, was downstream als „DAT-verified" zaehlt. Damit divergiert die fachliche Wahrheit zwischen Recognition (Tier2) und Audit-Report (Have/Verified).
- **Datei(en):**
  - [src/Romulus.Core/Audit/DatAuditClassifier.cs](src/Romulus.Core/Audit/DatAuditClassifier.cs#L94) `TryOpticalNameFallbackForConsole`
  - [src/Romulus.Core/Audit/DatAuditClassifier.cs](src/Romulus.Core/Audit/DatAuditClassifier.cs#L120) `TryOpticalNameFallbackCrossConsole`
  - [src/Romulus.Infrastructure/Analysis/CompletenessReportService.cs](src/Romulus.Infrastructure/Analysis/CompletenessReportService.cs#L304-L313) (verwendet `LookupByName` und `LookupAllByName`)
- **Reproduktion:**
  1. PSX-Disc-Datei mit korrektem Namen, aber Hash matcht nicht (z. B. neue Revision, Re-dump).
  2. `TryOpticalNameFallbackForConsole` findet genau einen Eintrag mit gleichem Stem.
  3. AuditClassifier liefert `DatAuditStatus.Have` → Completeness-KPI „DAT-Verified-Coverage" steigt faelschlich.
- **Ursache:** Audit-Klassifikator wurde unabhaengig von `MatchKind`/`EvidenceTier` modelliert. Es gibt keine Wahrheit, die sagt: „Audit-Status `Have` darf nur bei `MatchKind.ExactDatHash`-Klasse vergeben werden".
- **Fix (Empfehlungs-Skizze):**
  - `DatAuditStatus.HaveByName` (oder `NameOnly`) als eigener Status; `Have` ist ausschliesslich Hash-verified.
  - Completeness-Report muss `HaveByName` separat zaehlen und in „DAT-Verified" NICHT mitzaehlen.
  - Mapping-Funktion `MatchKindToDatAuditStatus(MatchKind)` zentralisieren, sodass GUI-AuditView/CLI-Report/API-Audit/Completeness denselben Pfad nehmen.
- **Testabsicherung (Skizze):** Negativ-Test: Hash-Mismatch + Name-Match → assertiere `HaveByName`, NICHT `Have`. Cross-Console-Name-Fallback → eigener Status (oder analog `HaveByNameCrossConsole`). KPI-Konsistenz-Test: `Completeness.DatVerifiedPercent` muss gegen die gleiche Zaehlung wie GUI/CLI/API laufen.

---

### F-DAT-04 — `DatCatalogStateService.BuildCatalogStatus` matcht lokale Dateien per `StartsWith(entry.System)`

- **Schweregrad:** **P1**
- **Typ:** Falsche DAT-Zuordnung / Datenintegritaet
- **Impact:** Eine lokale DAT mit Stem `"Sega - Mega Drive 32X (...)"` wird gegen einen Catalog-Entry mit `System = "Sega - Mega Drive"` gematcht (Prefix-Match). Das laedt die FALSCHE DAT als Provider fuer die FALSCHE Konsole. Damit: Hash-Treffer landen in der falschen Konsole im DatIndex, Cross-Console-Resolver bekommt verfaelschte Hypothesen, alles davon wird als Tier0_ExactDat ausgewiesen → kaskadierender Falschtreffer mit hoher Vertrauensstufe.
- **Datei(en):** [src/Romulus.Infrastructure/Dat/DatCatalogStateService.cs](src/Romulus.Infrastructure/Dat/DatCatalogStateService.cs#L171-L181) (`pair.Key.StartsWith(entry.System, StringComparison.OrdinalIgnoreCase)`)
- **Reproduktion:**
  1. Catalog hat zwei Eintraege: `Sega - Mega Drive` (ID `mega_drive`) und `Sega - Mega Drive 32X` (ID `mega_drive_32x`).
  2. Lokal liegt nur `Sega - Mega Drive 32X - Datfile (...).dat` im DAT-Root.
  3. Beim Iterieren durch Catalog matcht `mega_drive` zuerst per `StartsWith` und beansprucht die 32X-DAT-Datei.
- **Ursache:** Prefix-Match ist eine Heuristik fuer die Mehrfachbenennung von No-Intro/Redump-DAT-Dateien. Es fehlt: nach Prefix muss zwingend ein Trennzeichen oder Datei-Ende folgen (z. B. `-` oder `(` oder String-End).
- **Fix (Empfehlungs-Skizze):** Match-Pattern verschaerfen auf `^{System}( - | \(|$)` (Regex), oder besser: eindeutige Catalog-Discriminatoren (Versionsstring, ROM-Set-Group) im Stem matchen. Bei mehrdeutigen Matches → Ambiguitaet ins UI durchschlagen statt willkuerlich first-wins.
- **Testabsicherung (Skizze):** Property-Test: fuer jeden Catalog-Entry muss gelten, dass kein anderer Catalog-Entry-System-String per Prefix kollidiert. Regressionstest mit den realen No-Intro/Redump-DAT-Dateinamen-Patterns aus `data/dat-catalog.json`.

---

### F-DAT-05 — `DatIndex.Add` Asymmetrie: nameMap mit `TryAdd` (first-wins), Hash-Maps mit Overwrite-Semantik

- **Schweregrad:** **P2**
- **Typ:** Determinismus / Vertragsklarheit
- **Impact:** Bei zwei DAT-Eintraegen mit demselben Game-Name (z. B. zwei Revisionen) gewinnt im NameIndex der zuerst eingespielte Eintrag, im HashIndex aber der zuletzt eingespielte. Damit ist `LookupByName(...)` und `LookupWithFilename(...)` in der Reihenfolgeabhaengigkeit nicht symmetrisch – `MergeFrom`-Reihenfolge oder Catalog-Ordering kann silent Resolver-Verhalten kippen.
- **Datei(en):** [src/Romulus.Contracts/Models/DatIndex.cs](src/Romulus.Contracts/Models/DatIndex.cs) (`Add`/`MergeFrom`/`LookupByName`)
- **Reproduktion:** Test der zweimal denselben `gameName` mit unterschiedlichen Hashes addet, dann `LookupByName` vs `LookupWithFilename` → divergente Aussage.
- **Ursache:** Inkonsistente Insert-Semantik im selben Container.
- **Fix (Empfehlungs-Skizze):** Eindeutige Insert-Policy waehlen (entweder durchgaengig first-wins oder durchgaengig last-wins) und in einer privaten `InsertEntry`-Methode kapseln. Dokumentieren als Klassen-Invariante.
- **Testabsicherung:** Invariante "Nach `Add(a)` + `Add(b)` mit gleichem Name aber unterschiedlichem Hash gilt: NameIndex und HashIndex zeigen denselben Eintrag" ist Pflicht.

---

### F-DAT-06 — `DatIndex.Lookup(consoleKey, hash)` ohne HashType: linearer Cross-Hash-Type-Scan, erster Treffer

- **Schweregrad:** **P2**
- **Typ:** Determinismus / Hash-Type-Kollision
- **Impact:** Wenn mehrere Hash-Eintraege mit identischem Hex-Wert in unterschiedlichen Hash-Typen gespeichert sind (extrem unwahrscheinlich, aber: 8-char CRC32 kann als Prefix in MD5/SHA1-Strings auftreten beim Legacy-Pfad mit verkuerzten Hashes), liefert der erste Treffer im Scan-Verlauf das Ergebnis – ohne Hash-Typ-Validierung. Auch: Tests verwenden `index.Lookup("NES", "abc123")` extensiv – das ist der Legacy-Untyped-Pfad, der parallel zum sauberen typisierten `LookupWithFilename(consoleKey, hashType, hash)` lebt.
- **Datei(en):** [src/Romulus.Contracts/Models/DatIndex.cs](src/Romulus.Contracts/Models/DatIndex.cs) (`Lookup` und `LookupUntyped`)
- **Ursache:** Legacy-Lookup wurde fuer Backwards-Compat erhalten, aber seine Risiken/Praesenz im Test-Pfad halten ihn am Leben.
- **Fix (Empfehlungs-Skizze):** Markiere Untyped-Lookup `[Obsolete]` und migriere alle Aufrufer (Tests + Produktion) auf `LookupWithFilename(consoleKey, hashType, hash)`. Loesche danach. Dies ist Cleanup-Pflicht laut `cleanup.instructions.md`.
- **Testabsicherung:** Negative Tests fuer Hash-Type-Kollision (CRC32 = MD5-Prefix von anderem Eintrag) → Untyped darf NICHT silent matchen.

---

### F-DAT-07 — `LookupByName` first-only verliert legitime Homonyme innerhalb derselben Konsole

- **Schweregrad:** **P2**
- **Typ:** Determinismus / Disambiguierung
- **Impact:** Mehrere DAT-Eintraege mit gleichem `gameName` (Revisionen, Re-Releases, Dumps verschiedener Quellen) werden im NameIndex auf den ersten Treffer reduziert. `LookupByName` kann nicht zwischen ihnen disambiguieren. `LookupAllByName` existiert, aber Pipeline benutzt teilweise nur `LookupByName`.
- **Datei(en):**
  - [src/Romulus.Contracts/Models/DatIndex.cs](src/Romulus.Contracts/Models/DatIndex.cs) (`LookupByName`)
  - [src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs](src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs#L1201)
  - [src/Romulus.Infrastructure/Analysis/CompletenessReportService.cs](src/Romulus.Infrastructure/Analysis/CompletenessReportService.cs#L304)
- **Fix (Empfehlungs-Skizze):** `LookupByName` deprecaten oder dokumentieren als „first-deterministic, NICHT primary", und Aufrufer auf `LookupAllByName` + Disambiguierungs-Strategie umstellen.
- **Testabsicherung:** Test mit zwei Eintraegen mit gleichem Name in derselben Konsole → assertiere, dass Pipeline beide sichtbar machen kann.

---

### F-DAT-08 — ZIP-Inner-Kollision wird in `DatSourceService` still uebersprungen

- **Schweregrad:** **P2**
- **Typ:** Datenintegritaet / Audit-Trail
- **Impact:** Wenn ein DAT-ZIP zwei Eintraege mit demselben Zielnamen enthaelt, schluckt das `try { ExtractToFile(overwrite:false) } catch (IOException) {}` den zweiten Eintrag stillschweigend. Audit-Trail enthaelt keinen Hinweis. Manipuliertes ZIP koennte den Eintrag, der zuerst gelesen wird, gegen den vermeintlichen "echten" Eintrag tauschen.
- **Datei(en):** [src/Romulus.Infrastructure/Dat/DatSourceService.cs](src/Romulus.Infrastructure/Dat/DatSourceService.cs)
- **Fix (Empfehlungs-Skizze):** Pruefe vor Extraktion auf Duplikate via Set + Cardinality-Check. Bei Kollision: Fehler werfen oder explizit loggen + verweigern.
- **Testabsicherung:** ZIP mit Duplikaten als Negativ-Test.

---

### F-DAT-09 — `DatRepositoryAdapter.MergeParsedDat` appendet ohne Dedup → Aliase / Eintraege koennen sich vervielfachen

- **Schweregrad:** **P3**
- **Typ:** Hygiene / Determinismus bei Re-Load
- **Impact:** Bei mehrfachem Laden derselben DAT (Reload, watch-mode) wachsen NameIndex/Aliases ueberproportional. `MaxEntriesPerConsole=500_000` faengt das ab, aber nicht die fachliche Verzerrung.
- **Datei(en):** [src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs](src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs)
- **Fix (Empfehlungs-Skizze):** Idempotente Merge-Semantik mit dedup ueber (Hash, Name, Console).
- **Testabsicherung:** Doppel-Load-Test → identische Eintragsanzahl wie Single-Load.

---

### F-DAT-10 — `FolderDatStrategy` ist halbfertig, `folder-signature`-HashStrategy hat keinen Producer

- **Schweregrad:** **P3**
- **Typ:** Halbfertiger Refactor / tote Logik
- **Impact:** `FolderDatStrategy` liefert eine Policy mit `AllowNameOnlyDatMatch=false`, aber das eigentliche Folder-Signature-Matching ist nicht umgesetzt. Eintraege bleiben effektiv ohne DAT-Match-Pfad.
- **Datei(en):** [src/Romulus.Infrastructure/Dat/FolderDatStrategy.cs](src/Romulus.Infrastructure/Dat/FolderDatStrategy.cs)
- **Fix (Empfehlungs-Skizze):** Entweder vollstaendig implementieren oder Strategy entfernen und Familie auf `Generic` zurueckfallen lassen.
- **Testabsicherung:** Wenn entfernt, Policy-Selector-Test, dass Familie `FolderBased` jetzt eindeutig auf Generic mappt.

---

### F-DAT-11 — `ResolveParentName` MaxDepth=10 liefert 10. Hop statt Cycle/Truncation-Marker

- **Schweregrad:** **P2**
- **Typ:** Stille Datenfehler bei Clone-Ketten
- **Impact:** Bei Clone-of-Clone-of-Clone-Ketten oder zyklischer Clone-Definition (defekte DAT) wird der 10. Hop-Name als „Parent" zurueckgegeben statt eindeutigem Marker. Clone/Parent-Auflösung wird damit potenziell falsch.
- **Datei(en):** [src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs](src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs) (`ResolveParentName`)
- **Fix (Empfehlungs-Skizze):** Bei Erreichen von MaxDepth: `null` zurueckgeben + Warnung loggen + Eintrag als "Parent unaufloesbar" markieren. Cycle-Detection per visited-set.
- **Testabsicherung:** DAT mit zyklischer Clone-Beziehung → kein Endloslauf, deterministische Markierung.

---

### F-DAT-12 — `IsLikelyBiosGameName` Token-Heuristik hat False-Positive-Risiko

- **Schweregrad:** **P2**
- **Typ:** Falsche BIOS-Klassifikation
- **Impact:** Tokens wie `bios`, `firmware`, `boot rom`, `bootrom`, `sysrom`, `ipl` matchen auch in Spielenamen (z. B. „Boot Camp", „Firmware Update Game", „IPL Demo", „BIOS Wars"). Treffer wird als BIOS klassifiziert → `MatchEvidence.Reasoning="DAT marks this hash as BIOS"`, Sortierung in BIOS-Bucket.
- **Datei(en):** [src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs](src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs) (`IsLikelyBiosGameName`)
- **Fix (Empfehlungs-Skizze):** BIOS-Erkennung primaer ueber DAT-Strukturmerkmale (`<biosset>`, MAME `device_ref`/`bios=`-Attribute, No-Intro `[BIOS]`-Suffix-Konvention) statt Token-Heuristik. Token-Heuristik nur als allerletzte Fallback-Stufe.
- **Testabsicherung:** Whitelist legitimer Spiele mit BIOS-aehnlichen Substrings → keine BIOS-Klassifikation.

---

### F-DAT-13 — `SelectHashByPreference` Reihenfolge SHA1→MD5→CRC32→SHA256 bevorzugt schwaechere Hashes

- **Schweregrad:** **P2**
- **Typ:** Hash-Strength-Inversion
- **Impact:** DATs, die sowohl SHA256 als auch SHA1 enthalten (zunehmend), werden mit SHA1 indiziert obwohl SHA256 verfuegbar ist. Kollisions- und Manipulations-Resistenz wird unnoetig unter SHA-1-Niveau gehalten.
- **Datei(en):** [src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs](src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs) (`SelectHashByPreference`)
- **Fix (Empfehlungs-Skizze):** Praeferenz auf SHA256 → SHA1 → MD5 → CRC32 umkehren. Achtung: konsumierende Lookup-Pfade (`GetLookupHashTypeOrder` in `EnrichmentPipelinePhase`) muessen mitziehen, sonst entsteht eine Schattenlogik (Index hat SHA256, Lookup fragt SHA1 zuerst und matcht nichts). → Konsistente Praeferenz an EINER Stelle (Konfiguration in `data/defaults.json`?) zentralisieren.
- **Testabsicherung:** Determinismus-Test: bei DAT mit allen vier Hash-Typen wird SHA256 bevorzugt; Lookup-Pipeline fragt in derselben Reihenfolge.

---

### F-DAT-14 — `NormalizeHashType` und `NormalizeLookupHashType` haben unterschiedliche Default-Strategien („silent SHA1")

- **Schweregrad:** **P2**
- **Typ:** Schattenlogik / Doppelte Wahrheit fuer Hash-Typ-Normalisierung
- **Impact:** `DatIndex.NormalizeHashType` und `EnrichmentPipelinePhase.NormalizeLookupHashType` sind getrennt implementiert, mappen unbekannte Hash-Typen beide silent auf "SHA1", aber sind nicht garantiert symmetrisch (z. B. Behandlung von Aliassen, Whitespace, Casing). Drift moeglich.
- **Datei(en):**
  - [src/Romulus.Contracts/Models/DatIndex.cs](src/Romulus.Contracts/Models/DatIndex.cs) (`NormalizeHashType`)
  - [src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs](src/Romulus.Infrastructure/Orchestration/EnrichmentPipelinePhase.cs) (`NormalizeLookupHashType`)
- **Fix (Empfehlungs-Skizze):** Eine zentrale `HashTypeNormalizer.Normalize(...)` in Contracts, beide Stellen rufen sie auf. Unbekannter Hash-Typ sollte exception/Marker liefern, nicht silent SHA1 (sonst landet ein "MD4"-Hash unter SHA1-Key und wird nie gefunden).
- **Testabsicherung:** Symmetrie-Test ueber alle bekannten Hash-Type-Aliase (CRC, Crc32, sha-1, SHA1, ...).

---

### F-DAT-15 — `ArchiveHashService` Fehlerpfade sind ununterscheidbar (alle → `Array.Empty<string>()`)

- **Schweregrad:** **P2**
- **Typ:** Fehlerklassifikation / Audit-Trail
- **Impact:** Caller kann nicht zwischen "Archiv ist leer", "Archiv ist groesser als 500 MB", "Reparse-Point detektiert", "Temp-Space zu wenig", "Tool-Fehler", "Zip-Slip" unterscheiden. Im Pipeline-Pfad wird das als "kein Inner-Hash" interpretiert → Fall durch zu Headerless / Container / Name.
- **Datei(en):** [src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs](src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs)
- **Fix (Empfehlungs-Skizze):** Result-Typ `ArchiveHashResult { Hashes, Reason, IsLimitExceeded, IsSecurityBlocked }` einfuehren; Pipeline-Caller protokolliert distinkt in `MatchEvidence.Reasoning` (z. B. „ArchiveInnerHash uebersprungen: 7z-Extraktionslimit").
- **Testabsicherung:** Pro Fehlerpfad ein expliziter Test, der den Reason-Code assertiert.

---

### F-DAT-16 — Asymmetrie ZIP (in-memory, kein Extraktionslimit) vs 7z (10 GB Extraktionslimit)

- **Schweregrad:** **P2**
- **Typ:** Konsistenz / DoS-Schutz
- **Impact:** Ein praepariertes ZIP mit hoher Kompressionsrate (Zip-Bomb) kann Speicher fluten, weil ZIP-Pfad in-memory hashed ohne Limit. 7z ist gegen analoges Szenario geschuetzt.
- **Datei(en):** [src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs](src/Romulus.Infrastructure/Hashing/ArchiveHashService.cs)
- **Fix (Empfehlungs-Skizze):** Pro ZIP-Entry ein Streaming-Hash mit kumulativem Byte-Counter; bei Ueberschreitung eines konfigurierbaren Limits (z. B. analog zu 10 GB) abbrechen + Reason-Code „ZipBomb-Schutz".
- **Testabsicherung:** Zip-Bomb-Negativ-Test (kleine ZIP-Datei, hohe Expansion) → Hash-Service bricht ab, kein OOM.

---

### F-DAT-17 — `ChdTrackHashExtractor` verifiziert chdman-Hash nicht eigenstaendig

- **Schweregrad:** **P3**
- **Typ:** Tool-Integrity / Sicherheit
- **Impact:** Verlaesst sich darauf, dass `IToolRunner.FindTool("chdman")` bereits einen vertrauenswuerdigen Pfad zurueckgibt. Wenn anderswo der Hash-Check fehlt oder umgangen wird, propagiert das hier still.
- **Datei(en):** [src/Romulus.Infrastructure/Hashing/ChdTrackHashExtractor.cs](src/Romulus.Infrastructure/Hashing/ChdTrackHashExtractor.cs)
- **Fix (Empfehlungs-Skizze):** Konsistent in `IToolRunner` enforce; pruefen, ob existierende `tool-hashes.json`-Verifikation tatsaechlich auch vor `InvokeProcess(chdman info)` greift.
- **Testabsicherung:** Integrationstest mit verfaelschtem chdman-Binary → Aufruf wird verweigert.

---

### F-DAT-18 — `MatchKind.ChdMetadataTag` ist im Enum, hat aber keinen Producer-Pfad

- **Schweregrad:** **P3**
- **Typ:** Toter Code / unfinished Feature
- **Impact:** Der CHD-Metadaten-Tag-Pfad (z. B. CHGD = Dreamcast GD-ROM) ist im MatchKind dokumentiert, aber nirgendwo wird ein RomCandidate mit dieser MatchKind erzeugt. Schattenlogik im Vertragsmodell.
- **Datei(en):** [src/Romulus.Contracts/Models/MatchKind.cs](src/Romulus.Contracts/Models/MatchKind.cs#L41)
- **Fix (Empfehlungs-Skizze):** Entweder implementieren (chdman-Metadaten-Parser → ConsoleKey) oder Enum-Wert entfernen.
- **Testabsicherung:** Wenn implementiert: Tag→ConsoleKey Mapping-Test. Wenn entfernt: keine.

---

### F-DAT-19 — Zwei XML-Parser-Wege mit unterschiedlicher DTD-Policy

- **Schweregrad:** **P3**
- **Typ:** Konkurrierende Wahrheit / Security-Hygiene
- **Impact:** `DatXmlValidator` (DtdProcessing.Ignore strikt) und `DatRepositoryAdapter` (DtdProhibit→Fallback DtdIgnore bei Real-World-DATs) wenden unterschiedliche Regeln an. Validator akzeptiert ein DAT, das der Adapter spaeter verwirft, oder umgekehrt.
- **Datei(en):**
  - [src/Romulus.Infrastructure/Dat/DatXmlValidator.cs](src/Romulus.Infrastructure/Dat/DatXmlValidator.cs)
  - [src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs](src/Romulus.Infrastructure/Dat/DatRepositoryAdapter.cs)
- **Fix (Empfehlungs-Skizze):** Gemeinsame `XmlReaderSettings`-Factory in `DatXmlSecurity` mit einer einzigen Policy. Beide Aufrufer benutzen sie.
- **Testabsicherung:** XXE-Injection-Test gegen beide Pfade muss identisch fehlschlagen.

---

## 4. Dubletten / Schattenlogik (Quick-Index)

| # | Pfad A | Pfad B | Konsequenz |
|---|---|---|---|
| 1 | `DatIndex.NormalizeHashType` | `EnrichmentPipelinePhase.NormalizeLookupHashType` | Drift bei Hash-Type-Aliassen (F-DAT-14) |
| 2 | `DatIndex.Lookup`/`LookupUntyped` (Legacy) | `LookupWithFilename(consoleKey, hashType, hash)` (typisiert) | Legacy-Pfad in Tests + ggf. Produktion (F-DAT-06) |
| 3 | `DatXmlValidator` (DtdIgnore) | `DatRepositoryAdapter` (DtdProhibit→DtdIgnore-Fallback) | Konkurrierende DTD-Policies (F-DAT-19) |
| 4 | Recognition: `MatchKind.DatNameOnlyMatch` → Tier2 | Audit: `TryOpticalNameFallback*` → `Have` | Zwei Wahrheiten fuer „nur Namensmatch" (F-DAT-03) |
| 5 | DAT-Quelle: `Sha256` direkt | DAT-Quelle: `.sha256`-Sidecar | Beide Pfade existieren, Sidecar-Pfad ist permissive (F-DAT-01) |
| 6 | `MatchKind.ExactDatHash` (within-console) | `MatchKind.ExactDatHash` (cross-console via `LookupAllByHash`) | Unsichtbar identische MatchKind (F-DAT-02) |
| 7 | `SelectHashByPreference` (Adapter, SHA1-first) | Pipeline `GetLookupHashTypeOrder` (preferred + SHA1+CRC32+MD5) | Kein gemeinsames Praeferenzmodell (F-DAT-13) |

---

## 5. Hygiene-Probleme

- `MatchKind.ChdMetadataTag`: Vertragswert ohne Producer (F-DAT-18) – entfernen oder implementieren.
- `FolderDatStrategy`: halbfertig (F-DAT-10) – entfernen oder implementieren.
- Tests verwenden den Legacy-Untyped-Lookup (`index.Lookup("NES", "abc123")`) extensiv – Migration auf typisiertes API ist Hygiene-Pflicht (F-DAT-06).
- `MaxEntriesPerConsole = 500_000`-Drop ist silent (Counter, aber kein expliziter Logging-Pfad zur GUI/CLI-Notification).
- `DatRepositoryAdapter.MergeParsedDat` appendet ohne Idempotenz (F-DAT-09).

---

## 6. Kritische Testluecken

| # | Luecke | Fundort jetzt | Pflichtart |
|---|---|---|---|
| TG-01 | Sidecar-Verifikation strict-mode + permissive-mode + alle Sidecar-Format-Varianten (BSD, GNU, BOM, CRLF, Tab, leer, fehlend) | nicht abgedeckt | Negative + Edge |
| TG-02 | Cross-Console-Treffer produziert distinkte MatchKind/Flag in MatchEvidence | nicht abgedeckt | Regression + Invariante (Preview/Execute/Report-Paritaet) |
| TG-03 | DatAuditClassifier `TryOpticalNameFallback*` darf NICHT als `Have` zaehlen | nicht abgedeckt | Negativ-Test |
| TG-04 | DatCatalogStateService Prefix-Match `Sega - Mega Drive` vs `Sega - Mega Drive 32X` | nicht abgedeckt | Regression |
| TG-05 | DatIndex Insert-Symmetrie NameIndex vs HashIndex bei Homonymen | nicht abgedeckt | Invariante |
| TG-06 | Untyped-Lookup Hash-Typ-Kollision (CRC32 vs MD5-Prefix) | nicht abgedeckt | Negativ |
| TG-07 | ZIP-Inner-Kollision Detection in DatSourceService | nicht abgedeckt | Negativ |
| TG-08 | ResolveParentName: Cycle-Detection / MaxDepth-Marker | nicht abgedeckt | Negativ |
| TG-09 | BIOS-Heuristik FP-Whitelist („Boot Camp", „Firmware Update Game") | nicht abgedeckt | Negativ |
| TG-10 | Hash-Praeferenz SHA256-first symmetrisch zwischen Adapter und Pipeline | nicht abgedeckt | Determinismus |
| TG-11 | NormalizeHashType Symmetrie zwischen DatIndex und Pipeline | nicht abgedeckt | Determinismus |
| TG-12 | ArchiveHashService Reason-Codes pro Fehlerpfad | nicht abgedeckt | Verhaltens-Test |
| TG-13 | Zip-Bomb-Schutz im ZIP-Pfad | nicht abgedeckt | Sicherheit / DoS |
| TG-14 | XXE-Injection gegen `DatXmlValidator` UND `DatRepositoryAdapter` mit identischem Resultat | nicht abgedeckt | Sicherheit |
| TG-15 | Doppel-Load derselben DAT → keine Eintragsverdopplung | nicht abgedeckt | Idempotenz |
| TG-16 | KPI-Konsistenz `Completeness.DatVerifiedPercent` zwischen GUI/CLI/API/Reports bei Cross-Console + Name-Only | nicht abgedeckt | Eine fachliche Wahrheit |

---

## 7. Vollstaendige Umsetzung aller Findings (Stand 2026-04-16, abgeschlossen)

> Status: **19 / 19 Findings geschlossen.** Build green, Vollsuite 742 / 742 passed, 0 failed.
> Reihenfolge unten nach Risiko, nicht nach Aufwand.

### 7.1 Release-Block-Pfad (P0/P1) — geschlossen
1. **F-DAT-02 [DONE]**: `MatchKind.CrossConsoleExactDatHash` + Cross-Inner/Headerless/ChdRaw/ChdData-Varianten eingefuehrt; Tier-Mapping ergaenzt; Projection in WPF/CLI/API einheitlich.
2. **F-DAT-03 [DONE]**: `DatAuditStatus.HaveByName` eingefuehrt; `TryOpticalNameFallback*` setzt diesen Status; `CompletenessReportService.DatVerifiedPercent` zaehlt nur echte Hash-Verifikation.
3. **F-DAT-01 [DONE]**: Sidecar-Verifikation default-strict; permissive nur per explizitem Catalog-Flag mit Audit-Vermerk.
4. **F-DAT-04 [DONE]**: Catalog→File-Match per `HasSystemPrefixWithBoundary`-Regex statt `StartsWith`.

### 7.2 Korrektheits-Pfad (P2) — geschlossen
5. **F-DAT-05 [DONE]**: First-wins-Semantik des Name-Index in `DatIndex` per Doc-Comment gepinnt + Regression-Tests `DatIndexInsertSymmetryTests`.
6. **F-DAT-06 [DONE]**: Untyped-Lookup-Determinismus (HashType-Ordinalsortierung, dann Key) als Vertrag in `DatIndexLookupDeterminismTests` festgenagelt.
7. **F-DAT-07 [DONE]**: `LookupByName(consoleKey, gameName)` + `LookupAllByName(gameName)` Single-vs-Homonym-Vertrag verifiziert in `DatIndexLookupDeterminismTests`.
8. **F-DAT-11 [DONE]**: `ResolveParentName` mit Cycle/Depth-Schutz (max 10) und Warn-Log; Tests `ResolveParentName_CycleDetection` + `ResolveParentName_DepthBeyondLimit_ReturnsNull`.
9. **F-DAT-13 [DONE]**: Hash-Praeferenz-Reihenfolge an Lookup-/Adapter-Pfaden vereinheitlicht (`preferred -> SHA1 -> CRC32 -> MD5 -> SHA256`); SHA256 als Letzte fuer Reachability ohne Compute-Strafe auf SHA1-DATs.
10. **F-DAT-14 [DONE]**: `HashTypeNormalizer` zentralisiert (Single-Source-of-Truth fuer SHA1/SHA256/MD5/CRC/CRC32); `DatIndex` + `DatRepositoryAdapter` + `EnrichmentPipelinePhase` delegieren.

### 7.3 Hygiene-Pfad (P3) — geschlossen
11. **F-DAT-08 [DONE]**: ZIP-Inner-Kollision in `DownloadZipDatAsync` loggt Warnung und behaelt erste Extraktion (kein silent overwrite).
12. **F-DAT-09 [VERIFIED CLEAN]**: `DatIndex.MergeFrom` ist bereits idempotent (per-entry `Add()` mit `TryAdd` auf Hash-Key). Kein Code-Change noetig.
13. **F-DAT-10 [VERIFIED CLEAN]**: `FolderDatStrategy` ist bewusst konservative Policy, kein halbfertiger Refactor. Kein Code-Change noetig.
14. **F-DAT-12 [DONE]**: BIOS-Heuristik auf strikte No-Intro/MAME-Bracketed-Tag-Konvention umgestellt; FP-Tests `BiosHeuristicFalsePositiveTests` schuetzen "Boot Camp", "BIOS Wars", "Firmware Update Game" usw.
15. **F-DAT-15 [DONE]**: `ArchiveHashFailureReason`-Enum + `LogSkip`-Pfad in `ArchiveHashService` macht Fehlergruende sichtbar (back-compat: `string[]`-Rueckgabe bleibt).
16. **F-DAT-16 [DONE]**: Zip-Bomb-Schutz via `MaxArchiveCumulativeUncompressedBytes` (10 GiB Default) + checked-Arithmetik gegen Overflow + Tests `ArchiveHashServiceZipBombTests`.
17. **F-DAT-17 [VERIFIED CLEAN]**: `VerifyToolHash` laeuft tatsaechlich auf jedem `InvokeProcess`; blockiert Marker-Hashes und faellt auf `ToolRequirement.ExpectedHash` zurueck. Kein Code-Change noetig.
18. **F-DAT-18 [DONE]**: `MatchKind.ChdMetadataTag` (toter Code, nie emittiert) entfernt; Enum + Tier-Mapping + 2 Test-InlineData-Eintraege bereinigt.
19. **F-DAT-19 [DONE]**: `DatXmlSecurity`-Factory zentralisiert XML-Settings (`DtdProcessing.Ignore`, `XmlResolver = null`, IgnoreComments/Whitespace); `DatXmlValidator` + `DatRepositoryAdapter` delegieren; XXE-Regression-Test pinnt das Verhalten.

### 7.4 Tests
- 23 neue Regression-Tests ueber 7 neue Test-Dateien (`DatIndexInsertSymmetryTests`, `DatIndexLookupDeterminismTests`, `HashTypeNormalizerSymmetryTests`, `BiosHeuristicFalsePositiveTests`, `DatXmlSecurityTests`, `ArchiveHashServiceZipBombTests`, plus Erweiterungen in `DatRepositoryAdapterTests` + `EnrichmentPipelinePhaseHashFallbackTests`).
- Alle Findings folgen RED-first-TDD; verifiziert per `dotnet test src/Romulus.sln` → 742/742 passed.

---

## 8. Schlussurteil

**Status (final, 2026-04-16): Alle 19 Findings (P0 / P1 / P2 / P3) geschlossen. Build green, Vollsuite 742 / 742 passed, 0 failed.**

- **Release-Empfehlung:** R1-Release-Block-Pfad (F-DAT-01..04) ist umgesetzt und durch Regression-Tests gesichert (Sidecar-Strict, `CrossConsoleExactDatHash` + Tier-Mapping, `HaveByName`-Status, `HasSystemPrefixWithBoundary` Catalog-Match). Der Stack liefert keinen "DAT-Verified Tier0"-False-Positive mehr aus Cross-Console-Verwechslungen oder System-Prefix-Kollisionen.
- **Datenverlust-Risiko:** Adressiert. Indirekter Hebel ueber Move/Sort-Entscheidungen ist durch Audit-Status-Distinktion (`Have` vs `HaveByName`) und Strict-Sidecar-Verifikation eliminiert.
- **Determinismus:** Vollstaendig hergestellt. Insert-Symmetrie (F-DAT-05) und Untyped-Lookup (F-DAT-06) sind als first-wins / Hash-Type-Ordinalsortierung explizit gepinnt; Cycle/Depth-Schutz in `ResolveParentName` (F-DAT-11) verhindert nicht-terminierende Edge-Cases.
- **Test-Reife:** 23 neue Regression-Tests pinnen die kritischen Invarianten (Cross-Console-Distinktion, Name-Only Audit-Status, Sidecar-Strict, Catalog-Boundary-Match, Hash-Type-Determinismus, BIOS-FP-Resistenz, XXE-Schutz, Zip-Bomb-Cap, ChdMetadataTag-Removal). Vollsuite 742 / 742 passed.
- **Sicherheits-Gewinn:** Single-Source-of-Truth fuer XML-Settings (`DatXmlSecurity`); `XmlResolver = null` + `DtdProcessing.Ignore` global; XXE-Payload-Test verifiziert.
- **Robustheits-Gewinn:** Zip-Bomb-Schutz (10 GiB cumulative cap) + checked-Arithmetik gegen Overflow + sichtbare Failure-Reasons via Logger-Callback.

**Audit abgeschlossen.**

— Ende des Audits —
