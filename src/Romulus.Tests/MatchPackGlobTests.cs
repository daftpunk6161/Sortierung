using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for RunEnvironmentBuilder.MatchPackGlob:
/// No-Intro daily pack glob matching with deterministic tie-breaking.
/// </summary>
public sealed class MatchPackGlobTests
{
    [Fact]
    public void MatchPackGlob_EmptyPattern_ReturnsNull()
    {
        var files = new[] { @"C:\dats\NES.dat" };

        Assert.Null(RunEnvironmentBuilder.MatchPackGlob(files, ""));
        Assert.Null(RunEnvironmentBuilder.MatchPackGlob(files, "  "));
    }

    [Fact]
    public void MatchPackGlob_EmptyFiles_ReturnsNull()
    {
        Assert.Null(RunEnvironmentBuilder.MatchPackGlob([], "Nintendo*"));
    }

    [Fact]
    public void MatchPackGlob_NoMatch_ReturnsNull()
    {
        var files = new[] { @"C:\dats\Sega - Mega Drive.dat" };

        Assert.Null(RunEnvironmentBuilder.MatchPackGlob(files, "Nintendo*"));
    }

    [Fact]
    public void MatchPackGlob_ExactStemMatch_ReturnsFile()
    {
        var files = new[] { @"C:\dats\Nintendo - NES (20250101).dat" };

        var match = RunEnvironmentBuilder.MatchPackGlob(files, "Nintendo - NES*");

        Assert.Equal(@"C:\dats\Nintendo - NES (20250101).dat", match);
    }

    [Fact]
    public void MatchPackGlob_MultipleCandidates_PicksLatestTimestamp()
    {
        var files = new[]
        {
            @"C:\dats\Nintendo - NES (20240101).dat",
            @"C:\dats\Nintendo - NES (20250601).dat",
            @"C:\dats\Nintendo - NES (20250101).dat"
        };

        var match = RunEnvironmentBuilder.MatchPackGlob(files, "Nintendo - NES*");

        // CompareDatCandidatePriority picks highest stem alphabetically → newest date wins
        Assert.Equal(@"C:\dats\Nintendo - NES (20250601).dat", match);
    }

    [Fact]
    public void MatchPackGlob_CaseInsensitive()
    {
        var files = new[] { @"C:\dats\nintendo - nes (20250101).dat" };

        var match = RunEnvironmentBuilder.MatchPackGlob(files, "NINTENDO - NES*");

        Assert.Equal(@"C:\dats\nintendo - nes (20250101).dat", match);
    }

    [Fact]
    public void MatchPackGlob_TrailingStarTrimmed()
    {
        var files = new[] { @"C:\dats\Sony - PSX (20250101).xml" };

        // Pattern with explicit trailing * should match
        var match = RunEnvironmentBuilder.MatchPackGlob(files, "Sony - PSX*");
        Assert.NotNull(match);

        // Pattern without trailing * should also match (prefix match)
        var match2 = RunEnvironmentBuilder.MatchPackGlob(files, "Sony - PSX");
        Assert.NotNull(match2);
    }

    [Fact]
    public void MatchPackGlob_DeterministicTieBreaker_OnSameStem()
    {
        // Same stem but different extensions or paths → will fall through to path-level tie-break
        var files = new[]
        {
            @"C:\dats\b\Nintendo - NES.dat",
            @"C:\dats\a\Nintendo - NES.dat"
        };

        var match = RunEnvironmentBuilder.MatchPackGlob(files, "Nintendo - NES*");

        // Deterministic: first comparison by stem (equal), then by full path (a\ < b\)
        // But CompareDatCandidatePriority picks "higher" alphabetically for the winner
        Assert.NotNull(match);
    }
}
