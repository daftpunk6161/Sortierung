namespace Romulus.Contracts.Models;

/// <summary>
/// Tool constraints needed to execute a conversion capability.
/// </summary>
public sealed record ToolRequirement
{
    /// <summary>Tool identifier used by IToolRunner.FindTool.</summary>
    public required string ToolName { get; init; }

    /// <summary>Optional expected SHA-256 hash for tool binary pinning.</summary>
    public string? ExpectedHash { get; init; }

    /// <summary>Optional minimum accepted tool version.</summary>
    public string? MinVersion { get; init; }
}

/// <summary>
/// A directed conversion edge from source format to target format.
/// </summary>
public sealed record ConversionCapability
{
    /// <summary>Input extension for this edge (e.g. .iso).</summary>
    public required string SourceExtension { get; init; }

    /// <summary>Output extension for this edge (e.g. .chd).</summary>
    public required string TargetExtension { get; init; }

    /// <summary>Tool requirement for this conversion step.</summary>
    public required ToolRequirement Tool { get; init; }

    /// <summary>Tool command verb (e.g. createcd, convert).</summary>
    public required string Command { get; init; }

    /// <summary>Optional whitelist of applicable console keys.</summary>
    public IReadOnlySet<string>? ApplicableConsoles { get; init; }

    /// <summary>Optional required source integrity.</summary>
    public SourceIntegrity? RequiredSourceIntegrity { get; init; }

    /// <summary>Resulting integrity after this step.</summary>
    public required SourceIntegrity ResultIntegrity { get; init; }

    /// <summary>Whether this edge is lossless.</summary>
    public required bool Lossless { get; init; }

    /// <summary>Relative execution/pathfinding cost.</summary>
    public required int Cost { get; init; }

    /// <summary>Verification method for the produced output.</summary>
    public required VerificationMethod Verification { get; init; }

    /// <summary>Optional human-readable description.</summary>
    public string? Description { get; init; }

    /// <summary>Optional runtime condition for this edge.</summary>
    public ConversionCondition Condition { get; init; } = ConversionCondition.None;
}
