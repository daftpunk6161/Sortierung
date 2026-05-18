using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Review;
using Xunit;

namespace Romulus.Tests;

public sealed class PersistedReviewDecisionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _databasePath;

    public PersistedReviewDecisionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_ReviewStore_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _databasePath = Path.Combine(_tempDir, "collection.db");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task ApplyApprovalsAsync_PromotesOnlyReviewCandidates()
    {
        using var service = new PersistedReviewDecisionService(new LiteDbReviewDecisionStore(_databasePath));
        var reviewPath = Path.Combine(_tempDir, "review.bin");
        var blockedPath = Path.Combine(_tempDir, "blocked.bin");
        var unknownPath = Path.Combine(_tempDir, "unknown.bin");

        var persistedCount = await service.PersistApprovalsAsync(
        [
            CreateCandidate(reviewPath, SortDecision.Review),
            CreateCandidate(blockedPath, SortDecision.Blocked),
            CreateCandidate(unknownPath, SortDecision.Unknown)
        ], "test");

        var updated = await service.ApplyApprovalsAsync(
        [
            CreateCandidate(reviewPath, SortDecision.Review),
            CreateCandidate(blockedPath, SortDecision.Blocked),
            CreateCandidate(unknownPath, SortDecision.Unknown)
        ]);

        Assert.Equal(3, persistedCount);
        Assert.Equal(SortDecision.Sort, updated[0].SortDecision);
        Assert.Equal(SortDecision.Blocked, updated[1].SortDecision);
        Assert.Equal(SortDecision.Unknown, updated[2].SortDecision);
    }

    [Fact]
    public async Task GetApprovedPathSetAsync_RoundTripsPersistedApprovals()
    {
        using var service = new PersistedReviewDecisionService(new LiteDbReviewDecisionStore(_databasePath));
        var firstPath = Path.Combine(_tempDir, "first.bin");
        var secondPath = Path.Combine(_tempDir, "second.bin");

        await service.PersistApprovalsAsync(
        [
            CreateCandidate(firstPath, SortDecision.Review, consoleKey: "SNES"),
            CreateCandidate(secondPath, SortDecision.Review, consoleKey: "NES")
        ], "api");

        var approvedPaths = await service.GetApprovedPathSetAsync([firstPath, secondPath, Path.Combine(_tempDir, "missing.bin")]);

        Assert.Equal(2, approvedPaths.Count);
        Assert.Contains(Path.GetFullPath(firstPath), approvedPaths);
        Assert.Contains(Path.GetFullPath(secondPath), approvedPaths);
    }

    [Fact]
    public async Task ApplyApprovalsAsync_FileChangedAfterApproval_DoesNotPromoteCandidate()
    {
        var warningMessages = new List<string>();
        using var service = new PersistedReviewDecisionService(
            new LiteDbReviewDecisionStore(_databasePath),
            onWarning: warningMessages.Add);

        var stalePath = Path.Combine(_tempDir, "stale.bin");
        File.WriteAllText(stalePath, "v1");

        await service.PersistApprovalsAsync(
        [
            CreateCandidate(stalePath, SortDecision.Review)
        ], "test");

        File.WriteAllText(stalePath, "v2");
        File.SetLastWriteTimeUtc(stalePath, DateTime.UtcNow.AddMinutes(1));

        var updated = await service.ApplyApprovalsAsync(
        [
            CreateCandidate(stalePath, SortDecision.Review)
        ]);

        Assert.Equal(SortDecision.Review, updated[0].SortDecision);
        Assert.Contains(warningMessages, message => message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApplyApprovalsAsync_MissingApprovedFile_DoesNotPromoteCandidate()
    {
        var missingPath = Path.Combine(_tempDir, "missing.bin");
        var warnings = new List<string>();
        using var service = new PersistedReviewDecisionService(
            new InMemoryReviewDecisionStore(
            [
                new ReviewApprovalEntry
                {
                    Path = missingPath,
                    SortDecision = SortDecision.Review,
                    MatchLevel = MatchLevel.Exact,
                    MatchReasoning = "approved before file disappeared",
                    FileLastWriteUtcTicks = DateTime.UtcNow.Ticks
                }
            ]),
            warnings.Add);

        var updated = await service.ApplyApprovalsAsync(
        [
            CreateCandidate(missingPath, SortDecision.Review)
        ]);

        Assert.Equal(SortDecision.Review, updated[0].SortDecision);
        Assert.Contains(warnings, message => message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PersistApprovalsAsync_DeduplicatesPathsAndCapturesMissingFileAsNonTimestampedApproval()
    {
        var store = new InMemoryReviewDecisionStore();
        using var service = new PersistedReviewDecisionService(store);
        var missingPath = Path.Combine(_tempDir, "missing.bin");

        var persistedCount = await service.PersistApprovalsAsync(
        [
            CreateCandidate(missingPath, SortDecision.Review, consoleKey: ""),
            CreateCandidate(missingPath, SortDecision.Review, consoleKey: "NES"),
            CreateCandidate("   ", SortDecision.Review, consoleKey: "SNES")
        ], "api");

        Assert.Equal(1, persistedCount);
        var approval = Assert.Single(store.UpsertedApprovals);
        Assert.Equal(missingPath, approval.Path);
        Assert.Equal("UNKNOWN", approval.ConsoleKey);
        Assert.Equal("api", approval.Source);
        Assert.Null(approval.FileLastWriteUtcTicks);
    }

    [Fact]
    public async Task ReviewStoreFailures_AreWarningsInsteadOfRunBlockers()
    {
        var warningMessages = new List<string>();
        using var service = new PersistedReviewDecisionService(
            new ThrowingReviewDecisionStore(),
            warningMessages.Add);
        var reviewPath = Path.Combine(_tempDir, "review.bin");

        var updated = await service.ApplyApprovalsAsync([CreateCandidate(reviewPath, SortDecision.Review)]);
        var persistedCount = await service.PersistApprovalsAsync([CreateCandidate(reviewPath, SortDecision.Review)], "api");
        var approvedPaths = await service.GetApprovedPathSetAsync([reviewPath]);

        Assert.Equal(SortDecision.Review, updated[0].SortDecision);
        Assert.Equal(0, persistedCount);
        Assert.Empty(approvedPaths);
        Assert.Contains(warningMessages, message => message.Contains("reuse skipped", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warningMessages, message => message.Contains("write skipped", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warningMessages, message => message.Contains("read skipped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LiteDbReviewDecisionStore_CorruptDatabaseIsBackedUpAndRecreated()
    {
        var warningMessages = new List<string>();
        File.WriteAllText(_databasePath, "not a litedb database");

        using var store = new LiteDbReviewDecisionStore(_databasePath, warningMessages.Add);
        var approvedPath = Path.Combine(_tempDir, "approved.bin");
        await store.UpsertApprovalsAsync(
        [
            new ReviewApprovalEntry
            {
                Path = approvedPath,
                ConsoleKey = "SNES",
                SortDecision = SortDecision.Review,
                MatchLevel = MatchLevel.Exact,
                MatchReasoning = "manual",
                Source = "api",
                ApprovedUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
            }
        ]);

        var approvals = await store.ListApprovalsAsync([approvedPath]);

        var approval = Assert.Single(approvals);
        Assert.Equal(Path.GetFullPath(approvedPath), approval.Path);
        Assert.Contains(warningMessages, message => message.Contains("signature validation failure", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(Directory.EnumerateFiles(_tempDir, "*.review-open-failure.*.bak"));
    }

    private static RomCandidate CreateCandidate(string path, SortDecision sortDecision, string consoleKey = "UNKNOWN")
        => new()
        {
            MainPath = path,
            ConsoleKey = consoleKey,
            SortDecision = sortDecision,
            MatchEvidence = new MatchEvidence
            {
                Level = MatchLevel.Exact,
                Reasoning = "test"
            }
        };

    private sealed class InMemoryReviewDecisionStore(
        IReadOnlyList<ReviewApprovalEntry>? approvals = null) : IReviewDecisionStore
    {
        private readonly List<ReviewApprovalEntry> _approvals = approvals?.ToList() ?? new();

        public List<ReviewApprovalEntry> UpsertedApprovals { get; } = new();

        public ValueTask UpsertApprovalsAsync(
            IReadOnlyList<ReviewApprovalEntry> approvals,
            CancellationToken ct = default)
        {
            UpsertedApprovals.AddRange(approvals);
            _approvals.RemoveAll(existing => approvals.Any(next => string.Equals(existing.Path, next.Path, StringComparison.OrdinalIgnoreCase)));
            _approvals.AddRange(approvals);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<ReviewApprovalEntry>> ListApprovalsAsync(
            IReadOnlyList<string> paths,
            CancellationToken ct = default)
        {
            var results = paths
                .SelectMany(path => _approvals.Where(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            return new ValueTask<IReadOnlyList<ReviewApprovalEntry>>(results);
        }
    }

    private sealed class ThrowingReviewDecisionStore : IReviewDecisionStore
    {
        public ValueTask UpsertApprovalsAsync(
            IReadOnlyList<ReviewApprovalEntry> approvals,
            CancellationToken ct = default)
            => throw new IOException("store write failed");

        public ValueTask<IReadOnlyList<ReviewApprovalEntry>> ListApprovalsAsync(
            IReadOnlyList<string> paths,
            CancellationToken ct = default)
            => throw new IOException("store read failed");
    }
}
