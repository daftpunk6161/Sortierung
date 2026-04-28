# Feature-Cull-Liste — Wave 1 (Strategic Reduction 2026)

> **Status:** Accepted (Vorlage fuer T-W1-UI-REDUCTION + T-W1-I18N-ORPHAN-SWEEP)
> **Datum:** 2026-04-28
> **Quelle:** Plan-Task `T-W1-FEATURE-CULL` (siehe `plan.yaml`)
> **Bezug:** ADR-0022 (eine GUI), AGENTS.md (Eine fachliche Wahrheit, Keine doppelte Logik)

Diese Datei ist die **Wahrheit** fuer alle Reduktions-Tasks der Welle 1. Jedes
hier gelistete Feature wird aus dem aktiven Code (`src/`), aus Daten (`data/`),
aus Doku (`docs/`, `README.md`) und aus i18n (`data/i18n/*.json`) **vollstaendig**
entfernt. Halbzustaende (z. B. ungenutzte i18n-Keys) sind nicht erlaubt.

Wenn ein Feature spaeter zurueckkommt, geht das nur ueber neuen ADR + Plan-Task,
nicht ueber stille Wiederbelebung.

---

## Legende

- **REMOVE** — aktiver Code, wird im Wave-1-Sweep komplett entfernt
- **STUB-CLEANUP** — keine Implementierung, nur tote Reste (i18n-Keys, Test-Pins) entfernen
- **ALREADY-ABSENT** — kein Code vorhanden, nur dokumentiert dass es so bleibt
- **KEEP-WITH-CAVEAT** — bleibt (z. B. interne Hilfslogik), aber als Nicht-Feature markieren

---

## A. Frontend-Export (alle 11 Frontends) — REMOVE

Begruendung: 11 separate Frontend-Formate sind Wartungslast ohne belegten Nutzer-
bedarf. Bonus-Task `T-W11-FRONTEND-EXPORT-FORMAT` darf spaeter **eines** wieder
einfuehren, wenn 3+ Nutzer es konkret fordern.

### Source
- `src/Romulus.Infrastructure/Export/FrontendExportService.cs` (komplett)
- `src/Romulus.Infrastructure/Export/CollectionExportService.cs` (komplett)
- `src/Romulus.Contracts/Models/FrontendExportModels.cs` (komplett, inkl. `FrontendExportTargets`-Konstanten)
- `src/Romulus.UI.Wpf/Services/FeatureCommandService.Export.cs` (komplett — gehoert zur Reduktion FeatureCommandService 9 -> 4)
- `src/Romulus.CLI/Program.Subcommands.AnalysisAndDat.cs` Zeilen 104-110 (Subcommand-Registrierung; ggf. mehr — vor Edit reverifizieren)
- `src/Romulus.Api/Program.cs` Zeilen 443-540 (`/export/frontend`-Endpoint)
- `src/Romulus.Api/ProgramHelpers.cs` `ApiFrontendExportRequest`-Record
- `src/Romulus.UI.Wpf/Services/IRunService.cs` / `RunService.cs` — pruefen auf Frontend-Export-Hook und entfernen

### Tests
- `src/Romulus.Tests/FrontendExportCoverageTests.cs` (komplett)
- `src/Romulus.Tests/FeatureCommandSecurityCoverageTests.cs` Zeilen 690-720 (Frontend-Export-Bereich)
- Alle Tests, die `FrontendExportTargets.*` referenzieren (grep vor Edit)

### Daten / Settings
- `data/defaults.json` — pruefen auf Frontend-Export-Defaults
- `data/ui-lookups.json` — pruefen auf Frontend-Export-Eintraege

### i18n
- `data/i18n/de.json` `Tool.LauncherIntegration*` (alle Sub-Keys)
- alle weiteren `Frontend.*`, `Export.Frontend.*`, `Export.Playlist.*`, `Export.LaunchBox.*`, `Export.EmulationStation.*`, `Export.Playnite.*`, `Export.MiSTer.*`, `Export.Pocket.*`, `Export.Onion.*`, `Export.M3U.*`, `Export.RetroArch.*` Keys in **allen** Sprach-Dateien (de.json, en.json, fr.json)

### Doku
- `README.md` — Frontend-Export-Erwaehnungen entfernen (siehe `T-W2-README-REFRESH`)
- `docs/architecture/*` — Frontend-Export-Diagramme entfernen
- `docs/product/competitive-analysis.md` — Frontend-Export aus USP streichen

### Pruefung nach Removal
- `Select-String -Path src/ -Recurse -Pattern "Frontend|LaunchBox|EmulationStation|Playnite|MiSTer|RetroArch|OnionOS|Analogue.*Pocket"` liefert keine produktiven Treffer (nur Konsolen-Klassifizierung in `data/consoles.json` darf bleiben).

---

## B. ScreenScraper / Metadata-Scraping — REMOVE

Begruendung: Externer API-Provider mit Auth-Pflicht (DevId/DevPassword), eigener
Cache, eigene Datenbank-Abhaengigkeit (LiteDB). Liegt ausserhalb der Romulus-
Kernidentitaet (Dedup + Sort + Convert + Audit). Kein Plan-Bonus-Task definiert.

### Source
- `src/Romulus.Infrastructure/Metadata/ScreenScraperMetadataProvider.cs` (komplett)
- `src/Romulus.Infrastructure/Metadata/ScreenScraperSystemMap.cs` (komplett)
- `src/Romulus.Infrastructure/Metadata/MetadataEnrichmentService.cs` (komplett)
- `src/Romulus.Infrastructure/Metadata/LiteDbGameMetadataCache.cs` (komplett)
- `src/Romulus.Contracts/Ports/IGameMetadataProvider.cs` (komplett, inkl. Implementierungs-Interface)
- `src/Romulus.Contracts/Models/MetadataProviderSettings.cs` (komplett)
- DI-Registrierungen: `src/Romulus.Api/Program.cs` Zeilen 46-50, `src/Romulus.CLI/Program.cs` Zeile 1463
- Romulus.Infrastructure.csproj: LiteDB-Paketreferenz pruefen und entfernen, **falls** nur fuer Metadata genutzt

### Tests
- `src/Romulus.Tests/Metadata/ScreenScraperMetadataProviderTests.cs` (komplett)
- `src/Romulus.Tests/Metadata/MetadataEnrichmentServiceTests.cs` (komplett)
- gesamtes Verzeichnis `src/Romulus.Tests/Metadata/` pruefen und ggf. komplett entfernen

### Daten / Settings
- `%APPDATA%\Romulus\settings.json` Property `MetadataProvider*` — Settings-Klasse-Migration: alte Werte ignorieren

### i18n
- alle Keys mit Praefix `Metadata.*`, `Scraper.*`, `ScreenScraper.*` in allen Sprach-Dateien

### Doku
- `docs/architecture/*` Metadata-Provider-Erwaehnungen entfernen
- `README.md` Scraping-Erwaehnungen entfernen

### Pruefung nach Removal
- `Select-String -Path src/ -Recurse -Pattern "ScreenScraper|MetadataProvider|GameMetadata|MetadataCache"` liefert keine Treffer (Ausnahme: ggf. dokumentierte Nicht-Ziele in ADRs).

---

## C. RetroAchievements-Compliance — REMOVE

Begruendung: Spezialfunktion fuer eine externe Drittpartei-Plattform. Kein
belegter Nutzerbedarf, eigener Hash-Katalog, eigener Service. Kein Plan-Bonus-
Task definiert.

### Source
- `src/Romulus.Infrastructure/Monitoring/RetroAchievementsComplianceService.cs` (komplett)
- `src/Romulus.Contracts/Ports/IRetroAchievementsCatalog.cs` (komplett)
- `src/Romulus.Contracts/Models/RetroAchievementsModels.cs` (komplett)
- DI-Registrierungen in API/CLI/WPF — vor Edit greppen
- ggf. Aufrufe aus `IntegrityService` oder Reporting entfernen

### Tests
- `src/Romulus.Tests/Monitoring/RetroAchievementsComplianceServiceTests.cs` (komplett)
- `src/Romulus.Tests/Monitoring/` insgesamt pruefen — wenn ausschliesslich fuer RA, komplett entfernen

### i18n
- alle Keys mit `RetroAchievements.*`, `RA.*`, `Achievement*` in allen Sprach-Dateien

### Doku
- README/competitive-analysis: RetroAchievements-Erwaehnungen entfernen

### Pruefung nach Removal
- `Select-String -Path src/ -Recurse -Pattern "RetroAchievements|IRetroAchievementsCatalog"` liefert keine Treffer.

---

## D. ROM-Patching (IPS / BPS / UPS / xdelta) — REMOVE

Begruendung: Patch-Anwendung ist eine Klasse-eigene Pipeline mit zwei externen
Tools (`flips`, `xdelta`), eigenem Tool-Hash-Eintrag, eigenem GUI-Command und
substanziellen Tests. Kein Romulus-Kernworkflow (Dedup/Sort/Convert/Audit). Wer
Patches anwenden will, nutzt dedizierte Patcher (Floating IPS, Lunar IPS, etc.).
Kein Plan-Bonus-Task definiert.

**Achtung:** `IntegrityService` enthaelt mehr als nur Patching. Nur die
Patch-Methoden (ApplyPatch + Helper) entfernen, restliche Integritaets-Logik
(Hash-Vergleich, Format-Detection ohne Patch) bleibt.

### Source
- `src/Romulus.Infrastructure/Analysis/IntegrityService.cs` Zeilen 315-410 (ApplyPatch + Helper-Methoden) — chirurgisch entfernen, restliche Datei behalten
- `src/Romulus.UI.Wpf/Services/FeatureService.Security.cs` Zeile 104 (ApplyPatch-Wrapper) — entfernen
- `src/Romulus.UI.Wpf/Services/FeatureCommandService.Security.cs` Zeile 86 (Patch-Pipeline-Command) — entfernen
- `src/Romulus.Infrastructure/Tools/` — pruefen ob `flips` / `xdelta` Tool-Definitionen exklusiv fuer Patching existieren; wenn ja entfernen
- `data/tool-hashes.json` — `flips`/`xdelta`-Eintraege entfernen
- `data/conversion-registry.json` — Patch-Container-Eintraege pruefen

### Tests
- `src/Romulus.Tests/IntegrityServicePatchTests.cs` (komplett)
- `src/Romulus.Tests/IntegrityServiceCoverageTests.cs` — Patch-bezogene Faelle entfernen, Rest behalten

### Daten
- `data/format-scores.json` Zeile 150 `patchContainerExtensions` (Liste komplett entfernen oder leeren)

### i18n
- `data/i18n/de.json` (und alle weiteren Sprachen): `Tool.PatchPipeline`, `Tool.PatchPipeline.Desc`, alle `Cmd.PatchPipeline.*` (Applied, ApplyFailed, ExecutionFailed, PatchFileFilter, etc.)

### Pruefung nach Removal
- `Select-String -Path src/ -Recurse -Pattern "ApplyPatch|PatchPipeline|\\.ips|\\.bps|\\.ups|\\.xdelta"` liefert keine produktiven Treffer (Ausnahme: dokumentierte Nicht-Ziele in ADRs).

---

## E. MAME-Set-Building (split / merged / non-merged) — KEEP-WITH-CAVEAT

Inventur-Ergebnis: keine eigene Set-Builder-Implementierung existiert.
Vorhandene Treffer sind:

- `src/Romulus.Infrastructure/Sorting/ConsoleSorter.cs` Zeile 610 `BuildSetMemberships()` — interne Sortier-Hilfsfunktion fuer Arcade-Konsolen, **nicht** eine Set-Building-Feature. Bleibt erhalten.
- `src/Romulus.Tests/Benchmark/Infrastructure/DatasetExpander.cs` Zeilen 4153-4209 `GenerateArcadeMergedNonMerged()` — Test-Datensatz-Generator. Bleibt erhalten (Benchmark-Infrastruktur).
- `src/Romulus.Tests/Benchmark/Infrastructure/CoverageValidator.cs` Zeilen 259-267 — Test. Bleibt erhalten.

**Action:** Keine Loeschung. README darf nicht behaupten "MAME Set-Building unterstuetzt". Falls dort vorhanden, in `T-W2-README-REFRESH` entfernen.

---

## F. In-Browser-Play / Web-Emulation — ALREADY-ABSENT

Inventur-Ergebnis: keine Implementierung im aktiven Code. Keine Action.

**Pruefung dauerhaft:** `T-W2-I18N-ORPHAN-CI-TEST` muss sicherstellen, dass
keine `BrowserPlay.*` / `WebEmulator.*` / `EmulatorJS.*` i18n-Keys einsickern.

---

## G. Plugin / Marketplace / Extension-System — STUB-CLEANUP

Inventur-Ergebnis: Keine Service-/Loader-Implementierung, aber tote Reste:

### i18n (zu entfernen)
- `data/i18n/de.json` Zeile ~113: `Advanced.PluginManager` ("Plugin-Manager")
- `data/i18n/en.json` Zeile ~99: `Advanced.PluginManager` ("Plugin Manager")
- `data/i18n/fr.json` Zeile ~101: `Advanced.PluginManager` ("Gestionnaire de plugins")

### Tests (Pinning erweitern)
- `src/Romulus.Tests/FeatureCommandServiceTests.cs` `RemovedToolKeys_MustNotExistInFeatureCommands()` Liste pruefen — sicherstellen dass `"PluginManager"`, `"PluginMarketplaceFeature"` und neu `"Advanced.PluginManager"` weiterhin als removed gelten.

### Pruefung nach Removal
- `Select-String -Path data/ -Recurse -Pattern "PluginManager|Plugin\\."` liefert keine Treffer ausserhalb dokumentierter Removed-Listen.

---

## H. Sponsor-Beta-Frueh-Zugang / Donation-Tier — ALREADY-ABSENT

Inventur-Ergebnis: keine Tier-/Gate-/Unlock-Logik im Code. `docs/SPONSORS.md`
ist reine Spenden-Landingpage, kein Feature-Hook.

**Pruefung:** `Select-String -Path src/ -Recurse -Pattern "Sponsor|EarlyAccess|Tier|Donation"`
liefert nur ROM-Klassifizierungs-Treffer (z. B. "Beta" als ROM-Junk-Marker in
`FileClassifier.cs` Zeile 97). Diese **bleiben** — sie haben mit Sponsoring nichts zu tun.

---

## Zusammenfassung

| Bereich | Status | Wave-1-Action |
|---------|--------|---------------|
| A. Frontend-Export | REMOVE | Komplett raus aus WPF/CLI/API + Tests + i18n |
| B. ScreenScraper | REMOVE | Komplett raus inkl. LiteDB-Cache + Settings |
| C. RetroAchievements | REMOVE | Komplett raus inkl. Catalog + Models |
| D. ROM-Patching | REMOVE | Chirurgisch aus IntegrityService + Tools + i18n |
| E. MAME-Set-Building | KEEP-WITH-CAVEAT | Nichts loeschen, README ehrlich halten |
| F. In-Browser-Play | ALREADY-ABSENT | Keine Action, dauerhaft per CI absichern |
| G. Plugin/Marketplace | STUB-CLEANUP | Drei i18n-Keys raus, Test-Pinning erweitern |
| H. Sponsor-Tier | ALREADY-ABSENT | Keine Action |

---

## Reihenfolge fuer T-W1-UI-REDUCTION

Empfohlene Bearbeitungsreihenfolge zur Minimierung von Build-Brueche-Kaskaden:

1. **G** Stub-Cleanup zuerst (3 i18n-Keys, niedriges Risiko, kein Test-Impact)
2. **C** RetroAchievements (eigener Service, klarer Schnitt)
3. **B** ScreenScraper (eigene Klassen, klare DI-Punkte)
4. **D** ROM-Patching (chirurgisch in IntegrityService, mit Tests-Anpassung)
5. **A** Frontend-Export zuletzt (groesster Brocken, beruehrt alle drei Entry Points)
6. Build + Test-Lauf nach jedem Schritt
7. Finaler Sweep: `T-W1-I18N-ORPHAN-SWEEP` raeumt nicht erkannte verwaiste Keys
