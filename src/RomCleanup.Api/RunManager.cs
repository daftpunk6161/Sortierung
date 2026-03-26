using System.Text.Json.Serialization;
using RomCleanup.Contracts.Errors;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Audit;

namespace RomCleanup.Api;

/// <summary>
/// Manages run lifecycle: creation (singleton), execution, cancellation, results.
/// Now delegates to RunOrchestrator for the actual pipeline.
/// </summary>
public sealed class RunManager
{
    private readonly RunLifecycleManager _lifecycle;
    private readonly IRunOptionsFactory _runOptionsFactory;
    private readonly IRunEnvironmentFactory _runEnvironmentFactory;

    public RunManager(
        IFileSystem fs,
        IAuditStore audit,
        Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, RunExecutionOutcome>? executor = null,
        IRunOptionsFactory? runOptionsFactory = null,
        IRunEnvironmentFactory? runEnvironmentFactory = null)
    {
        _runOptionsFactory = runOptionsFactory ?? new RunOptionsFactory();
        _runEnvironmentFactory = runEnvironmentFactory ?? new RunEnvironmentFactory();
        _lifecycle = new RunLifecycleManager(fs, audit, executor ?? ExecuteWithOrchestrator);
    }

    internal RunLifecycleManager Lifecycle => _lifecycle;

    public RunRecord? TryCreate(RunRequest request, string mode, string? ownerClientId = null)
        => _lifecycle.TryCreate(request, mode, ownerClientId);

    public RunCreateResult TryCreateOrReuse(
        RunRequest request,
        string mode,
        string? idempotencyKey = null,
        string? ownerClientId = null)
        => _lifecycle.TryCreateOrReuse(request, mode, idempotencyKey, ownerClientId);

    public RunRecord? Get(string runId) =>
        _lifecycle.Get(runId);

    public RunRecord? GetActive() => _lifecycle.GetActive();

    public RunCancelResult Cancel(string runId) => _lifecycle.Cancel(runId);

    public Task<RunWaitResult> WaitForCompletion(
        string runId,
        int pollMs = 250,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => _lifecycle.WaitForCompletion(runId, pollMs, timeout, cancellationToken);

    /// <summary>
    /// Cancel any active run and wait for completion. Called during host shutdown.
    /// </summary>
    public Task ShutdownAsync() => _lifecycle.ShutdownAsync();

    private RunExecutionOutcome ExecuteWithOrchestrator(
        RunRecord run,
        IFileSystem fs,
        IAuditStore audit,
        CancellationToken ct)
    {
        var (auditPath, reportPath) = RunLifecycleManager.GetArtifactPaths(run.RunId);

        var options = _runOptionsFactory.Create(new RunRecordOptionsSource(run), auditPath, reportPath);
        var env = _runEnvironmentFactory.Create(options,
            onWarning: msg =>
            {
                run.ProgressMessage = msg;
                run.ProgressPercent = ProgressEstimator.EstimateFromMessage(msg);
            });

        var orchestrator = new RunOrchestrator(fs, audit,
            env.ConsoleDetector, env.HashService, env.Converter, env.DatIndex,
            onProgress: msg =>
            {
                run.ProgressMessage = msg;
                run.ProgressPercent = ProgressEstimator.EstimateFromMessage(msg);
            },
            archiveHashService: env.ArchiveHashService);

        var result = orchestrator.Execute(options, ct);
        var projection = RunProjectionFactory.Create(result);
        var status = RunOutcomeExtensions.ParseRunOutcome(result.Status) switch
        {
            RunOutcome.Ok => "completed",
            RunOutcome.CompletedWithErrors => "completed_with_errors",
            RunOutcome.Cancelled => "cancelled",
            RunOutcome.Blocked => "failed",
            RunOutcome.Failed => "failed",
            _ => result.ExitCode == 0 ? "completed" : "failed"
        };

        return new RunExecutionOutcome(
            status,
            ApiRunResultMapper.Map(result, projection));
    }
}

internal sealed class RunRecordOptionsSource : IRunOptionsSource
{
    public RunRecordOptionsSource(RunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);

        Roots = run.Roots;
        Mode = run.Mode;
        PreferRegions = run.PreferRegions;
        Extensions = run.Extensions;
        RemoveJunk = run.RemoveJunk;
        OnlyGames = run.OnlyGames;
        KeepUnknownWhenOnlyGames = run.KeepUnknownWhenOnlyGames;
        AggressiveJunk = run.AggressiveJunk;
        SortConsole = run.SortConsole;
        EnableDat = run.EnableDat;
        EnableDatAudit = run.EnableDatAudit;
        EnableDatRename = run.EnableDatRename;
        DatRoot = run.DatRoot;
        HashType = run.HashType;
        ConvertFormat = run.ConvertFormat;
        ConvertOnly = run.ConvertOnly;
        TrashRoot = run.TrashRoot;
        ConflictPolicy = run.ConflictPolicy;
    }

    public IReadOnlyList<string> Roots { get; }
    public string Mode { get; }
    public string[] PreferRegions { get; }
    public IReadOnlyList<string> Extensions { get; }
    public bool RemoveJunk { get; }
    public bool OnlyGames { get; }
    public bool KeepUnknownWhenOnlyGames { get; }
    public bool AggressiveJunk { get; }
    public bool SortConsole { get; }
    public bool EnableDat { get; }
    public bool EnableDatAudit { get; }
    public bool EnableDatRename { get; }
    public string? DatRoot { get; }
    public string HashType { get; }
    public string? ConvertFormat { get; }
    public bool ConvertOnly { get; }
    public string? TrashRoot { get; }
    public string ConflictPolicy { get; }
}

public enum RunCreateDisposition
{
    Created,
    Reused,
    ActiveConflict,
    IdempotencyConflict
}

public sealed record RunCreateResult(RunCreateDisposition Disposition, RunRecord? Run, string? Error = null);

public enum RunWaitDisposition
{
    Completed,
    TimedOut,
    ClientDisconnected,
    NotFound
}

public sealed record RunWaitResult(RunWaitDisposition Disposition, RunRecord? Run);

public enum RunCancelDisposition
{
    Accepted,
    NoOp,
    NotFound
}

public sealed record RunCancelResult(RunCancelDisposition Disposition, RunRecord? Run);

public sealed record RunExecutionOutcome(string Status, ApiRunResult Result);

public sealed class RunRequest
{
    public string[]? Roots { get; set; }
    public string? Mode { get; set; }
    public string[]? PreferRegions { get; set; }

    // ADR-0007 §3.1: Additional options for API parity with CLI/WPF
    public bool RemoveJunk { get; set; } = true;
    public bool AggressiveJunk { get; set; }
    public bool SortConsole { get; set; }
    public bool EnableDat { get; set; }
    public bool EnableDatAudit { get; set; }
    public bool EnableDatRename { get; set; }
    public string? DatRoot { get; set; }
    public bool OnlyGames { get; set; }
    public bool KeepUnknownWhenOnlyGames { get; set; } = true;
    public string? HashType { get; set; }
    public string? ConvertFormat { get; set; }
    public bool ConvertOnly { get; set; }
    public string? ConflictPolicy { get; set; }
    public string? TrashRoot { get; set; }
    public string[]? Extensions { get; set; }
}

public sealed class RunRecord
{
    private readonly object _lock = new();
    private string _status = "running";
    private DateTime? _completedUtc;
    private ApiRunResult? _result;
    private string? _progressMessage;
    private string _recoveryState = "in-progress";
    private bool _cancellationRequested;

    public string RunId { get; init; } = "";
    [JsonIgnore]
    public string? IdempotencyKey { get; init; }
    [JsonIgnore]
    public string RequestFingerprint { get; init; } = "";
    [JsonIgnore]
    public string OwnerClientId { get; set; } = "";
    public string Status
    {
        get { lock (_lock) return _status; }
        set { lock (_lock) _status = value; }
    }
    public string Mode { get; init; } = "DryRun";
    [JsonIgnore]
    public string[] Roots { get; init; } = Array.Empty<string>();
    public string[] PreferRegions { get; init; } = Array.Empty<string>();
    public bool RemoveJunk { get; init; } = true;
    public bool AggressiveJunk { get; init; }
    public bool SortConsole { get; init; }
    public bool EnableDat { get; init; }
    public bool EnableDatAudit { get; init; }
    public bool EnableDatRename { get; init; }
    [JsonIgnore]
    public string? DatRoot { get; init; }
    public bool OnlyGames { get; init; }
    public bool KeepUnknownWhenOnlyGames { get; init; } = true;
    public string HashType { get; init; } = "SHA1";
    public string? ConvertFormat { get; init; }
    public bool ConvertOnly { get; init; }
    public string ConflictPolicy { get; init; } = "Rename";
    [JsonIgnore]
    public string? TrashRoot { get; init; }
    public string[] Extensions { get; init; } = RunOptions.DefaultExtensions;
    public DateTime StartedUtc { get; init; }
    public DateTime? CompletedUtc
    {
        get { lock (_lock) return _completedUtc; }
        set { lock (_lock) _completedUtc = value; }
    }
    public ApiRunResult? Result
    {
        get { lock (_lock) return _result; }
        set { lock (_lock) _result = value; }
    }
    public string? ProgressMessage
    {
        get { lock (_lock) return _progressMessage; }
        set { lock (_lock) _progressMessage = value; }
    }
    public long ElapsedMs
    {
        get
        {
            var started = StartedUtc;
            var completed = CompletedUtc;
            if (completed.HasValue)
                return (long)Math.Max(0, (completed.Value - started).TotalMilliseconds);

            return (long)Math.Max(0, (DateTime.UtcNow - started).TotalMilliseconds);
        }
    }
    public int ProgressPercent { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    [JsonIgnore]
    public string? AuditPath { get; set; }
    [JsonIgnore]
    public string? ReportPath { get; set; }
    public string RecoveryModel { get; init; } = "audit-rollback-only";
    public string RestartRecovery { get; init; } = "not-persisted";
    public bool ResumeSupported => false;
    public bool CanRetry => Status != "running";
    public bool CanRollback => !string.IsNullOrWhiteSpace(AuditPath);
    public string RecoveryState
    {
        get { lock (_lock) return _recoveryState; }
        set { lock (_lock) _recoveryState = value; }
    }
    public bool CancellationRequested
    {
        get { lock (_lock) return _cancellationRequested; }
        set { lock (_lock) _cancellationRequested = value; }
    }

    internal CancellationTokenSource CancellationSource { get; } = new();
}

public sealed class ApiRunResult
{
    /// <summary>Orchestrator-level status (ok, completed_with_errors, blocked, cancelled).
    /// Distinct from RunRecord.Status which tracks lifecycle (pending, running, completed, failed).</summary>
    public string OrchestratorStatus { get; init; } = "";
    public int ExitCode { get; init; }
    public int TotalFiles { get; init; }
    public int Candidates { get; init; }
    public int Groups { get; init; }
    public int Winners { get; init; }
    /// <summary>Number of duplicate ROMs identified (losers in deduplication).
    /// In DryRun mode this is the count of files that *would* be moved, not actually moved files.</summary>
    public int Losers { get; init; }
    public int Games { get; init; }
    public int Unknown { get; init; }
    public int Junk { get; init; }
    public int Bios { get; init; }
    public int DatMatches { get; init; }
    public int HealthScore { get; init; }
    public int ConvertedCount { get; init; }
    public int ConvertErrorCount { get; init; }
    public int ConvertSkippedCount { get; init; }
    public int ConvertBlockedCount { get; init; }
    public int ConvertReviewCount { get; init; }
    public int ConvertLossyWarningCount { get; init; }
    public int ConvertVerifyPassedCount { get; init; }
    public int ConvertVerifyFailedCount { get; init; }
    public long ConvertSavedBytes { get; init; }
    public int DatHaveCount { get; init; }
    public int DatHaveWrongNameCount { get; init; }
    public int DatMissCount { get; init; }
    public int DatUnknownCount { get; init; }
    public int DatAmbiguousCount { get; init; }
    public int DatRenameProposedCount { get; init; }
    public int DatRenameExecutedCount { get; init; }
    public int DatRenameSkippedCount { get; init; }
    public int DatRenameFailedCount { get; init; }
    public int JunkRemovedCount { get; init; }
    public int FilteredNonGameCount { get; init; }
    public int JunkFailCount { get; init; }
    public int MoveCount { get; init; }
    public int SkipCount { get; init; }
    public int ConsoleSortMoved { get; init; }
    public int ConsoleSortFailed { get; init; }
    public int ConsoleSortReviewed { get; init; }
    public int ConsoleSortBlocked { get; init; }
    public int FailCount { get; init; }
    public long SavedBytes { get; init; }
    public long DurationMs { get; init; }
    public string[] PreflightWarnings { get; init; } = Array.Empty<string>();
    public ApiPhaseMetrics PhaseMetrics { get; init; } = new() { Phases = Array.Empty<ApiPhaseMetric>() };
    public ApiDedupeGroup[] DedupeGroups { get; init; } = Array.Empty<ApiDedupeGroup>();
    public ApiConversionPlan[] ConversionPlans { get; init; } = Array.Empty<ApiConversionPlan>();
    public ApiConversionBlocked[] ConversionBlocked { get; init; } = Array.Empty<ApiConversionBlocked>();
    public OperationError? Error { get; init; }
}

public sealed class ApiPhaseMetrics
{
    public string? RunId { get; init; }
    public DateTime? StartedAt { get; init; }
    public long TotalDurationMs { get; init; }
    public ApiPhaseMetric[] Phases { get; init; } = Array.Empty<ApiPhaseMetric>();
}

public sealed class ApiPhaseMetric
{
    public string Phase { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; }
    public long DurationMs { get; init; }
    public int ItemCount { get; init; }
    public double ItemsPerSec { get; init; }
    public double PercentOfTotal { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class ApiDedupeGroup
{
    public string GameKey { get; init; } = string.Empty;
    public RomCandidate Winner { get; init; } = new();
    public RomCandidate[] Losers { get; init; } = Array.Empty<RomCandidate>();
}

public sealed class ApiConversionPlan
{
    public string SourcePath { get; init; } = string.Empty;
    public string? TargetExtension { get; init; }
    public string Safety { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string Verification { get; init; } = string.Empty;
}

public sealed class ApiConversionBlocked
{
    public string SourcePath { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Safety { get; init; } = string.Empty;
}

public sealed class RunStatusDto
{
    public string RunId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Mode { get; init; } = "DryRun";
    public string[] PreferRegions { get; init; } = Array.Empty<string>();
    public bool RemoveJunk { get; init; }
    public bool AggressiveJunk { get; init; }
    public bool SortConsole { get; init; }
    public bool EnableDat { get; init; }
    public bool OnlyGames { get; init; }
    public bool KeepUnknownWhenOnlyGames { get; init; }
    public string HashType { get; init; } = "SHA1";
    public string? ConvertFormat { get; init; }
    public bool ConvertOnly { get; init; }
    public string ConflictPolicy { get; init; } = "Rename";
    public string[] Extensions { get; init; } = Array.Empty<string>();
    public DateTime StartedUtc { get; init; }
    public DateTime? CompletedUtc { get; init; }
    public long ElapsedMs { get; init; }
    public int ProgressPercent { get; init; }
    public string? ProgressMessage { get; init; }
    public DateTime? CancelledAtUtc { get; init; }
    public string RecoveryModel { get; init; } = "audit-rollback-only";
    public string RestartRecovery { get; init; } = "not-persisted";
    public bool ResumeSupported { get; init; }
    public bool CanRetry { get; init; }
    public bool CanRollback { get; init; }
    public string RecoveryState { get; init; } = "in-progress";
    public bool CancellationRequested { get; init; }
}

public static class RunStatusDtoMapper
{
    public static RunStatusDto ToDto(this RunRecord run)
    {
        return new RunStatusDto
        {
            RunId = run.RunId,
            Status = run.Status,
            Mode = run.Mode,
            PreferRegions = run.PreferRegions,
            RemoveJunk = run.RemoveJunk,
            AggressiveJunk = run.AggressiveJunk,
            SortConsole = run.SortConsole,
            EnableDat = run.EnableDat,
            OnlyGames = run.OnlyGames,
            KeepUnknownWhenOnlyGames = run.KeepUnknownWhenOnlyGames,
            HashType = run.HashType,
            ConvertFormat = run.ConvertFormat,
            ConvertOnly = run.ConvertOnly,
            ConflictPolicy = run.ConflictPolicy,
            Extensions = run.Extensions,
            StartedUtc = run.StartedUtc,
            CompletedUtc = run.CompletedUtc,
            ElapsedMs = run.ElapsedMs,
            ProgressPercent = run.ProgressPercent,
            ProgressMessage = run.ProgressMessage,
            CancelledAtUtc = run.CancelledAtUtc,
            RecoveryModel = run.RecoveryModel,
            RestartRecovery = run.RestartRecovery,
            ResumeSupported = run.ResumeSupported,
            CanRetry = run.CanRetry,
            CanRollback = run.CanRollback,
            RecoveryState = run.RecoveryState,
            CancellationRequested = run.CancellationRequested
        };
    }
}
