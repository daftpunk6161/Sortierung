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
    public void ResolveUnknownDatMatch_IgnoresSentinelConsoleKeys()
    {
        var index = new DatIndex();
        index.Add("UNKNOWN", "abc123", "Phantom");

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(index, "abc123", null);

        Assert.False(result.IsMatch);
        Assert.Null(result.ConsoleKey);
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
    public void ResolveUnknownDatNameMatch_IgnoresSentinelConsoleKeys()
    {
        var matches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("UNKNOWN", new DatIndex.DatIndexEntry("Phantom", null, false, null))
        };

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(matches, null);

        Assert.False(result.IsMatch);
        Assert.Null(result.ConsoleKey);
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

    // ═══ TryCrossConsoleDatLookup – Extension Plausibility Guard ═══════

    [Fact]
    public void TryCrossConsoleDatLookup_N64FileMatchedToNes_RejectedByExtensionGuard()
    {
        // Regression: .n64 files were incorrectly assigned to NES via single DAT-hash match
        var datIndex = new DatIndex();
        datIndex.Add("NES", "hash_n64_game", "1080 Snowboarding");

        var detector = new ConsoleDetector([
            new ConsoleInfo(Key: "N64", DisplayName: "Nintendo 64", DiscBased: false,
                UniqueExts: [".n64", ".z64", ".v64"], AmbigExts: [], FolderAliases: ["N64"],
                Family: PlatformFamily.NoIntroCartridge),
            new ConsoleInfo(Key: "NES", DisplayName: "Nintendo Entertainment System", DiscBased: false,
                UniqueExts: [".nes"], AmbigExts: [], FolderAliases: ["NES"],
                Family: PlatformFamily.NoIntroCartridge)
        ]);

        var result = EnrichmentPipelinePhase.TryCrossConsoleDatLookup(
            datIndex, "hash_n64_game", "UNKNOWN", null,
            @"C:\roms\1080 TenEighty Snowboarding (Japan, USA) (En,Ja).n64",
            detector, null);

        Assert.False(result.IsMatch, "DAT match to NES must be rejected when file has .n64 unique extension");
    }

    [Fact]
    public void TryCrossConsoleDatLookup_MatchingExtension_Accepted()
    {
        var datIndex = new DatIndex();
        datIndex.Add("NES", "hash_nes_game", "Super Mario Bros");

        var detector = new ConsoleDetector([
            new ConsoleInfo(Key: "NES", DisplayName: "NES", DiscBased: false,
                UniqueExts: [".nes"], AmbigExts: [], FolderAliases: ["NES"],
                Family: PlatformFamily.NoIntroCartridge)
        ]);

        var result = EnrichmentPipelinePhase.TryCrossConsoleDatLookup(
            datIndex, "hash_nes_game", "UNKNOWN", null,
            @"C:\roms\Super Mario Bros (USA).nes",
            detector, null);

        Assert.True(result.IsMatch);
        Assert.Equal("NES", result.ConsoleKey);
    }

    [Fact]
    public void TryCrossConsoleDatLookup_AmbiguousExtension_Accepted()
    {
        // .bin is ambiguous (not a uniqueExt), so guard must NOT reject
        var datIndex = new DatIndex();
        datIndex.Add("GENESIS", "hash_gen_game", "Sonic");

        var detector = new ConsoleDetector([
            new ConsoleInfo(Key: "GENESIS", DisplayName: "Genesis", DiscBased: false,
                UniqueExts: [".md"], AmbigExts: [".bin"], FolderAliases: ["Genesis"],
                Family: PlatformFamily.NoIntroCartridge),
            new ConsoleInfo(Key: "PS1", DisplayName: "PlayStation", DiscBased: true,
                UniqueExts: [".cue"], AmbigExts: [".bin"], FolderAliases: ["PS1"],
                Family: PlatformFamily.RedumpDisc)
        ]);

        var result = EnrichmentPipelinePhase.TryCrossConsoleDatLookup(
            datIndex, "hash_gen_game", "UNKNOWN", null,
            @"C:\roms\Sonic (USA).bin",
            detector, null);

        Assert.True(result.IsMatch, "Ambiguous extensions must not be rejected");
        Assert.Equal("GENESIS", result.ConsoleKey);
    }

    [Fact]
    public void TryCrossConsoleDatLookup_NoDetector_FallsThrough()
    {
        var datIndex = new DatIndex();
        datIndex.Add("NES", "hash_n64_game", "1080 Snowboarding");

        var result = EnrichmentPipelinePhase.TryCrossConsoleDatLookup(
            datIndex, "hash_n64_game", "UNKNOWN", null,
            @"C:\roms\1080 TenEighty Snowboarding.n64",
            null, null);

        // Without detector, no extension guard → match accepted (backward compat)
        Assert.True(result.IsMatch);
        Assert.Equal("NES", result.ConsoleKey);
    }

    [Fact]
    public void TryCrossConsoleDatLookup_FastPath_IgnoresExtensionGuard()
    {
        // Fast path (consoleKey already known and matches DAT) must NOT apply the guard
        var datIndex = new DatIndex();
        datIndex.Add("NES", "hash_game", "Game");

        var detector = new ConsoleDetector([
            new ConsoleInfo(Key: "NES", DisplayName: "NES", DiscBased: false,
                UniqueExts: [".nes"], AmbigExts: [], FolderAliases: ["NES"],
                Family: PlatformFamily.NoIntroCartridge),
            new ConsoleInfo(Key: "N64", DisplayName: "N64", DiscBased: false,
                UniqueExts: [".n64"], AmbigExts: [], FolderAliases: ["N64"],
                Family: PlatformFamily.NoIntroCartridge)
        ]);

        // File has .n64 ext but consoleKey is explicitly "NES" → fast path should match
        var result = EnrichmentPipelinePhase.TryCrossConsoleDatLookup(
            datIndex, "hash_game", "NES", null,
            @"C:\roms\Game.n64",
            detector, null);

        Assert.True(result.IsMatch);
        Assert.Equal("NES", result.ConsoleKey);
    }
}
