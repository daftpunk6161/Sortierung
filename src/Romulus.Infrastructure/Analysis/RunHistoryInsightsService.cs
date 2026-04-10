using System.Text;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Analysis;

public static class RunHistoryInsightsService
{
    public static async Task<RunSnapshotComparison?> CompareAsync(
        ICollectionIndex? collectionIndex,
        string runId,
        string compareToRunId,
        CancellationToken ct = default)
    {
        if (collectionIndex is null || string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(compareToRunId))
            return null;

        var snapshots = await collectionIndex.ListRunSnapshotsAsync(3650, ct).ConfigureAwait(false);
        var current = snapshots.FirstOrDefault(snapshot => string.Equals(snapshot.RunId, runId, StringComparison.OrdinalIgnoreCase));
        var previous = snapshots.FirstOrDefault(snapshot => string.Equals(snapshot.RunId, compareToRunId, StringComparison.OrdinalIgnoreCase));
        if (current is null || previous is null)
            return null;

        return new RunSnapshotComparison(
            current.RunId,
            previous.RunId,
            current.CompletedUtc,
            previous.CompletedUtc,
            current.Status,
            previous.Status,
            BuildDelta(current.TotalFiles, previous.TotalFiles),
            BuildDelta(current.CollectionSizeBytes, previous.CollectionSizeBytes),
            BuildDelta(current.Games, previous.Games),
            BuildDelta(current.Dupes, previous.Dupes),
            BuildDelta(current.Junk, previous.Junk),
            BuildDelta(current.DatMatches, previous.DatMatches),
            BuildDelta(current.ConvertedCount, previous.ConvertedCount),
            BuildDelta(current.FailCount, previous.FailCount),
            BuildDelta(current.SavedBytes, previous.SavedBytes),
            BuildDelta(current.ConvertSavedBytes, previous.ConvertSavedBytes),
            BuildDelta(current.HealthScore, previous.HealthScore));
    }

    public static async Task<StorageInsightReport> BuildStorageInsightsAsync(
        ICollectionIndex? collectionIndex,
        int limit = 30,
        CancellationToken ct = default)
    {
        if (collectionIndex is null)
            return EmptyReport();

        var boundedLimit = Math.Clamp(limit, 1, 3650);
        var snapshots = await collectionIndex.ListRunSnapshotsAsync(boundedLimit, ct).ConfigureAwait(false);
        if (snapshots.Count == 0)
            return EmptyReport();

        var ordered = snapshots
            .OrderBy(static snapshot => snapshot.CompletedUtc)
            .ThenBy(static snapshot => snapshot.RunId, StringComparer.Ordinal)
            .ToArray();

        var latest = ordered[^1];
        var previous = ordered.Length >= 2 ? ordered[^2] : latest;
        var cumulativeSaved = ordered.Sum(static snapshot => snapshot.SavedBytes);
        var cumulativeConvertSaved = ordered.Sum(static snapshot => snapshot.ConvertSavedBytes);
        var averageGrowth = ordered.Length <= 1
            ? 0
            : ordered.Zip(ordered.Skip(1), static (left, right) => right.CollectionSizeBytes - left.CollectionSizeBytes).Average();

        return new StorageInsightReport(
            ordered.Length,
            latest.CompletedUtc,
            BuildDelta(latest.TotalFiles, previous.TotalFiles),
            BuildDelta(latest.CollectionSizeBytes, previous.CollectionSizeBytes),
            BuildDelta(latest.HealthScore, previous.HealthScore),
            latest.SavedBytes,
            latest.ConvertSavedBytes,
            cumulativeSaved,
            cumulativeConvertSaved,
            averageGrowth);
    }

    public static string FormatComparisonReport(RunSnapshotComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        var sb = new StringBuilder();
        sb.AppendLine("Run Comparison");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine($"Current:   {comparison.RunId} ({comparison.CompletedUtc:yyyy-MM-dd HH:mm}, {comparison.Status})");
        sb.AppendLine($"Previous:  {comparison.CompareToRunId} ({comparison.CompareToCompletedUtc:yyyy-MM-dd HH:mm}, {comparison.CompareToStatus})");
        sb.AppendLine();
        AppendDelta(sb, "Files", comparison.TotalFiles);
        AppendDelta(sb, "CollectionSizeBytes", comparison.CollectionSizeBytes);
        AppendDelta(sb, "Games", comparison.Games);
        AppendDelta(sb, "Dupes", comparison.Dupes);
        AppendDelta(sb, "Junk", comparison.Junk);
        AppendDelta(sb, "DatMatches", comparison.DatMatches);
        AppendDelta(sb, "ConvertedCount", comparison.ConvertedCount);
        AppendDelta(sb, "FailCount", comparison.FailCount);
        AppendDelta(sb, "SavedBytes", comparison.SavedBytes);
        AppendDelta(sb, "ConvertSavedBytes", comparison.ConvertSavedBytes);
        AppendDelta(sb, "HealthScore", comparison.HealthScore);
        return sb.ToString();
    }

    public static string FormatStorageInsightReport(StorageInsightReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.SampleCount == 0)
            return "No persisted run snapshots available.";

        var sb = new StringBuilder();
        sb.AppendLine("Storage Insights");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine($"Samples: {report.SampleCount}");
        sb.AppendLine($"Latest completed UTC: {report.LatestCompletedUtc:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Files: {report.TotalFiles.Current} ({report.TotalFiles.Delta:+#;-#;0})");
        sb.AppendLine($"Collection size: {Formatting.FormatSize(report.CollectionSizeBytes.Current)} ({Formatting.FormatSize(report.CollectionSizeBytes.Delta)})");
        sb.AppendLine($"Health score: {report.HealthScore.Current}% ({report.HealthScore.Delta:+#;-#;0})");
        sb.AppendLine($"Latest dedupe saved: {Formatting.FormatSize(report.LatestSavedBytes)}");
        sb.AppendLine($"Latest conversion saved: {Formatting.FormatSize(report.LatestConvertSavedBytes)}");
        sb.AppendLine($"Cumulative dedupe saved: {Formatting.FormatSize(report.CumulativeSavedBytes)}");
        sb.AppendLine($"Cumulative conversion saved: {Formatting.FormatSize(report.CumulativeConvertSavedBytes)}");
        sb.AppendLine($"Average run growth: {Formatting.FormatSize((long)Math.Round(report.AverageRunGrowthBytes))}");
        return sb.ToString();
    }

    private static StorageInsightReport EmptyReport()
        => new(0, null, new(0, 0, 0), new(0, 0, 0), new(0, 0, 0), 0, 0, 0, 0, 0);

    private static RunSnapshotMetricDelta BuildDelta(long current, long previous)
        => new(current, previous, current - previous);

    private static void AppendDelta(StringBuilder builder, string label, RunSnapshotMetricDelta delta)
    {
        builder.Append(label)
            .Append(": ")
            .Append(delta.Current)
            .Append(" (")
            .Append(delta.Delta >= 0 ? "+" : string.Empty)
            .Append(delta.Delta)
            .AppendLine(")");
    }
}
