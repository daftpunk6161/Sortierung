using RomCleanup.Contracts.Models;

namespace RomCleanup.Core.Classification;

/// <summary>
/// A single detection hypothesis from one detection method.
/// </summary>
/// <param name="ConsoleKey">The detected console key (e.g. "PS1", "NES").</param>
/// <param name="Confidence">Confidence 0–100. Higher = more reliable.</param>
/// <param name="Source">Which detection method produced this hypothesis.</param>
/// <param name="Evidence">Human-readable evidence string (e.g. "folder=Nintendo", "serial=SLUS-00123").</param>
public sealed record DetectionHypothesis(
    string ConsoleKey,
    int Confidence,
    DetectionSource Source,
    string Evidence);

/// <summary>
/// Aggregated console detection result from all methods.
/// </summary>
/// <param name="ConsoleKey">The winning console key, or "UNKNOWN" or "AMBIGUOUS".</param>
/// <param name="Confidence">Aggregate confidence 0–100.</param>
/// <param name="Hypotheses">All hypotheses that contributed.</param>
/// <param name="HasConflict">True if different methods disagree on the console.</param>
/// <param name="ConflictDetail">Description of the conflict, if any.</param>
/// <param name="HasHardEvidence">True if at least one hypothesis came from a hard evidence source.</param>
/// <param name="IsSoftOnly">True if all hypotheses are from soft evidence sources only.</param>
/// <param name="SortDecision">The computed sorting gate decision.</param>
public sealed record ConsoleDetectionResult(
    string ConsoleKey,
    int Confidence,
    IReadOnlyList<DetectionHypothesis> Hypotheses,
    bool HasConflict,
    string? ConflictDetail,
    bool HasHardEvidence = false,
    bool IsSoftOnly = true,
    SortDecision SortDecision = SortDecision.Blocked)
{
    /// <summary>Unknown result with 0 confidence.</summary>
    public static ConsoleDetectionResult Unknown { get; } = new(
        "UNKNOWN", 0, Array.Empty<DetectionHypothesis>(), false, null,
        HasHardEvidence: false, IsSoftOnly: true, SortDecision: SortDecision.Blocked);
}



/// <summary>
/// Detection method source identifiers, ordered by typical reliability.
/// </summary>
public enum DetectionSource
{
    /// <summary>DAT hash match — most reliable (hash-verified content).</summary>
    DatHash = 100,

    /// <summary>Unique file extension — very high reliability.</summary>
    UniqueExtension = 95,

    /// <summary>Disc header binary signature (ISO/CHD magic bytes).</summary>
    DiscHeader = 92,

    /// <summary>Cartridge header binary signature (iNES/Genesis magic bytes).</summary>
    CartridgeHeader = 90,

    /// <summary>Serial number in filename (e.g. SLUS-00123).</summary>
    SerialNumber = 88,

    /// <summary>Folder name matches a console alias.</summary>
    FolderName = 85,

    /// <summary>Archive interior extension (ZIP/7z inner file).</summary>
    ArchiveContent = 80,

    /// <summary>System keyword tag in filename (e.g. [GBA]).</summary>
    FilenameKeyword = 75,

    /// <summary>Ambiguous extension with only one console match.</summary>
    AmbiguousExtension = 40,
}

/// <summary>
/// Extension methods for evidence classification on DetectionSource.
/// Hard evidence = binary/structural signals that can justify sorting alone.
/// Soft evidence = contextual/heuristic signals that require corroboration.
/// </summary>
public static class DetectionSourceExtensions
{
    /// <summary>Whether this source qualifies as hard (structural) evidence.</summary>
    public static bool IsHardEvidence(this DetectionSource source) =>
        source is DetectionSource.DatHash
            or DetectionSource.UniqueExtension
            or DetectionSource.DiscHeader
            or DetectionSource.CartridgeHeader;

    /// <summary>
    /// Maximum confidence when only this single source type is present.
    /// Prevents over-confident sorting from a single weak signal.
    /// </summary>
    public static int SingleSourceCap(this DetectionSource source) => source switch
    {
        DetectionSource.DatHash => 100,
        DetectionSource.UniqueExtension => 95,
        DetectionSource.DiscHeader => 92,
        DetectionSource.CartridgeHeader => 90,
        DetectionSource.SerialNumber => 75,
        DetectionSource.ArchiveContent => 70,
        DetectionSource.FolderName => 65,
        DetectionSource.FilenameKeyword => 60,
        DetectionSource.AmbiguousExtension => 40,
        _ => 60
    };
}
