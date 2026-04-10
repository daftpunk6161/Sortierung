namespace Romulus.UI.Wpf.Models;

/// <summary>GUI-071: Console distribution bar item for the Analyse dashboard.</summary>
public sealed class ConsoleDistributionItem
{
    public required string ConsoleKey { get; init; }
    public required string DisplayName { get; init; }
    public required int FileCount { get; init; }
    /// <summary>0.0–1.0 fraction for bar width.</summary>
    public required double Fraction { get; init; }
}
