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
        string classificationReasonCode = "game-default",
        int classificationConfidence = 100)
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
            ClassificationReasonCode = classificationReasonCode,
            ClassificationConfidence = classificationConfidence
        };
    }
}