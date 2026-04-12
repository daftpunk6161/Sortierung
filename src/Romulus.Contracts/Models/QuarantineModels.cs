namespace Romulus.Contracts.Models;

/// <summary>
/// Input item for quarantine evaluation.
/// </summary>
public sealed class QuarantineItem
{
    public string FilePath { get; init; } = "";
    public string Console { get; init; } = "";
    public string Format { get; init; } = "";
    public string DatStatus { get; init; } = "";
    public string Category { get; init; } = "";
    public string HeaderStatus { get; init; } = "";
}

/// <summary>
/// Custom quarantine rule: if Item[Field] == Value → quarantine.
/// </summary>
public sealed class QuarantineRule
{
    public string Field { get; init; } = "";
    public string Value { get; init; } = "";
}

/// <summary>
/// Result of quarantine candidate check.
/// </summary>
public sealed class QuarantineCandidateResult
{
    public bool IsCandidate { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public QuarantineItem Item { get; init; } = new();
}

/// <summary>
/// A single quarantine action (pending move).
/// </summary>
public sealed class QuarantineAction
{
    public string SourcePath { get; init; } = "";
    public string TargetPath { get; init; } = "";
    public string QuarantineDir { get; init; } = "";
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public string Mode { get; init; } = "DryRun";
    public string Status { get; init; } = "Pending";
    public string Timestamp { get; init; } = "";
    public string? Error { get; init; }
}

/// <summary>
/// Result of executing quarantine actions.
/// </summary>
public sealed class QuarantineResult
{
    public int Processed { get; init; }
    public int Moved { get; init; }
    public int Errors { get; init; }
    public IReadOnlyList<QuarantineAction> Results { get; init; } = Array.Empty<QuarantineAction>();
}

/// <summary>
/// File entry in quarantine listing.
/// </summary>
public sealed class QuarantineFileEntry
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public long Size { get; init; }
}

/// <summary>
/// Quarantine directory contents.
/// </summary>
public sealed class QuarantineContents
{
    public IReadOnlyList<QuarantineFileEntry> Files { get; init; } = Array.Empty<QuarantineFileEntry>();
    public long TotalSize { get; init; }
    public double TotalSizeMB { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<QuarantineFileEntry>> DateGroups { get; init; } =
        new Dictionary<string, IReadOnlyList<QuarantineFileEntry>>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Result of a quarantine restore operation.
/// </summary>
public sealed class QuarantineRestoreResult
{
    public string Status { get; init; } = "";
    public string? Reason { get; init; }
    public string From { get; init; } = "";
    public string To { get; init; } = "";
}
