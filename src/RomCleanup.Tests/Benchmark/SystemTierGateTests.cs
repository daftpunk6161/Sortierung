using RomCleanup.Tests.Benchmark.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace RomCleanup.Tests.Benchmark;

/// <summary>
/// CI gate tests that verify per-system-tier quality thresholds per EVALUATION_STRATEGY §8.1.
/// Systems are grouped into tiers by detection difficulty and evaluated against tier-specific gates.
/// </summary>
[Collection("BenchmarkEvaluation")]
public sealed class SystemTierGateTests : IClassFixture<BenchmarkFixture>
{
    private readonly BenchmarkFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SystemTierGateTests(BenchmarkFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    // Tier definitions per EVALUATION_STRATEGY §8.1
    private static readonly string[] DeterministicHeaderSystems =
        ["NES", "N64", "GBA", "GB", "LYNX", "A78"];

    private static readonly string[] ComplexHeaderSystems =
        ["SNES", "MD", "GBC"];

    private static readonly string[] DiscHeaderSystems =
        ["PS1", "PS2", "GC", "WII", "PSP"];

    private static readonly string[] DiscComplexSystems =
        ["SAT", "DC", "SCD", "3DO", "NEOCD", "PCECD", "PCFX", "JAGCD", "CD32"];

    private static readonly string[] ExtensionOnlySystems =
        ["A26", "A52", "AMIGA", "MSX", "NDS", "3DS", "SMS", "GG", "PCE", "32X", "FMTOWNS", "CDI"];

    [Fact]
    [Trait("Category", "QualityGate")]
    public void Tier1_DeterministicHeader_HighPrecision()
    {
        var results = EvaluateAllSets();
        var perSystem = MetricsAggregator.CalculatePerSystem(results);

        _output.WriteLine("=== Tier 1: Deterministic Header ===");
        _output.WriteLine("Target: Precision >= 99%, Recall >= 97%, WrongRate <= 0.2%");
        LogTier(perSystem, DeterministicHeaderSystems);

        // Informational — enforced only with ROMCLEANUP_ENFORCE_QUALITY_GATES
        if (!IsEnforceMode()) return;

        foreach (var sys in DeterministicHeaderSystems)
        {
            if (!perSystem.TryGetValue(sys, out var m)) continue;
            Assert.True(m.Precision >= 0.99, $"{sys} Precision {m.Precision:P2} < 99%");
        }
    }

    [Fact]
    [Trait("Category", "QualityGate")]
    public void Tier2_ComplexHeader_GoodPrecision()
    {
        var results = EvaluateAllSets();
        var perSystem = MetricsAggregator.CalculatePerSystem(results);

        _output.WriteLine("=== Tier 2: Complex Header ===");
        _output.WriteLine("Target: Precision >= 97%, Recall >= 90%, WrongRate <= 0.5%");
        LogTier(perSystem, ComplexHeaderSystems);

        if (!IsEnforceMode()) return;

        foreach (var sys in ComplexHeaderSystems)
        {
            if (!perSystem.TryGetValue(sys, out var m)) continue;
            Assert.True(m.Precision >= 0.97, $"{sys} Precision {m.Precision:P2} < 97%");
        }
    }

    [Fact]
    [Trait("Category", "QualityGate")]
    public void Tier3_DiscHeader_ReliablePrecision()
    {
        var results = EvaluateAllSets();
        var perSystem = MetricsAggregator.CalculatePerSystem(results);

        _output.WriteLine("=== Tier 3: Disc Header ===");
        _output.WriteLine("Target: Precision >= 96%, Recall >= 88%, WrongRate <= 0.5%");
        LogTier(perSystem, DiscHeaderSystems);

        if (!IsEnforceMode()) return;

        foreach (var sys in DiscHeaderSystems)
        {
            if (!perSystem.TryGetValue(sys, out var m)) continue;
            Assert.True(m.Precision >= 0.96, $"{sys} Precision {m.Precision:P2} < 96%");
        }
    }

    [Fact]
    [Trait("Category", "QualityGate")]
    public void Tier4_DiscComplex_AcceptablePrecision()
    {
        var results = EvaluateAllSets();
        var perSystem = MetricsAggregator.CalculatePerSystem(results);

        _output.WriteLine("=== Tier 4: Disc Complex ===");
        _output.WriteLine("Target: Precision >= 95%, Recall >= 85%, WrongRate <= 1%");
        LogTier(perSystem, DiscComplexSystems);

        if (!IsEnforceMode()) return;

        foreach (var sys in DiscComplexSystems)
        {
            if (!perSystem.TryGetValue(sys, out var m)) continue;
            Assert.True(m.Precision >= 0.95, $"{sys} Precision {m.Precision:P2} < 95%");
        }
    }

    [Fact]
    [Trait("Category", "QualityGate")]
    public void Tier5_ExtensionOnly_BaselinePrecision()
    {
        var results = EvaluateAllSets();
        var perSystem = MetricsAggregator.CalculatePerSystem(results);

        _output.WriteLine("=== Tier 5: Extension Only ===");
        _output.WriteLine("Target: Precision >= 90%, Recall >= 70%, WrongRate <= 2%");
        LogTier(perSystem, ExtensionOnlySystems);

        if (!IsEnforceMode()) return;

        foreach (var sys in ExtensionOnlySystems)
        {
            if (!perSystem.TryGetValue(sys, out var m)) continue;
            Assert.True(m.Precision >= 0.90, $"{sys} Precision {m.Precision:P2} < 90%");
        }
    }

    [Fact]
    [Trait("Category", "QualityGate")]
    public void QualityLevels_AG_Summary()
    {
        var results = EvaluateAllSets();
        var levels = QualityLevelEvaluator.Evaluate(results);

        _output.WriteLine("=== Quality Levels A-G ===");
        foreach (var level in levels)
        {
            _output.WriteLine(level.ToString());
        }
    }

    private void LogTier(IReadOnlyDictionary<string, SystemMetrics> perSystem, string[] systems)
    {
        foreach (var sys in systems)
        {
            if (perSystem.TryGetValue(sys, out var m))
            {
                _output.WriteLine($"  {sys,-8} P={m.Precision:P2} R={m.Recall:P2} F1={m.F1:P2} (TP={m.TruePositive} FP={m.FalsePositive} FN={m.FalseNegative})");
            }
            else
            {
                _output.WriteLine($"  {sys,-8} (no data)");
            }
        }
    }

    private List<BenchmarkSampleResult> EvaluateAllSets()
    {
        var setFiles = new[]
        {
            "golden-core.jsonl",
            "edge-cases.jsonl",
            "negative-controls.jsonl",
            "golden-realworld.jsonl",
            "chaos-mixed.jsonl",
            "dat-coverage.jsonl",
            "repair-safety.jsonl",
        };

        var results = new List<BenchmarkSampleResult>();
        foreach (var set in setFiles)
        {
            results.AddRange(BenchmarkEvaluationRunner.EvaluateSet(_fixture, set));
        }
        return results;
    }

    private static bool IsEnforceMode() =>
        string.Equals(
            Environment.GetEnvironmentVariable("ROMCLEANUP_ENFORCE_QUALITY_GATES"),
            "true",
            StringComparison.OrdinalIgnoreCase);
}
