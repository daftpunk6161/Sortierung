# ROM Cleanup Test Suite

## Overview

Comprehensive Pester 5 test suite for `simple_sort.ps1` with mutation testing support.

## Test Files

| File | Purpose | Mutations Killed |
|------|---------|------------------|
| `Startup.Tests.ps1` | Script startup, parameter validation | - |
| `RegionDedupe.Tests.ps1` | Winner selection, region scoring | M1, M2, M5, M6 |
| `Security.Tests.ps1` | XSS, CSV injection, path traversal | M4, M7, M11, M12 |
| `FaultInjection.Tests.ps1` | DAT errors, tool failures, bad input | M8, M9, M10, M11 |
| `GameKey.Tests.ps1` | Key normalization, empty protection | M3 |
| `E2E.Tests.ps1` | End-to-end smoke (dryrun/move/rollback) | - |
| `UiSmoke.Tests.ps1` | Accessibility smoke (DPI/tab-navigation) | - |
| `FixtureFactory.ps1` | Reusable CUE/GDI/CCD/M3U/Archive/Junk/Bios test fixtures | - |

## Running Tests

```powershell
# Run all tests
Invoke-Pester -Path .\dev\tests\*.Tests.ps1 -Output Detailed

# Run specific test file
Invoke-Pester -Path .\dev\tests\Security.Tests.ps1 -Output Detailed

# Run with coverage
Invoke-Pester -Path .\dev\tests\*.Tests.ps1 -CodeCoverage .\simple_sort.ps1
```

## Mutation Testing

The mutation runner validates that tests catch specific code changes.

```powershell
# Run mutation tests
.\dev\tools\Invoke-MutationLight.ps1

# Stop on first survived mutation
.\dev\tools\Invoke-MutationLight.ps1 -StopOnSurvived

# Verbose output
.\dev\tools\Invoke-MutationLight.ps1 -Verbose
```

### Mutation Definitions

Mutations are defined in `dev/tools/mutations.json`:

| ID | Mutation | Effect |
|----|----------|--------|
| M1 | `1000 - $idx` → `1000 + $idx` | Breaks region priority |
| M2 | `Descending=$true` → `$false` | Breaks version preference |
| M3 | Remove `IsNullOrWhiteSpace` | Allows empty game keys |
| M4 | Remove `__DUP` suffix | Causes file overwrites |
| M5 | Swap EU/US priority | Wrong region preference |
| M6 | Remove language guard | (Fr) treated as region |
| M7 | Remove `HtmlEncode` | XSS vulnerability |
| M8 | Ignore tool exit code | Silent tool failures |
| M9 | Skip track validation | Missing track files |
| M10 | Wrong XML exception | Crash on bad DAT |
| M11 | Remove `..` check | Path traversal attack |
| M12 | Remove CSV prefix | Formula injection |

### Exit Codes

- `0` - All mutations killed (tests are robust)
- `1` - Mutations survived (tests need improvement)

## Stage Pipeline (Unit/Integration/E2E)

Run staged test pipeline:

```powershell
# all stages
.\dev\tools\Invoke-TestPipeline.ps1 -Stage all

# single stage
.\dev\tools\Invoke-TestPipeline.ps1 -Stage unit
.\dev\tools\Invoke-TestPipeline.ps1 -Stage integration
.\dev\tools\Invoke-TestPipeline.ps1 -Stage e2e
```

Pipeline reports:

- Timestamped: `reports/test-pipeline-YYYYMMDD-HHMMSS.json|.md`
- Stable latest alias: `reports/test-pipeline-latest.json|.md`

## Reusable E2E Fixtures

Use `New-E2ESmokeFixtureSet` from `dev/tests/FixtureFactory.ps1` to provision a complete parser dataset in `TestDrive:`:

- CUE set (+tracks)
- GDI set (+tracks)
- CCD set (+img/sub)
- M3U playlist referencing multiple set types
- ZIP archive sample
- JUNK candidate file
- BIOS candidate file

## Performance Baseline Snapshots

Generate reproducible perf baselines in `reports/`:

```powershell
# Full baseline (default sample sizes)
.\dev\tools\Invoke-PerformanceSnapshot.ps1

# Faster local baseline while iterating
.\dev\tools\Invoke-PerformanceSnapshot.ps1 -GameKeySamples 12000 -ConsoleSamples 12000 -ScanFiles 3000

# Skip scan phase (GameKey + Console only)
.\dev\tools\Invoke-PerformanceSnapshot.ps1 -SkipScan
```

Output artifacts:

- Timestamped: `reports/performance-snapshot-YYYYMMDD-HHMMSS.json|.md`
- Stable latest alias: `reports/performance-snapshot-latest.json|.md`

## Test Categories

### Negative Tests (30%+)
- Invalid input handling
- Empty/null values
- Malformed files
- Edge cases

### Fault Injection (10+ tests)
- Malformed DAT XML
- Missing CUE/GDI tracks
- Tool exit codes
- Archive corruption
- Disk full simulation

### Security Tests
- Path traversal attempts
- XSS payloads in filenames
- CSV formula injection
- Zip slip protection

## Adding New Tests

1. Add test to appropriate file or create new `*.Tests.ps1`
2. If testing a mutation, add `M{N}-KILL` to test name
3. Update `mutations.json` with new mutation if needed
4. Run `Invoke-MutationLight.ps1` to validate

## Requirements

- PowerShell 5.1+
- Pester 5.x (`Install-Module Pester -Force -SkipPublisherCheck`)
