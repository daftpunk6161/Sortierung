using System.Collections.Concurrent;

namespace RomCleanup.Contracts.Models;

/// <summary>
/// Typed DAT index: maps ConsoleKey → (Hash → GameName).
/// Replaces the untyped IDictionary&lt;string, object&gt; from the initial port.
/// Mirrors the PowerShell structure: $index[$consoleKey][$hash] = $gameName.
/// Thread-safe: uses ConcurrentDictionary for concurrent access during parallel DAT parsing.
/// </summary>
public sealed class DatIndex
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _data = new(StringComparer.OrdinalIgnoreCase);
    private int _totalEntries;

    /// <summary>Maximum entries per console to prevent OOM from malicious DATs. 0 = unlimited.</summary>
    public int MaxEntriesPerConsole { get; init; }

    /// <summary>Number of consoles indexed.</summary>
    public int ConsoleCount => _data.Count;

    /// <summary>Total number of hash entries across all consoles.</summary>
    public int TotalEntries => Volatile.Read(ref _totalEntries);

    /// <summary>Add or update a hash→gameName mapping for a console.</summary>
    public void Add(string consoleKey, string hash, string gameName)
    {
        var hashMap = _data.GetOrAdd(consoleKey, _ => new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        // Allow updates for existing keys even when at capacity
        if (hashMap.ContainsKey(hash))
        {
            hashMap[hash] = gameName;
            return;
        }
        if (MaxEntriesPerConsole > 0 && hashMap.Count >= MaxEntriesPerConsole)
            return;
        if (hashMap.TryAdd(hash, gameName))
            Interlocked.Increment(ref _totalEntries);
    }

    /// <summary>Look up a game name by console key and hash.</summary>
    public string? Lookup(string consoleKey, string hash)
    {
        if (_data.TryGetValue(consoleKey, out var hashMap) &&
            hashMap.TryGetValue(hash, out var name))
            return name;
        return null;
    }

    /// <summary>Check if a console key exists in the index.</summary>
    public bool HasConsole(string consoleKey) => _data.ContainsKey(consoleKey);

    /// <summary>Get all hash→gameName pairs for a console.</summary>
    public IReadOnlyDictionary<string, string>? GetConsoleEntries(string consoleKey)
    {
        return _data.TryGetValue(consoleKey, out var hashMap) ? hashMap : null;
    }

    /// <summary>Get all indexed console keys (snapshot).</summary>
    public IReadOnlyCollection<string> ConsoleKeys => _data.Keys.ToArray();
}
