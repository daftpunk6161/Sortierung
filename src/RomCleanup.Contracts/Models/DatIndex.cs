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
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DatIndexEntry>> _data = new(StringComparer.OrdinalIgnoreCase);
    private int _totalEntries;

    public readonly record struct DatIndexEntry(string GameName, string? RomFileName);

    /// <summary>Maximum entries per console to prevent OOM from malicious DATs. 0 = unlimited.</summary>
    public int MaxEntriesPerConsole { get; init; }

    /// <summary>Number of consoles indexed.</summary>
    public int ConsoleCount => _data.Count;

    /// <summary>Total number of hash entries across all consoles.</summary>
    public int TotalEntries => Volatile.Read(ref _totalEntries);

    /// <summary>Add or update a hash→gameName mapping for a console.</summary>
    public void Add(string consoleKey, string hash, string gameName, string? romFileName = null)
    {
        var hashMap = _data.GetOrAdd(consoleKey, _ => new ConcurrentDictionary<string, DatIndexEntry>(StringComparer.OrdinalIgnoreCase));
        var newEntry = new DatIndexEntry(gameName, romFileName);

        // Allow updates for existing keys even when at capacity
        if (hashMap.ContainsKey(hash))
        {
            hashMap[hash] = newEntry;
            return;
        }
        if (MaxEntriesPerConsole > 0 && hashMap.Count >= MaxEntriesPerConsole)
            return;
        if (hashMap.TryAdd(hash, newEntry))
            Interlocked.Increment(ref _totalEntries);
    }

    /// <summary>Look up a game name by console key and hash.</summary>
    public string? Lookup(string consoleKey, string hash)
    {
        if (_data.TryGetValue(consoleKey, out var hashMap) &&
            hashMap.TryGetValue(hash, out var entry))
            return entry.GameName;
        return null;
    }

    /// <summary>Look up game name plus optional DAT ROM filename by console key and hash.</summary>
    public DatIndexEntry? LookupWithFilename(string consoleKey, string hash)
    {
        if (_data.TryGetValue(consoleKey, out var hashMap) &&
            hashMap.TryGetValue(hash, out var entry))
            return entry;
        return null;
    }

    /// <summary>
    /// Look up a hash across ALL loaded consoles (fallback when console is unknown).
    /// Returns the first match found: (consoleKey, gameName).
    /// Iterates consoles in deterministic (ordinal-sorted) order so that identical
    /// inputs always produce the same output regardless of ConcurrentDictionary ordering.
    /// </summary>
    public (string ConsoleKey, string GameName)? LookupAny(string hash)
    {
        foreach (var key in _data.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            if (_data.TryGetValue(key, out var hashMap) &&
                hashMap.TryGetValue(hash, out var entry))
                return (key, entry.GameName);
        }
        return null;
    }

    /// <summary>
    /// Looks up all console matches for a hash in deterministic console-key order.
    /// </summary>
    public IReadOnlyList<(string ConsoleKey, DatIndexEntry Entry)> LookupAllByHash(string hash)
    {
        var results = new List<(string ConsoleKey, DatIndexEntry Entry)>();
        foreach (var key in _data.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            if (_data.TryGetValue(key, out var hashMap) &&
                hashMap.TryGetValue(hash, out var entry))
            {
                results.Add((key, entry));
            }
        }

        return results;
    }

    /// <summary>Check if a console key exists in the index.</summary>
    public bool HasConsole(string consoleKey) => _data.ContainsKey(consoleKey);

    /// <summary>Get all hash→gameName pairs for a console.</summary>
    public IReadOnlyDictionary<string, string>? GetConsoleEntries(string consoleKey)
    {
        if (!_data.TryGetValue(consoleKey, out var hashMap))
            return null;

        return hashMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GameName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Get all indexed console keys (snapshot).</summary>
    public IReadOnlyCollection<string> ConsoleKeys => _data.Keys.ToArray();
}
