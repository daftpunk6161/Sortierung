using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for RunHistoryTrendService (0% line coverage).
/// Pure logic: snapshot-to-trend mapping and trend report formatting.
/// </summary>
public sealed class RunHistoryTrendServiceTests
{
    // ═══ LoadTrendHistoryAsync ══════════════════════════════════════

    [Fact]
    public async Task LoadTrendHistory_NullIndex_ReturnsEmpty()
    {
        var result = await RunHistoryTrendService.LoadTrendHistoryAsync(null);
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadTrendHistory_EmptySnapshots_ReturnsEmpty()
    {
        var index = new StubIndex([]);
        var result = await RunHistoryTrendService.LoadTrendHistoryAsync(index);
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadTrendHistory_SingleSnapshot_MapsTrendSnapshot()
    {
        var snapshot = CreateSnapshot("run1", new DateTime(2025, 1, 1), totalFiles: 100, sizeBytes: 5000, datMatches: 10, dupes: 5, junk: 3, healthScore: 82);
        var index = new StubIndex([snapshot]);

        var result = await RunHistoryTrendService.LoadTrendHistoryAsync(index);

        Assert.Single(result);
        Assert.Equal(100, result[0].TotalFiles);
        Assert.Equal(5000, result[0].SizeBytes);
        Assert.Equal(10, result[0].Verified);
        Assert.Equal(5, result[0].Dupes);
        Assert.Equal(3, result[0].Junk);
        Assert.Equal(82, result[0].QualityScore);
    }

    [Fact]
    public async Task LoadTrendHistory_MultipleSnapshots_OrderedByTimestamp()
    {
        var snapshots = new[]
        {
            CreateSnapshot("r2", new DateTime(2025, 3, 1), totalFiles: 200),
            CreateSnapshot("r1", new DateTime(2025, 1, 1), totalFiles: 100),
            CreateSnapshot("r3", new DateTime(2025, 2, 1), totalFiles: 150)
        };
        var index = new StubIndex(snapshots);

        var result = await RunHistoryTrendService.LoadTrendHistoryAsync(index);

        Assert.Equal(3, result.Count);
        Assert.Equal(100, result[0].TotalFiles); // oldest first
        Assert.Equal(150, result[1].TotalFiles);
        Assert.Equal(200, result[2].TotalFiles);
    }

    [Fact]
    public async Task LoadTrendHistory_SameTimestamp_OrderedByRunId()
    {
        var ts = new DateTime(2025, 1, 1);
        var snapshots = new[]
        {
            CreateSnapshot("b-run", ts, totalFiles: 20),
            CreateSnapshot("a-run", ts, totalFiles: 10)
        };
        var index = new StubIndex(snapshots);

        var result = await RunHistoryTrendService.LoadTrendHistoryAsync(index);

        Assert.Equal(2, result.Count);
        Assert.Equal(10, result[0].TotalFiles); // a-run first (alphabetical)
        Assert.Equal(20, result[1].TotalFiles);
    }

    [Fact]
    public async Task LoadTrendHistory_LimitClamped()
    {
        var snapshot = CreateSnapshot("r1", DateTime.UtcNow, totalFiles: 1);
        var index = new StubIndex([snapshot]);

        // Limit gets clamped to [1, 3650]
        var result = await RunHistoryTrendService.LoadTrendHistoryAsync(index, limit: 0);
        Assert.Single(result);

        var result2 = await RunHistoryTrendService.LoadTrendHistoryAsync(index, limit: 99999);
        Assert.Single(result2);
    }

    [Fact]
    public async Task LoadTrendHistory_CancellationRespected()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var index = new CancellingIndex();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => RunHistoryTrendService.LoadTrendHistoryAsync(index, ct: cts.Token));
    }

    // ═══ FormatTrendReport ═════════════════════════════════════════

    [Fact]
    public void FormatTrendReport_EmptyHistory_ReturnsEmptyMessage()
    {
        var result = RunHistoryTrendService.FormatTrendReport(
            [], "Title", "Nothing here", "Current", "Files", "Dupes", "History", "files", "quality");
        Assert.Equal("Nothing here", result);
    }

    [Fact]
    public void FormatTrendReport_SingleEntry_ShowsCurrentOnly()
    {
        var trend = new TrendSnapshot(new DateTime(2025, 6, 1), TotalFiles: 500, SizeBytes: 1_000_000, Verified: 50, Dupes: 10, Junk: 5, QualityScore: 90);
        var result = RunHistoryTrendService.FormatTrendReport(
            [trend], "My Title", "empty", "Current", "ΔFiles", "ΔDupes", "History", "files", "quality");

        Assert.Contains("My Title", result);
        Assert.Contains("Current: 500 files", result);
        Assert.Contains("quality=90%", result);
        Assert.DoesNotContain("ΔFiles", result); // No delta with single entry
    }

    [Fact]
    public void FormatTrendReport_TwoEntries_ShowsDelta()
    {
        var history = new[]
        {
            new TrendSnapshot(new DateTime(2025, 1, 1), 100, 500_000, 10, 5, 2, 80),
            new TrendSnapshot(new DateTime(2025, 2, 1), 120, 600_000, 15, 3, 1, 85)
        };

        var result = RunHistoryTrendService.FormatTrendReport(
            history, "Report", "empty", "Current", "ΔFiles", "ΔDupes", "History", "files", "quality");

        Assert.Contains("Current: 120 files", result);
        Assert.Contains("ΔFiles: +20", result);
        Assert.Contains("ΔDupes: -2", result);
    }

    [Fact]
    public void FormatTrendReport_ManyEntries_ShowsLast10()
    {
        var history = Enumerable.Range(1, 15)
            .Select(i => new TrendSnapshot(new DateTime(2025, 1, i), i * 10, i * 1000, i, 0, 0, 50 + i))
            .ToArray();

        var result = RunHistoryTrendService.FormatTrendReport(
            history, "Trend", "empty", "Current", "ΔFiles", "ΔDupes", "History", "files", "quality");

        // Should contain entries 6-15 (last 10)
        Assert.Contains("quality=65%", result); // entry 15
        Assert.Contains("quality=56%", result); // entry 6

        // Should contain the history header
        Assert.Contains("History", result);
    }

    [Fact]
    public void FormatTrendReport_NullHistory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RunHistoryTrendService.FormatTrendReport(null!, "T", "e", "c", "f", "d", "h", "fi", "q"));
    }

    [Fact]
    public void FormatTrendReport_NoDelta_ShowsZero()
    {
        var ts = DateTime.UtcNow;
        var history = new[]
        {
            new TrendSnapshot(ts.AddDays(-1), 100, 500_000, 10, 5, 2, 80),
            new TrendSnapshot(ts, 100, 500_000, 10, 5, 2, 80)
        };

        var result = RunHistoryTrendService.FormatTrendReport(
            history, "Stable", "empty", "Current", "ΔFiles", "ΔDupes", "History", "files", "quality");

        Assert.Contains("ΔFiles: 0", result);
        Assert.Contains("ΔDupes: 0", result);
    }

    // ═══ Helpers ════════════════════════════════════════════════════

    private static CollectionRunSnapshot CreateSnapshot(
        string runId, DateTime completedUtc,
        int totalFiles = 0, long sizeBytes = 0, int datMatches = 0,
        int dupes = 0, int junk = 0, int healthScore = 0)
    {
        return new CollectionRunSnapshot
        {
            RunId = runId,
            CompletedUtc = completedUtc,
            TotalFiles = totalFiles,
            CollectionSizeBytes = sizeBytes,
            DatMatches = datMatches,
            Dupes = dupes,
            Junk = junk,
            HealthScore = healthScore
        };
    }

    private sealed class StubIndex : ICollectionIndex
    {
        private readonly IReadOnlyList<CollectionRunSnapshot> _snapshots;
        public StubIndex(IReadOnlyList<CollectionRunSnapshot> snapshots) => _snapshots = snapshots;

        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default)
            => new(_snapshots.Take(limit).ToArray());

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(IReadOnlyList<string> roots, IReadOnlyCollection<string> extensions, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class CancellingIndex : ICollectionIndex
    {
        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return new(Array.Empty<CollectionRunSnapshot>());
        }

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(IReadOnlyList<string> roots, IReadOnlyCollection<string> extensions, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default) => throw new NotImplementedException();
        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }
}
