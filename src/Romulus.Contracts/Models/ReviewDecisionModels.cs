namespace Romulus.Contracts.Models;

/// <summary>
/// Persisted approval for a review-gated candidate.
/// This stores only contract-safe review metadata and never UI- or transport-specific state.
/// </summary>
public sealed record ReviewApprovalEntry
{
    /// <summary>Absolute, normalized candidate path that was approved.</summary>
    public string Path { get; init; } = "";

    /// <summary>Observed console key at approval time.</summary>
    public string ConsoleKey { get; init; } = "UNKNOWN";

    /// <summary>Observed sort decision at approval time.</summary>
    public SortDecision SortDecision { get; init; } = SortDecision.Review;

    /// <summary>Observed match level at approval time.</summary>
    public MatchLevel MatchLevel { get; init; } = MatchLevel.None;

    /// <summary>Observed reasoning string at approval time.</summary>
    public string MatchReasoning { get; init; } = string.Empty;

    /// <summary>Short source tag such as api, cli, wpf, or auto-run.</summary>
    public string Source { get; init; } = "manual";

    /// <summary>UTC approval timestamp.</summary>
    public DateTime ApprovedUtc { get; init; }

    /// <summary>
    /// Optional file timestamp captured at approval time.
    /// When set, mismatches indicate stale approvals for changed file content.
    /// </summary>
    public long? FileLastWriteUtcTicks { get; init; }
}
