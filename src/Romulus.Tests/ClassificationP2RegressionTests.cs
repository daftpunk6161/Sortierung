using System.IO.Compression;
using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Core.Scoring;
using Xunit;

namespace Romulus.Tests;

public sealed class ClassificationP2RegressionTests
{
    [Fact]
    public void AmbiguousExtension_SingleSource_CanReachReview()
    {
        var result = HypothesisResolver.Resolve([
            new DetectionHypothesis("MD", 55, DetectionSource.AmbiguousExtension, "ext=.bin")
        ]);

        Assert.Equal("MD", result.ConsoleKey);
        Assert.Equal(55, result.Confidence);
        Assert.Equal(SortDecision.Blocked, result.SortDecision);
    }

    [Fact]
    public void ArchiveDetection_EqualSizeEntries_IsDeterministic()
    {
        var detector = new ConsoleDetector([
            new ConsoleInfo("NES", "NES", false, [".nes"], [], ["nes"]),
            new ConsoleInfo("SNES", "SNES", false, [".sfc"], [], ["snes"])
        ]);

        var zipPath = Path.Combine(Path.GetTempPath(), $"archive_tie_{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var nes = archive.CreateEntry("b_game.nes");
                using (var stream = nes.Open())
                {
                    stream.Write(new byte[] { 1, 2, 3, 4 });
                }

                var snes = archive.CreateEntry("a_game.sfc");
                using (var stream = snes.Open())
                {
                    stream.Write(new byte[] { 5, 6, 7, 8 });
                }
            }

            var first = detector.DetectByArchiveContent(zipPath, ".zip");
            var second = detector.DetectByArchiveContent(zipPath, ".zip");
            var third = detector.DetectByArchiveContent(zipPath, ".zip");

            Assert.Equal("SNES", first);
            Assert.Equal(first, second);
            Assert.Equal(second, third);
        }
        finally
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }

    [Fact]
    public void SwitchPackages_SizeTieBreak_PrefersLargerCanonicalDump()
    {
        var smaller = FormatScorer.GetSizeTieBreakScore(null, ".nsp", 2_000_000);
        var larger = FormatScorer.GetSizeTieBreakScore(null, ".nsp", 4_000_000);

        Assert.True(larger > smaller);
    }

    // ── F-09 Regression: Zero hypotheses → Unknown ──────────────────────────

    [Fact]
    public void Resolve_ZeroHypotheses_ReturnsUnknown()
    {
        // Regression guard for F-09:
        // When no detection method matches anything, the resolver must return
        // ConsoleDetectionResult.Unknown — not throw, not return a random console.
        var result = HypothesisResolver.Resolve([]);

        Assert.Equal("UNKNOWN", result.ConsoleKey);
        Assert.Equal(0, result.Confidence);
        Assert.Equal(SortDecision.Unknown, result.SortDecision);
        Assert.Equal(DecisionClass.Unknown, result.DecisionClass);
        Assert.Empty(result.Hypotheses);
        Assert.False(result.HasConflict);
        Assert.False(result.HasHardEvidence);
    }

    [Fact]
    public void Resolve_ZeroHypotheses_MatchEvidence_TierIsUnknown()
    {
        // The MatchEvidence on an empty-hypothesis result must reflect Tier4_Unknown.
        var result = HypothesisResolver.Resolve([]);

        Assert.NotNull(result.MatchEvidence);
        Assert.Equal(EvidenceTier.Tier4_Unknown, result.MatchEvidence!.Tier);
        Assert.Equal(MatchKind.None, result.MatchEvidence.PrimaryMatchKind);
    }

    // ── F-04 Invariant: Soft-only multi-source confidence capped at 79 ────────

    [Theory]
    [InlineData(DetectionSource.FolderName, DetectionSource.FilenameKeyword)]
    [InlineData(DetectionSource.FolderName, DetectionSource.AmbiguousExtension)]
    [InlineData(DetectionSource.FilenameKeyword, DetectionSource.AmbiguousExtension)]
    public void Resolve_SoftOnlyMultiSource_ConfidenceNeverExceedsSoftOnlyCap(
        DetectionSource source1, DetectionSource source2)
    {
        // Both sources are Tier3 (WeakHeuristic). Even with multi-source agreement bonus,
        // ComputeSoftOnlyCap hard-clamps at 79. Tier3 routes to Blocked regardless.
        var result = HypothesisResolver.Resolve([
            new DetectionHypothesis("NES", source1.ConfidenceRating(), source1, $"source={source1}"),
            new DetectionHypothesis("NES", source2.ConfidenceRating(), source2, $"source={source2}"),
        ]);

        Assert.Equal("NES", result.ConsoleKey);
        Assert.True(result.Confidence <= 79,
            $"Soft-only multi-source must not exceed SoftOnlyCap(79). Got {result.Confidence}.");
        Assert.Equal(SortDecision.Blocked, result.SortDecision);
    }
}
