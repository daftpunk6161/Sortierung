using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RomCleanup.Contracts.Models;
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

            // NES (iNES): 4E 45 53 1A
            if (header.Length >= 16 && header[0] == 0x4E && header[1] == 0x45 &&
                header[2] == 0x53 && header[3] == 0x1A)
            {
                var isNes2 = (header[7] & 0x0C) == 0x08;
                return new RomHeaderInfo("NES", isNes2 ? "NES 2.0" : "iNES",
                    $"PRG={header[4] * 16}KB, CHR={header[5] * 8}KB, Mapper={(header[6] >> 4) | (header[7] & 0xF0)}");
            }

            // N64 Big-Endian: 80 37
            if (header.Length >= 0x40 && header[0] == 0x80 && header[1] == 0x37)
            {
                var title = Encoding.ASCII.GetString(header, 0x20, 20).TrimEnd('\0', ' ');
                return new RomHeaderInfo("N64", "Big-Endian (.z64)", $"Title={title}");
            }

            // N64 Byte-Swap: 37 80
            if (header.Length >= 0x40 && header[0] == 0x37 && header[1] == 0x80)
                return new RomHeaderInfo("N64", "Byte-Swapped (.v64)", "");

            // N64 Little-Endian: 40 12
            if (header.Length >= 0x40 && header[0] == 0x40 && header[1] == 0x12)
                return new RomHeaderInfo("N64", "Little-Endian (.n64)", "");

            // GBA: 0x96 at offset 0xB2
            if (header.Length >= 0xBE && header[0xB2] == 0x96)
            {
                var title = Encoding.ASCII.GetString(header, 0xA0, 12).TrimEnd('\0', ' ');
                var code = Encoding.ASCII.GetString(header, 0xAC, 4).TrimEnd('\0');
                return new RomHeaderInfo("GBA", "GBA ROM", $"Title={title}, Code={code}");
            }

            // SNES LoROM (header at 0x7FC0)
            if (header.Length >= 0x8000)
            {
                var snesTitle = Encoding.ASCII.GetString(header, 0x7FC0, 21).TrimEnd('\0', ' ');
                // Validate SNES checksum complement: checksum + complement must equal 0xFFFF
                int checksum = header[0x7FDE] | (header[0x7FDF] << 8);
                int complement = header[0x7FDC] | (header[0x7FDD] << 8);
                if (snesTitle.Length > 0 && snesTitle.All(c => c >= 0x20 && c <= 0x7E) &&
                    (checksum + complement) == 0xFFFF)
                    return new RomHeaderInfo("SNES", "LoROM", $"Title={snesTitle}");
            }

            // SNES HiROM (header at 0xFFC0)
            if (header.Length >= 0x10000)
            {
                var snesTitle = Encoding.ASCII.GetString(header, 0xFFC0, 21).TrimEnd('\0', ' ');
                int checksum = header[0xFFDE] | (header[0xFFDF] << 8);
                int complement = header[0xFFDC] | (header[0xFFDD] << 8);
                if (snesTitle.Length > 0 && snesTitle.All(c => c >= 0x20 && c <= 0x7E) &&
                    (checksum + complement) == 0xFFFF)
                    return new RomHeaderInfo("SNES", "HiROM", $"Title={snesTitle}");
            }

            return new RomHeaderInfo("Unbekannt", "Unbekanntes Format", $"Magic: {header[0]:X2} {header[1]:X2} {header[2]:X2} {header[3]:X2}");
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
        "RomCleanupRegionDedupe", "trend-history.json");


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
        "RomCleanupRegionDedupe", "integrity-baseline.json");


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


    // ═══ NES HEADER REPAIR ═════════════════════════════════════════════
    // Check if NES ROM has dirty bytes at offset 12-15. If so, zero them.

    public static bool RepairNesHeader(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

            if (fs.Length < 16)
                return false;

            var header = new byte[16];
            var read = fs.Read(header, 0, header.Length);
            if (read < 16)
                return false;

            // Verify iNES magic: 4E 45 53 1A
            if (header[0] != 0x4E || header[1] != 0x45 || header[2] != 0x53 || header[3] != 0x1A)
                return false;

            // Check if bytes 12-15 are dirty (non-zero)
            bool dirty = false;
            for (int i = 12; i <= 15; i++)
            {
                if (header[i] != 0x00)
                { dirty = true; break; }
            }

            if (!dirty) return false;

            // Create backup
            File.Copy(path, path + ".bak", overwrite: true);

            // Zero bytes 12-15 in place (streaming-safe for large files).
            fs.Seek(12, SeekOrigin.Begin);
            var zeroBytes = new byte[] { 0x00, 0x00, 0x00, 0x00 };
            fs.Write(zeroBytes, 0, zeroBytes.Length);
            fs.Flush();
            return true;
        }
        catch { return false; }
    }


    // ═══ COPIER HEADER REMOVAL ═════════════════════════════════════════
    // Check if SNES ROM has a 512-byte copier header and remove it.

    public static bool RemoveCopierHeader(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;

        try
        {
            var fi = new FileInfo(path);
            if (fi.Length < 512 || fi.Length % 1024 != 512)
                return false;

            // Create backup
            File.Copy(path, path + ".bak", overwrite: true);

            // Read file, skip first 512 bytes, write back
            byte[] data = File.ReadAllBytes(path);
            byte[] stripped = new byte[data.Length - 512];
            Array.Copy(data, 512, stripped, 0, stripped.Length);
            File.WriteAllBytes(path, stripped);
            return true;
        }
        catch { return false; }
    }

}
