namespace Romulus.Contracts.Models;

/// <summary>
/// Metadata for the persisted collection index store.
/// This stays storage-agnostic so adapters can back it with JSON, LiteDB, or future implementations.
/// </summary>
public sealed record CollectionIndexMetadata
{
    /// <summary>
    /// Contract/schema version of the persisted collection index representation.
    /// Adapters may use this value for migrations.
    /// </summary>
    public int SchemaVersion { get; init; } = 2;

    /// <summary>
    /// UTC timestamp when the index store was first created.
    /// </summary>
    public DateTime CreatedUtc { get; init; }

    /// <summary>
    /// UTC timestamp when the index store was last updated.
    /// </summary>
    public DateTime UpdatedUtc { get; init; }
}

/// <summary>
/// Persisted snapshot of a collection file state after scanning and enrichment.
/// This model intentionally stores only contract-safe data and never filesystem handles or adapter-specific state.
/// </summary>
public sealed record CollectionIndexEntry
{
    /// <summary>Absolute, normalized path to the indexed file.</summary>
    public string Path { get; init; } = "";

    /// <summary>Absolute, normalized collection root that contains <see cref="Path"/>.</summary>
    public string Root { get; init; } = "";

    /// <summary>File name portion of <see cref="Path"/>.</summary>
    public string FileName { get; init; } = "";

    /// <summary>Extension with leading dot, or empty when unknown.</summary>
    public string Extension { get; init; } = "";

    /// <summary>Observed file size in bytes at the time of indexing.</summary>
    public long SizeBytes { get; init; }

    /// <summary>UTC last-write timestamp used for delta detection.</summary>
    public DateTime LastWriteUtc { get; init; }

    /// <summary>UTC timestamp when this file state was last scanned and persisted.</summary>
    public DateTime LastScannedUtc { get; init; }

    /// <summary>
    /// Fingerprint of the enrichment-relevant runtime semantics (options plus influencing data files).
    /// Delta rehydration is only valid when this fingerprint matches the current run.
    /// </summary>
    public string EnrichmentFingerprint { get; init; } = "";

    /// <summary>Canonical uppercase hash algorithm name such as SHA1 or SHA256.</summary>
    public string PrimaryHashType { get; init; } = "SHA1";

    /// <summary>Lowercase hex hash for <see cref="PrimaryHashType"/>, when available.</summary>
    public string? PrimaryHash { get; init; }

    /// <summary>Lowercase headerless hash when the platform uses headerless DAT matching.</summary>
    public string? HeaderlessHash { get; init; }

    /// <summary>Detected console key or UNKNOWN when unresolved.</summary>
    public string ConsoleKey { get; init; } = "UNKNOWN";

    /// <summary>Normalized game key used by grouping and deduplication.</summary>
    public string GameKey { get; init; } = "";

    /// <summary>Detected preferred region token or UNKNOWN when unresolved.</summary>
    public string Region { get; init; } = "UNKNOWN";

    /// <summary>Resolved region preference score used by winner selection.</summary>
    public int RegionScore { get; init; }

    /// <summary>Resolved format score used by winner selection.</summary>
    public int FormatScore { get; init; }

    /// <summary>Resolved version score used by winner selection.</summary>
    public long VersionScore { get; init; }

    /// <summary>Resolved header score used by winner selection.</summary>
    public int HeaderScore { get; init; }

    /// <summary>Resolved completeness score used by winner selection.</summary>
    public int CompletenessScore { get; init; }

    /// <summary>Resolved size tie-break score used by winner selection.</summary>
    public long SizeTieBreakScore { get; init; }

    /// <summary>Classified content category.</summary>
    public FileCategory Category { get; init; } = FileCategory.Game;

    /// <summary>Whether the file matched the loaded DAT set for its detected console.</summary>
    public bool DatMatch { get; init; }

    /// <summary>Matched DAT game name, when available.</summary>
    public string? DatGameName { get; init; }

    /// <summary>DAT audit status produced by the recognition/audit pipeline.</summary>
    public DatAuditStatus DatAuditStatus { get; init; } = DatAuditStatus.Unknown;

    /// <summary>Current sorting gate decision for this item.</summary>
    public SortDecision SortDecision { get; init; } = SortDecision.Blocked;

    /// <summary>Decision class from the DAT-first recognition model.</summary>
    public DecisionClass DecisionClass { get; init; } = DecisionClass.Unknown;

    /// <summary>Evidence tier assigned by the recognition pipeline.</summary>
    public EvidenceTier EvidenceTier { get; init; } = EvidenceTier.Tier4_Unknown;

    /// <summary>Primary match kind used to reach the recognition outcome.</summary>
    public MatchKind PrimaryMatchKind { get; init; } = MatchKind.None;

    /// <summary>Classifier confidence in the range 0-100.</summary>
    public int DetectionConfidence { get; init; }

    /// <summary>Whether the detection process saw conflicting console hypotheses.</summary>
    public bool DetectionConflict { get; init; }

    /// <summary>Whether at least one hard evidence source contributed to recognition.</summary>
    public bool HasHardEvidence { get; init; }

    /// <summary>Whether recognition relied only on soft evidence.</summary>
    public bool IsSoftOnly { get; init; } = true;

    /// <summary>Canonical evidence object for review, explainability, and parity.</summary>
    public MatchEvidence MatchEvidence { get; init; } = new();

    /// <summary>Resolved platform family for recognition and DAT strategy.</summary>
    public PlatformFamily PlatformFamily { get; init; } = PlatformFamily.Unknown;

    /// <summary>Stable reasoning code used by reports, review, and diagnostics.</summary>
    public string ClassificationReasonCode { get; init; } = "game-default";

    /// <summary>Classification confidence in the range 0-100.</summary>
    public int ClassificationConfidence { get; init; } = 100;
}

/// <summary>
/// Persisted technical hash-cache entry keyed by path, algorithm, size, and last-write time.
/// This is a storage contract for validated reusable hashes, not a business-facing model.
/// </summary>
public sealed record CollectionHashCacheEntry
{
    /// <summary>Absolute, normalized file path.</summary>
    public string Path { get; init; } = "";

    /// <summary>Canonical uppercase hash algorithm name such as SHA1 or SHA256.</summary>
    public string Algorithm { get; init; } = "SHA1";

    /// <summary>Observed file size in bytes for cache validation.</summary>
    public long SizeBytes { get; init; }

    /// <summary>UTC last-write timestamp for cache validation.</summary>
    public DateTime LastWriteUtc { get; init; }

    /// <summary>Lowercase hex hash value.</summary>
    public string Hash { get; init; } = "";

    /// <summary>UTC timestamp when this hash entry was recorded.</summary>
    public DateTime RecordedUtc { get; init; }
}

/// <summary>
/// Persisted run summary for history, trend, and diff features.
/// This snapshot must be derived from existing run truth rather than recomputed by entry points.
/// </summary>
public sealed record CollectionRunSnapshot
{
    /// <summary>Stable run identifier.</summary>
    public string RunId { get; init; } = "";

    /// <summary>UTC run start timestamp.</summary>
    public DateTime StartedUtc { get; init; }

    /// <summary>UTC run completion timestamp.</summary>
    public DateTime CompletedUtc { get; init; }

    /// <summary>Canonical run mode from <see cref="RunConstants"/>.</summary>
    public string Mode { get; init; } = RunConstants.ModeDryRun;

    /// <summary>Canonical run status from <see cref="RunConstants"/>.</summary>
    public string Status { get; init; } = RunConstants.StatusOk;

    /// <summary>Absolute normalized roots included in the run.</summary>
    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();

    /// <summary>Deterministic fingerprint of the participating roots.</summary>
    public string RootFingerprint { get; init; } = "";

    /// <summary>Total runtime in milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary>Total scanned file count.</summary>
    public int TotalFiles { get; init; }

    /// <summary>Total bytes represented by the scanned collection candidates for this run.</summary>
    public long CollectionSizeBytes { get; init; }

    /// <summary>Total kept game count after grouping.</summary>
    public int Games { get; init; }

    /// <summary>Total duplicate/loser count.</summary>
    public int Dupes { get; init; }

    /// <summary>Total junk count in the run projection.</summary>
    public int Junk { get; init; }

    /// <summary>Total DAT-matched file count in the run projection.</summary>
    public int DatMatches { get; init; }

    /// <summary>Total converted file count.</summary>
    public int ConvertedCount { get; init; }

    /// <summary>Total failure count in the run projection.</summary>
    public int FailCount { get; init; }

    /// <summary>Total bytes saved by move/trash operations.</summary>
    public long SavedBytes { get; init; }

    /// <summary>Total bytes saved by conversion.</summary>
    public long ConvertSavedBytes { get; init; }

    /// <summary>Computed collection health score for the run.</summary>
    public int HealthScore { get; init; }
}
