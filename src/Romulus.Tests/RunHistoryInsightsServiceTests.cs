using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Xunit;

namespace Romulus.Tests;

public sealed class RunHistoryInsightsServiceTests
{
    [Fact]
    public async Task CompareAsync_NullIndex_ReturnsNull()
    {
        var result = await RunHistoryInsightsService.CompareAsync(null, "run-a", "run-b");

        Assert.Null(result);
    }

    [Fact]
    public async Task CompareAsync_EmptyRunIds_ReturnsNull()
    {
        var index = new SnapshotOnlyCollectionIndex(Array.Empty<CollectionRunSnapshot>());

        Assert.Null(await RunHistoryInsightsService.CompareAsync(index, string.Empty, "run-b"));
        Assert.Null(await RunHistoryInsightsService.CompareAsync(index, "run-a", " "));
    }

    [Fact]
    public async Task CompareAsync_MissingSnapshot_ReturnsNull()
    {
        var index = new SnapshotOnlyCollectionIndex(
        [
            Snapshot("run-a", completedUtc: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc), totalFiles: 10)
        ]);

        var result = await RunHistoryInsightsService.CompareAsync(index, "run-a", "run-b");

        Assert.Null(result);
    }

    [Fact]
    public async Task CompareAsync_ValidSnapshots_ComputesAllDeltas()
    {
        var current = Snapshot(
            "run-current",
            completedUtc: new DateTime(2026, 4, 2, 10, 0, 0, DateTimeKind.Utc),
            status: "completed",
            totalFiles: 120,
            collectionSizeBytes: 10_000,
            games: 80,
            dupes: 20,
            junk: 5,
            datMatches: 70,
            convertedCount: 15,
            failCount: 2,
            savedBytes: 2_000,
            convertSavedBytes: 500,
            healthScore: 88);

        var previous = Snapshot(
            "run-previous",
            completedUtc: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            status: "ok",
            totalFiles: 100,
            collectionSizeBytes: 8_000,
            games: 70,
            dupes: 15,
            junk: 6,
            datMatches: 60,
            convertedCount: 10,
            failCount: 1,
            savedBytes: 1_500,
            convertSavedBytes: 300,
            healthScore: 80);

        var index = new SnapshotOnlyCollectionIndex([current, previous]);

        var result = await RunHistoryInsightsService.CompareAsync(index, "run-current", "run-previous");

        Assert.NotNull(result);
        Assert.Equal("run-current", result!.RunId);
        Assert.Equal("run-previous", result.CompareToRunId);
        Assert.Equal("completed", result.Status);
        Assert.Equal("ok", result.CompareToStatus);

        Assert.Equal((120L, 100L, 20L), (result.TotalFiles.Current, result.TotalFiles.Previous, result.TotalFiles.Delta));
        Assert.Equal((10_000L, 8_000L, 2_000L), (result.CollectionSizeBytes.Current, result.CollectionSizeBytes.Previous, result.CollectionSizeBytes.Delta));
        Assert.Equal((80L, 70L, 10L), (result.Games.Current, result.Games.Previous, result.Games.Delta));
        Assert.Equal((20L, 15L, 5L), (result.Dupes.Current, result.Dupes.Previous, result.Dupes.Delta));
        Assert.Equal((5L, 6L, -1L), (result.Junk.Current, result.Junk.Previous, result.Junk.Delta));
        Assert.Equal((70L, 60L, 10L), (result.DatMatches.Current, result.DatMatches.Previous, result.DatMatches.Delta));
        Assert.Equal((15L, 10L, 5L), (result.ConvertedCount.Current, result.ConvertedCount.Previous, result.ConvertedCount.Delta));
        Assert.Equal((2L, 1L, 1L), (result.FailCount.Current, result.FailCount.Previous, result.FailCount.Delta));
        Assert.Equal((2_000L, 1_500L, 500L), (result.SavedBytes.Current, result.SavedBytes.Previous, result.SavedBytes.Delta));
        Assert.Equal((500L, 300L, 200L), (result.ConvertSavedBytes.Current, result.ConvertSavedBytes.Previous, result.ConvertSavedBytes.Delta));
        Assert.Equal((88L, 80L, 8L), (result.HealthScore.Current, result.HealthScore.Previous, result.HealthScore.Delta));
    }

    [Fact]
    public async Task BuildStorageInsightsAsync_NullIndex_ReturnsEmptyReport()
    {
        var report = await RunHistoryInsightsService.BuildStorageInsightsAsync(null);

        Assert.Equal(0, report.SampleCount);
        Assert.Null(report.LatestCompletedUtc);
        Assert.Equal(0, report.CumulativeSavedBytes);
        Assert.Equal(0, report.CumulativeConvertSavedBytes);
        Assert.Equal(0, report.AverageRunGrowthBytes);
    }

    [Fact]
    public async Task BuildStorageInsightsAsync_EmptySnapshots_ReturnsEmptyReport()
    {
        var index = new SnapshotOnlyCollectionIndex(Array.Empty<CollectionRunSnapshot>());

        var report = await RunHistoryInsightsService.BuildStorageInsightsAsync(index, limit: 25);

        Assert.Equal(0, report.SampleCount);
        Assert.Null(report.LatestCompletedUtc);
    }

    [Fact]
    public async Task BuildStorageInsightsAsync_ClampsLimitToOneAnd3650()
    {
        var snapshots = Enumerable.Range(0, 10)
            .Select(i => Snapshot($"run-{i}", completedUtc: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i), totalFiles: 100 + i))
            .ToArray();

        var index = new SnapshotOnlyCollectionIndex(snapshots);

        _ = await RunHistoryInsightsService.BuildStorageInsightsAsync(index, limit: 0);
        Assert.Equal(1, index.LastRequestedLimit);

        _ = await RunHistoryInsightsService.BuildStorageInsightsAsync(index, limit: 5000);
        Assert.Equal(3650, index.LastRequestedLimit);
    }

    [Fact]
    public async Task BuildStorageInsightsAsync_SingleSnapshot_UsesZeroGrowthAndSelfDelta()
    {
        var snapshot = Snapshot(
            "run-single",
            completedUtc: new DateTime(2026, 4, 5, 10, 0, 0, DateTimeKind.Utc),
            totalFiles: 42,
            collectionSizeBytes: 123_456,
            healthScore: 91,
            savedBytes: 7_000,
            convertSavedBytes: 300);

        var index = new SnapshotOnlyCollectionIndex([snapshot]);

        var report = await RunHistoryInsightsService.BuildStorageInsightsAsync(index);

        Assert.Equal(1, report.SampleCount);
        Assert.Equal(snapshot.CompletedUtc, report.LatestCompletedUtc);
        Assert.Equal(0, report.TotalFiles.Delta);
        Assert.Equal(0, report.CollectionSizeBytes.Delta);
        Assert.Equal(0, report.HealthScore.Delta);
        Assert.Equal(7_000, report.CumulativeSavedBytes);
        Assert.Equal(300, report.CumulativeConvertSavedBytes);
        Assert.Equal(0, report.AverageRunGrowthBytes);
    }

    [Fact]
    public async Task BuildStorageInsightsAsync_MultipleSnapshots_SortsByCompletedUtcAndComputesAggregates()
    {
        var runA = Snapshot(
            "run-a",
            completedUtc: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            totalFiles: 10,
            collectionSizeBytes: 1_000,
            healthScore: 70,
            savedBytes: 100,
            convertSavedBytes: 10);

        var runB = Snapshot(
            "run-b",
            completedUtc: new DateTime(2026, 4, 2, 10, 0, 0, DateTimeKind.Utc),
            totalFiles: 12,
            collectionSizeBytes: 1_300,
            healthScore: 80,
            savedBytes: 200,
            convertSavedBytes: 20);

        var runC = Snapshot(
            "run-c",
            completedUtc: new DateTime(2026, 4, 3, 10, 0, 0, DateTimeKind.Utc),
            totalFiles: 15,
            collectionSizeBytes: 1_900,
            healthScore: 90,
            savedBytes: 300,
            convertSavedBytes: 30);

        var index = new SnapshotOnlyCollectionIndex([runB, runC, runA]);

        var report = await RunHistoryInsightsService.BuildStorageInsightsAsync(index, limit: 3);

        Assert.Equal(3, report.SampleCount);
        Assert.Equal(runC.CompletedUtc, report.LatestCompletedUtc);
        Assert.Equal((15L, 12L, 3L), (report.TotalFiles.Current, report.TotalFiles.Previous, report.TotalFiles.Delta));
        Assert.Equal((1_900L, 1_300L, 600L), (report.CollectionSizeBytes.Current, report.CollectionSizeBytes.Previous, report.CollectionSizeBytes.Delta));
        Assert.Equal((90L, 80L, 10L), (report.HealthScore.Current, report.HealthScore.Previous, report.HealthScore.Delta));
        Assert.Equal(300, report.LatestSavedBytes);
        Assert.Equal(30, report.LatestConvertSavedBytes);
        Assert.Equal(600, report.CumulativeSavedBytes);
        Assert.Equal(60, report.CumulativeConvertSavedBytes);
        Assert.Equal(450d, report.AverageRunGrowthBytes);
    }

    [Fact]
    public void FormatComparisonReport_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RunHistoryInsightsService.FormatComparisonReport(null!));
    }

    [Fact]
    public void FormatComparisonReport_ContainsHeaderAndDeltaLines()
    {
        var comparison = new RunSnapshotComparison(
            "run-new",
            "run-old",
            new DateTime(2026, 4, 6, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 5, 10, 0, 0, DateTimeKind.Utc),
            "completed",
            "ok",
            new RunSnapshotMetricDelta(10, 8, 2),
            new RunSnapshotMetricDelta(2000, 1500, 500),
            new RunSnapshotMetricDelta(7, 6, 1),
            new RunSnapshotMetricDelta(2, 1, 1),
            new RunSnapshotMetricDelta(1, 2, -1),
            new RunSnapshotMetricDelta(6, 5, 1),
            new RunSnapshotMetricDelta(3, 2, 1),
            new RunSnapshotMetricDelta(0, 1, -1),
            new RunSnapshotMetricDelta(100, 50, 50),
            new RunSnapshotMetricDelta(40, 10, 30),
            new RunSnapshotMetricDelta(95, 90, 5));

        var text = RunHistoryInsightsService.FormatComparisonReport(comparison);

        Assert.Contains("Run Comparison", text);
        Assert.Contains("Current:", text);
        Assert.Contains("Previous:", text);
        Assert.Contains("Files: 10 (+2)", text);
        Assert.Contains("Junk: 1 (-1)", text);
        Assert.Contains("HealthScore: 95 (+5)", text);
    }

    [Fact]
    public void FormatStorageInsightReport_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RunHistoryInsightsService.FormatStorageInsightReport(null!));
    }

    [Fact]
    public void FormatStorageInsightReport_EmptyReport_ReturnsNoSnapshotsMessage()
    {
        var report = new StorageInsightReport(
            0,
            null,
            new RunSnapshotMetricDelta(0, 0, 0),
            new RunSnapshotMetricDelta(0, 0, 0),
            new RunSnapshotMetricDelta(0, 0, 0),
            0,
            0,
            0,
            0,
            0);

        var text = RunHistoryInsightsService.FormatStorageInsightReport(report);

        Assert.Equal("No persisted run snapshots available.", text);
    }

    [Fact]
    public void FormatStorageInsightReport_PopulatedReport_ContainsKeyFields()
    {
        var report = new StorageInsightReport(
            4,
            new DateTime(2026, 4, 6, 10, 0, 0, DateTimeKind.Utc),
            new RunSnapshotMetricDelta(120, 100, 20),
            new RunSnapshotMetricDelta(2_048_000, 1_024_000, 1_024_000),
            new RunSnapshotMetricDelta(87, 82, 5),
            10_000,
            2_000,
            100_000,
            20_000,
            256.4);

        var text = RunHistoryInsightsService.FormatStorageInsightReport(report);

        Assert.Contains("Storage Insights", text);
        Assert.Contains("Samples: 4", text);
        Assert.Contains("Files: 120 (+20)", text);
        Assert.Contains("Health score: 87% (+5)", text);
        Assert.Contains("Latest dedupe saved:", text);
        Assert.Contains("Cumulative conversion saved:", text);
        Assert.Contains("Average run growth:", text);
    }

    private static CollectionRunSnapshot Snapshot(
        string runId,
        DateTime completedUtc,
        string status = "ok",
        int totalFiles = 0,
        long collectionSizeBytes = 0,
        int games = 0,
        int dupes = 0,
        int junk = 0,
        int datMatches = 0,
        int convertedCount = 0,
        int failCount = 0,
        long savedBytes = 0,
        long convertSavedBytes = 0,
        int healthScore = 0)
    {
        return new CollectionRunSnapshot
        {
            RunId = runId,
            StartedUtc = completedUtc.AddMinutes(-3),
            CompletedUtc = completedUtc,
            Status = status,
            Mode = "DryRun",
            TotalFiles = totalFiles,
            CollectionSizeBytes = collectionSizeBytes,
            Games = games,
            Dupes = dupes,
            Junk = junk,
            DatMatches = datMatches,
            ConvertedCount = convertedCount,
            FailCount = failCount,
            SavedBytes = savedBytes,
            ConvertSavedBytes = convertSavedBytes,
            HealthScore = healthScore
        };
    }

    private sealed class SnapshotOnlyCollectionIndex : ICollectionIndex
    {
        private readonly IReadOnlyList<CollectionRunSnapshot> _snapshots;

        public SnapshotOnlyCollectionIndex(IReadOnlyList<CollectionRunSnapshot> snapshots)
        {
            _snapshots = snapshots;
        }

        public int LastRequestedLimit { get; private set; }

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new CollectionIndexMetadata());

        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(0);

        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => ValueTask.FromResult<CollectionIndexEntry?>(null);

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(Array.Empty<CollectionIndexEntry>());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(Array.Empty<CollectionIndexEntry>());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(IReadOnlyList<string> roots, IReadOnlyCollection<string> extensions, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(Array.Empty<CollectionIndexEntry>());

        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc, CancellationToken ct = default)
            => ValueTask.FromResult<CollectionHashCacheEntry?>(null);

        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_snapshots.Count);

        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default)
        {
            LastRequestedLimit = limit;
            return ValueTask.FromResult<IReadOnlyList<CollectionRunSnapshot>>(_snapshots.Take(limit).ToArray());
        }
    }
}
