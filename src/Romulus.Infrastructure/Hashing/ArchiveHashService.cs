using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Romulus.Contracts.Ports;
using Romulus.Core.Caching;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Safety;

namespace Romulus.Infrastructure.Hashing;

/// <summary>
/// Extracts hashes from files inside ZIP and 7z archives.
/// Port of Get-ArchiveHashes, Get-HashesFromZip, Get-HashesFrom7z from Dat.ps1.
/// ZIP entries are hashed in-memory (no temp extraction).
/// 7z archives require extraction via IToolRunner.
/// </summary>
public sealed class ArchiveHashService
{
    private static readonly Romulus.Contracts.Models.ToolRequirement SevenZipRequirement = new() { ToolName = "7z" };
    /// <summary>
    /// Hard cap for the cumulative uncompressed payload that ArchiveHashService will hash
    /// (sum across all entries inside a single archive). Mirrors the 7z extraction cap so
    /// that the ZIP path enforces the same zipbomb protection (F-DAT-16).
    /// </summary>
    internal const long MaxArchiveCumulativeUncompressedBytes = 10L * 1024 * 1024 * 1024;
    private const long MaxSevenZipExtractedTotalBytes = MaxArchiveCumulativeUncompressedBytes;

    /// <summary>
    /// Reasons why ArchiveHashService skipped or aborted hashing for an archive entry.
    /// Used for structured logging only — the public hash methods continue to return
    /// <c>string[]</c> for backwards compatibility (F-DAT-15).
    /// </summary>
    internal enum ArchiveHashFailureReason
    {
        None = 0,
        ArchiveTooLarge,
        CumulativeBytesExceeded,
        Corrupt,
        ToolMissing,
        AccessDenied,
        IoError,
        SecurityBlocked,
    }

    private static readonly Regex RxPathEntry = new(@"^Path = (.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline,
        TimeSpan.FromMilliseconds(500));

    /// <summary>Cache entry that tracks file metadata for staleness detection.</summary>
    private sealed record ArchiveCacheEntry(string[] Hashes, long FileSize, DateTime LastWriteUtc);

    private readonly LruCache<string, ArchiveCacheEntry> _cache;
    private readonly IToolRunner? _toolRunner;
    private readonly long _maxArchiveSizeBytes;
    private readonly long _maxCumulativeUncompressedBytes;
    private readonly Action<string>? _log;

    /// <param name="toolRunner">Required for .7z archives (delegates to 7z.exe).</param>
    /// <param name="maxEntries">LRU cache max entries.</param>
    /// <param name="maxArchiveSizeBytes">Archives larger than this skip hashing (default 500 MB).</param>
    /// <param name="maxCumulativeUncompressedBytes">Per-archive cumulative uncompressed cap (default 10 GB; F-DAT-16 zipbomb cap).</param>
    /// <param name="log">Optional structured-skip logger (F-DAT-15).</param>
    public ArchiveHashService(
        IToolRunner? toolRunner = null,
        int maxEntries = 5000,
        long maxArchiveSizeBytes = 500 * 1024 * 1024,
        long? maxCumulativeUncompressedBytes = null,
        Action<string>? log = null)
    {
        _toolRunner = toolRunner;
        _maxArchiveSizeBytes = maxArchiveSizeBytes;
        _maxCumulativeUncompressedBytes = maxCumulativeUncompressedBytes ?? MaxArchiveCumulativeUncompressedBytes;
        _log = log;
        _cache = new LruCache<string, ArchiveCacheEntry>(maxEntries, StringComparer.OrdinalIgnoreCase);
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
        {
            // R6-07: validate that the cache entry is still fresh before using it.
            // If the file has been modified (different size or mtime), recompute.
            try
            {
                var fi = new FileInfo(archivePath);
                if (fi.LastWriteTimeUtc == cached.LastWriteUtc && fi.Length == cached.FileSize)
                    return cached.Hashes;
                // File changed – fall through to recompute; new entry will overwrite this one.
            }
            catch (IOException) { return cached.Hashes; }
            catch (UnauthorizedAccessException) { return cached.Hashes; }
        }

        // Capture file metadata for cache entry and size-limit check
        long fileSize = 0;
        DateTime lastWrite = DateTime.MinValue;
        try
        {
            var fi = new FileInfo(archivePath);
            fileSize = fi.Length;
            lastWrite = fi.LastWriteTimeUtc;
            if (fi.Length > _maxArchiveSizeBytes)
            {
                _cache.Set(cacheKey, new ArchiveCacheEntry(Array.Empty<string>(), fileSize, lastWrite));
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

        _cache.Set(cacheKey, new ArchiveCacheEntry(hashes, fileSize, lastWrite));
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

    private string[] HashZipEntries(string zipPath, string hashType,
        CancellationToken ct = default)
    {
        var hashes = new List<string>();
        using var archive = ZipFile.OpenRead(zipPath);
        // TASK-150: Sort entries alphabetically for deterministic hash order
        var sortedEntries = archive.Entries
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // F-DAT-16: cumulative uncompressed-byte counter mirrors the 7z extraction cap so a
        // ZIP that declares >10 GB of payload (or a single oversized entry) is rejected before
        // any data flows through the hash algorithm. We rely on the declared
        // <see cref="ZipArchiveEntry.Length"/> for the budget check; the actual stream copy
        // still runs entry-by-entry.
        long cumulativeUncompressed = 0;
        foreach (var entry in sortedEntries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) continue;

            if (entry.Length < 0)
            {
                LogSkip(zipPath, ArchiveHashFailureReason.Corrupt, $"entry '{entry.FullName}' reports negative length");
                return Array.Empty<string>();
            }

            checked
            {
                try { cumulativeUncompressed += entry.Length; }
                catch (OverflowException)
                {
                    LogSkip(zipPath, ArchiveHashFailureReason.CumulativeBytesExceeded, "uncompressed-byte counter overflow");
                    return Array.Empty<string>();
                }
            }

            if (cumulativeUncompressed > _maxCumulativeUncompressedBytes)
            {
                LogSkip(
                    zipPath,
                    ArchiveHashFailureReason.CumulativeBytesExceeded,
                    $"cumulative uncompressed payload {cumulativeUncompressed:N0} bytes exceeds cap {_maxCumulativeUncompressedBytes:N0} bytes (zipbomb-Schutz)");
                return Array.Empty<string>();
            }

            try
            {
                using var stream = entry.Open();
                var h = HashStream(stream, hashType);
                if (h is not null)
                    hashes.Add(h);
            }
            catch (InvalidDataException) { /* skip corrupt entries */ }
            catch (IOException) { /* skip unreadable entries */ }
        }

        return hashes.ToArray();
    }

    private void LogSkip(string archivePath, ArchiveHashFailureReason reason, string detail)
    {
        if (_log is null) return;
        try { _log.Invoke($"[ArchiveHashService] skip archive='{Path.GetFileName(archivePath)}' reason={reason} detail={detail}"); }
        catch { /* logger must never break hashing */ }
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
        var entries = ListArchiveEntriesDetailed(archivePath, sevenZipPath, ct);
        var entryPaths = entries.Select(static entry => entry.Path).ToList();
        if (entryPaths.Count == 0)
            return Array.Empty<string>();

        if (!AreEntryPathsSafe(entryPaths))
            return Array.Empty<string>();

        var declaredTotalSize = 0L;
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Size < 0)
                return Array.Empty<string>();

            declaredTotalSize += entry.Size;
            if (declaredTotalSize > MaxSevenZipExtractedTotalBytes)
                return Array.Empty<string>();
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "romulus_7z_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            if (!FileSystemSafetyHelpers.HasAvailableTempSpace(tempDir, declaredTotalSize))
                return Array.Empty<string>();

            var outArg = $"-o{tempDir}";
            var result = _toolRunner.InvokeProcess(
                sevenZipPath,
                new[] { "x", "-y", "-snl-", outArg, archivePath },
                SevenZipRequirement,
                "7z extract",
                TimeSpan.FromMinutes(10),
                ct);
            if (result.ExitCode != 0)
                return Array.Empty<string>();

            ct.ThrowIfCancellationRequested();

            // Post-extraction security: check for path traversal, reparse points, and directory junctions
            var normalizedTemp = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;

            // Check directories for junctions/reparse points
            var dirIndex = 0;
            foreach (var dir in FileSystemSafetyHelpers.EnumerateDirectoriesWithoutFollowingReparsePoints(tempDir))
            {
                if (++dirIndex % 100 == 0) ct.ThrowIfCancellationRequested();
                var dirInfo = new DirectoryInfo(dir);
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    return Array.Empty<string>();
                if (!Path.GetFullPath(dir).StartsWith(normalizedTemp, StringComparison.OrdinalIgnoreCase))
                    return Array.Empty<string>();
            }

            ct.ThrowIfCancellationRequested();
            var extractedFiles = new FileSystemAdapter().GetFilesSafe(tempDir);
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
        => ListArchiveEntriesDetailed(archivePath, sevenZipPath, CancellationToken.None)
            .Select(static entry => entry.Path)
            .ToList();

    private List<SevenZipEntryInfo> ListArchiveEntriesDetailed(
        string archivePath,
        string sevenZipPath,
        CancellationToken cancellationToken)
    {
        var result = _toolRunner!.InvokeProcess(
            sevenZipPath,
            new[] { "l", "-slt", archivePath },
            SevenZipRequirement,
            "7z list",
            TimeSpan.FromMinutes(5),
            cancellationToken);
        if (result.ExitCode != 0)
            return [];

        var entries = new List<SevenZipEntryInfo>();
        var archiveName = Path.GetFileName(archivePath);
        bool pastSeparator = false;
        string? currentPath = null;
        long currentSize = 0;

        void FlushCurrent()
        {
            if (string.IsNullOrWhiteSpace(currentPath))
                return;

            if (!currentPath.Equals(archiveName, StringComparison.OrdinalIgnoreCase))
                entries.Add(new SevenZipEntryInfo(currentPath, currentSize));

            currentPath = null;
            currentSize = 0;
        }

        foreach (var line in (result.Output ?? "").Split('\n', StringSplitOptions.TrimEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.StartsWith("-----"))
            {
                pastSeparator = true;
                continue;
            }

            var match = RxPathEntry.Match(line);
            if (match.Success && pastSeparator)
            {
                FlushCurrent();
                currentPath = match.Groups[1].Value.Trim();
                currentSize = 0;
                continue;
            }

            if (!pastSeparator || currentPath is null)
                continue;

            if (line.StartsWith("Size = ", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(line["Size = ".Length..].Trim(), out var parsedSize))
            {
                currentSize = parsedSize;
            }
        }

        FlushCurrent();

        // TASK-150: Sort entries alphabetically for deterministic order
        entries.Sort(static (left, right) => string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
        return entries;
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

    private sealed record SevenZipEntryInfo(string Path, long Size);
}
