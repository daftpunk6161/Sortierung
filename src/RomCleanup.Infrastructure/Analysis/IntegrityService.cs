using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RomCleanup.Contracts;
using RomCleanup.Contracts.Models;
using RomCleanup.Core.Classification;
using RomCleanup.Infrastructure.Index;

namespace RomCleanup.Infrastructure.Analysis;

/// <summary>
/// Integrity monitoring and header analysis extracted from FeatureService.Security.
/// Pure logic + file I/O, no GUI dependency.
/// </summary>
public static class IntegrityService
{
    private static readonly string TrendFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppIdentity.AppFolderName, "trend-history.json");

    private static readonly string BaselinePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppIdentity.AppFolderName, "integrity-baseline.json");

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

    public static void SaveTrendSnapshot(int totalFiles, long sizeBytes, int verified, int dupes, int junk)
    {
        var history = LoadLegacyTrendHistory();
        history.Add(new TrendSnapshot(DateTime.Now, totalFiles, sizeBytes, verified, dupes, junk,
            CollectionAnalysisService.CalculateHealthScore(totalFiles, dupes, junk, verified)));
        if (history.Count > 365) history.RemoveRange(0, history.Count - 365);
        Directory.CreateDirectory(Path.GetDirectoryName(TrendFile)!);
        File.WriteAllText(TrendFile, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
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
        IReadOnlyList<string> filePaths, IProgress<string>? progress = null, CancellationToken ct = default)
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

        var entries = new Dictionary<string, IntegrityEntry>(baseline, StringComparer.OrdinalIgnoreCase);
        var wrapper = new IntegrityBaseline(commonRoot, entries);
        Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath)!);
        File.WriteAllText(BaselinePath, JsonSerializer.Serialize(wrapper, new JsonSerializerOptions { WriteIndented = true }));
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

    public static string CreateBackup(IReadOnlyList<string> filePaths, string backupRoot, string label)
    {
        var sessionDir = Path.Combine(backupRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}_{label}");
        Directory.CreateDirectory(sessionDir);

        var commonRoot = FindCommonRoot(filePaths);

        foreach (var path in filePaths)
        {
            if (!File.Exists(path)) continue;
            var relativePath = commonRoot is not null
                ? Path.GetRelativePath(commonRoot, path)
                : Path.GetFileName(path);
            var dest = Path.Combine(sessionDir, relativePath);
            var destDir = Path.GetDirectoryName(dest);
            if (destDir is not null) Directory.CreateDirectory(destDir);
            File.Copy(path, dest, overwrite: false);
        }
        return sessionDir;
    }

    public static int CleanupOldBackups(string backupRoot, int retentionDays, Func<int, bool>? confirmDelete = null)
    {
        if (!Directory.Exists(backupRoot)) return 0;
        var cutoff = DateTime.Now.AddDays(-retentionDays);
        var expired = Directory.GetDirectories(backupRoot)
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

    // --- Patch Detection ---

    public static string? DetectPatchFormat(string patchPath)
    {
        if (!File.Exists(patchPath)) return null;
        using var fs = File.OpenRead(patchPath);
        var magic = new byte[5];
        if (fs.Read(magic, 0, 5) < 5) return null;
        if (magic[0] == 'P' && magic[1] == 'A' && magic[2] == 'T' && magic[3] == 'C' && magic[4] == 'H') return "IPS";
        if (magic[0] == 'B' && magic[1] == 'P' && magic[2] == 'S' && magic[3] == '1') return "BPS";
        if (magic[0] == 'U' && magic[1] == 'P' && magic[2] == 'S' && magic[3] == '1') return "UPS";
        return null;
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
