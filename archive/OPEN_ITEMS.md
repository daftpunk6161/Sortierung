# Romulus — Offene Themen auf realen Repo-Stand reduziert

Stand: 2026-04-15  
Basis: Repo-Audit gegen `src/`, `benchmark/`, `.github/workflows/`, regeneriertes `benchmark/manifest.json`,
`benchmark/tools/analyze-gates.ps1` und aktuelle Gate-Laeufe (`615/615` gruen in der relevanten Release-Gate-Suite).

Dieses Dokument ist kein historisches Sammel-Backlog mehr. Es fuehrt nur noch reale Restarbeit fuer den aktuellen Release-Pfad. Historische Wunschlisten bleiben in `archive/completed/plans/` und `docs/plan/`.

## Erledigt im Repo

- [x] Benchmark- und Gate-Framework ist vorhanden:
  `CoverageGateTests`, `QualityGateTests`, `HoldoutGateTests`, `AntiGamingGateTests`,
  `ConfidenceCalibrationTests`, `DatCoverageBenchmarkTests`, `RepairSafetyBenchmarkTests`,
  `PerformanceBenchmarkTests`, `BaselineRegressionGateTests`, `TrendAnalyzer`,
  `BenchmarkHtmlReportWriter`, `CoverageValidator`, `ManifestCalculator`.
- [x] Datensatz-Breite ist bereits stark ausgebaut:
  `benchmark/manifest.json` steht aktuell bei `7692` Ground-Truth-Eintraegen,
  `78` Systemen, `200` Holdout-Eintraegen, `5000` Performance-Eintraegen,
  `389` DAT-Coverage-Eintraegen und `113` Repair-Safety-Eintraegen.
- [x] Plattformfamilien sind fuer den Release-Pfad breit genug abgedeckt:
  Cartridge, Disc, Arcade, Computer und Hybrid liegen deutlich ueber `hardFail`.
- [x] WPF-Basis ist nicht mehr Backlog:
  6 Themes, Command Palette, Region-Presets/-Prioritaeten, Smart Action Bar,
  Shell-Navigation und Accessibility-Grundlagen sind vorhanden.
- [x] Kanalneutrale Run-Projektionen sind umgesetzt:
  Preview/Execute/Report laufen ueber zentrale Modelle statt separater GUI/CLI/API-Schattenlogik.
- [x] Kernlogik fuer Sort-/Konvertierungsentscheidungen ist vorhanden:
  `SortDecision`, Hard-/Soft-Evidence, gehaertete Klassifikation, Conversion-Registry,
  Planner und Executor sind integriert.
- [x] DAT-Audit/Rename und Conversion-Engine-Basis sind umgesetzt.
- [x] `latest-baseline.json` ist der aktive Baseline-Name.
  Verweise auf `baseline-latest.json` sind Altlasten und kein aktiver Arbeitsauftrag mehr.
- [x] `ciso`/`maxcso` ist keine offene Integrationsluecke mehr.
- [x] RVZ-Verifikation ist nicht mehr nur Magic-Byte-basiert:
  vorhandene Tool-Integration nutzt `dolphintool verify`, mit Fallback fuer reine Signaturpruefung.
- [x] Manifest- und Gap-Report-Actuals laufen jetzt ueber deckungsgleiche Spezialbereich-Logik,
  einschliesslich `holdout`, `arcadeClone`, `arcadeParent`, `arcadeGameChd`,
  `psDisambiguation`, `satDcDisambiguation` und `pcePcecdDisambiguation`.
- [x] Der autoritative Manifest-Pfad ist aktiv verdrahtet:
  `ManifestCalculator` -> `benchmark/tools/Update-Manifest.ps1` -> `benchmark/manifest.json`.
- [x] Der CI-Workflow fuehrt die relevante Release-Gate-Suite jetzt explizit aus
  und regeneriert davor das Manifest.
- [x] Hard-Fail-Qualitaetsmodus ist fuer den aktuellen Benchmark-Stand wieder gruen:
  `ROMULUS_ENFORCE_QUALITY_GATES=true` besteht jetzt fuer `QualityGateTests`
  und `HoldoutGateTests`.
- [x] BIOS-/Device-Erkennung wurde fuer reale Restfaelle nachgezogen:
  u.a. `panafz10`, `dc_boot`, `pcfxbios`, `playstation_bios`, `ps2bios_*`,
  `saturn_bios_*`, `System Card`, `System ROM`, `IPL ROM`, `System Menu`,
  sowie Arcade-Devices wie `pgm`, `cps2`, `cps3`, `taitogn`, `stv`, `naomi`.
- [x] Holdout-Stubs fuer variantensensitive Systeme sind korrigiert:
  `GBC -> cgb-dual`, `32X -> 32x`, `DC -> dreamcast`, `SCD -> segacd`.
- [x] Atari-7800-Header-Detektion liefert jetzt den kanonischen Repo-Key `A78`
  statt eines abweichenden Legacy-Keys.

## Aktiv offen

- [x] Aktuell keine offenen Release-Restpunkte mehr in diesem Dokument.
- [x] Die zuletzt offenen Fallklassen sind auf Ziel:
  `FC-12` `45/45`, `FC-13` `45/45`, `FC-14` `35/35`, `FC-15` `25/25`, `FC-20` `100/100`.
- [x] Die zuletzt offenen Spezialbereiche sind auf Ziel:
  `biosSystems` `25/25`, `arcadeClone` `38/38`, `gbGbcCgb` `18/18`, `md32x` `14/14`,
  `directoryBased` `45/45`, `biosErrorModes` `25/25`, `arcadeConfusion` `25/25`,
  `headerVsHeaderlessPairs` `25/25`, `containerVariants` `20/20`,
  `satDcDisambiguation` `10/10`, `pcePcecdDisambiguation` `8/8`.
- [x] Die Erweiterungen wurden gezielt in reale Restluecken gelegt:
  Archive-Inner, Directory-Based, Expected-Unknown, Disc-Ambiguitaeten,
  Broken-Sets, BIOS-Systeme/-Fehlermodi, Arcade-Clone/-Konfusion,
  GB/GBC, MD/32X, Header-vs-Headerless, Container sowie SAT/DC- und PCE/PCECD-Disambiguation.

## Spaeter / optional

Diese Punkte sind bewusst `deferred` und keine offenen Release-Blocker fuer den aktuellen Stand:

- Weitere Benchmark-Tiefe nur dann, wenn neue Container-/Clone-/Broken-Set-Varianten
  eine reale Fehlklasse absichern statt nur Zahlen zu erhoehen.
- Parallele Batch-Conversion nur dann, wenn Determinismus, Tool-Isolation,
  Cleanup und Verifikation belastbar beweisbar bleiben.
- Zusaetzliche Formatpfade wie MDF/MDS/NRG nur bei belastbarer Tool-Kette
  und klarer Audit-/Undo-Strategie.
- Cross-Root-Repair und Archive-Rebuild bleiben Folgeepics, nicht Release-Pflicht
  fuer den aktuellen Kern.

## Verwerfen / obsolet

- [x] Holdout als fehlende Dataset-Breite fuehren.
  Das reale Problem war Tooling-/Manifest-Drift, nicht fehlender Content.
- [x] `baseline-latest.json` als aktiven Namen weiterpflegen.
  Aktiver Name ist `latest-baseline.json`.
- [x] Grossflaechige GUI-Redesign-Backlogs als aktives Release-Thema fuehren.
  Die aktuelle WPF-Basis ist fuer den Release-Pfad ausreichend.
- [x] `ciso`-Integration als offene Kernluecke fuehren.
  Die Integration ist bereits vorhanden.
- [x] RVZ-Verify als fehlendes Kernfeature fuehren.
  Der echte Restpunkt liegt heute eher bei PS2-CD/DVD-Disambiguation.
- [x] PS2 CD/DVD-Erkennung als offene Produktluecke fuehren.
  `SYSTEM.CNF`-basierte BOOT/BOOT2-Erkennung ist jetzt vor der
  Groessenheuristik verdrahtet, inklusive Planner-/Invoker-Paritaet.
- [x] Neue Sonderlogik wieder in `consoles.json` oder in Entry Points verteilen.
  Conversion-Registry/Planner/Executor bleiben die fachliche Wahrheit.
