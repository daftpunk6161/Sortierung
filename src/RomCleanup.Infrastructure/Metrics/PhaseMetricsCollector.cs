using System.Diagnostics;
using System.Text.Json;
using RomCleanup.Contracts.Models;

namespace RomCleanup.Infrastructure.Metrics;

/// <summary>
/// Phase metrics/timing collector for run phases.
/// Mirrors PhaseMetrics.ps1.
/// </summary>
public sealed class PhaseMetricsCollector
{
    private string _runId = "";
    private DateTime _startedAt;
    private readonly List<PhaseMetricEntry> _phases = new();
    private Stopwatch? _activeStopwatch;
    private PhaseMetricEntry? _activePhase;

    /// <summary>
    /// Initializes metrics for a new run.
    /// </summary>
    public void Initialize()
    {
        _runId = Guid.NewGuid().ToString("N")[..16];
        _startedAt = DateTime.UtcNow;
        _phases.Clear();
        _activeStopwatch = null;
        _activePhase = null;
    }

    /// <summary>
    /// Starts timing a new phase. Auto-completes previously active phase.
    /// </summary>
    public void StartPhase(string phaseName, Dictionary<string, object>? meta = null)
    {
        // Auto-complete previous phase
        if (_activePhase != null)
            CompletePhase();

        _activePhase = new PhaseMetricEntry
        {
            Phase = phaseName,
            StartedAt = DateTime.UtcNow,
            Status = "Running",
            Meta = meta ?? new Dictionary<string, object>()
        };

        _activeStopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// Completes the active phase with an optional item count.
    /// </summary>
    public void CompletePhase(int itemCount = 0)
    {
        if (_activePhase == null || _activeStopwatch == null)
            return;

        _activeStopwatch.Stop();
        _activePhase.Duration = _activeStopwatch.Elapsed;
        _activePhase.ItemCount = itemCount;
        _activePhase.Status = "Completed";
        _activePhase.ItemsPerSec = _activeStopwatch.Elapsed.TotalSeconds > 0
            ? Math.Round(itemCount / _activeStopwatch.Elapsed.TotalSeconds, 1)
            : 0;

        _phases.Add(_activePhase);
        _activePhase = null;
        _activeStopwatch = null;
    }

    /// <summary>
    /// Gets the collected metrics with percentage-of-total calculation.
    /// </summary>
    public PhaseMetricsResult GetMetrics()
    {
        // Auto-complete active phase
        if (_activePhase != null)
            CompletePhase();

        var totalMs = _phases.Sum(p => p.Duration.TotalMilliseconds);
        foreach (var phase in _phases)
        {
            phase.PercentOfTotal = totalMs > 0
                ? Math.Round(phase.Duration.TotalMilliseconds / totalMs * 100, 1)
                : 0;
        }

        return new PhaseMetricsResult
        {
            RunId = _runId,
            StartedAt = _startedAt,
            TotalDuration = TimeSpan.FromMilliseconds(totalMs),
            Phases = new List<PhaseMetricEntry>(_phases)
        };
    }

    /// <summary>
    /// Exports metrics to a JSON file (+ "-latest" copy).
    /// </summary>
    public void Export(string directory)
    {
        var metrics = GetMetrics();
        var dir = Path.GetFullPath(directory);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var filePath = Path.Combine(dir, $"phase-metrics-{timestamp}.json");
        var latestPath = Path.Combine(dir, "phase-metrics-latest.json");

        var json = JsonSerializer.Serialize(new
        {
            metrics.RunId,
            StartedAt = metrics.StartedAt.ToString("o"),
            TotalDurationMs = (long)metrics.TotalDuration.TotalMilliseconds,
            Phases = metrics.Phases.Select(p => new
            {
                p.Phase,
                StartedAt = p.StartedAt.ToString("o"),
                DurationMs = (long)p.Duration.TotalMilliseconds,
                p.ItemCount,
                p.ItemsPerSec,
                p.PercentOfTotal,
                p.Status
            })
        }, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
        File.Copy(filePath, latestPath, overwrite: true);
    }
}
