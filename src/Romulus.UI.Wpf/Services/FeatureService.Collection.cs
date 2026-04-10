using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Romulus.Core;
using System.Xml;
using System.Xml.Linq;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Tools;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Reporting;

namespace Romulus.UI.Wpf.Services;

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
        "console" => ResolveConsoleLabel(c),
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

        var normalizedRoots = roots.Select(ArtifactPathResolver.NormalizeRoot).ToList();

        string GetSubDir(string filePath)
        {
            var full = Path.GetFullPath(filePath);
            var root = ArtifactPathResolver.FindContainingRoot(filePath, normalizedRoots);
            if (root is not null)
            {
                var relative = full[(root.Length + 1)..];
                var sep = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
                return sep > 0 ? relative[..sep] : "(Root)";
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
        var normalizedRoots = roots.Select(ArtifactPathResolver.NormalizeRoot).ToList();

        var crossRootGroups = new List<DedupeGroup>();
        foreach (var g in dedupeGroups)
        {
            var allPaths = new[] { g.Winner }.Concat(g.Losers);
            var distinctRoots = allPaths.Select(c => ArtifactPathResolver.FindContainingRoot(c.MainPath, normalizedRoots)).Where(r => r is not null).Distinct().Count();
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

}
