namespace RomCleanup.Contracts.Models;

/// <summary>
/// Represents a candidate ROM item for deduplication scoring.
/// Maps to the SetItem/FileItem hashtable contracts in Sets.ps1.
/// </summary>
public sealed class RomCandidate
{
    public string MainPath { get; init; } = "";
    public string GameKey { get; init; } = "";
    public string Region { get; init; } = "UNKNOWN";
    public int RegionScore { get; init; }
    public int FormatScore { get; init; }
    public long VersionScore { get; init; }
    public int HeaderScore { get; init; }
    public int CompletenessScore { get; init; }
    public long SizeTieBreakScore { get; init; }
    public long SizeBytes { get; init; }
    public string Extension { get; init; } = "";
    public string ConsoleKey { get; init; } = "";
    public bool DatMatch { get; init; }
    public FileCategory Category { get; init; } = FileCategory.Game;
    public string ClassificationReasonCode { get; init; } = "game-default";
    public int ClassificationConfidence { get; init; } = 100;
}

/// <summary>
/// Result of a region deduplication run.
/// </summary>
public sealed class DedupeResult
{
    public RomCandidate Winner { get; init; } = null!;
    public IReadOnlyList<RomCandidate> Losers { get; init; } = Array.Empty<RomCandidate>();
    public string GameKey { get; init; } = "";
}
