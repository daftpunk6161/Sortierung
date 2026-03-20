using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Metrics;
using RomCleanup.Infrastructure.Sorting;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Mutable builder for assembling a RunResult across orchestrator phases.
/// </summary>
public sealed class RunResultBuilder
{
    public string Status { get; set; } = "ok";
    public int ExitCode { get; set; }
    public OperationResult? Preflight { get; set; }
    public int TotalFilesScanned { get; set; }
    public int GroupCount { get; set; }
    public int WinnerCount { get; set; }
    public int LoserCount { get; set; }
    public MovePhaseResult? MoveResult { get; set; }
    public MovePhaseResult? JunkMoveResult { get; set; }
    public ConsoleSortResult? ConsoleSortResult { get; set; }
    public int JunkRemovedCount { get; set; }
    public int FilteredNonGameCount { get; set; }
    public int UnknownCount { get; set; }
    public IReadOnlyDictionary<string, int> UnknownReasonCounts { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public int ConvertedCount { get; set; }
    public int ConvertErrorCount { get; set; }
    public int ConvertSkippedCount { get; set; }
    public long DurationMs { get; set; }
    public string? ReportPath { get; set; }
    public IReadOnlyList<RomCandidate> AllCandidates { get; set; } = Array.Empty<RomCandidate>();
    public IReadOnlyList<DedupeResult> DedupeGroups { get; set; } = Array.Empty<DedupeResult>();
    public PhaseMetricsResult? PhaseMetrics { get; set; }

    public RunResult Build() => new()
    {
        Status = Status,
        ExitCode = ExitCode,
        Preflight = Preflight,
        TotalFilesScanned = TotalFilesScanned,
        GroupCount = GroupCount,
        WinnerCount = WinnerCount,
        LoserCount = LoserCount,
        MoveResult = MoveResult,
        JunkMoveResult = JunkMoveResult,
        ConsoleSortResult = ConsoleSortResult,
        JunkRemovedCount = JunkRemovedCount,
        FilteredNonGameCount = FilteredNonGameCount,
        UnknownCount = UnknownCount,
        UnknownReasonCounts = UnknownReasonCounts,
        ConvertedCount = ConvertedCount,
        ConvertErrorCount = ConvertErrorCount,
        ConvertSkippedCount = ConvertSkippedCount,
        DurationMs = DurationMs,
        ReportPath = ReportPath,
        AllCandidates = AllCandidates,
        DedupeGroups = DedupeGroups,
        PhaseMetrics = PhaseMetrics
    };
}