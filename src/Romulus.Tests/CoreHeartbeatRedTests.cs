using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Core.Conversion;
using Romulus.Core.Deduplication;
using Romulus.Core.GameKeys;
using Romulus.Core.Regions;
using Romulus.Core.Scoring;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// RED PHASE: Targeted failing tests exposing real gaps in core domain logic.
/// Each test documents what is broken TODAY and why it matters.
///
/// These tests are NOT aspirational — they probe confirmed production gaps.
/// No production code changes — only test code.
///
/// Gap discovery method: Diagnostic probing against actual production behavior.
/// </summary>
public class CoreHeartbeatRedTests
{
    // ── Helper ────────────────────────────────────────────────────────────
    private static RomCandidate MakeCandidate(
        string mainPath = "game.zip",
        string gameKey = "game",
        FileCategory category = FileCategory.Game,
        int regionScore = 0,
        int versionScore = 0,
        int formatScore = 0,
        int headerScore = 0,
        int completenessScore = 0,
        long sizeTieBreakScore = 0,
        bool datMatch = false,
        string region = "UNKNOWN",
        string consoleKey = "SNES",
        string extension = ".zip")
        => new()
        {
            MainPath = mainPath,
            GameKey = gameKey,
            Category = category,
            RegionScore = regionScore,
            VersionScore = versionScore,
            FormatScore = formatScore,
            HeaderScore = headerScore,
            CompletenessScore = completenessScore,
            SizeTieBreakScore = sizeTieBreakScore,
            DatMatch = datMatch,
            Region = region,
            ConsoleKey = consoleKey,
            Extension = extension
        };

    // ═══════════════════════════════════════════════════════════════════════
    // GAP 1: BIOS DETECTION — Expanded console patterns missing
    // Production file: FileClassifier.cs → RxBios regex
    //
    // ROOT CAUSE: RxBios only lists gba|dc|psx|ps1|ps2|nds patterns.
    //   Console-specific BIOS files for Saturn, Genesis, Lynx, N64, Jaguar,
    //   TurboGrafx, Atari, CPS2, NeoGeo are not in the regex.
    //   Confirmed: all return Game with confidence=75, reason=game-default.
    //
    // IMPACT: These BIOS files enter game groups, pollute deduplication,
    //   may be incorrectly deduplicated against actual games, and are not
    //   isolated via __BIOS__ GameKey prefix.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// GAP: Console-specific _bios filenames NOT in the hardcoded RxBios pattern list.
    /// Today: all return Game (confidence=75, reason=game-default).
    /// Expected: Bios.
    /// </summary>
    [Theory]
    [InlineData("saturn_bios")]
    [InlineData("sega_cd_bios")]
    [InlineData("genesis_bios")]
    [InlineData("n64_bios")]
    [InlineData("lynx_bios")]
    [InlineData("jaguar_bios")]
    [InlineData("turbografx_bios")]
    [InlineData("atari7800_bios")]
    [InlineData("megadrive_bios")]
    [InlineData("segacd_bios")]
    public void Classify_ExpandedConsoleBiosNames_ShouldBeBios(string name)
    {
        // GAP: RxBios only has \b(?:gba|dc|psx|ps1|ps2|nds?)_bios\b
        // These console prefixes are missing.
        var result = FileClassifier.Analyze(name);
        Assert.Equal(FileCategory.Bios, result.Category);
    }

    /// <summary>
    /// GAP: BIOS filenames without any "bios" substring at all.
    /// "syscard3" is the TurboGrafx-16 system card (BIOS equivalent).
    /// Today: Game (confidence=75, reason=game-default).
    /// Expected: Bios.
    ///
    /// TESTABILITY FINDING: This requires a lookup table, not just regex.
    /// </summary>
    [Theory]
    [InlineData("syscard3")]
    [InlineData("syscard1")]
    [InlineData("syscard2")]
    public void Classify_SystemCardBios_ShouldBeBios(string name)
    {
        var result = FileClassifier.Analyze(name);
        Assert.Equal(FileCategory.Bios, result.Category);
    }

    /// <summary>
    /// GAP: "BIOS" as a word in the middle of a filename (not at start).
    /// RxBios has ^\s*bios pattern (anchored to START) but no word-boundary-
    /// based match for "BIOS" appearing after a console prefix.
    /// Today: Game (confidence=75, reason=game-default).
    /// Expected: Bios.
    /// </summary>
    [Theory]
    [InlineData("CPS2 BIOS")]
    [InlineData("NeoGeo BIOS")]
    [InlineData("Sega Saturn BIOS")]
    [InlineData("Atari Jaguar BIOS")]
    [InlineData("3DO BIOS")]
    public void Classify_BiosWordInMiddle_ShouldBeBios(string name)
    {
        // RxBios only matches "bios" at START of string, in tags (bios) / [bios],
        // or as console_bios pattern. Free-standing "BIOS" word after prefix = miss.
        var result = FileClassifier.Analyze(name);
        Assert.Equal(FileCategory.Bios, result.Category);
    }

    /// <summary>
    /// GAP CONSEQUENCE: Undetected BIOS pollutes game dedupe groups.
    /// When BIOS is misclassified as Game, CandidateFactory does NOT apply
    /// the __BIOS__ prefix → BIOS file groups with real games → wrong winner.
    /// </summary>
    [Fact]
    public void EndToEnd_UndetectedBios_PollutesGameGroup()
    {
        // saturn_bios is NOT detected as BIOS today
        var biosClassification = FileClassifier.Analyze("saturn_bios");

        // CandidateFactory uses classification category for key isolation
        var biosKey = GameKeyNormalizer.Normalize("saturn_bios");
        var candidate = CandidateFactory.Create(
            normalizedPath: "C:/roms/saturn_bios.bin",
            extension: ".bin", sizeBytes: 524288,
            category: biosClassification.Category,
            gameKey: biosKey, region: "JP",
            regionScore: 0, formatScore: 600, versionScore: 0,
            headerScore: 0, completenessScore: 0, sizeTieBreakScore: 0,
            datMatch: false, consoleKey: "SATURN");

        // If correctly classified as Bios, key would be "__BIOS__saturn_bios"
        // Today: classification is Game → key is "saturn_bios" → no isolation
        Assert.StartsWith("__BIOS__", candidate.GameKey);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GAP 2: GAMEKEY — Fullwidth parentheses not folded to ASCII
    // Production file: GameKeyNormalizer.cs → AsciiFold
    //
    // ROOT CAUSE: AsciiFold handles diacritics and ligatures but does NOT
    //   convert fullwidth characters (U+FF08 '（', U+FF09 '）') to ASCII '()'.
    //   Tag removal regex uses ASCII parentheses only.
    //
    // IMPACT: Japanese ROM filenames with fullwidth parens retain region tags
    //   in the GameKey → different key than same ROM with ASCII parens →
    //   same game is NOT grouped → no deduplication.
    //
    // Confirmed: "Game（Europe）" → key "game（europe）" ≠ "game"
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// GAP: Fullwidth parentheses in Japanese filenames are not folded.
    /// "Game（Europe）" produces key "game（europe）" instead of "game".
    /// Same game with ASCII parens produces key "game".
    /// </summary>
    [Fact]
    public void GameKey_FullwidthParentheses_ShouldProduceSameKeyAsAscii()
    {
        var asciiKey = GameKeyNormalizer.Normalize("Game (Europe)");
        var fullwidthKey = GameKeyNormalizer.Normalize("Game\uFF08Europe\uFF09");
        Assert.Equal(asciiKey, fullwidthKey);
    }

    /// <summary>
    /// GAP: Fullwidth brackets also not folded.
    /// Tags like ［！］ (fullwidth verified dump) are not stripped.
    /// </summary>
    [Fact]
    public void GameKey_FullwidthBrackets_ShouldBeStripped()
    {
        var asciiKey = GameKeyNormalizer.Normalize("Game [!]");
        var fullwidthKey = GameKeyNormalizer.Normalize("Game\uFF3B\uFF01\uFF3D");
        Assert.Equal(asciiKey, fullwidthKey);
    }

    /// <summary>
    /// GAP: Mix of fullwidth and ASCII parens in one filename.
    /// Realistic for some Japanese dump sets.
    /// </summary>
    [Fact]
    public void GameKey_MixedFullwidthAsciiParens_ShouldNormalize()
    {
        var normalKey = GameKeyNormalizer.Normalize("Puzzle Game (Japan)");
        var mixedKey = GameKeyNormalizer.Normalize("Puzzle Game\uFF08Japan\uFF09");
        Assert.Equal(normalKey, mixedKey);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GAP 3: VERSION SCORER — Dotted revisions lose precision
    // Production file: VersionScorer.cs → revision scoring branch
    //
    // ROOT CAUSE: Dotted revision like "1.1" falls through to RxLeadingDigits
    //   which only parses the first number ("1" → score 10). The fractional
    //   part ".1" vs ".2" is silently discarded.
    //
    // IMPACT: Rev 1.1 and Rev 1.2 get identical score (10).
    //   Winner selection between two revisions of the same game is decided
    //   by alphabetical path tiebreak instead of actual revision.
    //   This violates deterministic revision-based winner selection.
    //
    // Confirmed: "Game (Rev 1.1)" → 10, "Game (Rev 1.2)" → 10
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// GAP: Rev 1.2 must score higher than Rev 1.1.
    /// Today: both score 10 (only leading digit parsed).
    /// </summary>
    [Fact]
    public void VersionScore_DottedRevision_HigherMinor_ShouldScoreHigher()
    {
        var scorer = new VersionScorer();
        var score11 = scorer.GetVersionScore("Game (Rev 1.1)");
        var score12 = scorer.GetVersionScore("Game (Rev 1.2)");
        Assert.True(score12 > score11,
            $"Rev 1.2 ({score12}) must score higher than Rev 1.1 ({score11})");
    }

    /// <summary>
    /// GAP: Rev 1.10 must score higher than Rev 1.9 (semantic versioning).
    /// Today: both fall to RxLeadingDigits → "1" → score 10.
    /// </summary>
    [Fact]
    public void VersionScore_DottedRevision_TwoDigitMinor_ShouldScoreHigher()
    {
        var scorer = new VersionScorer();
        var score19 = scorer.GetVersionScore("Game (Rev 1.9)");
        var score110 = scorer.GetVersionScore("Game (Rev 1.10)");
        Assert.True(score110 > score19,
            $"Rev 1.10 ({score110}) must score higher than Rev 1.9 ({score19})");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GAP 4: REGION DETECTION — PAL60 not recognized
    // Production file: RegionDetector.cs → DefaultOrderedRules
    //
    // ROOT CAUSE: PAL is matched but PAL60 is not — the regex is
    //   \((?:Europe|EUR|PAL)\) which requires exact "PAL" without suffix.
    //
    // IMPACT: PAL60 ROMs (common for retro EU games) get UNKNOWN region →
    //   wrong region scoring → potentially wrong winner selection.
    //
    // Confirmed: "Game (PAL60)" → UNKNOWN
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// GAP: PAL60 is a valid EU region indicator.
    /// Today: UNKNOWN. Expected: EU.
    /// </summary>
    [Fact]
    public void Region_PAL60_ShouldBeEU()
    {
        Assert.Equal("EU", RegionDetector.GetRegionTag("Game (PAL60)"));
    }

    /// <summary>
    /// GAP: NTSC-U and NTSC-J with alternative punctuation/spacing.
    /// "NTSC - U" (spaces around dash) may fail token parsing.
    /// </summary>
    [Theory]
    [InlineData("Game (NTSC - J)", "JP")]
    public void Region_NtscWithSpaces_ShouldResolve(string name, string expected)
    {
        Assert.Equal(expected, RegionDetector.GetRegionTag(name));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GAP 5: END-TO-END DEDUPE — Undetected BIOS breaks winner selection
    // Production files: FileClassifier.cs, CandidateFactory.cs,
    //                   DeduplicationEngine.cs
    //
    // SCENARIO: Two candidates share a GameKey. One is actually a BIOS
    //   file (saturn_bios) but misclassified as Game. The BIOS may win
    //   over the real game if its scores are higher.
    //
    // IMPACT: User keeps BIOS instead of game, loses the actual game ROM.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// GAP CHAIN: Misclassified BIOS + same GameKey as game →
    /// BIOS may win over actual game in deduplication.
    /// The correct behavior: BIOS gets __BIOS__ prefix → separate group.
    ///
    /// Today: saturn_bios = Game → shares group with any game named "saturn_bios"
    /// (or aliased to same key) → wrong winner possible.
    /// </summary>
    [Fact]
    public void EndToEnd_BiosMisclassification_CausesGroupPollution()
    {
        // Step 1: saturn_bios is NOT classified as BIOS
        var biosResult = FileClassifier.Analyze("saturn_bios");

        // This MUST be Bios to trigger proper isolation
        Assert.Equal(FileCategory.Bios, biosResult.Category);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GAP 6: GAMEKEY — CJK ideographs and special characters
    // Production file: GameKeyNormalizer.cs → AsciiFold
    //
    // CJK ideographs (Kanji, Hanzi) go through NFC normalization but
    // are NOT converted to any ASCII form. This is expected — but mixed
    // CJK+ASCII filenames may produce inconsistent keys depending on
    // the position of CJK characters relative to tags.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// GAP: Fullwidth digits in version tags are not parsed.
    /// "Game（ｖ１．０）" → version tag not stripped (fullwidth v, 1, .)
    /// </summary>
    [Fact]
    public void GameKey_FullwidthDigitsInVersionTag_ShouldBeStripped()
    {
        var asciiKey = GameKeyNormalizer.Normalize("Game (v1.0)");
        // U+FF56=ｖ  U+FF11=１  U+FF0E=．  U+FF10=０
        var fullwidthKey = GameKeyNormalizer.Normalize(
            "Game\uFF08\uFF56\uFF11\uFF0E\uFF10\uFF09");
        Assert.Equal(asciiKey, fullwidthKey);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GAP 7: FORMAT SCORING — Missing modern formats
    // Production file: FormatScorer.cs → GetFormatScore
    //
    // Several modern emulation formats are not in the scoring table.
    // They return 300 (unknown default) instead of their proper tier.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// GAP: .wux (Wii U compressed disc image) returns 300 (unknown).
    /// Should be in the 650+ range with other compressed disc formats.
    /// </summary>
    [Fact]
    public void FormatScore_Wux_ShouldNotBeDefault()
    {
        var score = FormatScorer.GetFormatScore(".wux");
        Assert.True(score > 300,
            $".wux (Wii U compressed) scored {score}, should be > 300");
    }

    /// <summary>
    /// GAP: .rpx (Wii U executable) returns 300 (unknown).
    /// Should be in the 600+ range with console-native formats.
    /// </summary>
    [Fact]
    public void FormatScore_Rpx_ShouldNotBeDefault()
    {
        var score = FormatScorer.GetFormatScore(".rpx");
        Assert.True(score > 300,
            $".rpx (Wii U) scored {score}, should be > 300");
    }

    /// <summary>
    /// GAP: .wud (Wii U raw disc) returns 300 (unknown).
    /// Should be in the ISO-tier range.
    /// </summary>
    [Fact]
    public void FormatScore_Wud_ShouldNotBeDefault()
    {
        var score = FormatScorer.GetFormatScore(".wud");
        Assert.True(score > 300,
            $".wud (Wii U disc) scored {score}, should be > 300");
    }

    /// <summary>
    /// GAP: .lnx (Lynx ROM format) returns 300 (unknown).
    /// Should be in the console-native range (600).
    /// </summary>
    [Fact]
    public void FormatScore_Lnx_ShouldNotBeDefault()
    {
        var score = FormatScorer.GetFormatScore(".lnx");
        Assert.True(score > 300,
            $".lnx (Atari Lynx) scored {score}, should be > 300");
    }

    /// <summary>
    /// GAP: .jag (Jaguar ROM format) returns 300 (unknown).
    /// </summary>
    [Fact]
    public void FormatScore_Jag_ShouldNotBeDefault()
    {
        var score = FormatScorer.GetFormatScore(".jag");
        Assert.True(score > 300,
            $".jag (Atari Jaguar) scored {score}, should be > 300");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GAP 8: REGION DETECTION — Edge cases in multi-region
    // Production file: RegionDetector.cs
    //
    // Multi-language tags like (Fr,De) are treated as WORLD even though
    // both languages are EU. This may cause incorrect region scoring.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// DEBATE: (Fr,De) Both French and German = EU-only languages.
    /// Today: WORLD. Argued: Should be EU since both languages are EU.
    ///
    /// This is a design decision, but the current WORLD assignment
    /// gives these ROMs the WORLD region score (500) instead of EU
    /// score (potentially 1000 for preferred region). If user prefers
    /// EU, the (Fr,De) ROM is penalized vs a (Europe) ROM despite
    /// being the same region.
    /// </summary>
    [Theory]
    [InlineData("Game (Fr,De)")]
    [InlineData("Game (De,Fr,Es,It)")]
    [InlineData("Game (Nl,De,Fr)")]
    public void Region_AllEuLanguages_ShouldBeEU_NotWorld(string name)
    {
        Assert.Equal("EU", RegionDetector.GetRegionTag(name));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GAP 10: DEDUPE — Large groups determinism under permutation
    // Production file: DeduplicationEngine.cs → Deduplicate
    //
    // With many candidates in one group, input order should NOT affect
    // winner. But with complex tie-breaking chains, subtle ordering
    // dependencies could exist.
    // ═══════════════════════════════════════════════════════════════════════

}
