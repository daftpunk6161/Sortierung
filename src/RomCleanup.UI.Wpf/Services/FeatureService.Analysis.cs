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

    // ═══ HEALTH SCORE ═══════════════════════════════════════════════════
    // Port of WpfSlice.ReportPreview.ps1

    public static int CalculateHealthScore(int totalFiles, int dupes, int junk, int verified)
    {
        if (totalFiles <= 0) return 0;
        var dupePct = 100.0 * dupes / totalFiles;
        var junkPct = 100.0 * junk / totalFiles;
        var verifiedBonus = verified > 0 ? 10.0 * verified / totalFiles : 0;
        return (int)Math.Clamp(100 - Math.Min(60, dupePct) - Math.Min(30, junkPct) + verifiedBonus, 0, 100);
    }


    // ═══ DUPLICATE HEATMAP ══════════════════════════════════════════════
    // Port of DuplicateHeatmap.ps1

    public static List<HeatmapEntry> GetDuplicateHeatmap(IReadOnlyList<DedupeResult> groups)
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
            c.Category.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
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

    public static string GetHardlinkEstimate(IReadOnlyList<DedupeResult> groups)
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

    public static string BuildCloneTree(IReadOnlyList<DedupeResult> groups)
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


    // ═══ SPLIT PANEL PREVIEW ════════════════════════════════════════════
    // Port of SplitPanelPreview.ps1

    public static string BuildSplitPanelPreview(IReadOnlyList<DedupeResult> groups)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Split-Panel (Norton Commander Style)");
        sb.AppendLine(new string('═', 60));
        sb.AppendLine($"{"KEEP (Quelle)",-30} │ {"MOVE (Ziel)",-30}");
        sb.AppendLine(new string('─', 30) + "─┼─" + new string('─', 30));

        foreach (var g in groups.Take(30))
        {
            var winner = Path.GetFileName(g.Winner.MainPath);
            foreach (var l in g.Losers)
            {
                var loser = Path.GetFileName(l.MainPath);
                sb.AppendLine($"{Truncate(winner, 30),-30} │ {Truncate(loser, 30),-30}");
            }
        }
        if (groups.Count > 30)
            sb.AppendLine($"\n  … und {groups.Count - 30} weitere Gruppen");
        return sb.ToString();
    }


    // ═══ COMMAND PALETTE ════════════════════════════════════════════════
    // Port of CommandPalette.ps1

    public static readonly (string key, string name, string shortcut)[] PaletteCommands =
    [
        ("dryrun", "DryRun starten", "Ctrl+D"),
        ("move", "Move ausführen", "Ctrl+M"),
        ("convert", "Konvertierung starten", "Ctrl+K"),
        ("settings", "Einstellungen öffnen", "Ctrl+,"),
        ("dat-update", "DAT aktualisieren", "Ctrl+U"),
        ("export-csv", "CSV exportieren", "Ctrl+E"),
        ("export-report", "Report öffnen", "Ctrl+R"),
        ("history", "Verlauf anzeigen", "Ctrl+H"),
        ("cancel", "Lauf abbrechen", "Escape"),
        ("help", "Hilfe anzeigen", "F1"),
        ("rollback", "Rollback ausführen", "Ctrl+Z"),
        ("filter", "ROM-Filter", "Ctrl+F"),
        ("theme", "Theme wechseln", "Ctrl+T"),
        ("clear-log", "Log leeren", "Ctrl+L"),
        ("gamekey", "GameKey-Vorschau", "Ctrl+G")
    ];


    public static List<(string key, string name, string shortcut, int score)> SearchCommands(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return PaletteCommands.Select(c => (c.key, c.name, c.shortcut, 0)).ToList();

        // Limit query length to prevent excessive Levenshtein matrix allocation
        var safeQuery = query.Length > 50 ? query[..50] : query;

        var results = new List<(string key, string name, string shortcut, int score)>();
        foreach (var cmd in PaletteCommands)
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


    // ═══ GENRE CLASSIFICATION ═══════════════════════════════════════════
    // Port of GenreClassification.ps1

    private static (string keyword, string genre)[] GenreKeywords
    {
        get
        {
            var ext = UiLookupData.Instance.GenreKeywords;
            if (ext.Count > 0)
                return ext.Select(e => (e.Keyword, e.Genre)).ToArray();
            return _defaultGenreKeywords;
        }
    }

    private static readonly (string keyword, string genre)[] _defaultGenreKeywords =
    [
        ("rpg", "RPG"), ("quest", "RPG"), ("dragon", "RPG"), ("fantasy", "RPG"),
        ("race", "Racing"), ("rally", "Racing"), ("kart", "Racing"), ("speed", "Racing"),
        ("soccer", "Sports"), ("fifa", "Sports"), ("nba", "Sports"), ("tennis", "Sports"),
        ("fight", "Fighting"), ("tekken", "Fighting"), ("mortal", "Fighting"), ("street fighter", "Fighting"),
        ("puzzle", "Puzzle"), ("tetris", "Puzzle"),
        ("mario", "Platformer"), ("sonic", "Platformer"), ("jump", "Platformer"),
        ("shoot", "Shooter"), ("gun", "Shooter"), ("doom", "Shooter"),
        ("strategy", "Strategy"), ("chess", "Strategy"), ("war", "Strategy"),
        ("adventure", "Adventure"), ("zelda", "Adventure"),
        ("simulation", "Simulation"), ("sim", "Simulation"),
        ("pinball", "Arcade"), ("pong", "Arcade")
    ];


    public static string ClassifyGenre(string gameName)
    {
        var lower = gameName.ToLowerInvariant();
        foreach (var (keyword, genre) in GenreKeywords)
        {
            // Use word boundary matching to avoid false positives (e.g. "gun" matching "Gundam")
            if (System.Text.RegularExpressions.Regex.IsMatch(lower, $@"\b{System.Text.RegularExpressions.Regex.Escape(keyword)}\b", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(200)))
                return genre;
        }
        return "Other";
    }


    // ═══ CSV REPORT PARSER ═════════════════════════════════════════════
    // Parse a CSV report file (as exported by ExportCollectionCsv) back into RomCandidate objects.

    public static IReadOnlyList<RomCandidate> ParseCsvReport(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return [];

        var results = new List<RomCandidate>();
        var lines = File.ReadAllLines(filePath);
        if (lines.Length < 2) return [];

        // Parse header to determine column indices
        var headers = ParseCsvLine(lines[0]);
        var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
            colIndex[headers[i].Trim()] = i;

        for (int row = 1; row < lines.Length; row++)
        {
            var line = lines[row];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = ParseCsvLine(line);

            string GetField(string name) =>
                colIndex.TryGetValue(name, out var idx) && idx < fields.Length ? fields[idx].Trim() : "";

            var sizeStr = GetField("SizeBytes");
            long.TryParse(sizeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var sizeBytes);

            var datStr = GetField("DatMatch");
            var datMatch = datStr.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                           datStr.Equals("Verified", StringComparison.OrdinalIgnoreCase);

            results.Add(new RomCandidate
            {
                MainPath = GetField("MainPath"),
                GameKey = GetField("GameKey"),
                Extension = GetField("Extension"),
                Region = GetField("Region") is { Length: > 0 } r ? r : "UNKNOWN",
                Category = GetField("Category") is { Length: > 0 } cat ? cat : "GAME",
                SizeBytes = sizeBytes,
                DatMatch = datMatch
            });
        }

        return results;
    }


    // ═══ BATCH-4 EXTRACTIONS ════════════════════════════════════════════

    /// <summary>Detect auto-profile recommendation based on file extensions in roots.</summary>
    public static string DetectAutoProfile(IReadOnlyList<string> roots)
    {
        var hasDisc = false;
        var hasCartridge = false;
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Take(200))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".chd" or ".iso" or ".bin" or ".cue" or ".gdi") hasDisc = true;
                if (ext is ".nes" or ".sfc" or ".gba" or ".nds" or ".z64" or ".gb") hasCartridge = true;
            }
        }
        return (hasDisc, hasCartridge) switch
        {
            (true, true) => "Gemischt (Disc + Cartridge): Konvertierung empfohlen",
            (true, false) => "Disc-basiert: CHD-Konvertierung empfohlen, aggressive Deduplizierung",
            (false, true) => "Cartridge-basiert: ZIP-Komprimierung, leichte Deduplizierung",
            _ => "Unbekannt: Keine erkannten ROM-Formate gefunden. Bitte überprüfen Sie die Root-Ordner."
        };
    }


    /// <summary>Build playtime tracker report from .lrtl files.</summary>
    public static string BuildPlaytimeReport(string directory)
    {
        var lrtlFiles = Directory.GetFiles(directory, "*.lrtl", SearchOption.AllDirectories);
        if (lrtlFiles.Length == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Spielzeit-Tracker: {lrtlFiles.Length} Dateien\n");
        sb.AppendLine("Hinweis: Es werden nur RetroArch .lrtl-Dateien unterstützt.\n");
        foreach (var f in lrtlFiles.Take(20))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            var lines = File.ReadAllLines(f);
            sb.AppendLine($"  {name}: {lines.Length} Einträge");
        }
        return sb.ToString();
    }


    /// <summary>Build collection manager report grouped by genre.</summary>
    public static string BuildCollectionManagerReport(IReadOnlyList<RomCandidate> candidates)
    {
        var byConsole = candidates.GroupBy(c => ClassifyGenre(c.GameKey))
            .OrderByDescending(g => g.Count()).ToList();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Smart Collection Manager\n");
        sb.AppendLine($"Gesamt: {candidates.Count} ROMs\n");
        foreach (var g in byConsole)
            sb.AppendLine($"  {g.Key,-20} {g.Count(),5} ROMs");
        return sb.ToString();
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
