namespace Romulus.Contracts.Models;

/// <summary>
/// Channel-neutral run draft before settings/default fallback and before RunOptions normalization.
/// Nullable members preserve whether a value was actually supplied by the caller.
/// </summary>
public sealed record RunConfigurationDraft
{
    public string[] Roots { get; init; } = Array.Empty<string>();
    public string? Mode { get; init; }
    public string? WorkflowScenarioId { get; init; }
    public string? ProfileId { get; init; }
    public string[]? PreferRegions { get; init; }
    public string[]? Extensions { get; init; }
    public bool? RemoveJunk { get; init; }
    public bool? OnlyGames { get; init; }
    public bool? KeepUnknownWhenOnlyGames { get; init; }
    public bool? AggressiveJunk { get; init; }
    public bool? SortConsole { get; init; }
    public bool? EnableDat { get; init; }
    public bool? EnableDatAudit { get; init; }
    public bool? EnableDatRename { get; init; }
    public string? DatRoot { get; init; }
    public string? HashType { get; init; }
    public string? ConvertFormat { get; init; }
    public bool? ConvertOnly { get; init; }
    public bool? ApproveReviews { get; init; }
    public bool? ApproveConversionReview { get; init; }
    public string? ConflictPolicy { get; init; }
    public string? TrashRoot { get; init; }
}

/// <summary>
/// Tracks which run settings were explicitly supplied by the caller.
/// This prevents workflow/profile defaults from silently overriding channel input.
/// </summary>
public sealed record RunConfigurationExplicitness
{
    public bool Mode { get; init; }
    public bool PreferRegions { get; init; }
    public bool Extensions { get; init; }
    public bool RemoveJunk { get; init; }
    public bool OnlyGames { get; init; }
    public bool KeepUnknownWhenOnlyGames { get; init; }
    public bool AggressiveJunk { get; init; }
    public bool SortConsole { get; init; }
    public bool EnableDat { get; init; }
    public bool EnableDatAudit { get; init; }
    public bool EnableDatRename { get; init; }
    public bool DatRoot { get; init; }
    public bool HashType { get; init; }
    public bool ConvertFormat { get; init; }
    public bool ConvertOnly { get; init; }
    public bool ApproveReviews { get; init; }
    public bool ApproveConversionReview { get; init; }
    public bool ConflictPolicy { get; init; }
    public bool TrashRoot { get; init; }
}

public sealed record ResolvedRunConfiguration(
    RunConfigurationDraft Draft,
    WorkflowScenarioDefinition? Workflow,
    RunProfileDocument? Profile,
    string? EffectiveProfileId);

public sealed record MaterializedRunConfiguration(
    RunConfigurationDraft EffectiveDraft,
    WorkflowScenarioDefinition? Workflow,
    RunProfileDocument? Profile,
    string? EffectiveProfileId,
    RunOptions Options);
