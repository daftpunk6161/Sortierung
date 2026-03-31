using System.Text;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.Infrastructure.Analysis;

/// <summary>
/// Completeness report per console: compares DAT index entries against
/// files found in the collection roots to show coverage percentage.
/// </summary>
public static class CompletenessReportService
{
    /// <summary>
    /// Build completeness report comparing DAT entries against files in roots.
    /// </summary>
    public static CompletenessReport Build(DatIndex datIndex, IReadOnlyList<string> roots)
    {
        // Collect all file hashes from roots, grouped by console detection
        var filesByConsole = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var consoleKey = CollectionAnalysisService.DetectConsoleFromPath(file);
                if (!filesByConsole.TryGetValue(consoleKey, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    filesByConsole[consoleKey] = set;
                }
                set.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        var entries = new List<CompletenessEntry>();

        foreach (var consoleKey in datIndex.ConsoleKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var datEntries = datIndex.GetConsoleEntries(consoleKey);
            if (datEntries is null || datEntries.Count == 0) continue;

            // Get unique game names from DAT
            var datGameNames = new HashSet<string>(
                datEntries.Values.Distinct(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            var totalInDat = datGameNames.Count;

            // Match against files in collection
            var collectionFileNames = filesByConsole.TryGetValue(consoleKey, out var files)
                ? files
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int verified = 0;
            var missing = new List<string>();

            foreach (var gameName in datGameNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (collectionFileNames.Contains(gameName))
                    verified++;
                else
                    missing.Add(gameName);
            }

            var percentage = totalInDat > 0
                ? Math.Round(100.0 * verified / totalInDat, 1)
                : 0.0;

            entries.Add(new CompletenessEntry(
                consoleKey, totalInDat, verified, missing.Count, percentage, missing));
        }

        return new CompletenessReport(entries);
    }

    /// <summary>
    /// Format completeness report as human-readable text.
    /// </summary>
    public static string FormatReport(CompletenessReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Collection Completeness Report");
        sb.AppendLine(new string('=', 60));

        if (report.Entries.Count == 0)
        {
            sb.AppendLine("\n  No DAT data available. Enable DAT verification first.");
            return sb.ToString();
        }

        sb.AppendLine($"\n  {"Console",-20} {"In DAT",8} {"Have",8} {"Missing",8} {"Complete",10}");
        sb.AppendLine($"  {new string('-', 20)} {new string('-', 8)} {new string('-', 8)} {new string('-', 8)} {new string('-', 10)}");

        foreach (var entry in report.Entries.OrderByDescending(e => e.Percentage))
        {
            var bar = entry.Percentage >= 100 ? "[FULL]" : $"{entry.Percentage,5:F1}%";
            sb.AppendLine($"  {entry.ConsoleKey,-20} {entry.TotalInDat,8} {entry.Verified,8} {entry.MissingCount,8} {bar,10}");
        }

        var totalDat = report.Entries.Sum(e => e.TotalInDat);
        var totalHave = report.Entries.Sum(e => e.Verified);
        var totalMissing = report.Entries.Sum(e => e.MissingCount);
        var overallPct = totalDat > 0 ? Math.Round(100.0 * totalHave / totalDat, 1) : 0.0;

        sb.AppendLine($"  {new string('-', 20)} {new string('-', 8)} {new string('-', 8)} {new string('-', 8)} {new string('-', 10)}");
        sb.AppendLine($"  {"TOTAL",-20} {totalDat,8} {totalHave,8} {totalMissing,8} {overallPct,5:F1}%");

        return sb.ToString();
    }
}

/// <summary>
/// Completeness report for the entire collection.
/// </summary>
public sealed record CompletenessReport(IReadOnlyList<CompletenessEntry> Entries);

/// <summary>
/// Completeness per console: how many DAT entries are present in the collection.
/// </summary>
public sealed record CompletenessEntry(
    string ConsoleKey,
    int TotalInDat,
    int Verified,
    int MissingCount,
    double Percentage,
    IReadOnlyList<string> MissingGames);
