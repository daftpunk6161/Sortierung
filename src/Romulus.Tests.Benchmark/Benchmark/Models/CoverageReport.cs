namespace Romulus.Tests.Benchmark.Models;

/// <summary>
/// Coverage validation result from CoverageValidator.
/// </summary>
public sealed record CoverageReport
{
    public required int TotalEntries { get; init; }
    public required int SystemsCovered { get; init; }
    public required int FallklassenCovered { get; init; }
    public required Dictionary<string, int> ByPlatformFamily { get; init; }
    public required Dictionary<string, int> ByFallklasse { get; init; }
    public required Dictionary<string, int> BySpecialArea { get; init; }
    public required Dictionary<string, int> ByDifficulty { get; init; }
    public required Dictionary<string, int> BySet { get; init; }
    public required Dictionary<string, int> SystemEntryCounts { get; init; }
    public required List<GateResult> GateResults { get; init; }
    public required bool OverallPass { get; init; }
}

public sealed record GateResult
{
    public required string GateName { get; init; }
    public required int Actual { get; init; }
    public required int Target { get; init; }
    public required int HardFail { get; init; }
    public required GateStatus Status { get; init; }
}

public enum GateStatus
{
    Pass,
    Warning,
    Fail
}
