using Romulus.Contracts.Models;
using Romulus.Infrastructure.Index;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for CollectionRunHistoryPageBuilder: Build, NormalizeLimit.
/// Pure pagination and projection logic shared by API/CLI/GUI.
/// </summary>
public sealed class CollectionRunHistoryPageBuilderCoverageTests
{
    private static CollectionRunSnapshot MakeSnapshot(string runId, int totalFiles = 100) => new()
    {
        RunId = runId,
        StartedUtc = DateTime.UtcNow.AddHours(-1),
        CompletedUtc = DateTime.UtcNow,
        Mode = "DryRun",
        Status = "ok",
        TotalFiles = totalFiles,
        Games = totalFiles / 2,
        Dupes = totalFiles / 4,
    };

    // ═══ NormalizeLimit ═══════════════════════════════════════════════

    [Fact]
    public void NormalizeLimit_Null_ReturnsDefault()
    {
        Assert.Equal(CollectionRunHistoryPageBuilder.DefaultLimit,
            CollectionRunHistoryPageBuilder.NormalizeLimit(null));
    }

    [Fact]
    public void NormalizeLimit_Zero_ClampsToOne()
    {
        Assert.Equal(1, CollectionRunHistoryPageBuilder.NormalizeLimit(0));
    }

    [Fact]
    public void NormalizeLimit_NegativeValue_ClampsToOne()
    {
        Assert.Equal(1, CollectionRunHistoryPageBuilder.NormalizeLimit(-10));
    }

    [Fact]
    public void NormalizeLimit_ExceedsMax_ClampsToMax()
    {
        Assert.Equal(CollectionRunHistoryPageBuilder.MaxLimit,
            CollectionRunHistoryPageBuilder.NormalizeLimit(9999));
    }

    [Fact]
    public void NormalizeLimit_WithinRange_ReturnsAsIs()
    {
        Assert.Equal(50, CollectionRunHistoryPageBuilder.NormalizeLimit(50));
    }

    [Fact]
    public void NormalizeLimit_ExactMax_ReturnsMax()
    {
        Assert.Equal(CollectionRunHistoryPageBuilder.MaxLimit,
            CollectionRunHistoryPageBuilder.NormalizeLimit(CollectionRunHistoryPageBuilder.MaxLimit));
    }

    // ═══ Build ═══════════════════════════════════════════════════════

    [Fact]
    public void Build_EmptySnapshots_EmptyPage()
    {
        var page = CollectionRunHistoryPageBuilder.Build([], totalCount: 0);
        Assert.Equal(0, page.Total);
        Assert.Equal(0, page.Returned);
        Assert.False(page.HasMore);
        Assert.Empty(page.Runs);
    }

    [Fact]
    public void Build_NullSnapshots_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => CollectionRunHistoryPageBuilder.Build(null!, totalCount: 0));
    }

    [Fact]
    public void Build_SingleSnapshot_MapsAllFields()
    {
        var snapshot = new CollectionRunSnapshot
        {
            RunId = "run-1",
            StartedUtc = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            CompletedUtc = new DateTime(2025, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            Mode = "Move",
            Status = "ok",
            Roots = [@"C:\roms"],
            RootFingerprint = "abc123",
            DurationMs = 300000,
            TotalFiles = 500,
            CollectionSizeBytes = 1024 * 1024 * 100,
            Games = 200,
            Dupes = 50,
            Junk = 30,
            DatMatches = 180,
            ConvertedCount = 10,
            FailCount = 2,
            SavedBytes = 1024 * 1024,
            ConvertSavedBytes = 1024 * 512,
            HealthScore = 85
        };

        var page = CollectionRunHistoryPageBuilder.Build([snapshot], totalCount: 1);

        Assert.Equal(1, page.Total);
        Assert.Equal(1, page.Returned);
        Assert.False(page.HasMore);

        var item = Assert.Single(page.Runs);
        Assert.Equal("run-1", item.RunId);
        Assert.Equal("Move", item.Mode);
        Assert.Equal("ok", item.Status);
        Assert.Equal(1, item.RootCount);
        Assert.Equal("abc123", item.RootFingerprint);
        Assert.Equal(300000, item.DurationMs);
        Assert.Equal(500, item.TotalFiles);
        Assert.Equal(200, item.Games);
        Assert.Equal(50, item.Dupes);
        Assert.Equal(30, item.Junk);
        Assert.Equal(180, item.DatMatches);
        Assert.Equal(10, item.ConvertedCount);
        Assert.Equal(2, item.FailCount);
        Assert.Equal(85, item.HealthScore);
    }

    [Fact]
    public void Build_WithOffset_SkipsItems()
    {
        var snapshots = new[]
        {
            MakeSnapshot("run-1"),
            MakeSnapshot("run-2"),
            MakeSnapshot("run-3"),
        };

        var page = CollectionRunHistoryPageBuilder.Build(snapshots, totalCount: 3, offset: 1);
        Assert.Equal(2, page.Returned);
        Assert.Equal("run-2", page.Runs[0].RunId);
        Assert.Equal("run-3", page.Runs[1].RunId);
    }

    [Fact]
    public void Build_WithLimit_TakesLimitedItems()
    {
        var snapshots = new[]
        {
            MakeSnapshot("run-1"),
            MakeSnapshot("run-2"),
            MakeSnapshot("run-3"),
        };

        var page = CollectionRunHistoryPageBuilder.Build(snapshots, totalCount: 3, limit: 2);
        Assert.Equal(2, page.Returned);
        Assert.True(page.HasMore);
    }

    [Fact]
    public void Build_NegativeOffset_NormalizedToZero()
    {
        var snapshots = new[] { MakeSnapshot("run-1") };
        var page = CollectionRunHistoryPageBuilder.Build(snapshots, totalCount: 1, offset: -5);
        Assert.Equal(0, page.Offset);
        Assert.Equal(1, page.Returned);
    }

    [Fact]
    public void Build_NegativeTotalCount_NormalizedToZero()
    {
        var page = CollectionRunHistoryPageBuilder.Build([], totalCount: -10);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public void Build_OffsetBeyondSnapshots_EmptyRuns()
    {
        var snapshots = new[] { MakeSnapshot("run-1"), MakeSnapshot("run-2") };
        var page = CollectionRunHistoryPageBuilder.Build(snapshots, totalCount: 2, offset: 10);
        Assert.Equal(0, page.Returned);
        Assert.Empty(page.Runs);
    }

    [Fact]
    public void Build_HasMore_TrueWhenMoreData()
    {
        var snapshots = new[] { MakeSnapshot("run-1"), MakeSnapshot("run-2") };
        var page = CollectionRunHistoryPageBuilder.Build(snapshots, totalCount: 5, limit: 2);
        Assert.True(page.HasMore);
    }

    [Fact]
    public void Build_HasMore_FalseWhenAllReturned()
    {
        var snapshots = new[] { MakeSnapshot("run-1"), MakeSnapshot("run-2") };
        var page = CollectionRunHistoryPageBuilder.Build(snapshots, totalCount: 2, limit: 10);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void Build_DefaultLimitApplied()
    {
        var page = CollectionRunHistoryPageBuilder.Build([], totalCount: 0);
        Assert.Equal(CollectionRunHistoryPageBuilder.DefaultLimit, page.Limit);
    }
}
