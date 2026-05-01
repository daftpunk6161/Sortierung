using System.Text;
using System.Text.Json;

namespace Romulus.Tests.Benchmark;

internal static class BenchmarkArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static void WriteMetricsSummary(BenchmarkReport report, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var summary = new
        {
            report.Timestamp,
            report.GroundTruthVersion,
            report.TotalSamples,
            report.WrongMatchRate,
            report.UnsafeSortRate,
            Aggregate = report.AggregateMetrics
        };

        File.WriteAllText(path, JsonSerializer.Serialize(summary, JsonOptions));
    }

    public static void WriteConfusionCsv(IReadOnlyList<ConfusionEntry> confusion, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var sb = new StringBuilder();
        sb.AppendLine("ExpectedSystem,ActualSystem,Count");
        foreach (var row in confusion)
        {
            sb.AppendLine($"{Escape(row.ExpectedSystem)},{Escape(row.ActualSystem)},{row.Count}");
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static void WriteCategoryConfusionCsv(IReadOnlyList<BenchmarkSampleResult> results, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var rows = results
            .GroupBy(r => new
            {
                Expected = string.IsNullOrWhiteSpace(r.ExpectedCategory) ? "Unknown" : r.ExpectedCategory,
                Actual = string.IsNullOrWhiteSpace(r.ActualCategory) ? "Unknown" : r.ActualCategory,
            })
            .Select(g => new { g.Key.Expected, g.Key.Actual, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Expected, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Actual, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("ExpectedCategory,ActualCategory,Count");
        foreach (var row in rows)
        {
            sb.AppendLine($"{Escape(row.Expected)},{Escape(row.Actual)},{row.Count}");
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static void WriteErrorDetailsJsonl(IReadOnlyList<BenchmarkSampleResult> results, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        foreach (var result in results.Where(r => r.Verdict is BenchmarkVerdict.Wrong or BenchmarkVerdict.FalsePositive or BenchmarkVerdict.Missed or BenchmarkVerdict.Ambiguous))
        {
            var line = JsonSerializer.Serialize(new
            {
                id = result.Id,
                verdict = result.Verdict.ToString(),
                expectedConsoleKey = result.ExpectedConsoleKey,
                actualConsoleKey = result.ActualConsoleKey,
                expectedCategory = result.ExpectedCategory,
                actualCategory = result.ActualCategory,
                confidence = result.ActualConfidence,
                hasConflict = result.ActualHasConflict,
                sortDecision = result.ActualSortDecision.ToString(),
                details = result.Details,
            });

            writer.WriteLine(line);
        }
    }

    public static void WriteTrendComparison(RegressionReport regression, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var trend = new
        {
            regression.HasBaseline,
            regression.WrongMatchRateDelta,
            regression.UnsafeSortRateDelta,
            regression.UnknownToWrongMigrationRate,
            regression.PerSystemRegressions
        };

        File.WriteAllText(path, JsonSerializer.Serialize(trend, JsonOptions));
    }

    private static string Escape(string value)
    {
        var safe = value ?? string.Empty;
        if (safe.StartsWith("=", StringComparison.Ordinal) || safe.StartsWith("+", StringComparison.Ordinal) || safe.StartsWith("-", StringComparison.Ordinal) || safe.StartsWith("@", StringComparison.Ordinal))
        {
            safe = "'" + safe;
        }

        if (safe.Contains(',') || safe.Contains('"') || safe.Contains('\n') || safe.Contains('\r'))
        {
            return $"\"{safe.Replace("\"", "\"\"")}\"";
        }

        return safe;
    }
}
