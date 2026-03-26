using RomCleanup.Contracts.Models;
using RomCleanup.Core.Scoring;
using RomCleanup.Infrastructure.Orchestration;
using Xunit;

namespace RomCleanup.Tests;

public class RunProjectionFactoryTests
{
    [Fact]
    public void Create_WithEmptyRunResult_ReturnsZeroProjection()
    {
        var run = new RunResult();

        var projection = RunProjectionFactory.Create(run);

        Assert.Equal("ok", projection.Status);
        Assert.Equal(0, projection.TotalFiles);
        Assert.Equal(0, projection.Candidates);
        Assert.Equal(0, projection.Keep);
        Assert.Equal(0, projection.Dupes);
        Assert.Equal(0, projection.Games);
        Assert.Equal(0, projection.Junk);
        Assert.Equal(0, projection.Bios);
        Assert.Equal(0, projection.DatMatches);
        Assert.Equal(0, projection.HealthScore);
    }

    [Fact]
    public void Create_WithCandidatesAndMoves_ProjectsAllKpis()
    {
        var winner = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game, DatMatch = true, ConsoleKey = "psx" };
        var loser = new RomCandidate { MainPath = @"C:\roms\Game (US).chd", Category = FileCategory.Game, DatMatch = false, ConsoleKey = "psx" };
        var junk = new RomCandidate { MainPath = @"C:\roms\Trash (Beta).zip", Category = FileCategory.Junk, DatMatch = false, ConsoleKey = "psx" };
        var bios = new RomCandidate { MainPath = @"C:\roms\SCPH1001.bin", Category = FileCategory.Bios, DatMatch = false, ConsoleKey = "psx" };

        var run = new RunResult
        {
            Status = "completed_with_errors",
            ExitCode = 1,
            TotalFilesScanned = 4,
            GroupCount = 1,
            WinnerCount = 1,
            LoserCount = 1,
            DedupeGroups = new[]
            {
                new DedupeGroup
                {
                    GameKey = "game",
                    Winner = winner,
                    Losers = new[] { loser }
                }
            },
            AllCandidates = new[] { winner, loser, junk, bios },
            ConvertedCount = 2,
            ConvertErrorCount = 1,
            ConvertSkippedCount = 3,
            ConvertBlockedCount = 4,
            JunkRemovedCount = 1,
            MoveResult = new MovePhaseResult(MoveCount: 1, FailCount: 1, SavedBytes: 42, SkipCount: 1),
            JunkMoveResult = new MovePhaseResult(MoveCount: 1, FailCount: 2, SavedBytes: 0, SkipCount: 0),
            ConsoleSortResult = new ConsoleSortResult(
                Total: 10,
                Moved: 9,
                SetMembersMoved: 0,
                Skipped: 0,
                Unknown: 0,
                UnknownReasons: new Dictionary<string, int>(),
                Failed: 4),
            DurationMs = 1234
        };

        var projection = RunProjectionFactory.Create(run);

        Assert.Equal("completed_with_errors", projection.Status);
        Assert.Equal(1, projection.ExitCode);
        Assert.Equal(4, projection.TotalFiles);
        Assert.Equal(4, projection.Candidates);
        Assert.Equal(1, projection.Groups);
        Assert.Equal(1, projection.Keep);
        Assert.Equal(1, projection.Dupes);
        Assert.Equal(1, projection.Games);
        Assert.Equal(1, projection.Junk);
        Assert.Equal(1, projection.Bios);
        Assert.Equal(1, projection.DatMatches);
        Assert.Equal(2, projection.ConvertedCount);
        Assert.Equal(1, projection.ConvertErrorCount);
        Assert.Equal(3, projection.ConvertSkippedCount);
        Assert.Equal(4, projection.ConvertBlockedCount);
        Assert.Equal(1, projection.JunkRemovedCount);
        Assert.Equal(1, projection.MoveCount);
        Assert.Equal(1, projection.SkipCount);
        Assert.Equal(2, projection.JunkFailCount);
        Assert.Equal(9, projection.ConsoleSortMoved);
        Assert.Equal(4, projection.ConsoleSortFailed);
        // FailCount aggregates ALL failure sources: Move(1) + JunkMove(2) + Convert(1) + ConsoleSort(4)
        Assert.Equal(8, projection.FailCount);
        Assert.Equal(42, projection.SavedBytes);
        Assert.Equal(1234, projection.DurationMs);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(100, 100, 0, 0)]
    [InlineData(100, 20, 10, 50)]
    [InlineData(100, 0, 0, 100)]
    public void Create_HealthScore_EqualsCoreScorer(int total, int dupes, int junk, int verified)
    {
        var candidates = Enumerable.Range(0, Math.Max(0, total))
            .Select(i => new RomCandidate
            {
                MainPath = $@"C:\roms\f{i}.zip",
                Category = i < junk ? FileCategory.Junk : FileCategory.Game,
                DatMatch = i < verified
            })
            .ToList();

        var run = new RunResult
        {
            TotalFilesScanned = total,
            LoserCount = dupes,
            AllCandidates = candidates
        };

        var projection = RunProjectionFactory.Create(run);
        var expected = HealthScorer.GetHealthScore(total, dupes, junk, verified);

        Assert.Equal(expected, projection.HealthScore);
    }
}