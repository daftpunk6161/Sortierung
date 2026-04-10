namespace Romulus.Contracts.Models;

/// <summary>
/// A single recognition signal with explicit evidence typing.
/// </summary>
public sealed record RecognitionSignal(
    string ConsoleKey,
    EvidenceTier Tier,
    MatchKind Kind,
    int Confidence,
    string Evidence);
