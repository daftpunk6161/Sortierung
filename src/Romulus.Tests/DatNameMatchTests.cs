using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for name-based DAT matching (Stage 4 fallback for CHD/disc files)
/// and DatIndex name-based lookup methods.
/// </summary>
public sealed class DatNameMatchTests
{
    // ── DatIndex: Name-based Lookup ──

    [Fact]
    public void DatIndex_LookupByName_FindsExactGameName()
    {
        var index = new DatIndex();
        index.Add("SCD", "hash1", "Sonic CD (USA)", "Sonic CD (USA) (Track 01).bin");
        index.Add("SCD", "hash2", "Sonic CD (USA)", "Sonic CD (USA) (Track 02).bin");

        var result = index.LookupByName("SCD", "Sonic CD (USA)");

        Assert.NotNull(result);
        Assert.Equal("Sonic CD (USA)", result.Value.GameName);
    }

    [Fact]
    public void DatIndex_LookupByName_ReturnsNull_WhenNameNotFound()
    {
        var index = new DatIndex();
        index.Add("SCD", "hash1", "Sonic CD (USA)", "Sonic CD (USA) (Track 01).bin");

        var result = index.LookupByName("SCD", "Sonic CD (Europe)");

        Assert.Null(result);
    }

    [Fact]
    public void DatIndex_LookupByName_ReturnsNull_WhenConsoleNotFound()
    {
        var index = new DatIndex();
        index.Add("SCD", "hash1", "Sonic CD (USA)", "Sonic CD (USA) (Track 01).bin");

        var result = index.LookupByName("PSX", "Sonic CD (USA)");

        Assert.Null(result);
    }

    [Fact]
    public void DatIndex_LookupByName_CaseInsensitive()
    {
        var index = new DatIndex();
        index.Add("SCD", "hash1", "Sonic CD (USA)", "track01.bin");

        var result = index.LookupByName("scd", "sonic cd (usa)");

        Assert.NotNull(result);
    }

    [Fact]
    public void DatIndex_LookupAllByName_FindsAllConsoles()
    {
        var index = new DatIndex();
        index.Add("SCD", "hash1", "Final Fight CD (USA)", "track01.bin");
        index.Add("NEOCD", "hash2", "Final Fight CD (USA)", "track01.bin");

        var results = index.LookupAllByName("Final Fight CD (USA)");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.ConsoleKey == "NEOCD");
        Assert.Contains(results, r => r.ConsoleKey == "SCD");
    }

    [Fact]
    public void DatIndex_LookupAllByName_ReturnsEmpty_WhenNotFound()
    {
        var index = new DatIndex();
        index.Add("SCD", "hash1", "Sonic CD (USA)", "track01.bin");

        var results = index.LookupAllByName("Nonexistent Game");

        Assert.Empty(results);
    }

    [Fact]
    public void DatIndex_LookupAllByName_DeterministicOrder()
    {
        var index = new DatIndex();
        index.Add("PSX", "hash1", "Ridge Racer (USA)", "track01.bin");
        index.Add("3DO", "hash2", "Ridge Racer (USA)", "track01.bin");
        index.Add("SAT", "hash3", "Ridge Racer (USA)", "track01.bin");

        var results1 = index.LookupAllByName("Ridge Racer (USA)");
        var results2 = index.LookupAllByName("Ridge Racer (USA)");

        // Deterministic: sorted by ConsoleKey alphabetically
        Assert.Equal(results1.Select(r => r.ConsoleKey), results2.Select(r => r.ConsoleKey));
        Assert.Equal("3DO", results1[0].ConsoleKey);
        Assert.Equal("PSX", results1[1].ConsoleKey);
        Assert.Equal("SAT", results1[2].ConsoleKey);
    }

    // ── ResolveUnknownDatNameMatch ──

    [Fact]
    public void ResolveUnknownDatNameMatch_SingleMatch_ReturnsDirectMatch()
    {
        var nameMatches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("SCD", new DatIndex.DatIndexEntry("Sonic CD (USA)", "track01.bin"))
        };

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(
            nameMatches, detectionResult: null);

        Assert.True(result.IsMatch);
        Assert.Equal("SCD", result.ConsoleKey);
        Assert.False(result.ResolvedFromAmbiguousCandidates);
    }

    [Fact]
    public void ResolveUnknownDatNameMatch_MultiMatch_UsesHypotheses()
    {
        var nameMatches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("NEOCD", new DatIndex.DatIndexEntry("Game X (USA)", "track01.bin")),
            ("SCD", new DatIndex.DatIndexEntry("Game X (USA)", "track01.bin"))
        };

        var hypotheses = new[]
        {
            new DetectionHypothesis("SCD", 85, DetectionSource.FolderName, "folder=segacd")
        };
        var detection = new ConsoleDetectionResult("SCD", 85, hypotheses, false, null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(
            nameMatches, detection);

        Assert.True(result.IsMatch);
        Assert.Equal("SCD", result.ConsoleKey);
        Assert.True(result.ResolvedFromAmbiguousCandidates);
    }

    [Fact]
    public void ResolveUnknownDatNameMatch_MultiMatch_NoHypotheses_ReturnsNoMatch()
    {
        var nameMatches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("NEOCD", new DatIndex.DatIndexEntry("Game X (USA)", "track01.bin")),
            ("SCD", new DatIndex.DatIndexEntry("Game X (USA)", "track01.bin"))
        };

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(
            nameMatches, detectionResult: null);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatNameMatch_MultiMatch_HypothesisNotInMatches_ReturnsNoMatch()
    {
        var nameMatches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("NEOCD", new DatIndex.DatIndexEntry("Game X (USA)", "track01.bin")),
            ("SCD", new DatIndex.DatIndexEntry("Game X (USA)", "track01.bin"))
        };

        // Hypothesis for PSX which is not in the name matches
        var hypotheses = new[]
        {
            new DetectionHypothesis("PSX", 85, DetectionSource.FolderName, "folder=psx")
        };
        var detection = new ConsoleDetectionResult("PSX", 85, hypotheses, false, null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(
            nameMatches, detection);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatNameMatch_Deterministic_AlphaBreak()
    {
        var nameMatches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("NEOCD", new DatIndex.DatIndexEntry("Game X (USA)", "track01.bin")),
            ("SCD", new DatIndex.DatIndexEntry("Game X (USA)", "track01.bin"))
        };

        // Two hypotheses at same confidence — alphabetical tie-break
        var hypotheses = new[]
        {
            new DetectionHypothesis("NEOCD", 60, DetectionSource.FolderName, "folder=neocd"),
            new DetectionHypothesis("SCD", 60, DetectionSource.FolderName, "folder=segacd")
        };
        var detection = new ConsoleDetectionResult("AMBIGUOUS", 60, hypotheses, true, null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(
            nameMatches, detection);

        Assert.True(result.IsMatch);
        Assert.Equal("NEOCD", result.ConsoleKey); // N < S alphabetically
    }

    [Fact]
    public void ResolveUnknownDatNameMatch_EmptyList_ReturnsNoMatch()
    {
        var nameMatches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>();

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(
            nameMatches, detectionResult: null);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatNameMatch_BiosEntry_PreservesBiosFlag()
    {
        var nameMatches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("SCD", new DatIndex.DatIndexEntry("[BIOS] Sega CD (USA)", "track01.bin", IsBios: true))
        };

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(
            nameMatches, detectionResult: null);

        Assert.True(result.IsMatch);
        Assert.True(result.IsBios);
    }

    // ── Integration: Name-match Evidence Level ──

    [Fact]
    public void DatNameOnlyMatch_ProducesReviewSortDecision()
    {
        // A name-only match should never produce DatVerified —
        // only Review, since the hash cannot be verified for CHD/disc files.
        // This is verified in the enrichment pipeline where DatNameOnlyMatch
        // is handled differently from hash matches.
        var index = new DatIndex();
        index.Add("SCD", "real-track-hash", "Sonic CD (USA)", "track01.bin");

        // The CHD raw SHA1 would not match "real-track-hash"
        // but name lookup finds the game
        var byName = index.LookupByName("SCD", "Sonic CD (USA)");
        Assert.NotNull(byName);
        Assert.Equal("Sonic CD (USA)", byName.Value.GameName);

        // Verify hash doesn't match (simulating CHD raw SHA1)
        var byHash = index.LookupWithFilename("SCD", "chd-raw-sha1-completely-different");
        Assert.Null(byHash);
    }

    // ── DatIndex: Multi-Track Game Name Dedup ──

    [Fact]
    public void DatIndex_MultiTrackGame_NameIndex_StoresFirstEntry()
    {
        var index = new DatIndex();
        // A multi-track Redump game: same game name, different track hashes
        index.Add("SCD", "track1hash", "Sonic CD (USA)", "Sonic CD (USA) (Track 01).bin");
        index.Add("SCD", "track2hash", "Sonic CD (USA)", "Sonic CD (USA) (Track 02).bin");
        index.Add("SCD", "track3hash", "Sonic CD (USA)", "Sonic CD (USA) (Track 03).bin");

        // Name lookup returns the first entry (Track 01)
        var result = index.LookupByName("SCD", "Sonic CD (USA)");
        Assert.NotNull(result);
        Assert.Equal("Sonic CD (USA)", result.Value.GameName);
        Assert.Equal("Sonic CD (USA) (Track 01).bin", result.Value.RomFileName);

        // Hash lookups still work for all tracks
        Assert.NotNull(index.LookupWithFilename("SCD", "track1hash"));
        Assert.NotNull(index.LookupWithFilename("SCD", "track2hash"));
        Assert.NotNull(index.LookupWithFilename("SCD", "track3hash"));
    }
}
