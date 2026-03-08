# ROM Cleanup – Test-Strategie

**Datum:** 2026-02-27  
**Grundsatz:** Kein Alibi-Test. Jeder Test hat eine **Failure-First-Anforderung** – er muss ohne den zu testenden Code rot werden.

---

## 1. Test-Pyramide

```
        ┌──────────────────┐
        │   E2E (GUI-Live) │  ~5 Tests  – echte Dateisystem-Ops
        ├──────────────────┤
        │ Integration      │  ~15 Tests – mehrere Module zusammen
        ├──────────────────┤
        │ Unit             │  ~120 Tests– ein Modul, gemockte Deps
        └──────────────────┘
```

---

## 2. Bestehende Testdateien

| Datei | Stage | Zweck |
|---|---|---|
| `BugFinder.Tests.ps1` | unit | Settings/AppState Round-Trip |
| `CacheBenchmark.Tests.ps1` | unit | LRU Baseline (legacy, vor F-03) |
| `Classification.Tests.ps1` | unit | Konsolen-Erkennung via Dateinamen |
| `Convert.Tests.ps1` | unit | Format-Konvertierung Mocks |
| `Core.Tests.ps1` | unit | GameKey-Generierung, Region-Scoring |
| `Dat.Tests.ps1` | unit | DAT-XML- und CLRmamePro-Parser |
| `Dedupe.Tests.ps1` | unit | Region-Dedupe-Logik |
| `FileOps.Tests.ps1` | unit | Move/Link-Operationen |
| `FormatScoring.Tests.ps1` | unit | Prioritäts-Score-Berechnung |
| `GuiBugFinder.Tests.ps1` | unit | GUI-State-Regressions |
| `Modules.Tests.ps1` | unit | Modulexistenz + Parse-Check |
| `MutationBaseline.Tests.ps1` | unit | Mutations-Baseline |
| `Ps3Dedupe.Tests.ps1` *(neu)* | unit | PS3-Ordner-Gruppierung |
| `Report.Tests.ps1` | unit | HTML/CSV-Reportgenerierung |
| `SetParsing.Tests.ps1` | unit | CUE/GDI/M3U-Parser |
| `Settings.BugFinder.Tests.ps1` | unit | Settings-Schema-Validierung |
| `Tools.Tests.ps1` | unit | Werkzeug-Wrappers, Hash-Prüfung |
| `ZipSort.Tests.ps1` | unit | ZIP-Sortierlogik |

### Neu hinzugefügt (dieses Projekt)

| Datei | Stage | Befund | Failure-First |
|---|---|---|---|
| `ToolHash.Mandatory.Tests.ps1` | unit | F-01: WARN bei fehlendem hash-json | Vor Fix: kein WARN → Test rot |
| `SetParsing.EdgeCase.Tests.ps1` | unit | F-02: Verbose-Audit bei leerem RootPath | Vor Fix: kein Verbose → Test rot |
| `LruCache.Perf.Tests.ps1` | unit | F-03: 10k Evictions < 500 ms | Vor Fix: ArrayList O(n²) → > 500ms → rot |
| `ChdHeaderCache.Tests.ps1` | unit | F-04: Cache-Treffer nach erstem Aufruf | Vor Fix: kein Cache → zweiter Aufruf öffnet Datei → rot |
| `Settings.SchemaWarn.Tests.ps1` | unit | F-09: Write-Log bei Schema-Fehler | Vor Fix: nur Write-Warning → kein Log-Eintrag → rot |
| `Ps3Dedupe.Tests.ps1` | unit | PS3-Gruppenlogik | Vor Implementierung → rot |
| `RollbackWizard.Tests.ps1` | unit | Rollback-CSV-Parsing + Sicherheit | Vor Implementierung → rot |
| `WpfViewModels.Tests.ps1` | unit | C# Shim-Typen (ViewModelBase etc.) | Vor WpfShims.ps1 → rot |
| `WpfSmoke.Tests.ps1` | integration | Fenster instanziierbar ohne Exception | Vor WpfXaml/WpfHost → rot |

---

## 3. Test-Konventionen

### 3.1 Modul-Loading

Jeder Test-File lädt seine Abhängigkeiten explizit via Dot-Source:

```powershell
BeforeAll {
    $root = $PSScriptRoot
    while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
        $root = Split-Path -Parent $root
    }
    . (Join-Path $root 'dev\modules\Settings.ps1')
    . (Join-Path $root 'dev\modules\ZuTestendesModul.ps1')
}
```

**Warum:** Kein globaler Import-Module-Overhead. Tests sind isoliert. Reihenfolge ist deterministisch.

### 3.2 Naming

```
<Modul>.<Thema>.Tests.ps1       # Unit
<Feature>.Tests.ps1             # Integration
<Feature>.E2E.Tests.ps1         # E2E
```

### 3.3 Skipped-Tests statt Lügen

Wenn ein Test auf nicht verfügbare Infrastruktur trifft (STA-Thread, echte Dateipfade):

```powershell
if (-not $script:isSta) {
    Set-ItResult -Skipped -Because 'STA-Thread erforderlich'
    return
}
```

**Verboten:** `Should -BeTrue` auf `$true` (Alibi-Test), `try/catch` das immer grün macht.

### 3.4 Mock-Strategie

| Situation | Empfohlen |
|---|---|
| Externe Tools (chdman, 7z) | `Mock` via Pester 5 oder Dummy-Wrapper-Skripte in `dev/tests/fixtures/` |
| Dateisystem | `New-TemporaryFile` / `New-Item -TempDir` + cleanup in `AfterAll` |
| `$Log`-Callback | `$captured = [List]::new()` + `{ param($msg) $captured.Add($msg) }` |
| GUI-Controls | Mock-`$ctx`: `@{ btnRunGlobal = New-Object AnyMockType }` im Test |

### 3.5 Benchmark-Gate (`tests: benchmark gate`)

Der separate CI-Stage prüft Leistungsregression via `CacheBenchmark.Tests.ps1` + `LruCache.Perf.Tests.ps1`. **Beides muss bestehen bevor ein Merge erfolgt.**

---

## 4. Coverage-Ziel

| Modul | Minimal-Coverage |
|---|---|
| `Tools.ps1` | 70% |
| `Dedupe.ps1` | 65% |
| `Core.ps1` | 60% |
| `Classification.ps1` | 55% |
| `WpfShims.ps1` | 80% |
| `WpfHost.ps1` | 50% |
| `WpfEventHandlers.ps1` | 40% (GUI-Handling ist schwer vollständig zu covern) |

Task `tests: coverage` prüft aktuell einen globalen **Interim-Schwellwert von 34%** (`-CoverageTarget 34`).
Das **Sprintziel bleibt 50%** (wird nach Ausbau der Harness-Tests wieder als Gate gesetzt).

---

## 5. E2E-Tests

E2E-Tests (`dev/tests/e2e/`) nutzen synthetische ROM-Verzeichnisse aus `dev/tests/fixtures/` mit Dummy-Dateien (bekannte Hashes, 0-Byte-Stubs). Sie verifizieren End-to-End:

1. Modul-Initialisierung ohne Fehler
2. `Invoke-RegionDedupe` mit DryRun-Modus
3. Report-Generierung (CSV + HTML vorhanden nach Lauf)
4. Keine echten Dateibewegungen ohne `Mode=Move`

---

## 6. CI-Pipeline-Stages

| Stage | Trigger | Was wird getestet |
|---|---|---|
| `unit` | jeder Commit | 24+ Unit-Testdateien, < 30s |
| `integration` | PR | Unit + Integration (WpfSmoke, DAT-Index) |
| `e2e` | vor Release | Vollständiger Durchlauf mit Fixtures |
| `benchmark gate` | vor Release | Performance-Benchmarks |
| `coverage` | vor Release | Coverage ≥ 50% |
