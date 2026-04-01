using System.Text.Json;
using RomCleanup.Contracts;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using Xunit;

namespace RomCleanup.Tests;

public sealed class CollectionIndexContractTests
{
    [Fact]
    public void CollectionIndexMetadata_DefaultSchemaVersion_IsOne()
    {
        var metadata = new CollectionIndexMetadata();

        Assert.Equal(1, metadata.SchemaVersion);
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
        Assert.Equal("SHA1", entry.PrimaryHashType);
        Assert.Null(entry.PrimaryHash);
        Assert.Equal("UNKNOWN", entry.ConsoleKey);
        Assert.Equal("", entry.GameKey);
        Assert.Equal("UNKNOWN", entry.Region);
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
            PrimaryHashType = "SHA1",
            PrimaryHash = "abcdef0123456789",
            ConsoleKey = "SNES",
            GameKey = "super-mario-world",
            Region = "EU",
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
        Assert.Equal(entry.PrimaryHashType, roundTrip.Entry.PrimaryHashType);
        Assert.Equal(entry.PrimaryHash, roundTrip.Entry.PrimaryHash);
        Assert.Equal(entry.ConsoleKey, roundTrip.Entry.ConsoleKey);
        Assert.Equal(entry.GameKey, roundTrip.Entry.GameKey);
        Assert.Equal(entry.Region, roundTrip.Entry.Region);
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
