using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Paths;

namespace Romulus.Infrastructure.Index;

/// <summary>
/// Persists run history snapshots using the shared run projection.
/// This avoids separate KPI or status derivation paths in entry points.
/// </summary>
public static class CollectionRunSnapshotWriter
{
    public static async Task TryPersistAsync(
        ICollectionIndex collectionIndex,
        RunOptions options,
        RunResult result,
        DateTime startedUtc,
        DateTime completedUtc,
        Action<string>? onWarning = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(collectionIndex);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            var projection = RunProjectionFactory.Create(result);
            var snapshot = CreateSnapshot(options, result, projection, startedUtc, completedUtc);
            await collectionIndex.AppendRunSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            onWarning?.Invoke($"[CollectionIndex] Run snapshot persist failed: {ex.Message}");
        }
    }

    internal static CollectionRunSnapshot CreateSnapshot(
        RunOptions options,
        RunResult result,
        RunProjection projection,
        DateTime startedUtc,
        DateTime completedUtc)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(projection);

        var normalizedRoots = options.Roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(ArtifactPathResolver.NormalizeRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rootFingerprint = ArtifactPathResolver.ComputeRootsFingerprint(normalizedRoots);

        return new CollectionRunSnapshot
        {
            RunId = ResolveRunId(result, options, rootFingerprint, completedUtc),
            StartedUtc = ToUtc(startedUtc),
            CompletedUtc = ToUtc(completedUtc),
            Mode = options.Mode,
            Status = result.Status,
            Roots = normalizedRoots,
            RootFingerprint = rootFingerprint,
            DurationMs = result.DurationMs,
            TotalFiles = projection.TotalFiles,
            CollectionSizeBytes = result.AllCandidates.Sum(static candidate => candidate.SizeBytes),
            Games = projection.Games,
            Dupes = projection.Dupes,
            Junk = projection.Junk,
            DatMatches = projection.DatMatches,
            ConvertedCount = projection.ConvertedCount,
            FailCount = projection.FailCount,
            SavedBytes = projection.SavedBytes,
            ConvertSavedBytes = projection.ConvertSavedBytes,
            HealthScore = projection.HealthScore
        };
    }

    private static string ResolveRunId(RunResult result, RunOptions options, string rootFingerprint, DateTime completedUtc)
    {
        var runId = TryExtractRunId(options.AuditPath, "audit-")
            ?? TryExtractRunId(result.ReportPath, "report-")
            ?? TryExtractRunId(options.ReportPath, "report-");

        if (!string.IsNullOrWhiteSpace(runId))
            return runId;

        return $"{ToUtc(completedUtc):yyyyMMddHHmmssfff}-{rootFingerprint}";
    }

    private static string? TryExtractRunId(string? path, string prefix)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var fileName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var candidate = fileName[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    private static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
