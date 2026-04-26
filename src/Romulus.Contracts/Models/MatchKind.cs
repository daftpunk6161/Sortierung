namespace Romulus.Contracts.Models;

/// <summary>
/// Describes exactly how a recognition match was established.
/// Used for explainability, audit trail, and trust calibration.
/// </summary>
public enum MatchKind
{
    /// <summary>No match found.</summary>
    None = 0,

    // --- Tier 0: DAT-verified ---

    /// <summary>Exact hash match against DAT index (SHA1/SHA256/MD5).</summary>
    ExactDatHash,

    /// <summary>Archive inner file hash matches DAT entry.</summary>
    ArchiveInnerExactDat,

    /// <summary>Headerless hash matches No-Intro DAT entry.</summary>
    HeaderlessDatHash,

    /// <summary>CHD raw SHA1 matches Redump DAT entry.</summary>
    ChdRawDatHash,

    /// <summary>CHD inner data SHA1 (from chdman info) matches No-Intro DAT entry.</summary>
    ChdDataSha1DatHash,

    // --- Tier 0: DAT-verified, but resolved across consoles ---
    // F-DAT-02: Cross-console variants surface that the matched DAT entry belongs to a
    // different console than the caller's pre-DAT detection (or the console was unknown
    // on entry). The match itself is still hash-exact (Tier 0), but downstream consumers
    // must be able to tell them apart for trust calibration, audit and reporting.

    /// <summary>Exact DAT hash match resolved via cross-console lookup (or from UNKNOWN/AMBIGUOUS).</summary>
    CrossConsoleExactDatHash,

    /// <summary>Archive inner exact DAT hash match resolved via cross-console lookup.</summary>
    CrossConsoleArchiveInnerExactDat,

    /// <summary>Headerless DAT hash match resolved via cross-console lookup.</summary>
    CrossConsoleHeaderlessDatHash,

    /// <summary>CHD raw SHA1 DAT hash match resolved via cross-console lookup.</summary>
    CrossConsoleChdRawDatHash,

    /// <summary>CHD inner data SHA1 DAT hash match resolved via cross-console lookup.</summary>
    CrossConsoleChdDataSha1DatHash,

    // --- Tier 1: Structural ---

    /// <summary>Disc header binary signature (SEGA, PLAYSTATION, etc.).</summary>
    DiscHeaderSignature,

    /// <summary>Cartridge header magic bytes (iNES, N64, SNES, etc.).</summary>
    CartridgeHeaderMagic,

    /// <summary>Serial number extracted from filename or header (SLUS-xxxxx, etc.).</summary>
    SerialNumberMatch,

    /// <summary>CHD metadata tag identifies platform (CHGD = Dreamcast GD-ROM).</summary>
    ChdMetadataTag,

    // --- Tier 2: Strong Heuristic ---

    /// <summary>File extension unique to one console.</summary>
    UniqueExtensionMatch,

    /// <summary>Archive interior extension analysis.</summary>
    ArchiveContentExtension,

    /// <summary>DAT game name match (no hash verification — disc image format).</summary>
    DatNameOnlyMatch,

    // --- Tier 3: Weak Heuristic ---

    /// <summary>Folder path matches a console alias.</summary>
    FolderNameMatch,

    /// <summary>Filename contains system keyword tag.</summary>
    FilenameKeywordMatch,

    /// <summary>Ambiguous extension with single-console resolution.</summary>
    AmbiguousExtensionSingle,

    /// <summary>Filename pattern guess (no structural backing).</summary>
    FilenameGuess,
}

/// <summary>
/// Extension methods for MatchKind to derive evidence tier.
/// </summary>
public static class MatchKindExtensions
{
    /// <summary>
    /// Returns the evidence tier for a given match kind.
    /// </summary>
    public static EvidenceTier GetTier(this MatchKind kind) => kind switch
    {
        MatchKind.ExactDatHash => EvidenceTier.Tier0_ExactDat,
        MatchKind.ArchiveInnerExactDat => EvidenceTier.Tier0_ExactDat,
        MatchKind.HeaderlessDatHash => EvidenceTier.Tier0_ExactDat,
        MatchKind.ChdRawDatHash => EvidenceTier.Tier0_ExactDat,
        MatchKind.ChdDataSha1DatHash => EvidenceTier.Tier0_ExactDat,

        // F-DAT-02: cross-console hash matches stay Tier 0 (still hash-exact) but are
        // distinguishable for downstream trust calibration / reporting.
        MatchKind.CrossConsoleExactDatHash => EvidenceTier.Tier0_ExactDat,
        MatchKind.CrossConsoleArchiveInnerExactDat => EvidenceTier.Tier0_ExactDat,
        MatchKind.CrossConsoleHeaderlessDatHash => EvidenceTier.Tier0_ExactDat,
        MatchKind.CrossConsoleChdRawDatHash => EvidenceTier.Tier0_ExactDat,
        MatchKind.CrossConsoleChdDataSha1DatHash => EvidenceTier.Tier0_ExactDat,

        MatchKind.DiscHeaderSignature => EvidenceTier.Tier1_Structural,
        MatchKind.CartridgeHeaderMagic => EvidenceTier.Tier1_Structural,
        MatchKind.SerialNumberMatch => EvidenceTier.Tier1_Structural,
        MatchKind.ChdMetadataTag => EvidenceTier.Tier1_Structural,

        MatchKind.UniqueExtensionMatch => EvidenceTier.Tier2_StrongHeuristic,
        MatchKind.ArchiveContentExtension => EvidenceTier.Tier2_StrongHeuristic,
        MatchKind.DatNameOnlyMatch => EvidenceTier.Tier2_StrongHeuristic,

        MatchKind.FolderNameMatch => EvidenceTier.Tier3_WeakHeuristic,
        MatchKind.FilenameKeywordMatch => EvidenceTier.Tier3_WeakHeuristic,
        MatchKind.AmbiguousExtensionSingle => EvidenceTier.Tier3_WeakHeuristic,
        MatchKind.FilenameGuess => EvidenceTier.Tier3_WeakHeuristic,

        _ => EvidenceTier.Tier4_Unknown,
    };
}
