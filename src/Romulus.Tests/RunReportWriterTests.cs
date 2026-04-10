using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Reporting;
using Xunit;

namespace Romulus.Tests;

public class RunReportWriterTests
{
    [Fact]
    public void WriteReport_ProtectedSystemPath_Throws()
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir))
            return;

        var result = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 1,
            AllCandidates = [new RomCandidate { MainPath = @"C:\roms\Game.zip", Category = FileCategory.Game, GameKey = "game" }]
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RunReportWriter.WriteReport(Path.Combine(windowsDir, "romulus-report.html"), result, "DryRun"));

        Assert.Contains("protected system path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSummary_PopulatesProjectionFields()
    {
        var winner = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game, DatMatch = true };
        var loser = new RomCandidate { MainPath = @"C:\roms\Game (US).chd", Category = FileCategory.Game, DatMatch = false };
        var junk = new RomCandidate { MainPath = @"C:\roms\Game (Beta).zip", Category = FileCategory.Junk, DatMatch = false };

        var result = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 3,
            WinnerCount = 1,
            LoserCount = 1,
            GroupCount = 1,
            DedupeGroups = new[]
            {
                new DedupeGroup
                {
                    GameKey = "game",
                    Winner = winner,
                    Losers = new[] { loser }
                }
            },
            AllCandidates = new[] { winner, loser, junk },
            DurationMs = 1500,
            MoveResult = new MovePhaseResult(MoveCount: 1, FailCount: 0, SavedBytes: 100, SkipCount: 0)
        };

        var summary = RunReportWriter.BuildSummary(result, "DryRun");

        Assert.Equal(3, summary.TotalFiles);
        Assert.Equal(3, summary.Candidates);
        Assert.Equal(1, summary.KeepCount);
        Assert.Equal(1, summary.DupesCount);
        Assert.Equal(1, summary.GamesCount);
        Assert.Equal(1, summary.JunkCount);
        Assert.Equal(1, summary.DatMatches);
        Assert.Equal(100, summary.SavedBytes);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), summary.Duration);
        Assert.InRange(summary.HealthScore, 0, 100);
    }

    [Fact]
    public void BuildSummary_WhenAccountedExceedsScanned_Throws()
    {
        var winner = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game };
        var loser = new RomCandidate { MainPath = @"C:\roms\Game (US).chd", Category = FileCategory.Game };

        var result = new RunResult
        {
            TotalFilesScanned = 1,
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
            AllCandidates = new[] { winner, loser }
        };

        Assert.Throws<InvalidOperationException>(() => RunReportWriter.BuildSummary(result, "DryRun"));
    }

    [Fact]
    public void BuildSummary_WhenAccountedBelowScanned_Throws()
    {
        var winner = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game };
        var loser = new RomCandidate { MainPath = @"C:\roms\Game (US).chd", Category = FileCategory.Game };
        var unknown = new RomCandidate { MainPath = @"C:\roms\Mystery.rom", Category = FileCategory.Unknown };

        var result = new RunResult
        {
            TotalFilesScanned = 4,
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
            // 3 candidates only -> projection undercounts against scanned total 4
            AllCandidates = new[] { winner, loser, unknown }
        };

        Assert.Throws<InvalidOperationException>(() => RunReportWriter.BuildSummary(result, "DryRun"));
    }

    [Fact]
    public void BuildSummary_WhenUngroupedGameCandidateExists_DoesNotThrowIfScannedAccountingMatches()
    {
        var winner = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game, GameKey = "game" };
        var loser = new RomCandidate { MainPath = @"C:\roms\Game (US).chd", Category = FileCategory.Game, GameKey = "game" };
        var unknown = new RomCandidate { MainPath = @"C:\roms\Mystery.rom", Category = FileCategory.Unknown, GameKey = "mystery" };
        var extraUngroupedGame = new RomCandidate { MainPath = @"C:\roms\Standalone.iso", Category = FileCategory.Game, GameKey = "standalone" };

        var result = new RunResult
        {
            TotalFilesScanned = 4,
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
            AllCandidates = new[] { winner, loser, unknown, extraUngroupedGame }
        };

        var summary = RunReportWriter.BuildSummary(result, "DryRun");

        Assert.Equal(4, summary.TotalFiles);
        Assert.Equal(4, summary.Candidates);
    }

    [Fact]
    public void BuildSummary_NoDedupeGroups_DoesNotThrowInvariant()
    {
        // ConvertOnly or empty-scan: no dedupe ran, Keep/Dupes are 0
        var game = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game };

        var result = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 1,
            WinnerCount = 0,
            LoserCount = 0,
            GroupCount = 0,
            DedupeGroups = Array.Empty<DedupeGroup>(),
            AllCandidates = new[] { game },
            DurationMs = 100,
            ConvertedCount = 1
        };

        var summary = RunReportWriter.BuildSummary(result, "DryRun");
        Assert.Equal(1, summary.TotalFiles);
        Assert.Equal(1, summary.ConvertedCount);
    }

    [Fact]
    public void BuildSummary_ErrorCount_EqualsFailCountWithoutDoubleCounting()
    {
        var winner = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game };
        var loser = new RomCandidate { MainPath = @"C:\roms\Game (US).chd", Category = FileCategory.Game };

        var result = new RunResult
        {
            Status = "completed_with_errors",
            TotalFilesScanned = 2,
            WinnerCount = 1,
            LoserCount = 1,
            GroupCount = 1,
            DedupeGroups = new[]
            {
                new DedupeGroup
                {
                    GameKey = "game",
                    Winner = winner,
                    Losers = new[] { loser }
                }
            },
            AllCandidates = new[] { winner, loser },
            MoveResult = new MovePhaseResult(MoveCount: 0, FailCount: 1, SavedBytes: 0, SkipCount: 0),
            JunkMoveResult = new MovePhaseResult(MoveCount: 0, FailCount: 2, SavedBytes: 0, SkipCount: 0),
            ConsoleSortResult = new ConsoleSortResult(2, 0, 0, 0, 0, new Dictionary<string, int>(), Failed: 3)
        };

        var summary = RunReportWriter.BuildSummary(result, "Move");

        Assert.Equal(summary.FailCount, summary.ErrorCount);
        Assert.Equal(6, summary.FailCount);
    }

    [Fact]
    public void BuildEntries_NoDedupeGroups_IncludesAllCandidates()
    {
        var game = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game, GameKey = "game" };
        var junk = new RomCandidate { MainPath = @"C:\roms\Game (Beta).zip", Category = FileCategory.Junk, GameKey = "game" };

        var result = new RunResult
        {
            TotalFilesScanned = 2,
            DedupeGroups = Array.Empty<DedupeGroup>(),
            AllCandidates = new[] { game, junk }
        };

        var entries = RunReportWriter.BuildEntries(result);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Action == "KEEP" && e.FilePath == game.MainPath);
        Assert.Contains(entries, e => e.Action == "JUNK" && e.FilePath == junk.MainPath);
    }

    [Fact]
    public void BuildEntries_WithDedupeGroups_DoesNotDuplicateCandidates()
    {
        var winner = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game, GameKey = "game" };
        var loser = new RomCandidate { MainPath = @"C:\roms\Game (US).chd", Category = FileCategory.Game, GameKey = "game" };
        var extra = new RomCandidate { MainPath = @"C:\roms\Other (JP).zip", Category = FileCategory.Game, GameKey = "other" };

        var result = new RunResult
        {
            TotalFilesScanned = 3,
            DedupeGroups = new[]
            {
                new DedupeGroup { GameKey = "game", Winner = winner, Losers = new[] { loser } }
            },
            AllCandidates = new[] { winner, loser, extra }
        };

        var entries = RunReportWriter.BuildEntries(result);
        Assert.Equal(3, entries.Count);
        // winner + loser from dedupe, extra from AllCandidates fallback
        Assert.Single(entries, e => e.Action == "KEEP" && e.FilePath == winner.MainPath);
        Assert.Single(entries, e => e.Action == "MOVE" && e.FilePath == loser.MainPath);
        Assert.Single(entries, e => e.Action == "KEEP" && e.FilePath == extra.MainPath);
    }

    [Fact]
    public void BuildEntries_UsesProjectedConvertedWinnerPath()
    {
        var winner = new RomCandidate { MainPath = @"C:\roms\Game (EU).zip", Category = FileCategory.Game, GameKey = "game", Extension = ".zip" };
        var loser = new RomCandidate { MainPath = @"C:\roms\Game (US).zip", Category = FileCategory.Game, GameKey = "game", Extension = ".zip" };

        var result = new RunResult
        {
            TotalFilesScanned = 2,
            DedupeGroups =
            [
                new DedupeGroup { GameKey = "game", Winner = winner, Losers = [loser] }
            ],
            AllCandidates = [winner, loser],
            ConvertedCount = 1,
            ConversionReport = new ConversionReport
            {
                TotalPlanned = 1,
                Converted = 1,
                Skipped = 0,
                Errors = 0,
                Blocked = 0,
                RequiresReview = 0,
                TotalSavedBytes = 0,
                Results =
                [
                    new ConversionResult(winner.MainPath, @"C:\roms\Game (EU).chd", ConversionOutcome.Success)
                ]
            }
        };

        var entries = RunReportWriter.BuildEntries(result);

        Assert.Contains(entries, entry => entry.Action == "KEEP" && entry.FilePath == @"C:\roms\Game (EU).chd");
        Assert.DoesNotContain(entries, entry => entry.Action == "KEEP" && entry.FilePath == winner.MainPath);
    }
}
