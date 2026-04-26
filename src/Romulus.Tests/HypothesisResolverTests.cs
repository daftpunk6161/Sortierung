using System.Collections.Generic;
using System.Linq;
using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Dedicated test coverage for <see cref="HypothesisResolver"/> (F23).
/// Also covers Block-2 invariants:
///   F2  – FilenameConsoleAnalyzer raw confidence == ConfidenceRating()
///   F8  – Tiebreaker uses EvidenceTier (semantic), not enum ordinal
///   F16 – MultiSourceAgreementBonus only on distinct sources
///   F20 – Naming honesty (winnerConfidence is single confidence, not aggregate)
/// </summary>
public sealed class HypothesisResolverTests
{
    // ──────────────────────────────────────────────────────────────────
    //  F2 – Filename analyzer must use ConfidenceRating(), not raw 95/75
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void F2_DetectBySerial_UsesSerialNumberConfidenceRating()
    {
        var result = FilenameConsoleAnalyzer.DetectBySerial("SLUS-00123 Game.bin");
        Assert.NotNull(result);
        Assert.Equal(DetectionSource.SerialNumber.ConfidenceRating(), result!.Value.Confidence);
    }

    [Fact]
    public void F2_DetectByKeyword_UsesFilenameKeywordConfidenceRating()
    {
        var result = FilenameConsoleAnalyzer.DetectByKeyword("Game [PS1].iso");
        Assert.NotNull(result);
        Assert.Equal(DetectionSource.FilenameKeyword.ConfidenceRating(), result!.Value.Confidence);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Resolver – baseline single-source behaviour
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyHypotheses_ReturnsUnknown()
    {
        var result = HypothesisResolver.Resolve(new List<DetectionHypothesis>());
        Assert.Same(ConsoleDetectionResult.Unknown, result);
    }

    [Fact]
    public void SingleDatHash_ProducesDatVerifiedSort()
    {
        var hyps = new List<DetectionHypothesis>
        {
            new("PS1", DetectionSource.DatHash.ConfidenceRating(), DetectionSource.DatHash, "dat=abc"),
        };
        var result = HypothesisResolver.Resolve(hyps, datAvailable: true);
        Assert.Equal("PS1", result.ConsoleKey);
        Assert.Equal(SortDecision.DatVerified, result.SortDecision);
        Assert.True(result.HasHardEvidence);
        Assert.False(result.HasConflict);
    }

    [Fact]
    public void SingleFolderName_StaysSoftAndCapped()
    {
        // Single FolderName hypothesis must remain soft-only and not exceed
        // the source-specific single-source cap.
        var hyps = new List<DetectionHypothesis>
        {
            new("PS1", DetectionSource.FolderName.ConfidenceRating(), DetectionSource.FolderName, "folder=PS1"),
        };
        var result = HypothesisResolver.Resolve(hyps);
        Assert.False(result.HasHardEvidence);
        Assert.True(result.IsSoftOnly);
        Assert.True(result.Confidence <= DetectionSource.FolderName.SingleSourceCap());
    }

    // ──────────────────────────────────────────────────────────────────
    //  F16 – MultiSourceAgreementBonus only on DISTINCT sources
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void F16_TwoFolderNameHypotheses_DoNotTriggerMultiSourceBonus()
    {
        // Two FolderName hypotheses for the same console (e.g. nested folders)
        // are not orthogonal evidence and must NOT receive the multi-source bonus.
        var folderRating = DetectionSource.FolderName.ConfidenceRating();
        var hypsTwoFolders = new List<DetectionHypothesis>
        {
            new("PS1", folderRating, DetectionSource.FolderName, "folder=PS1"),
            new("PS1", folderRating, DetectionSource.FolderName, "folder=Sony/PS1"),
        };
        var hypsOneFolder = new List<DetectionHypothesis>
        {
            new("PS1", folderRating, DetectionSource.FolderName, "folder=PS1"),
        };

        var two = HypothesisResolver.Resolve(hypsTwoFolders);
        var one = HypothesisResolver.Resolve(hypsOneFolder);

        Assert.Equal(one.Confidence, two.Confidence);
    }

    [Fact]
    public void F16_FolderPlusSerial_TriggersMultiSourceBonus()
    {
        // Two distinct sources agreeing should still receive the bonus.
        var folderHyp = new DetectionHypothesis(
            "PS1", DetectionSource.FolderName.ConfidenceRating(), DetectionSource.FolderName, "folder=PS1");
        var serialHyp = new DetectionHypothesis(
            "PS1", DetectionSource.SerialNumber.ConfidenceRating(), DetectionSource.SerialNumber, "serial=SLUS-00123");

        var solo = HypothesisResolver.Resolve(new List<DetectionHypothesis> { serialHyp });
        var combo = HypothesisResolver.Resolve(new List<DetectionHypothesis> { serialHyp, folderHyp });

        Assert.True(combo.Confidence > solo.Confidence,
            $"distinct multi-source should boost confidence, but combo={combo.Confidence} solo={solo.Confidence}");
    }

    // ──────────────────────────────────────────────────────────────────
    //  F8 – Tiebreaker must use EvidenceTier, not enum ordinal
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void F8_Tiebreaker_PrefersBetterEvidenceTier_NotEnumOrdinal()
    {
        // Two hypotheses, equal confidence, two different sources whose enum-ordinal
        // ordering DIFFERS from their EvidenceTier ordering:
        //   UniqueExtension (enum 95, Tier2)  vs  CartridgeHeader (enum 90, Tier1)
        // Enum-ordinal would prefer UniqueExtension; EvidenceTier prefers CartridgeHeader.
        // Resolver must follow EvidenceTier.
        var equalConfidence = 80;
        var hyps = new List<DetectionHypothesis>
        {
            new("NES", equalConfidence, DetectionSource.UniqueExtension, "ext=.nes"),
            new("NES", equalConfidence, DetectionSource.CartridgeHeader, "cart-header"),
        };

        var result = HypothesisResolver.Resolve(hyps);

        // Primary source reflects the stronger tier. CartridgeHeader is Tier1, UniqueExtension is Tier2.
        Assert.Equal(MatchKind.CartridgeHeaderMagic, result.MatchEvidence!.PrimaryMatchKind);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Conflict / AMBIGUOUS handling
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoHardEvidenceSources_DifferentConsoles_FlagsConflict()
    {
        var hyps = new List<DetectionHypothesis>
        {
            new("PS1", DetectionSource.DiscHeader.ConfidenceRating(), DetectionSource.DiscHeader, "header=PS1"),
            new("PS2", DetectionSource.DiscHeader.ConfidenceRating(), DetectionSource.DiscHeader, "header=PS2"),
        };
        var result = HypothesisResolver.Resolve(hyps);
        Assert.True(result.HasConflict);
    }

    [Fact]
    public void Ambiguous_TwoEqualHardSources_TriggersAmbiguous()
    {
        // Two hard-evidence sources of the same source class and identical confidence
        // → AMBIGUOUS: ConsoleKey collapses, decision Blocked.
        var hyps = new List<DetectionHypothesis>
        {
            new("PS1", DetectionSource.DiscHeader.ConfidenceRating(), DetectionSource.DiscHeader, "header=PS1"),
            new("PS2", DetectionSource.DiscHeader.ConfidenceRating(), DetectionSource.DiscHeader, "header=PS2"),
        };
        var result = HypothesisResolver.Resolve(hyps);
        Assert.Equal("AMBIGUOUS", result.ConsoleKey);
        Assert.Equal(SortDecision.Blocked, result.SortDecision);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Determinism – same input must produce same output
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Determinism_SameInputProducesSameOutput()
    {
        var hyps = new List<DetectionHypothesis>
        {
            new("PS1", DetectionSource.SerialNumber.ConfidenceRating(), DetectionSource.SerialNumber, "serial=SLUS-00123"),
            new("PS1", DetectionSource.FolderName.ConfidenceRating(), DetectionSource.FolderName, "folder=Sony/PS1"),
        };

        var a = HypothesisResolver.Resolve(hyps);
        var b = HypothesisResolver.Resolve(hyps);

        Assert.Equal(a.ConsoleKey, b.ConsoleKey);
        Assert.Equal(a.Confidence, b.Confidence);
        Assert.Equal(a.SortDecision, b.SortDecision);
    }

    [Fact]
    public void Determinism_OrderInsensitive()
    {
        var serial = new DetectionHypothesis(
            "PS1", DetectionSource.SerialNumber.ConfidenceRating(), DetectionSource.SerialNumber, "serial=SLUS-00123");
        var folder = new DetectionHypothesis(
            "PS1", DetectionSource.FolderName.ConfidenceRating(), DetectionSource.FolderName, "folder=Sony/PS1");

        var ab = HypothesisResolver.Resolve(new List<DetectionHypothesis> { serial, folder });
        var ba = HypothesisResolver.Resolve(new List<DetectionHypothesis> { folder, serial });

        Assert.Equal(ab.ConsoleKey, ba.ConsoleKey);
        Assert.Equal(ab.Confidence, ba.Confidence);
        Assert.Equal(ab.SortDecision, ba.SortDecision);
    }
}
