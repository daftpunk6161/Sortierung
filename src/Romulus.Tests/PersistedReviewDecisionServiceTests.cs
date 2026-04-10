using Romulus.Contracts.Models;
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
}
