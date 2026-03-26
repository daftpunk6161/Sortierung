using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RomCleanup.Core;
using System.Xml;
using System.Xml.Linq;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Tools;
using RomCleanup.Infrastructure.Reporting;

namespace RomCleanup.UI.Wpf.Services;

public static partial class FeatureService
{

    // ═══ APPLY FILTER ══════════════════════════════════════════════════
    // Filter candidates by field/operator/value.

    public static IReadOnlyList<RomCandidate> ApplyFilter(
        IReadOnlyList<RomCandidate> candidates, string field, string op, string value)
    {
        if (candidates is null || candidates.Count == 0)
            return [];

        return candidates.Where(c => EvaluateFilter(c, field, op, value)).ToList();
    }


    internal static string ResolveField(RomCandidate c, string field) => field.ToLowerInvariant() switch
    {
        "console" => DetectConsoleFromPath(c.MainPath),
        "region" => c.Region,
        "format" => c.Extension,
        "category" => ToCategoryLabel(c.Category),
        "sizemb" => (c.SizeBytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture),
        "datstatus" => c.DatMatch ? "Verified" : "Unverified",
        "filename" => Path.GetFileName(c.MainPath),
        "gamekey" => c.GameKey,
        _ => ""
    };


    internal static bool EvaluateFilter(RomCandidate c, string field, string op, string value)
    {
        var fieldValue = ResolveField(c, field);
        return op.ToLowerInvariant() switch
        {
            "eq" => fieldValue.Equals(value, StringComparison.OrdinalIgnoreCase),
            "neq" => !fieldValue.Equals(value, StringComparison.OrdinalIgnoreCase),
            "contains" => fieldValue.Contains(value, StringComparison.OrdinalIgnoreCase),
            "gt" => double.TryParse(fieldValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var fv) &&
                    double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var tv) && fv > tv,
            "lt" => double.TryParse(fieldValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var fv2) &&
                    double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var tv2) && fv2 < tv2,
            "regex" => TryRegexMatch(fieldValue, value),
            _ => false
        };
    }


    internal static bool TryRegexMatch(string input, string pattern)
    {
        try
        {
            var rx = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
            return SafeRegex.IsMatch(rx, input);
        }
        catch (ArgumentException) { return false; } // invalid regex pattern
    }


    // ═══ MISSING ROM REPORT ════════════════════════════════════════════

    /// <summary>
    /// Build a "missing ROMs" (not DAT-verified) report grouped by subdirectory.
    /// Returns null if no unverified candidates exist.
    /// </summary>
    public static string? BuildMissingRomReport(IReadOnlyList<RomCandidate> candidates, IReadOnlyList<string> roots)
    {
        var unverified = candidates.Where(c => !c.DatMatch).ToList();
        if (unverified.Count == 0) return null;

        var normalizedRoots = roots
            .Select(r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToList();

        string GetSubDir(string filePath)
        {
            var full = Path.GetFullPath(filePath);
            foreach (var root in normalizedRoots)
            {
                if (full.Length > root.Length && full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full[root.Length] is '\\' or '/')
                {
                    var relative = full[(root.Length + 1)..];
                    var sep = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
                    return sep > 0 ? relative[..sep] : "(Root)";
                }
            }
            return Path.GetDirectoryName(filePath) ?? "(Unbekannt)";
        }

        var byDir = unverified.GroupBy(c => GetSubDir(c.MainPath))
            .OrderByDescending(g => g.Count())
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("Fehlende ROMs (ohne DAT-Match)");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"\n  Gesamt ohne DAT-Match: {unverified.Count} / {candidates.Count}");
        sb.AppendLine($"\n  Nach Verzeichnis:\n");
        foreach (var g in byDir)
            sb.AppendLine($"    {g.Count(),5}  {g.Key}");

        return sb.ToString();
    }


    // ═══ CROSS-ROOT DUPLICATE REPORT ════════════════════════════════════

    /// <summary>
    /// Build a cross-root duplicate report showing groups spanning multiple roots.
    /// </summary>
    public static string BuildCrossRootReport(IReadOnlyList<DedupeGroup> dedupeGroups, IReadOnlyList<string> roots)
    {
        var normalizedRoots = roots
            .Select(r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToList();

        string? GetRoot(string filePath)
        {
            var full = Path.GetFullPath(filePath);
            return normalizedRoots.FirstOrDefault(r => full.Length > r.Length && full.StartsWith(r, StringComparison.OrdinalIgnoreCase) && full[r.Length] is '\\' or '/');
        }

        var crossRootGroups = new List<DedupeGroup>();
        foreach (var g in dedupeGroups)
        {
            var allPaths = new[] { g.Winner }.Concat(g.Losers);
            var distinctRoots = allPaths.Select(c => GetRoot(c.MainPath)).Where(r => r is not null).Distinct().Count();
            if (distinctRoots > 1)
                crossRootGroups.Add(g);
        }

        var sb = new StringBuilder();
        sb.AppendLine("Cross-Root-Duplikate");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"\n  Roots: {roots.Count}");
        sb.AppendLine($"  Dedupe-Gruppen gesamt: {dedupeGroups.Count}");
        sb.AppendLine($"  Cross-Root-Gruppen: {crossRootGroups.Count}\n");

        foreach (var g in crossRootGroups.Take(30))
        {
            sb.AppendLine($"  [{g.GameKey}]");
            sb.AppendLine($"    Winner: {g.Winner.MainPath}");
            foreach (var l in g.Losers)
                sb.AppendLine($"    Loser:  {l.MainPath}");
        }
        if (crossRootGroups.Count > 30)
            sb.AppendLine($"\n  … und {crossRootGroups.Count - 30} weitere Gruppen");
        if (crossRootGroups.Count == 0)
            sb.AppendLine("  Keine Cross-Root-Duplikate gefunden.");

        return sb.ToString();
    }


    /// <summary>
    /// Match cover images against ROM candidates and build a report.
    /// Returns (report, matchedCount, unmatchedCount).
    /// </summary>
    public static (string Report, int Matched, int Unmatched) BuildCoverReport(
        string coverDir, IReadOnlyList<RomCandidate> candidates)
    {
        var imageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
        var coverFiles = Directory.GetFiles(coverDir, "*.*", SearchOption.AllDirectories)
            .Where(f => imageExts.Contains(Path.GetExtension(f)))
            .ToList();

        if (coverFiles.Count == 0)
            return ($"Keine Cover-Bilder gefunden in:\n{coverDir}", 0, 0);

        var gameKeys = candidates
            .Select(c => RomCleanup.Core.GameKeys.GameKeyNormalizer.Normalize(
                Path.GetFileNameWithoutExtension(c.MainPath)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matched = new List<string>();
        var unmatched = new List<string>();
        foreach (var cover in coverFiles)
        {
            var coverName = Path.GetFileNameWithoutExtension(cover);
            var normalizedCover = RomCleanup.Core.GameKeys.GameKeyNormalizer.Normalize(coverName);
            if (gameKeys.Contains(normalizedCover))
                matched.Add(coverName);
            else
                unmatched.Add(coverName);
        }

        var sb = new StringBuilder();
        sb.AppendLine("Cover-Scraper Ergebnis\n");
        sb.AppendLine($"  Cover-Ordner: {coverDir}");
        sb.AppendLine($"  Gefundene Bilder: {coverFiles.Count}");
        sb.AppendLine($"  ROMs in Sammlung: {gameKeys.Count}");
        sb.AppendLine($"\n  Zugeordnet:    {matched.Count}");
        sb.AppendLine($"  Nicht zugeordnet: {unmatched.Count}");
        sb.AppendLine($"  Ohne Cover:    {gameKeys.Count - matched.Count}");

        if (matched.Count > 0)
        {
            sb.AppendLine($"\n  --- Zugeordnet (erste {Math.Min(15, matched.Count)}) ---");
            foreach (var m in matched.Take(15))
                sb.AppendLine($"    \u2713 {m}");
        }
        if (unmatched.Count > 0)
        {
            sb.AppendLine($"\n  --- Nicht zugeordnet (erste {Math.Min(15, unmatched.Count)}) ---");
            foreach (var u in unmatched.Take(15))
                sb.AppendLine($"    ? {u}");
        }

        return (sb.ToString(), matched.Count, unmatched.Count);
    }


    // ═══ FILTER BUILDER ═════════════════════════════════════════════════

    /// <summary>Parse a filter expression like "field=value" or "field>=value".</summary>
    public static (string Field, string Op, string Value)? ParseFilterExpression(string input)
    {
        string field, op, value;
        if (input.Contains(">=")) { var p = input.Split(">=", 2); field = p[0].Trim().ToLowerInvariant(); op = ">="; value = p[1].Trim(); }
        else if (input.Contains("<=")) { var p = input.Split("<=", 2); field = p[0].Trim().ToLowerInvariant(); op = "<="; value = p[1].Trim(); }
        else if (input.Contains('>')) { var p = input.Split('>', 2); field = p[0].Trim().ToLowerInvariant(); op = ">"; value = p[1].Trim(); }
        else if (input.Contains('<')) { var p = input.Split('<', 2); field = p[0].Trim().ToLowerInvariant(); op = "<"; value = p[1].Trim(); }
        else if (input.Contains('=')) { var p = input.Split('=', 2); field = p[0].Trim().ToLowerInvariant(); op = "="; value = p[1].Trim(); }
        else return null;
        return (field, op, value);
    }


    /// <summary>Apply a parsed filter to candidates and build a report.</summary>
    public static string BuildFilterReport(IReadOnlyList<RomCandidate> candidates, string field, string op, string value)
    {
        var filtered = candidates.Where(c =>
        {
            string fieldValue = field switch
            {
                "region" => c.Region,
                "category" => ToCategoryLabel(c.Category),
                "extension" or "ext" => c.Extension,
                "gamekey" or "game" => c.GameKey,
                "type" or "consolekey" or "console" => c.ConsoleKey,
                "datmatch" or "dat" => c.DatMatch.ToString(),
                "sizemb" => (c.SizeBytes / 1048576.0).ToString("F1"),
                "sizebytes" or "size" => c.SizeBytes.ToString(),
                "filename" or "name" => Path.GetFileName(c.MainPath),
                _ => ""
            };
            if (op == "=")
                return fieldValue.Contains(value, StringComparison.OrdinalIgnoreCase);
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var numVal) &&
                double.TryParse(fieldValue, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var fieldNum))
            {
                return op switch { ">" => fieldNum > numVal, "<" => fieldNum < numVal, ">=" => fieldNum >= numVal, "<=" => fieldNum <= numVal, _ => false };
            }
            return false;
        }).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Filter-Builder: {field} {op} {value}");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"\n  Gesamt: {candidates.Count}");
        sb.AppendLine($"  Gefiltert: {filtered.Count}\n");
        foreach (var r in filtered.Take(50))
            sb.AppendLine($"  {Path.GetFileName(r.MainPath),-45} [{r.Region}] {r.Extension} {r.Category} {FormatSize(r.SizeBytes)}");
        if (filtered.Count > 50)
            sb.AppendLine($"\n  … und {filtered.Count - 50} weitere");
        return sb.ToString();
    }

}
