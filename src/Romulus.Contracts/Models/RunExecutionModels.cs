namespace Romulus.Contracts.Models;

/// <summary>
/// Shared run options for CLI, API, and WPF orchestrator execution.
/// </summary>
public sealed class RunOptions
{
    /// <summary>
    /// Default file extensions for ROM scanning. Shared across CLI, API, and WPF entry points.
    /// </summary>
    public static readonly string[] DefaultExtensions =
    {
        // Archives
        ".zip", ".7z", ".rar",
        // Disc images
        ".chd", ".iso", ".bin", ".cue", ".gdi", ".ccd", ".img", ".cso", ".ecm",
        ".gcz", ".rvz", ".wbfs", ".pbp",
        // Nintendo
        ".nes", ".fds", ".sfc", ".smc", ".gb", ".gbc", ".gba", ".vb",
        ".n64", ".z64", ".v64", ".ndd", ".nds", ".3ds", ".cia", ".dsi",
        ".nsp", ".xci", ".wux", ".rpx", ".wad", ".bs",
        // Sega
        ".md", ".gen", ".sms", ".gg", ".sg", ".sc", ".32x", ".sgx",
        // NEC
        ".pce", ".pcfx",
        // SNK
        ".ngp",
        // Bandai
        ".ws", ".wsc",
        // Atari
        ".a26", ".a52", ".a78", ".lnx", ".j64", ".st", ".stx", ".atr", ".xex", ".xfd",
        // Other handhelds / retro
        ".col", ".int", ".o2", ".vec", ".min", ".tgc",
        ".tzx", ".adf", ".d64", ".t64", ".mx1", ".mx2",
        // Misc
        ".rom", ".pkg", ".vpk", ".app", ".gpe", ".st2",
        ".p00", ".prc", ".pdb", ".dmg", ".gxb", ".cdi"
    };

    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();
    public string Mode { get; init; } = RunConstants.ModeDryRun;
    public string[] PreferRegions { get; init; } = RunConstants.DefaultPreferRegions;
    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();
    public bool RemoveJunk { get; init; } = true;
    public bool OnlyGames { get; init; }
    public bool KeepUnknownWhenOnlyGames { get; init; } = true;
    public bool AggressiveJunk { get; init; }
    public bool SortConsole { get; init; }
    public bool EnableDat { get; init; }
    public bool EnableDatAudit { get; init; }
    public bool EnableDatRename { get; init; }
    public string? DatRoot { get; init; }
    public IReadOnlyList<string> PreferredDatSources { get; init; } = Array.Empty<string>();
    public string HashType { get; init; } = "SHA1";
    public string? ConvertFormat { get; init; }
    public bool ConvertOnly { get; init; }
    public bool ApproveReviews { get; init; }
    public bool ApproveConversionReview { get; init; }
    public string? TrashRoot { get; init; }
    public string? AuditPath { get; init; }
    public string? ReportPath { get; init; }
    public string ConflictPolicy { get; init; } = "Rename";

    /// <summary>
    /// DAT-first policy gate (ADR-0023). When <c>false</c> (default), only detection
    /// results with hard evidence (Tier 0/1: DatHash, DiscHeader, CartridgeHeader,
    /// SerialNumber) are accepted for Sort/Move/Convert/Rename. Heuristic-only
    /// results (folder/extension/keyword) are routed to the Review lane.
    /// When <c>true</c>, soft-evidence detections may also drive sorting decisions
    /// (opt-in "Best Effort" mode). Consumers must surface <see
    /// cref="Romulus.Core.Classification.ConsoleDetectionResult.IsBestEffort"/>
    /// in GUI/CLI/API/Reports — never recompute the flag locally.
    /// </summary>
    public bool AllowHeuristicFallback { get; init; }

    /// <summary>
    /// Wave 2 — T-W2-CONVERSION-SAFETY-CONTRACT. Token that the caller must
    /// forward from the plan phase (see <c>RunResult.PendingLossyToken</c>)
    /// to authorise lossy conversion paths in the execute phase. <c>null</c>
    /// or empty means "no lossy authorisation given"; the orchestrator
    /// rejects any run that contains lossy plan items without a matching
    /// token via <c>ConversionLossyTokenPolicy.ValidateAcceptDataLossToken</c>.
    /// Token is computed deterministically from the lossy plan; never
    /// hardcoded, never invented client-side.
    /// </summary>
    public string? AcceptDataLossToken { get; init; }
}

/// <summary>
/// A persisted path mutation from one concrete artifact location to another.
/// Used to project the executed filesystem truth across channels.
/// </summary>
public sealed record PathMutation(
    string SourcePath,
    string TargetPath);

/// <summary>
/// Result of the move phase.
/// </summary>
public sealed record MovePhaseResult(
    int MoveCount,
    int FailCount,
    long SavedBytes,
    int SkipCount = 0,
    IReadOnlySet<string>? MovedSourcePaths = null);

/// <summary>
/// Full result of a pipeline execution.
/// </summary>
public sealed class RunResult
{
    public string Status { get; init; } = RunConstants.StatusOk;
    public int ExitCode { get; init; }
    public OperationResult? Preflight { get; init; }
    public int TotalFilesScanned { get; init; }
    public int GroupCount { get; init; }
    public int WinnerCount { get; init; }
    public int LoserCount { get; init; }
    public int SkippedEmptyGameKeyCount { get; init; }
    public MovePhaseResult? MoveResult { get; init; }
    public MovePhaseResult? JunkMoveResult { get; init; }
    public ConsoleSortResult? ConsoleSortResult { get; init; }
    public int JunkRemovedCount { get; init; }
    public int FilteredNonGameCount { get; init; }
    public int UnknownCount { get; init; }
    public IReadOnlyDictionary<string, int> UnknownReasonCounts { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public int ConvertedCount { get; init; }
    public int ConvertErrorCount { get; init; }
    public int ConvertSkippedCount { get; init; }
    public int ConvertBlockedCount { get; init; }
    public int ConvertReviewCount { get; init; }
    public int ConvertLossyWarningCount { get; init; }
    public int ConvertVerifyPassedCount { get; init; }
    public int ConvertVerifyFailedCount { get; init; }
    public long ConvertSavedBytes { get; init; }
    public ConversionReport? ConversionReport { get; init; }
    public DatAuditResult? DatAuditResult { get; init; }
    public int DatHaveCount { get; init; }
    public int DatHaveWrongNameCount { get; init; }
    public int DatMissCount { get; init; }
    public int DatUnknownCount { get; init; }
    public int DatAmbiguousCount { get; init; }
    public int DatRenameProposedCount { get; init; }
    public int DatRenameExecutedCount { get; init; }
    public int DatRenameSkippedCount { get; init; }
    public int DatRenameFailedCount { get; init; }
    public IReadOnlyList<PathMutation> DatRenamePathMutations { get; init; } = Array.Empty<PathMutation>();
    public DateTime? StartedUtc { get; init; }
    public DateTime? CompletedUtc { get; init; }
    public long DurationMs { get; init; }
    public string? ReportPath { get; init; }

    /// <summary>
    /// All scanned candidates (for report generation).
    /// </summary>
    public IReadOnlyList<RomCandidate> AllCandidates { get; init; } = Array.Empty<RomCandidate>();

    /// <summary>
    /// Dedupe group results (for DryRun JSON output and reports).
    /// </summary>
    public IReadOnlyList<DedupeGroup> DedupeGroups { get; init; } = Array.Empty<DedupeGroup>();

    /// <summary>
    /// T-W2-SCORING-REASON-TRACE: One <see cref="WinnerReasonTrace"/> per
    /// winner-selection decision. Pure projection of <see cref="DedupeGroups"/>;
    /// used by the upcoming GUI/CLI/API Decision Explainer
    /// (T-W4-DECISION-EXPLAINER) to explain WHY a candidate won. Deterministic
    /// — same inputs produce the same trace order.
    /// </summary>
    public IReadOnlyList<WinnerReasonTrace> WinnerReasons { get; init; } = Array.Empty<WinnerReasonTrace>();

    /// <summary>
    /// Wave 2 — T-W2-CONVERSION-SAFETY-CONTRACT. Deterministic token the
    /// caller must echo back via <c>RunOptions.AcceptDataLossToken</c> to
    /// authorise the lossy-conversion subset of the next execute phase.
    /// <c>null</c> when the plan contains no lossy items. Computed via
    /// <c>ConversionLossyTokenPolicy.ComputeAcceptDataLossToken</c>; never
    /// emitted to logs.
    /// </summary>
    public string? PendingLossyToken { get; init; }

    /// <summary>
    /// Structured phase timing metrics.
    /// </summary>
    public PhaseMetricsResult? PhaseMetrics { get; init; }

    /// <summary>
    /// Non-fatal warnings collected during pipeline execution (e.g. skipped deferred analysis).
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>R4-023: Indicates partial completion (cancel/error mid-pipeline).</summary>
    public bool IsPartial { get; init; }

    /// <summary>
    /// Deep-dive audit (Orchestration) F5: phase that caused the run to fail
    /// or be aborted (Scan, Deduplicate, Move, ...). Null on success / clean
    /// cancel paths where no specific phase reported failure. Mirrors
    /// <see cref="PipelineState.FailedPhaseName"/> so GUI / CLI / API / Reports
    /// share one fachliche Wahrheit instead of recomputing locally.
    /// </summary>
    public string? FailedPhaseName { get; init; }

    /// <summary>
    /// Deep-dive audit (Orchestration) F5: status string of the failed phase
    /// (typically <see cref="RunConstants.StatusFailed"/>). Null when no phase
    /// reported failure.
    /// </summary>
    public string? FailedPhaseStatus { get; init; }
}

public static class RunResultValidator
{
    public static IReadOnlyList<string> Validate(RunResult result)
    {
        var errors = new List<string>();

        if (result.MoveResult is { } move)
        {
            if (move.MoveCount < 0 || move.FailCount < 0 || move.SkipCount < 0)
                errors.Add("MoveResult counts must not be negative.");

            if (move.MovedSourcePaths is not null && move.MovedSourcePaths.Count != move.MoveCount)
                errors.Add("MoveResult.MovedSourcePaths count must equal MoveCount.");
        }

        if (result.DatAuditResult is { } dat)
        {
            var total = dat.HaveCount + dat.HaveWrongNameCount + dat.MissCount + dat.UnknownCount + dat.AmbiguousCount;
            if (total != dat.Entries.Count)
                errors.Add("DatAuditResult summary counts must equal Entries.Count.");
        }

        return errors;
    }
}
