using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Romulus.Contracts.Models;
using Romulus.Core.Scoring;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Tools;
using Romulus.Infrastructure.Reporting;

namespace Romulus.UI.Wpf.Services;

public static partial class FeatureService
{

    // ═══ DELEGATED TO INFRASTRUCTURE ═══════════════════════════════════
    // These methods delegate to CollectionAnalysisService for shared logic.
    // GUI-specific formatting kept here; core logic in Infrastructure.

    public static int CalculateHealthScore(int totalFiles, int dupes, int junk, int verified)
        => CollectionAnalysisService.CalculateHealthScore(totalFiles, dupes, junk, verified);

    public static List<HeatmapEntry> GetDuplicateHeatmap(IReadOnlyList<DedupeGroup> groups)
        => CollectionAnalysisService.GetDuplicateHeatmap(groups);

    public static List<DuplicateSourceEntry> GetDuplicateInspector(string? auditPath)
        => CollectionAnalysisService.GetDuplicateInspector(auditPath);

    public static List<RomCandidate> SearchRomCollection(IReadOnlyList<RomCandidate> candidates, string searchText)
        => CollectionAnalysisService.SearchRomCollection(candidates, searchText);

    public static string AnalyzeStorageTiers(IReadOnlyList<RomCandidate> candidates, int hotThresholdDays = 30)
        => CollectionAnalysisService.AnalyzeStorageTiers(candidates, hotThresholdDays);

    public static string GetHardlinkEstimate(IReadOnlyList<DedupeGroup> groups)
        => CollectionAnalysisService.GetHardlinkEstimate(groups);

    public static string GetNasInfo(IReadOnlyList<string> roots)
        => CollectionAnalysisService.GetNasInfo(roots);

    public static string BuildCloneTree(IReadOnlyList<DedupeGroup> groups)
        => CollectionAnalysisService.BuildCloneTree(groups);

    public static string BuildVirtualFolderPreview(IReadOnlyList<RomCandidate> candidates)
        => CollectionAnalysisService.BuildVirtualFolderPreview(candidates);


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
        => CollectionAnalysisService.ExportRetroArchPlaylist(winners, playlistName, CoreMapping);


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
