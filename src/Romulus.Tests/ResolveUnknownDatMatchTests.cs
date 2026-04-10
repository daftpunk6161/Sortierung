using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for EnrichmentPipelinePhase.ResolveUnknownDatMatch / ResolveUnknownDatNameMatch.
/// </summary>
public sealed class ResolveUnknownDatMatchTests
{
    // ═══ ResolveUnknownDatMatch ═══════════════════════════════════════

    [Fact]
    public void ResolveUnknownDatMatch_NoHashMatches_ReturnsNoMatch()
    {
        var index = new DatIndex();
        index.Add("NES", "hash1", "Super Mario");

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(index, "nonexistent", null);

        Assert.False(result.IsMatch);
        Assert.Null(result.ConsoleKey);
    }

    [Fact]
    public void ResolveUnknownDatMatch_SingleMatch_ReturnsConsoleWithoutAmbiguity()
    {
        var index = new DatIndex();
        index.Add("NES", "abc123", "Super Mario Bros");

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(index, "abc123", null);

        Assert.True(result.IsMatch);
        Assert.Equal("NES", result.ConsoleKey);
        Assert.Equal("Super Mario Bros", result.DatGameName);
        Assert.False(result.ResolvedFromAmbiguousCandidates);
    }

    [Fact]
    public void ResolveUnknownDatMatch_SingleMatch_NoBios_ReturnsNotBios()
    {
        var index = new DatIndex();
        index.Add("NES", "abc123", "Super Mario Bros", isBios: false);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(index, "abc123", null);

        Assert.False(result.IsBios);
    }

    [Fact]
    public void ResolveUnknownDatMatch_SingleMatch_Bios_ReturnsBios()
    {
        var index = new DatIndex();
        index.Add("NES", "biosHash", "[BIOS] NES", isBios: true);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(index, "biosHash", null);

        Assert.True(result.IsBios);
    }

    [Fact]
    public void ResolveUnknownDatMatch_MultipleMatches_NoDetection_ReturnsNoMatch()
    {
        var index = new DatIndex();
        index.Add("NES", "sharedHash", "Game A");
        index.Add("SNES", "sharedHash", "Game B");

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(index, "sharedHash", null);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatMatch_MultipleMatches_EmptyHypotheses_ReturnsNoMatch()
    {
        var index = new DatIndex();
        index.Add("NES", "sharedHash", "Game A");
        index.Add("SNES", "sharedHash", "Game B");

        var detection = new ConsoleDetectionResult("UNKNOWN", 0, [], false, null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(index, "sharedHash", detection);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatMatch_MultipleMatches_HypothesisMatchesCandidate_Resolves()
    {
        var index = new DatIndex();
        index.Add("NES", "sharedHash", "Game A");
        index.Add("SNES", "sharedHash", "Game B");

        var detection = new ConsoleDetectionResult(
            "SNES", 80,
            [new DetectionHypothesis("SNES", 80, DetectionSource.FolderName, "folder=SNES")],
            false, null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(index, "sharedHash", detection);

        Assert.True(result.IsMatch);
        Assert.Equal("SNES", result.ConsoleKey);
        Assert.Equal("Game B", result.DatGameName);
        Assert.True(result.ResolvedFromAmbiguousCandidates);
    }

    [Fact]
    public void ResolveUnknownDatMatch_MultipleMatches_HypothesisNotInCandidates_ReturnsNoMatch()
    {
        var index = new DatIndex();
        index.Add("NES", "sharedHash", "Game A");
        index.Add("SNES", "sharedHash", "Game B");

        var detection = new ConsoleDetectionResult(
            "GBA", 90,
            [new DetectionHypothesis("GBA", 90, DetectionSource.UniqueExtension, "ext=.gba")],
            false, null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(index, "sharedHash", detection);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatMatch_MultipleMatches_HighestConfidenceHypothesisWins()
    {
        var index = new DatIndex();
        index.Add("NES", "sharedHash", "Game A");
        index.Add("SNES", "sharedHash", "Game B");

        var detection = new ConsoleDetectionResult(
            "SNES", 90,
            [
                new DetectionHypothesis("NES", 50, DetectionSource.FolderName, "folder=NES"),
                new DetectionHypothesis("SNES", 80, DetectionSource.UniqueExtension, "ext=.sfc")
            ],
            true, "conflict");

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(index, "sharedHash", detection);

        Assert.True(result.IsMatch);
        Assert.Equal("SNES", result.ConsoleKey);
    }

    // ═══ ResolveUnknownDatNameMatch ═══════════════════════════════════

    [Fact]
    public void ResolveUnknownDatNameMatch_EmptyMatches_ReturnsNoMatch()
    {
        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch([], null);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatNameMatch_SingleMatch_ReturnsDirectly()
    {
        var matches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("NES", new DatIndex.DatIndexEntry("Super Mario Bros", null, false, null))
        };

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(matches, null);

        Assert.True(result.IsMatch);
        Assert.Equal("NES", result.ConsoleKey);
        Assert.Equal("Super Mario Bros", result.DatGameName);
        Assert.False(result.ResolvedFromAmbiguousCandidates);
    }

    [Fact]
    public void ResolveUnknownDatNameMatch_MultipleMatches_NoDetection_ReturnsNoMatch()
    {
        var matches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("NES", new DatIndex.DatIndexEntry("Game", null)),
            ("SNES", new DatIndex.DatIndexEntry("Game", null))
        };

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(matches, null);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatNameMatch_MultipleMatches_HypothesisResolves()
    {
        var matches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("NES", new DatIndex.DatIndexEntry("Game NES", null)),
            ("SNES", new DatIndex.DatIndexEntry("Game SNES", null))
        };

        var detection = new ConsoleDetectionResult(
            "SNES", 80,
            [new DetectionHypothesis("SNES", 80, DetectionSource.FolderName, "folder")],
            false, null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(matches, detection);

        Assert.True(result.IsMatch);
        Assert.Equal("SNES", result.ConsoleKey);
        Assert.True(result.ResolvedFromAmbiguousCandidates);
    }

    [Fact]
    public void ResolveUnknownDatNameMatch_MultipleMatches_NoHypothesisInCandidates_ReturnsNoMatch()
    {
        var matches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("NES", new DatIndex.DatIndexEntry("Game", null)),
            ("SNES", new DatIndex.DatIndexEntry("Game", null))
        };

        var detection = new ConsoleDetectionResult(
            "GBA", 90,
            [new DetectionHypothesis("GBA", 90, DetectionSource.UniqueExtension, "ext")],
            false, null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(matches, detection);

        Assert.False(result.IsMatch);
    }
}
