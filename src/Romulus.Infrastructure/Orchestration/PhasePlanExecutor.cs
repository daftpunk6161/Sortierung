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
            var stepResult = phase.Execute(pipelineState, cancellationToken);

            foreach (var warning in stepResult.Warnings)
                _onProgress?.Invoke($"[WARN] {phase.Name}: {warning}");

            if (string.Equals(stepResult.Status, Contracts.RunConstants.StatusFailed, StringComparison.OrdinalIgnoreCase))
            {
                _onProgress?.Invoke($"[Plan] Phase '{phase.Name}' failed – aborting remaining phases.");
                break;
            }
        }
    }
}
