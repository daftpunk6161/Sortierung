using RomCleanup.Contracts.Models;
using RomCleanup.Core.Classification;
using Xunit;

namespace RomCleanup.Tests.Benchmark;

public sealed class MetricsAggregatorTests
{
    [Fact]
    public void MetricsAggregator_CalculatesCorrectly()
    {
        var samples = new List<BenchmarkSampleResult>
        {
            new("1", BenchmarkVerdict.Correct, "NES", "NES", 90, false, null),
            new("2", BenchmarkVerdict.Wrong, "NES", "SNES", 90, false, null),
            new("3", BenchmarkVerdict.Correct, "SNES", "SNES", 90, false, null),
            new("4", BenchmarkVerdict.Missed, "SNES", "UNKNOWN", 0, false, null),
            new("5", BenchmarkVerdict.FalsePositive, null, "NES", 90, false, null),
        };

        var perSystem = MetricsAggregator.CalculatePerSystem(samples);
        var aggregate = MetricsAggregator.CalculateAggregate(samples);
        var matrix = MetricsAggregator.BuildConfusionMatrix(samples);

        Assert.True(perSystem.ContainsKey("NES"));
        Assert.True(perSystem.ContainsKey("SNES"));

        // NES: TP=1, FP=1, FN=1 => precision=0.5, recall=0.5, f1=0.5
        Assert.Equal(0.5, perSystem["NES"].Precision, 3);
        Assert.Equal(0.5, perSystem["NES"].Recall, 3);
        Assert.Equal(0.5, perSystem["NES"].F1, 3);

        // Wrongs include wrong + false positive => 2/5 = 0.4
        Assert.Equal(0.4, aggregate["wrongMatchRate"], 3);
        Assert.NotEmpty(matrix);
    }

    [Fact]
    public void M9_CategoryConfusion_CalculatesRates()
    {
        var samples = new List<BenchmarkSampleResult>
        {
            new("1", BenchmarkVerdict.Correct, "NES", "NES", 90, false, null, "Game", "Game"),
            new("2", BenchmarkVerdict.Wrong, "NES", "SNES", 90, false, null, "Game", "Junk"),
            new("3", BenchmarkVerdict.Correct, "SNES", "SNES", 90, false, null, "BIOS", "Game"),
            new("4", BenchmarkVerdict.Correct, "PS1", "PS1", 80, false, null, "Junk", "Game"),
        };

        var catConfusion = MetricsAggregator.CalculateCategoryConfusion(samples);

        // 3 out of 4 are off-diagonal (Game→Junk, BIOS→Game, Junk→Game)
        Assert.Equal(0.75, catConfusion["categoryConfusionRate"], 3);
        // 1 Game expected, 1 classified as Junk → gameAsJunkRate = 0.5
        Assert.Equal(0.5, catConfusion["gameAsJunkRate"], 3);
        // 1 BIOS expected, 1 classified as Game → biosAsGameRate = 1.0
        Assert.Equal(1.0, catConfusion["biosAsGameRate"], 3);
        // 1 Junk expected, 1 classified as Game → junkAsGameRate = 1.0
        Assert.Equal(1.0, catConfusion["junkAsGameRate"], 3);
    }

    [Fact]
    public void M9_CategoryConfusionMatrix_BuildsCorrectly()
    {
        var samples = new List<BenchmarkSampleResult>
        {
            new("1", BenchmarkVerdict.Correct, "NES", "NES", 90, false, null, "Game", "Game"),
            new("2", BenchmarkVerdict.Wrong, "NES", "SNES", 90, false, null, "Game", "Junk"),
            new("3", BenchmarkVerdict.Wrong, "PS1", "PS2", 80, false, null, "Game", "Junk"),
        };

        var matrix = MetricsAggregator.BuildCategoryConfusionMatrix(samples);

        Assert.Equal(2, matrix.Count); // Game→Game (1) + Game→Junk (2)
        Assert.Equal("Game", matrix[0].ExpectedCategory);
        Assert.Equal("Junk", matrix[0].ActualCategory);
        Assert.Equal(2, matrix[0].Count);
    }

    [Fact]
    public void M10_ConsoleConfusionPairs_IdentifiesSystematicConfusion()
    {
        // 10 NES samples, 3 misidentified as SNES = 30% confusion rate
        var samples = new List<BenchmarkSampleResult>();
        for (int i = 0; i < 7; i++)
            samples.Add(new($"nes-ok-{i}", BenchmarkVerdict.Correct, "NES", "NES", 90, false, null));
        for (int i = 0; i < 3; i++)
            samples.Add(new($"nes-wrong-{i}", BenchmarkVerdict.Wrong, "NES", "SNES", 85, false, null));

        var pairs = MetricsAggregator.CalculateConsoleConfusionPairs(samples, thresholdRate: 0.02);

        Assert.Single(pairs);
        Assert.Equal("NES", pairs[0].SystemA);
        Assert.Equal("SNES", pairs[0].SystemB);
        Assert.Equal(0.3, pairs[0].Rate, 3);
        Assert.Equal(3, pairs[0].Count);
    }

    [Fact]
    public void M10_MaxConsoleConfusionRate_ReturnsHighestPairRate()
    {
        var samples = new List<BenchmarkSampleResult>
        {
            new("1", BenchmarkVerdict.Correct, "NES", "NES", 90, false, null),
            new("2", BenchmarkVerdict.Wrong, "NES", "SNES", 90, false, null),
        };

        double max = MetricsAggregator.CalculateMaxConsoleConfusionRate(samples);
        Assert.Equal(0.5, max, 3); // 1 out of 2 NES → SNES
    }

    [Fact]
    public void M11_DatExactMatchRate_CountsDatVerified()
    {
        var samples = new List<BenchmarkSampleResult>
        {
            new("1", BenchmarkVerdict.Correct, "NES", "NES", 95, false, null, ActualSortDecision: SortDecision.DatVerified),
            new("2", BenchmarkVerdict.Correct, "NES", "NES", 80, false, null, ActualSortDecision: SortDecision.Sort),
            new("3", BenchmarkVerdict.Correct, "SNES", "SNES", 90, false, null, ActualSortDecision: SortDecision.DatVerified),
            new("4", BenchmarkVerdict.Missed, "PS1", "UNKNOWN", 0, false, null, ActualSortDecision: SortDecision.Blocked),
        };

        double rate = MetricsAggregator.CalculateDatExactMatchRate(samples);
        Assert.Equal(0.5, rate, 3); // 2 out of 4
    }

    [Fact]
    public void M13_AmbiguousMatchRate_CountsConflicts()
    {
        var samples = new List<BenchmarkSampleResult>
        {
            new("1", BenchmarkVerdict.Correct, "NES", "NES", 90, true, null),
            new("2", BenchmarkVerdict.Correct, "NES", "NES", 90, false, null),
            new("3", BenchmarkVerdict.Wrong, "SNES", "NES", 60, true, null),
            new("4", BenchmarkVerdict.Correct, "PS1", "PS1", 80, false, null),
        };

        double rate = MetricsAggregator.CalculateAmbiguousMatchRate(samples);
        Assert.Equal(0.5, rate, 3); // 2 out of 4
    }

    [Fact]
    public void M14_RepairSafeRate_RequiresHighConfidenceNoConflict()
    {
        var samples = new List<BenchmarkSampleResult>
        {
            new("1", BenchmarkVerdict.Correct, "NES", "NES", 98, false, null),  // repair-safe
            new("2", BenchmarkVerdict.Correct, "NES", "NES", 80, false, null),  // too low confidence
            new("3", BenchmarkVerdict.Correct, "NES", "NES", 96, true, null),   // has conflict
            new("4", BenchmarkVerdict.Wrong, "NES", "SNES", 99, false, null),   // wrong verdict
            new("5", BenchmarkVerdict.Acceptable, "GB", "GBC", 95, false, null), // acceptable + safe
        };

        double rate = MetricsAggregator.CalculateRepairSafeRate(samples);
        Assert.Equal(0.4, rate, 3); // 2 out of 5 (items 1 and 5)
    }

    [Fact]
    public void ExtendedAggregate_IncludesAllMetrics()
    {
        var samples = new List<BenchmarkSampleResult>
        {
            new("1", BenchmarkVerdict.Correct, "NES", "NES", 95, false, null, "Game", "Game", SortDecision.DatVerified),
            new("2", BenchmarkVerdict.Wrong, "NES", "SNES", 80, false, null, "Game", "Junk", SortDecision.Sort),
        };

        var extended = MetricsAggregator.CalculateExtendedAggregate(samples);

        // Basic aggregate keys
        Assert.True(extended.ContainsKey("wrongMatchRate"));
        Assert.True(extended.ContainsKey("unknownRate"));
        Assert.True(extended.ContainsKey("safeSortCoverage"));

        // Extended M9-M14 keys
        Assert.True(extended.ContainsKey("categoryConfusionRate"));
        Assert.True(extended.ContainsKey("gameAsJunkRate"));
        Assert.True(extended.ContainsKey("biosAsGameRate"));
        Assert.True(extended.ContainsKey("junkAsGameRate"));
        Assert.True(extended.ContainsKey("maxConsoleConfusionRate"));
        Assert.True(extended.ContainsKey("datExactMatchRate"));
        Assert.True(extended.ContainsKey("ambiguousMatchRate"));
        Assert.True(extended.ContainsKey("repairSafeRate"));
    }

    [Fact]
    public void EmptyResults_ReturnZeros()
    {
        var empty = Array.Empty<BenchmarkSampleResult>();

        var catConfusion = MetricsAggregator.CalculateCategoryConfusion(empty);
        Assert.Empty(catConfusion);

        Assert.Equal(0, MetricsAggregator.CalculateMaxConsoleConfusionRate(empty));
        Assert.Equal(0, MetricsAggregator.CalculateDatExactMatchRate(empty));
        Assert.Equal(0, MetricsAggregator.CalculateAmbiguousMatchRate(empty));
        Assert.Equal(0, MetricsAggregator.CalculateRepairSafeRate(empty));
    }
}
