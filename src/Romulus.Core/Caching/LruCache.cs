namespace Romulus.Core.Caching;

/// <summary>
/// Thread-safe LRU (Least Recently Used) cache with configurable max size.
/// Port of PowerShell LRU cache pattern used in Dat.ps1 and Classification.ps1.
/// Uses a dictionary + linked list for O(1) get/set/eviction.
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
    private readonly LinkedList<CacheEntry> _list = new();
    private readonly object _lock = new();
    private int _maxEntries;

    public LruCache(int maxEntries, IEqualityComparer<TKey>? comparer = null)
    {
        if (maxEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "Must be >= 1.");

        _maxEntries = maxEntries;
        _map = new Dictionary<TKey, LinkedListNode<CacheEntry>>(comparer ?? EqualityComparer<TKey>.Default);
    }

    public int MaxEntries
    {
        get { lock (_lock) { return _maxEntries; } }
        set
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
            lock (_lock)
            {
                _maxEntries = value;
                Evict();
            }
        }
    }

    public int Count
    {
        get { lock (_lock) { return _map.Count; } }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // Move to front (most recently used)
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                // Update value and move to front
                existing.Value = new CacheEntry(key, value);
                _list.Remove(existing);
                _list.AddFirst(existing);
                return;
            }

            var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, value));
            _list.AddFirst(node);
            _map[key] = node;

            Evict();
        }
    }

    public bool ContainsKey(TKey key)
    {
        lock (_lock)
        {
            return _map.ContainsKey(key);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _list.Clear();
            _map.Clear();
        }
    }

    /// <summary>
    /// Returns a snapshot of all current entries (for diagnostics/serialization).
    /// </summary>
    public IReadOnlyList<KeyValuePair<TKey, TValue>> GetSnapshot()
    {
        lock (_lock)
        {
            var result = new List<KeyValuePair<TKey, TValue>>(_map.Count);
            foreach (var node in _list)
                result.Add(new KeyValuePair<TKey, TValue>(node.Key, node.Value));
            return result;
        }
    }

    private void Evict()
    {
        while (_map.Count > _maxEntries)
        {
            var last = _list.Last;
            if (last is null) break;
            _map.Remove(last.Value.Key);
            _list.RemoveLast();
        }
    }

    private readonly record struct CacheEntry(TKey Key, TValue Value);
}
