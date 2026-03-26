using System.Buffers.Binary;
using System.Security.Cryptography;
using RomCleanup.Core.Caching;

namespace RomCleanup.Infrastructure.Hashing;

/// <summary>
/// Cached file hashing service. Port of Get-FileHashCached from Dat.ps1.
/// Uses LruCache for O(1) lookups, supports SHA1/SHA256/MD5/CRC32.
/// Thread-safe — multiple callers can hash concurrently.
/// Cache key is hashType|path (no timestamp) to avoid per-call stat syscalls.
/// Create a new instance per run to ensure cache freshness.
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
        var fullPath = Path.GetFullPath(path);
        var cacheKey = $"{NormalizeHashType(hashType)}|{fullPath}";

        if (_cache.TryGet(cacheKey, out var cached))
            return cached;

        try
        {
            var hash = ComputeHash(fullPath, hashType);
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

        // CHD v5 stores the SHA1 of the uncompressed raw content in the header.
        // Using that value keeps DAT matching deterministic for CHD vs ISO variants.
        if (type == "SHA1" && TryReadChdRawSha1(path, out var chdRawSha1))
            return chdRawSha1;

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

    private static bool TryReadChdRawSha1(string path, out string? sha1)
    {
        sha1 = null;

        if (!path.EndsWith(".chd", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 0x54)
                return false;

            Span<byte> header = stackalloc byte[0x80];
            var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            if (read < 0x54)
                return false;

            if (!header[..8].SequenceEqual("MComprHD"u8))
                return false;

            var version = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(12, 4));
            // v5: raw sha1 at 0x40.
            if (version == 5)
            {
                if (TryReadSha1AtOffset(header, 0x40, out sha1))
                    return true;
                return false;
            }

            // v3/v4: many dumps store raw sha1 around 0x50 and parent sha1 at 0x64.
            // We prefer 0x50 and fall back to 0x64 if needed.
            if (version is 3 or 4)
            {
                if (TryReadSha1AtOffset(header, 0x50, out sha1))
                    return true;
                if (TryReadSha1AtOffset(header, 0x64, out sha1))
                    return true;
                return false;
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryReadSha1AtOffset(ReadOnlySpan<byte> data, int offset, out string? sha1)
    {
        sha1 = null;
        if (offset < 0 || data.Length < offset + 20)
            return false;

        var shaSpan = data.Slice(offset, 20);
        for (var i = 0; i < shaSpan.Length; i++)
        {
            if (shaSpan[i] != 0)
            {
                sha1 = Convert.ToHexString(shaSpan).ToLowerInvariant();
                return true;
            }
        }

        return false;
    }

    /// <summary>Normalize hash type aliases to canonical form for consistent cache keys.</summary>
    private static string NormalizeHashType(string hashType)
    {
        var upper = hashType.ToUpperInvariant();
        return upper switch
        {
            "CRC" => "CRC32",
            _ => upper
        };
    }
}
