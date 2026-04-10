namespace Romulus.Contracts.Models;

/// <summary>
/// Phase timing entry collected during a run.
/// </summary>
public sealed class PhaseMetricEntry
{
    public string Phase { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public int ItemCount { get; set; }
    public string Status { get; set; } = "Completed";
    public double ItemsPerSec { get; set; }
    public double PercentOfTotal { get; set; }
    public Dictionary<string, object> Meta { get; set; } = new();
}

/// <summary>
/// Full metrics result for a run.
/// </summary>
public sealed class PhaseMetricsResult
{
    public string RunId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public List<PhaseMetricEntry> Phases { get; set; } = new();
}

/// <summary>
/// Run history entry (from run plan JSON files).
/// </summary>
public sealed class RunHistoryEntry
{
    public string FileName { get; set; } = "";
    public DateTime Date { get; set; }
    public string[] Roots { get; set; } = Array.Empty<string>();
    public string Mode { get; set; } = "";
    public string Status { get; set; } = "";
    public int FileCount { get; set; }
}

/// <summary>
/// Run history listing result.
/// </summary>
public sealed class RunHistoryResult
{
    public List<RunHistoryEntry> Entries { get; set; } = new();
    public int Total { get; set; }
}

/// <summary>
/// Scan index entry for tracking file state.
/// </summary>
public sealed class ScanIndexEntry
{
    public string Path { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string? Hash { get; set; }
    public DateTime LastScan { get; set; }
}

/// <summary>
/// Link operation for hardlink/symlink mode.
/// </summary>
public sealed class LinkOperation
{
    public string SourcePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public LinkType LinkType { get; set; } = LinkType.Hardlink;
    public string Status { get; set; } = "Pending";
    public string? Error { get; set; }
}

public enum LinkType { Hardlink, Symlink, Junction }

/// <summary>
/// Link structure configuration.
/// </summary>
public sealed class LinkStructureConfig
{
    public string SourceRoot { get; set; } = "";
    public string TargetRoot { get; set; } = "";
    public LinkType LinkType { get; set; } = LinkType.Hardlink;
    public LinkGroupBy GroupBy { get; set; } = LinkGroupBy.Console;
}

public enum LinkGroupBy { Console, Genre, Region, ConsoleAndGenre }

/// <summary>
/// Plan for creating links.
/// </summary>
public sealed class LinkPlan
{
    public LinkStructureConfig Config { get; set; } = new();
    public List<LinkOperation> Operations { get; set; } = new();
    public LinkSavingsEstimate Savings { get; set; } = new();
}

/// <summary>
/// Storage savings estimate from linking.
/// </summary>
public sealed class LinkSavingsEstimate
{
    public long TotalSourceBytes { get; set; }
    public long SavedBytes { get; set; }
    public double SavedPercent { get; set; }
    public int FileCount { get; set; }
}

/// <summary>
/// Statistics for link operations.
/// </summary>
public sealed class LinkStatistics
{
    public int Completed { get; set; }
    public int Pending { get; set; }
    public int Failed { get; set; }
    public int Total { get; set; }
}

/// <summary>
/// Cross-root duplicate group.
/// </summary>
public sealed class CrossRootDuplicateGroup
{
    public string Hash { get; set; } = "";
    public List<CrossRootFile> Files { get; set; } = new();
}

/// <summary>
/// File entry in a cross-root duplicate group.
/// </summary>
public sealed class CrossRootFile
{
    public string Path { get; set; } = "";
    public string Root { get; set; } = "";
    public string Region { get; set; } = "UNKNOWN";
    public string Extension { get; set; } = "";
    public long SizeBytes { get; set; }
    public int RegionScore { get; set; }
    public int FormatScore { get; set; }
    public long VersionScore { get; set; }
    public int HeaderScore { get; set; }
    public int CompletenessScore { get; set; }
    public long SizeTieBreakScore { get; set; }
    public bool DatMatch { get; set; }
    public FileCategory Category { get; set; } = FileCategory.Game;
    public string Hash { get; set; } = "";
}

/// <summary>
/// Merge advice for cross-root duplicates.
/// </summary>
public sealed class CrossRootMergeAdvice
{
    public string Hash { get; set; } = "";
    public CrossRootFile Keep { get; set; } = new();
    public List<CrossRootFile> Remove { get; set; } = new();
}
