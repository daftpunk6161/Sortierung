namespace Romulus.Contracts.Models;

/// <summary>
/// Canonical projection of a single DAT-backed match for a given hash.
/// Returned in <see cref="System.Collections.Generic.IReadOnlyList{T}"/> form by
/// <c>DatRepositoryAdapter.LookupByHash</c> to give downstream callers an explicit
/// multi-match surface; legacy single-match consumers route through
/// <c>DatMatchSelector.SelectSingle</c>.
/// </summary>
public sealed record DatMatch(
    string ConsoleKey,
    string GameName,
    string? RomFileName,
    bool IsBios,
    string? ParentGameName,
    string HashType,
    string? SourceId = null);
