namespace Romulus.Infrastructure.Orchestration;

public interface IPhasePlanExecutor
{
    void Execute(
        IReadOnlyList<IPhaseStep> phasePlan,
        PipelineState pipelineState,
        CancellationToken cancellationToken);
}

internal sealed class PhasePlanExecutor(Action<string>? onProgress) : IPhasePlanExecutor
{
    private readonly Action<string>? _onProgress = onProgress;

    public void Execute(
        IReadOnlyList<IPhaseStep> phasePlan,
        PipelineState pipelineState,
        CancellationToken cancellationToken)
    {
        foreach (var phase in phasePlan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _onProgress?.Invoke($"[Plan] Phase: {phase.Name}");
            PhaseStepResult stepResult;
            try
            {
                stepResult = phase.Execute(pipelineState, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                pipelineState.SetFailedPhase(phase.Name, Contracts.RunConstants.StatusFailed);
                _onProgress?.Invoke($"[Plan] Phase '{phase.Name}' failed with exception: {ex.Message}");
                throw;
            }

            foreach (var warning in stepResult.Warnings)
                _onProgress?.Invoke($"[WARN] {phase.Name}: {warning}");

            if (string.Equals(stepResult.Status, Contracts.RunConstants.StatusFailed, StringComparison.OrdinalIgnoreCase))
            {
                pipelineState.SetFailedPhase(phase.Name, stepResult.Status);
                _onProgress?.Invoke($"[Plan] Phase '{phase.Name}' failed - aborting remaining phases.");
                break;
            }
        }
    }
}
