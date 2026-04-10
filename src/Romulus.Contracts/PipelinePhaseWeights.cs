namespace Romulus.Contracts;

/// <summary>
/// Shared pipeline phase progress weights.
/// Used by API ProgressEstimator and potentially by GUI/CLI progress bars.
/// Phase message prefixes (e.g. "[Scan]") are emitted by RunOrchestrator and
/// matched here for progress estimation.
/// </summary>
public static class PipelinePhaseWeights
{
    public static readonly IReadOnlyList<(string Prefix, int ProgressPercent)> Phases =
    [
        ("[Preflight]", 5),
        ("[Scan]", 20),
        ("[Filter]", 30),
        ("[Dedupe]", 45),
        ("[Junk]", 60),
        ("[Move]", 75),
        ("[Sort]", 85),
        ("[Convert]", 92),
        ("[Report]", 97),
    ];
}
