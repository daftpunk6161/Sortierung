using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Wave 5 — T-W5-BEFORE-AFTER-SIMULATOR. Produces a deterministic before/after
/// projection of a planned run. Implementations MUST drive the projection from
/// the canonical <c>RunOrchestrator</c>/<c>PhasePlanBuilder</c> (DryRun mode);
/// a parallel plan implementation would violate the "eine fachliche Wahrheit"
/// invariant and is forbidden.
/// </summary>
public interface IBeforeAfterSimulator
{
    /// <summary>
    /// Run the underlying plan in DryRun mode and project the resulting
    /// <see cref="RunResult"/> into a flat before/after item list plus summary.
    /// The caller's <see cref="RunOptions.Mode"/> is overridden to
    /// <c>RunConstants.ModeDryRun</c> so simulation is always side-effect-free.
    /// </summary>
    BeforeAfterSimulationResult Simulate(RunOptions options, CancellationToken cancellationToken = default);
}
