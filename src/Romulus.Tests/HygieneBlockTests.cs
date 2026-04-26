using System.Reflection;
using System.Text.RegularExpressions;
using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Core.GameKeys;
using Romulus.Core.Regions;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for the audit hygiene block (F7, F14, F19, F21, F22).
/// Each test pins behavior that was previously fragile/obscure.
/// </summary>
public sealed class HygieneBlockTests
{
    // ── F19 ─────────────────────────────────────────────────────────────
    // EmptyMultiRegionPattern must match NOTHING. Original pattern was the
    // obscure literal "$a" which works by accident (end-anchor + char). The
    // canonical empty-matcher is "(?!)" (always-fail negative lookahead).
    // Behavior contract: never matches, regardless of input.

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("$a")]
    [InlineData("Game (USA, Europe).zip")]
    [InlineData("anything")]
    public void F19_EmptyMultiRegionPattern_NeverMatches(string input)
    {
        var pattern = (Regex)typeof(RegionDetector)
            .GetField("EmptyMultiRegionPattern", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        Assert.False(pattern.IsMatch(input),
            $"EmptyMultiRegionPattern matched '{input}' but must always be empty.");
    }

    // ── F14 ─────────────────────────────────────────────────────────────
    // Empty-key fallback must use the deterministic sentinel "__empty_key_<hash>"
    // and must NOT silently re-introduce the unstripped baseName as the key
    // (which would defeat the purpose of tag-stripping and cause two unrelated
    // titles to collide on identical tag patterns).

    [Fact]
    public void F14_Normalize_AllTagsStripped_UsesEmptyKeySentinel_NotRawBaseName()
    {
        // Pattern that strips EVERYTHING (any non-empty input).
        var stripAll = new Regex(".+", RegexOptions.Compiled);
        var key = GameKeyNormalizer.Normalize(
            "(USA)",
            tagPatterns: [stripAll],
            alwaysAliasMap: new Dictionary<string, string>());

        Assert.StartsWith("__empty_key_", key);
        // Must NOT be the raw baseName lowercased — that would defeat tag-stripping.
        Assert.NotEqual("(usa)", key);
    }

    [Fact]
    public void F14_Normalize_TwoDifferentInputs_AllStripped_ProduceDifferentSentinels()
    {
        // Two different inputs that both strip to nothing must still produce
        // distinct sentinel keys (deterministic per-input hash suffix).
        var stripAll = new Regex(".+", RegexOptions.Compiled);
        var keyA = GameKeyNormalizer.Normalize("Alpha",
            tagPatterns: [stripAll], alwaysAliasMap: new Dictionary<string, string>());
        var keyB = GameKeyNormalizer.Normalize("Beta",
            tagPatterns: [stripAll], alwaysAliasMap: new Dictionary<string, string>());

        Assert.StartsWith("__empty_key_", keyA);
        Assert.StartsWith("__empty_key_", keyB);
        Assert.NotEqual(keyA, keyB);
    }

    // ── F21 ─────────────────────────────────────────────────────────────
    // DOS-family detection must not depend on the literal key "DOS".
    // Common synonyms (MSDOS, MS-DOS, PC-DOS, PCDOS) must trigger the same
    // metadata-tag stripping. Unknown / unrelated keys must NOT trigger it.

    [Theory]
    [InlineData("DOS", true)]
    [InlineData("dos", true)]
    [InlineData("MSDOS", true)]
    [InlineData("MS-DOS", true)]
    [InlineData("PC-DOS", true)]
    [InlineData("PCDOS", true)]
    [InlineData(" DOS ", true)]
    [InlineData("PSX", false)]
    [InlineData("AMIGA", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void F21_IsDosFamilyConsole_RecognisesAllVariants(string? consoleType, bool expected)
    {
        var method = typeof(GameKeyNormalizer)
            .GetMethod("IsDosFamilyConsole", BindingFlags.NonPublic | BindingFlags.Static)!;
        var actual = (bool)method.Invoke(null, [consoleType])!;
        Assert.Equal(expected, actual);
    }

    // ── F7 ──────────────────────────────────────────────────────────────
    // When the AMBIGUOUS branch is entered BECAUSE both competing candidates
    // have hard evidence (winnerHasHard == runnerHasHard == true), the
    // resulting MatchEvidence must preserve HasHardEvidence=true rather
    // than collapsing it to false. Otherwise downstream consumers cannot
    // distinguish a hard-vs-hard ambiguity (genuine conflict) from a
    // soft-only ambiguity (no real evidence at all).

    [Fact]
    public void F7_Ambiguous_TwoHardEvidenceCandidates_PreservesHasHardEvidence()
    {
        // Two candidates both at Tier1 hard evidence (CartridgeHeader),
        // similar confidence → AMBIGUOUS branch fires.
        var result = HypothesisResolver.Resolve([
            new DetectionHypothesis("NES",
                DetectionSource.CartridgeHeader.ConfidenceRating(),
                DetectionSource.CartridgeHeader, "header=NES"),
            new DetectionHypothesis("SNES",
                DetectionSource.CartridgeHeader.ConfidenceRating(),
                DetectionSource.CartridgeHeader, "header=SNES"),
        ]);

        Assert.Equal("AMBIGUOUS", result.ConsoleKey);
        Assert.True(result.HasConflict);
        // F7 contract: hard-vs-hard ambiguity must report hard evidence.
        Assert.True(result.HasHardEvidence,
            "AMBIGUOUS with two hard-evidence candidates must preserve HasHardEvidence=true.");
        Assert.True(result.MatchEvidence!.HasHardEvidence,
            "MatchEvidence.HasHardEvidence must mirror the result-level flag.");
        // IsSoftOnly must be the inverse of HasHardEvidence.
        Assert.False(result.IsSoftOnly,
            "Hard-vs-hard ambiguity must not be marked IsSoftOnly.");
    }

    [Fact]
    public void F7_Ambiguous_TwoSoftEvidenceCandidates_RemainsSoftOnly()
    {
        // Both candidates are Tier2 soft (UniqueExtension). AMBIGUOUS branch
        // still allowed because both at <= Tier2; result must remain
        // HasHardEvidence=false because UniqueExtension is not hard evidence.
        var result = HypothesisResolver.Resolve([
            new DetectionHypothesis("NES",
                DetectionSource.UniqueExtension.ConfidenceRating(),
                DetectionSource.UniqueExtension, "ext=.nes"),
            new DetectionHypothesis("SNES",
                DetectionSource.UniqueExtension.ConfidenceRating(),
                DetectionSource.UniqueExtension, "ext=.sfc"),
        ]);

        Assert.Equal("AMBIGUOUS", result.ConsoleKey);
        Assert.False(result.HasHardEvidence,
            "AMBIGUOUS with two soft-only candidates must report HasHardEvidence=false.");
        Assert.True(result.IsSoftOnly);
    }

    // ── F15 ─────────────────────────────────────────────────────────────
    // Archive-content detection must be fully deterministic regardless of
    // how the underlying ZIP central directory orders its entries. The
    // existing ClassificationP2RegressionTests.ArchiveDetection_EqualSizeEntries_IsDeterministic
    // covers same-size ties; this guard adds a stronger cross-priority case
    // and verifies multiple repeated reads return the identical winner even
    // when entry order in the archive is reversed between writes.

    [Fact]
    public void F15_Archive_Detection_IsDeterministic_AcrossReversedEntryOrder()
    {
        var detector = new ConsoleDetector([
            new ConsoleInfo("NES", "NES", false, [".nes"], [], ["nes"]),
            new ConsoleInfo("SNES", "SNES", false, [".sfc"], [], ["snes"])
        ]);

        var zipA = Path.Combine(Path.GetTempPath(), $"f15_a_{Guid.NewGuid():N}.zip");
        var zipB = Path.Combine(Path.GetTempPath(), $"f15_b_{Guid.NewGuid():N}.zip");
        try
        {
            // zipA: NES first then SNES.
            using (var archive = System.IO.Compression.ZipFile.Open(
                zipA, System.IO.Compression.ZipArchiveMode.Create))
            {
                using (var s = archive.CreateEntry("game.nes").Open()) s.Write([1, 2, 3, 4]);
                using (var s = archive.CreateEntry("game.sfc").Open()) s.Write([5, 6, 7, 8]);
            }
            // zipB: same files, reversed write order.
            using (var archive = System.IO.Compression.ZipFile.Open(
                zipB, System.IO.Compression.ZipArchiveMode.Create))
            {
                using (var s = archive.CreateEntry("game.sfc").Open()) s.Write([5, 6, 7, 8]);
                using (var s = archive.CreateEntry("game.nes").Open()) s.Write([1, 2, 3, 4]);
            }

            var detectedA = detector.DetectByArchiveContent(zipA, ".zip");
            var detectedB = detector.DetectByArchiveContent(zipB, ".zip");

            Assert.NotNull(detectedA);
            // Reversing entry write order must not change the winner.
            Assert.Equal(detectedA, detectedB);
        }
        finally
        {
            if (File.Exists(zipA)) File.Delete(zipA);
            if (File.Exists(zipB)) File.Delete(zipB);
        }
    }
}
