using System.Collections.Concurrent;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.Api;

/// <summary>
/// Manages run lifecycle: creation (singleton), execution, cancellation, results.
/// Now delegates to RunOrchestrator for the actual pipeline.
/// </summary>
public sealed class RunManager
{
    private static readonly string[] DefaultExtensions =
    {
        ".zip", ".7z", ".chd", ".iso", ".bin", ".cue", ".gdi", ".ccd",
        ".rvz", ".gcz", ".wbfs", ".nsp", ".xci", ".nes", ".snes",
        ".sfc", ".smc", ".gb", ".gbc", ".gba", ".nds", ".3ds",
        ".n64", ".z64", ".v64", ".md", ".gen", ".sms", ".gg",
        ".pce", ".ngp", ".ws", ".rom", ".pbp", ".pkg"
    };

    private readonly ConcurrentDictionary<string, RunRecord> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _activeLock = new();
    private string? _activeRunId;

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
                PreferRegions = request.PreferRegions ?? new[] { "EU", "US", "WORLD", "JP" },
                StartedUtc = DateTime.UtcNow
            };

            _runs[runId] = record;
            _activeRunId = runId;

            Task.Run(() => ExecuteRun(record));

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
            run.CancellationSource.Cancel();
        }
    }

    public async Task WaitForCompletion(string runId, int pollMs = 250)
    {
        while (_runs.TryGetValue(runId, out var run) && run.Status == "running")
        {
            await Task.Delay(pollMs);
        }
    }

    private void ExecuteRun(RunRecord run)
    {
        try
        {
            var fs = new FileSystemAdapter();
            var audit = new AuditCsvStore();
            var ct = run.CancellationSource.Token;

            var orchestrator = new RunOrchestrator(fs, audit,
                onProgress: msg => run.ProgressMessage = msg);

            var options = new RunOptions
            {
                Roots = run.Roots,
                Mode = run.Mode,
                PreferRegions = run.PreferRegions,
                Extensions = DefaultExtensions
            };

            var result = orchestrator.Execute(options, ct);

            run.Result = new RunResult
            {
                Status = result.Status,
                ExitCode = result.ExitCode,
                TotalFiles = result.TotalFilesScanned,
                Groups = result.GroupCount,
                Keep = result.WinnerCount,
                Move = result.LoserCount,
                DurationMs = result.DurationMs
            };
            run.Status = result.ExitCode == 0 ? "completed" : "failed";
        }
        catch (OperationCanceledException)
        {
            run.Status = "cancelled";
            run.Result = new RunResult { Status = "cancelled", ExitCode = 2 };
        }
        catch (Exception ex)
        {
            run.Status = "failed";
            run.Result = new RunResult { Status = "failed", ExitCode = 1, Error = ex.Message };
        }
        finally
        {
            run.CompletedUtc = DateTime.UtcNow;
            lock (_activeLock)
            {
                if (_activeRunId == run.RunId)
                    _activeRunId = null;
            }
        }
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
    public string RunId { get; init; } = "";
    public string Status { get; set; } = "running";
    public string Mode { get; init; } = "DryRun";
    public string[] Roots { get; init; } = Array.Empty<string>();
    public string[] PreferRegions { get; init; } = Array.Empty<string>();
    public DateTime StartedUtc { get; init; }
    public DateTime? CompletedUtc { get; set; }
    public RunResult? Result { get; set; }
    public string? ProgressMessage { get; set; }

    internal CancellationTokenSource CancellationSource { get; } = new();
}

public sealed class RunResult
{
    public string Status { get; init; } = "";
    public int ExitCode { get; init; }
    public int TotalFiles { get; init; }
    public int Groups { get; init; }
    public int Keep { get; init; }
    public int Move { get; init; }
    public long DurationMs { get; init; }
    public string? Error { get; init; }
}
