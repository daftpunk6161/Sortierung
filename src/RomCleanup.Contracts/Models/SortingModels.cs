namespace RomCleanup.Contracts.Models;

/// <summary>
/// Result of a console sort operation.
/// </summary>
public sealed record ConsoleSortResult(
    int Total,
    int Moved,
    int SetMembersMoved,
    int Skipped,
    int Unknown,
    IReadOnlyDictionary<string, int> UnknownReasons,
    int Failed = 0,
    int Reviewed = 0,
    int Blocked = 0);

/// <summary>
/// Result of a ZIP sort operation.
/// </summary>
public sealed record ZipSortResult(
    int Total,
    int Moved,
    int Skipped,
    int Errors);
