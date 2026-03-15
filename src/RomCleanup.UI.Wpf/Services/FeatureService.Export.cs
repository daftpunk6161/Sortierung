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

    // ═══ JUNK REPORT ════════════════════════════════════════════════════
    // Port of JunkReport.ps1

    private static readonly (string pattern, string tag, string reason)[] JunkPatterns =
    [
        (@"\(Beta[^)]*\)", "Beta", "Beta-Version"),
        (@"\(Proto[^)]*\)", "Proto", "Prototyp"),
        (@"\(Demo[^)]*\)", "Demo", "Demo-Version"),
        (@"\(Sample\)", "Sample", "Sample"),
        (@"\(Homebrew\)", "Homebrew", "Homebrew"),
        (@"\(Hack\)", "Hack", "ROM-Hack"),
        (@"\(Unl\)", "Unlicensed", "Unlizenziert"),
        (@"\(Aftermarket\)", "Aftermarket", "Aftermarket"),
        (@"\(Pirate\)", "Pirate", "Pirate"),
        (@"\(Program\)", "Program", "Programm/Utility"),
        (@"\[b\d*\]", "[b]", "Bad Dump"),
        (@"\[h\d*\]", "[h]", "Hack-Tag"),
        (@"\[o\d*\]", "[o]", "Overdump"),
        (@"\[t\d*\]", "[t]", "Trainer"),
        (@"\[f\d*\]", "[f]", "Fixed"),
        (@"\[T[\+\-]", "[T]", "Translation")
    ];


    private static readonly (string pattern, string tag, string reason)[] AggressivePatterns =
    [
        (@"\(Alt[^)]*\)", "Alt", "Alternative Version"),
        (@"\(Bonus Disc\)", "Bonus", "Bonus Disc"),
        (@"\(Reprint\)", "Reprint", "Nachdruck"),
        (@"\(Virtual Console\)", "VC", "Virtual Console")
    ];


    private static readonly TimeSpan JunkRxTimeout = TimeSpan.FromMilliseconds(500);

    public static JunkReportEntry? GetJunkReason(string baseName, bool aggressive)
    {
        foreach (var (pattern, tag, reason) in JunkPatterns)
        {
            if (Regex.IsMatch(baseName, pattern, RegexOptions.IgnoreCase, JunkRxTimeout))
                return new JunkReportEntry(tag, reason, "standard");
        }

        if (aggressive)
        {
            foreach (var (pattern, tag, reason) in AggressivePatterns)
            {
                if (Regex.IsMatch(baseName, pattern, RegexOptions.IgnoreCase, JunkRxTimeout))
                    return new JunkReportEntry(tag, reason, "aggressive");
            }
        }

        return null;
    }


    public static string BuildJunkReport(IReadOnlyList<RomCandidate> candidates, bool aggressive)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Junk-Klassifizierungsbericht");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine();

        var junkItems = new List<(string file, JunkReportEntry reason)>();
        foreach (var c in candidates.Where(c => c.Category == "JUNK"))
        {
            var name = Path.GetFileNameWithoutExtension(c.MainPath);
            var reason = GetJunkReason(name, aggressive) ?? new JunkReportEntry("JUNK", "Klassifiziert als Junk", "core");
            junkItems.Add((Path.GetFileName(c.MainPath), reason));
        }

        var byTag = junkItems.GroupBy(j => j.reason.Tag).OrderByDescending(g => g.Count());
        foreach (var group in byTag)
        {
            sb.AppendLine($"── {group.Key} ({group.Count()} Dateien) ──");
            sb.AppendLine($"   Grund: {group.First().reason.Reason} [{group.First().reason.Level}]");
            foreach (var item in group.Take(10))
                sb.AppendLine($"   • {item.file}");
            if (group.Count() > 10)
                sb.AppendLine($"   … und {group.Count() - 10} weitere");
            sb.AppendLine();
        }

        sb.AppendLine($"Gesamt: {junkItems.Count} Junk-Dateien");
        return sb.ToString();
    }


    // ═══ COLLECTION CSV EXPORT ══════════════════════════════════════════
    // Port of CollectionCsvExport.ps1

    public static string ExportCollectionCsv(IReadOnlyList<RomCandidate> candidates, char delimiter = ';')
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Dateiname{delimiter}Konsole{delimiter}Region{delimiter}Format{delimiter}Groesse_MB{delimiter}Kategorie{delimiter}DAT_Status{delimiter}Pfad");
        foreach (var c in candidates)
        {
            sb.Append(SanitizeCsvField(Path.GetFileName(c.MainPath)));
            sb.Append(delimiter);
            sb.Append(SanitizeCsvField(DetectConsoleFromPath(c.MainPath)));
            sb.Append(delimiter);
            sb.Append(SanitizeCsvField(c.Region));
            sb.Append(delimiter);
            sb.Append(SanitizeCsvField(c.Extension));
            sb.Append(delimiter);
            sb.Append((c.SizeBytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture));
            sb.Append(delimiter);
            sb.Append(SanitizeCsvField(c.Category));
            sb.Append(delimiter);
            sb.Append(c.DatMatch ? "Verified" : "Unverified");
            sb.Append(delimiter);
            sb.AppendLine(SanitizeCsvField(c.MainPath));
        }
        return sb.ToString();
    }


    // ═══ EXCEL XML EXPORT ═══════════════════════════════════════════════
    // Port of WpfSlice.ReportPreview.ps1 - Export-WpfSummaryData -Format ExcelXml

    public static string ExportExcelXml(IReadOnlyList<RomCandidate> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");
        sb.AppendLine("<Worksheet ss:Name=\"ROMs\"><Table>");

        // Header
        sb.AppendLine("<Row>");
        foreach (var h in new[] { "Dateiname", "Konsole", "Region", "Format", "Groesse_MB", "Kategorie", "DAT", "Pfad" })
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(h)}</Data></Cell>");
        sb.AppendLine("</Row>");

        // Data
        foreach (var c in candidates)
        {
            sb.AppendLine("<Row>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(Path.GetFileName(c.MainPath))}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(DetectConsoleFromPath(c.MainPath))}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(c.Region)}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(c.Extension)}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"Number\">{(c.SizeBytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture)}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(c.Category)}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{(c.DatMatch ? "Verified" : "Unverified")}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(c.MainPath)}</Data></Cell>");
            sb.AppendLine("</Row>");
        }

        sb.AppendLine("</Table></Worksheet></Workbook>");
        return sb.ToString();
    }


    // ═══ FORMAT RULES FROM JSON ════════════════════════════════════════
    // Load data/rules.json, format rules as a readable string.

    public static string FormatRulesFromJson(string rulesPath, IReadOnlyList<RomCandidate>? candidates = null)
    {
        if (string.IsNullOrEmpty(rulesPath) || !File.Exists(rulesPath))
            return "Keine Regeldatei gefunden.";

        List<ClassificationRule>? rules;
        try
        {
            var json = File.ReadAllText(rulesPath);
            rules = JsonSerializer.Deserialize<List<ClassificationRule>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            return $"Fehler beim Laden der Regeln: {ex.Message}";
        }

        if (rules is null || rules.Count == 0)
            return "Keine Regeln definiert.";

        var sb = new StringBuilder();
        sb.AppendLine("Regel-Übersicht");
        sb.AppendLine(new string('═', 50));

        foreach (var rule in rules.OrderBy(r => r.Priority))
        {
            var status = rule.Enabled ? "aktiv" : "inaktiv";
            sb.AppendLine($"\n  [{rule.Priority}] {rule.Name} ({status})");
            sb.AppendLine($"    Aktion: {rule.Action}");
            if (!string.IsNullOrEmpty(rule.Reason))
                sb.AppendLine($"    Grund: {rule.Reason}");

            if (rule.Conditions.Count > 0)
            {
                sb.AppendLine("    Bedingungen:");
                foreach (var cond in rule.Conditions)
                    sb.AppendLine($"      • {cond.Field} {cond.Op} \"{cond.Value}\"");
            }

            if (candidates is { Count: > 0 } && rule.Enabled)
            {
                var matchCount = candidates.Count(c =>
                    rule.Conditions.All(cond => EvaluateFilter(c, cond.Field, cond.Op, cond.Value)));
                sb.AppendLine($"    Treffer: {matchCount}/{candidates.Count} Kandidaten");
            }
        }

        sb.AppendLine($"\nGesamt: {rules.Count} Regeln ({rules.Count(r => r.Enabled)} aktiv)");
        return sb.ToString();
    }


    // ═══ RULE ENGINE REPORT ═════════════════════════════════════════════

    /// <summary>
    /// Build a report of all rules from rules.json, or a default help text if not found.
    /// </summary>
    public static string BuildRuleEngineReport()
    {
        var dataDir = ResolveDataDirectory() ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var rulesPath = Path.Combine(dataDir, "rules.json");

        if (!File.Exists(rulesPath))
        {
            return "Benutzerdefinierte Regeln\n\n" +
                "Erstelle Regeln mit Bedingungen und Aktionen:\n\n" +
                "Bedingungen: Region, Format, Größe, Name, Konsole, DAT-Status\n" +
                "Operatoren: eq, neq, contains, gt, lt, regex\n" +
                "Aktionen: junk, keep, quarantine\n\n" +
                "Regeln werden nach Priorität (höher = zuerst) ausgewertet.\n" +
                "Die erste passende Regel gewinnt.\n\n" +
                "Keine rules.json gefunden.\n" +
                "Konfiguration in data/rules.json";
        }

        var json = File.ReadAllText(rulesPath);
        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        sb.AppendLine("Regel-Engine");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"\n  Datei: {rulesPath}\n");

        int idx = 0;
        foreach (var rule in doc.RootElement.EnumerateArray())
        {
            idx++;
            var name = rule.TryGetProperty("name", out var np) ? np.GetString() : $"Regel {idx}";
            var priority = rule.TryGetProperty("priority", out var pp) ? pp.GetInt32() : 0;
            var action = rule.TryGetProperty("action", out var ap) ? ap.GetString() : "?";

            sb.AppendLine($"  [{idx}] {name}  (Priorität: {priority}, Aktion: {action})");

            if (rule.TryGetProperty("conditions", out var conds))
            {
                foreach (var cond in conds.EnumerateArray())
                {
                    var field = cond.TryGetProperty("field", out var fp) ? fp.GetString() : "?";
                    var op = cond.TryGetProperty("operator", out var opp) ? opp.GetString() : "?";
                    var val = cond.TryGetProperty("value", out var vp) ? vp.GetString() : "?";
                    sb.AppendLine($"      Bedingung: {field} {op} {val}");
                }
            }
            sb.AppendLine();
        }

        if (idx == 0)
            sb.AppendLine("  Keine Regeln definiert.");

        return sb.ToString();
    }


    /// <summary>Build report data for PDF/HTML export.</summary>
    public static (ReportSummary Summary, List<ReportEntry> Entries) BuildPdfReportData(
        IReadOnlyList<RomCandidate> candidates, IReadOnlyList<DedupeResult> groups,
        RunResult? runResult, bool dryRun)
    {
        var summary = new ReportSummary
        {
            Mode = dryRun ? "DryRun" : "Move",
            TotalFiles = candidates.Count,
            KeepCount = groups.Count,
            MoveCount = groups.Sum(g => g.Losers.Count),
            JunkCount = candidates.Count(c => c.Category == "JUNK"),
            GroupCount = groups.Count,
            Duration = TimeSpan.FromMilliseconds(runResult?.DurationMs ?? 0)
        };
        // Build winner/loser lookup from groups so Action reflects actual dedupe decisions
        var loserPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
            foreach (var l in g.Losers)
                loserPaths.Add(l.MainPath);

        var entries = candidates.Select(c => new ReportEntry
        {
            GameKey = c.GameKey,
            Action = c.Category == "JUNK" ? "JUNK" : loserPaths.Contains(c.MainPath) ? "MOVE" : "KEEP",
            Category = c.Category, Region = c.Region, FilePath = c.MainPath,
            FileName = Path.GetFileName(c.MainPath), Extension = c.Extension,
            SizeBytes = c.SizeBytes, RegionScore = c.RegionScore, FormatScore = c.FormatScore,
            VersionScore = (int)c.VersionScore, DatMatch = c.DatMatch
        }).ToList();
        return (summary, entries);
    }

}
