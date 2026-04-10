namespace Romulus.Contracts.Models;

/// <summary>
/// Shareable, versioned profile settings that can be overlaid onto channel-specific run inputs.
/// Paths stay optional so profiles remain portable across machines.
/// </summary>
public sealed record RunProfileSettings
{
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
    public string? Mode { get; init; }
}

/// <summary>
/// Portable profile document for GUI, CLI, and API.
/// </summary>
public sealed record RunProfileDocument
{
    public int Version { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool BuiltIn { get; init; }
    public string[] Tags { get; init; } = Array.Empty<string>();
    public string? WorkflowScenarioId { get; init; }
    public RunProfileSettings Settings { get; init; } = new();
}

/// <summary>
/// Lightweight profile listing item used by CLI, API, and WPF selectors.
/// </summary>
public sealed record RunProfileSummary(
    string Id,
    string Name,
    string Description,
    bool BuiltIn,
    string[] Tags,
    string? WorkflowScenarioId);
