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
        (RunConstants.Phases.Preflight, 5),
        (RunConstants.Phases.Scan, 20),
        (RunConstants.Phases.Filter, 30),
        (RunConstants.Phases.Dedupe, 45),
        (RunConstants.Phases.Junk, 60),
        (RunConstants.Phases.Move, 75),
        (RunConstants.Phases.Sort, 85),
        (RunConstants.Phases.Convert, 92),
        (RunConstants.Phases.Report, 97),
    ];
}
