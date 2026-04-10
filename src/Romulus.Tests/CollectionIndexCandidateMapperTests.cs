using Romulus.Contracts.Models;
using Romulus.Infrastructure.Index;
using Xunit;

namespace Romulus.Tests;

public sealed class CollectionIndexCandidateMapperTests
{
    [Fact]
    public void ToEntry_ThenToCandidate_PreservesCandidateTruth()
    {
        var candidate = new RomCandidate
        {
            MainPath = @"C:\Roms\NES\Game.nes",
            GameKey = "game",
            Region = "US",
            RegionScore = 100,
            FormatScore = 50,
            VersionScore = 7,
            HeaderScore = 2,
            CompletenessScore = 4,
            SizeTieBreakScore = 1234,
            SizeBytes = 1234,
            Extension = ".nes",
            ConsoleKey = "NES",
            DatMatch = true,
            Hash = "abcdef",
            HeaderlessHash = "001122",
            DatGameName = "Game (USA)",
            DatAuditStatus = DatAuditStatus.Have,
            Category = FileCategory.Game,
            ClassificationReasonCode = "dat-hash",
            ClassificationConfidence = 100,
            DetectionConfidence = 95,
            DetectionConflict = false,
            HasHardEvidence = true,
            IsSoftOnly = false,
            SortDecision = SortDecision.DatVerified,
            DecisionClass = DecisionClass.DatVerified,
            MatchEvidence = new MatchEvidence
            {
                Level = MatchLevel.Exact,
                Reasoning = "Exact DAT hash match.",
                Sources = ["DatHash"],
                HasHardEvidence = true,
                DatVerified = true,
                Tier = EvidenceTier.Tier0_ExactDat,
                PrimaryMatchKind = MatchKind.ExactDatHash
            },
            EvidenceTier = EvidenceTier.Tier0_ExactDat,
            PrimaryMatchKind = MatchKind.ExactDatHash,
            PlatformFamily = PlatformFamily.NoIntroCartridge
        };
        var scannedFile = new ScannedFileEntry(
            @"C:\Roms\NES",
            candidate.MainPath,
            ".nes",
            SizeBytes: 1234,
            LastWriteUtc: new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc));

        var entry = CollectionIndexCandidateMapper.ToEntry(
            candidate,
            scannedFile,
            "sha1",
            "fp-a",
            new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc));
        var roundTrip = CollectionIndexCandidateMapper.ToCandidate(entry);

        Assert.Equal("fp-a", entry.EnrichmentFingerprint);
        Assert.Equal("SHA1", entry.PrimaryHashType);
        Assert.Equal(candidate.ConsoleKey, roundTrip.ConsoleKey);
        Assert.Equal(candidate.Hash, roundTrip.Hash);
        Assert.Equal(candidate.HeaderlessHash, roundTrip.HeaderlessHash);
        Assert.Equal(candidate.MatchEvidence.Reasoning, roundTrip.MatchEvidence.Reasoning);
        Assert.Equal(candidate.PlatformFamily, roundTrip.PlatformFamily);
        Assert.Equal(candidate.SortDecision, roundTrip.SortDecision);
    }

    [Fact]
    public void CanReuseCandidate_RequiresMatchingMetadataAndFingerprint()
    {
        var scannedFile = new ScannedFileEntry(
            @"C:\Roms\NES",
            @"C:\Roms\NES\Game.nes",
            ".nes",
            SizeBytes: 1234,
            LastWriteUtc: new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc));
        var entry = new CollectionIndexEntry
        {
            Path = scannedFile.Path,
            Root = scannedFile.Root,
            FileName = "Game.nes",
            Extension = scannedFile.Extension,
            SizeBytes = scannedFile.SizeBytes!.Value,
            LastWriteUtc = scannedFile.LastWriteUtc!.Value,
            LastScannedUtc = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc),
            EnrichmentFingerprint = "fp-a",
            PrimaryHashType = "SHA1"
        };

        Assert.True(CollectionIndexCandidateMapper.CanReuseCandidate(entry, scannedFile, "sha1", "fp-a"));
        Assert.False(CollectionIndexCandidateMapper.CanReuseCandidate(entry, scannedFile with { SizeBytes = 999 }, "sha1", "fp-a"));
        Assert.False(CollectionIndexCandidateMapper.CanReuseCandidate(entry, scannedFile, "sha1", "fp-b"));
        Assert.False(CollectionIndexCandidateMapper.CanReuseCandidate(entry, scannedFile, "md5", "fp-a"));
    }
}
