namespace Romulus.Contracts.Models;

/// <summary>
/// Canonical result of resolving multiple DAT matches for the same lookup.
/// Consumers render this projection; they do not re-rank matches locally.
/// </summary>
public sealed record MultiDatResolution(
    DatMatch? SelectedMatch,
    IReadOnlyList<DatMatch> Candidates,
    bool IsConflict,
    string Reason,
    IReadOnlyList<MultiDatResolutionCandidate> RankedCandidates)
{
    public static MultiDatResolution NoMatch { get; } = new(
        SelectedMatch: null,
        Candidates: Array.Empty<DatMatch>(),
        IsConflict: false,
        Reason: "no-match",
        RankedCandidates: Array.Empty<MultiDatResolutionCandidate>());
}

public sealed record MultiDatResolutionCandidate(
    DatMatch Match,
    int PreferredSourceRank,
    int MatchStrength,
    string RankKey);

public sealed record MultiDatResolutionContext(
    string? ExpectedConsoleKey,
    IReadOnlyDictionary<string, int> ConsoleStrengths,
    IReadOnlyList<string> PreferredSources,
    MatchKind MatchKind)
{
    public static MultiDatResolutionContext Empty { get; } = new(
        ExpectedConsoleKey: null,
        ConsoleStrengths: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        PreferredSources: Array.Empty<string>(),
        MatchKind: MatchKind.None);
}
