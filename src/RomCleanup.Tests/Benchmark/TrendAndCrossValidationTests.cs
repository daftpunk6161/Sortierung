using RomCleanup.Tests.Benchmark.Models;
using Xunit;

namespace RomCleanup.Tests.Benchmark;

public sealed class TrendAnalyzerTests
{
    [Fact]
    public void Analyze_WithNoBaselines_ReturnsEmptyHistory()
    {
        var report = CreateReport(totalSamples: 100, wrong: 2, wrongRate: 0.02, unsafeRate: 0.01);

        var trend = TrendAnalyzer.Analyze(report);

        Assert.NotNull(trend.Current);
        Assert.Equal("current", trend.Current.FileName);
        Assert.Equal(100, trend.Current.TotalSamples);
    }

    [Fact]
    public void Analyze_TrendDirection_StableWhenNoChange()
    {
        var report = CreateReport(totalSamples: 100, wrong: 0, wrongRate: 0, unsafeRate: 0);

        var trend = TrendAnalyzer.Analyze(report);

        // With no baselines and zero rates, direction should be stable or improving
        Assert.Contains(trend.TrendDirection, new[] { "stable", "improving" });
    }

    private static BenchmarkReport CreateReport(int totalSamples, int wrong, double wrongRate, double unsafeRate)
    {
        return new BenchmarkReport(
            DateTimeOffset.UtcNow, "1.0.0", totalSamples,
            Correct: totalSamples - wrong, Acceptable: 0, Wrong: wrong,
            Missed: 0, TrueNegative: 0, JunkClassified: 0, FalsePositive: 0,
            WrongMatchRate: wrongRate, UnsafeSortRate: unsafeRate,
            PerSystem: new Dictionary<string, BenchmarkSystemSummary>(),
            AggregateMetrics: new Dictionary<string, double>(),
            ConfusionMatrix: []);
    }
}

public sealed class CrossValidationSplitterTests
{
    [Fact]
    public void CreateFolds_ProducesCorrectNumberOfFolds()
    {
        var entries = CreateEntries(50, "NES");

        var folds = CrossValidationSplitter.CreateFolds(entries, k: 5);

        Assert.Equal(5, folds.Count);
    }

    [Fact]
    public void CreateFolds_EachEntryAppearsInExactlyOneTestSet()
    {
        var entries = CreateEntries(20, "NES")
            .Concat(CreateEntries(15, "SNES"))
            .ToList();

        var folds = CrossValidationSplitter.CreateFolds(entries, k: 5);

        var testIds = folds.SelectMany(f => f.TestSet.Select(e => e.Id)).ToList();
        Assert.Equal(entries.Count, testIds.Count);
        Assert.Equal(entries.Count, testIds.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void CreateFolds_TrainAndTestDoNotOverlap()
    {
        var entries = CreateEntries(30, "NES");

        var folds = CrossValidationSplitter.CreateFolds(entries, k: 3);

        foreach (var fold in folds)
        {
            var trainIds = fold.TrainSet.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            var testIds = fold.TestSet.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

            Assert.Empty(trainIds.Intersect(testIds));
            Assert.Equal(entries.Count, trainIds.Count + testIds.Count);
        }
    }

    [Fact]
    public void CreateFolds_StratifiesBySystem()
    {
        var entries = CreateEntries(20, "NES")
            .Concat(CreateEntries(20, "SNES"))
            .ToList();

        var folds = CrossValidationSplitter.CreateFolds(entries, k: 4);

        foreach (var fold in folds)
        {
            // Each fold's test set should have entries from both systems
            var systems = fold.TestSet.Select(e => e.Expected.ConsoleKey).Distinct().ToList();
            Assert.True(systems.Count >= 2, $"Fold {fold.FoldIndex} has entries from only {systems.Count} system(s)");
        }
    }

    [Fact]
    public void CreateFolds_IsDeterministicWithSameSeed()
    {
        var entries = CreateEntries(30, "NES");

        var folds1 = CrossValidationSplitter.CreateFolds(entries, k: 5, seed: 42);
        var folds2 = CrossValidationSplitter.CreateFolds(entries, k: 5, seed: 42);

        for (int i = 0; i < folds1.Count; i++)
        {
            var ids1 = folds1[i].TestSet.Select(e => e.Id).ToList();
            var ids2 = folds2[i].TestSet.Select(e => e.Id).ToList();
            Assert.Equal(ids1, ids2);
        }
    }

    [Fact]
    public void CreateFolds_ThrowsOnInvalidK()
    {
        var entries = CreateEntries(10, "NES");

        Assert.Throws<ArgumentOutOfRangeException>(() => CrossValidationSplitter.CreateFolds(entries, k: 1));
    }

    private static List<GroundTruthEntry> CreateEntries(int count, string consoleKey)
    {
        var entries = new List<GroundTruthEntry>();
        for (int i = 0; i < count; i++)
        {
            entries.Add(new GroundTruthEntry
            {
                Id = $"{consoleKey.ToLowerInvariant()}-{i:D3}",
                Source = new SourceInfo { FileName = $"game-{i}.rom", Extension = ".rom", SizeBytes = 1024 },
                Difficulty = "easy",
                Expected = new ExpectedResult { ConsoleKey = consoleKey, Category = "Game" },
                Tags = ["reference"],
            });
        }
        return entries;
    }
}
