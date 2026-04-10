using System.Text;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Scoring;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Index;

namespace Romulus.Infrastructure.Analysis;

/// <summary>
/// Collection analysis operations extracted from FeatureService.
/// Pure logic + file I/O, no GUI dependency.
/// </summary>
public static class CollectionAnalysisService
{
    public static int CalculateHealthScore(int totalFiles, int dupes, int junk, int verified)
        => HealthScorer.GetHealthScore(totalFiles, dupes, junk, verified);

    public static List<HeatmapEntry> GetDuplicateHeatmap(IReadOnlyList<DedupeGroup> groups)
    {
        var consoleMap = new Dictionary<string, (int total, int dupes)>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            var console = ResolveConsoleLabel(g.Winner);
            if (!consoleMap.TryGetValue(console, out var val))
                val = (0, 0);
            val.total += 1 + g.Losers.Count;
            val.dupes += g.Losers.Count;
            consoleMap[console] = val;
        }

        return consoleMap
            .Select(kv => new HeatmapEntry(kv.Key, kv.Value.total, kv.Value.dupes,
                kv.Value.total > 0 ? 100.0 * kv.Value.dupes / kv.Value.total : 0))
            .OrderByDescending(h => h.Duplicates)
            .ToList();
    }

    public static List<DuplicateSourceEntry> GetDuplicateInspector(string? auditPath)
    {
        if (string.IsNullOrEmpty(auditPath) || !File.Exists(auditPath))
            return [];

        var dirCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(auditPath, Encoding.UTF8).Skip(1))
        {
            var fields = AuditCsvParser.ParseCsvLine(line);
            if (fields.Length < 5) continue;
            var action = fields[3];
            if (!action.Equals("MOVE", StringComparison.OrdinalIgnoreCase) &&
                !action.Equals("SKIP_DRYRUN", StringComparison.OrdinalIgnoreCase))
                continue;
            var dir = Path.GetDirectoryName(fields[1]) ?? "";
            dirCounts[dir] = dirCounts.GetValueOrDefault(dir) + 1;
        }

        return dirCounts
            .OrderByDescending(kv => kv.Value)
            .Take(8)
            .Select(kv => new DuplicateSourceEntry(kv.Key, kv.Value))
            .ToList();
    }

    public static List<RomCandidate> SearchRomCollection(IReadOnlyList<RomCandidate> candidates, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return candidates.ToList();
        return candidates.Where(c =>
            c.MainPath.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            c.GameKey.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            c.Region.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            ToCategoryLabel(c.Category).Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            c.Extension.Contains(searchText, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }

    public static string AnalyzeStorageTiers(IReadOnlyList<RomCandidate> candidates, int hotThresholdDays = 30, TimeProvider? timeProvider = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Storage Tiering Analysis");
        sb.AppendLine(new string('=', 50));

        long hotSize = 0, coldSize = 0;
        int hotCount = 0, coldCount = 0;
        var now = (timeProvider ?? TimeProvider.System).GetLocalNow().DateTime;

        foreach (var c in candidates)
        {
            var fi = new FileInfo(c.MainPath);
            if (!fi.Exists) continue;
            var daysSince = (now - fi.LastAccessTime).TotalDays;
            if (daysSince <= hotThresholdDays)
            { hotSize += c.SizeBytes; hotCount++; }
            else
            { coldSize += c.SizeBytes; coldCount++; }
        }

        sb.AppendLine($"  Hot (<={hotThresholdDays}d): {hotCount} files, {Formatting.FormatSize(hotSize)}");
        sb.AppendLine($"  Cold (>{hotThresholdDays}d): {coldCount} files, {Formatting.FormatSize(coldSize)}");
        sb.AppendLine($"\n  Recommendation: Move cold files to HDD/NAS -> {Formatting.FormatSize(coldSize)} SSD space freed");
        return sb.ToString();
    }

    public static string GetHardlinkEstimate(IReadOnlyList<DedupeGroup> groups)
    {
        long savedBytes = 0;
        int linkCount = 0;
        foreach (var g in groups)
        {
            foreach (var l in g.Losers)
            {
                savedBytes += l.SizeBytes;
                linkCount++;
            }
        }
        return $"Hardlink-Modus: {linkCount} Links möglich, {Formatting.FormatSize(savedBytes)} Speicher sparbar (100% Effizienz auf NTFS)";
    }

    public static string GetNasInfo(IReadOnlyList<string> roots)
    {
        var sb = new StringBuilder();
        sb.AppendLine("NAS Optimization");
        sb.AppendLine(new string('=', 50));
        foreach (var root in roots)
        {
            var isUncPath = root.StartsWith(@"\\") || root.StartsWith("//");
            var isMappedNetworkDrive = false;

            if (!isUncPath && root.Length >= 2 && root[1] == ':')
            {
                try
                {
                    var driveInfo = new DriveInfo(root[..1]);
                    if (driveInfo.DriveType == DriveType.Network)
                        isMappedNetworkDrive = true;
                }
                catch { /* DriveInfo may fail on disconnected drives */ }
            }

            var isNetwork = isUncPath || isMappedNetworkDrive;
            sb.AppendLine($"\n  {root}");
            if (isMappedNetworkDrive)
                sb.AppendLine($"    Type: Mapped network drive");
            else if (isUncPath)
                sb.AppendLine($"    Type: UNC network path");
            else
                sb.AppendLine($"    Type: Local drive");
            sb.AppendLine($"    Network path: {(isNetwork ? "Yes" : "No")}");
            if (isNetwork)
            {
                sb.AppendLine("    Recommendations:");
                sb.AppendLine("      - Reduce batch size (max 500 files/batch)");
                sb.AppendLine("      - Limit hashing threads (max 2 for SMB)");
                sb.AppendLine("      - Store audit/reports locally (not on NAS)");
                sb.AppendLine("      - Throttling: Medium (200ms delay)");
                sb.AppendLine("      - UNC path preferred over drive letter (more stable)");
            }
            else
            {
                sb.AppendLine("    Recommendation: Maximum parallelism possible");
            }

            try
            {
                if (!Directory.Exists(root))
                    sb.AppendLine("    WARNING: Path not reachable!");
            }
            catch
            {
                sb.AppendLine("    WARNING: Access check failed!");
            }
        }
        return sb.ToString();
    }

    public static string BuildCloneTree(IReadOnlyList<DedupeGroup> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Parent/Clone-Baum");
        sb.AppendLine(new string('=', 50));
        foreach (var g in groups.Take(50))
        {
            sb.AppendLine($"\n  > {g.GameKey} (Winner)");
            sb.AppendLine($"    {Path.GetFileName(g.Winner.MainPath)} [{g.Winner.Region}] {g.Winner.Extension}");
            foreach (var l in g.Losers)
                sb.AppendLine($"    +-- {Path.GetFileName(l.MainPath)} [{l.Region}] {l.Extension}");
        }
        if (groups.Count > 50)
            sb.AppendLine($"\n  ... und {groups.Count - 50} weitere Gruppen");
        return sb.ToString();
    }

    public static string BuildVirtualFolderPreview(IReadOnlyList<RomCandidate> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Virtuelle Ordner-Vorschau");
        sb.AppendLine(new string('=', 50));

        var byConsole = candidates.GroupBy(ResolveConsoleLabel)
            .OrderBy(g => g.Key);

        foreach (var group in byConsole)
        {
            var total = group.Sum(c => c.SizeBytes);
            sb.AppendLine($"\n  [{group.Key}] ({group.Count()} files, {Formatting.FormatSize(total)})");
            var byRegion = group.GroupBy(c => c.Region).OrderByDescending(g => g.Count());
            foreach (var rg in byRegion.Take(5))
                sb.AppendLine($"      [{rg.Key}] {rg.Count()} files");
        }
        return sb.ToString();
    }

    public static async ValueTask<ScopedCandidateLoadResult> TryLoadScopedCandidatesFromCollectionIndexAsync(
        ICollectionIndex? collectionIndex,
        IFileSystem fileSystem,
        IReadOnlyList<string> roots,
        IReadOnlyCollection<string> extensions,
        string? enrichmentFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(extensions);

        var materialized = await CollectionCompareService.TryMaterializeSourceAsync(
            collectionIndex,
            fileSystem,
            new CollectionSourceScope
            {
                SourceId = "scope",
                Label = "Scope",
                Roots = roots.ToArray(),
                Extensions = extensions.ToArray(),
                EnrichmentFingerprint = enrichmentFingerprint ?? string.Empty
            },
            ct).ConfigureAwait(false);

        if (!materialized.CanUse)
        {
            if (!string.IsNullOrWhiteSpace(enrichmentFingerprint)
                && materialized.Reason is not null
                && materialized.Reason.Contains("mixed enrichment fingerprints", StringComparison.OrdinalIgnoreCase))
            {
                return ScopedCandidateLoadResult.Unavailable("collection index fingerprint mismatch");
            }

            return ScopedCandidateLoadResult.Unavailable(materialized.Reason ?? "collection index unavailable");
        }

        return ScopedCandidateLoadResult.Success(
            CollectionCompareService.MaterializeCandidates(materialized.Entries),
            materialized.Source);
    }

    public static string ExportRetroArchPlaylist(IReadOnlyList<RomCandidate> winners, string playlistName,
        IReadOnlyDictionary<string, string>? coreMapping = null)
    {
        coreMapping ??= DefaultCoreMapping;
        var entries = new List<object>();
        foreach (var w in winners)
        {
            var console = ResolveConsoleLabel(w).ToLowerInvariant();
            var core = coreMapping.GetValueOrDefault(console, "");
            entries.Add(new
            {
                path = w.MainPath.Replace('\\', '/'),
                label = Path.GetFileNameWithoutExtension(w.MainPath),
                core_path = core,
                core_name = core.Replace("_libretro", ""),
                db_name = playlistName + ".lpl"
            });
        }
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            version = "1.5",
            default_core_path = "",
            default_core_name = "",
            items = entries
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    internal static readonly IReadOnlyDictionary<string, string> DefaultCoreMapping =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nes"] = "mesen_libretro", ["snes"] = "snes9x_libretro", ["n64"] = "mupen64plus_next_libretro",
            ["gb"] = "gambatte_libretro", ["gbc"] = "gambatte_libretro", ["gba"] = "mgba_libretro",
            ["nds"] = "melonds_libretro", ["ps1"] = "mednafen_psx_hw_libretro", ["ps2"] = "pcsx2_libretro",
            ["psp"] = "ppsspp_libretro", ["gc"] = "dolphin_libretro", ["wii"] = "dolphin_libretro",
            ["genesis"] = "genesis_plus_gx_libretro", ["arcade"] = "fbneo_libretro",
            ["dreamcast"] = "flycast_libretro", ["saturn"] = "mednafen_saturn_libretro"
        };

    // --- Shared helpers ---

    public static string ToCategoryLabel(FileCategory category) => category switch
    {
        FileCategory.Game => "GAME",
        FileCategory.Bios => "BIOS",
        FileCategory.Junk => "JUNK",
        _ => "UNKNOWN"
    };

    public static string ResolveConsoleLabel(RomCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return ResolveConsoleLabel(candidate.ConsoleKey, candidate.MainPath);
    }

    public static string ResolveConsoleLabel(string? consoleKey, string path)
    {
        var detectedFromPath = DetectConsoleFromPath(path);
        if (!HasResolvedConsoleKey(consoleKey))
            return detectedFromPath;

        var normalizedConsoleKey = consoleKey!.Trim();
        return string.Equals(detectedFromPath, normalizedConsoleKey, StringComparison.OrdinalIgnoreCase)
            ? detectedFromPath
            : normalizedConsoleKey;
    }

    public static string DetectConsoleFromPath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^2] : "Unknown";
    }

    private static bool HasResolvedConsoleKey(string? consoleKey)
        => !string.IsNullOrWhiteSpace(consoleKey)
           && !string.Equals(consoleKey, "UNKNOWN", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(consoleKey, "AMBIGUOUS", StringComparison.OrdinalIgnoreCase);
}

public sealed record ScopedCandidateLoadResult(
    bool CanUse,
    IReadOnlyList<RomCandidate> Candidates,
    string Source,
    string? Reason = null)
{
    public static ScopedCandidateLoadResult Success(IReadOnlyList<RomCandidate> candidates, string source)
        => new(true, candidates, source);

    public static ScopedCandidateLoadResult Unavailable(string reason)
        => new(false, Array.Empty<RomCandidate>(), ScopedCandidateSources.FallbackRun, reason);
}

public static class ScopedCandidateSources
{
    public const string CollectionIndex = CollectionMaterializationSources.CollectionIndex;
    public const string EmptyScope = CollectionMaterializationSources.EmptyScope;
    public const string FallbackRun = CollectionMaterializationSources.FallbackRun;
}
