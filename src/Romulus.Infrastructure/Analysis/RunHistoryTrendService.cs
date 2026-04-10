using System.Text;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Analysis;

/// <summary>
/// Builds trend views from persisted run snapshots instead of ad-hoc sidecar history files.
/// </summary>
public static class RunHistoryTrendService
{
    public static async Task<IReadOnlyList<TrendSnapshot>> LoadTrendHistoryAsync(
        ICollectionIndex? collectionIndex,
        int limit = 365,
        CancellationToken ct = default)
    {
        if (collectionIndex is null)
            return Array.Empty<TrendSnapshot>();

        var boundedLimit = Math.Clamp(limit, 1, 3650);
        var snapshots = await collectionIndex.ListRunSnapshotsAsync(boundedLimit, ct).ConfigureAwait(false);
        if (snapshots.Count == 0)
            return Array.Empty<TrendSnapshot>();

        return snapshots
            .OrderBy(static snapshot => snapshot.CompletedUtc)
            .ThenBy(static snapshot => snapshot.RunId, StringComparer.Ordinal)
            .Select(static snapshot => new TrendSnapshot(
                snapshot.CompletedUtc,
                snapshot.TotalFiles,
                snapshot.CollectionSizeBytes,
                snapshot.DatMatches,
                snapshot.Dupes,
                snapshot.Junk,
                snapshot.HealthScore))
            .ToArray();
    }

    public static string FormatTrendReport(
        IReadOnlyList<TrendSnapshot> history,
        string title,
        string emptyMessage,
        string currentLabel,
        string deltaFilesLabel,
        string deltaDuplicatesLabel,
        string historyLabel,
        string filesLabel,
        string qualityLabel)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (history.Count == 0)
            return emptyMessage;

        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine(new string('=', 50));
        var latest = history[^1];
        sb.AppendLine($"{currentLabel}: {latest.TotalFiles} {filesLabel}, {Formatting.FormatSize(latest.SizeBytes)}, {qualityLabel}={latest.QualityScore}%");

        if (history.Count >= 2)
        {
            var prev = history[^2];
            var fileDelta = latest.TotalFiles - prev.TotalFiles;
            var dupeDelta = latest.Dupes - prev.Dupes;
            sb.AppendLine($"{deltaFilesLabel}: {fileDelta:+#;-#;0}, {deltaDuplicatesLabel}: {dupeDelta:+#;-#;0}");
        }

        sb.AppendLine();
        sb.AppendLine(historyLabel);
        foreach (var snapshot in history.TakeLast(10))
            sb.AppendLine($"  {snapshot.Timestamp:yyyy-MM-dd HH:mm} | {snapshot.TotalFiles} {filesLabel} | {qualityLabel}={snapshot.QualityScore}%");
        return sb.ToString();
    }
}
