namespace RomCleanup.Contracts.Models;

/// <summary>
/// Typed DAT index: maps ConsoleKey → (Hash → GameName).
/// Replaces the untyped IDictionary&lt;string, object&gt; from the initial port.
/// Mirrors the PowerShell structure: $index[$consoleKey][$hash] = $gameName.
/// </summary>
public sealed class DatIndex
{
    private readonly Dictionary<string, Dictionary<string, string>> _data = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of consoles indexed.</summary>
    public int ConsoleCount => _data.Count;

    /// <summary>Total number of hash entries across all consoles.</summary>
    public int TotalEntries
    {
        get
        {
            int count = 0;
            foreach (var console in _data.Values)
                count += console.Count;
            return count;
        }
    }

    /// <summary>Add or update a hash→gameName mapping for a console.</summary>
    public void Add(string consoleKey, string hash, string gameName)
    {
        if (!_data.TryGetValue(consoleKey, out var hashMap))
        {
            hashMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _data[consoleKey] = hashMap;
        }
        hashMap[hash.ToLowerInvariant()] = gameName;
    }

    /// <summary>Look up a game name by console key and hash.</summary>
    public string? Lookup(string consoleKey, string hash)
    {
        if (_data.TryGetValue(consoleKey, out var hashMap) &&
            hashMap.TryGetValue(hash.ToLowerInvariant(), out var name))
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

    /// <summary>Get all indexed console keys.</summary>
    public IEnumerable<string> ConsoleKeys => _data.Keys;
}
