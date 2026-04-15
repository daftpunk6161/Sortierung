# Romulus — Offene Themen auf realen Repo-Stand reduziert

Stand: 2026-04-15  
Basis: Repo-Audit gegen `src/`, `benchmark/`, `.github/workflows/` und aktuelle Gate-Laeufe.

Dieses Dokument ist kein historisches Sammel-Backlog mehr. Es fuehrt nur noch reale Restarbeit fuer den aktuellen Release-Pfad. Historische Wunschlisten bleiben in `archive/completed/plans/` und `docs/plan/`.

## Erledigt im Repo

- [x] Benchmark- und Gate-Framework ist vorhanden:
  `CoverageGateTests`, `QualityGateTests`, `HoldoutGateTests`, `AntiGamingGateTests`,
  `ConfidenceCalibrationTests`, `DatCoverageBenchmarkTests`, `RepairSafetyBenchmarkTests`,
  `PerformanceBenchmarkTests`, `BaselineRegressionGateTests`, `TrendAnalyzer`,
  `BenchmarkHtmlReportWriter`, `CoverageValidator`, `ManifestCalculator`.
- [x] Datensatz-Breite ist bereits stark ausgebaut:
  `benchmark/manifest.json` steht aktuell bei `7639` Ground-Truth-Eintraegen,
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
- [x] `specialAreas.holdout` ist jetzt in Manifest, Coverage-Actuals und Gap-Report konsistent verdrahtet.
- [x] Der autoritative Manifest-Pfad ist aktiv verdrahtet:
  `ManifestCalculator` -> `benchmark/tools/Update-Manifest.ps1` -> `benchmark/manifest.json`.
- [x] Der CI-Workflow fuehrt die relevante Release-Gate-Suite jetzt explizit aus
  und regeneriert davor das Manifest.

## Aktiv offen

### P0 — Echten Hard-Fail-Betrieb fuer Quality/Holdout sauber machen

- [ ] Hinweis fuer echten Hard-Fail-Betrieb:
  Mit `ROMULUS_ENFORCE_QUALITY_GATES=true` schlagen aktuell mindestens
  Holdout-Drift und `biosAsGameRate` fehl. Vor voller Enforce-Umschaltung
  muessen diese fachlichen Luecken geschlossen werden.

### P1 — Coverage weiter ausreizen, aber nur in echten Restluecken

Die Breite ist bereits hoch genug. Zusatzeintraege sollen nicht kuenstlich in
bereits ueberversorgte Familien gepumpt werden, sondern gezielt in die noch
offenen Fallklassen und Spezialbereiche.

- [ ] Fallklassen mit echtem Restbedarf:
  `FC-12` `43/45`, `FC-13` `40/45`, `FC-14` `30/35`, `FC-15` `22/25`, `FC-20` `90/100`.
- [ ] Spezialbereiche mit echtem Restbedarf:
  `biosSystems` `23/25`, `arcadeClone` `36/38`, `gbGbcCgb` `15/18`, `md32x` `12/14`,
  `directoryBased` `40/45`, `biosErrorModes` `24/25`, `arcadeConfusion` `23/25`,
  `headerVsHeaderlessPairs` `15/25`, `containerVariants` `19/20`,
  `satDcDisambiguation` `8/10`, `pcePcecdDisambiguation` `5/8`.
- [ ] Neue Samples bevorzugt als realistische Disambiguation-, Container-,
  Broken-Set- und Directory-Faelle verteilen:
  Cartridge, Disc, Arcade und Computer breit nutzen; keine kosmetische
  Massenaufblaehung in bereits weit ueber Ziel liegenden Bereichen.

### P1 — Letzte produktive Fachluecke ausserhalb Benchmark

- [ ] PS2 CD/DVD-Erkennung robuster machen.
  Der verbleibende sinnvolle Ausbau ist `SYSTEM.CNF`-basierte Erkennung statt
  reiner Groessenheuristik fuer `chdman createcd/createdvd`.

## Spaeter / optional

- [ ] Weitere Benchmark-Tiefe nach Schliessen der echten Luecken:
  mehr Container-/Clone-/Broken-Set-Varianten fuer Arcade, Computer und Disc,
  wenn sie eine reale Fehlklasse absichern und nicht nur Zahlen aufblasen.
- [ ] Parallele Batch-Conversion nur dann, wenn Determinismus, Tool-Isolation,
  Cleanup und Verifikation sauber beweisbar bleiben.
- [ ] Zusaetzliche Formatpfade wie MDF/MDS/NRG nur bei belastbarer Tool-Kette
  und klarer Audit-/Undo-Strategie.
- [ ] Cross-Root-Repair und Archive-Rebuild bleiben optionale Folgeepics,
  nicht Release-Pflicht fuer den aktuellen Kern.

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
- [x] Neue Sonderlogik wieder in `consoles.json` oder in Entry Points verteilen.
  Conversion-Registry/Planner/Executor bleiben die fachliche Wahrheit.
