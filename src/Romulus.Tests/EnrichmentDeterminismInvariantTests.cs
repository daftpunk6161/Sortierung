using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Invariant tests for determinism in the enrichment pipeline.
/// Protects against non-deterministic hypothesis tie-breaking and DAT resolution.
/// </summary>
public sealed class EnrichmentDeterminismInvariantTests
{
    // ── Hypothesis Tie-Break Determinism ──

    [Fact]
    public void ResolveUnknown_EqualConfidenceHypotheses_SelectsAlphabeticallyFirstConsole()
    {
        // Two hypotheses with identical confidence → must pick alphabetically first matching console
        var hash = "AABBCCDD11223344";
        var datIndex = new DatIndex();
        datIndex.Add("SNES", hash, "Game SNES", "game.sfc");
        datIndex.Add("NES", hash, "Game NES", "game.nes");

        var hypotheses = new[]
        {
            new DetectionHypothesis("SNES", 60, DetectionSource.FolderName, "folder=SNES"),
            new DetectionHypothesis("NES", 60, DetectionSource.FolderName, "folder=NES")
        };

        var detection = new ConsoleDetectionResult(
            "AMBIGUOUS", 60, hypotheses, true, "Conflict: SNES vs NES");

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, hash, detection);

        Assert.True(result.IsMatch);
        Assert.Equal("NES", result.ConsoleKey); // N < S alphabetically
        Assert.True(result.ResolvedFromAmbiguousCandidates);
    }

    [Fact]
    public void ResolveUnknown_EqualConfidenceHypotheses_DeterministicRegardlessOfInputOrder()
    {
        // Same hypotheses in reversed input order → must still select the same console
        var hash = "AABBCCDD11223344";
        var datIndex = new DatIndex();
        datIndex.Add("SNES", hash, "Game SNES", "game.sfc");
        datIndex.Add("NES", hash, "Game NES", "game.nes");

        // Order 1: NES first
        var hypothesesOrder1 = new[]
        {
            new DetectionHypothesis("NES", 60, DetectionSource.FolderName, "folder=NES"),
            new DetectionHypothesis("SNES", 60, DetectionSource.FolderName, "folder=SNES")
        };

        // Order 2: SNES first
        var hypothesesOrder2 = new[]
        {
            new DetectionHypothesis("SNES", 60, DetectionSource.FolderName, "folder=SNES"),
            new DetectionHypothesis("NES", 60, DetectionSource.FolderName, "folder=NES")
        };

        var detection1 = new ConsoleDetectionResult(
            "AMBIGUOUS", 60, hypothesesOrder1, true, null);
        var detection2 = new ConsoleDetectionResult(
            "AMBIGUOUS", 60, hypothesesOrder2, true, null);

        var result1 = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, hash, detection1);
        var result2 = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, hash, detection2);

        Assert.Equal(result1.ConsoleKey, result2.ConsoleKey);
        Assert.Equal("NES", result1.ConsoleKey);
    }

    [Fact]
    public void ResolveUnknown_DifferentConfidence_SelectsHigherConfidence()
    {
        // Different confidence → higher wins regardless of alphabetical order
        var hash = "AABBCCDD11223344";
        var datIndex = new DatIndex();
        datIndex.Add("SNES", hash, "Game SNES", "game.sfc");
        datIndex.Add("NES", hash, "Game NES", "game.nes");

        var hypotheses = new[]
        {
            new DetectionHypothesis("SNES", 80, DetectionSource.UniqueExtension, "ext=sfc"),
            new DetectionHypothesis("NES", 60, DetectionSource.FolderName, "folder=NES")
        };

        var detection = new ConsoleDetectionResult(
            "AMBIGUOUS", 80, hypotheses, true, null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, hash, detection);

        Assert.True(result.IsMatch);
        Assert.Equal("SNES", result.ConsoleKey); // Higher confidence wins
    }

    [Fact]
    public void ResolveUnknown_SingleDatMatch_NoAmbiguityFlag()
    {
        var hash = "AABBCCDD11223344";
        var datIndex = new DatIndex();
        datIndex.Add("NES", hash, "Game NES", "game.nes");

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, hash, null);

        Assert.True(result.IsMatch);
        Assert.Equal("NES", result.ConsoleKey);
        Assert.False(result.ResolvedFromAmbiguousCandidates);
    }

    [Fact]
    public void ResolveUnknown_NoHypotheses_MultipleDatMatches_ReturnsNoMatch()
    {
        // Multiple DAT matches but no hypotheses to resolve → cannot pick
        var hash = "AABBCCDD11223344";
        var datIndex = new DatIndex();
        datIndex.Add("SNES", hash, "Game SNES", "game.sfc");
        datIndex.Add("NES", hash, "Game NES", "game.nes");

        var detection = new ConsoleDetectionResult(
            "UNKNOWN", 0, Array.Empty<DetectionHypothesis>(), false, null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, hash, detection);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknown_NullDetection_SingleMatch_ReturnsMatch()
    {
        var hash = "AABBCCDD11223344";
        var datIndex = new DatIndex();
        datIndex.Add("NES", hash, "Game NES", "game.nes");

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, hash, null);

        Assert.True(result.IsMatch);
        Assert.Equal("NES", result.ConsoleKey);
    }

    [Fact]
    public void ResolveUnknown_NullDetection_MultipleDatMatches_ReturnsNoMatch()
    {
        var hash = "AABBCCDD11223344";
        var datIndex = new DatIndex();
        datIndex.Add("SNES", hash, "Game SNES", "game.sfc");
        datIndex.Add("NES", hash, "Game NES", "game.nes");

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, hash, null);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknown_HypothesisNotInDat_Skipped()
    {
        // Hypothesis exists for MD, but DAT only has NES and SNES
        var hash = "AABBCCDD11223344";
        var datIndex = new DatIndex();
        datIndex.Add("SNES", hash, "Game SNES", "game.sfc");
        datIndex.Add("NES", hash, "Game NES", "game.nes");

        var hypotheses = new[]
        {
            new DetectionHypothesis("MD", 90, DetectionSource.UniqueExtension, "ext=md"),
            new DetectionHypothesis("NES", 70, DetectionSource.FolderName, "folder=NES")
        };

        var detection = new ConsoleDetectionResult(
            "AMBIGUOUS", 90, hypotheses, true, null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, hash, detection);

        Assert.True(result.IsMatch);
        Assert.Equal("NES", result.ConsoleKey); // MD not in DAT → skipped → NES wins
    }

    [Fact]
    public void ResolveUnknown_NoHypothesisMatchesDat_ReturnsNoMatch()
    {
        var hash = "AABBCCDD11223344";
        var datIndex = new DatIndex();
        // Need 2+ DAT matches so single-match shortcut doesn't trigger
        datIndex.Add("SNES", hash, "Game SNES", "game.sfc");
        datIndex.Add("GBA", hash, "Game GBA", "game.gba");

        var hypotheses = new[]
        {
            new DetectionHypothesis("MD", 90, DetectionSource.UniqueExtension, "ext=md"),
            new DetectionHypothesis("NES", 70, DetectionSource.FolderName, "folder=NES")
        };

        var detection = new ConsoleDetectionResult(
            "AMBIGUOUS", 90, hypotheses, true, null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, hash, detection);

        Assert.False(result.IsMatch); // Neither hypothesis matches DAT consoles
    }

    [Fact]
    public void ResolveUnknown_BiosEntry_MarkedCorrectly()
    {
        var hash = "AABBCCDD11223344";
        var datIndex = new DatIndex();
        datIndex.Add("PS1", hash, "SCPH1001", "scph1001.bin", isBios: true);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, hash, null);

        Assert.True(result.IsMatch);
        Assert.True(result.IsBios);
        Assert.Equal("PS1", result.ConsoleKey);
    }

    [Fact]
    public void ResolveUnknown_ThreeWayTie_SelectsAlphabeticallyFirst()
    {
        // Three consoles with equal confidence → must be deterministic
        var hash = "AABBCCDD11223344";
        var datIndex = new DatIndex();
        datIndex.Add("SNES", hash, "Game SNES", "game.sfc");
        datIndex.Add("NES", hash, "Game NES", "game.nes");
        datIndex.Add("GBA", hash, "Game GBA", "game.gba");

        var hypotheses = new[]
        {
            new DetectionHypothesis("SNES", 50, DetectionSource.FolderName, "folder=SNES"),
            new DetectionHypothesis("NES", 50, DetectionSource.FolderName, "folder=NES"),
            new DetectionHypothesis("GBA", 50, DetectionSource.FolderName, "folder=GBA")
        };

        var detection = new ConsoleDetectionResult(
            "AMBIGUOUS", 50, hypotheses, true, null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, hash, detection);

        Assert.True(result.IsMatch);
        Assert.Equal("GBA", result.ConsoleKey); // G < N < S alphabetically
    }

    [Fact]
    public void ResolveUnknown_EmptyHash_ReturnsNoMatch()
    {
        var datIndex = new DatIndex();
        datIndex.Add("NES", "SOMEHASH", "Game", "game.nes");

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, "NONEXISTENT", null);

        Assert.False(result.IsMatch);
    }

    // ── DatIndex.LookupAllByHash Deterministic Order ──

    [Fact]
    public void DatIndex_LookupAllByHash_ReturnsSortedByConsoleKey()
    {
        var datIndex = new DatIndex();
        var hash = "TESTDETERMINISM";

        // Add in reverse alphabetical order
        datIndex.Add("SNES", hash, "Game SNES", "game.sfc");
        datIndex.Add("NES", hash, "Game NES", "game.nes");
        datIndex.Add("GBA", hash, "Game GBA", "game.gba");

        var results = datIndex.LookupAllByHash(hash);

        Assert.Equal(3, results.Count);
        Assert.Equal("GBA", results[0].ConsoleKey);
        Assert.Equal("NES", results[1].ConsoleKey);
        Assert.Equal("SNES", results[2].ConsoleKey);
    }

    [Fact]
    public void DatIndex_LookupAny_ReturnsDeterministicFirstMatch()
    {
        var datIndex = new DatIndex();
        var hash = "TESTDETERMINISM2";

        datIndex.Add("SNES", hash, "Game SNES", "game.sfc");
        datIndex.Add("GBA", hash, "Game GBA", "game.gba");

        var result = datIndex.LookupAny(hash);

        Assert.NotNull(result);
        Assert.Equal("GBA", result.Value.ConsoleKey); // Alphabetically first
    }
}
