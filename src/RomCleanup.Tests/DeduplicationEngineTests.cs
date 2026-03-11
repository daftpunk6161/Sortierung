using RomCleanup.Contracts.Models;
using RomCleanup.Core.Deduplication;
using Xunit;

namespace RomCleanup.Tests;

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
}
