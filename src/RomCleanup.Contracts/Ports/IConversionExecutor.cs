using RomCleanup.Contracts.Models;

namespace RomCleanup.Contracts.Ports;

/// <summary>
/// Executes conversion plans and reports per-step outcomes.
/// </summary>
public interface IConversionExecutor
{
    /// <summary>Executes a conversion plan.</summary>
    ConversionResult Execute(
        ConversionPlan plan,
        Action<ConversionStep, ConversionStepResult>? onStepComplete = null,
        CancellationToken cancellationToken = default);
}
