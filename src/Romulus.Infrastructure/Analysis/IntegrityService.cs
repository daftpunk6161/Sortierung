using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Safety;
using Romulus.Infrastructure.Tools;

namespace Romulus.Infrastructure.Analysis;

/// <summary>
/// Integrity monitoring and header analysis extracted from FeatureService.Security.
/// Pure logic + file I/O, no GUI dependency.
/// </summary>
public static class IntegrityService
{
    private static readonly string TrendFile = AppStoragePathResolver.ResolveRoamingPath("trend-history.json");

    private static readonly string BaselinePath = AppStoragePathResolver.ResolveRoamingPath("integrity-baseline.json");

    // --- Header Analysis ---

    public static RomHeaderInfo? AnalyzeHeader(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            using var fs = File.OpenRead(filePath);
            var header = new byte[Math.Min(65536, fs.Length)];
            _ = fs.Read(header, 0, header.Length);
            var analyzed = HeaderAnalyzer.AnalyzeHeader(header, fs.Length);
            return analyzed is null
                ? null
                : new RomHeaderInfo(analyzed.Platform, analyzed.Format, analyzed.Details);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    // --- Trend Analysis ---

    public static void SaveTrendSnapshot(int totalFiles, long sizeBytes, int verified, int dupes, int junk, TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetLocalNow().DateTime;
        var history = LoadLegacyTrendHistory();
        history.Add(new TrendSnapshot(now, totalFiles, sizeBytes, verified, dupes, junk,
            CollectionAnalysisService.CalculateHealthScore(totalFiles, dupes, junk, verified)));
        if (history.Count > 365) history.RemoveRange(0, history.Count - 365);
        WriteTextSafely(TrendFile, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static List<TrendSnapshot> LoadTrendHistory()
    {
        try
        {
            using var collectionIndex = new LiteDbCollectionIndex(CollectionIndexPaths.ResolveDefaultDatabasePath());
            var history = RunHistoryTrendService.LoadTrendHistoryAsync(collectionIndex).GetAwaiter().GetResult();
            if (history.Count > 0)
                return history.ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // fall back to legacy trend sidecar
        }

        return LoadLegacyTrendHistory();
    }

    public static string FormatTrendReport(List<TrendSnapshot> history)
        => RunHistoryTrendService.FormatTrendReport(
            history,
            title: "Trend Analysis",
            emptyMessage: "No trend data available.",
            currentLabel: "Current",
            deltaFilesLabel: "Delta files",
            deltaDuplicatesLabel: "Delta duplicates",
            historyLabel: "History (last 10):",
            filesLabel: "files",
            qualityLabel: "Quality");

    // --- Integrity Baseline ---

    public static async Task<Dictionary<string, IntegrityEntry>> CreateBaseline(
        IReadOnlyList<string> filePaths, IProgress<string>? progress = null, CancellationToken ct = default, IFileSystem? fileSystem = null)
    {
        if (filePaths.Count == 0)
            return new Dictionary<string, IntegrityEntry>(StringComparer.OrdinalIgnoreCase);
        var commonRoot = FindCommonRoot(filePaths) ?? Path.GetDirectoryName(filePaths[0]) ?? "";
        var baseline = new ConcurrentDictionary<string, IntegrityEntry>(StringComparer.OrdinalIgnoreCase);
        int completed = 0;
        var total = filePaths.Count;

        await Parallel.ForEachAsync(filePaths, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
            async (path, token) =>
            {
                if (!File.Exists(path)) return;
                var fi = new FileInfo(path);
                var count = Interlocked.Increment(ref completed);
                progress?.Report($"Baseline: {count}/{total} - {Path.GetFileName(path)}");
                var hash = await Task.Run(() => ComputeSha256(path), token);
                var relPath = Path.GetRelativePath(commonRoot, path);
                baseline[relPath] = new IntegrityEntry(hash, fi.Length, fi.LastWriteTimeUtc);
            });

        var entries = baseline
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var wrapper = new IntegrityBaseline(commonRoot, entries);
        WriteTextSafely(BaselinePath, JsonSerializer.Serialize(wrapper, new JsonSerializerOptions { WriteIndented = true }), fileSystem);
        return entries;
    }

    public static async Task<IntegrityCheckResult> CheckIntegrity(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(BaselinePath))
            return new IntegrityCheckResult([], [], [], false, "No baseline found. Create a baseline first via 'integrity baseline'.");

        var json = File.ReadAllText(BaselinePath);
        Dictionary<string, IntegrityEntry> entries;
        string root;

        try
        {
            var wrapper = JsonSerializer.Deserialize<IntegrityBaseline>(json);
            if (wrapper is { Root: not null, Entries: not null })
            {
                root = wrapper.Root;
                entries = wrapper.Entries;
            }
            else
            {
                entries = JsonSerializer.Deserialize<Dictionary<string, IntegrityEntry>>(json) ?? [];
                root = "";
            }
        }
        catch (JsonException)
        {
            entries = JsonSerializer.Deserialize<Dictionary<string, IntegrityEntry>>(json) ?? [];
            root = "";
        }

        var changed = new List<string>();
        var missing = new List<string>();
        var intact = new List<string>();
        int i = 0;

        foreach (var (relPath, entry) in entries)
        {
            ct.ThrowIfCancellationRequested();
            var absPath = string.IsNullOrEmpty(root) ? relPath : Path.GetFullPath(Path.Combine(root, relPath));
            progress?.Report($"Checking: {++i}/{entries.Count} - {Path.GetFileName(absPath)}");
            if (!File.Exists(absPath)) { missing.Add(absPath); continue; }
            var hash = await Task.Run(() => ComputeSha256(absPath), ct);
            if (hash != entry.Hash) changed.Add(absPath);
            else intact.Add(absPath);
        }

        return new IntegrityCheckResult(changed, missing, intact, changed.Count > 0);
    }

    // --- Backup ---

    public static string CreateBackup(
        IReadOnlyList<string> filePaths,
        string backupRoot,
        string label,
        TimeProvider? timeProvider = null,
        IFileSystem? fileSystem = null)
    {
        var fs = ResolveFileSystem(fileSystem);
        var safeBackupRoot = SafetyValidator.EnsureSafeOutputPath(backupRoot, allowUnc: false);
        fs.EnsureDirectory(safeBackupRoot);

        var now = (timeProvider ?? TimeProvider.System).GetLocalNow().DateTime;
        var normalizedLabel = NormalizeBackupLabel(label);
        var sessionLeaf = $"{now:yyyyMMdd-HHmmss}_{normalizedLabel}";
        var sessionDir = SafetyValidator.EnsureSafeOutputPath(Path.Combine(safeBackupRoot, sessionLeaf), allowUnc: false);

        var rootPrefix = safeBackupRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!sessionDir.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Blocked: Backup session path escapes backup root.");

        fs.EnsureDirectory(sessionDir);

        var commonRoot = FindCommonRoot(filePaths);

        foreach (var path in filePaths)
        {
            var normalizedSource = SafetyValidator.NormalizePath(path);
            if (string.IsNullOrWhiteSpace(normalizedSource) || !File.Exists(normalizedSource))
                continue;

            var relativePath = commonRoot is not null
                ? Path.GetRelativePath(commonRoot, normalizedSource)
                : Path.GetFileName(normalizedSource);
            var dest = fs.ResolveChildPathWithinRoot(sessionDir, relativePath);
            if (string.IsNullOrWhiteSpace(dest))
                throw new InvalidOperationException("Blocked: Backup destination escaped session root.");

            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrWhiteSpace(destDir))
                fs.EnsureDirectory(destDir);

            fs.CopyFile(normalizedSource, dest, overwrite: false);
        }
        return sessionDir;
    }

    public static int CleanupOldBackups(
        string backupRoot,
        int retentionDays,
        Func<int, bool>? confirmDelete = null,
        TimeProvider? timeProvider = null,
        IFileSystem? fileSystem = null)
    {
        var fs = ResolveFileSystem(fileSystem);
        var safeBackupRoot = SafetyValidator.EnsureSafeOutputPath(backupRoot, allowUnc: false);
        if (!Directory.Exists(safeBackupRoot)) return 0;

        var rootPrefix = safeBackupRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var cutoff = (timeProvider ?? TimeProvider.System).GetLocalNow().DateTime.AddDays(-retentionDays);
        var expired = Directory.GetDirectories(safeBackupRoot)
            .Select(Path.GetFullPath)
            .Where(dir => dir.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(dir => !fs.IsReparsePoint(dir))
            .Where(dir => !HasReparsePointInDirectoryTree(dir, fs))
            .Where(dir => Directory.GetCreationTime(dir) < cutoff)
            .ToList();
        if (expired.Count == 0) return 0;
        if (confirmDelete is not null && !confirmDelete(expired.Count)) return 0;
        int removed = 0;
        foreach (var dir in expired)
        {
            Directory.Delete(dir, recursive: true);
            removed++;
        }
        return removed;
    }

    private static IFileSystem ResolveFileSystem(IFileSystem? fileSystem)
        => fileSystem ?? new FileSystemAdapter();

    private static void WriteTextSafely(string path, string content, IFileSystem? fileSystem = null)
    {
        var fs = ResolveFileSystem(fileSystem);
        var safePath = SafetyValidator.EnsureSafeOutputPath(path, allowUnc: false);
        var directory = Path.GetDirectoryName(safePath);
        if (!string.IsNullOrWhiteSpace(directory))
            fs.EnsureDirectory(directory);

        fs.WriteAllText(safePath, content);
    }

    private static string NormalizeBackupLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "backup";

        var invalidChars = Path.GetInvalidFileNameChars();
        var normalized = new string(label
            .Trim()
            .Select(c => invalidChars.Contains(c) ? '_' : c)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized) ? "backup" : normalized;
    }

    private static bool HasReparsePointInDirectoryTree(string directoryPath, IFileSystem fileSystem)
    {
        var stack = new Stack<string>();
        stack.Push(directoryPath);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (fileSystem.IsReparsePoint(current))
                return true;

            try
            {
                foreach (var subDirectory in Directory.GetDirectories(current))
                    stack.Push(subDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    // --- Shared helpers ---

    public static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    public static string? FindCommonRoot(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return null;
        var dirs = paths.Select(p => Path.GetDirectoryName(Path.GetFullPath(p)) ?? "").ToList();
        if (dirs.Count == 0) return null;
        var common = dirs[0];
        foreach (var dir in dirs.Skip(1))
        {
            while (!dir.StartsWith(common + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(dir, common, StringComparison.OrdinalIgnoreCase))
            {
                common = Path.GetDirectoryName(common) ?? "";
                if (common.Length == 0) return null;
            }
        }
        return common;
    }

    private static List<TrendSnapshot> LoadLegacyTrendHistory()
    {
        if (!File.Exists(TrendFile))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<TrendSnapshot>>(File.ReadAllText(TrendFile)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
