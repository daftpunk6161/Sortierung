using System.Text.Json.Serialization;
using Romulus.Contracts;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Review;
using Romulus.Infrastructure.Time;

namespace Romulus.Api;

/// <summary>
/// Manages run lifecycle: creation (singleton), execution, cancellation, results.
/// Now delegates to RunOrchestrator for the actual pipeline.
/// </summary>
public sealed class RunManager : IDisposable, IAsyncDisposable
{
    private readonly RunLifecycleManager _lifecycle;
    private readonly IRunOptionsFactory _runOptionsFactory;
    private readonly IRunEnvironmentFactory _runEnvironmentFactory;
    private readonly PersistedReviewDecisionService? _reviewDecisionService;
    private readonly string? _collectionDatabasePath;
    private readonly ITimeProvider _timeProvider;
    private bool _disposed;

    public RunManager(
        IFileSystem fs,
        IAuditStore audit,
        Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, RunExecutionOutcome>? executor = null,
        IRunOptionsFactory? runOptionsFactory = null,
        IRunEnvironmentFactory? runEnvironmentFactory = null,
        PersistedReviewDecisionService? reviewDecisionService = null,
        CollectionIndexPathOptions? collectionIndexPathOptions = null,
        ITimeProvider? timeProvider = null)
    {
        _runOptionsFactory = runOptionsFactory ?? new RunOptionsFactory();
        _runEnvironmentFactory = runEnvironmentFactory ?? new RunEnvironmentFactory();
        _reviewDecisionService = reviewDecisionService;
        _collectionDatabasePath = collectionIndexPathOptions?.DatabasePath;
        _timeProvider = timeProvider ?? new SystemTimeProvider();
        Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, Task<RunExecutionOutcome>> effectiveExecutor =
            executor is null
                ? ExecuteWithOrchestratorAsync
                : (run, fileSystem, auditStore, cancellationToken) =>
                    Task.FromResult(executor(run, fileSystem, auditStore, cancellationToken));

        _lifecycle = new RunLifecycleManager(fs, audit, effectiveExecutor, _timeProvider);
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

    public IReadOnlyList<RunRecord> List() => _lifecycle.List();

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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        try
        {
            await ShutdownAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // best-effort shutdown on host dispose
        }

        _disposed = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // Synchronous fallback: the host should prefer DisposeAsync.
        // Mark disposed without blocking on async shutdown.
        _disposed = true;
    }

    private async Task<RunExecutionOutcome> ExecuteWithOrchestratorAsync(
        RunRecord run,
        IFileSystem fs,
        IAuditStore audit,
        CancellationToken ct)
    {
        var (auditPath, reportPath) = RunLifecycleManager.GetArtifactPaths(run.RunId, run.Roots);

        var options = _runOptionsFactory.Create(new RunRecordOptionsSource(run), auditPath, reportPath);
        using var env = _runEnvironmentFactory.Create(options,
            onWarning: msg =>
            {
                run.ProgressMessage = msg;
                run.ProgressPercent = ProgressEstimator.EstimateFromMessage(msg);
            });

        using var orchestrator = new RunOrchestrator(fs, audit,
            env.ConsoleDetector, env.HashService, env.Converter, env.DatIndex,
            onProgress: msg =>
            {
                run.ProgressMessage = msg;
                run.ProgressPercent = ProgressEstimator.EstimateFromMessage(msg);
            },
            archiveHashService: env.ArchiveHashService,
            headerlessHasher: env.HeaderlessHasher,
            knownBiosHashes: env.KnownBiosHashes,
            collectionIndex: env.CollectionIndex,
            enrichmentFingerprint: env.EnrichmentFingerprint,
            reviewDecisionService: _reviewDecisionService,
            provenanceStore: env.ProvenanceStore);

        var runStartedUtc = _timeProvider.UtcNow.UtcDateTime;
        var result = orchestrator.Execute(options, ct);
        var runCompletedUtc = _timeProvider.UtcNow.UtcDateTime;

        try
        {
            using var collectionIndex = new LiteDbCollectionIndex(CollectionIndexPaths.ResolveDatabasePath(_collectionDatabasePath),
                msg =>
                {
                    run.ProgressMessage = msg;
                    run.ProgressPercent = ProgressEstimator.EstimateFromMessage(msg);
                });

            await CollectionRunSnapshotWriter.TryPersistAsync(
                collectionIndex,
                options,
                result,
                runStartedUtc,
                runCompletedUtc,
                msg =>
                {
                    run.ProgressMessage = msg;
                    run.ProgressPercent = ProgressEstimator.EstimateFromMessage(msg);
                },
                CancellationToken.None,
                ownerClientId: run.OwnerClientId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            run.ProgressMessage = $"[CollectionIndex] Run snapshot persist skipped: {ex.Message}";
        }

        run.CoreRunResult = result;
        var projection = RunProjectionFactory.Create(result);
        var status = RunConstants.ToLifecycleStatus(result.Status) switch
        {
            RunLifecycleStatus.Completed => RunConstants.StatusCompleted,
            RunLifecycleStatus.CompletedWithErrors => RunConstants.StatusCompletedWithErrors,
            RunLifecycleStatus.Cancelled => RunConstants.StatusCancelled,
            RunLifecycleStatus.Blocked => RunConstants.StatusBlocked,
            RunLifecycleStatus.Failed => RunConstants.StatusFailed,
            _ => result.ExitCode == 0 ? RunConstants.StatusCompleted : RunConstants.StatusFailed
        };

        return new RunExecutionOutcome(
            status,
            ApiRunResultMapper.Map(result, projection, run.Mode));
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
        PreferredDatSources = run.PreferredDatSources;
        HashType = run.HashType;
        ConvertFormat = run.ConvertFormat;
        ConvertOnly = run.ConvertOnly;
        ApproveReviews = run.ApproveReviews;
        ApproveConversionReview = run.ApproveConversionReview;
        AcceptDataLossToken = run.AcceptDataLossToken;
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
    public IReadOnlyList<string> PreferredDatSources { get; }
    public string HashType { get; }
    public string? ConvertFormat { get; }
    public bool ConvertOnly { get; }
    public bool ApproveReviews { get; }
    public bool ApproveConversionReview { get; }
    public string? AcceptDataLossToken { get; }
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
    public string? WorkflowScenarioId { get; set; }
    public string? ProfileId { get; set; }
    public string[]? PreferRegions { get; set; }

    // ADR-0007 §3.1: Additional options for API parity with CLI/WPF
    public bool RemoveJunk { get; set; } = true;
    public bool AggressiveJunk { get; set; }
    public bool SortConsole { get; set; }
    public bool EnableDat { get; set; }
    public bool EnableDatAudit { get; set; }
    public bool EnableDatRename { get; set; }
    public string? DatRoot { get; set; }
    public string[]? PreferredDatSources { get; set; }
    public bool OnlyGames { get; set; }
    public bool KeepUnknownWhenOnlyGames { get; set; } = true;
    public string? HashType { get; set; }
    public string? ConvertFormat { get; set; }
    public bool ConvertOnly { get; set; }
    public bool ApproveReviews { get; set; }
    public bool ApproveConversionReview { get; set; }
    public string? AcceptDataLossToken { get; set; }
    public string? ConflictPolicy { get; set; }
    public string? TrashRoot { get; set; }
    public string[]? Extensions { get; set; }
}

public sealed class RunRecord
{
    private readonly object _lock = new();
    private string _status = RunConstants.StatusRunning;
    private DateTime? _completedUtc;
    private ApiRunResult? _result;
    private string? _progressMessage;
    private string _recoveryState = "in-progress";
    private bool _cancellationRequested;
    private bool _cancellationSourceDisposed;
    private bool _canRollback;
    private readonly HashSet<string> _approvedReviewPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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
    public string Mode { get; init; } = RunConstants.ModeDryRun;
    public string? WorkflowScenarioId { get; init; }
    public string? ProfileId { get; init; }
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
    public string[] PreferredDatSources { get; init; } = Array.Empty<string>();
    public bool OnlyGames { get; init; }
    public bool KeepUnknownWhenOnlyGames { get; init; } = true;
    public string HashType { get; init; } = RunConstants.DefaultHashType;
    public string? ConvertFormat { get; init; }
    public bool ConvertOnly { get; init; }
    public bool ApproveReviews { get; init; }
    public bool ApproveConversionReview { get; init; }
    [JsonIgnore]
    public string? AcceptDataLossToken { get; init; }
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
    [JsonIgnore]
    internal Func<DateTimeOffset> UtcNowProvider { get; init; } = static () => DateTimeOffset.UtcNow;
    public ApiRunResult? Result
    {
        get { lock (_lock) return _result; }
        set { lock (_lock) _result = value; }
    }
    [JsonIgnore]
    public RunResult? CoreRunResult { get; set; }
    public bool TryApproveReviewPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        lock (_lock)
            return _approvedReviewPaths.Add(path);
    }

    public int ApprovedReviewCount
    {
        get
        {
            lock (_lock)
                return _approvedReviewPaths.Count;
        }
    }

    public bool IsReviewPathApproved(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        lock (_lock)
            return _approvedReviewPaths.Contains(path);
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

            return (long)Math.Max(0, (UtcNowProvider().UtcDateTime - started).TotalMilliseconds);
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
    public bool CanRetry => Status != RunConstants.StatusRunning;
    public bool CanRollback
    {
        get { lock (_lock) return _canRollback; }
        set { lock (_lock) _canRollback = value; }
    }
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

    internal CancellationToken GetCancellationToken()
    {
        lock (_lock)
        {
            if (_cancellationSourceDisposed)
                return _cancellationRequested ? new CancellationToken(canceled: true) : CancellationToken.None;

            try
            {
                return CancellationSource.Token;
            }
            catch (ObjectDisposedException)
            {
                _cancellationSourceDisposed = true;
                return _cancellationRequested ? new CancellationToken(canceled: true) : CancellationToken.None;
            }
        }
    }

    internal bool TryCancelExecution()
    {
        lock (_lock)
        {
            if (_cancellationSourceDisposed)
                return false;

            try
            {
                CancellationSource.Cancel();
                return true;
            }
            catch (ObjectDisposedException)
            {
                _cancellationSourceDisposed = true;
                return false;
            }
        }
    }

    internal void DisposeCancellationSource()
    {
        lock (_lock)
        {
            if (_cancellationSourceDisposed)
                return;

            CancellationSource.Dispose();
            _cancellationSourceDisposed = true;
        }
    }

    internal Task CompletionTask => _completion.Task;

    internal void SignalCompletion() => _completion.TrySetResult(true);
}

public sealed class ApiRunResult
{
    /// <summary>F-03 (CLI/API parity audit, Apr 2026): schema version of this payload.
    /// Mirrors CliDryRunOutput.SchemaVersion. Default = RunConstants.ApiOutputSchemaVersion.</summary>
    public string SchemaVersion { get; init; } = Romulus.Contracts.RunConstants.ApiOutputSchemaVersion;
    /// <summary>F-03: run mode (DryRun / Move) — mirrored from RunRecord.Mode so callers do
    /// not have to inspect RunStatusDto separately.</summary>
    public string Mode { get; init; } = Romulus.Contracts.RunConstants.ModeDryRun;
    /// <summary>F-03: short alias for OrchestratorStatus (ok / completed_with_errors / blocked / cancelled).
    /// Provided for entry-point parity with CLI which exposes the same field as `Status`.</summary>
    public string Status { get; init; } = "";
    /// <summary>Orchestrator-level status (ok, completed_with_errors, blocked, cancelled).
    /// Distinct from RunRecord.Status which tracks lifecycle (pending, running, completed, failed).</summary>
    public string OrchestratorStatus { get; init; } = "";
    public int ExitCode { get; init; }
    public int TotalFiles { get; init; }
    public int Candidates { get; init; }
    public int Groups { get; init; }
    public int Winners { get; init; }
    /// <summary>F-03: alias for Winners — mirrors CLI projection field name (`Keep`) for entry-point parity.</summary>
    public int Keep { get; init; }
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
    public int ConsoleSortUnknown { get; init; }
    public int FailCount { get; init; }
    public long SavedBytes { get; init; }
    public long DurationMs { get; init; }
    public string[] PreflightWarnings { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Deep-dive audit (Orchestration, R2-F9): name of the phase that aborted the run
    /// (Scan, Deduplicate, Move, ...). Null on success / clean cancel paths. Mirrors
    /// <see cref="Romulus.Contracts.Models.RunResult.FailedPhaseName"/> so API consumers
    /// share the same fachliche Wahrheit as Sidecar / RunProjection / Dashboard.
    /// </summary>
    public string? FailedPhaseName { get; init; }
    /// <summary>
    /// Deep-dive audit (Orchestration, R2-F9): status string of the failed phase
    /// (typically "failed"). Null when no phase reported failure.
    /// </summary>
    public string? FailedPhaseStatus { get; init; }
    public ApiPhaseMetrics PhaseMetrics { get; init; } = new() { Phases = Array.Empty<ApiPhaseMetric>() };
    public ApiDedupeGroup[] DedupeGroups { get; init; } = Array.Empty<ApiDedupeGroup>();
    public ApiConversionPlan[] ConversionPlans { get; init; } = Array.Empty<ApiConversionPlan>();
    public ApiConversionBlocked[] ConversionBlocked { get; init; } = Array.Empty<ApiConversionBlocked>();
    public OperationError? Error { get; init; }
}

public sealed class ApiReviewItem
{
    public string MainPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ConsoleKey { get; init; } = string.Empty;
    public string SortDecision { get; init; } = string.Empty;
    public string DecisionClass { get; init; } = string.Empty;
    public string EvidenceTier { get; init; } = string.Empty;
    public string PrimaryMatchKind { get; init; } = string.Empty;
    public string PlatformFamily { get; init; } = string.Empty;
    public string MatchLevel { get; init; } = string.Empty;
    public string MatchReasoning { get; init; } = string.Empty;
    public int DetectionConfidence { get; init; }
    public bool Approved { get; init; }
}

public sealed class ApiReviewQueue
{
    public string RunId { get; init; } = string.Empty;
    public int Total { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public int Returned { get; init; }
    public bool HasMore { get; init; }
    public ApiReviewItem[] Items { get; init; } = Array.Empty<ApiReviewItem>();
}

public sealed class ApiRunList
{
    public int Total { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public int Returned { get; init; }
    public bool HasMore { get; init; }
    public RunStatusDto[] Runs { get; init; } = Array.Empty<RunStatusDto>();
}

public sealed class ApiRunHistoryList
{
    public int Total { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
    public int Returned { get; init; }
    public bool HasMore { get; init; }
    public ApiRunHistoryEntry[] Runs { get; init; } = Array.Empty<ApiRunHistoryEntry>();
}

public sealed class ApiProfileListResponse
{
    public RunProfileSummary[] Profiles { get; init; } = Array.Empty<RunProfileSummary>();
}

public sealed class ApiWorkflowListResponse
{
    public WorkflowScenarioDefinition[] Workflows { get; init; } = Array.Empty<WorkflowScenarioDefinition>();
}

public sealed class ApiRunHistoryEntry
{
    public string RunId { get; init; } = "";
    public DateTime StartedUtc { get; init; }
    public DateTime CompletedUtc { get; init; }
    public string Mode { get; init; } = "";
    public string Status { get; init; } = "";
    public int RootCount { get; init; }
    public string RootFingerprint { get; init; } = "";
    public long DurationMs { get; init; }
    public int TotalFiles { get; init; }
    public long CollectionSizeBytes { get; init; }
    public int Games { get; init; }
    public int Dupes { get; init; }
    public int Junk { get; init; }
    public int DatMatches { get; init; }
    public int ConvertedCount { get; init; }
    public int FailCount { get; init; }
    public long SavedBytes { get; init; }
    public long ConvertSavedBytes { get; init; }
    public int HealthScore { get; init; }
}

public sealed class RunEnvelope
{
    public RunStatusDto? Run { get; init; }
}

public sealed class RunStartEnvelope
{
    public RunStatusDto? Run { get; init; }
    public ApiRunResult? Result { get; init; }
    public bool Reused { get; init; }
    public bool WaitTimedOut { get; init; }
}

public sealed class RunResultEnvelope
{
    public RunStatusDto? Run { get; init; }
    public ApiRunResult? Result { get; init; }
}

public sealed class RunCancelEnvelope
{
    public RunStatusDto? Run { get; init; }
    public bool CancelAccepted { get; init; }
    public bool Idempotent { get; init; }
    public string? CancelledAtUtc { get; init; }
}

public sealed class RunRollbackEnvelope
{
    public RunStatusDto Run { get; init; } = new();
    public bool DryRun { get; init; }
    public int ProvenanceEventsAppended { get; init; }
    public AuditRollbackResult Rollback { get; init; } = new();
}

public sealed class RunReviewApprovalEnvelope
{
    public string RunId { get; init; } = string.Empty;
    public int ApprovedCount { get; init; }
    public int TotalApproved { get; init; }
    public ApiReviewQueue Queue { get; init; } = new();
}

public sealed class ApiReviewApprovalRequest
{
    [JsonPropertyName("consoleKey")]
    public string? ConsoleKey { get; set; }

    [JsonPropertyName("matchLevel")]
    public string? MatchLevel { get; set; }

    [JsonPropertyName("paths")]
    public string[]? Paths { get; set; }
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
    public string Mode { get; init; } = RunConstants.ModeDryRun;
    public string? WorkflowScenarioId { get; init; }
    public string? ProfileId { get; init; }
    public string[] PreferRegions { get; init; } = Array.Empty<string>();
    public bool RemoveJunk { get; init; }
    public bool AggressiveJunk { get; init; }
    public bool SortConsole { get; init; }
    public bool EnableDat { get; init; }
    public bool EnableDatAudit { get; init; }
    public bool EnableDatRename { get; init; }
    public bool OnlyGames { get; init; }
    public bool KeepUnknownWhenOnlyGames { get; init; }
    public string HashType { get; init; } = RunConstants.DefaultHashType;
    public string? ConvertFormat { get; init; }
    public bool ConvertOnly { get; init; }
    public bool ApproveReviews { get; init; }
    public bool ApproveConversionReview { get; init; }
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
            WorkflowScenarioId = run.WorkflowScenarioId,
            ProfileId = run.ProfileId,
            PreferRegions = run.PreferRegions,
            RemoveJunk = run.RemoveJunk,
            AggressiveJunk = run.AggressiveJunk,
            SortConsole = run.SortConsole,
            EnableDat = run.EnableDat,
            EnableDatAudit = run.EnableDatAudit,
            EnableDatRename = run.EnableDatRename,
            OnlyGames = run.OnlyGames,
            KeepUnknownWhenOnlyGames = run.KeepUnknownWhenOnlyGames,
            HashType = run.HashType,
            ConvertFormat = run.ConvertFormat,
            ConvertOnly = run.ConvertOnly,
            ApproveReviews = run.ApproveReviews,
            ApproveConversionReview = run.ApproveConversionReview,
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
