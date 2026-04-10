using System.Text.Json;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Xunit;

namespace Romulus.Tests;

public sealed class CollectionIndexContractTests
{
    [Fact]
    public void CollectionIndexMetadata_DefaultSchemaVersion_IsTwo()
    {
        var metadata = new CollectionIndexMetadata();

        Assert.Equal(2, metadata.SchemaVersion);
        Assert.Equal(default, metadata.CreatedUtc);
        Assert.Equal(default, metadata.UpdatedUtc);
    }

    [Fact]
    public void CollectionIndexEntry_Defaults_AreDeterministic()
    {
        var entry = new CollectionIndexEntry();

        Assert.Equal("", entry.Path);
        Assert.Equal("", entry.Root);
        Assert.Equal("", entry.FileName);
        Assert.Equal("", entry.Extension);
        Assert.Equal(0, entry.SizeBytes);
        Assert.Equal(default, entry.LastWriteUtc);
        Assert.Equal(default, entry.LastScannedUtc);
        Assert.Equal("", entry.EnrichmentFingerprint);
        Assert.Equal("SHA1", entry.PrimaryHashType);
        Assert.Null(entry.PrimaryHash);
        Assert.Null(entry.HeaderlessHash);
        Assert.Equal("UNKNOWN", entry.ConsoleKey);
        Assert.Equal("", entry.GameKey);
        Assert.Equal("UNKNOWN", entry.Region);
        Assert.Equal(0, entry.RegionScore);
        Assert.Equal(0, entry.FormatScore);
        Assert.Equal(0, entry.VersionScore);
        Assert.Equal(0, entry.HeaderScore);
        Assert.Equal(0, entry.CompletenessScore);
        Assert.Equal(0, entry.SizeTieBreakScore);
        Assert.Equal(FileCategory.Game, entry.Category);
        Assert.False(entry.DatMatch);
        Assert.Null(entry.DatGameName);
        Assert.Equal(DatAuditStatus.Unknown, entry.DatAuditStatus);
        Assert.Equal(SortDecision.Blocked, entry.SortDecision);
        Assert.Equal(DecisionClass.Unknown, entry.DecisionClass);
        Assert.Equal(EvidenceTier.Tier4_Unknown, entry.EvidenceTier);
        Assert.Equal(MatchKind.None, entry.PrimaryMatchKind);
        Assert.Equal(0, entry.DetectionConfidence);
        Assert.False(entry.DetectionConflict);
        Assert.False(entry.HasHardEvidence);
        Assert.True(entry.IsSoftOnly);
        Assert.Equal(MatchLevel.None, entry.MatchEvidence.Level);
        Assert.Equal(PlatformFamily.Unknown, entry.PlatformFamily);
        Assert.Equal("game-default", entry.ClassificationReasonCode);
        Assert.Equal(100, entry.ClassificationConfidence);
    }

    [Fact]
    public void CollectionHashCacheEntry_Defaults_AreDeterministic()
    {
        var entry = new CollectionHashCacheEntry();

        Assert.Equal("", entry.Path);
        Assert.Equal("SHA1", entry.Algorithm);
        Assert.Equal(0, entry.SizeBytes);
        Assert.Equal(default, entry.LastWriteUtc);
        Assert.Equal("", entry.Hash);
        Assert.Equal(default, entry.RecordedUtc);
    }

    [Fact]
    public void CollectionRunSnapshot_Defaults_UseRunConstants()
    {
        var snapshot = new CollectionRunSnapshot();

        Assert.Equal("", snapshot.RunId);
        Assert.Equal(default, snapshot.StartedUtc);
        Assert.Equal(default, snapshot.CompletedUtc);
        Assert.Equal(RunConstants.ModeDryRun, snapshot.Mode);
        Assert.Equal(RunConstants.StatusOk, snapshot.Status);
        Assert.Empty(snapshot.Roots);
        Assert.Equal("", snapshot.RootFingerprint);
        Assert.Equal(0, snapshot.DurationMs);
        Assert.Equal(0, snapshot.TotalFiles);
        Assert.Equal(0, snapshot.CollectionSizeBytes);
        Assert.Equal(0, snapshot.Games);
        Assert.Equal(0, snapshot.Dupes);
        Assert.Equal(0, snapshot.Junk);
        Assert.Equal(0, snapshot.DatMatches);
        Assert.Equal(0, snapshot.ConvertedCount);
        Assert.Equal(0, snapshot.FailCount);
        Assert.Equal(0, snapshot.SavedBytes);
        Assert.Equal(0, snapshot.ConvertSavedBytes);
        Assert.Equal(0, snapshot.HealthScore);
    }

    [Fact]
    public void CollectionIndexModels_SystemTextJson_RoundTrip_PreservesFields()
    {
        var metadata = new CollectionIndexMetadata
        {
            CreatedUtc = new DateTime(2026, 4, 1, 10, 15, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        var entry = new CollectionIndexEntry
        {
            Path = @"C:\Roms\SNES\Mario.sfc",
            Root = @"C:\Roms\SNES",
            FileName = "Mario.sfc",
            Extension = ".sfc",
            SizeBytes = 1024,
            LastWriteUtc = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc),
            LastScannedUtc = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc),
            EnrichmentFingerprint = "fingerprint-a",
            PrimaryHashType = "SHA1",
            PrimaryHash = "abcdef0123456789",
            HeaderlessHash = "00112233",
            ConsoleKey = "SNES",
            GameKey = "super-mario-world",
            Region = "EU",
            RegionScore = 100,
            FormatScore = 50,
            VersionScore = 7,
            HeaderScore = 10,
            CompletenessScore = 4,
            SizeTieBreakScore = 1024,
            Category = FileCategory.Game,
            DatMatch = true,
            DatGameName = "Super Mario World (Europe)",
            DatAuditStatus = DatAuditStatus.Have,
            SortDecision = SortDecision.DatVerified,
            DecisionClass = DecisionClass.DatVerified,
            EvidenceTier = EvidenceTier.Tier0_ExactDat,
            PrimaryMatchKind = MatchKind.ExactDatHash,
            DetectionConfidence = 100,
            DetectionConflict = false,
            HasHardEvidence = true,
            IsSoftOnly = false,
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
            PlatformFamily = PlatformFamily.NoIntroCartridge,
            ClassificationReasonCode = "dat-hash",
            ClassificationConfidence = 100
        };

        var hashEntry = new CollectionHashCacheEntry
        {
            Path = @"C:\Roms\SNES\Mario.sfc",
            Algorithm = "SHA256",
            SizeBytes = 1024,
            LastWriteUtc = new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc),
            Hash = "00112233445566778899aabbccddeeff",
            RecordedUtc = new DateTime(2026, 4, 1, 9, 5, 0, DateTimeKind.Utc)
        };

        var snapshot = new CollectionRunSnapshot
        {
            RunId = "run-123",
            StartedUtc = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc),
            CompletedUtc = new DateTime(2026, 4, 1, 9, 2, 0, DateTimeKind.Utc),
            Mode = RunConstants.ModeMove,
            Status = RunConstants.StatusCompletedWithErrors,
            Roots = ["C:\\Roms\\SNES", "D:\\Roms\\NES"],
            RootFingerprint = "ABCDEF",
            DurationMs = 120000,
            TotalFiles = 200,
            CollectionSizeBytes = 123456789,
            Games = 150,
            Dupes = 25,
            Junk = 10,
            DatMatches = 140,
            ConvertedCount = 12,
            FailCount = 3,
            SavedBytes = 4096,
            ConvertSavedBytes = 8192,
            HealthScore = 91
        };

        var payload = new ContractRoundTripPayload
        {
            Metadata = metadata,
            Entry = entry,
            HashEntry = hashEntry,
            Snapshot = snapshot
        };

        var json = JsonSerializer.Serialize(payload);
        var roundTrip = JsonSerializer.Deserialize<ContractRoundTripPayload>(json);

        Assert.NotNull(roundTrip);
        Assert.NotNull(roundTrip.Metadata);
        Assert.NotNull(roundTrip.Entry);
        Assert.NotNull(roundTrip.HashEntry);
        Assert.NotNull(roundTrip.Snapshot);

        Assert.Equal(metadata.SchemaVersion, roundTrip.Metadata.SchemaVersion);
        Assert.Equal(metadata.CreatedUtc, roundTrip.Metadata.CreatedUtc);
        Assert.Equal(metadata.UpdatedUtc, roundTrip.Metadata.UpdatedUtc);

        Assert.Equal(entry.Path, roundTrip.Entry.Path);
        Assert.Equal(entry.Root, roundTrip.Entry.Root);
        Assert.Equal(entry.FileName, roundTrip.Entry.FileName);
        Assert.Equal(entry.Extension, roundTrip.Entry.Extension);
        Assert.Equal(entry.SizeBytes, roundTrip.Entry.SizeBytes);
        Assert.Equal(entry.LastWriteUtc, roundTrip.Entry.LastWriteUtc);
        Assert.Equal(entry.LastScannedUtc, roundTrip.Entry.LastScannedUtc);
        Assert.Equal(entry.EnrichmentFingerprint, roundTrip.Entry.EnrichmentFingerprint);
        Assert.Equal(entry.PrimaryHashType, roundTrip.Entry.PrimaryHashType);
        Assert.Equal(entry.PrimaryHash, roundTrip.Entry.PrimaryHash);
        Assert.Equal(entry.HeaderlessHash, roundTrip.Entry.HeaderlessHash);
        Assert.Equal(entry.ConsoleKey, roundTrip.Entry.ConsoleKey);
        Assert.Equal(entry.GameKey, roundTrip.Entry.GameKey);
        Assert.Equal(entry.Region, roundTrip.Entry.Region);
        Assert.Equal(entry.RegionScore, roundTrip.Entry.RegionScore);
        Assert.Equal(entry.FormatScore, roundTrip.Entry.FormatScore);
        Assert.Equal(entry.VersionScore, roundTrip.Entry.VersionScore);
        Assert.Equal(entry.HeaderScore, roundTrip.Entry.HeaderScore);
        Assert.Equal(entry.CompletenessScore, roundTrip.Entry.CompletenessScore);
        Assert.Equal(entry.SizeTieBreakScore, roundTrip.Entry.SizeTieBreakScore);
        Assert.Equal(entry.Category, roundTrip.Entry.Category);
        Assert.Equal(entry.DatMatch, roundTrip.Entry.DatMatch);
        Assert.Equal(entry.DatGameName, roundTrip.Entry.DatGameName);
        Assert.Equal(entry.DatAuditStatus, roundTrip.Entry.DatAuditStatus);
        Assert.Equal(entry.SortDecision, roundTrip.Entry.SortDecision);
        Assert.Equal(entry.DecisionClass, roundTrip.Entry.DecisionClass);
        Assert.Equal(entry.EvidenceTier, roundTrip.Entry.EvidenceTier);
        Assert.Equal(entry.PrimaryMatchKind, roundTrip.Entry.PrimaryMatchKind);
        Assert.Equal(entry.DetectionConfidence, roundTrip.Entry.DetectionConfidence);
        Assert.Equal(entry.DetectionConflict, roundTrip.Entry.DetectionConflict);
        Assert.Equal(entry.HasHardEvidence, roundTrip.Entry.HasHardEvidence);
        Assert.Equal(entry.IsSoftOnly, roundTrip.Entry.IsSoftOnly);
        Assert.Equal(entry.MatchEvidence.Level, roundTrip.Entry.MatchEvidence.Level);
        Assert.Equal(entry.MatchEvidence.Reasoning, roundTrip.Entry.MatchEvidence.Reasoning);
        Assert.Equal(entry.MatchEvidence.Sources, roundTrip.Entry.MatchEvidence.Sources);
        Assert.Equal(entry.PlatformFamily, roundTrip.Entry.PlatformFamily);
        Assert.Equal(entry.ClassificationReasonCode, roundTrip.Entry.ClassificationReasonCode);
        Assert.Equal(entry.ClassificationConfidence, roundTrip.Entry.ClassificationConfidence);

        Assert.Equal(hashEntry.Path, roundTrip.HashEntry.Path);
        Assert.Equal(hashEntry.Algorithm, roundTrip.HashEntry.Algorithm);
        Assert.Equal(hashEntry.SizeBytes, roundTrip.HashEntry.SizeBytes);
        Assert.Equal(hashEntry.LastWriteUtc, roundTrip.HashEntry.LastWriteUtc);
        Assert.Equal(hashEntry.Hash, roundTrip.HashEntry.Hash);
        Assert.Equal(hashEntry.RecordedUtc, roundTrip.HashEntry.RecordedUtc);

        Assert.Equal(snapshot.RunId, roundTrip.Snapshot.RunId);
        Assert.Equal(snapshot.StartedUtc, roundTrip.Snapshot.StartedUtc);
        Assert.Equal(snapshot.CompletedUtc, roundTrip.Snapshot.CompletedUtc);
        Assert.Equal(snapshot.Mode, roundTrip.Snapshot.Mode);
        Assert.Equal(snapshot.Status, roundTrip.Snapshot.Status);
        Assert.Equal(snapshot.RootFingerprint, roundTrip.Snapshot.RootFingerprint);
        Assert.Equal(snapshot.DurationMs, roundTrip.Snapshot.DurationMs);
        Assert.Equal(snapshot.TotalFiles, roundTrip.Snapshot.TotalFiles);
        Assert.Equal(snapshot.CollectionSizeBytes, roundTrip.Snapshot.CollectionSizeBytes);
        Assert.Equal(snapshot.Games, roundTrip.Snapshot.Games);
        Assert.Equal(snapshot.Dupes, roundTrip.Snapshot.Dupes);
        Assert.Equal(snapshot.Junk, roundTrip.Snapshot.Junk);
        Assert.Equal(snapshot.DatMatches, roundTrip.Snapshot.DatMatches);
        Assert.Equal(snapshot.ConvertedCount, roundTrip.Snapshot.ConvertedCount);
        Assert.Equal(snapshot.FailCount, roundTrip.Snapshot.FailCount);
        Assert.Equal(snapshot.SavedBytes, roundTrip.Snapshot.SavedBytes);
        Assert.Equal(snapshot.ConvertSavedBytes, roundTrip.Snapshot.ConvertSavedBytes);
        Assert.Equal(snapshot.HealthScore, roundTrip.Snapshot.HealthScore);
        Assert.Equal(snapshot.Roots, roundTrip.Snapshot.Roots);
    }

    [Fact]
    public void ICollectionIndex_Port_ExposesExpectedContractSurface()
    {
        var methods = typeof(ICollectionIndex).GetMethods()
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
        [
            "AppendRunSnapshotAsync",
            "CountEntriesAsync",
            "CountRunSnapshotsAsync",
            "GetByPathsAsync",
            "GetMetadataAsync",
            "ListByConsoleAsync",
            "ListEntriesInScopeAsync",
            "ListRunSnapshotsAsync",
            "RemovePathsAsync",
            "SetHashAsync",
            "TryGetByPathAsync",
            "TryGetHashAsync",
            "UpsertEntriesAsync"
        ], methods);
    }

    private sealed record ContractRoundTripPayload
    {
        public CollectionIndexMetadata Metadata { get; init; } = new();
        public CollectionIndexEntry Entry { get; init; } = new();
        public CollectionHashCacheEntry HashEntry { get; init; } = new();
        public CollectionRunSnapshot Snapshot { get; init; } = new();
    }
}
