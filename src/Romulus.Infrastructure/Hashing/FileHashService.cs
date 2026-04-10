using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Caching;
using Romulus.Infrastructure.Paths;

namespace Romulus.Infrastructure.Hashing;

/// <summary>
/// Cached file hashing service. Port of Get-FileHashCached from Dat.ps1.
/// Uses an in-memory LRU cache for hot lookups and can optionally persist validated
/// file hashes across runs using (hashType, path, lastWriteUtc, length) metadata.
/// </summary>
public sealed class FileHashService : IDisposable
{
    private readonly LruCache<string, string> _cache;
    private readonly string? _persistentCachePath;
    private readonly ICollectionIndex? _collectionIndex;
    private readonly IDisposable? _ownedCollectionIndex;
    private readonly Dictionary<string, PersistentCacheEntry> _persistentEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _persistentGate = new();
    private bool _persistentDirty;
    private bool _disposed;

    private static readonly JsonSerializerOptions PersistentJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FileHashService(
        int maxEntries = 20_000,
        string? persistentCachePath = null,
        ICollectionIndex? collectionIndex = null,
        bool ownsCollectionIndex = false)
    {
        _cache = new LruCache<string, string>(maxEntries, StringComparer.OrdinalIgnoreCase);
        _persistentCachePath = string.IsNullOrWhiteSpace(persistentCachePath)
            ? null
            : Path.GetFullPath(persistentCachePath);
        _collectionIndex = collectionIndex;
        _ownedCollectionIndex = ownsCollectionIndex ? collectionIndex as IDisposable : null;

        if (_collectionIndex is null && _persistentCachePath is not null)
            LoadPersistentCache();
    }

    /// <summary>Current number of cached entries.</summary>
    public int CacheCount => _cache.Count;

    /// <summary>Shared collection index used for persistent hash and candidate reuse when available.</summary>
    public ICollectionIndex? CollectionIndex => _collectionIndex;

    /// <summary>Whether this instance persists hashes across runs.</summary>
    public bool IsPersistent => _collectionIndex is not null || _persistentCachePath is not null;

    /// <summary>
    /// Get or compute the hash for a file. Results are cached by (hashType|path).
    /// Returns null if the file cannot be read.
    /// </summary>
    public string? GetHash(string path, string hashType = "SHA1")
    {
        ThrowIfDisposed();

        var fullPath = NormalizePathForCacheKey(path);
        var normalizedHashType = NormalizeHashType(hashType);
        var cacheKey = $"{normalizedHashType}|{fullPath}";

        if (_cache.TryGet(cacheKey, out var cached))
            return cached;

        try
        {
            if (TryGetFileFingerprint(fullPath, out var fingerprint)
                && TryGetPersistedHash(cacheKey, fullPath, normalizedHashType, fingerprint, out var persistedHash))
            {
                _cache.Set(cacheKey, persistedHash);
                return persistedHash;
            }

            var hash = ComputeHash(fullPath, normalizedHashType);
            if (hash is not null)
            {
                _cache.Set(cacheKey, hash);
                PersistHashBestEffort(cacheKey, normalizedHashType, fullPath, fingerprint, hash);
            }
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

    /// <summary>Clear the entire hash cache, including any pending persistent entries.</summary>
    public void ClearCache()
    {
        ThrowIfDisposed();
        _cache.Clear();

        if (_collectionIndex is not null)
            return;

        if (_persistentCachePath is null)
            return;

        lock (_persistentGate)
        {
            _persistentEntries.Clear();
            _persistentDirty = true;
        }
    }

    /// <summary>Adjust maximum cache size at runtime (mirrors PS AppState config).</summary>
    public int MaxEntries
    {
        get => _cache.MaxEntries;
        set => _cache.MaxEntries = Math.Max(500, value);
    }

    /// <summary>Flush persistent cache entries to disk if persistence is enabled.</summary>
    public void FlushPersistentCache()
    {
        ThrowIfDisposed();

        if (_collectionIndex is not null)
            return;

        if (_persistentCachePath is null)
            return;

        lock (_persistentGate)
        {
            if (!_persistentDirty)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(_persistentCachePath)!);

            TrimPersistentEntriesToMaxEntries();
            var document = new PersistentCacheDocument
            {
                Entries = _persistentEntries.Values
                    .OrderByDescending(entry => entry.RecordedUtcTicks)
                    .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            var tempPath = _persistentCachePath + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(document, PersistentJsonOptions));
            File.Move(tempPath, _persistentCachePath, overwrite: true);
            _persistentDirty = false;
        }
    }

    public static string ResolveDefaultPersistentCachePath()
    {
        return AppStoragePathResolver.ResolveLocalPath("cache", "file-hashes-v1.json");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_collectionIndex is null)
        {
            try
            {
                FlushPersistentCache();
            }
            catch (Exception) when (_persistentCachePath is not null)
            {
            }
        }

        _ownedCollectionIndex?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
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

    private void LoadPersistentCache()
    {
        try
        {
            if (_persistentCachePath is null || !File.Exists(_persistentCachePath))
                return;

            var json = File.ReadAllText(_persistentCachePath);
            var document = JsonSerializer.Deserialize<PersistentCacheDocument>(json, PersistentJsonOptions);
            if (document?.Entries is null)
                return;

            foreach (var entry in document.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Path)
                    || string.IsNullOrWhiteSpace(entry.Hash)
                    || string.IsNullOrWhiteSpace(entry.HashType))
                {
                    continue;
                }

                _persistentEntries[BuildCacheKey(entry.HashType, entry.Path)] = entry with
                {
                    HashType = NormalizeHashType(entry.HashType),
                    Path = NormalizePathForCacheKey(entry.Path)
                };
            }

            TrimPersistentEntriesToMaxEntries();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }
    }

    private bool TryGetPersistedHash(
        string cacheKey,
        string fullPath,
        string normalizedHashType,
        FileFingerprint fingerprint,
        out string hash)
    {
        hash = string.Empty;
        if (_collectionIndex is not null)
            return TryGetIndexedHash(fullPath, normalizedHashType, fingerprint, out hash);

        if (_persistentCachePath is null)
            return false;

        lock (_persistentGate)
        {
            if (!_persistentEntries.TryGetValue(cacheKey, out var entry))
                return false;

            if (entry.LastWriteUtcTicks != fingerprint.LastWriteUtcTicks || entry.Length != fingerprint.Length)
                return false;

            hash = entry.Hash;
            return true;
        }
    }

    private bool TryGetIndexedHash(
        string fullPath,
        string normalizedHashType,
        FileFingerprint fingerprint,
        out string hash)
    {
        hash = string.Empty;
        if (_collectionIndex is null)
            return false;

        try
        {
            var entry = _collectionIndex.TryGetHashAsync(
                    fullPath,
                    normalizedHashType,
                    fingerprint.Length,
                    new DateTime(fingerprint.LastWriteUtcTicks, DateTimeKind.Utc))
                .GetAwaiter()
                .GetResult();

            if (entry is null || string.IsNullOrWhiteSpace(entry.Hash))
                return false;

            hash = entry.Hash;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private void PersistHashBestEffort(
        string cacheKey,
        string normalizedHashType,
        string fullPath,
        FileFingerprint fingerprint,
        string hash)
    {
        if (_collectionIndex is not null)
        {
            try
            {
                _collectionIndex.SetHashAsync(new CollectionHashCacheEntry
                {
                    Path = fullPath,
                    Algorithm = normalizedHashType,
                    SizeBytes = fingerprint.Length,
                    LastWriteUtc = new DateTime(fingerprint.LastWriteUtcTicks, DateTimeKind.Utc),
                    Hash = hash,
                    RecordedUtc = DateTime.UtcNow
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
            }

            return;
        }

        if (_persistentCachePath is null)
            return;

        lock (_persistentGate)
        {
            _persistentEntries[cacheKey] = new PersistentCacheEntry
            {
                HashType = normalizedHashType,
                Path = fullPath,
                LastWriteUtcTicks = fingerprint.LastWriteUtcTicks,
                Length = fingerprint.Length,
                Hash = hash,
                RecordedUtcTicks = DateTime.UtcNow.Ticks
            };
            _persistentDirty = true;
        }
    }

    private void TrimPersistentEntriesToMaxEntries()
    {
        var overflow = _persistentEntries.Count - MaxEntries;
        if (overflow <= 0)
            return;

        var keysToRemove = _persistentEntries.Values
            .OrderBy(entry => entry.RecordedUtcTicks)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Take(overflow)
            .Select(entry => BuildCacheKey(entry.HashType, entry.Path))
            .ToList();

        foreach (var key in keysToRemove)
            _persistentEntries.Remove(key);
    }

    private static bool TryGetFileFingerprint(string path, out FileFingerprint fingerprint)
    {
        fingerprint = default;
        if (!File.Exists(path))
            return false;

        var info = new FileInfo(path);
        fingerprint = new FileFingerprint(info.LastWriteTimeUtc.Ticks, info.Length);
        return true;
    }

    internal static string NormalizePathForCacheKey(string path)
        => Path.GetFullPath(path).Normalize(NormalizationForm.FormC);

    private static string BuildCacheKey(string hashType, string path)
        => $"{NormalizeHashType(hashType)}|{NormalizePathForCacheKey(path)}";

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private readonly record struct FileFingerprint(long LastWriteUtcTicks, long Length);

    private sealed record PersistentCacheDocument
    {
        public int Version { get; init; } = 1;
        public List<PersistentCacheEntry> Entries { get; init; } = [];
    }

    private sealed record PersistentCacheEntry
    {
        public string HashType { get; init; } = "";
        public string Path { get; init; } = "";
        public long LastWriteUtcTicks { get; init; }
        public long Length { get; init; }
        public string Hash { get; init; } = "";
        public long RecordedUtcTicks { get; init; }
    }
}
