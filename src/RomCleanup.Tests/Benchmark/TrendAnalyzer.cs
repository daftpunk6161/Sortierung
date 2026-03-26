using System.Text.Json;
using RomCleanup.Tests.Benchmark.Infrastructure;

namespace RomCleanup.Tests.Benchmark;

/// <summary>
/// D4: Trend Dashboard — compares current benchmark results against all historical baselines
/// in the baselines/archive/ directory to track quality over time.
/// </summary>
internal sealed record TrendSnapshot(
    string FileName,
    DateTimeOffset Timestamp,
    int TotalSamples,
    double WrongMatchRate,
    double UnsafeSortRate,
    int Wrong,
    int FalsePositive);

internal sealed record TrendReport(
    IReadOnlyList<TrendSnapshot> History,
    TrendSnapshot? Current,
    double WrongMatchRateTrend,
    double UnsafeSortRateTrend,
    string TrendDirection);

internal static class TrendAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static TrendReport Analyze(BenchmarkReport currentReport)
    {
        var archiveDir = Path.Combine(BenchmarkPaths.BaselinesDir, "archive");
        var snapshots = new List<TrendSnapshot>();

        // Load historical baselines
        if (Directory.Exists(archiveDir))
        {
            foreach (var file in Directory.GetFiles(archiveDir, "baseline-*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var snapshot = LoadSnapshot(file);
                if (snapshot is not null)
                    snapshots.Add(snapshot);
            }
        }

        // Also include latest-baseline if it exists
        if (File.Exists(BenchmarkPaths.LatestBaselinePath))
        {
            var latest = LoadSnapshot(BenchmarkPaths.LatestBaselinePath);
            if (latest is not null && !snapshots.Any(s => s.FileName == latest.FileName))
                snapshots.Add(latest);
        }

        var current = new TrendSnapshot(
            "current",
            currentReport.Timestamp,
            currentReport.TotalSamples,
            currentReport.WrongMatchRate,
            currentReport.UnsafeSortRate,
            currentReport.Wrong,
            currentReport.FalsePositive);

        // Calculate trend direction from last 3 data points
        var recentWithCurrent = snapshots.TakeLast(2).Append(current).ToList();
        double wrongTrend = CalculateLinearTrend(recentWithCurrent.Select(s => s.WrongMatchRate).ToList());
        double unsafeTrend = CalculateLinearTrend(recentWithCurrent.Select(s => s.UnsafeSortRate).ToList());

        string direction = (wrongTrend <= 0 && unsafeTrend <= 0) ? "improving"
            : (wrongTrend > 0.001 || unsafeTrend > 0.001) ? "degrading"
            : "stable";

        return new TrendReport(snapshots, current, wrongTrend, unsafeTrend, direction);
    }

    public static void WriteTrendReport(TrendReport trend, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(trend, JsonOptions));
    }

    private static TrendSnapshot? LoadSnapshot(string filePath)
    {
        try
        {
            var report = BenchmarkReportWriter.Read(filePath);
            return new TrendSnapshot(
                Path.GetFileName(filePath),
                report.Timestamp,
                report.TotalSamples,
                report.WrongMatchRate,
                report.UnsafeSortRate,
                report.Wrong,
                report.FalsePositive);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Simple linear trend: positive = degrading, negative = improving.
    /// Uses least-squares slope over the data points.
    /// </summary>
    private static double CalculateLinearTrend(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;

        double n = values.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (int i = 0; i < values.Count; i++)
        {
            sumX += i;
            sumY += values[i];
            sumXY += i * values[i];
            sumXX += i * i;
        }

        double denominator = n * sumXX - sumX * sumX;
        if (Math.Abs(denominator) < 1e-10) return 0;

        return (n * sumXY - sumX * sumY) / denominator;
    }
}
