using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RomCleanup.Contracts.Models;
using RomCleanup.Core.Classification;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Tools;
using RomCleanup.Infrastructure.Reporting;

namespace RomCleanup.UI.Wpf.Services;

public static partial class FeatureService
{

    // ═══ HEADER ANALYSIS ════════════════════════════════════════════════
    // Port of HeaderAnalysis.ps1

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
        catch
        {
            return null;
        }
    }


    // ═══ TREND ANALYSIS ═════════════════════════════════════════════════
    // Port of TrendAnalysis.ps1

    private static readonly string TrendFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        RomCleanup.Contracts.AppIdentity.AppFolderName, "trend-history.json");


    public static void SaveTrendSnapshot(int totalFiles, long sizeBytes, int verified, int dupes, int junk)
    {
        var history = LoadTrendHistory();
        history.Add(new TrendSnapshot(DateTime.Now, totalFiles, sizeBytes, verified, dupes, junk,
            CalculateHealthScore(totalFiles, dupes, junk, verified)));
        if (history.Count > 365) history.RemoveRange(0, history.Count - 365);
        Directory.CreateDirectory(Path.GetDirectoryName(TrendFile)!);
        File.WriteAllText(TrendFile, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
    }


    public static List<TrendSnapshot> LoadTrendHistory()
    {
        if (!File.Exists(TrendFile)) return [];
        try { return JsonSerializer.Deserialize<List<TrendSnapshot>>(File.ReadAllText(TrendFile)) ?? []; }
        catch { return []; }
    }


    public static string FormatTrendReport(List<TrendSnapshot> history)
    {
        if (history.Count == 0) return "Keine Trend-Daten vorhanden.";
        var sb = new StringBuilder();
        sb.AppendLine("Trend-Analyse");
        sb.AppendLine(new string('═', 50));
        var latest = history[^1];
        sb.AppendLine($"Aktuell: {latest.TotalFiles} Dateien, {FormatSize(latest.SizeBytes)}, Quality={latest.QualityScore}%");

        if (history.Count >= 2)
        {
            var prev = history[^2];
            var fileDelta = latest.TotalFiles - prev.TotalFiles;
            var dupeDelta = latest.Dupes - prev.Dupes;
            sb.AppendLine($"Δ Dateien: {fileDelta:+#;-#;0}, Δ Duplikate: {dupeDelta:+#;-#;0}");
        }

        sb.AppendLine();
        sb.AppendLine("Verlauf (letzte 10):");
        foreach (var s in history.TakeLast(10))
            sb.AppendLine($"  {s.Timestamp:yyyy-MM-dd HH:mm} | {s.TotalFiles} Dateien | Q={s.QualityScore}%");
        return sb.ToString();
    }


    // ═══ INTEGRITY MONITOR ══════════════════════════════════════════════
    // Port of IntegrityMonitor.ps1
    // Baseline stores paths relative to the common root for portability.

    private static readonly string BaselinePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        RomCleanup.Contracts.AppIdentity.AppFolderName, "integrity-baseline.json");


    public static async Task<Dictionary<string, IntegrityEntry>> CreateBaseline(
        IReadOnlyList<string> filePaths, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (filePaths.Count == 0)
            return new Dictionary<string, IntegrityEntry>(StringComparer.OrdinalIgnoreCase);
        var commonRoot = FindCommonRoot(filePaths) ?? Path.GetDirectoryName(filePaths[0]) ?? "";
        var baseline = new System.Collections.Concurrent.ConcurrentDictionary<string, IntegrityEntry>(StringComparer.OrdinalIgnoreCase);
        int completed = 0;
        var total = filePaths.Count;

        await Parallel.ForEachAsync(filePaths, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
            async (path, token) =>
            {
                if (!File.Exists(path)) return;
                var fi = new FileInfo(path);
                var count = Interlocked.Increment(ref completed);
                progress?.Report($"Baseline: {count}/{total} – {Path.GetFileName(path)}");
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
            return new IntegrityCheckResult([], [], [], false, "Keine Baseline vorhanden. Erstellen Sie zuerst eine Baseline über 'Integrity-Baseline speichern'.");

        var json = File.ReadAllText(BaselinePath);
        Dictionary<string, IntegrityEntry> entries;
        string root;

        // Support both new (IntegrityBaseline wrapper) and legacy (flat dictionary) format
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
                // Legacy format: flat dictionary with absolute paths
                entries = JsonSerializer.Deserialize<Dictionary<string, IntegrityEntry>>(json) ?? [];
                root = "";
            }
        }
        catch
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
            progress?.Report($"Prüfe: {++i}/{entries.Count} – {Path.GetFileName(absPath)}");
            if (!File.Exists(absPath)) { missing.Add(absPath); continue; }
            var hash = await Task.Run(() => ComputeSha256(absPath), ct);
            if (hash != entry.Hash) changed.Add(absPath);
            else intact.Add(absPath);
        }

        return new IntegrityCheckResult(changed, missing, intact, changed.Count > 0);
    }


    // ═══ BACKUP MANAGER ═════════════════════════════════════════════════
    // Port of BackupManager.ps1

    public static string CreateBackup(IReadOnlyList<string> filePaths, string backupRoot, string label)
    {
        var sessionDir = Path.Combine(backupRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}_{label}");
        Directory.CreateDirectory(sessionDir);

        // Find common root to preserve relative directory structure
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


    internal static string? FindCommonRoot(IReadOnlyList<string> paths)
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


    // ═══ PATCH ENGINE ═══════════════════════════════════════════════════
    // Port of PatchEngine.ps1

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

}
