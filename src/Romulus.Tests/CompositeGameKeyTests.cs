using Romulus.Contracts.Models;
using Romulus.Core.Deduplication;
using Xunit;

namespace Romulus.Tests;

public sealed class CompositeGameKeyTests
{
    private static RomCandidate Candidate(string path, string consoleKey, string gameKey, int regionScore)
        => new()
        {
            MainPath = path,
            ConsoleKey = consoleKey,
            GameKey = gameKey,
            RegionScore = regionScore,
            Category = FileCategory.Game
        };

    [Fact]
    public void Deduplicate_SameGameKeyDifferentConsoleKeys_CreatesSeparateGroups()
    {
        var ps1 = Candidate("ps1_ff7.bin", "PS1", "final-fantasy-vii", 1000);
        var ps2 = Candidate("ps2_ff7.bin", "PS2", "final-fantasy-vii", 1000);

        var groups = DeduplicationEngine.Deduplicate([ps1, ps2]);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Winner.MainPath == "ps1_ff7.bin");
        Assert.Contains(groups, g => g.Winner.MainPath == "ps2_ff7.bin");
    }

    [Fact]
    public void Deduplicate_EmptyConsoleKey_GroupsWithExplicitUnknown()
    {
        var emptyConsole = Candidate("unknown_a.bin", "", "mario", 900);
        var explicitUnknown = Candidate("unknown_b.bin", "UNKNOWN", "mario", 1000);

        var groups = DeduplicationEngine.Deduplicate([emptyConsole, explicitUnknown]);

        var group = Assert.Single(groups);
        Assert.Equal("unknown_b.bin", group.Winner.MainPath);
        Assert.Single(group.Losers);
    }

    [Fact]
    public void Deduplicate_CompositeKey_IsCaseInsensitive()
    {
        var upper = Candidate("upper.bin", "PS1", "FF7", 900);
        var lower = Candidate("lower.bin", "ps1", "ff7", 1000);

        var groups = DeduplicationEngine.Deduplicate([upper, lower]);

        var group = Assert.Single(groups);
        Assert.Equal("lower.bin", group.Winner.MainPath);
        Assert.Single(group.Losers);
    }
}
