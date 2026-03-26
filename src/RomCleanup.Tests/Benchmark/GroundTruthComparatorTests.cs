using RomCleanup.Core.Classification;
using RomCleanup.Tests.Benchmark.Models;
using Xunit;

namespace RomCleanup.Tests.Benchmark;

public sealed class GroundTruthComparatorTests
{
    [Fact]
    public void Compare_JunkWithMatchingConsole_ReturnsJunkClassified()
    {
        var entry = CreateEntry("junk-1", "Junk", "NES");
        var actual = new ConsoleDetectionResult("NES", 80, [], false, null);

        var result = GroundTruthComparator.Compare(entry, actual);

        Assert.Equal(BenchmarkVerdict.JunkClassified, result.Verdict);
    }

    [Fact]
    public void Compare_JunkBlockedAsUnknown_ReturnsTrueNegative()
    {
        var entry = CreateEntry("junk-2", "Junk", "NES");

        var result = GroundTruthComparator.Compare(entry, ConsoleDetectionResult.Unknown);

        Assert.Equal(BenchmarkVerdict.TrueNegative, result.Verdict);
    }

    [Fact]
    public void Compare_UnknownCategoryWithConsole_ReturnsFalsePositive()
    {
        var entry = CreateEntry("unk-1", "Unknown", null);
        var actual = new ConsoleDetectionResult(
            "NES",
            85,
            [new DetectionHypothesis("NES", 90, DetectionSource.CartridgeHeader, "iNES")],
            false,
            null);

        var result = GroundTruthComparator.Compare(entry, actual);

        Assert.Equal(BenchmarkVerdict.FalsePositive, result.Verdict);
    }

    [Fact]
    public void Compare_UnknownCategoryWithSoftOnlyDetection_IsBlockedAsTrueNegative()
    {
        var entry = CreateEntry("unk-2", "Unknown", null);
        var actual = new ConsoleDetectionResult(
            "NES",
            95,
            [new DetectionHypothesis("NES", 95, DetectionSource.UniqueExtension, "ext=.nes")],
            false,
            null);

        var result = GroundTruthComparator.Compare(entry, actual);

        Assert.Equal(BenchmarkVerdict.TrueNegative, result.Verdict);
        Assert.Equal("UNKNOWN", result.ActualConsoleKey);
    }

    private static GroundTruthEntry CreateEntry(string id, string category, string? consoleKey)
    {
        return new GroundTruthEntry
        {
            Id = id,
            Source = new SourceInfo
            {
                FileName = "test.rom",
                Extension = ".rom",
                SizeBytes = 1024,
                Directory = "unsorted"
            },
            Tags = ["unit-test"],
            Difficulty = "easy",
            Expected = new ExpectedResult
            {
                ConsoleKey = consoleKey,
                Category = category,
                Confidence = 0,
                DatMatchLevel = "none",
                DatEcosystem = "none",
                SortDecision = "block"
            },
            DetectionExpectations = null,
            FileModel = null,
            Relationships = null,
            Notes = null
        };
    }
}
