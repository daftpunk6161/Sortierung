namespace Romulus.Contracts.Models;

/// <summary>
/// Input item for quarantine evaluation.
/// </summary>
public sealed class QuarantineItem
{
    public string FilePath { get; set; } = "";
    public string Console { get; set; } = "";
    public string Format { get; set; } = "";
    public string DatStatus { get; set; } = "";
    public string Category { get; set; } = "";
    public string HeaderStatus { get; set; } = "";
}

/// <summary>
/// Custom quarantine rule: if Item[Field] == Value → quarantine.
/// </summary>
public sealed class QuarantineRule
{
    public string Field { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>
/// Result of quarantine candidate check.
/// </summary>
public sealed class QuarantineCandidateResult
{
    public bool IsCandidate { get; set; }
    public List<string> Reasons { get; set; } = new();
    public QuarantineItem Item { get; set; } = new();
}

/// <summary>
/// A single quarantine action (pending move).
/// </summary>
public sealed class QuarantineAction
{
    public string SourcePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string QuarantineDir { get; set; } = "";
    public List<string> Reasons { get; set; } = new();
    public string Mode { get; set; } = "DryRun";
    public string Status { get; set; } = "Pending";
    public string Timestamp { get; set; } = "";
    public string? Error { get; set; }
}

/// <summary>
/// Result of executing quarantine actions.
/// </summary>
public sealed class QuarantineResult
{
    public int Processed { get; set; }
    public int Moved { get; set; }
    public int Errors { get; set; }
    public List<QuarantineAction> Results { get; set; } = new();
}

/// <summary>
/// File entry in quarantine listing.
/// </summary>
public sealed class QuarantineFileEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }
}

/// <summary>
/// Quarantine directory contents.
/// </summary>
public sealed class QuarantineContents
{
    public List<QuarantineFileEntry> Files { get; set; } = new();
    public long TotalSize { get; set; }
    public double TotalSizeMB { get; set; }
    public Dictionary<string, List<QuarantineFileEntry>> DateGroups { get; set; } = new();
}

/// <summary>
/// Result of a quarantine restore operation.
/// </summary>
public sealed class QuarantineRestoreResult
{
    public string Status { get; set; } = "";
    public string? Reason { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}
