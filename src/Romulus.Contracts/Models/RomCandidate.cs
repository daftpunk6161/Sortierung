namespace Romulus.Contracts.Models;

/// <summary>
/// Represents a candidate ROM item for deduplication scoring.
/// Maps to the SetItem/FileItem hashtable contracts in Sets.ps1.
/// </summary>
public sealed record RomCandidate
{
    public string MainPath { get; init; } = "";
    public string GameKey { get; init; } = "";
    public string Region { get; init; } = "UNKNOWN";
    public int RegionScore { get; init; }
    public int FormatScore { get; init; }
    public long VersionScore { get; init; }
    public int HeaderScore { get; init; }
    public int CompletenessScore { get; init; }
    public long SizeTieBreakScore { get; init; }
    public long SizeBytes { get; init; }
    public string Extension { get; init; } = "";
    public string ConsoleKey { get; init; } = "";
    public bool DatMatch { get; init; }
    public string? Hash { get; init; }
    public string? HeaderlessHash { get; init; }
    public string? DatGameName { get; init; }
    public DatAuditStatus DatAuditStatus { get; init; } = DatAuditStatus.Unknown;
    public FileCategory Category { get; init; } = FileCategory.Game;
    public string ClassificationReasonCode { get; init; } = "game-default";
    public int ClassificationConfidence { get; init; } = 100;

    /// <summary>Console detection confidence (0-100). Higher = more reliable detection.</summary>
    public int DetectionConfidence { get; init; }

    /// <summary>Whether multiple detection methods disagreed on the console.</summary>
    public bool DetectionConflict { get; init; }

    /// <summary>Classification of the detection conflict (none/intra-family/cross-family).</summary>
    public ConflictType DetectionConflictType { get; init; } = ConflictType.None;

    /// <summary>Whether at least one hard evidence source (DAT/Header/UniqueExt) contributed to detection.</summary>
    public bool HasHardEvidence { get; init; }

    /// <summary>Whether detection relied exclusively on soft evidence (Folder/Keyword/AmbiguousExt).</summary>
    public bool IsSoftOnly { get; init; } = true;

    /// <summary>The computed sort gate decision from the detection pipeline.</summary>
    public SortDecision SortDecision { get; init; } = SortDecision.Blocked;

    /// <summary>Decision class used by DAT-first recognition semantics.</summary>
    public DecisionClass DecisionClass { get; init; } = DecisionClass.Unknown;

    /// <summary>Canonical evidence details for explainability and review batching.</summary>
    public MatchEvidence MatchEvidence { get; init; } = new();

    /// <summary>Evidence tier from DAT-first recognition pipeline.</summary>
    public EvidenceTier EvidenceTier { get; init; } = EvidenceTier.Tier4_Unknown;

    /// <summary>How the primary match was established.</summary>
    public MatchKind PrimaryMatchKind { get; init; } = MatchKind.None;

    /// <summary>Platform family for family-based recognition routing.</summary>
    public PlatformFamily PlatformFamily { get; init; } = PlatformFamily.Unknown;
}

/// <summary>
/// Result of a region deduplication run.
/// </summary>
public sealed record DedupeGroup
{
    public RomCandidate Winner { get; init; } = null!;
    public IReadOnlyList<RomCandidate> Losers { get; init; } = Array.Empty<RomCandidate>();
    public string GameKey { get; init; } = "";
    /// <summary>
    /// SEC-DEDUP audit: Number of losers removed because their path was a winner in another group.
    /// Zero means no cross-group filtering occurred.
    /// </summary>
    public int CrossGroupFilteredCount { get; init; }
}
