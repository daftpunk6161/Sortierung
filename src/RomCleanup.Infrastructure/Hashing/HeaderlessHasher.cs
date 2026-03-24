using System.Buffers.Binary;
using System.Security.Cryptography;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Caching;
using RomCleanup.Core.Classification;

namespace RomCleanup.Infrastructure.Hashing;

/// <summary>
/// Computes headerless hashes for ROM files with known header formats.
/// Skips header bytes (iNES, SNES copier, Atari 7800/Lynx) so the hash matches No-Intro DAT entries.
/// Thread-safe, cached via LruCache.
/// </summary>
public sealed class HeaderlessHasher : IHeaderlessHasher
{
    private readonly LruCache<string, string?> _cache;

    public HeaderlessHasher(int cacheSize = 8192)
    {
        _cache = new LruCache<string, string?>(cacheSize, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Current number of cached entries.</summary>
    public int CacheCount => _cache.Count;

    public string? ComputeHeaderlessHash(string filePath, string consoleKey, string hashType = "SHA1")
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(consoleKey))
            return null;

        if (!HeaderSizeMap.HasMapping(consoleKey))
            return null;

        var fullPath = Path.GetFullPath(filePath);
        var cacheKey = $"{NormalizeHashType(hashType)}|HL|{consoleKey}|{fullPath}";

        if (_cache.TryGet(cacheKey, out var cached))
            return cached;

        try
        {
            var result = ComputeCore(fullPath, consoleKey, hashType);
            _cache.Set(cacheKey, result);
            return result;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string? ComputeCore(string fullPath, string consoleKey, string hashType)
    {
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileSize = fs.Length;

        // Read header bytes for magic detection
        Span<byte> headerBuf = stackalloc byte[512];
        var headerRead = fs.Read(headerBuf);
        var header = headerBuf[..headerRead];

        var skipBytes = HeaderSizeMap.GetSkipBytes(consoleKey, header, fileSize);
        if (skipBytes <= 0 || fileSize <= skipBytes)
            return null;

        // Seek to content start and hash the rest
        fs.Position = skipBytes;

        using var algo = CreateHashAlgorithm(hashType);
        var hashBytes = algo.ComputeHash(fs);
        return Convert.ToHexStringLower(hashBytes);
    }

    private static HashAlgorithm CreateHashAlgorithm(string hashType)
    {
        return NormalizeHashType(hashType) switch
        {
            "SHA1" => SHA1.Create(),
            "SHA256" => SHA256.Create(),
            "MD5" => MD5.Create(),
            _ => SHA1.Create()
        };
    }

    private static string NormalizeHashType(string hashType)
        => hashType.ToUpperInvariant().Replace("-", "");
}
