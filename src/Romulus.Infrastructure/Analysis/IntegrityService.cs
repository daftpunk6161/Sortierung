using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Paths;
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

        var entries = baseline
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
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

    public static string CreateBackup(IReadOnlyList<string> filePaths, string backupRoot, string label, TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetLocalNow().DateTime;
        var sessionDir = Path.Combine(backupRoot, $"{now:yyyyMMdd-HHmmss}_{label}");
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

    public static int CleanupOldBackups(string backupRoot, int retentionDays, Func<int, bool>? confirmDelete = null, TimeProvider? timeProvider = null)
    {
        if (!Directory.Exists(backupRoot)) return 0;
        var cutoff = (timeProvider ?? TimeProvider.System).GetLocalNow().DateTime.AddDays(-retentionDays);
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

    public static PatchApplyResult ApplyPatch(
        string sourceRomPath,
        string patchPath,
        string outputPath,
        IToolRunner? toolRunner = null,
        string? flipsToolPath = null,
        string? xdeltaToolPath = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRomPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(patchPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        sourceRomPath = Path.GetFullPath(sourceRomPath);
        patchPath = Path.GetFullPath(patchPath);
        outputPath = Path.GetFullPath(outputPath);

        if (!File.Exists(sourceRomPath))
            throw new InvalidOperationException($"Source ROM not found: {sourceRomPath}");
        if (!File.Exists(patchPath))
            throw new InvalidOperationException($"Patch file not found: {patchPath}");

        var format = ResolvePatchFormat(patchPath);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        var runner = toolRunner ?? new ToolRunnerAdapter();
        string? toolPath = null;

        switch (format)
        {
            case "IPS":
                ApplyIpsPatch(sourceRomPath, patchPath, outputPath);
                break;
            case "BPS":
            case "UPS":
                toolPath = ApplyViaFlips(runner, sourceRomPath, patchPath, outputPath, flipsToolPath, format, ct);
                break;
            case "XDELTA":
                toolPath = ApplyViaXdelta(runner, sourceRomPath, patchPath, outputPath, xdeltaToolPath, ct);
                break;
            default:
                throw new InvalidOperationException($"Unsupported patch format '{format}'.");
        }

        if (!File.Exists(outputPath))
            throw new InvalidOperationException("Patch output was not created.");

        var outputInfo = new FileInfo(outputPath);
        if (outputInfo.Length == 0)
            throw new InvalidOperationException("Patch output is empty.");

        return new PatchApplyResult(
            format,
            sourceRomPath,
            patchPath,
            outputPath,
            outputInfo.Length,
            ComputeSha256(outputPath),
            toolPath);
    }

    internal static string ResolvePatchFormat(string patchPath)
    {
        var detected = DetectPatchFormat(patchPath);
        if (!string.IsNullOrWhiteSpace(detected))
            return detected;

        var extension = Path.GetExtension(patchPath).ToLowerInvariant();
        return extension switch
        {
            ".ips" => "IPS",
            ".bps" => "BPS",
            ".ups" => "UPS",
            ".xdelta" or ".xdelta3" or ".vcdiff" => "XDELTA",
            _ => throw new InvalidOperationException($"Unsupported patch format for file '{patchPath}'.")
        };
    }

    private static string ApplyViaFlips(
        IToolRunner toolRunner,
        string sourceRomPath,
        string patchPath,
        string outputPath,
        string? flipsToolPath,
        string format,
        CancellationToken ct)
    {
        var toolPath = ResolveToolPath(toolRunner, flipsToolPath, "flips", "BPS/UPS patches require 'flips.exe'.");
        var result = toolRunner.InvokeProcess(
            toolPath,
            ["--apply", patchPath, sourceRomPath, outputPath],
            errorLabel: $"{format} patch",
            timeout: TimeSpan.FromMinutes(10),
            cancellationToken: ct);
        if (!result.Success)
            throw new InvalidOperationException($"{format} patch failed: {result.Output}");

        return toolPath;
    }

    private static string ApplyViaXdelta(
        IToolRunner toolRunner,
        string sourceRomPath,
        string patchPath,
        string outputPath,
        string? xdeltaToolPath,
        CancellationToken ct)
    {
        var toolPath = ResolveToolPath(toolRunner, xdeltaToolPath, "xdelta3", "xdelta patches require 'xdelta3.exe'.");
        var result = toolRunner.InvokeProcess(
            toolPath,
            ["-d", "-s", sourceRomPath, patchPath, outputPath],
            errorLabel: "xdelta patch",
            timeout: TimeSpan.FromMinutes(10),
            cancellationToken: ct);
        if (!result.Success)
            throw new InvalidOperationException($"xdelta patch failed: {result.Output}");

        return toolPath;
    }

    private static string ResolveToolPath(
        IToolRunner toolRunner,
        string? configuredPath,
        string toolName,
        string missingToolMessage)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = Path.GetFullPath(configuredPath);
            if (!File.Exists(fullPath))
                throw new InvalidOperationException($"Configured tool path does not exist: {fullPath}");

            return fullPath;
        }

        var discovered = toolRunner.FindTool(toolName);
        if (string.IsNullOrWhiteSpace(discovered) || !File.Exists(discovered))
            throw new InvalidOperationException(missingToolMessage);

        return discovered;
    }

    private static void ApplyIpsPatch(string sourceRomPath, string patchPath, string outputPath)
    {
        var source = File.ReadAllBytes(sourceRomPath);
        var output = new List<byte>(source);

        using var fs = File.OpenRead(patchPath);
        using var reader = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

        var header = reader.ReadBytes(5);
        if (header.Length != 5 || header[0] != 'P' || header[1] != 'A' || header[2] != 'T' || header[3] != 'C' || header[4] != 'H')
            throw new InvalidOperationException("Invalid IPS patch header.");

        while (fs.Position < fs.Length)
        {
            var offsetBytes = reader.ReadBytes(3);
            if (offsetBytes.Length != 3)
                throw new InvalidOperationException("Invalid IPS patch record: truncated offset.");

            if (offsetBytes[0] == 'E' && offsetBytes[1] == 'O' && offsetBytes[2] == 'F')
            {
                var remaining = fs.Length - fs.Position;
                if (remaining == 0)
                    break;
                if (remaining < 3)
                    throw new InvalidOperationException("Invalid IPS patch footer.");

                var targetSize = ReadUInt24BigEndian(reader);
                EnsureOutputLength(output, targetSize);
                if (targetSize < output.Count)
                    output.RemoveRange(targetSize, output.Count - targetSize);
                break;
            }

            var offset = (offsetBytes[0] << 16) | (offsetBytes[1] << 8) | offsetBytes[2];
            var size = ReadUInt16BigEndian(reader);
            if (size == 0)
            {
                var rleSize = ReadUInt16BigEndian(reader);
                var value = reader.ReadByte();
                EnsureOutputLength(output, offset + rleSize);
                for (var i = 0; i < rleSize; i++)
                    output[offset + i] = value;
                continue;
            }

            var patchData = reader.ReadBytes(size);
            if (patchData.Length != size)
                throw new InvalidOperationException("Invalid IPS patch record: truncated payload.");

            EnsureOutputLength(output, offset + size);
            for (var i = 0; i < patchData.Length; i++)
                output[offset + i] = patchData[i];
        }

        File.WriteAllBytes(outputPath, output.ToArray());
    }

    internal static int ReadUInt16BigEndian(BinaryReader reader)
    {
        var high = reader.ReadByte();
        var low = reader.ReadByte();
        return (high << 8) | low;
    }

    internal static int ReadUInt24BigEndian(BinaryReader reader)
    {
        var b0 = reader.ReadByte();
        var b1 = reader.ReadByte();
        var b2 = reader.ReadByte();
        return (b0 << 16) | (b1 << 8) | b2;
    }

    internal static void EnsureOutputLength(List<byte> output, int targetLength)
    {
        if (targetLength <= output.Count)
            return;

        output.AddRange(new byte[targetLength - output.Count]);
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
