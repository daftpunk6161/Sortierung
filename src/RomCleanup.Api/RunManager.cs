using System.Collections.Concurrent;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.Api;

/// <summary>
/// Manages run lifecycle: creation (singleton), execution, cancellation, results.
/// Now delegates to RunOrchestrator for the actual pipeline.
/// </summary>
public sealed class RunManager
{
    private readonly ConcurrentDictionary<string, RunRecord> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _activeLock = new();
    private const int MaxRunHistory = 100;
    private string? _activeRunId;
    private Task? _activeTask;
    private readonly IFileSystem _fs;
    private readonly IAuditStore _audit;

    public RunManager(IFileSystem fs, IAuditStore audit)
    {
        _fs = fs;
        _audit = audit;
    }

    public RunRecord? TryCreate(RunRequest request, string mode)
    {
        lock (_activeLock)
        {
            if (_activeRunId is not null)
                return null;

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
                StartedUtc = DateTime.UtcNow
            };

            _runs[runId] = record;
            _activeRunId = runId;

            _activeTask = Task.Run(() => ExecuteRun(record));

            return record;
        }
    }

    public RunRecord? Get(string runId) =>
        _runs.TryGetValue(runId, out var run) ? run : null;

    public RunRecord? GetActive()
    {
        var id = _activeRunId;
        return id is not null ? Get(id) : null;
    }

    public void Cancel(string runId)
    {
        if (_runs.TryGetValue(runId, out var run) && run.Status == "running")
        {
            try { run.CancellationSource.Cancel(); }
            catch (ObjectDisposedException) { /* CTS already disposed — run already finished */ }
        }
    }

    public async Task WaitForCompletion(string runId, int pollMs = 250)
    {
        while (_runs.TryGetValue(runId, out var run) && run.Status == "running")
        {
            await Task.Delay(pollMs);
        }
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
        try
        {
            var ct = run.CancellationSource.Token;

            var orchestrator = new RunOrchestrator(_fs, _audit,
                onProgress: msg => run.ProgressMessage = msg);

            // Generate audit and report paths per-run
            var auditDir = Path.Combine(Path.GetTempPath(), "RomCleanup", "audit");
            Directory.CreateDirectory(auditDir);
            var auditPath = Path.Combine(auditDir, $"audit-{run.RunId}.csv");
            var reportPath = Path.Combine(auditDir, $"report-{run.RunId}.html");

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

            run.Result = new ApiRunResult
            {
                Status = result.Status,
                ExitCode = result.ExitCode,
                TotalFiles = result.TotalFilesScanned,
                Groups = result.GroupCount,
                Keep = result.WinnerCount,
                Move = result.LoserCount,
                DurationMs = result.DurationMs,
                AuditPath = File.Exists(auditPath) ? auditPath : null,
                ReportPath = File.Exists(reportPath) ? reportPath : null
            };
            run.Status = result.ExitCode switch
            {
                0 => "completed",
                2 => "cancelled",
                _ => "failed"
            };
        }
        catch (OperationCanceledException)
        {
            run.Status = "cancelled";
            run.Result = new ApiRunResult { Status = "cancelled", ExitCode = 2 };
        }
        catch (Exception)
        {
            run.Status = "failed";
            run.Result = new ApiRunResult { Status = "failed", ExitCode = 1, Error = "Internal error during run execution." };
        }
        finally
        {
            run.CompletedUtc = DateTime.UtcNow;
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
            _runs.TryRemove(old.RunId, out _);
    }
}

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

    public string RunId { get; init; } = "";
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
