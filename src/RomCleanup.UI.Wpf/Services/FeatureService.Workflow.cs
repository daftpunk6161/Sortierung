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

    // ═══ DRY RUN COMPARE ════════════════════════════════════════════════
    // Port of DryRunCompare.ps1

    public static DryRunCompareResult CompareDryRuns(IReadOnlyList<ReportEntry> a, IReadOnlyList<ReportEntry> b)
    {
        // Use first-wins to avoid ArgumentException on duplicate FilePath entries
        var indexA = new Dictionary<string, ReportEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in a)
            indexA.TryAdd(e.FilePath, e);
        var indexB = new Dictionary<string, ReportEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in b)
            indexB.TryAdd(e.FilePath, e);

        var onlyInA = a.Where(e => !indexB.ContainsKey(e.FilePath)).ToList();
        var onlyInB = b.Where(e => !indexA.ContainsKey(e.FilePath)).ToList();
        var different = new List<(ReportEntry left, ReportEntry right)>();
        var identical = 0;

        foreach (var entry in a)
        {
            if (indexB.TryGetValue(entry.FilePath, out var other))
            {
                if (entry.Action != other.Action || entry.Category != other.Category)
                    different.Add((entry, other));
                else
                    identical++;
            }
        }

        return new DryRunCompareResult(onlyInA, onlyInB, different, identical);
    }


    // ═══ SORT TEMPLATES ═════════════════════════════════════════════════
    // Port of SortTemplates.ps1

    public static Dictionary<string, string> GetSortTemplates()
    {
        var ext = UiLookupData.Instance.SortTemplates;
        if (ext.Count > 0) return new(ext);
        return new()
        {
            ["RetroArch"] = "{console}/{filename}",
            ["EmulationStation"] = "roms/{console_lower}/{filename}",
            ["LaunchBox"] = "Games/{console}/{filename}",
            ["Batocera"] = "share/roms/{console_lower}/{filename}",
            ["Flat"] = "{filename}"
        };
    }


    // ═══ SCHEDULER ══════════════════════════════════════════════════════
    // Port of SchedulerAdvanced.ps1

    public static bool TestCronMatch(string cronExpression, DateTime dt)
    {
        var fields = cronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5) return false;

        return CronFieldMatch(fields[0], dt.Minute) &&
               CronFieldMatch(fields[1], dt.Hour) &&
               CronFieldMatch(fields[2], dt.Day) &&
               CronFieldMatch(fields[3], dt.Month) &&
               CronFieldMatch(fields[4], (int)dt.DayOfWeek);
    }


    internal static bool CronFieldMatch(string field, int value)
    {
        if (field == "*") return true;
        foreach (var part in field.Split(','))
        {
            if (part.Contains('/'))
            {
                var segments = part.Split('/');
                if (segments.Length == 2 && int.TryParse(segments[1], out var step) && step > 0)
                {
                    // Support range/step syntax like "10-30/5"
                    int lo = 0;
                    if (segments[0].Contains('-'))
                    {
                        var range = segments[0].Split('-');
                        if (int.TryParse(range[0], out var rLo) && int.TryParse(range[1], out var rHi))
                        {
                            if (value >= rLo && value <= rHi && (value - rLo) % step == 0)
                                return true;
                        }
                    }
                    else if (segments[0] == "*" || int.TryParse(segments[0], out lo))
                    {
                        if ((value - lo) % step == 0 && value >= lo)
                            return true;
                    }
                }
            }
            else if (part.Contains('-'))
            {
                var range = part.Split('-');
                if (int.TryParse(range[0], out var lo) && int.TryParse(range[1], out var hi) && value >= lo && value <= hi)
                    return true;
            }
            else if (int.TryParse(part, out var exact) && exact == value)
                return true;
        }
        return false;
    }


    // ═══ CSV DIFF ═══════════════════════════════════════════════════════

    /// <summary>
    /// Compare two CSV report files and build a diff report string.
    /// Returns null if comparison is not possible (non-CSV files).
    /// </summary>
    public static string? BuildCsvDiff(string fileA, string fileB, string title)
    {
        if (!fileA.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
            !fileB.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return null;

        var setA = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var setB = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(fileA).Skip(1))
        {
            var mainPath = ExtractFirstCsvField(line);
            if (!string.IsNullOrWhiteSpace(mainPath))
                setA.Add(mainPath);
        }
        foreach (var line in File.ReadLines(fileB).Skip(1))
        {
            var mainPath = ExtractFirstCsvField(line);
            if (!string.IsNullOrWhiteSpace(mainPath))
                setB.Add(mainPath);
        }

        var added = setB.Except(setA).ToList();
        var removed = setA.Except(setB).ToList();
        var same = setA.Intersect(setB).Count();

        var sb = new StringBuilder();
        sb.AppendLine($"{title} (CSV)");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"\n  A: {Path.GetFileName(fileA)} ({setA.Count} Einträge)");
        sb.AppendLine($"  B: {Path.GetFileName(fileB)} ({setB.Count} Einträge)");
        sb.AppendLine($"\n  Gleich:      {same}");
        sb.AppendLine($"  Hinzugefügt: {added.Count}");
        sb.AppendLine($"  Entfernt:    {removed.Count}");

        if (added.Count > 0)
        {
            sb.AppendLine($"\n  --- Hinzugefügt (erste {Math.Min(30, added.Count)}) ---");
            foreach (var entry in added.Take(30))
                sb.AppendLine($"    + {Path.GetFileName(entry)}");
            if (added.Count > 30)
                sb.AppendLine($"    … und {added.Count - 30} weitere");
        }

        if (removed.Count > 0)
        {
            sb.AppendLine($"\n  --- Entfernt (erste {Math.Min(30, removed.Count)}) ---");
            foreach (var entry in removed.Take(30))
                sb.AppendLine($"    - {Path.GetFileName(entry)}");
            if (removed.Count > 30)
                sb.AppendLine($"    … und {removed.Count - 30} weitere");
        }

        return sb.ToString();
    }


    /// <summary>
    /// Extract the first field from a CSV line, auto-detecting delimiter (semicolon or comma).
    /// Handles RFC 4180 quoted fields.
    /// </summary>
    internal static string ExtractFirstCsvField(string line)
    {
        if (string.IsNullOrEmpty(line)) return "";
        if (line[0] == '"')
        {
            // Quoted field — find closing quote
            for (int i = 1; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    { i++; continue; } // escaped quote
                    return line[1..i].Replace("\"\"", "\"");
                }
            }
            return line[1..].Replace("\"\"", "\"");
        }
        // Unquoted — split on first semicolon or comma
        var idxSemi = line.IndexOf(';');
        var idxComma = line.IndexOf(',');
        int idx;
        if (idxSemi < 0) idx = idxComma;
        else if (idxComma < 0) idx = idxSemi;
        else idx = Math.Min(idxSemi, idxComma);
        return idx >= 0 ? line[..idx] : line;
    }


    // ═══ PIPELINE REPORT ════════════════════════════════════════════════

    /// <summary>
    /// Build a pipeline engine report from a run result and candidate list.
    /// If result is null, returns a default help text.
    /// </summary>
    public static string BuildPipelineReport(RunResult? result, IReadOnlyList<RomCandidate> candidates)
    {
        if (result is null)
        {
            return "Pipeline-Engine\n\n" +
                "Bedingte Multi-Step-Pipelines:\n\n" +
                "  1. Scan → Dateien erfassen\n" +
                "  2. Dedupe → Duplikate erkennen\n" +
                "  3. Sort → Nach Konsole sortieren\n" +
                "  4. Convert → Formate konvertieren\n" +
                "  5. Verify → Konvertierung prüfen\n\n" +
                "Jeder Schritt kann übersprungen werden.\n" +
                "DryRun-aware: Kein Schreibzugriff im DryRun-Modus.\n\n" +
                "Starte einen Lauf, um Pipeline-Ergebnisse zu sehen.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Pipeline-Engine — Letzter Lauf");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"\n  Status: {result.Status}");
        sb.AppendLine($"  Dauer:  {result.DurationMs / 1000.0:F1}s\n");

        sb.AppendLine($"  {"Phase",-20} {"Status",-15} {"Details"}");
        sb.AppendLine($"  {new string('-', 20)} {new string('-', 15)} {new string('-', 30)}");

        sb.AppendLine($"  {"Scan",-20} {"OK",-15} {result.TotalFilesScanned} Dateien");
        sb.AppendLine($"  {"Dedupe",-20} {"OK",-15} {result.GroupCount} Gruppen, {result.WinnerCount} Winner");

        var junkCount = candidates.Count(c => c.Category == "JUNK");
        sb.AppendLine($"  {"Junk-Erkennung",-20} {"OK",-15} {junkCount} Junk-Dateien");

        if (result.ConsoleSortResult is { } cs)
            sb.AppendLine($"  {"Konsolen-Sort",-20} {"OK",-15} sortiert");
        else
            sb.AppendLine($"  {"Konsolen-Sort",-20} {"Übersprungen",-15}");

        if (result.ConvertedCount > 0)
            sb.AppendLine($"  {"Konvertierung",-20} {"OK",-15} {result.ConvertedCount} konvertiert");
        else
            sb.AppendLine($"  {"Konvertierung",-20} {"Übersprungen",-15}");

        if (result.MoveResult is { } mv)
            sb.AppendLine($"  {"Move",-20} {(mv.FailCount > 0 ? "WARNUNG" : "OK"),-15} {mv.MoveCount} verschoben, {mv.FailCount} Fehler");
        else
            sb.AppendLine($"  {"Move",-20} {"DryRun",-15} keine Änderungen");

        return sb.ToString();
    }


    // ═══ MULTI-INSTANCE SYNC ════════════════════════════════════════════

    /// <summary>Build a report about lock files found in the given roots.</summary>
    public static string BuildMultiInstanceReport(IReadOnlyList<string> roots, bool isBusy)
    {
        var locks = new List<(string path, string content)>();
        foreach (var root in roots)
        {
            var lockFile = Path.Combine(root, ".romcleanup.lock");
            if (File.Exists(lockFile))
            {
                try { locks.Add((lockFile, File.ReadAllText(lockFile))); }
                catch { locks.Add((lockFile, "(nicht lesbar)")); }
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Multi-Instanz-Synchronisation");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"\n  Konfigurierte Roots: {roots.Count}");
        sb.AppendLine($"  Aktive Locks:       {locks.Count}");

        if (locks.Count > 0)
        {
            sb.AppendLine("\n  Gefundene Lock-Dateien:");
            foreach (var (path, content) in locks)
            {
                sb.AppendLine($"    {path}");
                sb.AppendLine($"      {content}");
            }
        }
        else
        {
            sb.AppendLine("\n  Keine aktiven Locks gefunden.");
        }

        sb.AppendLine($"\n  Diese Instanz:");
        sb.AppendLine($"    PID:      {Environment.ProcessId}");
        sb.AppendLine($"    Hostname: {Environment.MachineName}");
        sb.AppendLine($"    Status:   {(isBusy ? "LÄUFT" : "Bereit")}");
        return sb.ToString();
    }


    /// <summary>Remove lock files from roots. Returns number removed.</summary>
    public static int RemoveLockFiles(IReadOnlyList<string> roots)
    {
        int removed = 0;
        foreach (var root in roots)
        {
            var lockFile = Path.Combine(root, ".romcleanup.lock");
            if (File.Exists(lockFile))
            {
                try { File.Delete(lockFile); removed++; }
                catch { /* in use */ }
            }
        }
        return removed;
    }


    /// <summary>Check if any lock files exist in the given roots.</summary>
    public static bool HasLockFiles(IReadOnlyList<string> roots) =>
        roots.Any(r => File.Exists(Path.Combine(r, ".romcleanup.lock")));

}
