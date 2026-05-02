using System.Collections.Concurrent;
using System.Diagnostics;
using Romulus.Contracts.Hashing;

namespace Romulus.Contracts.Models;

/// <summary>
/// Typed DAT index: maps ConsoleKey → (Hash → GameName).
/// Replaces the untyped IDictionary&lt;string, object&gt; from the initial port.
/// Mirrors the PowerShell structure: $index[$consoleKey][$hash] = $gameName.
/// Thread-safe: uses ConcurrentDictionary for concurrent access during parallel DAT parsing.
/// </summary>
public sealed class DatIndex
{
    private const string DefaultHashType = "SHA1";
    private const char HashKeySeparator = '\u001F';

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DatIndexEntry>> _data = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DatIndexEntry>> _aliasData = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DatIndexEntry>> _nameIndex = new(StringComparer.OrdinalIgnoreCase);
    private int _totalEntries;
    private int _droppedByCapacityLimit;

    public readonly record struct DatIndexEntry(
        string GameName,
        string? RomFileName,
        bool IsBios = false,
        string? ParentGameName = null,
        string HashType = DefaultHashType,
        string? SourceId = null)
    {
        public bool IsClone => !string.IsNullOrWhiteSpace(ParentGameName);
    }

    /// <summary>Maximum entries per console to prevent OOM from malicious DATs.</summary>
    public int MaxEntriesPerConsole { get; init; } = 500_000;

    /// <summary>Number of consoles indexed.</summary>
    public int ConsoleCount => _data.Count;

    /// <summary>Total number of hash entries across all consoles.</summary>
    public int TotalEntries => Volatile.Read(ref _totalEntries);

    /// <summary>Number of new entries rejected because a console-specific capacity limit was reached.</summary>
    public int DroppedByCapacityLimit => Volatile.Read(ref _droppedByCapacityLimit);

    /// <summary>Add or update a hash→gameName mapping for a console.</summary>
    public void Add(
        string consoleKey,
        string hash,
        string gameName,
        string? romFileName = null,
        bool isBios = false,
        string? parentGameName = null,
        string hashType = DefaultHashType,
        string? sourceId = null)
    {
        var hashMap = _data.GetOrAdd(consoleKey, _ => new ConcurrentDictionary<string, DatIndexEntry>(StringComparer.OrdinalIgnoreCase));
        var normalizedHashType = NormalizeHashType(hashType);
        var typedHashKey = BuildTypedHashKey(normalizedHashType, hash);
        var newEntry = new DatIndexEntry(gameName, romFileName, isBios, parentGameName, normalizedHashType, sourceId);
        var nameMap = _nameIndex.GetOrAdd(consoleKey, _ => new ConcurrentDictionary<string, DatIndexEntry>(StringComparer.OrdinalIgnoreCase));

        // Allow updates for existing keys even when at capacity
        if (hashMap.TryGetValue(typedHashKey, out var oldEntry))
        {
            hashMap[typedHashKey] = newEntry;
            if (!string.Equals(oldEntry.GameName, gameName, StringComparison.OrdinalIgnoreCase))
                nameMap.TryRemove(oldEntry.GameName, out _);
            // Keep name index in sync on update
            nameMap[gameName] = newEntry;
            return;
        }
        if (hashMap.Count >= MaxEntriesPerConsole)
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
        if (hashMap.TryAdd(typedHashKey, newEntry))
            Interlocked.Increment(ref _totalEntries);

        // F-DAT-05 (regression-pinned): name index uses deterministic first-wins.
        // A single DAT game frequently contains multiple ROM tracks/files (cue/bin, multi-track CHD)
        // each with its own hash. Every distinct hash is added to hashMap above; the name index
        // intentionally keeps only the first observation per gameName so name-based lookup stays
        // O(1) and deterministic. See DatIndexInsertSymmetryTests for the contract.
        nameMap.TryAdd(gameName, newEntry);
    }

    /// <summary>
    /// Add or update a primary hash mapping plus optional alias hashes for fallback lookup.
    /// Alias hashes do not increase <see cref="TotalEntries"/> and exist only for lookup compatibility
    /// (e.g. SHA1 primary with CRC32/MD5 aliases from the same DAT row).
    /// </summary>
    public void AddWithAliases(
        string consoleKey,
        string primaryHashType,
        string primaryHash,
        IEnumerable<(string HashType, string Hash)>? aliasHashes,
        string gameName,
        string? romFileName = null,
        bool isBios = false,
        string? parentGameName = null,
        string? sourceId = null)
    {
        var normalizedPrimaryHashType = NormalizeHashType(primaryHashType);
        Add(consoleKey, primaryHash, gameName, romFileName, isBios, parentGameName, normalizedPrimaryHashType, sourceId);

        if (aliasHashes is null)
            return;

        var aliasMap = _aliasData.GetOrAdd(consoleKey,
            _ => new ConcurrentDictionary<string, DatIndexEntry>(StringComparer.OrdinalIgnoreCase));
        var primaryKey = BuildTypedHashKey(normalizedPrimaryHashType, primaryHash);

        foreach (var (hashType, hash) in aliasHashes)
        {
            if (string.IsNullOrWhiteSpace(hash))
                continue;

            var normalizedHashType = NormalizeHashType(hashType);
            var normalizedAlias = hash.Trim();
            if (normalizedAlias.Length == 0)
                continue;

            var aliasKey = BuildTypedHashKey(normalizedHashType, normalizedAlias);
            if (string.Equals(aliasKey, primaryKey, StringComparison.OrdinalIgnoreCase))
                continue;

            aliasMap[aliasKey] = new DatIndexEntry(gameName, romFileName, isBios, parentGameName, normalizedHashType, sourceId);
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

    /// <summary>Look up a game name by console key, hash type and hash.</summary>
    public string? Lookup(string consoleKey, string hashType, string hash)
    {
        var entry = LookupEntry(consoleKey, hashType, hash);
        return entry?.GameName;
    }

    /// <summary>Look up game name plus optional DAT ROM filename by console key and hash.</summary>
    public DatIndexEntry? LookupWithFilename(string consoleKey, string hash)
    {
        var entry = LookupEntry(consoleKey, hash);
        if (entry is not null)
            return entry;
        return null;
    }

    /// <summary>Look up game name plus optional DAT ROM filename by console key, hash type and hash.</summary>
    public DatIndexEntry? LookupWithFilename(string consoleKey, string hashType, string hash)
        => LookupEntry(consoleKey, hashType, hash);

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

    /// <summary>Look up a typed hash across all loaded consoles.</summary>
    public (string ConsoleKey, string GameName)? LookupAny(string hashType, string hash)
    {
        foreach (var key in EnumerateAllConsoleKeys())
        {
            var entry = LookupEntry(key, hashType, hash);
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

        foreach (var key in EnumerateAllConsoleKeys())
        {
            var entry = LookupEntry(key, hash);
            if (entry is not null)
            {
                results.Add((key, entry.Value));
            }
        }

        return results;
    }

    /// <summary>Looks up all console matches for a typed hash in deterministic console-key order.</summary>
    public IReadOnlyList<(string ConsoleKey, DatIndexEntry Entry)> LookupAllByHash(string hashType, string hash)
    {
        var results = new List<(string ConsoleKey, DatIndexEntry Entry)>();

        foreach (var key in EnumerateAllConsoleKeys())
        {
            var entry = LookupEntry(key, hashType, hash);
            if (entry is not null)
                results.Add((key, entry.Value));
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

        return hashMap
            .OrderBy(kvp => kvp.Value.HashType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .GroupBy(kvp => ExtractRawHash(kvp.Key), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value.GameName, StringComparer.OrdinalIgnoreCase);
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
            .Select(kvp => (ExtractRawHash(kvp.Key), kvp.Value))
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
                Add(consoleKey, ExtractRawHash(kvp.Key), kvp.Value.GameName, kvp.Value.RomFileName,
                    kvp.Value.IsBios, kvp.Value.ParentGameName, kvp.Value.HashType, kvp.Value.SourceId);
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
        if (_data.TryGetValue(consoleKey, out var hashMap))
        {
            var match = LookupUntyped(hashMap, hash);
            if (match is not null)
                return match;
        }

        if (_aliasData.TryGetValue(consoleKey, out var aliasMap))
            return LookupUntyped(aliasMap, hash);

        return null;
    }

    private DatIndexEntry? LookupEntry(string consoleKey, string hashType, string hash)
    {
        var typedHashKey = BuildTypedHashKey(hashType, hash);

        if (_data.TryGetValue(consoleKey, out var hashMap) &&
            hashMap.TryGetValue(typedHashKey, out var primaryEntry))
            return primaryEntry;

        if (_aliasData.TryGetValue(consoleKey, out var aliasMap) &&
            aliasMap.TryGetValue(typedHashKey, out var aliasEntry))
            return aliasEntry;

        return null;
    }

    private static DatIndexEntry? LookupUntyped(
        ConcurrentDictionary<string, DatIndexEntry> map,
        string hash)
    {
        if (map.TryGetValue(hash, out var legacyEntry))
            return legacyEntry;

        return map
            .Where(kvp => string.Equals(ExtractRawHash(kvp.Key), hash, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kvp => kvp.Value.HashType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => (DatIndexEntry?)kvp.Value)
            .FirstOrDefault();
    }

    private IEnumerable<string> EnumerateAllConsoleKeys()
        => _data.Keys
            .Concat(_aliasData.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

    private static string BuildTypedHashKey(string hashType, string hash)
        => $"{NormalizeHashType(hashType)}{HashKeySeparator}{hash.Trim()}";

    private static string ExtractRawHash(string typedHashKey)
    {
        var separatorIndex = typedHashKey.IndexOf(HashKeySeparator);
        return separatorIndex < 0 ? typedHashKey : typedHashKey[(separatorIndex + 1)..];
    }

    private static string NormalizeHashType(string? hashType)
        => HashTypeNormalizer.Normalize(hashType);

    /// <summary>Get all indexed console keys (snapshot).</summary>
    public IReadOnlyCollection<string> ConsoleKeys => _data.Keys.ToArray();
}
