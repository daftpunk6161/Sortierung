# ROM Cleanup – Test-Strategie

**Stand:** 2026-03-15  
**Framework:** xUnit (.NET 10, `src/RomCleanup.Tests/`)  
**Grundsatz:** Kein Alibi-Test. Jeder Test hat eine **Failure-First-Anforderung** – er muss ohne den zu testenden Code rot werden.

---

## 1. Test-Pyramide

```
        ┌──────────────────┐
        │   Integration    │  RunOrchestrator, API-RunManager, FileSystem-Ops
        ├──────────────────┤
        │   Unit           │  72 Testdateien, 3090+ Tests
        └──────────────────┘
        Gesamt: 3090+ Tests (xUnit, alle grün)
```

---

## 2. Testdateien-Übersicht

Alle Tests liegen in `src/RomCleanup.Tests/` (xUnit, 72 Testdateien):

### 2.1 Core-/Engine-Tests

| Datei | Zweck |
|---|---|
| `GameKeyNormalizerTests.cs` | GameKey-Generierung, ASCII-Fold, Tag-Parsing, Alias-Map |
| `RegionDetectorTests.cs` | Region-Erkennung aus Dateinamen |
| `DeduplicationEngineTests.cs` | Winner-Selection (deterministisch), 1G1R, BIOS/Junk |
| `FormatScorerTests.cs` | Format-Score-Berechnung (CHD, ISO, ZIP etc.) |
| `VersionScorerTests.cs` | Versions-/Revisions-Scoring |
| `VersionHelperTests.cs` | Versions-Parsing-Hilfsfunktionen |
| `FileClassifierTests.cs` | BIOS/Junk/Game-Klassifikation |
| `ConsoleDetectorTests.cs` | Konsolen-Erkennung (Ordner, Extension, DAT) |
| `ExtensionNormalizerTests.cs` | Dateiendungs-Normalisierung |
| `SetParsingTests.cs` | CUE/GDI/CCD/M3U-Parser |
| `RuleEngineTests.cs` | Regelanwendung auf ROM-Kandidaten |
| `InsightsEngineTests.cs` | Analyse-/Statistik-Engine |

### 2.2 Infrastructure-Tests

| Datei | Zweck |
|---|---|
| `FileSystemAdapterTests.cs` | Path-Traversal-Schutz, Reparse-Point-Blocking, Move/Scan |
| `AuditCsvStoreTests.cs` | Audit-CSV-Erzeugung, CSV-Injection-Schutz, Rollback |
| `AuditSigningServiceTests.cs` | SHA256-Signierung von Audit-CSVs |
| `JsonlLogWriterTests.cs` | JSONL-Logging, Rotation, Felder |
| `SettingsLoaderTests.cs` | Settings-Laden/Validierung/Defaults |
| `FileHashServiceTests.cs` | SHA1/SHA256/MD5-Hashing, LRU-Cache |
| `Crc32Tests.cs` | CRC32-Berechnung |
| `ArchiveHashServiceTests.cs` | Hash-Extraktion aus Archiven |
| `ParallelHasherTests.cs` | Paralleles Hashing |
| `DatRepositoryAdapterTests.cs` | DAT-XML-Parsing, XXE-Schutz, Parent/Clone |
| `DatSourceServiceTests.cs` | DAT-Download, SHA256-Sidecar-Verifizierung |
| `ReportGeneratorTests.cs` | HTML/CSV-Reports, CSP-Header, HTML-Encoding |
| `FormatConverterAdapterTests.cs` | Format-Konvertierung (CHD/RVZ/ZIP) |
| `SortingTests.cs` | Konsolen-Sortierung |
| `LruCacheTests.cs` | LRU-Cache (Thread-Safety, Eviction) |
| `AppStateStoreTests.cs` | App-State, Undo/Redo, Watch-Pattern |

### 2.3 Orchestrierung & API-Tests

| Datei | Zweck |
|---|---|
| `RunOrchestratorTests.cs` | Full Pipeline (Preflight→Scan→Dedupe→Sort→Move) |
| `RunManagerTests.cs` | API-RunManager, Singleton-Run, Cancel |
| `RateLimiterTests.cs` | Rate-Limiting (120/min sliding window) |
| `ExecutionHelpersTests.cs` | Ausführungshelfer |
| `SafetyValidatorTests.cs` | Safety-Checks vor Operationen |

### 2.4 Erweiterte Services

| Datei | Zweck |
|---|---|
| `EventBusTests.cs` | Pub/Sub-Events, Wildcard-Topics |
| `PipelineEngineTests.cs` | Conditional-Step-Chains |
| `QuarantineServiceTests.cs` | ROM-Quarantäne |
| `PhaseMetricsCollectorTests.cs` | Phasen-Zeitmessung |
| `HistoryAndIndexTests.cs` | Run-History, Scan-Index |
| `CrossRootAndHardlinkTests.cs` | Cross-Root-Deduplizierung, Hardlinks |
| `FolderDeduplicatorTests.cs` | Ordner-Deduplizierung |
| `DiscHeaderDetectorTests.cs` | Disc-Header-Erkennung (PS1/PS2/Saturn etc.) |
| `CatchGuardServiceTests.cs` | Silent-Catch-Governance |
| `ErrorClassifierTests.cs` | Fehlerklassen (Transient/Recoverable/Critical) |

---

## 3. Test-Konventionen

### 3.1 Naming

```
<Klasse>Tests.cs    # z.B. GameKeyNormalizerTests.cs, DeduplicationEngineTests.cs
```

### 3.2 Test-Methoden-Naming

```csharp
[Fact]
public void SelectWinner_SameInputs_ReturnsSameWinner()  // Determinismus

[Theory]
[InlineData("EU", 1000)]
[InlineData("US", 999)]
public void GetRegionScore_PreferredRegion_ReturnsExpectedScore(string region, int expected)
```

### 3.3 Fixture-Strategie

| Situation | Empfohlen |
|---|---|
| Dateisystem | `Path.GetTempPath()` + `Directory.CreateTempSubdirectory()` + cleanup in `Dispose` |
| Externe Tools (chdman, 7z) | Mock via Interface (`IToolRunner`) |
| Port-Interfaces | Eigene Test-Implementierungen oder Mocks |
| Große Dateien (>500 MB) | Überspringen mit `Skip` oder künstliche Stubs |

### 3.4 Pflicht-Invarianten

Jeder Test muss einen echten Fehler finden können. Typische Invarianten:

- **Winner-Selection ist deterministisch** — gleiche Inputs = gleicher Winner
- **Kein Move außerhalb der Root** — Path-Traversal-Versuche müssen scheitern
- **Keine leeren Keys** — GameKey-Normalisierung darf keine leeren Strings liefern
- **CSV-Injection-Schutz** — führende `=`, `+`, `-`, `@` werden escaped

### 3.5 Verboten

- `Assert.True(true)` (Alibi-Test)
- `try/catch` das Tests immer grün macht
- Tests ohne Assertions

---

## 4. Coverage-Ziel

CI-Pipeline prüft einen globalen **Minimum-Schwellwert von 50%**.

| Bereich | Minimal-Coverage |
|---|---|
| Core-Domain-Logik | 70% |
| Infrastructure-Adapter | 50% |
| API/CLI Entry Points | 40% |

---

## 5. CI-Pipeline (`.github/workflows/test-pipeline.yml`)

| Job | Gate | Bei Fehler |
|---|---|---|
| Unit-Tests + Coverage | 50% Minimum | Fail |
| Governance | Modul-Grenzen, Komplexität | Warn only |
| Mutation-Testing | Reporting only | continue-on-error |
| Benchmark-Gate | Regression-Erkennung | continue-on-error |

Trigger: Push/PR auf `dev/**`, `.github/**`

### Testausführung

```bash
# Alle Tests
dotnet test src/RomCleanup.sln

# Einzelnes Testprojekt
dotnet test src/RomCleanup.Tests/RomCleanup.Tests.csproj

# Mit Filter
dotnet test src/RomCleanup.sln --filter "FullyQualifiedName~GameKey"

# Mit Coverage
dotnet test src/RomCleanup.sln --collect:"XPlat Code Coverage"
```
