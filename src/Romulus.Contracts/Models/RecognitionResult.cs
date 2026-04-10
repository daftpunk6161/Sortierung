namespace Romulus.Contracts.Models;

/// <summary>
/// Unified recognition result for DAT-first and fallback detection paths.
/// </summary>
public sealed record RecognitionResult
{
    public string ConsoleKey { get; init; } = "UNKNOWN";
    public EvidenceTier Tier { get; init; } = EvidenceTier.Tier4_Unknown;
    public MatchKind PrimaryMatchKind { get; init; } = MatchKind.None;
    public DecisionClass Decision { get; init; } = DecisionClass.Unknown;
    public int Confidence { get; init; }
    public PlatformFamily Family { get; init; } = PlatformFamily.Unknown;
    public bool DatVerified { get; init; }
    public string? DatGameName { get; init; }
    public IReadOnlyList<RecognitionSignal> Signals { get; init; } = Array.Empty<RecognitionSignal>();
    public string Reasoning { get; init; } = string.Empty;
    public bool HasConflict { get; init; }
    public ConflictType ConflictType { get; init; } = ConflictType.None;
}
