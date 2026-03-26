# RomCleanup Benchmark

Deterministisches Benchmark-System fuer Console-Detection, Sortier-Sicherheit und Regressions-Gates.

## Quickstart

1. Nur Benchmark-Tests ausfuehren:

```powershell
dotnet test src/RomCleanup.Tests/RomCleanup.Tests.csproj --filter Category=Benchmark --nologo
```

2. Regressions-Gate gegen Baseline ausfuehren:

```powershell
dotnet test src/RomCleanup.Tests/RomCleanup.Tests.csproj --filter Category=BenchmarkRegression --nologo
```

3. Performance-Benchmark (5.000 Dateien):

```powershell
dotnet test src/RomCleanup.Tests/RomCleanup.Tests.csproj --filter Category=BenchmarkPerformance --nologo
```

## Struktur

- `benchmark/ground-truth/`: JSONL Ground-Truth-Sets
- `benchmark/dats/`: synthetische Test-DAT-Dateien
- `benchmark/baselines/`: versionierte Benchmark-Baselines
- `benchmark/reports/`: Laufberichte (gitignored)
- `benchmark/tools/`: Helper-Skripte

## Referenzen

- `docs/architecture/TESTSET_DESIGN.md`
- `docs/architecture/RECOGNITION_QUALITY_BENCHMARK.md`
- `plan/feature-benchmark-testset-1.md`
