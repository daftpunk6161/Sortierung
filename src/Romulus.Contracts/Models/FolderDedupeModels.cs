namespace Romulus.Contracts.Models;

/// <summary>
/// Result of PS3 folder deduplication (hash-based).
/// </summary>
public sealed class Ps3FolderDedupeResult
{
    public int Total { get; init; }
    public int Dupes { get; init; }
    public int Moved { get; init; }
    public int Skipped { get; init; }
}

/// <summary>
/// Result of base-name folder deduplication.
/// </summary>
public sealed class FolderDedupeResult
{
    public int TotalFolders { get; init; }
    public int DupeGroups { get; init; }
    public int Moved { get; init; }
    public int Skipped { get; init; }
    public int Errors { get; init; }
    public string Mode { get; init; } = "DryRun";
    public IReadOnlyList<FolderDedupeAction> Actions { get; init; } = [];
}

/// <summary>
/// A single folder dedupe action (move or dry-run).
/// </summary>
public sealed record FolderDedupeAction
{
    public string Key { get; init; } = "";
    public string Source { get; init; } = "";
    public string Dest { get; init; } = "";
    public string Winner { get; init; } = "";
    public string Action { get; init; } = "";
    public string? Error { get; init; }
}

/// <summary>
/// Combined auto-dedupe result dispatching PS3 and base-name strategies.
/// </summary>
public sealed class AutoFolderDedupeResult
{
    public IReadOnlyList<string> Ps3Roots { get; init; } = [];
    public IReadOnlyList<string> FolderRoots { get; init; } = [];
    public string Mode { get; init; } = "DryRun";
    public IReadOnlyList<AutoFolderDedupeEntry> Results { get; init; } = [];
}

public sealed class AutoFolderDedupeEntry
{
    public string Type { get; init; } = "";
    public IReadOnlyList<string> Roots { get; init; } = [];
    public object? Result { get; init; }
}
