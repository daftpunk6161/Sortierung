using Romulus.Tests.Benchmark.Infrastructure;
using Romulus.Tests.Benchmark.Models;

namespace Romulus.Tests.Benchmark;

/// <summary>
/// D5: Cross-Validation Split — provides k-fold splitting of ground-truth entries
/// for future ML-based detection evaluation and anti-overfitting analysis.
/// Uses stratified splitting by console key to maintain per-system representation.
/// </summary>
internal sealed record CrossValidationFold(
    int FoldIndex,
    IReadOnlyList<GroundTruthEntry> TrainSet,
    IReadOnlyList<GroundTruthEntry> TestSet);

internal static class CrossValidationSplitter
{
    /// <summary>
    /// Creates k stratified folds from all ground-truth entries.
    /// Stratification is by ExpectedConsoleKey to ensure each fold
    /// has proportional system representation.
    /// </summary>
    public static IReadOnlyList<CrossValidationFold> CreateFolds(
        IReadOnlyList<GroundTruthEntry> entries, int k = 5, int seed = 42)
    {
        if (k < 2) throw new ArgumentOutOfRangeException(nameof(k), "k must be >= 2");
        if (entries.Count < k) throw new ArgumentException($"Need at least {k} entries for {k}-fold CV");

        // Group by console key for stratification
        var bySystem = entries
            .GroupBy(e => e.Expected.ConsoleKey ?? "UNKNOWN", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Assign each entry to a fold (round-robin within each system group)
        var foldAssignments = new int[entries.Count];
        var entryToIndex = entries.Select((e, i) => (e.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);

        var rng = new Random(seed);
        foreach (var (_, systemEntries) in bySystem)
        {
            // Shuffle within system for randomness
            var shuffled = systemEntries.OrderBy(_ => rng.Next()).ToList();
            for (int i = 0; i < shuffled.Count; i++)
            {
                var idx = entryToIndex[shuffled[i].Id];
                foldAssignments[idx] = i % k;
            }
        }

        // Build folds
        var folds = new List<CrossValidationFold>(k);
        for (int fold = 0; fold < k; fold++)
        {
            var testSet = new List<GroundTruthEntry>();
            var trainSet = new List<GroundTruthEntry>();

            for (int i = 0; i < entries.Count; i++)
            {
                if (foldAssignments[i] == fold)
                    testSet.Add(entries[i]);
                else
                    trainSet.Add(entries[i]);
            }

            folds.Add(new CrossValidationFold(fold, trainSet, testSet));
        }

        return folds;
    }

    /// <summary>
    /// Loads all non-holdout entries and creates k-fold splits.
    /// </summary>
    public static IReadOnlyList<CrossValidationFold> CreateFoldsFromGroundTruth(int k = 5, int seed = 42)
    {
        var setFiles = new[]
        {
            "golden-core.jsonl",
            "golden-realworld.jsonl",
            "edge-cases.jsonl",
            "negative-controls.jsonl",
            "chaos-mixed.jsonl",
            "dat-coverage.jsonl",
            "repair-safety.jsonl",
        };

        var entries = new List<GroundTruthEntry>();
        foreach (var set in setFiles)
        {
            entries.AddRange(GroundTruthLoader.LoadSet(set));
        }

        return CreateFolds(entries, k, seed);
    }
}
