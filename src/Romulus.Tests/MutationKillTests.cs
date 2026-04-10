using Romulus.Contracts.Models;
using Romulus.Core.Deduplication;
using Romulus.Core.GameKeys;
using Romulus.Core.Regions;
using Romulus.Core.Rules;
using Romulus.Core.Scoring;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Mutation-kill tests ensuring specific code paths cannot be removed
/// without test failures. Covers DeduplicationEngine, GameKeyNormalizer,
/// RegionDetector, RuleEngine, FormatScorer, and VersionScorer.
/// </summary>
public sealed class MutationKillTests
{
    // =========================================================================
    //  TEST-MUT-DE: DeduplicationEngine Mutations
    // =========================================================================

    private static RomCandidate C(
        string mainPath = "game.zip", string gameKey = "game",
        int regionScore = 0, int versionScore = 0, int formatScore = 0,
        int headerScore = 0, int completenessScore = 0,
        long sizeTieBreakScore = 0, bool datMatch = false) => new()
    {
        MainPath = mainPath, GameKey = gameKey,
        RegionScore = regionScore, VersionScore = versionScore,
        FormatScore = formatScore, HeaderScore = headerScore,
        CompletenessScore = completenessScore,
        SizeTieBreakScore = sizeTieBreakScore, DatMatch = datMatch
    };

    // TEST-MUT-DE-01: HeaderScore isolated from RegionScore
    [Fact]
    public void HeaderScore_BreaksTie_WhenRegionEqual()
    {
        var withHeader = C(mainPath: "a.zip", regionScore: 500, headerScore: 10);
        var noHeader = C(mainPath: "b.zip", regionScore: 500, headerScore: 0);
        Assert.Same(withHeader, DeduplicationEngine.SelectWinner(new[] { noHeader, withHeader }));
    }

    // TEST-MUT-DE-02: Case-insensitive GameKey grouping
    [Fact]
    public void Deduplicate_CaseInsensitiveGrouping()
    {
        var candidates = new[]
        {
            C(mainPath: "a.zip", gameKey: "mario", regionScore: 1000),
            C(mainPath: "b.zip", gameKey: "MARIO", regionScore: 999),
        };
        var results = DeduplicationEngine.Deduplicate(candidates);
        Assert.Single(results); // same group
        Assert.Single(results[0].Losers);
    }

    // TEST-MUT-DE-03: Priority chain order matters
    [Fact]
    public void PriorityChain_CompletenessBeforeRegion()
    {
        var complete = C(mainPath: "a.zip", completenessScore: 100, regionScore: 500);
        var regional = C(mainPath: "b.zip", completenessScore: 0, regionScore: 1000);
        Assert.Same(complete, DeduplicationEngine.SelectWinner(new[] { regional, complete }));
    }

    [Fact]
    public void PriorityChain_DatMatchBeforeRegion()
    {
        var dat = C(mainPath: "a.zip", datMatch: true, regionScore: 500);
        var noDat = C(mainPath: "b.zip", datMatch: false, regionScore: 1000);
        Assert.Same(dat, DeduplicationEngine.SelectWinner(new[] { noDat, dat }));
    }

    [Fact]
    public void PriorityChain_RegionBeforeHeader()
    {
        var highRegion = C(mainPath: "a.zip", regionScore: 1000, headerScore: 0);
        var highHeader = C(mainPath: "b.zip", regionScore: 500, headerScore: 100);
        Assert.Same(highRegion, DeduplicationEngine.SelectWinner(new[] { highHeader, highRegion }));
    }

    [Fact]
    public void PriorityChain_HeaderBeforeVersion()
    {
        var highHeader = C(mainPath: "a.zip", headerScore: 100, versionScore: 0);
        var highVer = C(mainPath: "b.zip", headerScore: 0, versionScore: 1000);
        Assert.Same(highHeader, DeduplicationEngine.SelectWinner(new[] { highVer, highHeader }));
    }

    [Fact]
    public void PriorityChain_VersionBeforeFormat()
    {
        var highVer = C(mainPath: "a.zip", versionScore: 100, formatScore: 0);
        var highFmt = C(mainPath: "b.zip", versionScore: 0, formatScore: 1000);
        Assert.Same(highVer, DeduplicationEngine.SelectWinner(new[] { highFmt, highVer }));
    }

    [Fact]
    public void PriorityChain_FormatBeforeSize()
    {
        var highFmt = C(mainPath: "a.zip", formatScore: 100, sizeTieBreakScore: 0);
        var highSize = C(mainPath: "b.zip", formatScore: 0, sizeTieBreakScore: 999999);
        Assert.Same(highFmt, DeduplicationEngine.SelectWinner(new[] { highSize, highFmt }));
    }

    // TEST-MUT-DE-04: Size tiebreak
    [Fact]
    public void SizeTieBreak_LargerWins_WhenAllElseEqual()
    {
        var large = C(mainPath: "a.zip", sizeTieBreakScore: 999999);
        var small = C(mainPath: "b.zip", sizeTieBreakScore: 1);
        Assert.Same(large, DeduplicationEngine.SelectWinner(new[] { small, large }));
    }

    // TEST-MUT-DE: Empty GameKey filtered out
    [Fact]
    public void Deduplicate_EmptyGameKey_Filtered()
    {
        var candidates = new[]
        {
            C(mainPath: "a.zip", gameKey: ""),
            C(mainPath: "b.zip", gameKey: "  "),
            C(mainPath: "c.zip", gameKey: "mario"),
        };
        var results = DeduplicationEngine.Deduplicate(candidates);
        Assert.Single(results);
        Assert.Equal("mario", results[0].GameKey);
    }

    // =========================================================================
    //  TEST-MUT-GK: GameKeyNormalizer Mutations
    // =========================================================================

    // TEST-MUT-GK-01: Headered tag stripped
    [Fact]
    public void GK_Headered_Stripped()
    {
        var h = GameKeyNormalizer.Normalize("Game (Headered)");
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, h);
    }

    // TEST-MUT-GK-02: Program tag stripped
    [Fact]
    public void GK_Program_Stripped()
    {
        var p = GameKeyNormalizer.Normalize("Game (Program)");
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, p);
    }

    // TEST-MUT-GK-03: Hack tag stripped
    [Fact]
    public void GK_Hack_Stripped()
    {
        var h = GameKeyNormalizer.Normalize("Game (Hack)");
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, h);
    }

    // TEST-MUT-GK-04: Unlicensed tag stripped
    [Fact]
    public void GK_Unlicensed_Stripped()
    {
        var u = GameKeyNormalizer.Normalize("Game (Unlicensed)");
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, u);
    }

    // TEST-MUT-GK-05: BIOS tag stripped
    [Fact]
    public void GK_Bios_Stripped()
    {
        var b = GameKeyNormalizer.Normalize("System (BIOS)");
        var plain = GameKeyNormalizer.Normalize("System");
        Assert.Equal(plain, b);
    }

    // TEST-MUT-GK-06: Virtual Console tag stripped
    [Fact]
    public void GK_VirtualConsole_Stripped()
    {
        var vc = GameKeyNormalizer.Normalize("Game (Virtual Console)");
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, vc);
    }

    // TEST-MUT-GK-07: Reprint tag stripped
    [Fact]
    public void GK_Reprint_Stripped()
    {
        var r = GameKeyNormalizer.Normalize("Game (Reprint)");
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, r);
    }

    // TEST-MUT-GK-08: EDC tag stripped
    [Fact]
    public void GK_EDC_Stripped()
    {
        var edc = GameKeyNormalizer.Normalize("Game (EDC)");
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, edc);
    }

    // TEST-MUT-GK-09: LibCrypt tag stripped
    [Fact]
    public void GK_LibCrypt_Stripped()
    {
        var lc = GameKeyNormalizer.Normalize("Game (LibCrypt)");
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, lc);
    }

    // TEST-MUT-GK-10: Sector count tag stripped
    [Fact]
    public void GK_SectorCount_Stripped()
    {
        var sc = GameKeyNormalizer.Normalize("Game (2S)");
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, sc);
    }

    // TEST-MUT-GK-11: Made in Japan tag stripped
    [Fact]
    public void GK_MadeInJapan_Stripped()
    {
        var m = GameKeyNormalizer.Normalize("Game (Made in Japan)");
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, m);
    }

    // TEST-MUT-GK-12: Not for Resale tag stripped
    [Fact]
    public void GK_NotForResale_Stripped()
    {
        var nfr = GameKeyNormalizer.Normalize("Game (Not for Resale)");
        var plain = GameKeyNormalizer.Normalize("Game");
        Assert.Equal(plain, nfr);
    }

    // TEST-MUT-GK-13: Empty key returns deterministic fallback
    [Fact]
    public void GK_EmptyInput_ReturnsFallback()
    {
        var k1 = GameKeyNormalizer.Normalize("");
        var k2 = GameKeyNormalizer.Normalize("");
        Assert.StartsWith("__empty_key_", k1);
        Assert.StartsWith("__empty_key_", k2);
        Assert.Equal(k1, k2); // deterministic: same input → same key
    }

    // TEST-MUT-GK-14: DOS mode removes trailing bracket tags
    [Fact]
    public void GK_DosMode_StripsTrailingBrackets()
    {
        var patterns = Array.Empty<System.Text.RegularExpressions.Regex>();
        var aliases = new Dictionary<string, string>();
        var dosResult = GameKeyNormalizer.Normalize("Doom II [666]", patterns, aliases, consoleType: "DOS");
        var normalResult = GameKeyNormalizer.Normalize("Doom II [666]", patterns, aliases, consoleType: null);
        // DOS mode should strip trailing brackets that normal mode doesn't
        Assert.NotEqual(dosResult, normalResult);
    }

    // =========================================================================
    //  TEST-MUT-RD: RegionDetector Mutations
    // =========================================================================

    // TEST-MUT-RD-01: Two-letter codes work
    [Theory]
    [InlineData("Game (DE)", "EU")]
    [InlineData("Game (FR)", "EU")]
    [InlineData("Game (ES)", "EU")]
    [InlineData("Game (IT)", "EU")]
    [InlineData("Game (NL)", "EU")]
    [InlineData("Game (SE)", "EU")]
    [InlineData("Game (AU)", "AU")]
    public void TwoLetterCodes_MapCorrectly(string input, string expected)
    {
        Assert.Equal(expected, RegionDetector.GetRegionTag(input));
    }

    // TEST-MUT-RD-02: Return value is always uppercase
    [Theory]
    [InlineData("Game (europe)")]
    [InlineData("Game (usa)")]
    [InlineData("Game (japan)")]
    [InlineData("Random text")]
    public void RegionTag_AlwaysUppercase(string input)
    {
        var result = RegionDetector.GetRegionTag(input);
        Assert.Equal(result, result.ToUpperInvariant());
    }

    // =========================================================================
    //  TEST-MUT-RE: RuleEngine Mutations
    // =========================================================================

    private static ClassificationRule MakeRule(
        string name, string action, int priority,
        params RuleCondition[] conditions) => new()
    {
        Name = name, Action = action, Priority = priority,
        Conditions = conditions, Enabled = true
    };

    private static IReadOnlyDictionary<string, string> Item(params (string k, string v)[] pairs)
        => pairs.ToDictionary(p => p.k, p => p.v, StringComparer.OrdinalIgnoreCase);

    // TEST-MUT-RE-01: Equal priority → alphabetical tiebreaker by Name
    [Fact]
    public void RE_EqualPriority_AlphabeticalByName()
    {
        var rules = new[]
        {
            MakeRule("z-rule", "keep", 10, new RuleCondition { Field = "X", Op = "eq", Value = "1" }),
            MakeRule("a-rule", "junk", 10, new RuleCondition { Field = "X", Op = "eq", Value = "1" })
        };
        var result = RuleEngine.Evaluate(rules, Item(("X", "1")));
        Assert.True(result.Matched);
        Assert.Equal("a-rule", result.RuleName);
    }

    // TEST-MUT-RE-02: Missing field defaults to empty string
    [Fact]
    public void RE_MissingField_DefaultsToEmpty()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Missing", Op = "eq", Value = "" });
        Assert.True(RuleEngine.TestRule(rule, Item(("Other", "x"))));
    }

    // TEST-MUT-RE-03: gt boundary — equal is not greater
    [Fact]
    public void RE_Gt_EqualIsNotGreater()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Size", Op = "gt", Value = "100" });
        Assert.False(RuleEngine.TestRule(rule, Item(("Size", "100"))));
        Assert.True(RuleEngine.TestRule(rule, Item(("Size", "101"))));
    }

    // TEST-MUT-RE-04: lt boundary — equal is not less
    [Fact]
    public void RE_Lt_EqualIsNotLess()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Size", Op = "lt", Value = "100" });
        Assert.False(RuleEngine.TestRule(rule, Item(("Size", "100"))));
        Assert.True(RuleEngine.TestRule(rule, Item(("Size", "99"))));
    }

    // TEST-MUT-RE-05: Validation catches empty field in condition
    [Fact]
    public void RE_Validation_EmptyField_Error()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "", Op = "eq", Value = "x" });
        var result = RuleEngine.ValidateSyntax(rule);
        Assert.False(result.Valid);
    }

    // TEST-MUT-RE-06: Reason is set in match result
    [Fact]
    public void RE_MatchResult_ContainsReason()
    {
        var rule = new ClassificationRule
        {
            Name = "r", Action = "junk", Priority = 1, Enabled = true, Reason = "test reason",
            Conditions = [new RuleCondition { Field = "X", Op = "eq", Value = "1" }]
        };
        var result = RuleEngine.Evaluate(new[] { rule }, Item(("X", "1")));
        Assert.Equal("test reason", result.Reason);
    }

    // TEST-MUT-RE-07: Non-parseable numeric → gt/lt return false
    [Fact]
    public void RE_NonParseable_GtLt_ReturnFalse()
    {
        var gt = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Size", Op = "gt", Value = "100" });
        Assert.False(RuleEngine.TestRule(gt, Item(("Size", "not-a-number"))));

        var lt = MakeRule("r2", "junk", 1,
            new RuleCondition { Field = "Size", Op = "lt", Value = "abc" });
        Assert.False(RuleEngine.TestRule(lt, Item(("Size", "50"))));
    }

    // TEST-MUT-RE-08: Unknown operator returns false
    [Fact]
    public void RE_UnknownOperator_ReturnsFalse()
    {
        var rule = new ClassificationRule
        {
            Name = "r", Action = "junk", Priority = 1, Enabled = true,
            Conditions = [new RuleCondition { Field = "X", Op = "unknown_op", Value = "1" }]
        };
        Assert.False(RuleEngine.TestRule(rule, Item(("X", "1"))));
    }

    // =========================================================================
    //  TEST-MUT-FS: FormatScorer Mutations
    // =========================================================================

    // TEST-MUT-FS-01: Absolute score values for key formats
    [Theory]
    [InlineData(".chd", 850)]
    [InlineData(".iso", 700)]
    [InlineData(".cso", 680)]
    [InlineData(".pbp", 680)]
    [InlineData(".rvz", 680)]
    [InlineData(".wbfs", 650)]
    [InlineData(".zip", 500)]
    [InlineData(".7z", 480)]
    [InlineData(".rar", 400)]
    [InlineData(".unknown", 300)]
    public void FS_AbsoluteScores(string ext, int expected)
    {
        Assert.Equal(expected, FormatScorer.GetFormatScore(ext));
    }

    // TEST-MUT-FS-02: Set type sizes — CUESET larger beats smaller
    [Fact]
    public void FS_SetType_SizeTieBreak_LargerBetter()
    {
        var large = FormatScorer.GetSizeTieBreakScore("CUESET", ".bin", 1_000_000);
        var small = FormatScorer.GetSizeTieBreakScore("CUESET", ".bin", 100);
        Assert.True(large > small);
    }

    // TEST-MUT-FS-03: IsDiscExtension
    [Theory]
    [InlineData(".iso", true)]
    [InlineData(".chd", true)]
    [InlineData(".nes", false)]
    [InlineData(".zip", false)]
    public void FS_IsDiscExtension(string ext, bool expected)
    {
        Assert.Equal(expected, FormatScorer.IsDiscExtension(ext));
    }

    // TEST-MUT-FS-04: Region score — non-preferred, non-WORLD, non-UNKNOWN
    [Fact]
    public void FS_RegionScore_OtherRegion_200()
    {
        Assert.Equal(200, FormatScorer.GetRegionScore("BR", Array.Empty<string>()));
        Assert.Equal(200, FormatScorer.GetRegionScore("KR", Array.Empty<string>()));
    }

    // TEST-MUT-FS-05: Cartridge format — smaller = better (negative score)
    [Fact]
    public void FS_CartridgeSizeTieBreak_SmallerBetter()
    {
        var large = FormatScorer.GetSizeTieBreakScore(null, ".nes", 1_000_000);
        var small = FormatScorer.GetSizeTieBreakScore(null, ".nes", 100);
        Assert.True(small > large); // negative, so smaller abs = higher
    }

    // =========================================================================
    //  TEST-MUT-VS: VersionScorer Mutations
    // =========================================================================

    private readonly VersionScorer _vs = new();

    // TEST-MUT-VS-01: Revision score — higher revision = higher score
    [Fact]
    public void VS_RevisionScore_Ordering()
    {
        var revA = _vs.GetVersionScore("Game (Rev A)");
        var revB = _vs.GetVersionScore("Game (Rev B)");
        var revC = _vs.GetVersionScore("Game (Rev C)");
        Assert.True(revC > revB);
        Assert.True(revB > revA);
        Assert.True(revA > 0);
    }

    // TEST-MUT-VS-02: German language bonus
    [Fact]
    public void VS_GermanBonus()
    {
        var withDe = _vs.GetVersionScore("Game (de)");
        var noLang = _vs.GetVersionScore("Game");
        Assert.True(withDe > noLang);
    }

    // TEST-MUT-VS-03: English language bonus
    [Fact]
    public void VS_EnglishBonus()
    {
        var withEn = _vs.GetVersionScore("Game (en)");
        var noLang = _vs.GetVersionScore("Game");
        Assert.True(withEn > noLang);
    }

    // TEST-MUT-VS-04: Multi-language count multiplier
    [Fact]
    public void VS_MultiLangBonus_IncreasesWithCount()
    {
        var two = _vs.GetVersionScore("Game (en,fr)");
        var four = _vs.GetVersionScore("Game (en,fr,de,es)");
        Assert.True(four > two);
    }

    // TEST-MUT-VS: Verified dump exact bonus value
    [Fact]
    public void VS_VerifiedDump_Exactly500()
    {
        var verified = _vs.GetVersionScore("Game [!]");
        var plain = _vs.GetVersionScore("Game");
        Assert.Equal(500, verified - plain);
    }
}
