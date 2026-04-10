using Romulus.Infrastructure.Watch;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// WPF adapter over the shared <see cref="WatchFolderService"/>.
/// It preserves the existing GUI-facing API while marshalling callbacks onto the UI dispatcher.
/// </summary>
public sealed class WatchService : IDisposable
{
    private readonly WatchFolderService _inner = new();
    private readonly Action _runTriggeredHandler;
    private readonly Action<string> _watcherErrorHandler;
    private bool _disposed;

    public WatchService()
    {
        _runTriggeredHandler = () => DispatchOnUiThread(() => RunTriggered?.Invoke());
        _watcherErrorHandler = message => DispatchOnUiThread(() => WatcherError?.Invoke(message));

        _inner.RunTriggered += _runTriggeredHandler;
        _inner.WatcherError += _watcherErrorHandler;
    }

    public event Action? RunTriggered;
    public event Action<string>? WatcherError;

    public bool IsActive => _inner.IsActive;
    public bool HasPending => _inner.HasPending;

    public Func<bool>? IsBusyCheck
    {
        get => _inner.IsBusyCheck;
        set => _inner.IsBusyCheck = value;
    }

    public int Start(IReadOnlyList<string> roots)
        => _inner.Start(roots);

    public void Stop()
        => _inner.Stop();

    public void FlushPendingIfNeeded()
        => _inner.FlushPendingIfNeeded();

    public void MarkPendingWhileBusy()
        => _inner.MarkPendingWhileBusy();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _inner.RunTriggered -= _runTriggeredHandler;
        _inner.WatcherError -= _watcherErrorHandler;
        _inner.Dispose();
    }

    private static void DispatchOnUiThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.InvokeAsync(action);
    }
}
