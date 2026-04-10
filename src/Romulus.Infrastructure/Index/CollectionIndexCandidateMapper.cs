using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Index;

/// <summary>
/// Central projection between persisted collection index entries and enriched candidates.
/// This keeps delta rehydration on the same candidate truth instead of recomputing it in entry points.
/// </summary>
public static class CollectionIndexCandidateMapper
{
    public static bool CanReuseCandidate(
        CollectionIndexEntry entry,
        ScannedFileEntry scannedFile,
        string hashType,
        string? enrichmentFingerprint)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(scannedFile);

        if (string.IsNullOrWhiteSpace(enrichmentFingerprint))
            return false;

        if (!string.Equals(entry.Path, scannedFile.Path, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(entry.Root, scannedFile.Root, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(entry.Extension, scannedFile.Extension, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(entry.EnrichmentFingerprint, enrichmentFingerprint, StringComparison.Ordinal))
            return false;

        if (!string.Equals(entry.PrimaryHashType, NormalizeHashType(hashType), StringComparison.Ordinal))
            return false;

        if (!scannedFile.SizeBytes.HasValue || !scannedFile.LastWriteUtc.HasValue)
            return false;

        return entry.SizeBytes == scannedFile.SizeBytes.Value
               && NormalizeUtc(entry.LastWriteUtc) == NormalizeUtc(scannedFile.LastWriteUtc.Value);
    }

    public static RomCandidate ToCandidate(CollectionIndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new RomCandidate
        {
            MainPath = entry.Path,
            GameKey = entry.GameKey,
            Region = entry.Region,
            RegionScore = entry.RegionScore,
            FormatScore = entry.FormatScore,
            VersionScore = entry.VersionScore,
            HeaderScore = entry.HeaderScore,
            CompletenessScore = entry.CompletenessScore,
            SizeTieBreakScore = entry.SizeTieBreakScore,
            SizeBytes = entry.SizeBytes,
            Extension = entry.Extension,
            ConsoleKey = entry.ConsoleKey,
            DatMatch = entry.DatMatch,
            Hash = entry.PrimaryHash,
            HeaderlessHash = entry.HeaderlessHash,
            DatGameName = entry.DatGameName,
            DatAuditStatus = entry.DatAuditStatus,
            Category = entry.Category,
            ClassificationReasonCode = entry.ClassificationReasonCode,
            ClassificationConfidence = entry.ClassificationConfidence,
            DetectionConfidence = entry.DetectionConfidence,
            DetectionConflict = entry.DetectionConflict,
            HasHardEvidence = entry.HasHardEvidence,
            IsSoftOnly = entry.IsSoftOnly,
            SortDecision = entry.SortDecision,
            DecisionClass = entry.DecisionClass,
            MatchEvidence = entry.MatchEvidence,
            EvidenceTier = entry.EvidenceTier,
            PrimaryMatchKind = entry.PrimaryMatchKind,
            PlatformFamily = entry.PlatformFamily
        };
    }

    public static CollectionIndexEntry ToEntry(
        RomCandidate candidate,
        ScannedFileEntry scannedFile,
        string hashType,
        string enrichmentFingerprint,
        DateTime lastScannedUtc)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(scannedFile);

        var normalizedPath = Path.GetFullPath(candidate.MainPath);
        var normalizedRoot = Path.GetFullPath(scannedFile.Root);
        var effectiveLastWriteUtc = scannedFile.LastWriteUtc.HasValue
            ? NormalizeUtc(scannedFile.LastWriteUtc.Value)
            : default;
        var effectiveSizeBytes = scannedFile.SizeBytes ?? candidate.SizeBytes;

        return new CollectionIndexEntry
        {
            Path = normalizedPath,
            Root = normalizedRoot,
            FileName = Path.GetFileName(normalizedPath),
            Extension = string.IsNullOrWhiteSpace(scannedFile.Extension)
                ? candidate.Extension ?? string.Empty
                : scannedFile.Extension,
            SizeBytes = effectiveSizeBytes,
            LastWriteUtc = effectiveLastWriteUtc,
            LastScannedUtc = NormalizeUtc(lastScannedUtc),
            EnrichmentFingerprint = enrichmentFingerprint ?? string.Empty,
            PrimaryHashType = NormalizeHashType(hashType),
            PrimaryHash = candidate.Hash,
            HeaderlessHash = candidate.HeaderlessHash,
            ConsoleKey = candidate.ConsoleKey,
            GameKey = candidate.GameKey,
            Region = candidate.Region,
            RegionScore = candidate.RegionScore,
            FormatScore = candidate.FormatScore,
            VersionScore = candidate.VersionScore,
            HeaderScore = candidate.HeaderScore,
            CompletenessScore = candidate.CompletenessScore,
            SizeTieBreakScore = candidate.SizeTieBreakScore,
            Category = candidate.Category,
            DatMatch = candidate.DatMatch,
            DatGameName = candidate.DatGameName,
            DatAuditStatus = candidate.DatAuditStatus,
            SortDecision = candidate.SortDecision,
            DecisionClass = candidate.DecisionClass,
            EvidenceTier = candidate.EvidenceTier,
            PrimaryMatchKind = candidate.PrimaryMatchKind,
            DetectionConfidence = candidate.DetectionConfidence,
            DetectionConflict = candidate.DetectionConflict,
            HasHardEvidence = candidate.HasHardEvidence,
            IsSoftOnly = candidate.IsSoftOnly,
            MatchEvidence = candidate.MatchEvidence,
            PlatformFamily = candidate.PlatformFamily,
            ClassificationReasonCode = candidate.ClassificationReasonCode,
            ClassificationConfidence = candidate.ClassificationConfidence
        };
    }

    public static string NormalizeHashType(string hashType)
    {
        if (string.IsNullOrWhiteSpace(hashType))
            return "SHA1";

        return hashType.Trim().ToUpperInvariant() switch
        {
            "CRC" => "CRC32",
            var normalized => normalized
        };
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
