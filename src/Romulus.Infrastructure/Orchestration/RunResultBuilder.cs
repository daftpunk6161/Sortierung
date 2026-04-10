using Romulus.Contracts.Models;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Sorting;

namespace Romulus.Infrastructure.Orchestration;

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
    public int ConvertBlockedCount { get; set; }
    public int ConvertReviewCount { get; set; }
    public int ConvertLossyWarningCount { get; set; }
    public int ConvertVerifyPassedCount { get; set; }
    public int ConvertVerifyFailedCount { get; set; }
    public long ConvertSavedBytes { get; set; }
    public ConversionReport? ConversionReport { get; set; }
    public DatAuditResult? DatAuditResult { get; set; }
    public int DatHaveCount { get; set; }
    public int DatHaveWrongNameCount { get; set; }
    public int DatMissCount { get; set; }
    public int DatUnknownCount { get; set; }
    public int DatAmbiguousCount { get; set; }
    public int DatRenameProposedCount { get; set; }
    public int DatRenameExecutedCount { get; set; }
    public int DatRenameSkippedCount { get; set; }
    public int DatRenameFailedCount { get; set; }
    public IReadOnlyList<PathMutation> DatRenamePathMutations { get; set; } = Array.Empty<PathMutation>();
    public long DurationMs { get; set; }
    public string? ReportPath { get; set; }
    public IReadOnlyList<RomCandidate> AllCandidates { get; set; } = Array.Empty<RomCandidate>();
    public IReadOnlyList<DedupeGroup> DedupeGroups { get; set; } = Array.Empty<DedupeGroup>();
    public PhaseMetricsResult? PhaseMetrics { get; set; }

    public RunResult Build()
    {
        var runResult = new RunResult
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
        ConvertBlockedCount = ConvertBlockedCount,
        ConvertReviewCount = ConvertReviewCount,
        ConvertLossyWarningCount = ConvertLossyWarningCount,
        ConvertVerifyPassedCount = ConvertVerifyPassedCount,
        ConvertVerifyFailedCount = ConvertVerifyFailedCount,
        ConvertSavedBytes = ConvertSavedBytes,
        ConversionReport = ConversionReport,
        DatAuditResult = DatAuditResult,
        DatHaveCount = DatHaveCount,
        DatHaveWrongNameCount = DatHaveWrongNameCount,
        DatMissCount = DatMissCount,
        DatUnknownCount = DatUnknownCount,
        DatAmbiguousCount = DatAmbiguousCount,
        DatRenameProposedCount = DatRenameProposedCount,
        DatRenameExecutedCount = DatRenameExecutedCount,
        DatRenameSkippedCount = DatRenameSkippedCount,
        DatRenameFailedCount = DatRenameFailedCount,
        DatRenamePathMutations = DatRenamePathMutations,
        DurationMs = DurationMs,
        ReportPath = ReportPath,
        AllCandidates = AllCandidates,
        DedupeGroups = DedupeGroups,
        PhaseMetrics = PhaseMetrics
        };

        // Defensive invariant check keeps report/output channels aligned.
        var validationErrors = RunResultValidator.Validate(runResult);
        if (validationErrors.Count > 0)
            throw new InvalidOperationException($"RunResult invariant violation: {string.Join(" | ", validationErrors)}");

        return runResult;
    }
}
