using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using RomCleanup.Contracts.Errors;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Paths;

namespace RomCleanup.Api;

/// <summary>
/// Manages run lifecycle: creation (singleton), execution, cancellation, results.
/// Execution is delegated via an injected executor function.
/// </summary>
public sealed class RunLifecycleManager
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

    public RunLifecycleManager(
        IFileSystem fs,
        IAuditStore audit,
        Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, RunExecutionOutcome> executor)
    {
        _fs = fs;
        _audit = audit;
        _executor = executor;
    }

    public RunRecord? TryCreate(RunRequest request, string mode, string? ownerClientId = null)
    {
        var create = TryCreateOrReuse(request, mode, ownerClientId: ownerClientId);
        return create.Disposition == RunCreateDisposition.Created ? create.Run : null;
    }

    public RunCreateResult TryCreateOrReuse(
        RunRequest request,
        string mode,
        string? idempotencyKey = null,
        string? ownerClientId = null)
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
            var normalizedConflictPolicy = string.IsNullOrWhiteSpace(request.ConflictPolicy)
                ? "Rename"
                : request.ConflictPolicy.Trim();

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
                DatRoot = string.IsNullOrWhiteSpace(request.DatRoot) ? null : request.DatRoot.Trim(),
                OnlyGames = request.OnlyGames,
                KeepUnknownWhenOnlyGames = request.KeepUnknownWhenOnlyGames,
                HashType = normalizedHashType,
                ConvertFormat = string.IsNullOrWhiteSpace(request.ConvertFormat) ? null : request.ConvertFormat.Trim().ToLowerInvariant(),
                ConvertOnly = request.ConvertOnly,
                ConflictPolicy = normalizedConflictPolicy,
                TrashRoot = string.IsNullOrWhiteSpace(request.TrashRoot) ? null : request.TrashRoot.Trim(),
                Extensions = normalizedExtensions,
                StartedUtc = DateTime.UtcNow,
                IdempotencyKey = idempotencyKey,
                RequestFingerprint = requestFingerprint,
                RecoveryState = "in-progress"
            };

            if (!string.IsNullOrWhiteSpace(ownerClientId))
                record.OwnerClientId = ownerClientId.Trim();

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
            run.CancelledAtUtc = DateTime.UtcNow;
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
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                TryWriteEmergencyShutdownSidecar(activeId);
            }
            catch (Exception)
            {
                TryWriteEmergencyShutdownSidecar(activeId);
            }
        }
    }

    internal static (string AuditPath, string ReportPath) GetArtifactPaths(string runId)
    {
        var auditDir = RomCleanup.Infrastructure.Audit.AuditSecurityPaths.GetDefaultAuditDirectory();
        var reportDir = RomCleanup.Infrastructure.Audit.AuditSecurityPaths.GetDefaultReportDirectory();
        Directory.CreateDirectory(auditDir);
        Directory.CreateDirectory(reportDir);
        return (
            Path.Combine(auditDir, $"audit-{runId}.csv"),
            Path.Combine(reportDir, $"report-{runId}.html"));
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
            run.ProgressPercent = 100;
            // SEC: If run completed despite cancellation request, clear misleading CancelledAtUtc
            if (run.Status is "completed" or "completed_with_errors" && run.CancelledAtUtc is not null)
                run.CancelledAtUtc = null;
        }
        catch (OperationCanceledException)
        {
            var elapsedMs = (long)Math.Max(0, (DateTime.UtcNow - run.StartedUtc).TotalMilliseconds);
            run.Status = "cancelled";
            run.Result = new ApiRunResult
            {
                OrchestratorStatus = "cancelled",
                ExitCode = 2,
                DurationMs = elapsedMs
            };
            run.ProgressPercent = 100;
        }
        catch (Exception ex)
        {
            run.Status = "failed";
            // SEC: Do not leak exception details to clients — log internally, return generic message
            SafeLog($"[ERROR] Run {run.RunId} failed: {ex}");
            run.Result = new ApiRunResult
            {
                OrchestratorStatus = "failed",
                ExitCode = 1,
                Error = new OperationError("RUN-INTERNAL-ERROR", "An internal error occurred during execution.", ErrorKind.Critical, "API")
            };
            run.ProgressPercent = 100;
        }
        finally
        {
            run.AuditPath = File.Exists(auditPath) ? auditPath : run.AuditPath;
            run.ReportPath = File.Exists(reportPath) ? reportPath : run.ReportPath;
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

    private static void UpdateRecoveryState(RunRecord run)
    {
        var hasAudit = !string.IsNullOrWhiteSpace(run.AuditPath);
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

    private void TryWriteEmergencyShutdownSidecar(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return;

        var (auditPath, _) = GetArtifactPaths(runId);
        if (!File.Exists(auditPath))
            return;

        try
        {
            _audit.WriteMetadataSidecar(auditPath, new Dictionary<string, object>
            {
                ["Status"] = "emergency-shutdown",
                ["ShutdownUtc"] = DateTime.UtcNow.ToString("o")
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best effort during host shutdown
        }
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
            request.EnableDatAudit ? "1" : "0",
            request.EnableDatRename ? "1" : "0",
            request.OnlyGames ? "1" : "0",
            request.KeepUnknownWhenOnlyGames ? "1" : "0",
            string.IsNullOrWhiteSpace(request.DatRoot) ? "" : ArtifactPathResolver.NormalizeRootForIdentity(request.DatRoot),
            string.IsNullOrWhiteSpace(request.HashType) ? "SHA1" : request.HashType.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(request.ConvertFormat) ? "" : "AUTO",
            string.IsNullOrWhiteSpace(request.ConflictPolicy) ? "RENAME" : request.ConflictPolicy.Trim().ToUpperInvariant(),
            request.ConvertOnly ? "1" : "0",
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

    private static void SafeLog(string message)
    {
        try { Console.Error.WriteLine(message); }
        catch (ObjectDisposedException) { }
    }
}
