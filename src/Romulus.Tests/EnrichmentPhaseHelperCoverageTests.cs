using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for EnrichmentPipelinePhase internal static helpers:
/// IsStrictDatNameCandidate, GetParallelismHint, ResolveFamily, ResolveHashStrategy.
/// </summary>
public sealed class EnrichmentPhaseHelperCoverageTests
{
    // ═══════════════════════════════════════════
    //  IsStrictDatNameCandidate
    // ═══════════════════════════════════════════

    [Theory]
    [InlineData("Super Mario World", true)]
    [InlineData("Zelda", true)]
    [InlineData("abc", true)]
    [InlineData("a1b", true)]
    [InlineData("Game123", true)]
    [InlineData("  SpacedName  ", true)] // trimmed
    public void IsStrictDatNameCandidate_ValidStems_True(string stem, bool expected)
        => Assert.Equal(expected, EnrichmentPipelinePhase.IsStrictDatNameCandidate(stem));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")] // too short
    [InlineData("a")]
    [InlineData("--")] // no alphanumeric >= 3
    [InlineData("...")] // no alphanumeric >= 3
    public void IsStrictDatNameCandidate_TooShortOrEmpty_False(string? stem)
        => Assert.False(EnrichmentPipelinePhase.IsStrictDatNameCandidate(stem!));

    [Theory]
    [InlineData("track")]
    [InlineData("TRACK")]
    [InlineData("disk")]
    [InlineData("disc")]
    [InlineData("rom")]
    [InlineData("game")]
    [InlineData("image")]
    [InlineData("IMAGE")]
    public void IsStrictDatNameCandidate_BlocklistStem_False(string stem)
        => Assert.False(EnrichmentPipelinePhase.IsStrictDatNameCandidate(stem));

    [Theory]
    [InlineData("a-b")] // only 2 alphanumeric
    [InlineData("x_y")] // only 2 alphanumeric
    public void IsStrictDatNameCandidate_InsufficientAlphanumeric_False(string stem)
        => Assert.False(EnrichmentPipelinePhase.IsStrictDatNameCandidate(stem));

    // ═══════════════════════════════════════════
    //  GetParallelismHint
    // ═══════════════════════════════════════════

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void GetParallelismHint_SmallItemCount_ReturnsSingle(int count)
        => Assert.Equal(1, EnrichmentPipelinePhase.GetParallelismHint(count));

    [Fact]
    public void GetParallelismHint_LargeItemCount_ReturnsPositive()
    {
        var hint = EnrichmentPipelinePhase.GetParallelismHint(1000);
        Assert.True(hint >= 1);
    }

    [Fact]
    public void GetParallelismHint_DefaultMaxValue_ReturnsPositive()
    {
        var hint = EnrichmentPipelinePhase.GetParallelismHint();
        Assert.True(hint >= 1);
    }

    // ═══════════════════════════════════════════
    //  ResolveFamily
    // ═══════════════════════════════════════════

    private static ConsoleDetector BuildDetector()
    {
        var consoles = new List<ConsoleInfo>
        {
            new("NES", "Nintendo", false, [".nes"], [], ["NES"], Family: PlatformFamily.NoIntroCartridge),
            new("SNES", "Super Nintendo", false, [".sfc", ".smc"], [], ["SNES"], Family: PlatformFamily.NoIntroCartridge),
            new("PS1", "PlayStation", true, [".cue"], [], ["PS1"], Family: PlatformFamily.RedumpDisc),
            new("GBA", "Game Boy Advance", false, [".gba"], [], ["GBA"], Family: PlatformFamily.NoIntroCartridge),
        };
        return new ConsoleDetector(consoles);
    }

    [Fact]
    public void ResolveFamily_KnownConsoleKey_ReturnsFamily()
    {
        var detector = BuildDetector();
        var family = EnrichmentPipelinePhase.ResolveFamily(detector, "NES", null);
        Assert.Equal(PlatformFamily.NoIntroCartridge, family);
    }

    [Fact]
    public void ResolveFamily_UnknownConsoleKey_ReturnsUnknown()
    {
        var detector = BuildDetector();
        var family = EnrichmentPipelinePhase.ResolveFamily(detector, "UNKNOWN", null);
        Assert.Equal(PlatformFamily.Unknown, family);
    }

    [Fact]
    public void ResolveFamily_AmbiguousConsoleKey_ReturnsUnknown()
    {
        var detector = BuildDetector();
        var family = EnrichmentPipelinePhase.ResolveFamily(detector, "AMBIGUOUS", null);
        Assert.Equal(PlatformFamily.Unknown, family);
    }

    [Fact]
    public void ResolveFamily_NullConsoleKey_WithHypotheses_UsesTopHypothesis()
    {
        var detector = BuildDetector();
        var detection = new ConsoleDetectionResult(
            "PS1", 85,
            [
                new DetectionHypothesis("PS1", 85, DetectionSource.UniqueExtension, ".cue match"),
                new DetectionHypothesis("SNES", 40, DetectionSource.FolderName, "folder match")
            ],
            false, null);

        var family = EnrichmentPipelinePhase.ResolveFamily(detector, "UNKNOWN", detection);
        Assert.Equal(PlatformFamily.RedumpDisc, family);
    }

    [Fact]
    public void ResolveFamily_NullDetector_ReturnsUnknown()
    {
        var family = EnrichmentPipelinePhase.ResolveFamily(null, "NES", null);
        Assert.Equal(PlatformFamily.Unknown, family);
    }

    [Fact]
    public void ResolveFamily_NullConsoleKey_NoHypotheses_ReturnsUnknown()
    {
        var detector = BuildDetector();
        var family = EnrichmentPipelinePhase.ResolveFamily(detector, "", null);
        Assert.Equal(PlatformFamily.Unknown, family);
    }

    // ═══════════════════════════════════════════
    //  ResolveHashStrategy
    // ═══════════════════════════════════════════

    [Fact]
    public void ResolveHashStrategy_KnownConsole_ReturnsStrategy()
    {
        var consoles = new List<ConsoleInfo>
        {
            new("NES", "Nintendo", false, [".nes"], [], ["NES"], HashStrategy: "headerless"),
        };
        var detector = new ConsoleDetector(consoles);

        var strategy = EnrichmentPipelinePhase.ResolveHashStrategy(detector, "NES", null);
        Assert.Equal("headerless", strategy);
    }

    [Fact]
    public void ResolveHashStrategy_UnknownConsole_ReturnsNull()
    {
        var detector = BuildDetector();
        var strategy = EnrichmentPipelinePhase.ResolveHashStrategy(detector, "UNKNOWN", null);
        Assert.Null(strategy);
    }

    [Fact]
    public void ResolveHashStrategy_NullDetector_ReturnsNull()
    {
        var strategy = EnrichmentPipelinePhase.ResolveHashStrategy(null, "NES", null);
        Assert.Null(strategy);
    }

    [Fact]
    public void ResolveHashStrategy_WithHypotheses_UsesTopHypothesis()
    {
        var consoles = new List<ConsoleInfo>
        {
            new("NES", "Nintendo", false, [".nes"], [], ["NES"], HashStrategy: "headerless"),
            new("SNES", "Super Nintendo", false, [".sfc"], [], ["SNES"]),
        };
        var detector = new ConsoleDetector(consoles);
        var detection = new ConsoleDetectionResult(
            "UNKNOWN", 0,
            [
                new DetectionHypothesis("NES", 90, DetectionSource.UniqueExtension, "ext"),
            ],
            false, null);

        var strategy = EnrichmentPipelinePhase.ResolveHashStrategy(detector, "UNKNOWN", detection);
        Assert.Equal("headerless", strategy);
    }

    [Fact]
    public void ResolveHashStrategy_EmptyHypotheses_ReturnsNull()
    {
        var detector = BuildDetector();
        var detection = new ConsoleDetectionResult("UNKNOWN", 0, [], false, null);

        var strategy = EnrichmentPipelinePhase.ResolveHashStrategy(detector, "UNKNOWN", detection);
        Assert.Null(strategy);
    }
}
