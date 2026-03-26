using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using Xunit;

namespace RomCleanup.Tests;

/// <summary>
/// Phase 5A invariant tests for core model structures:
/// - RomCandidate sealed record semantics
/// - DedupeGroup sealed record semantics
/// - SortDecision enum type safety
/// - RunProjection KPI-additivity
/// - RunResultBuilder → RunResult → RunProjection consistency
/// </summary>
public class Phase5AModelInvariantTests
{
    // ── RomCandidate Record Semantics ──────────────────────────────

    [Fact]
    public void RomCandidate_IsValueEqual_WhenAllPropertiesMatch()
    {
        var a = new RomCandidate { MainPath = "/a.bin", GameKey = "game", Region = "USA", RegionScore = 100 };
        var b = new RomCandidate { MainPath = "/a.bin", GameKey = "game", Region = "USA", RegionScore = 100 };

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void RomCandidate_IsNotValueEqual_WhenPropertyDiffers()
    {
        var a = new RomCandidate { MainPath = "/a.bin", GameKey = "game" };
        var b = new RomCandidate { MainPath = "/b.bin", GameKey = "game" };

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void RomCandidate_WithExpression_CreatesModifiedCopy()
    {
        var original = new RomCandidate { MainPath = "/a.bin", GameKey = "game", Region = "USA" };
        var modified = original with { Region = "EUR" };

        Assert.Equal("USA", original.Region);
        Assert.Equal("EUR", modified.Region);
        Assert.Equal(original.MainPath, modified.MainPath);
        Assert.NotEqual(original, modified);
    }

    [Fact]
    public void RomCandidate_SortDecision_IsEnumType()
    {
        var candidate = new RomCandidate { SortDecision = SortDecision.Sort };
        Assert.IsType<SortDecision>(candidate.SortDecision);
        Assert.Equal(SortDecision.Sort, candidate.SortDecision);
    }

    [Fact]
    public void RomCandidate_DefaultSortDecision_IsBlocked()
    {
        var candidate = new RomCandidate();
        Assert.Equal(SortDecision.Blocked, candidate.SortDecision);
    }

    [Fact]
    public void RomCandidate_SortDecisionEnum_HasAllExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(SortDecision), SortDecision.Sort));
        Assert.True(Enum.IsDefined(typeof(SortDecision), SortDecision.Review));
        Assert.True(Enum.IsDefined(typeof(SortDecision), SortDecision.Blocked));
        Assert.True(Enum.IsDefined(typeof(SortDecision), SortDecision.DatVerified));
    }

    // ── DedupeGroup Record Semantics ────────────────────────────────

    [Fact]
    public void DedupeGroup_IsValueEqual_WhenAllPropertiesMatch()
    {
        var winner = new RomCandidate { MainPath = "/a.bin", GameKey = "game" };
        var loser = new RomCandidate { MainPath = "/b.bin", GameKey = "game" };
        var losers = new[] { loser };

        var a = new DedupeGroup { Winner = winner, Losers = losers, GameKey = "game" };
        var b = new DedupeGroup { Winner = winner, Losers = losers, GameKey = "game" };

        Assert.Equal(a, b);
    }

    [Fact]
    public void DedupeGroup_WithExpression_CreatesModifiedCopy()
    {
        var winner = new RomCandidate { MainPath = "/a.bin", GameKey = "game" };
        var original = new DedupeGroup { Winner = winner, GameKey = "game" };
        var modified = original with { GameKey = "other" };

        Assert.Equal("game", original.GameKey);
        Assert.Equal("other", modified.GameKey);
        Assert.Same(original.Winner, modified.Winner);
    }

    // ── KPI-Additivität: FailCount ──────────────────────────────────

    [Theory]
    [InlineData(1, 2, 3, 0, 4, 10)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(5, 0, 0, 3, 0, 8)]
    [InlineData(0, 0, 7, 0, 0, 7)]
    public void RunProjection_FailCount_IsAdditiveSum(
        int moveFails, int junkFails, int convertErrors, int datRenameFails, int consoleSortFails, int expectedTotal)
    {
        var run = new RunResult
        {
            MoveResult = new MovePhaseResult(MoveCount: 0, FailCount: moveFails, SavedBytes: 0),
            JunkMoveResult = new MovePhaseResult(MoveCount: 0, FailCount: junkFails, SavedBytes: 0),
            ConvertErrorCount = convertErrors,
            DatRenameFailedCount = datRenameFails,
            ConsoleSortResult = new ConsoleSortResult(
                Total: 10, Moved: 0, SetMembersMoved: 0, Skipped: 0, Unknown: 0,
                UnknownReasons: new Dictionary<string, int>(), Failed: consoleSortFails),
        };

        var projection = RunProjectionFactory.Create(run);

        Assert.Equal(expectedTotal, projection.FailCount);
        Assert.Equal(moveFails + junkFails + convertErrors + datRenameFails + consoleSortFails, projection.FailCount);
    }

    [Fact]
    public void RunProjection_FailCount_HandlesNullSubResults()
    {
        var run = new RunResult
        {
            MoveResult = null,
            JunkMoveResult = null,
            ConsoleSortResult = null,
            ConvertErrorCount = 5,
            DatRenameFailedCount = 3,
        };

        var projection = RunProjectionFactory.Create(run);

        Assert.Equal(8, projection.FailCount);
    }

    // ── RunResultBuilder → RunResult → RunProjection Konsistenz ────

    [Fact]
    public void RunResultBuilder_Build_ProducesRunResult_ThenProjection_IsConsistent()
    {
        var winner = new RomCandidate { MainPath = "/a.bin", GameKey = "game", Category = FileCategory.Game, DatMatch = true };
        var loser = new RomCandidate { MainPath = "/b.bin", GameKey = "game", Category = FileCategory.Game };
        var junkItem = new RomCandidate { MainPath = "/c.bin", GameKey = "junk", Category = FileCategory.Junk };

        var builder = new RunResultBuilder
        {
            Status = "ok",
            TotalFilesScanned = 10,
            GroupCount = 1,
            WinnerCount = 1,
            LoserCount = 1,
            ConvertedCount = 2,
            ConvertErrorCount = 1,
            ConvertSkippedCount = 3,
            JunkRemovedCount = 1,
            AllCandidates = new[] { winner, loser, junkItem },
            DedupeGroups = new[] { new DedupeGroup { Winner = winner, Losers = new[] { loser }, GameKey = "game" } },
            MoveResult = new MovePhaseResult(MoveCount: 1, FailCount: 0, SavedBytes: 100),
        };

        var result = builder.Build();
        var projection = RunProjectionFactory.Create(result);

        // Builder → RunResult consistency
        Assert.Equal(builder.Status, result.Status);
        Assert.Equal(builder.TotalFilesScanned, result.TotalFilesScanned);
        Assert.Equal(builder.GroupCount, result.GroupCount);
        Assert.Equal(builder.ConvertedCount, result.ConvertedCount);
        Assert.Equal(builder.ConvertErrorCount, result.ConvertErrorCount);

        // RunResult → RunProjection consistency
        Assert.Equal(result.Status, projection.Status);
        Assert.Equal(result.TotalFilesScanned, projection.TotalFiles);
        Assert.Equal(result.GroupCount, projection.Groups);
        Assert.Equal(result.WinnerCount, projection.Keep);
        Assert.Equal(result.LoserCount, projection.Dupes);
        Assert.Equal(result.ConvertedCount, projection.ConvertedCount);
        Assert.Equal(result.ConvertErrorCount, projection.ConvertErrorCount);
        Assert.Equal(result.ConvertSkippedCount, projection.ConvertSkippedCount);
        Assert.Equal(result.JunkRemovedCount, projection.JunkRemovedCount);
        Assert.Equal(result.MoveResult!.MoveCount, projection.MoveCount);
        Assert.Equal(result.MoveResult!.SavedBytes, projection.SavedBytes);

        // KPI additivity
        Assert.Equal(1, projection.FailCount); // Only ConvertErrorCount=1, MoveFail=0
        Assert.Equal(1, projection.Junk);
        Assert.Equal(1, projection.DatMatches);
    }

    [Fact]
    public void RunProjection_DatCountsPassThrough_ExactlyFromRunResult()
    {
        var run = new RunResult
        {
            DatHaveCount = 10,
            DatHaveWrongNameCount = 2,
            DatMissCount = 3,
            DatUnknownCount = 4,
            DatAmbiguousCount = 1,
            DatRenameProposedCount = 5,
            DatRenameExecutedCount = 4,
            DatRenameSkippedCount = 2,
            DatRenameFailedCount = 1,
        };

        var projection = RunProjectionFactory.Create(run);

        Assert.Equal(run.DatHaveCount, projection.DatHaveCount);
        Assert.Equal(run.DatHaveWrongNameCount, projection.DatHaveWrongNameCount);
        Assert.Equal(run.DatMissCount, projection.DatMissCount);
        Assert.Equal(run.DatUnknownCount, projection.DatUnknownCount);
        Assert.Equal(run.DatAmbiguousCount, projection.DatAmbiguousCount);
        Assert.Equal(run.DatRenameProposedCount, projection.DatRenameProposedCount);
        Assert.Equal(run.DatRenameExecutedCount, projection.DatRenameExecutedCount);
        Assert.Equal(run.DatRenameSkippedCount, projection.DatRenameSkippedCount);
        Assert.Equal(run.DatRenameFailedCount, projection.DatRenameFailedCount);
    }

    [Fact]
    public void RunProjection_ConvertCountsPassThrough_ExactlyFromRunResult()
    {
        var run = new RunResult
        {
            ConvertedCount = 10,
            ConvertErrorCount = 2,
            ConvertSkippedCount = 3,
            ConvertBlockedCount = 4,
            ConvertReviewCount = 1,
            ConvertLossyWarningCount = 5,
            ConvertVerifyPassedCount = 8,
            ConvertVerifyFailedCount = 2,
            ConvertSavedBytes = 123456,
        };

        var projection = RunProjectionFactory.Create(run);

        Assert.Equal(run.ConvertedCount, projection.ConvertedCount);
        Assert.Equal(run.ConvertErrorCount, projection.ConvertErrorCount);
        Assert.Equal(run.ConvertSkippedCount, projection.ConvertSkippedCount);
        Assert.Equal(run.ConvertBlockedCount, projection.ConvertBlockedCount);
        Assert.Equal(run.ConvertReviewCount, projection.ConvertReviewCount);
        Assert.Equal(run.ConvertLossyWarningCount, projection.ConvertLossyWarningCount);
        Assert.Equal(run.ConvertVerifyPassedCount, projection.ConvertVerifyPassedCount);
        Assert.Equal(run.ConvertVerifyFailedCount, projection.ConvertVerifyFailedCount);
        Assert.Equal(run.ConvertSavedBytes, projection.ConvertSavedBytes);
    }
}
