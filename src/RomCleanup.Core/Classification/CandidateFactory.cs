using RomCleanup.Contracts.Models;

namespace RomCleanup.Core.Classification;

/// <summary>
/// Centralized factory for creating RomCandidate instances with consistent category mapping
/// and BIOS key isolation.
/// </summary>
public static class CandidateFactory
{
    public static RomCandidate Create(
        string normalizedPath,
        string extension,
        long sizeBytes,
        FileCategory category,
        string gameKey,
        string region,
        int regionScore,
        int formatScore,
        long versionScore,
        int headerScore,
        int completenessScore,
        long sizeTieBreakScore,
        bool datMatch,
        string consoleKey,
        string? hash = null,
        string? headerlessHash = null,
        string classificationReasonCode = "game-default",
        int classificationConfidence = 100,
        int detectionConfidence = 0,
        bool detectionConflict = false,
        bool hasHardEvidence = false,
        bool isSoftOnly = true,
        SortDecision sortDecision = SortDecision.Blocked)
    {
        var effectiveGameKey = category == FileCategory.Bios
            ? $"__BIOS__{gameKey}"
            : gameKey;

        return new RomCandidate
        {
            MainPath = normalizedPath,
            GameKey = effectiveGameKey,
            Region = region,
            Category = category,
            RegionScore = regionScore,
            FormatScore = formatScore,
            VersionScore = versionScore,
            HeaderScore = headerScore,
            CompletenessScore = completenessScore,
            SizeTieBreakScore = sizeTieBreakScore,
            SizeBytes = sizeBytes,
            Extension = extension,
            DatMatch = datMatch,
            ConsoleKey = consoleKey,
            Hash = hash,
            HeaderlessHash = headerlessHash,
            ClassificationReasonCode = classificationReasonCode,
            ClassificationConfidence = classificationConfidence,
            DetectionConfidence = detectionConfidence,
            DetectionConflict = detectionConflict,
            HasHardEvidence = hasHardEvidence,
            IsSoftOnly = isSoftOnly,
            SortDecision = sortDecision
        };
    }
}