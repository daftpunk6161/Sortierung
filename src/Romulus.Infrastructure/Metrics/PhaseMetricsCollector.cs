using System.Diagnostics;
using System.Text.Json;
using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Metrics;

/// <summary>
/// Phase metrics/timing collector for run phases.
/// Mirrors PhaseMetrics.ps1. Thread-safe via lock.
/// </summary>
public sealed class PhaseMetricsCollector
{
    private readonly object _lock = new();
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
        lock (_lock)
        {
            _runId = Guid.NewGuid().ToString("N")[..16];
            _startedAt = DateTime.UtcNow;
            _phases.Clear();
            _activeStopwatch = null;
            _activePhase = null;
        }
    }

    /// <summary>
    /// Starts timing a new phase. Auto-completes previously active phase.
    /// </summary>
    public void StartPhase(string phaseName, Dictionary<string, object>? meta = null)
    {
        lock (_lock)
        {
            // Auto-complete previous phase
            if (_activePhase != null)
                CompletePhaseInternal();

            _activePhase = new PhaseMetricEntry
            {
                Phase = phaseName,
                StartedAt = DateTime.UtcNow,
                Status = "Running",
                Meta = meta ?? new Dictionary<string, object>()
            };

            _activeStopwatch = Stopwatch.StartNew();
        }
    }

    /// <summary>
    /// Completes the active phase with an optional item count.
    /// </summary>
    public void CompletePhase(int itemCount = 0)
    {
        lock (_lock)
        {
            CompletePhaseInternal(itemCount);
        }
    }

    /// <summary>
    /// Gets the name of the currently active phase, or null if no phase is active.
    /// </summary>
    public string? GetCurrentPhaseName()
    {
        lock (_lock)
        {
            return _activePhase?.Phase;
        }
    }

    private void CompletePhaseInternal(int itemCount = 0)
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
    /// Does NOT auto-complete the active phase (no side-effect).
    /// </summary>
    public PhaseMetricsResult GetMetrics()
    {
        lock (_lock)
        {
            // Work on a snapshot — no mutation of internal state
            var snapshot = new List<PhaseMetricEntry>(_phases);

            // Include the currently active (non-stopped) phase
            if (_activePhase != null && _activeStopwatch != null)
            {
                var activeDuration = _activeStopwatch.Elapsed;
                var activeItemsPerSec = activeDuration.TotalSeconds > 0
                    ? Math.Round(_activePhase.ItemCount / activeDuration.TotalSeconds, 1)
                    : 0;
                snapshot.Add(new PhaseMetricEntry
                {
                    Phase = _activePhase.Phase,
                    StartedAt = _activePhase.StartedAt,
                    Duration = activeDuration,
                    ItemCount = _activePhase.ItemCount,
                    ItemsPerSec = activeItemsPerSec,
                    Status = "Running",
                    Meta = _activePhase.Meta
                });
            }

            var totalMs = snapshot.Sum(p => p.Duration.TotalMilliseconds);

            // Build result entries with calculated percentages
            var resultEntries = snapshot.Select(p => new PhaseMetricEntry
            {
                Phase = p.Phase,
                StartedAt = p.StartedAt,
                Duration = p.Duration,
                ItemCount = p.ItemCount,
                ItemsPerSec = p.ItemsPerSec,
                Status = p.Status,
                Meta = p.Meta,
                PercentOfTotal = totalMs > 0
                    ? Math.Round(p.Duration.TotalMilliseconds / totalMs * 100, 1)
                    : 0
            }).ToList();

            return new PhaseMetricsResult
            {
                RunId = _runId,
                StartedAt = _startedAt,
                TotalDuration = TimeSpan.FromMilliseconds(totalMs),
                Phases = resultEntries
            };
        }
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

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
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

        // Atomic update for -latest: write to temp, then move
        var tempLatest = latestPath + ".tmp";
        File.WriteAllText(tempLatest, json, System.Text.Encoding.UTF8);
        File.Move(tempLatest, latestPath, overwrite: true);
    }
}
