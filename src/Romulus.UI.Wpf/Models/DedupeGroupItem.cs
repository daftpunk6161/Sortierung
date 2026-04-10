namespace Romulus.UI.Wpf.Models;

/// <summary>GUI-072: Dedup decision group for the decision browser TreeView.</summary>
public sealed class DedupeGroupItem
{
    public required string GameKey { get; init; }
    public required DedupeEntryItem Winner { get; init; }
    public required IReadOnlyList<DedupeEntryItem> Losers { get; init; }
}

/// <summary>One ROM entry within a dedup group (winner or loser).</summary>
public sealed class DedupeEntryItem
{
    public required string FileName { get; init; }
    public required string Region { get; init; }
    public required int RegionScore { get; init; }
    public required int FormatScore { get; init; }
    public required long VersionScore { get; init; }
    public required string DecisionClass { get; init; }
    public required string EvidenceTier { get; init; }
    public required string PrimaryMatchKind { get; init; }
    public required string PlatformFamily { get; init; }
    public required bool IsWinner { get; init; }
}
