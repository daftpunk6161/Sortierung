using System.IO;

namespace Romulus.Infrastructure.Watch;

/// <summary>
/// Shared file watch service with debounce, cooldown, busy-queueing, and deterministic toggle semantics.
/// </summary>
public sealed class WatchFolderService : IDisposable
{
    private static readonly TimeSpan CooldownAfterTrigger = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Func<DateTime> _utcNowProvider;
    private Timer? _debounceTimer;
    private DateTime _firstChangeUtc = DateTime.MaxValue;
    private DateTime _lastTriggerUtc = DateTime.MinValue;
    private TimeSpan _debounceInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _maxWait = TimeSpan.FromSeconds(30);
    private bool _pendingWhileBusy;
    private bool _disposed;

    public WatchFolderService(Func<DateTime>? utcNowProvider = null)
    {
        _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
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
        int debounceSeconds = 5,
        int maxWaitSeconds = 30)
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

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                try
                {
                    var watcher = new FileSystemWatcher(root)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };

                    watcher.Changed += OnFileChanged;
                    watcher.Created += OnFileChanged;
                    watcher.Deleted += OnFileChanged;
                    watcher.Renamed += OnFileChanged;
                    watcher.Error += OnWatcherError;
                    _watchers.Add(watcher);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    WatcherError?.Invoke($"FileSystemWatcher-Fehler: {ex.Message}");
                }
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
        var message = e.GetException()?.Message ?? "Unbekannter Watcher-Fehler";
        WatcherError?.Invoke($"FileSystemWatcher-Fehler: {message}");
    }

    private void OnDebounceTimer(object? state)
    {
        bool shouldTrigger;
        lock (_sync)
        {
            if (_disposed || _watchers.Count == 0)
                return;

            _firstChangeUtc = DateTime.MaxValue;
            shouldTrigger = IsBusyCheck?.Invoke() != true;
            if (!shouldTrigger)
            {
                _pendingWhileBusy = true;
                return;
            }

            if ((_utcNowProvider() - _lastTriggerUtc) < CooldownAfterTrigger)
            {
                _pendingWhileBusy = true;
                return;
            }

            _lastTriggerUtc = _utcNowProvider();
        }

        RunTriggered?.Invoke();
    }

    private void EnsureDebounceTimerLocked()
    {
        _debounceTimer ??= new Timer(OnDebounceTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void StopInternal()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnFileChanged;
            watcher.Created -= OnFileChanged;
            watcher.Deleted -= OnFileChanged;
            watcher.Renamed -= OnFileChanged;
            watcher.Error -= OnWatcherError;
            watcher.Dispose();
        }

        _watchers.Clear();
        _firstChangeUtc = DateTime.MaxValue;
        _pendingWhileBusy = false;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
