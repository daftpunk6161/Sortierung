using System.Security.Cryptography;
using RomCleanup.Core.Caching;

namespace RomCleanup.Infrastructure.Hashing;

/// <summary>
/// Cached file hashing service. Port of Get-FileHashCached from Dat.ps1.
/// Uses LruCache for O(1) lookups, supports SHA1/SHA256/MD5/CRC32.
/// Thread-safe — multiple callers can hash concurrently.
/// </summary>
public sealed class FileHashService
{
    private readonly LruCache<string, string> _cache;

    public FileHashService(int maxEntries = 20_000)
    {
        _cache = new LruCache<string, string>(maxEntries, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Current number of cached entries.</summary>
    public int CacheCount => _cache.Count;

    /// <summary>
    /// Get or compute the hash for a file. Results are cached by (hashType|path).
    /// Returns null if the file cannot be read.
    /// </summary>
    public string? GetHash(string path, string hashType = "SHA1")
    {
        var cacheKey = $"{hashType}|{path}";

        if (_cache.TryGet(cacheKey, out var cached))
            return cached;

        try
        {
            var hash = ComputeHash(path, hashType);
            if (hash is not null)
                _cache.Set(cacheKey, hash);
            return hash;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Clear the entire hash cache.</summary>
    public void ClearCache() => _cache.Clear();

    /// <summary>Adjust maximum cache size at runtime (mirrors PS AppState config).</summary>
    public int MaxEntries
    {
        get => _cache.MaxEntries;
        set => _cache.MaxEntries = Math.Max(500, value);
    }

    private static string? ComputeHash(string path, string hashType)
    {
        if (!File.Exists(path))
            return null;

        var type = hashType.ToUpperInvariant();

        if (type is "CRC" or "CRC32")
            return Crc32.HashFile(path);

        using var stream = File.OpenRead(path);
        using var algo = type switch
        {
            "SHA256" => (HashAlgorithm)SHA256.Create(),
            "MD5" => MD5.Create(),
            _ => SHA1.Create() // SHA1 default
        };

        var bytes = algo.ComputeHash(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
