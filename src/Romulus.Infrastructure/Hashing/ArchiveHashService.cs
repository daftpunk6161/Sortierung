using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Romulus.Contracts.Ports;
using Romulus.Core.Caching;

namespace Romulus.Infrastructure.Hashing;

/// <summary>
/// Extracts hashes from files inside ZIP and 7z archives.
/// Port of Get-ArchiveHashes, Get-HashesFromZip, Get-HashesFrom7z from Dat.ps1.
/// ZIP entries are hashed in-memory (no temp extraction).
/// 7z archives require extraction via IToolRunner.
/// </summary>
public sealed class ArchiveHashService
{
    private static readonly Regex RxPathEntry = new(@"^Path = (.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline,
        TimeSpan.FromMilliseconds(500));

    private readonly LruCache<string, string[]> _cache;
    private readonly IToolRunner? _toolRunner;
    private readonly long _maxArchiveSizeBytes;

    /// <param name="toolRunner">Required for .7z archives (delegates to 7z.exe).</param>
    /// <param name="maxEntries">LRU cache max entries.</param>
    /// <param name="maxArchiveSizeBytes">Archives larger than this skip hashing (default 500 MB).</param>
    public ArchiveHashService(
        IToolRunner? toolRunner = null,
        int maxEntries = 5000,
        long maxArchiveSizeBytes = 500 * 1024 * 1024)
    {
        _toolRunner = toolRunner;
        _maxArchiveSizeBytes = maxArchiveSizeBytes;
        _cache = new LruCache<string, string[]>(maxEntries, StringComparer.OrdinalIgnoreCase);
        CleanupStaleTempDirs();
    }

    /// <summary>Remove leftover temp directories from previous crashed runs.</summary>
    private static void CleanupStaleTempDirs()
    {
        try
        {
            var tempRoot = Path.GetTempPath();
            var staleThreshold = DateTime.UtcNow.AddMinutes(-5);
            foreach (var dir in Directory.GetDirectories(tempRoot, "romulus_7z_*"))
            {
                try
                {
                    // Block reparse points to prevent directory junction attacks
                    var dirInfo = new DirectoryInfo(dir);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    // Only clean up directories older than 5 minutes to avoid
                    // deleting temp dirs still in use by concurrent operations.
                    if (dirInfo.CreationTimeUtc > staleThreshold)
                        continue;
                    Directory.Delete(dir, recursive: true);
                }
                catch (IOException) { /* best-effort cleanup of stale temp dir */ }
                catch (UnauthorizedAccessException) { /* permission denied — skip stale temp dir */ }
            }
        }
        catch (IOException) { /* temp path inaccessible — ignore */ }
        catch (UnauthorizedAccessException) { /* temp path access denied — ignore */ }
    }

    /// <summary>
    /// Get hashes of all entries inside an archive file.
    /// Returns empty array on failure or if archive exceeds size limit.
    /// </summary>
    public string[] GetArchiveHashes(string archivePath, string hashType = "SHA1",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            return Array.Empty<string>();

        ct.ThrowIfCancellationRequested();

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
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }

        var ext = Path.GetExtension(archivePath).ToLowerInvariant();
        string[] hashes;
        try
        {
            hashes = ext == ".zip"
                ? HashZipEntries(archivePath, hashType, ct)
                : ext == ".7z"
                    ? Hash7zEntries(archivePath, hashType, ct)
                    : Array.Empty<string>();
        }
        catch (OperationCanceledException) { throw; }
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

    /// <summary>
    /// Get the entry names inside a 7z archive (for console detection by inner extension).
    /// Returns a list of relative entry paths. Returns empty list on failure.
    /// For ZIP files, uses System.IO.Compression to list entries directly.
    /// </summary>
    public IReadOnlyList<string> GetArchiveEntryNames(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            return Array.Empty<string>();

        var ext = Path.GetExtension(archivePath).ToLowerInvariant();

        if (ext == ".zip")
        {
            try
            {
                using var archive = ZipFile.OpenRead(archivePath);
                return archive.Entries
                    .Where(e => !string.IsNullOrEmpty(e.Name))
                    .Select(e => e.FullName)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (InvalidDataException) { return Array.Empty<string>(); }
            catch (IOException) { return Array.Empty<string>(); }
        }

        if (ext == ".7z" && _toolRunner is not null)
        {
            var sevenZipPath = _toolRunner.FindTool("7z");
            if (string.IsNullOrEmpty(sevenZipPath))
                return Array.Empty<string>();

            return ListArchiveEntries(archivePath, sevenZipPath);
        }

        return Array.Empty<string>();
    }

    // ── ZIP: in-memory stream hashing ──

    private static string[] HashZipEntries(string zipPath, string hashType,
        CancellationToken ct = default)
    {
        var hashes = new List<string>();
        using var archive = ZipFile.OpenRead(zipPath);

        // TASK-150: Sort entries alphabetically for deterministic hash order
        var sortedEntries = archive.Entries
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var isCrc = hashType.ToUpperInvariant() is "CRC" or "CRC32";

        foreach (var entry in sortedEntries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Length <= 0) continue;
            try
            {
                using var stream = entry.Open();
                var h = HashStream(stream, hashType);
                if (h is not null)
                    hashes.Add(h);

                // Also emit native CRC32 from ZIP header for DATs that only carry CRC32
                // (MAME, FBNeo). ZIP stores CRC32 per entry at no extra cost.
                if (!isCrc && entry.Crc32 != 0)
                {
                    hashes.Add(entry.Crc32.ToString("x8"));
                }
            }
            catch (InvalidDataException) { /* skip corrupt entries */ }
            catch (IOException) { /* skip unreadable entries */ }
        }

        return hashes.ToArray();
    }

    // ── 7z: temp extraction + file hashing ──

    private string[] Hash7zEntries(string archivePath, string hashType,
        CancellationToken ct = default)
    {
        if (_toolRunner is null)
            return Array.Empty<string>();

        var sevenZipPath = _toolRunner.FindTool("7z");
        if (string.IsNullOrEmpty(sevenZipPath))
            return Array.Empty<string>();

        // Pre-check entry paths for Zip-Slip (fail-closed: empty listing = reject)
        var entryPaths = ListArchiveEntries(archivePath, sevenZipPath);
        if (!AreEntryPathsSafe(entryPaths))
            return Array.Empty<string>();

        var tempDir = Path.Combine(Path.GetTempPath(), "romulus_7z_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var outArg = $"-o{tempDir}";
            var result = _toolRunner.InvokeProcess(sevenZipPath, new[] { "x", "-y", outArg, archivePath });
            if (result.ExitCode != 0)
                return Array.Empty<string>();

            ct.ThrowIfCancellationRequested();

            // Post-extraction security: check for path traversal, reparse points, and directory junctions
            var normalizedTemp = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;

            // Check directories for junctions/reparse points
            var dirIndex = 0;
            foreach (var dir in Directory.GetDirectories(tempDir, "*", SearchOption.AllDirectories))
            {
                if (++dirIndex % 100 == 0) ct.ThrowIfCancellationRequested();
                var dirInfo = new DirectoryInfo(dir);
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    return Array.Empty<string>();
                if (!Path.GetFullPath(dir).StartsWith(normalizedTemp, StringComparison.OrdinalIgnoreCase))
                    return Array.Empty<string>();
            }

            ct.ThrowIfCancellationRequested();
            var extractedFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
            // TASK-150: Sort extracted files alphabetically for deterministic hash order
            Array.Sort(extractedFiles, StringComparer.OrdinalIgnoreCase);
            var hashes = new List<string>();

            foreach (var file in extractedFiles)
            {
                ct.ThrowIfCancellationRequested();
                // Validate extracted file is within tempDir (with separator guard)
                if (!Path.GetFullPath(file).StartsWith(normalizedTemp, StringComparison.OrdinalIgnoreCase))
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
                catch (IOException) { /* skip inaccessible */ }
                catch (UnauthorizedAccessException) { /* skip inaccessible */ }
            }

            return hashes.ToArray();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                try { Directory.Delete(tempDir, true); }
                catch (IOException) { /* Best-effort cleanup — dir may be locked by AV or other process */ }
                catch (UnauthorizedAccessException) { /* Permission denied on temp cleanup — non-fatal */ }
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

        // TASK-150: Sort entries alphabetically for deterministic order
        paths.Sort(StringComparer.OrdinalIgnoreCase);
        return paths;
    }

    private static readonly Regex RxDotDotTraversal = new(
        @"(^|[\\/])\.\.([\\/]|$)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// Detect Zip-Slip patterns: absolute paths or ".." traversal.
    /// </summary>
    internal static bool AreEntryPathsSafe(IEnumerable<string> entryPaths)
    {
        foreach (var p in entryPaths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (Path.IsPathRooted(p)) return false;
            if (RxDotDotTraversal.IsMatch(p)) return false;
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
