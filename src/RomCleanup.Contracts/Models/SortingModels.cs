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
    IReadOnlyDictionary<string, int> UnknownReasons);

/// <summary>
/// Result of a ZIP sort operation.
/// </summary>
public sealed record ZipSortResult(
    int Total,
    int Moved,
    int Skipped,
    int Errors);
