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
    public int VersionScore { get; init; }
    public int HeaderScore { get; init; }
    public int CompletenessScore { get; init; }
    public long SizeTieBreakScore { get; init; }
    public long SizeBytes { get; init; }
    public string Extension { get; init; } = "";
    public string Type { get; init; } = "";
    public bool DatMatch { get; init; }
    public string Category { get; init; } = "GAME";
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

/// <summary>
/// File category classification result.
/// </summary>
public enum FileCategory
{
    Game,
    Bios,
    Junk
}
