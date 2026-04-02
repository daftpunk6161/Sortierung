namespace RomCleanup.Contracts.Models;

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
        ".zip", ".7z", ".chd", ".iso", ".bin", ".cue", ".gdi", ".ccd",
        ".rvz", ".gcz", ".wbfs", ".ecm", ".nsp", ".xci", ".nes", ".snes",
        ".sfc", ".smc", ".gb", ".gbc", ".gba", ".nds", ".3ds",
        ".n64", ".z64", ".v64", ".md", ".gen", ".sms", ".gg",
        ".pce", ".ngp", ".ws", ".rom", ".pbp", ".pkg"
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
    public string HashType { get; init; } = "SHA1";
    public string? ConvertFormat { get; init; }
    public bool ConvertOnly { get; init; }
    public bool ApproveReviews { get; init; }
    public bool ApproveConversionReview { get; init; }
    public string? TrashRoot { get; init; }
    public string? AuditPath { get; init; }
    public string? ReportPath { get; init; }
    public string ConflictPolicy { get; init; } = "Rename";
    public HashSet<string> DiscBasedConsoles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
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
    /// Structured phase timing metrics.
    /// </summary>
    public PhaseMetricsResult? PhaseMetrics { get; init; }
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
