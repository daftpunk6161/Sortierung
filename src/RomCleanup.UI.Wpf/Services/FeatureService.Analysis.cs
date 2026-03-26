using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RomCleanup.Contracts.Models;
using RomCleanup.Core.Scoring;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Tools;
using RomCleanup.Infrastructure.Reporting;

namespace RomCleanup.UI.Wpf.Services;

public static partial class FeatureService
{

    // ═══ HEALTH SCORE ═══════════════════════════════════════════════════
    // Port of WpfSlice.ReportPreview.ps1

    public static int CalculateHealthScore(int totalFiles, int dupes, int junk, int verified)
        => HealthScorer.GetHealthScore(totalFiles, dupes, junk, verified);


    // ═══ DUPLICATE HEATMAP ══════════════════════════════════════════════
    // Port of DuplicateHeatmap.ps1

    public static List<HeatmapEntry> GetDuplicateHeatmap(IReadOnlyList<DedupeGroup> groups)
    {
        var consoleMap = new Dictionary<string, (int total, int dupes)>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            var console = DetectConsoleFromPath(g.Winner.MainPath);
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


    // ═══ DUPLICATE INSPECTOR ════════════════════════════════════════════
    // Port of WpfSlice.ReportPreview.ps1 - btnDuplicateInspector

    public static List<DuplicateSourceEntry> GetDuplicateInspector(string? auditPath)
    {
        if (string.IsNullOrEmpty(auditPath) || !File.Exists(auditPath))
            return [];

        var dirCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(auditPath, Encoding.UTF8).Skip(1)) // skip header
        {
            var fields = ParseCsvLine(line);
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


    // ═══ ROM FILTER ═════════════════════════════════════════════════════
    // Port of RomFilter.ps1

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


    // ═══ STORAGE TIERING ════════════════════════════════════════════════
    // Port of StorageTiering.ps1

    public static string AnalyzeStorageTiers(IReadOnlyList<RomCandidate> candidates, int hotThresholdDays = 30)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Storage-Tiering-Analyse");
        sb.AppendLine(new string('═', 50));

        long hotSize = 0, coldSize = 0;
        int hotCount = 0, coldCount = 0;
        var now = DateTime.Now;

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

        sb.AppendLine($"  Hot (≤{hotThresholdDays}d): {hotCount} Dateien, {FormatSize(hotSize)}");
        sb.AppendLine($"  Cold (>{hotThresholdDays}d): {coldCount} Dateien, {FormatSize(coldSize)}");
        sb.AppendLine($"\n  Empfehlung: Cold-Dateien auf HDD/NAS verschieben → {FormatSize(coldSize)} SSD-Platz frei");
        return sb.ToString();
    }


    // ═══ HARDLINK ESTIMATE ══════════════════════════════════════════════
    // Port of HardlinkMode.ps1

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
        return $"Hardlink-Modus: {linkCount} Links möglich, {FormatSize(savedBytes)} Speicher gespart (100% Effizienz bei NTFS)";
    }


    // ═══ NAS OPTIMIZATION ═══════════════════════════════════════════════
    // Port of NasOptimization.ps1

    public static string GetNasInfo(IReadOnlyList<string> roots)
    {
        var sb = new StringBuilder();
        sb.AppendLine("NAS-Optimierung");
        sb.AppendLine(new string('═', 50));
        foreach (var root in roots)
        {
            var isUncPath = root.StartsWith(@"\\") || root.StartsWith("//");
            var isMappedNetworkDrive = false;
            string? uncResolved = null;

            // Detect mapped network drives (e.g. W:\ → \\server\share)
            if (!isUncPath && root.Length >= 2 && root[1] == ':')
            {
                try
                {
                    var driveInfo = new DriveInfo(root[..1]);
                    if (driveInfo.DriveType == DriveType.Network)
                    {
                        isMappedNetworkDrive = true;
                        // Try to resolve UNC path from mapped drive
                        var fullPath = Path.GetFullPath(root);
                        uncResolved = fullPath.StartsWith(@"\\") ? fullPath : null;
                    }
                }
                catch { /* DriveInfo may fail on disconnected drives */ }
            }

            var isNetwork = isUncPath || isMappedNetworkDrive;
            sb.AppendLine($"\n  {root}");
            if (isMappedNetworkDrive)
                sb.AppendLine($"    Typ: Zugeordnetes Netzlaufwerk{(uncResolved != null ? $" → {uncResolved}" : "")}");
            else if (isUncPath)
                sb.AppendLine($"    Typ: UNC-Netzwerkpfad");
            else
                sb.AppendLine($"    Typ: Lokales Laufwerk");
            sb.AppendLine($"    Netzwerk-Pfad: {(isNetwork ? "Ja" : "Nein")}");
            if (isNetwork)
            {
                sb.AppendLine("    Empfehlungen:");
                sb.AppendLine("      • Batch-Größe reduzieren (max 500 Dateien/Batch)");
                sb.AppendLine("      • Hashing-Threads begrenzen (max 2 für SMB)");
                sb.AppendLine("      • Audit/Reports lokal speichern (nicht auf NAS)");
                sb.AppendLine("      • Throttling: Medium (200ms Verzögerung)");
                sb.AppendLine("      • UNC-Pfad statt Laufwerksbuchstabe empfohlen (stabiler)");
            }
            else
            {
                sb.AppendLine("    Empfehlung: Maximale Parallelität möglich");
            }

            // Check accessibility
            try
            {
                if (!Directory.Exists(root))
                    sb.AppendLine("    ⚠ WARNUNG: Pfad nicht erreichbar!");
            }
            catch
            {
                sb.AppendLine("    ⚠ WARNUNG: Zugriffsprüfung fehlgeschlagen!");
            }
        }
        return sb.ToString();
    }


    // ═══ CLONE LIST VIEWER ══════════════════════════════════════════════
    // Port of CloneListViewer.ps1

    public static string BuildCloneTree(IReadOnlyList<DedupeGroup> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Parent/Clone-Baum");
        sb.AppendLine(new string('═', 50));
        foreach (var g in groups.Take(50))
        {
            sb.AppendLine($"\n  ► {g.GameKey} (Winner)");
            sb.AppendLine($"    {Path.GetFileName(g.Winner.MainPath)} [{g.Winner.Region}] {g.Winner.Extension}");
            foreach (var l in g.Losers)
                sb.AppendLine($"    └─ {Path.GetFileName(l.MainPath)} [{l.Region}] {l.Extension}");
        }
        if (groups.Count > 50)
            sb.AppendLine($"\n  … und {groups.Count - 50} weitere Gruppen");
        return sb.ToString();
    }


    // ═══ VIRTUAL FOLDER PREVIEW ═════════════════════════════════════════
    // Port of VirtualFolderPreview.ps1

    public static string BuildVirtualFolderPreview(IReadOnlyList<RomCandidate> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Virtuelle Ordner-Vorschau");
        sb.AppendLine(new string('═', 50));

        var byConsole = candidates.GroupBy(c => DetectConsoleFromPath(c.MainPath))
            .OrderBy(g => g.Key);

        foreach (var group in byConsole)
        {
            var total = group.Sum(c => c.SizeBytes);
            sb.AppendLine($"\n  📁 {group.Key} ({group.Count()} Dateien, {FormatSize(total)})");
            var byRegion = group.GroupBy(c => c.Region).OrderByDescending(g => g.Count());
            foreach (var rg in byRegion.Take(5))
                sb.AppendLine($"      [{rg.Key}] {rg.Count()} Dateien");
        }
        return sb.ToString();
    }


    // ═══ COMMAND PALETTE ════════════════════════════════════════════════

    /// <summary>Core VM-level shortcuts that are not registered as FeatureCommands.</summary>
    internal static readonly (string key, string name, string shortcut)[] CoreShortcuts =
    [
        ("dryrun",    "DryRun starten",         "Ctrl+D"),
        ("move",      "Move ausführen",         "Ctrl+M"),
        ("cancel",    "Lauf abbrechen",         "Escape"),
        ("rollback",  "Rollback ausführen",     "Ctrl+Z"),
        ("theme",     "Theme wechseln",         "Ctrl+T"),
        ("clear-log", "Log leeren",             "Ctrl+L"),
        ("settings",  "Einstellungen öffnen",   "Ctrl+,")
    ];

    /// <summary>
    /// Searches all registered FeatureCommands + CoreShortcuts for a query.
    /// Returns matching entries ordered by relevance (exact substring first, then Levenshtein distance).
    /// </summary>
    public static List<(string key, string name, string shortcut, int score)> SearchCommands(
        string query, IReadOnlyDictionary<string, System.Windows.Input.ICommand>? featureCommands = null)
    {
        // Build the searchable command list from FeatureCommands + CoreShortcuts
        var allCommands = new List<(string key, string name, string shortcut)>();

        if (featureCommands is not null)
        {
            foreach (var kvp in featureCommands)
                allCommands.Add((kvp.Key, kvp.Key, ""));
        }

        foreach (var cs in CoreShortcuts)
            allCommands.Add(cs);

        if (string.IsNullOrWhiteSpace(query))
            return allCommands.Select(c => (c.key, c.name, c.shortcut, 0)).ToList();

        // Limit query length to prevent excessive Levenshtein matrix allocation
        var safeQuery = query.Length > 50 ? query[..50] : query;

        var results = new List<(string key, string name, string shortcut, int score)>();
        foreach (var cmd in allCommands)
        {
            // Substring match = best score
            if (cmd.name.Contains(safeQuery, StringComparison.OrdinalIgnoreCase) ||
                cmd.key.Contains(safeQuery, StringComparison.OrdinalIgnoreCase))
            {
                results.Add((cmd.key, cmd.name, cmd.shortcut, 0));
                continue;
            }
            // Levenshtein fuzzy match
            var dist = LevenshteinDistance(safeQuery.ToLowerInvariant(), cmd.key.ToLowerInvariant());
            if (dist <= 3)
                results.Add((cmd.key, cmd.name, cmd.shortcut, dist + 2));
        }
        return results.OrderBy(r => r.score).ToList();
    }


    // ═══ LAUNCHER INTEGRATION ═══════════════════════════════════════════
    // Port of LauncherIntegration.ps1

    private static Dictionary<string, string> CoreMapping =>
        UiLookupData.Instance.CoreMapping.Count > 0 ? UiLookupData.Instance.CoreMapping
        : new(StringComparer.OrdinalIgnoreCase)
    {
        ["nes"] = "mesen_libretro", ["snes"] = "snes9x_libretro", ["n64"] = "mupen64plus_next_libretro",
        ["gb"] = "gambatte_libretro", ["gbc"] = "gambatte_libretro", ["gba"] = "mgba_libretro",
        ["nds"] = "melonds_libretro", ["ps1"] = "mednafen_psx_hw_libretro", ["ps2"] = "pcsx2_libretro",
        ["psp"] = "ppsspp_libretro", ["gc"] = "dolphin_libretro", ["wii"] = "dolphin_libretro",
        ["genesis"] = "genesis_plus_gx_libretro", ["arcade"] = "fbneo_libretro",
        ["dreamcast"] = "flycast_libretro", ["saturn"] = "mednafen_saturn_libretro"
    };


    public static string ExportRetroArchPlaylist(IReadOnlyList<RomCandidate> winners, string playlistName)
    {
        var entries = new List<object>();
        foreach (var w in winners)
        {
            var console = DetectConsoleFromPath(w.MainPath).ToLowerInvariant();
            var core = CoreMapping.GetValueOrDefault(console, "");
            entries.Add(new
            {
                path = w.MainPath.Replace('\\', '/'),
                label = Path.GetFileNameWithoutExtension(w.MainPath),
                core_path = core,
                core_name = core.Replace("_libretro", ""),
                db_name = playlistName + ".lpl"
            });
        }
        return JsonSerializer.Serialize(new
        {
            version = "1.5",
            default_core_path = "",
            default_core_name = "",
            items = entries
        }, new JsonSerializerOptions { WriteIndented = true });
    }


    /// <summary>Build command palette results report.</summary>
    public static string BuildCommandPaletteReport(string input,
        IReadOnlyList<(string key, string name, string shortcut, int score)> results)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Ergebnisse für \"{input}\":\n");
        foreach (var r in results)
            sb.AppendLine($"  {r.shortcut,-12} {r.name}");
        return sb.ToString();
    }

}
