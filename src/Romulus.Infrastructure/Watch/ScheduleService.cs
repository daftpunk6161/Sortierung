namespace Romulus.Infrastructure.Watch;

/// <summary>
/// Shared schedule trigger service for interval- and cron-based automation.
/// </summary>
public sealed class ScheduleService : IDisposable
{
    private readonly object _sync = new();
    private readonly Func<DateTime> _nowProvider;
    private readonly TimeSpan _pollInterval;
    private Timer? _timer;
    private bool _disposed;
    private bool _pendingWhileBusy;
    private int? _intervalMinutes;
    private string? _cronExpression;
    private DateTime _nextIntervalDueUtc = DateTime.MaxValue;
    private string? _lastCronTriggerKey;

    public ScheduleService(Func<DateTime>? nowProvider = null, TimeSpan? pollInterval = null)
    {
        _nowProvider = nowProvider ?? (() => DateTime.Now);
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(15);
    }

    public event Action? Triggered;

    public bool IsActive
    {
        get
        {
            lock (_sync)
                return _timer is not null;
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

    public Func<bool>? IsBusyCheck { get; set; }

    public int? IntervalMinutes
    {
        get
        {
            lock (_sync)
                return _intervalMinutes;
        }
    }

    public string? CronExpression
    {
        get
        {
            lock (_sync)
                return _cronExpression;
        }
    }

    public bool Start(int? intervalMinutes = null, string? cronExpression = null)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            StopInternal();

            var sanitizedInterval = intervalMinutes.GetValueOrDefault() > 0
                ? intervalMinutes
                : null;
            var sanitizedCron = string.IsNullOrWhiteSpace(cronExpression)
                ? null
                : cronExpression.Trim();

            if (sanitizedInterval is null && sanitizedCron is null)
                return false;

            _intervalMinutes = sanitizedInterval;
            _cronExpression = sanitizedCron;
            _pendingWhileBusy = false;
            _lastCronTriggerKey = null;
            _nextIntervalDueUtc = sanitizedInterval is null
                ? DateTime.MaxValue
                : _nowProvider().ToUniversalTime().AddMinutes(sanitizedInterval.Value);

            _timer = new Timer(OnTimerTick, null, _pollInterval, _pollInterval);
            return true;
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
        bool shouldTrigger;
        lock (_sync)
        {
            shouldTrigger = _pendingWhileBusy && _timer is not null && IsBusyCheck?.Invoke() != true;
            if (shouldTrigger)
                _pendingWhileBusy = false;
        }

        if (shouldTrigger)
            Triggered?.Invoke();
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

    private void OnTimerTick(object? state)
    {
        bool shouldTrigger = false;
        lock (_sync)
        {
            if (_disposed || _timer is null)
                return;

            var nowLocal = _nowProvider();
            var nowUtc = nowLocal.ToUniversalTime();
            var isDue = false;

            if (_intervalMinutes is int intervalMinutes && nowUtc >= _nextIntervalDueUtc)
            {
                isDue = true;
                _nextIntervalDueUtc = nowUtc.AddMinutes(intervalMinutes);
            }

            if (!string.IsNullOrWhiteSpace(_cronExpression))
            {
                var cronKey = $"{nowLocal:yyyyMMddHHmm}";
                if (!string.Equals(cronKey, _lastCronTriggerKey, StringComparison.Ordinal)
                    && CronScheduleEvaluator.TestCronMatch(_cronExpression, nowLocal))
                {
                    isDue = true;
                    _lastCronTriggerKey = cronKey;
                }
            }

            if (!isDue)
                return;

            if (IsBusyCheck?.Invoke() == true)
            {
                _pendingWhileBusy = true;
                return;
            }

            shouldTrigger = true;
        }

        if (shouldTrigger)
            Triggered?.Invoke();
    }

    private void StopInternal()
    {
        _timer?.Dispose();
        _timer = null;
        _pendingWhileBusy = false;
        _intervalMinutes = null;
        _cronExpression = null;
        _nextIntervalDueUtc = DateTime.MaxValue;
        _lastCronTriggerKey = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
