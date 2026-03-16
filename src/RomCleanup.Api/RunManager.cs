using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Paths;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.Api;

/// <summary>
/// Manages run lifecycle: creation (singleton), execution, cancellation, results.
/// Now delegates to RunOrchestrator for the actual pipeline.
/// </summary>
public sealed class RunManager
{
    private readonly ConcurrentDictionary<string, RunRecord> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _idempotencyIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _activeLock = new();
    private const int MaxRunHistory = 100;
    private string? _activeRunId;
    private Task? _activeTask;
    private readonly IFileSystem _fs;
    private readonly IAuditStore _audit;
    private readonly Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, RunExecutionOutcome> _executor;

    public RunManager(
        IFileSystem fs,
        IAuditStore audit,
        Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, RunExecutionOutcome>? executor = null)
    {
        _fs = fs;
        _audit = audit;
        _executor = executor ?? ExecuteWithOrchestrator;
    }

    public RunRecord? TryCreate(RunRequest request, string mode)
    {
        var create = TryCreateOrReuse(request, mode);
        return create.Disposition == RunCreateDisposition.Created ? create.Run : null;
    }

    public RunCreateResult TryCreateOrReuse(RunRequest request, string mode, string? idempotencyKey = null)
    {
        lock (_activeLock)
        {
            var requestFingerprint = BuildRequestFingerprint(request, mode);

            if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
                _idempotencyIndex.TryGetValue(idempotencyKey, out var indexedRunId))
            {
                if (_runs.TryGetValue(indexedRunId, out var existingRun))
                {
                    if (!string.Equals(existingRun.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
                        return new RunCreateResult(RunCreateDisposition.IdempotencyConflict, existingRun,
                            "Idempotency key already used for a different request.");

                    return new RunCreateResult(RunCreateDisposition.Reused, existingRun);
                }

                _idempotencyIndex.TryRemove(idempotencyKey, out _);
            }

            if (_activeRunId is not null)
            {
                var activeRun = Get(_activeRunId);
                return new RunCreateResult(RunCreateDisposition.ActiveConflict, activeRun,
                    "A run is already active.");
            }

            var runId = Guid.NewGuid().ToString("N");
            var record = new RunRecord
            {
                RunId = runId,
                Status = "running",
                Mode = mode,
                Roots = request.Roots!,
                PreferRegions = request.PreferRegions is { Length: > 0 }
                    ? request.PreferRegions
                    : new[] { "EU", "US", "WORLD", "JP" },
                StartedUtc = DateTime.UtcNow,
                IdempotencyKey = idempotencyKey,
                RequestFingerprint = requestFingerprint,
                RecoveryState = "in-progress"
            };

            _runs[runId] = record;
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
                _idempotencyIndex[idempotencyKey] = runId;
            _activeRunId = runId;

            _activeTask = Task.Run(() => ExecuteRun(record));

            return new RunCreateResult(RunCreateDisposition.Created, record);
        }
    }

    public RunRecord? Get(string runId) =>
        _runs.TryGetValue(runId, out var run) ? run : null;

    public RunRecord? GetActive()
    {
        var id = _activeRunId;
        return id is not null ? Get(id) : null;
    }

    public RunCancelResult Cancel(string runId)
    {
        if (!_runs.TryGetValue(runId, out var run))
            return new RunCancelResult(RunCancelDisposition.NotFound, null);

        if (run.Status == "running")
        {
            run.CancellationRequested = true;
            try { run.CancellationSource.Cancel(); }
            catch (ObjectDisposedException) { /* CTS already disposed — run already finished */ }
            return new RunCancelResult(RunCancelDisposition.Accepted, run);
        }

        return new RunCancelResult(RunCancelDisposition.NoOp, run);
    }

    public async Task<RunWaitResult> WaitForCompletion(
        string runId,
        int pollMs = 250,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var start = DateTime.UtcNow;

        while (_runs.TryGetValue(runId, out var run))
        {
            if (run.Status != "running")
                return new RunWaitResult(RunWaitDisposition.Completed, run);

            if (timeout is not null && DateTime.UtcNow - start >= timeout.Value)
                return new RunWaitResult(RunWaitDisposition.TimedOut, run);

            try
            {
                await Task.Delay(pollMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new RunWaitResult(RunWaitDisposition.ClientDisconnected, Get(runId));
            }
        }

        return new RunWaitResult(RunWaitDisposition.NotFound, null);
    }

    /// <summary>
    /// Cancel any active run and wait for completion. Called during host shutdown.
    /// </summary>
    public async Task ShutdownAsync()
    {
        string? activeId;
        Task? task;
        lock (_activeLock)
        {
            activeId = _activeRunId;
            task = _activeTask;
        }

        if (activeId is not null)
            Cancel(activeId);

        if (task is not null)
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch { /* timeout or already completed */ }
        }
    }

    private void ExecuteRun(RunRecord run)
    {
        var (auditPath, reportPath) = GetArtifactPaths(run.RunId);

        try
        {
            var ct = run.CancellationSource.Token;
            var outcome = _executor(run, _fs, _audit, ct);
            run.Status = outcome.Status;
            run.Result = outcome.Result;
        }
        catch (OperationCanceledException)
        {
            run.Status = "cancelled";
            run.Result = new ApiRunResult
            {
                Status = "cancelled",
                ExitCode = 2,
                AuditPath = File.Exists(auditPath) ? auditPath : null,
                ReportPath = File.Exists(reportPath) ? reportPath : null
            };
        }
        catch (Exception)
        {
            run.Status = "failed";
            run.Result = new ApiRunResult
            {
                Status = "failed",
                ExitCode = 1,
                Error = "Internal error during run execution.",
                AuditPath = File.Exists(auditPath) ? auditPath : null,
                ReportPath = File.Exists(reportPath) ? reportPath : null
            };
        }
        finally
        {
            run.CompletedUtc = DateTime.UtcNow;
            UpdateRecoveryState(run);
            run.CancellationSource.Dispose();
            lock (_activeLock)
            {
                if (_activeRunId == run.RunId)
                {
                    _activeRunId = null;
                    _activeTask = null;
                }
            }
            EvictOldRuns();
        }
    }

    private void EvictOldRuns()
    {
        if (_runs.Count <= MaxRunHistory) return;
        var oldest = _runs.Values
            .Where(r => r.Status != "running")
            .OrderBy(r => r.StartedUtc)
            .Take(_runs.Count - MaxRunHistory)
            .ToList();
        foreach (var old in oldest)
        {
            if (!string.IsNullOrWhiteSpace(old.IdempotencyKey))
                _idempotencyIndex.TryRemove(old.IdempotencyKey, out _);
            _runs.TryRemove(old.RunId, out _);
        }
    }

    private static (string AuditPath, string ReportPath) GetArtifactPaths(string runId)
    {
        var auditDir = Path.Combine(Path.GetTempPath(), "RomCleanup", "audit");
        Directory.CreateDirectory(auditDir);
        return (
            Path.Combine(auditDir, $"audit-{runId}.csv"),
            Path.Combine(auditDir, $"report-{runId}.html"));
    }

    private static RunExecutionOutcome ExecuteWithOrchestrator(
        RunRecord run,
        IFileSystem fs,
        IAuditStore audit,
        CancellationToken ct)
    {
        var orchestrator = new RunOrchestrator(fs, audit,
            onProgress: msg => run.ProgressMessage = msg);

        var (auditPath, reportPath) = GetArtifactPaths(run.RunId);

        var options = new RunOptions
        {
            Roots = run.Roots,
            Mode = run.Mode,
            PreferRegions = run.PreferRegions,
            Extensions = RunOptions.DefaultExtensions,
            AuditPath = auditPath,
            ReportPath = reportPath
        };

        var result = orchestrator.Execute(options, ct);
        var status = result.ExitCode switch
        {
            0 => "completed",
            2 => "cancelled",
            _ => "failed"
        };

        return new RunExecutionOutcome(
            status,
            new ApiRunResult
            {
                Status = result.Status,
                ExitCode = result.ExitCode,
                TotalFiles = result.TotalFilesScanned,
                Groups = result.GroupCount,
                Keep = result.WinnerCount,
                Move = result.LoserCount,
                DurationMs = result.DurationMs,
                AuditPath = File.Exists(auditPath) ? auditPath : null,
                ReportPath = result.ReportPath
            });
    }

    private static void UpdateRecoveryState(RunRecord run)
    {
        var hasAudit = !string.IsNullOrWhiteSpace(run.Result?.AuditPath);
        run.RecoveryState = run.Status switch
        {
            "running" => "in-progress",
            "completed" when hasAudit => "rollback-available",
            "completed" => "not-required",
            "cancelled" when hasAudit => "partial-rollback-available",
            "failed" when hasAudit => "partial-rollback-available",
            "cancelled" or "failed" => "manual-cleanup-may-be-required",
            _ => "unknown"
        };
    }

    private static string BuildRequestFingerprint(RunRequest request, string mode)
    {
        var roots = (request.Roots ?? Array.Empty<string>())
            .Select(ArtifactPathResolver.NormalizeRootForIdentity)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase);

        var regions = (request.PreferRegions ?? Array.Empty<string>())
            .Select(region => region.Trim().ToUpperInvariant())
            .OrderBy(region => region, StringComparer.Ordinal);

        var payload = string.Join("\n", new[]
        {
            mode.Trim(),
            string.Join(";", roots),
            string.Join(",", regions)
        });

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
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
    public string? IdempotencyKey { get; init; }
    public string RequestFingerprint { get; init; } = "";
    public string Status
    {
        get { lock (_lock) return _status; }
        set { lock (_lock) _status = value; }
    }
    public string Mode { get; init; } = "DryRun";
    public string[] Roots { get; init; } = Array.Empty<string>();
    public string[] PreferRegions { get; init; } = Array.Empty<string>();
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
    public string RecoveryModel { get; init; } = "audit-rollback-only";
    public string RestartRecovery { get; init; } = "not-persisted";
    public bool ResumeSupported => false;
    public bool CanRetry => Status != "running";
    public bool CanRollback => !string.IsNullOrWhiteSpace(Result?.AuditPath);
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
    public string Status { get; init; } = "";
    public int ExitCode { get; init; }
    public int TotalFiles { get; init; }
    public int Groups { get; init; }
    public int Keep { get; init; }
    public int Move { get; init; }
    public long DurationMs { get; init; }
    public string? Error { get; init; }
    public string? AuditPath { get; init; }
    public string? ReportPath { get; init; }
}
