namespace Romulus.Contracts.Models;

/// <summary>
/// Comprehensive collection health report combining score, integrity, and coverage data.
/// </summary>
public sealed record CollectionHealthReport
{
    public required int HealthScore { get; init; }
    public required string Grade { get; init; }
    public required CollectionHealthBreakdown Breakdown { get; init; }
    public required CollectionHealthIntegrity Integrity { get; init; }
    public required DateTime GeneratedUtc { get; init; }
    public string? ConsoleFilter { get; init; }
}

/// <summary>
/// Score breakdown showing contribution of each factor to the overall health score.
/// </summary>
public sealed record CollectionHealthBreakdown
{
    public required int TotalFiles { get; init; }
    public required int Games { get; init; }
    public required int Duplicates { get; init; }
    public required int Junk { get; init; }
    public required int DatVerified { get; init; }
    public required int Errors { get; init; }
    public required double DuplicatePercent { get; init; }
    public required double JunkPercent { get; init; }
    public required double VerifiedPercent { get; init; }
}

/// <summary>
/// Integrity status from last baseline check.
/// </summary>
public sealed record CollectionHealthIntegrity
{
    public required bool HasBaseline { get; init; }
    public required int IntactCount { get; init; }
    public required int ChangedCount { get; init; }
    public required int MissingCount { get; init; }
    public required bool BitRotRisk { get; init; }
    public DateTime? LastCheckedUtc { get; init; }
}
