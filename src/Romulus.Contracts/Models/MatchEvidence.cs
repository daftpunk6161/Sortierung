namespace Romulus.Contracts.Models;

/// <summary>
/// Evidence strength used for review/sort explainability.
/// </summary>
public enum MatchLevel
{
    None,
    Weak,
    Probable,
    Strong,
    Exact,
    Ambiguous
}

/// <summary>
/// Canonical evidence object describing how a console/category decision was made.
/// </summary>
public sealed record MatchEvidence
{
    public MatchLevel Level { get; init; } = MatchLevel.None;
    public string Reasoning { get; init; } = string.Empty;
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();
    public bool HasHardEvidence { get; init; }
    public bool HasConflict { get; init; }
    public bool DatVerified { get; init; }

    /// <summary>Evidence tier for DAT-first recognition pipeline. Defaults to Unknown.</summary>
    public EvidenceTier Tier { get; init; } = EvidenceTier.Tier4_Unknown;

    /// <summary>How the match was established. Defaults to None.</summary>
    public MatchKind PrimaryMatchKind { get; init; } = MatchKind.None;
}
