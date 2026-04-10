using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Core.Deduplication;
using Romulus.Core.Regions;
using Romulus.Core.Scoring;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Invariant tests for the core decision pipeline:
/// GameKey → Grouping → Winner Selection → Tie-Breaker → Region → Scoring.
/// Each test protects a documented invariant that must never be silently broken.
/// </summary>
public sealed class CoreDecisionInvariantTests
{
    // ══════════════════════════════════════════════════════════════
    // HELPER
    // ══════════════════════════════════════════════════════════════

    private static RomCandidate C(
        string mainPath = "game.zip",
        string gameKey = "game",
        FileCategory category = FileCategory.Game,
        int completenessScore = 0,
        bool datMatch = false,
        int regionScore = 0,
        int headerScore = 0,
        long versionScore = 0,
        int formatScore = 0,
        long sizeTieBreakScore = 0)
        => new()
        {
            MainPath = mainPath,
            GameKey = gameKey,
            Category = category,
            CompletenessScore = completenessScore,
            DatMatch = datMatch,
            RegionScore = regionScore,
            HeaderScore = headerScore,
            VersionScore = versionScore,
            FormatScore = formatScore,
            SizeTieBreakScore = sizeTieBreakScore,
        };

    // ══════════════════════════════════════════════════════════════
    // SECTION 1: PRIORITY CASCADE — strict ordering of criteria
    // Invariant: each criterion only decides when ALL higher criteria tie.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Cascade_CompletenessBeats_DatMatch()
    {
        var high = C(mainPath: "a.zip", completenessScore: 100, datMatch: false);
        var low = C(mainPath: "b.zip", completenessScore: 0, datMatch: true);
        Assert.Equal("a.zip", DeduplicationEngine.SelectWinner([low, high])!.MainPath);
    }

    [Fact]
    public void Cascade_DatMatchBeats_RegionScore_WhenCompletenessTied()
    {
        var dat = C(mainPath: "a.zip", completenessScore: 50, datMatch: true, regionScore: 100);
        var noDat = C(mainPath: "b.zip", completenessScore: 50, datMatch: false, regionScore: 1000);
        Assert.Equal("a.zip", DeduplicationEngine.SelectWinner([noDat, dat])!.MainPath);
    }

    [Fact]
    public void Cascade_RegionScoreBeats_HeaderScore_WhenDatTied()
    {
        var highRegion = C(mainPath: "a.zip", datMatch: true, regionScore: 1000, headerScore: -10);
        var highHeader = C(mainPath: "b.zip", datMatch: true, regionScore: 100, headerScore: 10);
        Assert.Equal("a.zip", DeduplicationEngine.SelectWinner([highHeader, highRegion])!.MainPath);
    }

    [Fact]
    public void Cascade_HeaderScoreBeats_VersionScore_WhenRegionTied()
    {
        var highHeader = C(mainPath: "a.zip", regionScore: 500, headerScore: 10, versionScore: 0);
        var highVersion = C(mainPath: "b.zip", regionScore: 500, headerScore: -10, versionScore: 9999);
        Assert.Equal("a.zip", DeduplicationEngine.SelectWinner([highVersion, highHeader])!.MainPath);
    }

    [Fact]
    public void Cascade_VersionScoreBeats_FormatScore_WhenHeaderTied()
    {
        var highVer = C(mainPath: "a.zip", headerScore: 0, versionScore: 500, formatScore: 300);
        var highFmt = C(mainPath: "b.zip", headerScore: 0, versionScore: 10, formatScore: 850);
        Assert.Equal("a.zip", DeduplicationEngine.SelectWinner([highFmt, highVer])!.MainPath);
    }

    [Fact]
    public void Cascade_FormatScoreBeats_SizeTieBreak_WhenVersionTied()
    {
        var highFmt = C(mainPath: "a.zip", versionScore: 100, formatScore: 850, sizeTieBreakScore: -999);
        var highSize = C(mainPath: "b.zip", versionScore: 100, formatScore: 300, sizeTieBreakScore: 999_999);
        Assert.Equal("a.zip", DeduplicationEngine.SelectWinner([highSize, highFmt])!.MainPath);
    }

    [Fact]
    public void Cascade_SizeTieBreakBeats_Alphabetical_WhenFormatTied()
    {
        var largeZ = C(mainPath: "z.zip", formatScore: 700, sizeTieBreakScore: 999_999);
        var smallA = C(mainPath: "a.zip", formatScore: 700, sizeTieBreakScore: 1);
        Assert.Equal("z.zip", DeduplicationEngine.SelectWinner([smallA, largeZ])!.MainPath);
    }

    // ══════════════════════════════════════════════════════════════
    // SECTION 2: CATEGORY RANK — strict chain GAME > BIOS > NonGame > Junk > Unknown
    // ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(FileCategory.Game, FileCategory.Bios)]
    [InlineData(FileCategory.Game, FileCategory.NonGame)]
    [InlineData(FileCategory.Game, FileCategory.Junk)]
    [InlineData(FileCategory.Game, FileCategory.Unknown)]
    [InlineData(FileCategory.Bios, FileCategory.NonGame)]
    [InlineData(FileCategory.Bios, FileCategory.Junk)]
    [InlineData(FileCategory.Bios, FileCategory.Unknown)]
    [InlineData(FileCategory.NonGame, FileCategory.Junk)]
    [InlineData(FileCategory.NonGame, FileCategory.Unknown)]
    [InlineData(FileCategory.Junk, FileCategory.Unknown)]
    public void CategoryRank_HigherAlwaysWins_RegardlessOfScores(FileCategory higher, FileCategory lower)
    {
        // Lower-ranked candidate has maximum scores; higher-ranked has minimum.
        var winner = C(mainPath: "win.zip", category: higher,
            completenessScore: 0, regionScore: 1, formatScore: 1, versionScore: 0);
        var loser = C(mainPath: "lose.zip", category: lower,
            completenessScore: 100, regionScore: 1000, formatScore: 850, versionScore: 9999,
            datMatch: true, headerScore: 10, sizeTieBreakScore: 999_999);

        var result = DeduplicationEngine.SelectWinner([loser, winner]);
        Assert.Equal(higher, result!.Category);
    }

    [Fact]
    public void CategoryRank_SameCategory_FallsToScoreCascade()
    {
        // Both GAME — higher scores should win.
        var highScore = C(mainPath: "b.zip", category: FileCategory.Game, regionScore: 1000);
        var lowScore = C(mainPath: "a.zip", category: FileCategory.Game, regionScore: 100);
        Assert.Equal("b.zip", DeduplicationEngine.SelectWinner([lowScore, highScore])!.MainPath);
    }

    // ══════════════════════════════════════════════════════════════
    // SECTION 3: DEDUPLICATE PERMUTATION DETERMINISM
    // Invariant: group output order and winners are input-order-independent.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Deduplicate_AllPermutationsOf5Candidates_SameGroupsAndWinners()
    {
        var candidates = new[]
        {
            C(mainPath: "mario_eu.chd", gameKey: "mario", regionScore: 1000, formatScore: 850),
            C(mainPath: "mario_us.iso", gameKey: "mario", regionScore: 999, formatScore: 700),
            C(mainPath: "mario_jp.zip", gameKey: "mario", regionScore: 998, formatScore: 500),
            C(mainPath: "zelda_eu.chd", gameKey: "zelda", regionScore: 1000, formatScore: 850),
            C(mainPath: "zelda_us.iso", gameKey: "zelda", regionScore: 999, formatScore: 700),
        };

        var reference = DeduplicationEngine.Deduplicate(candidates);
        Assert.Equal(2, reference.Count);

        foreach (var perm in GetPermutations(candidates))
        {
            var result = DeduplicationEngine.Deduplicate(perm.ToArray());
            Assert.Equal(reference.Count, result.Count);
            for (int i = 0; i < reference.Count; i++)
            {
                Assert.Equal(reference[i].GameKey, result[i].GameKey);
                Assert.Equal(reference[i].Winner.MainPath, result[i].Winner.MainPath);
                Assert.Equal(reference[i].Losers.Count, result[i].Losers.Count);
                for (int j = 0; j < reference[i].Losers.Count; j++)
                    Assert.Equal(reference[i].Losers[j].MainPath, result[i].Losers[j].MainPath);
            }
        }
    }

    [Fact]
    public void Deduplicate_ThreeGroupsShuffled_DeterministicGroupOrder()
    {
        var candidates = new[]
        {
            C(mainPath: "c_zelda.zip", gameKey: "zelda", regionScore: 500),
            C(mainPath: "a_mario.zip", gameKey: "mario", regionScore: 500),
            C(mainPath: "b_sonic.zip", gameKey: "sonic", regionScore: 500),
        };

        var result = DeduplicationEngine.Deduplicate(candidates);

        // Groups must be sorted alphabetically by GameKey regardless of input order.
        Assert.Equal("mario", result[0].GameKey);
        Assert.Equal("sonic", result[1].GameKey);
        Assert.Equal("zelda", result[2].GameKey);
    }

    // ══════════════════════════════════════════════════════════════
    // SECTION 4: SIZE TIEBREAK POLARITY
    // Invariant: disc=larger wins, cartridge=smaller wins, set=larger wins.
    // ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(".iso", 1_000_000, 500_000)]   // disc: larger wins
    [InlineData(".bin", 1_000_000, 500_000)]   // disc: larger wins
    [InlineData(".chd", 1_000_000, 500_000)]   // disc: larger wins
    public void SizeTieBreak_DiscFormat_LargerFileWins(string ext, long largeSize, long smallSize)
    {
        var large = FormatScorer.GetSizeTieBreakScore(null, ext, largeSize);
        var small = FormatScorer.GetSizeTieBreakScore(null, ext, smallSize);
        Assert.True(large > small, $"Disc {ext}: larger ({large}) should beat smaller ({small})");
        Assert.True(large > 0, "Disc scores must be positive");
    }

    [Theory]
    [InlineData(".nes", 100_000, 200_000)]
    [InlineData(".sfc", 100_000, 200_000)]
    [InlineData(".gba", 100_000, 200_000)]
    public void SizeTieBreak_CartridgeFormat_SmallerFileWins(string ext, long smallSize, long largeSize)
    {
        var small = FormatScorer.GetSizeTieBreakScore(null, ext, smallSize);
        var large = FormatScorer.GetSizeTieBreakScore(null, ext, largeSize);
        Assert.True(small > large, $"Cartridge {ext}: smaller ({small}) should beat larger ({large})");
        Assert.True(small < 0, "Cartridge scores must be negative (negated size)");
    }

    [Theory]
    [InlineData("M3USET")]
    [InlineData("GDISET")]
    [InlineData("CUESET")]
    [InlineData("CCDSET")]
    [InlineData("DOSDIR")]
    public void SizeTieBreak_SetType_LargerFileWins(string type)
    {
        var large = FormatScorer.GetSizeTieBreakScore(type, ".cue", 1_000_000);
        var small = FormatScorer.GetSizeTieBreakScore(type, ".cue", 500_000);
        Assert.True(large > small, $"Set {type}: larger should beat smaller");
        Assert.True(large > 0, "Set scores must be positive");
    }

    // ══════════════════════════════════════════════════════════════
    // SECTION 5: REGION SCORE FALLBACKS
    // Invariant: WORLD=500, UNKNOWN=100, unlisted=200, prefer-order=1000-idx.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void RegionScore_WorldFallback_Is500()
    {
        Assert.Equal(500, FormatScorer.GetRegionScore("WORLD", ["EU", "US", "JP"]));
    }

    [Fact]
    public void RegionScore_UnknownFallback_Is100()
    {
        Assert.Equal(100, FormatScorer.GetRegionScore("UNKNOWN", ["EU", "US", "JP"]));
    }

    [Fact]
    public void RegionScore_UnlistedRegion_Is200()
    {
        Assert.Equal(200, FormatScorer.GetRegionScore("KR", ["EU", "US", "JP"]));
    }

    [Fact]
    public void RegionScore_PreferOrder_DescendingFromTop()
    {
        var prefs = new[] { "EU", "US", "JP" };
        Assert.Equal(1000, FormatScorer.GetRegionScore("EU", prefs));
        Assert.Equal(999, FormatScorer.GetRegionScore("US", prefs));
        Assert.Equal(998, FormatScorer.GetRegionScore("JP", prefs));
    }

    [Fact]
    public void RegionScore_PreferOrder_CaseInsensitive()
    {
        Assert.Equal(1000, FormatScorer.GetRegionScore("eu", ["EU", "US"]));
        Assert.Equal(999, FormatScorer.GetRegionScore("us", ["EU", "US"]));
    }

    // ══════════════════════════════════════════════════════════════
    // SECTION 6: LANGUAGE MULTI-TAG BOUNDARIES
    // Invariant: EU-only langs → EU; en-included or non-EU → WORLD.
    // ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Game (fr,de)", "EU")]
    [InlineData("Game (de,fr,es,it)", "EU")]
    [InlineData("Game (nl,de,fr)", "EU")]
    [InlineData("Game (pl,cs,hu)", "EU")]
    public void LanguageMultiTag_AllEuLanguages_ReturnsEU(string name, string expected)
    {
        Assert.Equal(expected, RegionDetector.GetRegionTag(name));
    }

    [Theory]
    [InlineData("Game (en,fr)", "WORLD")]
    [InlineData("Game (en,de,fr)", "WORLD")]
    [InlineData("Game (ja,ko)", "WORLD")]
    [InlineData("Game (ru,pl)", "WORLD")]
    [InlineData("Game (zh,ja,ko)", "WORLD")]
    public void LanguageMultiTag_NonEuLanguageIncluded_ReturnsWORLD(string name, string expected)
    {
        Assert.Equal(expected, RegionDetector.GetRegionTag(name));
    }

    [Fact]
    public void LanguageTag_SingleEn_NotResolvedAsRegion()
    {
        // Single "en" is a language code, NOT a region token → UNKNOWN.
        Assert.Equal("UNKNOWN", RegionDetector.GetRegionTag("Game (en)"));
    }

    [Fact]
    public void LanguageTag_SingleDe_ResolvesAsEU_ViaCountryCode()
    {
        // Single "de" is BOTH a language code AND a country code (Germany).
        // Token parsing maps "de" → EU via RegionTokenMap.
        Assert.Equal("EU", RegionDetector.GetRegionTag("Game (de)"));
    }

    // ══════════════════════════════════════════════════════════════
    // SECTION 7: CROSS-GROUP WINNER CONFLICT (SEC-DEDUP)
    // Invariant: A file that wins one group cannot be a loser in another.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Deduplicate_CrossGroupWinnerConflict_LoserRemoved()
    {
        // Same MainPath appears in two groups:
        // Group "alpha": shared_file wins (higher score) → shared_file is a winner path
        // Group "beta": shared_file is a loser (lower score than beta_winner)
        // SEC-DEDUP must remove shared_file from beta's losers.
        var sharedFile = C(mainPath: "shared.zip", gameKey: "alpha", regionScore: 1000);
        var alphaLoser = C(mainPath: "alpha_loser.zip", gameKey: "alpha", regionScore: 100);
        var betaWinner = C(mainPath: "beta_winner.zip", gameKey: "beta", regionScore: 999);
        var sharedAsLoser = C(mainPath: "shared.zip", gameKey: "beta", regionScore: 500);

        var results = DeduplicationEngine.Deduplicate([sharedFile, alphaLoser, betaWinner, sharedAsLoser]);

        var alphaGroup = results.First(g => g.GameKey == "alpha");
        var betaGroup = results.First(g => g.GameKey == "beta");

        Assert.Equal("shared.zip", alphaGroup.Winner.MainPath);
        Assert.Equal("beta_winner.zip", betaGroup.Winner.MainPath);

        // SEC-DEDUP invariant: shared.zip must NOT appear as loser in beta group
        Assert.DoesNotContain(betaGroup.Losers,
            l => string.Equals(l.MainPath, "shared.zip", StringComparison.OrdinalIgnoreCase));
        Assert.True(betaGroup.CrossGroupFilteredCount > 0);
    }

    [Fact]
    public void Deduplicate_NoCrossGroupConflict_AllLosersRetained()
    {
        var candidates = new[]
        {
            C(mainPath: "a1.zip", gameKey: "alpha", regionScore: 1000),
            C(mainPath: "a2.zip", gameKey: "alpha", regionScore: 500),
            C(mainPath: "b1.zip", gameKey: "beta", regionScore: 1000),
            C(mainPath: "b2.zip", gameKey: "beta", regionScore: 500),
        };

        var results = DeduplicationEngine.Deduplicate(candidates);

        Assert.Equal(2, results.Count);
        Assert.All(results, g =>
        {
            Assert.Single(g.Losers);
            Assert.Equal(0, g.CrossGroupFilteredCount);
        });
    }

    // ══════════════════════════════════════════════════════════════
    // SECTION 8: ALPHABETICAL TIEBREAKER — CASE SENSITIVITY
    // Invariant: dual OrdinalIgnoreCase + Ordinal ensures determinism.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Tiebreaker_CaseDifference_DeterministicPick()
    {
        // "Game.zip" (uppercase G) vs "game.zip" (lowercase g)
        // OrdinalIgnoreCase: both equal → falls to Ordinal
        // Ordinal: 'G' (0x47) < 'g' (0x67) → "Game.zip" sorts first
        var upper = C(mainPath: "Game.zip");
        var lower = C(mainPath: "game.zip");

        var result = DeduplicationEngine.SelectWinner([lower, upper]);
        Assert.Equal("Game.zip", result!.MainPath);
    }

    [Fact]
    public void Tiebreaker_PunctuationDifference_DeterministicPick()
    {
        // "_game.zip" vs "game.zip"
        // OrdinalIgnoreCase: 'G' (0x47) < '_' (0x5F) → "game.zip" sorts first.
        // (OrdinalIgnoreCase compares ToUpper: 'g'→'G'=0x47 vs '_'=0x5F)
        var underscore = C(mainPath: "_game.zip");
        var plain = C(mainPath: "game.zip");

        var result = DeduplicationEngine.SelectWinner([plain, underscore]);
        Assert.Equal("game.zip", result!.MainPath);
    }

    // ══════════════════════════════════════════════════════════════
    // SECTION 9: BIOS ISOLATION VIA CANDIDATEFACTORY
    // Invariant: CandidateFactory prefixes __BIOS__ to BIOS keys.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void CandidateFactory_BiosKey_NeverCollides_WithGameKey()
    {
        var bios = CandidateFactory.Create(
            normalizedPath: "bios.bin", extension: ".bin", sizeBytes: 1,
            category: FileCategory.Bios, gameKey: "playstation",
            region: "UNKNOWN", regionScore: 0, formatScore: 0, versionScore: 0,
            headerScore: 0, completenessScore: 0, sizeTieBreakScore: 0,
            datMatch: false, consoleKey: "PSX");

        var game = CandidateFactory.Create(
            normalizedPath: "game.bin", extension: ".bin", sizeBytes: 1,
            category: FileCategory.Game, gameKey: "playstation",
            region: "EU", regionScore: 0, formatScore: 0, versionScore: 0,
            headerScore: 0, completenessScore: 0, sizeTieBreakScore: 0,
            datMatch: false, consoleKey: "PSX");

        // BIOS key must be prefixed; GAME key must NOT be prefixed.
        Assert.StartsWith("__BIOS__", bios.GameKey);
        Assert.DoesNotContain("__BIOS__", game.GameKey);
        Assert.NotEqual(bios.GameKey, game.GameKey);
    }

    [Fact]
    public void CandidateFactory_BiosIsolation_PreventsGroupCollision()
    {
        var bios = CandidateFactory.Create(
            normalizedPath: "bios.bin", extension: ".bin", sizeBytes: 1,
            category: FileCategory.Bios, gameKey: "console",
            region: "UNKNOWN", regionScore: 1000, formatScore: 850, versionScore: 500,
            headerScore: 10, completenessScore: 100, sizeTieBreakScore: 999_999,
            datMatch: true, consoleKey: "PSX");

        var game = CandidateFactory.Create(
            normalizedPath: "game.bin", extension: ".bin", sizeBytes: 1,
            category: FileCategory.Game, gameKey: "console",
            region: "EU", regionScore: 100, formatScore: 300, versionScore: 0,
            headerScore: 0, completenessScore: 0, sizeTieBreakScore: 1,
            datMatch: false, consoleKey: "PSX");

        var results = DeduplicationEngine.Deduplicate([bios, game]);

        // BIOS and GAME must be in separate groups (different effective GameKeys).
        Assert.Equal(2, results.Count);
    }

    // ══════════════════════════════════════════════════════════════
    // SECTION 10: EMPTY/NULL GAMEKEY ROBUSTNESS
    // Invariant: various empty-like GameKeys never create groups.
    // ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData(" \t\n ")]
    public void Deduplicate_VariousEmptyKeys_NeverGrouped(string emptyKey)
    {
        var candidates = new[]
        {
            new RomCandidate { MainPath = "empty.zip", GameKey = emptyKey, RegionScore = 999 },
            new RomCandidate { MainPath = "real.zip", GameKey = "real_game", RegionScore = 500 },
        };

        var results = DeduplicationEngine.Deduplicate(candidates);
        Assert.Single(results);
        Assert.Equal("real_game", results[0].GameKey);
    }

    [Fact]
    public void Deduplicate_AllEmptyKeys_ReturnsEmptyResults()
    {
        var candidates = new[]
        {
            new RomCandidate { MainPath = "a.zip", GameKey = "" },
            new RomCandidate { MainPath = "b.zip", GameKey = "   " },
        };

        var results = DeduplicationEngine.Deduplicate(candidates);
        Assert.Empty(results);
    }

    // ══════════════════════════════════════════════════════════════
    // SECTION 11: SUM INVARIANT — no silent data loss
    // Invariant: winners + losers == non-empty-key input count.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Deduplicate_SumInvariant_StressTest()
    {
        // 50 candidates across 10 groups with 5 members each.
        var candidates = Enumerable.Range(0, 50)
            .Select(i => C(
                mainPath: $"file_{i:D3}.zip",
                gameKey: $"group_{i / 5}",
                regionScore: 1000 - (i % 5)))
            .ToArray();

        var results = DeduplicationEngine.Deduplicate(candidates);

        int totalAccountedFor = results.Sum(g => 1 + g.Losers.Count);
        Assert.Equal(50, totalAccountedFor);
        Assert.Equal(10, results.Count);
        Assert.All(results, g => Assert.Equal(4, g.Losers.Count));
    }

    // ══════════════════════════════════════════════════════════════
    // SECTION 12: VERSION SCORE LANGUAGE BONUS ADDITIVITY
    // Invariant: en=+50+langs*5, de=+25, both stack.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void VersionScore_EnAndDe_BothBonusesStack()
    {
        var scorer = new VersionScorer();
        var enOnly = scorer.GetVersionScore("Game (en)");
        var deOnly = scorer.GetVersionScore("Game (de)");
        var enDe = scorer.GetVersionScore("Game (en,de)");

        // en alone: 50 + 1*5 = 55
        Assert.Equal(55, enOnly);
        // de alone: 25
        Assert.Equal(25, deOnly);
        // en,de: 50 (en base) + 2*5 (lang count) + 25 (de bonus) = 85
        Assert.Equal(85, enDe);
    }

    [Fact]
    public void VersionScore_MultiLangBonus_ScalesWithCount()
    {
        var scorer = new VersionScorer();
        var two = scorer.GetVersionScore("Game (en,fr)");
        var four = scorer.GetVersionScore("Game (en,fr,de,es)");

        // (en,fr): 50 + 2*5 = 60
        Assert.Equal(60, two);
        // (en,fr,de,es): 50 + 4*5 + 25(de) = 95
        Assert.Equal(95, four);
    }

    // ══════════════════════════════════════════════════════════════
    // SECTION 13: HEADER VARIANT SCORING
    // Invariant: headered=+10, headerless=-10, neutral=0.
    // ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("C:\\roms\\headered", "game.zip", 10)]
    [InlineData("C:\\roms\\headerless", "game.zip", -10)]
    [InlineData("C:\\roms\\normal", "game.zip", 0)]
    [InlineData("C:\\roms", "game (headered).zip", 10)]
    [InlineData("C:\\roms", "game (headerless).zip", -10)]
    public void HeaderScore_DetectsHint_InRootAndPath(string root, string path, int expected)
    {
        Assert.Equal(expected, FormatScorer.GetHeaderVariantScore(root, path));
    }

    // ══════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════

    private static IEnumerable<IEnumerable<T>> GetPermutations<T>(T[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }
        for (int i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, idx) => idx != i).ToArray();
            foreach (var perm in GetPermutations(rest))
                yield return new[] { items[i] }.Concat(perm);
        }
    }
}
