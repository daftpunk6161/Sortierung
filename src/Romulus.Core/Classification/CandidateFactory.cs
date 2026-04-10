using Romulus.Contracts.Models;

namespace Romulus.Core.Classification;

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
        ConflictType detectionConflictType = ConflictType.None,
        bool hasHardEvidence = false,
        bool isSoftOnly = true,
        SortDecision sortDecision = SortDecision.Blocked,
        DecisionClass decisionClass = DecisionClass.Unknown,
        MatchEvidence? matchEvidence = null,
        EvidenceTier evidenceTier = EvidenceTier.Tier4_Unknown,
        MatchKind primaryMatchKind = MatchKind.None,
        PlatformFamily platformFamily = PlatformFamily.Unknown,
        string? datGameName = null)
    {
        var biosRegionKey = string.IsNullOrWhiteSpace(region)
            ? "UNKNOWN"
            : region.Trim().ToUpperInvariant();

        var effectiveGameKey = category == FileCategory.Bios
            ? $"__BIOS__{biosRegionKey}__{gameKey}"
            : gameKey;
        var effectiveDecisionClass = decisionClass == DecisionClass.Unknown && sortDecision != SortDecision.Unknown
            ? sortDecision.ToDecisionClass()
            : decisionClass;

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
            DatGameName = datGameName,
            Hash = hash,
            HeaderlessHash = headerlessHash,
            ClassificationReasonCode = classificationReasonCode,
            ClassificationConfidence = classificationConfidence,
            DetectionConfidence = detectionConfidence,
            DetectionConflict = detectionConflict,
            DetectionConflictType = detectionConflictType,
            HasHardEvidence = hasHardEvidence,
            IsSoftOnly = isSoftOnly,
            SortDecision = sortDecision,
            DecisionClass = effectiveDecisionClass,
            MatchEvidence = matchEvidence ?? new MatchEvidence(),
            EvidenceTier = evidenceTier,
            PrimaryMatchKind = primaryMatchKind,
            PlatformFamily = platformFamily,
        };
    }
}
