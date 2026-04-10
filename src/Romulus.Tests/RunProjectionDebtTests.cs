using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for RunProjectionFactory technical debt fixes:
/// - FailCount must aggregate ALL failure sources
/// - Shadow-logic elimination: use RunResult values instead of re-deriving
/// - FilteredNonGameCount must not fall back to bios count
/// </summary>
public sealed class RunProjectionDebtTests
{
    // =========================================================================
    //  DEBT-01: FailCount must include DatRenameFailed + ConsoleSortFailed + JunkMoveFailed
    // =========================================================================

    [Fact]
    public void FailCount_IncludesDatRenameFailedCount()
    {
        var result = new RunResult
        {
            DatRenameFailedCount = 3
        };

        var projection = RunProjectionFactory.Create(result);

        Assert.True(projection.FailCount >= 3,
            $"FailCount should include DatRenameFailedCount=3, but was {projection.FailCount}");
    }

    [Fact]
    public void FailCount_IncludesConsoleSortFailed()
    {
        var result = new RunResult
        {
            ConsoleSortResult = new ConsoleSortResult(Total: 5, Moved: 0, SetMembersMoved: 0, Skipped: 0, Unknown: 0, UnknownReasons: new Dictionary<string, int>(), Failed: 5)
        };

        var projection = RunProjectionFactory.Create(result);

        Assert.True(projection.FailCount >= 5,
            $"FailCount should include ConsoleSortFailed=5, but was {projection.FailCount}");
    }

    [Fact]
    public void FailCount_IncludesJunkMoveResultFailCount()
    {
        var result = new RunResult
        {
            JunkMoveResult = new MovePhaseResult(MoveCount: 0, FailCount: 2, SavedBytes: 0)
        };

        var projection = RunProjectionFactory.Create(result);

        Assert.True(projection.FailCount >= 2,
            $"FailCount should include JunkMoveResult.FailCount=2, but was {projection.FailCount}");
    }

    [Fact]
    public void FailCount_AggregatesAllFailureSources()
    {
        var result = new RunResult
        {
            MoveResult = new MovePhaseResult(MoveCount: 10, FailCount: 1, SavedBytes: 100),
            JunkMoveResult = new MovePhaseResult(MoveCount: 5, FailCount: 2, SavedBytes: 50),
            ConvertErrorCount = 3,
            DatRenameFailedCount = 4,
            ConsoleSortResult = new ConsoleSortResult(Total: 15, Moved: 10, SetMembersMoved: 0, Skipped: 0, Unknown: 0, UnknownReasons: new Dictionary<string, int>(), Failed: 5)
        };

        var projection = RunProjectionFactory.Create(result);

        Assert.Equal(1 + 2 + 3 + 4 + 5, projection.FailCount);
    }

    // =========================================================================
    //  DEBT-02: Unknown should come from RunResult, not re-derived from candidates
    // =========================================================================

    [Fact]
    public void Unknown_UsesRunResultValue_NotCandidateReDerivation()
    {
        // RunResult.UnknownCount = 7, but no AllCandidates provided
        // If projection re-derives from candidates, it would get 0
        var result = new RunResult
        {
            UnknownCount = 7
        };

        var projection = RunProjectionFactory.Create(result);

        Assert.Equal(7, projection.Unknown);
    }

    // =========================================================================
    //  DEBT-03: Games should follow kept game truth, not raw GroupCount metadata
    // =========================================================================

    [Fact]
    public void Games_UsesKeptGameTruth_NotRawGroupCountMetadata()
    {
        var result = new RunResult
        {
            GroupCount = 15,
            AllCandidates =
            [
                new RomCandidate { MainPath = @"C:\roms\winner.zip", GameKey = "winner", Category = FileCategory.Game },
                new RomCandidate { MainPath = @"C:\roms\loser.zip", GameKey = "winner", Category = FileCategory.Game }
            ],
            DedupeGroups =
            [
                new DedupeGroup
                {
                    GameKey = "winner",
                    Winner = new RomCandidate { MainPath = @"C:\roms\winner.zip", GameKey = "winner", Category = FileCategory.Game },
                    Losers = [new RomCandidate { MainPath = @"C:\roms\loser.zip", GameKey = "winner", Category = FileCategory.Game }]
                }
            ]
        };

        var projection = RunProjectionFactory.Create(result);

        Assert.Equal(1, projection.Games);
    }

    // =========================================================================
    //  DEBT-04: FilteredNonGameCount must not fall back to bios count
    // =========================================================================

    [Fact]
    public void FilteredNonGameCount_ZeroMeansZero_NotBiosFallback()
    {
        // FilteredNonGameCount = 0, but we have bios candidates
        // Old code falls back to bios count when FilteredNonGameCount is 0
        var result = new RunResult
        {
            FilteredNonGameCount = 0,
            AllCandidates = new[]
            {
                new RomCandidate { MainPath = "bios.rom", GameKey = "bios", Category = FileCategory.Bios }
            }
        };

        var projection = RunProjectionFactory.Create(result);

        Assert.Equal(0, projection.FilteredNonGameCount);
    }
}
