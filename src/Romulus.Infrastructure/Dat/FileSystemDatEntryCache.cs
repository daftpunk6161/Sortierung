using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Romulus.Infrastructure.Dat;

/// <summary>
/// Disk-backed implementation of <see cref="IDatEntryCache"/>. Each DAT entry is
/// persisted as a single JSON file under the cache root (default:
/// <c>%APPDATA%\Romulus\dat-cache</c>). The cache key folds the canonical absolute
/// path and the requested hash type into a SHA-256 hex digest to derive the
/// filename. Validity is determined by comparing the stored size + mtime + hash
/// type + parser version against the current source file; any mismatch yields a
/// cache miss so the adapter re-parses and overwrites the entry.
/// </summary>
public sealed class FileSystemDatEntryCache : IDatEntryCache
{
    /// <summary>
    /// Bumped whenever the parser output shape changes. Stored entries with a
    /// different version are treated as stale, forcing a re-parse.
    /// </summary>
    private const int ParserVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// In-memory hot cache so the second consumer of the same DAT in one process
    /// (e.g. main run after prewarm) skips the disk roundtrip entirely.
    /// </summary>
    private readonly ConcurrentDictionary<string, CachedDatPayload> _hotCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _cacheDir;
    private readonly Action<string>? _log;

    public FileSystemDatEntryCache(string cacheDir, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(cacheDir))
            throw new ArgumentException("Cache directory must be specified.", nameof(cacheDir));
        _cacheDir = cacheDir;
        _log = log;
        try { Directory.CreateDirectory(_cacheDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"[DAT-Cache] Konnte Cache-Verzeichnis nicht anlegen: {ex.Message}");
        }
    }

    public bool TryGet(string datPath, string hashType, out CachedDatPayload payload)
    {
        payload = null!;
        if (string.IsNullOrWhiteSpace(datPath) || !File.Exists(datPath))
            return false;

        FileInfo fi;
        try
        {
            fi = new FileInfo(datPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var hotKey = BuildHotKey(datPath, hashType, fi.Length, fi.LastWriteTimeUtc.Ticks);
        if (_hotCache.TryGetValue(hotKey, out var hot))
        {
            payload = hot;
            return true;
        }

        var cacheFile = ResolveCachePath(datPath, hashType);
        if (!File.Exists(cacheFile))
            return false;

        try
        {
            using var stream = File.OpenRead(cacheFile);
            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(stream, SerializerOptions);
            if (envelope is null)
                return false;
            if (envelope.Version != ParserVersion) return false;
            if (envelope.Size != fi.Length) return false;
            if (envelope.MtimeUtcTicks != fi.LastWriteTimeUtc.Ticks) return false;
            if (!string.Equals(envelope.HashType, hashType, StringComparison.OrdinalIgnoreCase)) return false;

            payload = new CachedDatPayload
            {
                ParentMap = envelope.ParentMap is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(envelope.ParentMap, StringComparer.OrdinalIgnoreCase),
                Games = MaterializeGames(envelope.Games)
            };
            _hotCache[hotKey] = payload;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _log?.Invoke($"[DAT-Cache] Cache-Hit verworfen ({Path.GetFileName(datPath)}): {ex.Message}");
            return false;
        }
    }

    public void Set(string datPath, string hashType, CachedDatPayload payload)
    {
        if (string.IsNullOrWhiteSpace(datPath) || payload is null || !File.Exists(datPath))
            return;

        FileInfo fi;
        try
        {
            fi = new FileInfo(datPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var envelope = new CacheEnvelope
        {
            Version = ParserVersion,
            DatPath = datPath,
            HashType = hashType,
            Size = fi.Length,
            MtimeUtcTicks = fi.LastWriteTimeUtc.Ticks,
            ParentMap = payload.ParentMap,
            Games = payload.Games
        };

        var cacheFile = ResolveCachePath(datPath, hashType);
        var tempFile = cacheFile + ".tmp";
        try
        {
            using (var stream = File.Create(tempFile))
            {
                JsonSerializer.Serialize(stream, envelope, SerializerOptions);
            }
            // Atomic replace so partial writes never leave a half-written cache file.
            if (File.Exists(cacheFile))
                File.Delete(cacheFile);
            File.Move(tempFile, cacheFile);

            var hotKey = BuildHotKey(datPath, hashType, fi.Length, fi.LastWriteTimeUtc.Ticks);
            _hotCache[hotKey] = payload;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"[DAT-Cache] Cache-Schreiben fehlgeschlagen ({Path.GetFileName(datPath)}): {ex.Message}");
            try { if (File.Exists(tempFile)) File.Delete(tempFile); }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException) { /* best effort */ }
        }
    }

    private string ResolveCachePath(string datPath, string hashType)
    {
        var canonical = TryGetCanonicalPath(datPath);
        var keyText = canonical + "\u001F" + (hashType ?? string.Empty).Trim().ToUpperInvariant();
        Span<byte> hashBuffer = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(keyText), hashBuffer);
        var hex = Convert.ToHexString(hashBuffer);
        return Path.Combine(_cacheDir, hex + ".dcache.json");
    }

    private static string TryGetCanonicalPath(string datPath)
    {
        try
        {
            return Path.GetFullPath(datPath);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return datPath;
        }
    }

    private static string BuildHotKey(string datPath, string hashType, long size, long mtimeTicks)
        => $"{TryGetCanonicalPath(datPath).ToLowerInvariant()}|{(hashType ?? string.Empty).Trim().ToUpperInvariant()}|{size}|{mtimeTicks}";

    private static Dictionary<string, List<Dictionary<string, string>>> MaterializeGames(
        Dictionary<string, List<Dictionary<string, string>>>? source)
    {
        var result = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
            return result;
        foreach (var (game, roms) in source)
        {
            var list = new List<Dictionary<string, string>>(roms.Count);
            foreach (var rom in roms)
                list.Add(new Dictionary<string, string>(rom, StringComparer.OrdinalIgnoreCase));
            result[game] = list;
        }
        return result;
    }

    private sealed class CacheEnvelope
    {
        [JsonPropertyName("v")] public int Version { get; set; }
        [JsonPropertyName("path")] public string DatPath { get; set; } = "";
        [JsonPropertyName("ht")] public string HashType { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("mtime")] public long MtimeUtcTicks { get; set; }
        [JsonPropertyName("parents")] public Dictionary<string, string>? ParentMap { get; set; }
        [JsonPropertyName("games")] public Dictionary<string, List<Dictionary<string, string>>>? Games { get; set; }
    }
}
