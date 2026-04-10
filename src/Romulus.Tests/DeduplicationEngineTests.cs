using Romulus.Contracts.Models;
using Romulus.Core.Deduplication;
using Xunit;

namespace Romulus.Tests;

public class DeduplicationEngineTests
{
    private static RomCandidate MakeCandidate(
        string mainPath = "game.zip",
        string gameKey = "game",
        int regionScore = 0,
        int versionScore = 0,
        int formatScore = 0,
        int headerScore = 0,
        int completenessScore = 0,
        long sizeTieBreakScore = 0,
        bool datMatch = false)
        => new()
        {
            MainPath = mainPath,
            GameKey = gameKey,
            RegionScore = regionScore,
            VersionScore = versionScore,
            FormatScore = formatScore,
            HeaderScore = headerScore,
            CompletenessScore = completenessScore,
            SizeTieBreakScore = sizeTieBreakScore,
            DatMatch = datMatch
        };

    // --- Null / Empty ---

    [Fact]
    public void Null_ReturnsNull()
    {
        Assert.Null(DeduplicationEngine.SelectWinner(null!));
    }

    [Fact]
    public void Empty_ReturnsNull()
    {
        Assert.Null(DeduplicationEngine.SelectWinner(Array.Empty<RomCandidate>()));
    }

    [Fact]
    public void SingleItem_ReturnsThatItem()
    {
        var item = MakeCandidate();
        Assert.Same(item, DeduplicationEngine.SelectWinner(new[] { item }));
    }

    // --- RegionScore Priority ---

    [Fact]
    public void HigherRegionScore_Wins()
    {
        var high = MakeCandidate(mainPath: "a.zip", regionScore: 1000, versionScore: 100);
        var low = MakeCandidate(mainPath: "b.zip", regionScore: 999, versionScore: 500);
        Assert.Same(high, DeduplicationEngine.SelectWinner(new[] { low, high }));
    }

    // --- VersionScore Tiebreak ---

    [Fact]
    public void HigherVersionScore_WinsOnTie()
    {
        var v100 = MakeCandidate(mainPath: "a.zip", regionScore: 1000, versionScore: 100);
        var v200 = MakeCandidate(mainPath: "b.zip", regionScore: 1000, versionScore: 200);
        Assert.Same(v200, DeduplicationEngine.SelectWinner(new[] { v100, v200 }));
    }

    // --- FormatScore Tiebreak ---

    [Fact]
    public void HigherFormatScore_WinsOnTie()
    {
        var chd = MakeCandidate(mainPath: "a.chd", formatScore: 850);
        var iso = MakeCandidate(mainPath: "b.iso", formatScore: 700);
        Assert.Same(chd, DeduplicationEngine.SelectWinner(new[] { iso, chd }));
    }

    // --- Size Tiebreak ---

    [Fact]
    public void HigherSizeTieBreakScore_WinsOnTie()
    {
        var large = MakeCandidate(mainPath: "a.chd", sizeTieBreakScore: 500_000);
        var small = MakeCandidate(mainPath: "b.chd", sizeTieBreakScore: 100_000);
        Assert.Same(large, DeduplicationEngine.SelectWinner(new[] { small, large }));
    }

    // --- Completeness Bonus ---

    [Fact]
    public void HigherCompleteness_Wins()
    {
        var complete = MakeCandidate(mainPath: "a.zip", completenessScore: 100);
        var incomplete = MakeCandidate(mainPath: "b.zip", completenessScore: 0);
        Assert.Same(complete, DeduplicationEngine.SelectWinner(new[] { incomplete, complete }));
    }

    // --- DatMatch Bonus ---

    [Fact]
    public void DatMatch_WinsOverNonMatch()
    {
        var matched = MakeCandidate(mainPath: "a.zip", datMatch: true);
        var unmatched = MakeCandidate(mainPath: "b.zip", datMatch: false);
        Assert.Same(matched, DeduplicationEngine.SelectWinner(new[] { unmatched, matched }));
    }

    // --- Lexicographic Tiebreak (BUG-011) ---

    [Fact]
    public void AllEqual_AlphabeticallyFirst_Wins()
    {
        var a = MakeCandidate(mainPath: "a_game.chd");
        var z = MakeCandidate(mainPath: "z_game.chd");
        Assert.Same(a, DeduplicationEngine.SelectWinner(new[] { z, a }));
    }

    // --- Determinism ---

    [Fact]
    public void SelectWinner_IsDeterministic()
    {
        var items = Enumerable.Range(0, 10)
            .Select(i => MakeCandidate(
                mainPath: $"game_{i:D2}.zip",
                regionScore: 500 + (i % 3),
                versionScore: i * 10))
            .ToArray();

        var first = DeduplicationEngine.SelectWinner(items);
        for (var run = 0; run < 10; run++)
        {
            var result = DeduplicationEngine.SelectWinner(items);
            Assert.Same(first, result);
        }
    }

    // --- Deduplicate groups ---

    [Fact]
    public void Deduplicate_GroupsByGameKey()
    {
        var candidates = new[]
        {
            MakeCandidate(mainPath: "mario_eu.zip", gameKey: "mario", regionScore: 1000),
            MakeCandidate(mainPath: "mario_us.zip", gameKey: "mario", regionScore: 999),
            MakeCandidate(mainPath: "zelda_eu.zip", gameKey: "zelda", regionScore: 1000),
        };

        var results = DeduplicationEngine.Deduplicate(candidates);
        Assert.Equal(2, results.Count);

        var mario = results.First(r => r.GameKey == "mario");
        Assert.Equal("mario_eu.zip", mario.Winner.MainPath);
        Assert.Single(mario.Losers);

        var zelda = results.First(r => r.GameKey == "zelda");
        Assert.Equal("zelda_eu.zip", zelda.Winner.MainPath);
        Assert.Empty(zelda.Losers);
    }

    [Fact]
    public void Deduplicate_EmptyGameKey_ExcludedFromGroups()
    {
        var candidates = new[]
        {
            MakeCandidate(mainPath: "no_key.zip", gameKey: "", regionScore: 1000),
            MakeCandidate(mainPath: "mario.zip", gameKey: "mario", regionScore: 500),
        };

        var results = DeduplicationEngine.Deduplicate(candidates);
        Assert.Single(results);
        Assert.Equal("mario", results[0].GameKey);
    }

    [Fact]
    public void Deduplicate_CaseInsensitiveGrouping()
    {
        var candidates = new[]
        {
            MakeCandidate(mainPath: "mario_upper.zip", gameKey: "MARIO", regionScore: 500),
            MakeCandidate(mainPath: "mario_lower.zip", gameKey: "mario", regionScore: 1000),
        };

        var results = DeduplicationEngine.Deduplicate(candidates);
        Assert.Single(results);
        Assert.Equal("mario_lower.zip", results[0].Winner.MainPath);
        Assert.Single(results[0].Losers);
    }

    [Fact]
    public void Deduplicate_HeaderScore_BreaksTie()
    {
        var a = MakeCandidate(mainPath: "a.zip", gameKey: "game", regionScore: 100, headerScore: 50);
        var b = MakeCandidate(mainPath: "b.zip", gameKey: "game", regionScore: 100, headerScore: 100);

        var results = DeduplicationEngine.Deduplicate(new[] { a, b });
        Assert.Single(results);
        Assert.Equal("b.zip", results[0].Winner.MainPath);
    }

    [Fact]
    public void Deduplicate_SameMainPathDifferentCandidates_FiltersSamePathLoser()
    {
        // Safety invariant: a physical file path must never be both winner and loser.
        // When two candidates share the same MainPath, the loser is filtered out
        // because you cannot move a file to trash that is also the winner.
        var stronger = MakeCandidate(
            mainPath: "same_path.zip",
            gameKey: "game",
            regionScore: 1000,
            versionScore: 100,
            formatScore: 850);
        var weaker = MakeCandidate(
            mainPath: "same_path.zip",
            gameKey: "game",
            regionScore: 200,
            versionScore: 0,
            formatScore: 300);

        var results = DeduplicationEngine.Deduplicate(new[] { stronger, weaker });

        Assert.Single(results);
        Assert.Equal("same_path.zip", results[0].Winner.MainPath);
        Assert.Empty(results[0].Losers);
    }

    [Fact]
    public void SelectWinner_GameCategory_BeatsUnknownDespiteHigherScores()
    {
        var unknown = new RomCandidate
        {
            MainPath = "u.zip",
            GameKey = "game",
            Category = FileCategory.Unknown,
            RegionScore = 1000,
            VersionScore = 1000,
            FormatScore = 1000
        };
        var game = new RomCandidate
        {
            MainPath = "g.zip",
            GameKey = "game",
            Category = FileCategory.Game,
            RegionScore = 1,
            VersionScore = 1,
            FormatScore = 1
        };

        var winner = DeduplicationEngine.SelectWinner(new[] { unknown, game });

        Assert.NotNull(winner);
        Assert.Equal(FileCategory.Game, winner!.Category);
    }

    // --- Whitespace-only GameKey excluded ---

    [Fact]
    public void Deduplicate_WhitespaceOnlyGameKey_ExcludedFromGroups()
    {
        var candidates = new[]
        {
            MakeCandidate(mainPath: "space_key.zip", gameKey: "   ", regionScore: 1000),
            MakeCandidate(mainPath: "mario.zip", gameKey: "mario", regionScore: 500),
        };

        var results = DeduplicationEngine.Deduplicate(candidates);
        Assert.Single(results);
        Assert.Equal("mario", results[0].GameKey);
    }

    // --- Deduplicate determinism (full pipeline) ---

    [Fact]
    public void Deduplicate_IsDeterministic()
    {
        var candidates = Enumerable.Range(0, 20)
            .Select(i => MakeCandidate(
                mainPath: $"game_{i:D2}.zip",
                gameKey: $"group_{i % 4}",
                regionScore: 500 + (i % 5),
                versionScore: i * 10,
                formatScore: 300 + (i % 3) * 100))
            .ToArray();

        var first = DeduplicationEngine.Deduplicate(candidates);
        for (var run = 0; run < 20; run++)
        {
            var result = DeduplicationEngine.Deduplicate(candidates);
            Assert.Equal(first.Count, result.Count);
            for (var g = 0; g < first.Count; g++)
            {
                Assert.Equal(first[g].GameKey, result[g].GameKey);
                Assert.Equal(first[g].Winner.MainPath, result[g].Winner.MainPath);
                Assert.Equal(first[g].Losers.Count, result[g].Losers.Count);
            }
        }
    }

    // --- Full Category Ranking: Bios > Game > NonGame > Junk > Unknown ---

    [Fact]
    public void SelectWinner_BiosCategory_BeatsGameDespiteHigherScores()
    {
        var game = new RomCandidate
        {
            MainPath = "g.zip", GameKey = "key",
            Category = FileCategory.Game,
            RegionScore = 1000, VersionScore = 1000, FormatScore = 1000
        };
        var bios = new RomCandidate
        {
            MainPath = "b.zip", GameKey = "key",
            Category = FileCategory.Bios,
            RegionScore = 1, VersionScore = 1, FormatScore = 1
        };

        var winner = DeduplicationEngine.SelectWinner(new[] { game, bios });
        Assert.Equal(FileCategory.Game, winner!.Category);
        // Game rank (5) > Bios rank (4), so Game wins
    }

    [Fact]
    public void SelectWinner_GameCategory_BeatsNonGame()
    {
        var nonGame = new RomCandidate
        {
            MainPath = "n.zip", GameKey = "key",
            Category = FileCategory.NonGame,
            RegionScore = 1000, VersionScore = 1000, FormatScore = 1000
        };
        var game = new RomCandidate
        {
            MainPath = "g.zip", GameKey = "key",
            Category = FileCategory.Game,
            RegionScore = 1, VersionScore = 1, FormatScore = 1
        };

        var winner = DeduplicationEngine.SelectWinner(new[] { nonGame, game });
        Assert.Equal(FileCategory.Game, winner!.Category);
    }

    [Fact]
    public void SelectWinner_NonGameCategory_BeatsJunk()
    {
        var junk = new RomCandidate
        {
            MainPath = "j.zip", GameKey = "key",
            Category = FileCategory.Junk,
            RegionScore = 1000, VersionScore = 1000
        };
        var nonGame = new RomCandidate
        {
            MainPath = "n.zip", GameKey = "key",
            Category = FileCategory.NonGame,
            RegionScore = 1, VersionScore = 1
        };

        var winner = DeduplicationEngine.SelectWinner(new[] { junk, nonGame });
        Assert.Equal(FileCategory.NonGame, winner!.Category);
    }

    [Fact]
    public void SelectWinner_JunkCategory_BeatsUnknown()
    {
        var unknown = new RomCandidate
        {
            MainPath = "u.zip", GameKey = "key",
            Category = FileCategory.Unknown,
            RegionScore = 1000
        };
        var junk = new RomCandidate
        {
            MainPath = "j.zip", GameKey = "key",
            Category = FileCategory.Junk,
            RegionScore = 1
        };

        var winner = DeduplicationEngine.SelectWinner(new[] { unknown, junk });
        Assert.Equal(FileCategory.Junk, winner!.Category);
    }
}
