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
}
