using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Watch;

namespace Romulus.Api;

/// <summary>
/// API-local composition service that wires the shared watch/schedule services to the run manager.
/// </summary>
public sealed class ApiAutomationService : IDisposable
{
    private readonly object _sync = new();
    private readonly RunManager _runManager;
    private readonly WatchFolderService _watchService = new();
    private readonly ScheduleService _scheduleService = new();
    private bool _disposed;
    private bool _active;
    private RunRequest? _request;
    private string _mode = RunConstants.ModeDryRun;
    private string _ownerClientId = string.Empty;
    private int _debounceSeconds = 5;
    private int? _intervalMinutes;
    private string? _cronExpression;
    private DateTime? _lastTriggerUtc;
    private string? _lastTriggerSource;
    private string? _lastRunId;
    private string? _lastError;

    public ApiAutomationService(RunManager runManager)
    {
        _runManager = runManager ?? throw new ArgumentNullException(nameof(runManager));
        _watchService.IsBusyCheck = () => _runManager.GetActive() is not null;
        _scheduleService.IsBusyCheck = () => _runManager.GetActive() is not null;
        _watchService.RunTriggered += OnWatchTriggered;
        _watchService.WatcherError += OnWatcherError;
        _scheduleService.Triggered += OnScheduleTriggered;
    }

    public ApiWatchStatus Start(
        RunRequest request,
        string mode,
        string ownerClientId,
        int debounceSeconds,
        int? intervalMinutes,
        string? cronExpression)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_sync)
        {
            ThrowIfDisposed();

            StopInternal();

            _request = CloneRequest(request);
            _mode = mode;
            _ownerClientId = ownerClientId;
            _debounceSeconds = Math.Max(1, debounceSeconds);
            _intervalMinutes = intervalMinutes.GetValueOrDefault() > 0 ? intervalMinutes : null;
            _cronExpression = string.IsNullOrWhiteSpace(cronExpression) ? null : cronExpression.Trim();
            _lastError = null;

            var watchedRootCount = _watchService.Start(_request.Roots ?? Array.Empty<string>(), _debounceSeconds);
            var scheduleActive = _scheduleService.Start(_intervalMinutes, _cronExpression);
            _active = watchedRootCount > 0 || scheduleActive;

            return GetStatusUnsafe();
        }
    }

    public ApiWatchStatus Stop()
    {
        lock (_sync)
        {
            StopInternal();
            return GetStatusUnsafe();
        }
    }

    public ApiWatchStatus GetStatus()
    {
        lock (_sync)
            return GetStatusUnsafe();
    }

    public bool CanAccess(string ownerClientId)
    {
        lock (_sync)
            return !_active || string.Equals(_ownerClientId, ownerClientId, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _watchService.RunTriggered -= OnWatchTriggered;
            _watchService.WatcherError -= OnWatcherError;
            _scheduleService.Triggered -= OnScheduleTriggered;
            StopInternal();
            _watchService.Dispose();
            _scheduleService.Dispose();
        }
    }

    private void OnWatchTriggered()
        => _ = TriggerRunAsync("watch");

    private void OnScheduleTriggered()
        => _ = TriggerRunAsync("schedule");

    private void OnWatcherError(string message)
    {
        lock (_sync)
            _lastError = message;
    }

    private async Task TriggerRunAsync(string source)
    {
        RunRequest? request;
        string mode;
        string ownerClientId;

        lock (_sync)
        {
            if (!_active || _request is null)
                return;

            request = CloneRequest(_request);
            mode = _mode;
            ownerClientId = _ownerClientId;
            _lastTriggerUtc = DateTime.UtcNow;
            _lastTriggerSource = source;
            _lastError = null;
        }

        var create = _runManager.TryCreateOrReuse(request, mode, ownerClientId: ownerClientId);
        if (create.Run is null)
        {
            lock (_sync)
                _lastError = create.Error ?? "Automated run trigger failed.";
            return;
        }

        lock (_sync)
            _lastRunId = create.Run.RunId;

        if (create.Disposition == RunCreateDisposition.ActiveConflict)
        {
            if (string.Equals(source, "watch", StringComparison.OrdinalIgnoreCase))
                _watchService.MarkPendingWhileBusy();
            else
                _scheduleService.MarkPendingWhileBusy();
            return;
        }

        if (create.Disposition is RunCreateDisposition.Created or RunCreateDisposition.Reused)
        {
            try
            {
                await _runManager.WaitForCompletion(
                    create.Run.RunId,
                    timeout: TimeSpan.FromHours(24)).ConfigureAwait(false);
            }
            finally
            {
                _watchService.FlushPendingIfNeeded();
                _scheduleService.FlushPendingIfNeeded();
            }
        }
    }

    private ApiWatchStatus GetStatusUnsafe()
    {
        var roots = _request?.Roots ?? Array.Empty<string>();
        return new ApiWatchStatus
        {
            Active = _active,
            Mode = _mode,
            Roots = roots,
            DebounceSeconds = _debounceSeconds,
            IntervalMinutes = _intervalMinutes,
            CronExpression = _cronExpression,
            WatchActive = _watchService.IsActive,
            ScheduleActive = _scheduleService.IsActive,
            WatchPending = _watchService.HasPending,
            SchedulePending = _scheduleService.HasPending,
            WatchedRootCount = roots.Length,
            LastTriggerUtc = _lastTriggerUtc,
            LastTriggerSource = _lastTriggerSource,
            LastRunId = _lastRunId,
            LastError = _lastError
        };
    }

    private void StopInternal()
    {
        _active = false;
        _watchService.Stop();
        _scheduleService.Stop();
        _request = null;
        _mode = RunConstants.ModeDryRun;
        _ownerClientId = string.Empty;
        _debounceSeconds = 5;
        _intervalMinutes = null;
        _cronExpression = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static RunRequest CloneRequest(RunRequest request)
        => new()
        {
            Roots = request.Roots?.ToArray(),
            Mode = request.Mode,
            WorkflowScenarioId = request.WorkflowScenarioId,
            ProfileId = request.ProfileId,
            PreferRegions = request.PreferRegions?.ToArray(),
            RemoveJunk = request.RemoveJunk,
            AggressiveJunk = request.AggressiveJunk,
            SortConsole = request.SortConsole,
            EnableDat = request.EnableDat,
            EnableDatAudit = request.EnableDatAudit,
            EnableDatRename = request.EnableDatRename,
            DatRoot = request.DatRoot,
            OnlyGames = request.OnlyGames,
            KeepUnknownWhenOnlyGames = request.KeepUnknownWhenOnlyGames,
            HashType = request.HashType,
            ConvertFormat = request.ConvertFormat,
            ConvertOnly = request.ConvertOnly,
            ApproveReviews = request.ApproveReviews,
            ApproveConversionReview = request.ApproveConversionReview,
            ConflictPolicy = request.ConflictPolicy,
            TrashRoot = request.TrashRoot,
            Extensions = request.Extensions?.ToArray()
        };
}

public sealed class ApiWatchStatus
{
    public bool Active { get; init; }
    public string Mode { get; init; } = RunConstants.ModeDryRun;
    public string[] Roots { get; init; } = Array.Empty<string>();
    public int DebounceSeconds { get; init; }
    public int? IntervalMinutes { get; init; }
    public string? CronExpression { get; init; }
    public bool WatchActive { get; init; }
    public bool ScheduleActive { get; init; }
    public bool WatchPending { get; init; }
    public bool SchedulePending { get; init; }
    public int WatchedRootCount { get; init; }
    public DateTime? LastTriggerUtc { get; init; }
    public string? LastTriggerSource { get; init; }
    public string? LastRunId { get; init; }
    public string? LastError { get; init; }
}
