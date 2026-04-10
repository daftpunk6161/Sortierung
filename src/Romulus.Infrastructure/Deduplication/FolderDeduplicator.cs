using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.GameKeys;

namespace Romulus.Infrastructure.Deduplication;

/// <summary>
/// Folder-level deduplication engine for PS3 (hash-based) and base-name strategies.
/// Port of FolderDedupe.ps1.
/// </summary>
public sealed class FolderDeduplicator
{
    private static readonly HashSet<string> FolderDedupeConsoleKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "DOS", "AMIGA", "CD32", "C64", "PC98", "X68K", "MSX", "ATARIST",
        "ZX", "CPC", "FMTOWNS", "XBOX", "X360", "3DO", "CDI"
    };

    private static readonly HashSet<string> Ps3DedupeConsoleKeys = new(StringComparer.OrdinalIgnoreCase) { "PS3" };

    private static readonly string[] Ps3KeyFiles = ["PS3_DISC.SFB", "PARAM.SFO", "EBOOT.BIN"];

    private static readonly Regex MultidiscPattern = new(
        @"(?:Disc|Disk|CD|Side)\s*\d", RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    private readonly IFileSystem _fs;
    private readonly Action<string>? _log;

    public FolderDeduplicator(IFileSystem fs, Action<string>? log = null)
    {
        _fs = fs;
        _log = log;
    }

    /// <summary>
    /// Compute SHA256 hash of PS3 key files in a folder.
    /// Returns null if no key files found or no data could be read.
    /// </summary>
    public static string? GetPs3FolderHash(string folderPath)
    {
        using var sha = SHA256.Create();
        bool found = false;
        bool dataFed = false;

        foreach (var keyFile in Ps3KeyFiles)
        {
            var filePath = FindFileRecursive(folderPath, keyFile);
            if (filePath is null) continue;

            found = true;
            if (!File.Exists(filePath)) continue;
            try
            {
                using var stream = File.OpenRead(filePath);
                var buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha.TransformBlock(buffer, 0, bytesRead, null, 0);
                    dataFed = true;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        if (!found || !dataFed) return null;

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexStringLower(sha.Hash!);
    }

    /// <summary>
    /// Check if a folder name indicates a multi-disc PS3 game.
    /// </summary>
    public static bool IsPs3MultidiscFolder(string folderName)
        => MultidiscPattern.IsMatch(folderName);

    /// <summary>
    /// Normalize a folder name into a grouping key for deduplication.
    /// Preserves disc/side markers and platform-variant tags (AGA/ECS/OCS/NTSC/PAL).
    /// Delegates to <see cref="FolderKeyNormalizer"/> in Core (ADR-0007 §3.4).
    /// </summary>
    public static string GetFolderBaseKey(string folderName)
        => FolderKeyNormalizer.GetFolderBaseKey(folderName);

    /// <summary>
    /// PS3 folder deduplication: hash key files and move duplicates.
    /// </summary>
    public Ps3FolderDedupeResult DeduplicatePs3(
        IReadOnlyList<string> roots,
        string? dupeRoot = null,
        CancellationToken ct = default)
    {
        int total = 0, dupes = 0, moved = 0, skipped = 0;

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                _log?.Invoke($"WARNING: Root nicht gefunden: {root}");
                continue;
            }

            var dupeBase = string.IsNullOrWhiteSpace(dupeRoot)
                ? Path.Combine(root, RunConstants.WellKnownFolders.Ps3Dupes)
                : Path.Combine(dupeRoot, Path.GetFileName(root));
            dupeBase = Path.GetFullPath(dupeBase);

            // Cache normalized dupe path to avoid repeated GetFullPath on UNC per directory
            var normalizedDupeBase = dupeBase.TrimEnd(Path.DirectorySeparatorChar);
            var folders = Directory.GetDirectories(root)
                .Where(d => !string.Equals(d.TrimEnd(Path.DirectorySeparatorChar), normalizedDupeBase, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _log?.Invoke($"PS3 Scan: {folders.Length} Ordner in {root}");

            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < folders.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                total++;
                var folder = folders[i];
                var folderName = Path.GetFileName(folder);
                _log?.Invoke($"  [{i + 1}/{folders.Length}] {folderName}");

                var hash = GetPs3FolderHash(folder);
                if (hash is null)
                {
                    skipped++;
                    _log?.Invoke("    ÜBERSPRUNGEN (keine PS3-Schlüsseldateien)");
                    continue;
                }

                if (hashes.TryGetValue(hash, out var existingPath))
                {
                    // Create dupe directory on first actual duplicate found (TASK-013)
                    _fs.EnsureDirectory(dupeBase);
                    dupes++;
                    var existingCount = CountFilesRecursive(existingPath);
                    var newCount = CountFilesRecursive(folder);

                    // Quality comparison: more files wins, then alphabetical for determinism
                    var loserPath = folder;
                    if (newCount > existingCount ||
                        (newCount == existingCount && string.Compare(folder, existingPath, StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        loserPath = existingPath;
                        hashes[hash] = folder;
                    }

                    var loserName = Path.GetFileName(loserPath);
                    var dest = Path.Combine(dupeBase, loserName!);

                    // Path traversal check — source AND destination
                    var resolvedSrc = _fs.ResolveChildPathWithinRoot(root, Path.GetRelativePath(root, loserPath));
                    if (resolvedSrc is null)
                    {
                        _log?.Invoke($"    BLOCKED: {loserName} - außerhalb Root");
                        continue;
                    }

                    // SEC-DEDUP-01: Validate destination path within dupeBase
                    var resolvedDest = _fs.ResolveChildPathWithinRoot(dupeBase, loserName!);
                    if (resolvedDest is null)
                    {
                        _log?.Invoke($"    BLOCKED: {loserName} - Ziel außerhalb Dupe-Root");
                        continue;
                    }

                    if (_fs.MoveDirectorySafely(loserPath, dest))
                    {
                        moved++;
                        _log?.Invoke($"    DUP -> {loserName}");
                    }
                }
                else
                {
                    hashes[hash] = folder;
                }
            }
        }

        _log?.Invoke($"PS3 Dedupe: {total} gescannt, {dupes} Duplikate, {moved} verschoben, {skipped} übersprungen");
        return new Ps3FolderDedupeResult { Total = total, Dupes = dupes, Moved = moved, Skipped = skipped };
    }

    /// <summary>
    /// Base-name folder deduplication. Winner: most files (populated beats empty),
    /// then newest file, then most files count, then shortest name.
    /// </summary>
    public FolderDedupeResult DeduplicateByBaseName(
        IReadOnlyList<string> roots,
        string? dupeRoot = null,
        string mode = RunConstants.ModeDryRun,
        CancellationToken ct = default)
    {
        int totalFolders = 0, dupeGroups = 0, movedFolders = 0, errorCount = 0;
        var actions = new List<FolderDedupeAction>();

        foreach (var rootPath in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                _log?.Invoke($"WARNING: Root not found: {rootPath}");
                continue;
            }

            var normalizedRoot = Path.GetFullPath(rootPath);
            var dupeBase = string.IsNullOrWhiteSpace(dupeRoot)
                ? Path.Combine(normalizedRoot, RunConstants.WellKnownFolders.FolderDupes)
                : Path.GetFullPath(dupeRoot);

            if (mode == RunConstants.ModeMove)
                _fs.EnsureDirectory(dupeBase);

            // Cache normalized dupe path to avoid repeated GetFullPath on UNC per directory
            var normalizedDupeBase = dupeBase.TrimEnd(Path.DirectorySeparatorChar);
            var folders = Directory.GetDirectories(normalizedRoot)
                .Where(d => !string.Equals(d.TrimEnd(Path.DirectorySeparatorChar), normalizedDupeBase, StringComparison.OrdinalIgnoreCase))
                .Select(d => new DirectoryInfo(d))
                .ToArray();

            if (folders.Length == 0)
            {
                _log?.Invoke($"No sub-folders found in: {normalizedRoot}");
                continue;
            }

            totalFolders += folders.Length;
            _log?.Invoke($"Folder-Dedupe scan: {folders.Length} folders in {normalizedRoot}");

            // Group by normalized base key
            var groups = folders.GroupBy(d => GetFolderBaseKey(d.Name));

            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(group.Key) || group.Count() <= 1)
                    continue;

                dupeGroups++;
                var candidates = group.Select(dir => new
                {
                    Dir = dir,
                    Newest = GetNewestFileTimestamp(dir),
                    FileCount = CountFilesRecursive(dir.FullName)
                }).ToList();

                // Sort: populated first, newest, most files, shortest name, then alpha
                var sorted = candidates
                    .OrderByDescending(c => c.FileCount > 0 ? 1 : 0)
                    .ThenByDescending(c => c.Newest)
                    .ThenByDescending(c => c.FileCount)
                    .ThenBy(c => c.Dir.Name.Length)
                    .ThenBy(c => c.Dir.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var winner = sorted[0];
                _log?.Invoke($"  Key '{group.Key}' -> {sorted.Count} folders | KEEP: {winner.Dir.Name} (newest: {winner.Newest:u}, files: {winner.FileCount})");

                for (int i = 1; i < sorted.Count; i++)
                {
                    var loser = sorted[i];
                    var srcPath = loser.Dir.FullName;
                    var destPath = Path.Combine(dupeBase, loser.Dir.Name);

                    var action = new FolderDedupeAction
                    {
                        Key = group.Key,
                        Source = srcPath,
                        Dest = destPath,
                        Winner = winner.Dir.FullName
                    };

                    if (mode == RunConstants.ModeDryRun)
                    {
                        action = action with { Action = "DRYRUN-MOVE" };
                        _log?.Invoke($"    DRYRUN -> {loser.Dir.Name}");
                    }
                    else
                    {
                        var resolvedSrc = _fs.ResolveChildPathWithinRoot(
                            normalizedRoot, Path.GetRelativePath(normalizedRoot, srcPath));
                        if (resolvedSrc is null)
                        {
                            action = action with { Action = "BLOCKED", Error = "Source outside root or crosses reparse point" };
                            errorCount++;
                            _log?.Invoke($"    BLOCKED: {loser.Dir.Name} - outside root or reparse point");
                            actions.Add(action);
                            continue;
                        }

                        // TASK-014: Validate destination path too
                        var resolvedDest = _fs.ResolveChildPathWithinRoot(
                            dupeBase, loser.Dir.Name);
                        if (resolvedDest is null)
                        {
                            action = action with { Action = "BLOCKED", Error = "Destination outside dupe root" };
                            errorCount++;
                            _log?.Invoke($"    BLOCKED: {loser.Dir.Name} - destination outside dupe root");
                            actions.Add(action);
                            continue;
                        }

                        try
                        {
                            if (_fs.MoveDirectorySafely(srcPath, destPath))
                            {
                                action = action with { Action = "MOVED", Dest = destPath };
                                movedFolders++;
                                _log?.Invoke($"    MOVED -> {destPath}");
                            }
                            else
                            {
                                action = action with { Action = "ERROR", Error = "Move returned false" };
                                errorCount++;
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            action = action with { Action = "ERROR", Error = ex.Message };
                            errorCount++;
                            _log?.Invoke($"    ERROR moving {loser.Dir.Name}: {ex.Message}");
                        }
                    }
                    actions.Add(action);
                }
            }
        }

        _log?.Invoke($"Folder-Dedupe complete: {totalFolders} scanned, {dupeGroups} dupe groups, {movedFolders} moved, {errorCount} errors (mode: {mode})");
        return new FolderDedupeResult
        {
            TotalFolders = totalFolders,
            DupeGroups = dupeGroups,
            Moved = movedFolders,
            Errors = errorCount,
            Mode = mode,
            Actions = actions
        };
    }

    /// <summary>
    /// Auto-detect which roots need folder-level dedup and dispatch accordingly.
    /// </summary>
    public AutoFolderDedupeResult AutoDeduplicate(
        IReadOnlyList<string> roots,
        string mode = RunConstants.ModeDryRun,
        string? dupeRoot = null,
        Func<string, string?>? consoleKeyDetector = null,
        CancellationToken ct = default)
    {
        var ps3Roots = new List<string>();
        var folderRoots = new List<string>();
        var results = new List<AutoFolderDedupeEntry>();

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            var consoleKey = consoleKeyDetector?.Invoke(root);
            if (consoleKey is not null && Ps3DedupeConsoleKeys.Contains(consoleKey))
            {
                ps3Roots.Add(root);
                _log?.Invoke($"Auto-dedupe: {root} -> PS3 hash-based dedupe");
            }
            else if (consoleKey is not null && FolderDedupeConsoleKeys.Contains(consoleKey))
            {
                folderRoots.Add(root);
                _log?.Invoke($"Auto-dedupe: {root} -> folder base-name dedupe ({consoleKey})");
            }
        }

        if (ps3Roots.Count > 0 && mode == RunConstants.ModeMove)
        {
            _log?.Invoke($"Auto-dedupe: running PS3 dedupe on {ps3Roots.Count} root(s)...");
            try
            {
                var ps3Result = DeduplicatePs3(ps3Roots, dupeRoot, ct);
                results.Add(new AutoFolderDedupeEntry { Type = "PS3", Roots = ps3Roots, Result = ps3Result });
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Auto-dedupe: PS3 dedupe error: {ex.Message}");
            }
        }
        else if (ps3Roots.Count > 0)
        {
            _log?.Invoke("Auto-dedupe: PS3 dedupe skipped in DryRun mode (hash-based dedupe is destructive-only)");
        }

        if (folderRoots.Count > 0)
        {
            _log?.Invoke($"Auto-dedupe: running folder dedupe on {folderRoots.Count} root(s)...");
            try
            {
                var folderResult = DeduplicateByBaseName(folderRoots, dupeRoot, mode, ct);
                results.Add(new AutoFolderDedupeEntry { Type = "FolderBaseName", Roots = folderRoots, Result = folderResult });
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Auto-dedupe: folder dedupe error: {ex.Message}");
            }
        }

        if (ps3Roots.Count == 0 && folderRoots.Count == 0)
            _log?.Invoke("Auto-dedupe: no roots detected that need folder-level deduplication");

        return new AutoFolderDedupeResult
        {
            Ps3Roots = ps3Roots,
            FolderRoots = folderRoots,
            Mode = mode,
            Results = results
        };
    }

    /// <summary>
    /// Check if a console key requires folder-level deduplication.
    /// </summary>
    public static bool NeedsFolderDedupe(string consoleKey)
        => FolderDedupeConsoleKeys.Contains(consoleKey);

    /// <summary>
    /// Check if a console key requires PS3 hash-based deduplication.
    /// </summary>
    public static bool NeedsPs3Dedupe(string consoleKey)
        => Ps3DedupeConsoleKeys.Contains(consoleKey);

    private static DateTime GetNewestFileTimestamp(DirectoryInfo dir)
    {
        try
        {
            var newest = dir.EnumerateFiles("*", SearchOption.AllDirectories)
                .Select(f => f.LastWriteTimeUtc)
                .DefaultIfEmpty(dir.LastWriteTimeUtc)
                .Max();
            return newest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return dir.LastWriteTimeUtc;
        }
    }

    private static int CountFilesRecursive(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count();
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string? FindFileRecursive(string folderPath, string fileName)
    {
        try
        {
            return Directory.EnumerateFiles(folderPath, fileName, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
