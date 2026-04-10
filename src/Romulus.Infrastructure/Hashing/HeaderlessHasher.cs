using System.Buffers.Binary;
using System.Security.Cryptography;
using Romulus.Contracts.Ports;
using Romulus.Core.Caching;
using Romulus.Core.Classification;

namespace Romulus.Infrastructure.Hashing;

/// <summary>
/// Computes DAT-compatible normalized hashes for ROM files with known normalization rules.
/// This includes classic header skipping (iNES, SNES copier, Atari 7800/Lynx)
/// and canonical byte-order normalization for N64 variants.
/// Thread-safe, cached via LruCache.
/// </summary>
public sealed class HeaderlessHasher : IHeaderlessHasher
{
    private readonly LruCache<string, string?> _cache;

    private static ReadOnlySpan<byte> N64MagicBE => [0x80, 0x37, 0x12, 0x40];
    private static ReadOnlySpan<byte> N64MagicBS => [0x37, 0x80, 0x40, 0x12];
    private static ReadOnlySpan<byte> N64MagicLE => [0x40, 0x12, 0x37, 0x80];

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

        if (!HasNormalizationStrategy(consoleKey))
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
        if (consoleKey.Equals("N64", StringComparison.OrdinalIgnoreCase))
            return ComputeN64CanonicalHash(fullPath, hashType);

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

    private static string? ComputeN64CanonicalHash(string fullPath, string hashType)
    {
        var bytes = File.ReadAllBytes(fullPath);
        if (bytes.Length < 4)
            return null;

        var normalized = bytes.ToArray();
        var header = normalized.AsSpan(0, 4);

        if (header.SequenceEqual(N64MagicBS))
            NormalizeN64ByteSwapped(normalized);
        else if (header.SequenceEqual(N64MagicLE))
            NormalizeN64LittleEndian(normalized);
        else if (!header.SequenceEqual(N64MagicBE))
            return null;

        using var algo = CreateHashAlgorithm(hashType);
        return Convert.ToHexStringLower(algo.ComputeHash(normalized));
    }

    private static void NormalizeN64ByteSwapped(byte[] data)
    {
        for (var i = 0; i + 1 < data.Length; i += 2)
            (data[i], data[i + 1]) = (data[i + 1], data[i]);
    }

    private static void NormalizeN64LittleEndian(byte[] data)
    {
        for (var i = 0; i + 3 < data.Length; i += 4)
        {
            var b0 = data[i];
            var b1 = data[i + 1];
            data[i] = data[i + 3];
            data[i + 1] = data[i + 2];
            data[i + 2] = b1;
            data[i + 3] = b0;
        }
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

    private static bool HasNormalizationStrategy(string consoleKey)
        => HeaderSizeMap.HasMapping(consoleKey)
           || consoleKey.Equals("N64", StringComparison.OrdinalIgnoreCase);
}
