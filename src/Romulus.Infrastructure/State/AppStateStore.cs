using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.State;

/// <summary>
/// In-memory implementation of IAppState with undo/redo support.
/// Port of AppState management from PortInterfaces.ps1 / WpfMainViewModel.ps1.
/// </summary>
public sealed class AppStateStore : IAppState
{
    private const int MaxUndoDepth = 100;
    private readonly Dictionary<string, object?> _state = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Dictionary<string, object?>> _undoStack = new();
    private readonly List<Dictionary<string, object?>> _redoStack = new();
    private readonly List<Action<IDictionary<string, object?>>> _watchers = new();
    private readonly object _lock = new();
    private volatile bool _cancelRequested;

    public IDictionary<string, object?> Get()
    {
        lock (_lock)
        {
            return new Dictionary<string, object?>(_state, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Set(IDictionary<string, object?> patch, string reason = "update")
    {
        lock (_lock)
        {
            // Save current state for undo
            _undoStack.Add(new Dictionary<string, object?>(_state, StringComparer.OrdinalIgnoreCase));
            if (_undoStack.Count > MaxUndoDepth)
                _undoStack.RemoveAt(0);
            _redoStack.Clear();

            foreach (var kvp in patch)
                _state[kvp.Key] = kvp.Value;
        }

        NotifyWatchers();
    }

    public IDisposable Watch(Action<IDictionary<string, object?>> handler)
    {
        lock (_lock)
        {
            _watchers.Add(handler);
        }
        return new WatcherDisposable(this, handler);
    }

    public bool Undo()
    {
        lock (_lock)
        {
            if (_undoStack.Count == 0)
                return false;

            _redoStack.Add(new Dictionary<string, object?>(_state, StringComparer.OrdinalIgnoreCase));

            var previous = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);

            _state.Clear();
            foreach (var kvp in previous)
                _state[kvp.Key] = kvp.Value;
        }

        NotifyWatchers();
        return true;
    }

    public bool Redo()
    {
        lock (_lock)
        {
            if (_redoStack.Count == 0)
                return false;

            _undoStack.Add(new Dictionary<string, object?>(_state, StringComparer.OrdinalIgnoreCase));

            var next = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);

            _state.Clear();
            foreach (var kvp in next)
                _state[kvp.Key] = kvp.Value;
        }

        NotifyWatchers();
        return true;
    }

    public T? GetValue<T>(string key, T? defaultValue = default)
    {
        lock (_lock)
        {
            if (_state.TryGetValue(key, out var value) && value is T typed)
                return typed;
            return defaultValue;
        }
    }

    public void SetValue(string key, object? value)
    {
        Set(new Dictionary<string, object?> { [key] = value }, $"SetValue:{key}");
    }

    public bool TestCancel()
    {
        return _cancelRequested;
    }

    public void RequestCancel()
    {
        _cancelRequested = true;
    }

    public void ResetCancel()
    {
        _cancelRequested = false;
    }

    private void NotifyWatchers()
    {
        Action<IDictionary<string, object?>>[] snapshot;
        IDictionary<string, object?> state;

        lock (_lock)
        {
            snapshot = _watchers.ToArray();
            state = new Dictionary<string, object?>(_state, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var handler in snapshot)
        {
            try { handler(state); }
            catch (Exception) { /* watchers must not crash the state store */ }
        }
    }

    private sealed class WatcherDisposable : IDisposable
    {
        private readonly AppStateStore _store;
        private readonly Action<IDictionary<string, object?>> _handler;

        public WatcherDisposable(AppStateStore store, Action<IDictionary<string, object?>> handler)
        {
            _store = store;
            _handler = handler;
        }

        public void Dispose()
        {
            lock (_store._lock)
            {
                _store._watchers.Remove(_handler);
            }
        }
    }
}
