using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Classification;
using RomCleanup.Infrastructure.Conversion;
using RomCleanup.Infrastructure.Dat;
using RomCleanup.Infrastructure.Hashing;
using RomCleanup.Infrastructure.Paths;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Tools;

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
                if (activeRun is null || activeRun.Status != "running")
                {
                    // Stale active marker from a run that already completed.
                    // This closes a race where status flips before finally clears _activeRunId.
                    _activeRunId = null;
                    _activeTask = null;
                }
                else
                {
                    return new RunCreateResult(RunCreateDisposition.ActiveConflict, activeRun,
                        "A run is already active.");
                }
            }

            var runId = Guid.NewGuid().ToString("N");
            var normalizedExtensions = (request.Extensions is { Length: > 0 }
                    ? request.Extensions
                    : RunOptions.DefaultExtensions)
                .Where(ext => !string.IsNullOrWhiteSpace(ext))
                .Select(ext => ext.Trim())
                .Select(ext => ext.StartsWith('.') ? ext : "." + ext)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var normalizedHashType = string.IsNullOrWhiteSpace(request.HashType)
                ? "SHA1"
                : request.HashType.Trim().ToUpperInvariant();

            var record = new RunRecord
            {
                RunId = runId,
                Status = "running",
                Mode = mode,
                Roots = request.Roots!,
                PreferRegions = request.PreferRegions is { Length: > 0 }
                    ? request.PreferRegions
                    : new[] { "EU", "US", "WORLD", "JP" },
                RemoveJunk = request.RemoveJunk,
                AggressiveJunk = request.AggressiveJunk,
                SortConsole = request.SortConsole,
                EnableDat = request.EnableDat,
                OnlyGames = request.OnlyGames,
                KeepUnknownWhenOnlyGames = request.KeepUnknownWhenOnlyGames,
                HashType = normalizedHashType,
                ConvertFormat = string.IsNullOrWhiteSpace(request.ConvertFormat) ? null : request.ConvertFormat.Trim(),
                TrashRoot = string.IsNullOrWhiteSpace(request.TrashRoot) ? null : request.TrashRoot.Trim(),
                Extensions = normalizedExtensions,
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
                OrchestratorStatus = "cancelled",
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
                OrchestratorStatus = "failed",
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
            Extensions = run.Extensions,
            RemoveJunk = run.RemoveJunk,
            AggressiveJunk = run.AggressiveJunk,
            SortConsole = run.SortConsole,
            EnableDat = run.EnableDat,
            OnlyGames = run.OnlyGames,
            KeepUnknownWhenOnlyGames = run.KeepUnknownWhenOnlyGames,
            HashType = run.HashType,
            ConvertFormat = run.ConvertFormat,
            TrashRoot = run.TrashRoot,
            AuditPath = auditPath,
            ReportPath = reportPath
        };

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
            new ApiRunResult
            {
                OrchestratorStatus = projection.Status,
                ExitCode = projection.ExitCode,
                TotalFiles = projection.TotalFiles,
                Candidates = projection.Candidates,
                Groups = projection.Groups,
                Keep = projection.Keep,
                Dupes = projection.Dupes,
                Games = projection.Games,
                Unknown = projection.Unknown,
                Junk = projection.Junk,
                Bios = projection.Bios,
                DatMatches = projection.DatMatches,
                HealthScore = projection.HealthScore,
                ConvertedCount = projection.ConvertedCount,
                ConvertErrorCount = projection.ConvertErrorCount,
                ConvertSkippedCount = projection.ConvertSkippedCount,
                JunkRemovedCount = projection.JunkRemovedCount,
                FilteredNonGameCount = projection.FilteredNonGameCount,
                JunkFailCount = projection.JunkFailCount,
                MoveCount = projection.MoveCount,
                SkipCount = projection.SkipCount,
                ConsoleSortMoved = projection.ConsoleSortMoved,
                ConsoleSortFailed = projection.ConsoleSortFailed,
                FailCount = projection.FailCount,
                SavedBytes = projection.SavedBytes,
                DurationMs = projection.DurationMs,
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
            string.Join(",", regions),
            request.RemoveJunk ? "1" : "0",
            request.AggressiveJunk ? "1" : "0",
            request.SortConsole ? "1" : "0",
            request.EnableDat ? "1" : "0",
            request.OnlyGames ? "1" : "0",
            request.KeepUnknownWhenOnlyGames ? "1" : "0",
            string.IsNullOrWhiteSpace(request.HashType) ? "SHA1" : request.HashType.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(request.ConvertFormat) ? "" : request.ConvertFormat.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(request.TrashRoot) ? "" : ArtifactPathResolver.NormalizeRootForIdentity(request.TrashRoot),
            string.Join(",", (request.Extensions ?? Array.Empty<string>())
                .Where(ext => !string.IsNullOrWhiteSpace(ext))
                .Select(ext => ext.Trim().ToLowerInvariant())
                .Select(ext => ext.StartsWith('.') ? ext : "." + ext)
                .OrderBy(ext => ext, StringComparer.Ordinal))
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

    // ADR-0007 §3.1: Additional options for API parity with CLI/WPF
    public bool RemoveJunk { get; set; } = true;
    public bool AggressiveJunk { get; set; }
    public bool SortConsole { get; set; }
    public bool EnableDat { get; set; }
    public bool OnlyGames { get; set; }
    public bool KeepUnknownWhenOnlyGames { get; set; } = true;
    public string? HashType { get; set; }
    public string? ConvertFormat { get; set; }
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
    public bool RemoveJunk { get; init; } = true;
    public bool AggressiveJunk { get; init; }
    public bool SortConsole { get; init; }
    public bool EnableDat { get; init; }
    public bool OnlyGames { get; init; }
    public bool KeepUnknownWhenOnlyGames { get; init; } = true;
    public string HashType { get; init; } = "SHA1";
    public string? ConvertFormat { get; init; }
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
    /// <summary>Orchestrator-level status (ok, completed_with_errors, blocked, cancelled).
    /// Distinct from RunRecord.Status which tracks lifecycle (pending, running, completed, failed).</summary>
    public string OrchestratorStatus { get; init; } = "";
    public int ExitCode { get; init; }
    public int TotalFiles { get; init; }
    public int Candidates { get; init; }
    public int Groups { get; init; }
    public int Keep { get; init; }
    /// <summary>Number of duplicate ROMs identified (losers in deduplication).
    /// In DryRun mode this is the count of files that *would* be moved, not actually moved files.</summary>
    public int Dupes { get; init; }
    public int Games { get; init; }
    public int Unknown { get; init; }
    public int Junk { get; init; }
    public int Bios { get; init; }
    public int DatMatches { get; init; }
    public int HealthScore { get; init; }
    public int ConvertedCount { get; init; }
    public int ConvertErrorCount { get; init; }
    public int ConvertSkippedCount { get; init; }
    public int JunkRemovedCount { get; init; }
    public int FilteredNonGameCount { get; init; }
    public int JunkFailCount { get; init; }
    public int MoveCount { get; init; }
    public int SkipCount { get; init; }
    public int ConsoleSortMoved { get; init; }
    public int ConsoleSortFailed { get; init; }
    public int FailCount { get; init; }
    public long SavedBytes { get; init; }
    public long DurationMs { get; init; }
    public string? Error { get; init; }
    public string? AuditPath { get; init; }
    public string? ReportPath { get; init; }
}
