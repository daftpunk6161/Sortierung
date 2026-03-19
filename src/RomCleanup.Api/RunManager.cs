using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using RomCleanup.Contracts.Errors;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Paths;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Audit;

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
                ConvertFormat = string.IsNullOrWhiteSpace(request.ConvertFormat) ? null : "auto",
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
            catch
            {
                TryWriteEmergencyShutdownSidecar(activeId);
            }
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

    private static void SafeLog(string message)
    {
        try { Console.Error.WriteLine(message); }
        catch (ObjectDisposedException) { }
    }

    private static (string AuditPath, string ReportPath) GetArtifactPaths(string runId)
    {
        var auditDir = AuditSecurityPaths.GetDefaultAuditDirectory();
        var reportDir = AuditSecurityPaths.GetDefaultReportDirectory();
        Directory.CreateDirectory(auditDir);
        Directory.CreateDirectory(reportDir);
        return (
            Path.Combine(auditDir, $"audit-{runId}.csv"),
            Path.Combine(reportDir, $"report-{runId}.html"));
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
        catch
        {
            // best effort during host shutdown
        }
    }

    private static RunExecutionOutcome ExecuteWithOrchestrator(
        RunRecord run,
        IFileSystem fs,
        IAuditStore audit,
        CancellationToken ct)
    {
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
            DatRoot = run.DatRoot,
            OnlyGames = run.OnlyGames,
            KeepUnknownWhenOnlyGames = run.KeepUnknownWhenOnlyGames,
            HashType = run.HashType,
            ConvertFormat = run.ConvertFormat,
            ConvertOnly = run.ConvertOnly,
            ConflictPolicy = run.ConflictPolicy,
            TrashRoot = run.TrashRoot,
            AuditPath = auditPath,
            ReportPath = reportPath
        };

        // Shared setup path with CLI/WPF for DAT/Converter/ConsoleDetector.
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        settings.Dat.UseDat = run.EnableDat;
        settings.Dat.HashType = run.HashType;

        var env = RunEnvironmentBuilder.Build(options, settings, dataDir,
            onWarning: msg =>
            {
                run.ProgressMessage = msg;
                run.ProgressPercent = EstimateProgressPercent(msg);
            });

        var orchestrator = new RunOrchestrator(fs, audit,
            env.ConsoleDetector, env.HashService, env.Converter, env.DatIndex,
            onProgress: msg =>
            {
                run.ProgressMessage = msg;
                run.ProgressPercent = EstimateProgressPercent(msg);
            });

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
                Winners = projection.Keep,
                Losers = projection.Dupes,
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
                PreflightWarnings = result.Preflight?.Warnings?.ToArray() ?? Array.Empty<string>(),
                PhaseMetrics = BuildPhaseMetricsPayload(result.PhaseMetrics),
                DedupeGroups = BuildDedupeGroupsPayload(result.DedupeGroups)
            });
    }

    private static int EstimateProgressPercent(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return 0;

        if (message.StartsWith("[Preflight]", StringComparison.OrdinalIgnoreCase)) return 5;
        if (message.StartsWith("[Scan]", StringComparison.OrdinalIgnoreCase)) return 20;
        if (message.StartsWith("[Filter]", StringComparison.OrdinalIgnoreCase)) return 30;
        if (message.StartsWith("[Dedupe]", StringComparison.OrdinalIgnoreCase)) return 45;
        if (message.StartsWith("[Junk]", StringComparison.OrdinalIgnoreCase)) return 60;
        if (message.StartsWith("[Move]", StringComparison.OrdinalIgnoreCase)) return 75;
        if (message.StartsWith("[Sort]", StringComparison.OrdinalIgnoreCase)) return 85;
        if (message.StartsWith("[Convert]", StringComparison.OrdinalIgnoreCase)) return 92;
        if (message.StartsWith("[Report]", StringComparison.OrdinalIgnoreCase)) return 97;

        return 0;
    }

    private static ApiPhaseMetrics BuildPhaseMetricsPayload(PhaseMetricsResult? metrics)
    {
        if (metrics is null)
            return new ApiPhaseMetrics
            {
                Phases = Array.Empty<ApiPhaseMetric>()
            };

        return new ApiPhaseMetrics
        {
            RunId = metrics.RunId,
            StartedAt = metrics.StartedAt,
            TotalDurationMs = (long)metrics.TotalDuration.TotalMilliseconds,
            Phases = metrics.Phases.Select(phase => new ApiPhaseMetric
            {
                Phase = phase.Phase,
                StartedAt = phase.StartedAt,
                DurationMs = (long)phase.Duration.TotalMilliseconds,
                ItemCount = phase.ItemCount,
                ItemsPerSec = phase.ItemsPerSec,
                PercentOfTotal = phase.PercentOfTotal,
                Status = phase.Status
            }).ToArray()
        };
    }

    private static ApiDedupeGroup[] BuildDedupeGroupsPayload(IReadOnlyList<DedupeResult> dedupeGroups)
    {
        if (dedupeGroups.Count == 0)
            return Array.Empty<ApiDedupeGroup>();

        return dedupeGroups.Select(group => new ApiDedupeGroup
        {
            GameKey = group.GameKey,
            Winner = group.Winner,
            Losers = group.Losers.ToArray()
        }).ToArray();
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
    public string[] PreflightWarnings { get; init; } = Array.Empty<string>();
    public ApiPhaseMetrics PhaseMetrics { get; init; } = new() { Phases = Array.Empty<ApiPhaseMetric>() };
    public ApiDedupeGroup[] DedupeGroups { get; init; } = Array.Empty<ApiDedupeGroup>();
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
