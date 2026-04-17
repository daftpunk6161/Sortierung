using System.Collections.Concurrent;
using System.Diagnostics;

namespace Romulus.Contracts.Models;

/// <summary>
/// Typed DAT index: maps ConsoleKey → (Hash → GameName).
/// Replaces the untyped IDictionary&lt;string, object&gt; from the initial port.
/// Mirrors the PowerShell structure: $index[$consoleKey][$hash] = $gameName.
/// Thread-safe: uses ConcurrentDictionary for concurrent access during parallel DAT parsing.
/// </summary>
public sealed class DatIndex
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DatIndexEntry>> _data = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DatIndexEntry>> _aliasData = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DatIndexEntry>> _nameIndex = new(StringComparer.OrdinalIgnoreCase);
    private int _totalEntries;
    private int _droppedByCapacityLimit;

    public readonly record struct DatIndexEntry(string GameName, string? RomFileName, bool IsBios = false, string? ParentGameName = null)
    {
        public bool IsClone => !string.IsNullOrWhiteSpace(ParentGameName);
    }

    /// <summary>Maximum entries per console to prevent OOM from malicious DATs. 0 = unlimited.</summary>
    public int MaxEntriesPerConsole { get; init; }

    /// <summary>Number of consoles indexed.</summary>
    public int ConsoleCount => _data.Count;

    /// <summary>Total number of hash entries across all consoles.</summary>
    public int TotalEntries => Volatile.Read(ref _totalEntries);

    /// <summary>Number of new entries rejected because a console-specific capacity limit was reached.</summary>
    public int DroppedByCapacityLimit => Volatile.Read(ref _droppedByCapacityLimit);

    /// <summary>Add or update a hash→gameName mapping for a console.</summary>
    public void Add(string consoleKey, string hash, string gameName, string? romFileName = null, bool isBios = false, string? parentGameName = null)
    {
        var hashMap = _data.GetOrAdd(consoleKey, _ => new ConcurrentDictionary<string, DatIndexEntry>(StringComparer.OrdinalIgnoreCase));
        var newEntry = new DatIndexEntry(gameName, romFileName, isBios, parentGameName);
        var nameMap = _nameIndex.GetOrAdd(consoleKey, _ => new ConcurrentDictionary<string, DatIndexEntry>(StringComparer.OrdinalIgnoreCase));

        // Allow updates for existing keys even when at capacity
        if (hashMap.TryGetValue(hash, out var oldEntry))
        {
            hashMap[hash] = newEntry;
            if (!string.Equals(oldEntry.GameName, gameName, StringComparison.OrdinalIgnoreCase))
                nameMap.TryRemove(oldEntry.GameName, out _);
            // Keep name index in sync on update
            nameMap[gameName] = newEntry;
            return;
        }
        if (MaxEntriesPerConsole > 0 && hashMap.Count >= MaxEntriesPerConsole)
        {
            var dropped = Interlocked.Increment(ref _droppedByCapacityLimit);
            if (dropped == 1 || dropped % 100 == 0)
            {
                Trace.TraceWarning(
                    "[DatIndex] Capacity limit reached for console '{0}'. Limit={1}, Dropped={2}.",
                    consoleKey,
                    MaxEntriesPerConsole,
                    dropped);
            }

            return;
        }
        if (hashMap.TryAdd(hash, newEntry))
            Interlocked.Increment(ref _totalEntries);

        // Also index by game name (first entry per game wins — sufficient for name-based lookup)
        nameMap.TryAdd(gameName, newEntry);
    }

    /// <summary>
    /// Add or update a primary hash mapping plus optional alias hashes for fallback lookup.
    /// Alias hashes do not increase <see cref="TotalEntries"/> and exist only for lookup compatibility
    /// (e.g. SHA1 primary with CRC32/MD5 aliases from the same DAT row).
    /// </summary>
    public void AddWithAliases(
        string consoleKey,
        string primaryHash,
        IEnumerable<string>? aliasHashes,
        string gameName,
        string? romFileName = null,
        bool isBios = false,
        string? parentGameName = null)
    {
        Add(consoleKey, primaryHash, gameName, romFileName, isBios, parentGameName);

        if (aliasHashes is null)
            return;

        var aliasMap = _aliasData.GetOrAdd(consoleKey,
            _ => new ConcurrentDictionary<string, DatIndexEntry>(StringComparer.OrdinalIgnoreCase));
        var entry = new DatIndexEntry(gameName, romFileName, isBios, parentGameName);

        foreach (var alias in aliasHashes)
        {
            if (string.IsNullOrWhiteSpace(alias))
                continue;

            var normalizedAlias = alias.Trim();
            if (normalizedAlias.Length == 0)
                continue;

            if (string.Equals(normalizedAlias, primaryHash, StringComparison.OrdinalIgnoreCase))
                continue;

            aliasMap[normalizedAlias] = entry;
        }
    }

    /// <summary>Look up a game name by console key and hash.</summary>
    public string? Lookup(string consoleKey, string hash)
    {
        var entry = LookupEntry(consoleKey, hash);
        if (entry is not null)
            return entry.Value.GameName;
        return null;
    }

    /// <summary>Look up game name plus optional DAT ROM filename by console key and hash.</summary>
    public DatIndexEntry? LookupWithFilename(string consoleKey, string hash)
    {
        var entry = LookupEntry(consoleKey, hash);
        if (entry is not null)
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
        var allKeys = _data.Keys
            .Concat(_aliasData.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

        foreach (var key in allKeys)
        {
            var entry = LookupEntry(key, hash);
            if (entry is not null)
                return (key, entry.Value.GameName);
        }
        return null;
    }

    /// <summary>
    /// Looks up all console matches for a hash in deterministic console-key order.
    /// </summary>
    public IReadOnlyList<(string ConsoleKey, DatIndexEntry Entry)> LookupAllByHash(string hash)
    {
        var results = new List<(string ConsoleKey, DatIndexEntry Entry)>();
        var allKeys = _data.Keys
            .Concat(_aliasData.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

        foreach (var key in allKeys)
        {
            var entry = LookupEntry(key, hash);
            if (entry is not null)
            {
                results.Add((key, entry.Value));
            }
        }

        return results;
    }

    /// <summary>Check if a console key exists in the index.</summary>
    public bool HasConsole(string consoleKey) => _data.ContainsKey(consoleKey);

    /// <summary>Look up by game name for a specific console (name-based fallback for CHD/disc files).</summary>
    public DatIndexEntry? LookupByName(string consoleKey, string gameName)
    {
        if (_nameIndex.TryGetValue(consoleKey, out var nameMap) &&
            nameMap.TryGetValue(gameName, out var entry))
            return entry;
        return null;
    }

    /// <summary>
    /// Look up all console matches for a game name in deterministic order.
    /// Used as fallback when hash matching fails (e.g. CHD raw SHA1 ≠ per-track SHA1).
    /// </summary>
    public IReadOnlyList<(string ConsoleKey, DatIndexEntry Entry)> LookupAllByName(string gameName)
    {
        var results = new List<(string ConsoleKey, DatIndexEntry Entry)>();
        foreach (var key in _nameIndex.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            if (_nameIndex.TryGetValue(key, out var nameMap) &&
                nameMap.TryGetValue(gameName, out var entry))
            {
                results.Add((key, entry));
            }
        }
        return results;
    }

    /// <summary>Get all hash→gameName pairs for a console.</summary>
    public IReadOnlyDictionary<string, string>? GetConsoleEntries(string consoleKey)
    {
        if (!_data.TryGetValue(consoleKey, out var hashMap))
            return null;

        return hashMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GameName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get all hash entries with metadata for a console in deterministic hash order.
    /// </summary>
    public IReadOnlyList<(string Hash, DatIndexEntry Entry)> GetConsoleEntriesDetailed(string consoleKey)
    {
        if (!_data.TryGetValue(consoleKey, out var hashMap))
            return [];

        return hashMap
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToArray();
    }

    /// <summary>Merge all entries from another DatIndex into this one (supplemental DATs).</summary>
    public void MergeFrom(DatIndex other)
    {
        foreach (var consoleKey in other._data.Keys)
        {
            if (!other._data.TryGetValue(consoleKey, out var otherHashMap))
                continue;

            foreach (var kvp in otherHashMap)
            {
                Add(consoleKey, kvp.Key, kvp.Value.GameName, kvp.Value.RomFileName,
                    kvp.Value.IsBios, kvp.Value.ParentGameName);
            }

            if (!other._aliasData.TryGetValue(consoleKey, out var otherAliasMap))
                continue;

            var aliasMap = _aliasData.GetOrAdd(consoleKey,
                _ => new ConcurrentDictionary<string, DatIndexEntry>(StringComparer.OrdinalIgnoreCase));
            foreach (var alias in otherAliasMap)
                aliasMap[alias.Key] = alias.Value;
        }
    }

    private DatIndexEntry? LookupEntry(string consoleKey, string hash)
    {
        if (_data.TryGetValue(consoleKey, out var hashMap) &&
            hashMap.TryGetValue(hash, out var primaryEntry))
            return primaryEntry;

        if (_aliasData.TryGetValue(consoleKey, out var aliasMap) &&
            aliasMap.TryGetValue(hash, out var aliasEntry))
            return aliasEntry;

        return null;
    }

    /// <summary>Get all indexed console keys (snapshot).</summary>
    public IReadOnlyCollection<string> ConsoleKeys => _data.Keys.ToArray();
}
