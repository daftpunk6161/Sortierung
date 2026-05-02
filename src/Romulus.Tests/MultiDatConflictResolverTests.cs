using Romulus.Contracts.Models;
using Romulus.Infrastructure.Dat;
using Xunit;

namespace Romulus.Tests;

public sealed class MultiDatConflictResolverTests
{
    private readonly MultiDatConflictResolver _resolver = new();

    [Fact]
    public void Resolve_PreferredSourceWinsBeforeMatchStrength()
    {
        var matches = new[]
        {
            new DatMatch("PS1", "Game", "game.bin", false, null, "SHA1", "redump"),
            new DatMatch("PS2", "Game", "game.iso", false, null, "SHA256", "no-intro")
        };

        var resolution = _resolver.Resolve(matches, new MultiDatResolutionContext(
            ExpectedConsoleKey: "PS2",
            ConsoleStrengths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["PS2"] = 100
            },
            PreferredSources: ["redump"],
            MatchKind: MatchKind.ExactDatHash));

        Assert.Equal("PS1", resolution.SelectedMatch?.ConsoleKey);
        Assert.True(resolution.IsConflict);
        Assert.Equal("preferred-source", resolution.Reason);
    }

    [Fact]
    public void Resolve_MatchStrengthWinsWhenNoPreferredSourceApplies()
    {
        var matches = new[]
        {
            new DatMatch("NES", "Shared", "shared.nes", false, null, "SHA1", "no-intro"),
            new DatMatch("SNES", "Shared", "shared.sfc", false, null, "SHA1", "no-intro")
        };

        var resolution = _resolver.Resolve(matches, new MultiDatResolutionContext(
            ExpectedConsoleKey: null,
            ConsoleStrengths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["NES"] = 20,
                ["SNES"] = 90
            },
            PreferredSources: Array.Empty<string>(),
            MatchKind: MatchKind.ExactDatHash));

        Assert.Equal("SNES", resolution.SelectedMatch?.ConsoleKey);
        Assert.Equal("match-strength", resolution.Reason);
    }

    [Fact]
    public void Resolve_LexicographicTiebreakIsStableAcrossInputOrder()
    {
        var firstOrder = new[]
        {
            new DatMatch("SNES", "Shared", "shared.sfc", false, null, "SHA1", null),
            new DatMatch("NES", "Shared", "shared.nes", false, null, "SHA1", null)
        };
        var secondOrder = firstOrder.Reverse().ToArray();

        var first = _resolver.Resolve(firstOrder, MultiDatResolutionContext.Empty);
        var second = _resolver.Resolve(secondOrder, MultiDatResolutionContext.Empty);

        Assert.Equal("NES", first.SelectedMatch?.ConsoleKey);
        Assert.Equal(first.SelectedMatch, second.SelectedMatch);
        Assert.Equal("lexicographic-tiebreak", first.Reason);
        Assert.Equal("lexicographic-tiebreak", second.Reason);
    }

    [Fact]
    public void Resolve_ExposesRankedCandidatesForUiCliApiProjection()
    {
        var matches = new[]
        {
            new DatMatch("SNES", "Shared", "shared.sfc", false, null, "SHA1", "redump"),
            new DatMatch("NES", "Shared", "shared.nes", false, null, "SHA1", "no-intro")
        };

        var resolution = _resolver.Resolve(matches, MultiDatResolutionContext.Empty);

        Assert.True(resolution.IsConflict);
        Assert.Equal(2, resolution.Candidates.Count);
        Assert.Equal(2, resolution.RankedCandidates.Count);
        Assert.All(resolution.RankedCandidates, candidate => Assert.False(string.IsNullOrWhiteSpace(candidate.RankKey)));
    }
}
