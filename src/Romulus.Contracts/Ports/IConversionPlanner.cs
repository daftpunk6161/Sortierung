using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Builds conversion plans without executing tool commands.
/// </summary>
public interface IConversionPlanner
{
    /// <summary>Plans conversion for a single file.</summary>
    ConversionPlan Plan(string sourcePath, string consoleKey, string sourceExtension);

    /// <summary>Plans conversion for a batch of candidates.</summary>
    IReadOnlyList<ConversionPlan> PlanBatch(
        IReadOnlyList<(string Path, string ConsoleKey, string Extension)> candidates);
}
