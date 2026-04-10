namespace Romulus.Tests.Benchmark;

internal sealed record RegressionReport(
    bool HasBaseline,
    double WrongMatchRateDelta,
    double UnsafeSortRateDelta,
    double UnknownToWrongMigrationRate,
    IReadOnlyList<string> PerSystemRegressions,
    BenchmarkReport? BaselineReport,
    BenchmarkReport CurrentReport);

internal static class BaselineComparator
{
    public static RegressionReport Compare(BenchmarkReport currentReport, string baselinePath)
    {
        if (!File.Exists(baselinePath))
        {
            return new RegressionReport(
                HasBaseline: false,
                WrongMatchRateDelta: 0,
                UnsafeSortRateDelta: 0,
                UnknownToWrongMigrationRate: 0,
                PerSystemRegressions: [],
                BaselineReport: null,
                CurrentReport: currentReport);
        }

        var baseline = BenchmarkReportWriter.Read(baselinePath);

        var currentPerSystem = currentReport.PerSystem ?? new Dictionary<string, BenchmarkSystemSummary>(StringComparer.OrdinalIgnoreCase);
        var baselinePerSystem = baseline.PerSystem ?? new Dictionary<string, BenchmarkSystemSummary>(StringComparer.OrdinalIgnoreCase);

        var regressions = new List<string>();
        var allSystemKeys = new HashSet<string>(baselinePerSystem.Keys, StringComparer.OrdinalIgnoreCase);
        allSystemKeys.UnionWith(currentPerSystem.Keys);

        foreach (var systemKey in allSystemKeys)
        {
            baselinePerSystem.TryGetValue(systemKey, out var previous);
            currentPerSystem.TryGetValue(systemKey, out var current);

            int baselineWrong = (previous?.Wrong ?? 0) + (previous?.FalsePositive ?? 0);
            int currentWrong = (current?.Wrong ?? 0) + (current?.FalsePositive ?? 0);

            if (currentWrong > baselineWrong)
            {
                regressions.Add(systemKey);
            }
        }

        var unknownToWrongMigrationRate = CalculateUnknownToWrongMigrationRate(baseline, currentReport);

        return new RegressionReport(
            HasBaseline: true,
            WrongMatchRateDelta: currentReport.WrongMatchRate - baseline.WrongMatchRate,
            UnsafeSortRateDelta: currentReport.UnsafeSortRate - baseline.UnsafeSortRate,
            UnknownToWrongMigrationRate: unknownToWrongMigrationRate,
            PerSystemRegressions: regressions,
            BaselineReport: baseline,
            CurrentReport: currentReport);
    }

    private static double CalculateUnknownToWrongMigrationRate(BenchmarkReport baseline, BenchmarkReport current)
    {
        var baselineOutcomes = baseline.SampleOutcomes;
        var currentOutcomes = current.SampleOutcomes;
        if (baselineOutcomes is null || currentOutcomes is null)
        {
            return 0;
        }

        var baselineUnknownIds = baselineOutcomes
            .Where(kv => string.Equals(kv.Value, nameof(BenchmarkVerdict.Missed), StringComparison.OrdinalIgnoreCase)
                         || string.Equals(kv.Value, nameof(BenchmarkVerdict.TrueNegative), StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        if (baselineUnknownIds.Count == 0)
        {
            return 0;
        }

        int migrated = baselineUnknownIds.Count(id =>
            currentOutcomes.TryGetValue(id, out var outcome)
            && (string.Equals(outcome, nameof(BenchmarkVerdict.Wrong), StringComparison.OrdinalIgnoreCase)
                || string.Equals(outcome, nameof(BenchmarkVerdict.FalsePositive), StringComparison.OrdinalIgnoreCase)));

        return (double)migrated / baselineUnknownIds.Count;
    }
}
