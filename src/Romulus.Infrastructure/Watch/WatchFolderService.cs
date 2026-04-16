using System.IO;
using Microsoft.Extensions.Logging;
using Romulus.Infrastructure.Orchestration;

namespace Romulus.Infrastructure.Watch;

/// <summary>
/// Shared file watch service with debounce, cooldown, busy-queueing, and deterministic toggle semantics.
/// </summary>
public sealed class WatchFolderService : IDisposable
{
    private const int DefaultDebounceSeconds = 5;
    private const int DefaultMaxWaitSeconds = 30;
    private const int CooldownAfterTriggerSeconds = 30;
    private const int RecoveryRetryIntervalSeconds = 5;
    private const int WatcherInternalBufferSizeBytes = 64 * 1024;

    private static readonly TimeSpan CooldownAfterTrigger = TimeSpan.FromSeconds(CooldownAfterTriggerSeconds);
    private static readonly TimeSpan RecoveryRetryInterval = TimeSpan.FromSeconds(RecoveryRetryIntervalSeconds);

    private readonly object _sync = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly HashSet<string> _configuredRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTime> _utcNowProvider;
    private readonly ILogger<WatchFolderService>? _logger;
    private Timer? _debounceTimer;
    private Timer? _recoveryTimer;
    private DateTime _firstChangeUtc = DateTime.MaxValue;
    private DateTime _lastTriggerUtc = DateTime.MinValue;
    private TimeSpan _debounceInterval = TimeSpan.FromSeconds(DefaultDebounceSeconds);
    private TimeSpan _maxWait = TimeSpan.FromSeconds(DefaultMaxWaitSeconds);
    private bool _pendingWhileBusy;
    private bool _disposed;

    public WatchFolderService(Func<DateTime>? utcNowProvider = null, ILogger<WatchFolderService>? logger = null)
    {
        _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
        _logger = logger;
    }

    public event Action? RunTriggered;
    public event Action<string>? WatcherError;

    public Func<bool>? IsBusyCheck { get; set; }

    public bool IsActive
    {
        get
        {
            lock (_sync)
                return _watchers.Count > 0;
        }
    }

    public bool HasPending
    {
        get
        {
            lock (_sync)
                return _pendingWhileBusy;
        }
    }

    public int Start(
        IReadOnlyList<string> roots,
        int debounceSeconds = DefaultDebounceSeconds,
        int maxWaitSeconds = DefaultMaxWaitSeconds)
    {
        ArgumentNullException.ThrowIfNull(roots);

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_watchers.Count > 0)
            {
                StopInternal();
                return 0;
            }

            _debounceInterval = TimeSpan.FromSeconds(Math.Max(1, debounceSeconds));
            _maxWait = TimeSpan.FromSeconds(Math.Max(1, maxWaitSeconds));
            _firstChangeUtc = DateTime.MaxValue;
            _pendingWhileBusy = false;
            _lastTriggerUtc = DateTime.MinValue;
            _configuredRoots.Clear();

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                try
                {
                    var normalizedRoot = Path.GetFullPath(root);
                    _configuredRoots.Add(normalizedRoot);
                    TryAttachWatcherLocked(normalizedRoot);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    _logger?.LogWarning(ex, "Failed to attach watcher for root {Root}", root);
                    WatcherError?.Invoke(RunProgressLocalization.Format("Watch.Error", ex.Message));
                }
            }

            if (NeedsRecoveryScanLocked())
            {
                EnsureRecoveryTimerLocked();
                _recoveryTimer!.Change(RecoveryRetryInterval, RecoveryRetryInterval);
            }

            return _watchers.Count;
        }
    }

    public void Stop()
    {
        lock (_sync)
            StopInternal();
    }

    public void MarkPendingWhileBusy()
    {
        lock (_sync)
            _pendingWhileBusy = true;
    }

    public void FlushPendingIfNeeded()
    {
        lock (_sync)
        {
            if (!_pendingWhileBusy || _watchers.Count == 0)
                return;

            _pendingWhileBusy = false;
            EnsureDebounceTimerLocked();
            _debounceTimer!.Change(_debounceInterval, Timeout.InfiniteTimeSpan);
        }
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
            StopInternal();
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_sync)
        {
            if (_disposed || _watchers.Count == 0)
                return;

            var nowUtc = _utcNowProvider();
            if (_firstChangeUtc == DateTime.MaxValue)
                _firstChangeUtc = nowUtc;

            EnsureDebounceTimerLocked();
            if (nowUtc - _firstChangeUtc >= _maxWait)
            {
                _debounceTimer!.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
                return;
            }

            _debounceTimer!.Change(_debounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var message = e.GetException()?.Message ?? RunProgressLocalization.Format("Watch.UnknownError");
        if (e.GetException() is Exception ex)
            _logger?.LogWarning(ex, "File watcher error occurred: {Message}", message);
        else
            _logger?.LogWarning("File watcher error occurred: {Message}", message);
        WatcherError?.Invoke(RunProgressLocalization.Format("Watch.Error", message));

        lock (_sync)
        {
            if (_disposed || _configuredRoots.Count == 0)
                return;

            TryRecoverWatchers();

            if (NeedsRecoveryScanLocked())
            {
                EnsureRecoveryTimerLocked();
                _recoveryTimer!.Change(RecoveryRetryInterval, RecoveryRetryInterval);
            }
            else
            {
                _recoveryTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnRecoveryTimer(object? state)
    {
        lock (_sync)
        {
            if (_disposed || _configuredRoots.Count == 0)
                return;

            TryRecoverWatchers();

            if (!NeedsRecoveryScanLocked())
                _recoveryTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceTimer(object? state)
    {
        bool shouldTrigger;
        lock (_sync)
        {
            if (_disposed || _watchers.Count == 0)
                return;

            var nowUtc = _utcNowProvider();
            _firstChangeUtc = DateTime.MaxValue;
            shouldTrigger = IsBusyCheck?.Invoke() != true;
            if (!shouldTrigger)
            {
                _pendingWhileBusy = true;
                return;
            }

            var cooldownRemaining = CooldownAfterTrigger - (nowUtc - _lastTriggerUtc);
            if (cooldownRemaining > TimeSpan.Zero)
            {
                _pendingWhileBusy = true;
                EnsureDebounceTimerLocked();
                _debounceTimer!.Change(cooldownRemaining, Timeout.InfiniteTimeSpan);
                return;
            }

            _pendingWhileBusy = false;
            _lastTriggerUtc = nowUtc;
        }

        // R3-018 FIX: Check _disposed after lock release to prevent invoking on disposed service.
        if (_disposed) return;

        RunTriggered?.Invoke();
    }

    private void EnsureDebounceTimerLocked()
    {
        _debounceTimer ??= new Timer(OnDebounceTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void EnsureRecoveryTimerLocked()
    {
        _recoveryTimer ??= new Timer(OnRecoveryTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private bool TryRecoverWatchers()
    {
        for (var index = _watchers.Count - 1; index >= 0; index--)
        {
            var watcher = _watchers[index];
            if (Directory.Exists(watcher.Path))
                continue;

            DisposeWatcher(watcher);
            _watchers.RemoveAt(index);
        }

        foreach (var root in _configuredRoots)
        {
            if (IsWatchingRootLocked(root))
                continue;

            TryAttachWatcherLocked(root);
        }

        return !NeedsRecoveryScanLocked();
    }

    private bool TryAttachWatcherLocked(string root)
    {
        if (!Directory.Exists(root) || IsWatchingRootLocked(root))
            return false;

        try
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                // R7-03: Default 8KB buffer is too small for deep ROM trees; 64KB prevents missed events
                InternalBufferSize = WatcherInternalBufferSizeBytes,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            watcher.Error += OnWatcherError;
            _watchers.Add(watcher);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger?.LogWarning(ex, "Watcher attachment failed for root {Root}", root);
            WatcherError?.Invoke(RunProgressLocalization.Format("Watch.Error", ex.Message));
            return false;
        }
    }

    private bool NeedsRecoveryScanLocked()
    {
        foreach (var configuredRoot in _configuredRoots)
        {
            if (!IsWatchingRootLocked(configuredRoot))
                return true;
        }

        return false;
    }

    private bool IsWatchingRootLocked(string root)
    {
        foreach (var watcher in _watchers)
        {
            if (string.Equals(watcher.Path, root, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void DisposeWatcher(FileSystemWatcher watcher)
    {
        watcher.EnableRaisingEvents = false;
        watcher.Changed -= OnFileChanged;
        watcher.Created -= OnFileChanged;
        watcher.Deleted -= OnFileChanged;
        watcher.Renamed -= OnFileChanged;
        watcher.Error -= OnWatcherError;
        watcher.Dispose();
    }

    private void StopInternal()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _recoveryTimer?.Dispose();
        _recoveryTimer = null;

        foreach (var watcher in _watchers)
            DisposeWatcher(watcher);

        _watchers.Clear();
        _configuredRoots.Clear();
        _firstChangeUtc = DateTime.MaxValue;
        _pendingWhileBusy = false;
        _lastTriggerUtc = DateTime.MinValue;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
