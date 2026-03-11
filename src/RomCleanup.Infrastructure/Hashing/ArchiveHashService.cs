using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Caching;

namespace RomCleanup.Infrastructure.Hashing;

/// <summary>
/// Extracts hashes from files inside ZIP and 7z archives.
/// Port of Get-ArchiveHashes, Get-HashesFromZip, Get-HashesFrom7z from Dat.ps1.
/// ZIP entries are hashed in-memory (no temp extraction).
/// 7z archives require extraction via IToolRunner.
/// </summary>
public sealed class ArchiveHashService
{
    private static readonly Regex RxPathEntry = new(@"^Path = (.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly LruCache<string, string[]> _cache;
    private readonly IToolRunner? _toolRunner;
    private readonly long _maxArchiveSizeBytes;

    /// <param name="toolRunner">Required for .7z archives (delegates to 7z.exe).</param>
    /// <param name="maxEntries">LRU cache max entries.</param>
    /// <param name="maxArchiveSizeBytes">Archives larger than this skip hashing (default 100 MB).</param>
    public ArchiveHashService(
        IToolRunner? toolRunner = null,
        int maxEntries = 5000,
        long maxArchiveSizeBytes = 100 * 1024 * 1024)
    {
        _toolRunner = toolRunner;
        _maxArchiveSizeBytes = maxArchiveSizeBytes;
        _cache = new LruCache<string, string[]>(maxEntries, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get hashes of all entries inside an archive file.
    /// Returns empty array on failure or if archive exceeds size limit.
    /// </summary>
    public string[] GetArchiveHashes(string archivePath, string hashType = "SHA1")
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            return Array.Empty<string>();

        var cacheKey = $"ARCHIVE|{hashType}|{archivePath}";
        if (_cache.TryGet(cacheKey, out var cached))
            return cached;

        // Size check
        try
        {
            var fi = new FileInfo(archivePath);
            if (fi.Length > _maxArchiveSizeBytes)
            {
                _cache.Set(cacheKey, Array.Empty<string>());
                return Array.Empty<string>();
            }
        }
        catch { return Array.Empty<string>(); }

        var ext = Path.GetExtension(archivePath).ToLowerInvariant();
        string[] hashes;
        try
        {
            hashes = ext == ".zip"
                ? HashZipEntries(archivePath, hashType)
                : ext == ".7z"
                    ? Hash7zEntries(archivePath, hashType)
                    : Array.Empty<string>();
        }
        catch (IOException) { hashes = Array.Empty<string>(); }
        catch (InvalidDataException) { hashes = Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { hashes = Array.Empty<string>(); }

        _cache.Set(cacheKey, hashes);
        return hashes;
    }

    /// <summary>Clear the archive hash cache.</summary>
    public void ClearCache() => _cache.Clear();

    /// <summary>Current number of cached archive hash entries.</summary>
    public int CacheCount => _cache.Count;

    // ── ZIP: in-memory stream hashing ──

    private static string[] HashZipEntries(string zipPath, string hashType)
    {
        var hashes = new List<string>();
        using var archive = ZipFile.OpenRead(zipPath);

        foreach (var entry in archive.Entries)
        {
            if (entry.Length <= 0) continue;
            try
            {
                using var stream = entry.Open();
                var h = HashStream(stream, hashType);
                if (h is not null)
                    hashes.Add(h);
            }
            catch { /* skip corrupt entries */ }
        }

        return hashes.ToArray();
    }

    // ── 7z: temp extraction + file hashing ──

    private string[] Hash7zEntries(string archivePath, string hashType)
    {
        if (_toolRunner is null)
            return Array.Empty<string>();

        var sevenZipPath = _toolRunner.FindTool("7z");
        if (string.IsNullOrEmpty(sevenZipPath))
            return Array.Empty<string>();

        // Pre-check entry paths for Zip-Slip
        var entryPaths = ListArchiveEntries(archivePath, sevenZipPath);
        if (entryPaths.Count > 0 && !AreEntryPathsSafe(entryPaths))
            return Array.Empty<string>();

        var tempDir = Path.Combine(Path.GetTempPath(), "romcleanup_7z_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var outArg = $"-o{tempDir}";
            var result = _toolRunner.InvokeProcess(sevenZipPath, new[] { "x", "-y", outArg, archivePath });
            if (result.ExitCode != 0)
                return Array.Empty<string>();

            // Post-extraction security: check for path traversal and reparse points
            var extractedFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
            var hashes = new List<string>();

            foreach (var file in extractedFiles)
            {
                // Validate extracted file is within tempDir
                if (!Path.GetFullPath(file).StartsWith(Path.GetFullPath(tempDir), StringComparison.OrdinalIgnoreCase))
                    return Array.Empty<string>();

                // Check reparse points
                var attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    return Array.Empty<string>();

                try
                {
                    using var fs = File.OpenRead(file);
                    var h = HashStream(fs, hashType);
                    if (h is not null)
                        hashes.Add(h);
                }
                catch { /* skip inaccessible */ }
            }

            return hashes.ToArray();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private List<string> ListArchiveEntries(string archivePath, string sevenZipPath)
    {
        var result = _toolRunner!.InvokeProcess(sevenZipPath, new[] { "l", "-slt", archivePath });
        if (result.ExitCode != 0)
            return new List<string>();

        var paths = new List<string>();
        var archiveName = Path.GetFileName(archivePath);
        bool pastSeparator = false;

        foreach (var line in (result.Output ?? "").Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("-----"))
            {
                pastSeparator = true;
                continue;
            }

            var match = RxPathEntry.Match(line);
            if (!match.Success) continue;
            if (!pastSeparator) continue;

            var entryPath = match.Groups[1].Value.Trim();
            // Skip the archive's own metadata entry
            if (entryPath.Equals(archiveName, StringComparison.OrdinalIgnoreCase))
                continue;

            paths.Add(entryPath);
        }

        return paths;
    }

    /// <summary>
    /// Detect Zip-Slip patterns: absolute paths or ".." traversal.
    /// </summary>
    internal static bool AreEntryPathsSafe(IEnumerable<string> entryPaths)
    {
        foreach (var p in entryPaths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (Path.IsPathRooted(p)) return false;
            if (Regex.IsMatch(p, @"(^|[\\/])\.\.([\\/]|$)")) return false;
        }
        return true;
    }

    private static string? HashStream(Stream stream, string hashType)
    {
        var type = hashType.ToUpperInvariant();

        if (type is "CRC" or "CRC32")
            return Crc32.HashStream(stream);

        using var algo = type switch
        {
            "SHA256" => (HashAlgorithm)SHA256.Create(),
            "MD5" => MD5.Create(),
            _ => SHA1.Create()
        };

        var bytes = algo.ComputeHash(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
