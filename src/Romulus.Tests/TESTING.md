# Romulus.Tests - Konventionen

## Test-Naming

Schema: `Subject_Scenario_ExpectedBehavior`

- **Subject**: Klasse, Methode oder fachlicher Begriff (z. B. `RootBoundaryValidator`,
  `ConsoleDetector`, `RunResultProjection`).
- **Scenario**: konkrete Eingabe oder Vorbedingung (z. B. `EmptyRoots`,
  `WithCancelToken`, `OnDuplicateGameKey`).
- **ExpectedBehavior**: beobachtbares Ergebnis (z. B. `ReturnsBlocked`,
  `ThrowsInvalidOperation`, `KeepsSourceUntouched`).

Praefixe `R\d+_`, `R\d+\.\d+_`, `Block[A-Z]\d_`, `Phase\d+_`, `Wave\d+_` sind
historisch und werden in zukuenftigen Domaenen-Splits (Block E2/E3) abgebaut.
Neue Tests sollen den Praefix nicht mehr verwenden.

### Beispiele

```csharp
[Fact]
public void RootBoundaryValidator_DetectsModificationOfFileOutsideRoots() { ... }

[Fact]
public void RunResultProjection_DecisionFields_OrderedDeterministicallyAndCaseNormalized() { ... }
```

## Zentrale Test-Hilfen

Liegen unter `TestFixtures/`. Nicht parallel im Test-File reimplementieren.

| Datei | Zweck |
|---|---|
| `EnrichmentTestHarness.cs` | `PipelineContext` + `RunOptions` Builder fuer Enrichment-Phase-Tests (Block D1). |
| `FixedFamilyDatPolicyResolver` | Fester Policy-Resolver fuer Familien-Tests (Block D1). |
| `RootBoundaryValidator.cs` | SHA-256-Snapshot/Verify fuer Pfade ausserhalb erlaubter Roots (Block D2). |
| `ScenarioToolRunner.cs` | `IToolRunner`-Doppel mit Crash/Cancel/DiskFull/OutputTooSmall/HashMismatch (Block D3). |
| `RunResultProjection.cs` | Vergleichsprojektionen fuer Entry-Point-Paritaet (Block D4). |
| `TraceCapture.cs` | `System.Diagnostics.Trace`-Capture fuer Log-Verifikation (Block D6). |
| `RepoPaths.cs` | `RepoFile(...)` und `SrcRoot()` (Block E4). Ersetzt lokale `FindRepoFile`/`FindSrcRoot`-Kopien. |
| `InMemoryFileSystem.cs`, `TrackingAuditStore.cs`, `StubToolRunner.cs`, `StubDialogService.cs`, ... | Bestehende, weiter verwendete Stubs/Fakes. |

## Was Tests NICHT tun sollen

- Keine `Assert.Contains(literal, sourceText)`-Spiegel gegen die Codebasis.
- Keine no-crash-only Tests (`Assert.True(true)` nach Action).
- Keine reine Reflection-Surface-Spiegel (Klassennamen, Members), wenn das
  fachliche Verhalten testbar ist.
- Keine doppelten Pfad-/Builder-Helper neben den zentralen Fixtures.

Vergleiche `.claude/rules/testing.instructions.md`.
