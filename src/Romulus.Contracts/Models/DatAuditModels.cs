namespace Romulus.Contracts.Models;

/// <summary>
/// DAT-based audit status for a ROM candidate.
/// </summary>
public enum DatAuditStatus
{
    Have,
    HaveWrongName,
    Miss,
    Unknown,
    Ambiguous
}

/// <summary>
/// Single DAT audit entry for one ROM candidate.
/// </summary>
/// <param name="FilePath">Absolute ROM file path.</param>
/// <param name="Hash">ROM hash used for DAT lookup.</param>
/// <param name="Status">Audit status.</param>
/// <param name="DatGameName">Matched DAT game name, if available.</param>
/// <param name="DatRomFileName">Matched DAT rom filename, if available.</param>
/// <param name="ConsoleKey">Console key context.</param>
/// <param name="Confidence">Classifier confidence (0-100).</param>
public sealed record DatAuditEntry(
    string FilePath,
    string Hash,
    DatAuditStatus Status,
    string? DatGameName,
    string? DatRomFileName,
    string ConsoleKey,
    int Confidence);

/// <summary>
/// Aggregated DAT audit result for a full candidate set.
/// </summary>
/// <param name="Entries">Per-file audit entries.</param>
/// <param name="HaveCount">Count of Have entries.</param>
/// <param name="HaveWrongNameCount">Count of HaveWrongName entries.</param>
/// <param name="MissCount">Count of Miss entries.</param>
/// <param name="UnknownCount">Count of Unknown entries.</param>
/// <param name="AmbiguousCount">Count of Ambiguous entries.</param>
public sealed record DatAuditResult(
    IReadOnlyList<DatAuditEntry> Entries,
    int HaveCount,
    int HaveWrongNameCount,
    int MissCount,
    int UnknownCount,
    int AmbiguousCount);

/// <summary>
/// DAT-driven rename proposal.
/// </summary>
/// <param name="SourcePath">Current absolute source file path.</param>
/// <param name="TargetFileName">Proposed target filename (without directory).</param>
/// <param name="Status">Source DAT status used for decision.</param>
/// <param name="ConflictReason">Reason when rename is blocked or skipped.</param>
public sealed record DatRenameProposal(
    string SourcePath,
    string TargetFileName,
    DatAuditStatus Status,
    string? ConflictReason);
