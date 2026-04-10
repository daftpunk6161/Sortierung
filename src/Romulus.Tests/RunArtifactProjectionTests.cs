using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

public sealed class RunArtifactProjectionTests
{
    [Fact]
    public void Project_ComposesDatRenameConsoleSortAndConversionMutations()
    {
        var originalPath = @"C:\roms\Game.zip";
        var renamedPath = @"C:\roms\Game Renamed.zip";
        var sortedPath = @"C:\roms\PS1\Game Renamed.zip";
        var convertedPath = sortedPath + ".chd";

        var winner = new RomCandidate
        {
            MainPath = originalPath,
            GameKey = "game",
            Extension = ".zip",
            ConsoleKey = "PS1",
            Category = FileCategory.Game,
            SortDecision = SortDecision.Sort,
            SizeBytes = 100
        };

        var result = new RunResult
        {
            AllCandidates = [winner],
            DedupeGroups =
            [
                new DedupeGroup
                {
                    GameKey = "game",
                    Winner = winner,
                    Losers = Array.Empty<RomCandidate>()
                }
            ],
            DatRenamePathMutations =
            [
                new PathMutation(originalPath, renamedPath)
            ],
            ConsoleSortResult = new ConsoleSortResult(
                Total: 1,
                Moved: 1,
                SetMembersMoved: 0,
                Skipped: 0,
                Unknown: 0,
                UnknownReasons: new Dictionary<string, int>(),
                Failed: 0,
                Reviewed: 0,
                Blocked: 0,
                PathMutations:
                [
                    new PathMutation(renamedPath, sortedPath)
                ]),
            ConversionReport = new ConversionReport
            {
                TotalPlanned = 1,
                Converted = 1,
                Skipped = 0,
                Errors = 0,
                Blocked = 0,
                RequiresReview = 0,
                TotalSavedBytes = 58,
                Results =
                [
                    new ConversionResult(sortedPath, convertedPath, ConversionOutcome.Success)
                    {
                        TargetBytes = 42
                    }
                ]
            }
        };

        var projected = RunArtifactProjection.Project(result);
        var projectedCandidate = Assert.Single(projected.AllCandidates);
        var projectedGroup = Assert.Single(projected.DedupeGroups);

        Assert.Equal(convertedPath, projectedCandidate.MainPath);
        Assert.Equal(".chd", projectedCandidate.Extension);
        Assert.Equal(42, projectedCandidate.SizeBytes);
        Assert.Equal(convertedPath, projectedGroup.Winner.MainPath);
        Assert.Equal(".chd", projectedGroup.Winner.Extension);
    }
}
