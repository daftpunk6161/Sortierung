namespace Romulus.Contracts.Models;

/// <summary>
/// One planned conversion step in a conversion chain.
/// </summary>
public sealed record ConversionStep
{
    /// <summary>Zero-based step order.</summary>
    public required int Order { get; init; }

    /// <summary>Input extension for this step.</summary>
    public required string InputExtension { get; init; }

    /// <summary>Output extension for this step.</summary>
    public required string OutputExtension { get; init; }

    /// <summary>Capability edge chosen for this step.</summary>
    public required ConversionCapability Capability { get; init; }

    /// <summary>Indicates this step produces an intermediate artifact.</summary>
    public required bool IsIntermediate { get; init; }

    /// <summary>Optional expected output path for preview/reporting.</summary>
    public string? ExpectedOutputPath { get; init; }
}

/// <summary>
/// Full conversion plan produced before execution.
/// </summary>
public sealed record ConversionPlan
{
    /// <summary>Original source path.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Detected console key.</summary>
    public required string ConsoleKey { get; init; }

    /// <summary>Policy in effect for this console.</summary>
    public required ConversionPolicy Policy { get; init; }

    /// <summary>Integrity classification of the source.</summary>
    public required SourceIntegrity SourceIntegrity { get; init; }

    /// <summary>Safety level for this full plan.</summary>
    public required ConversionSafety Safety { get; init; }

    /// <summary>Planned ordered conversion steps.</summary>
    public required IReadOnlyList<ConversionStep> Steps { get; init; }

    /// <summary>Machine-readable skip reason when no execution happens.</summary>
    public string? SkipReason { get; init; }

    /// <summary>Final target extension if at least one step exists.</summary>
    public string? FinalTargetExtension => Steps.Count > 0 ? Steps[^1].OutputExtension : null;

    /// <summary>True when plan can be executed automatically.</summary>
    public bool IsExecutable => Steps.Count > 0 && Safety != ConversionSafety.Blocked;

    /// <summary>True when explicit user review/confirmation is required.</summary>
    public bool RequiresReview => Policy == ConversionPolicy.ManualOnly
                               || Safety == ConversionSafety.Risky
                               || SourceIntegrity == SourceIntegrity.Lossy;
}
