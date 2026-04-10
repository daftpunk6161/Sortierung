using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Tools;
using Romulus.Infrastructure.Reporting;

namespace Romulus.UI.Wpf.Services;

public static partial class FeatureService
{

    // ═══ JUNK REPORT ════════════════════════════════════════════════════
    // Port of JunkReport.ps1
    // Pattern definitions live in CollectionExportService (single source of truth).

    public static JunkReportEntry? GetJunkReason(string baseName, bool aggressive)
        => CollectionExportService.GetJunkReason(baseName, aggressive);


    public static string BuildJunkReport(IReadOnlyList<RomCandidate> candidates, bool aggressive)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Junk-Klassifizierungsbericht");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine();

        var junkItems = new List<(string file, JunkReportEntry reason)>();
        foreach (var c in candidates.Where(c => c.Category == FileCategory.Junk))
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
        => CollectionExportService.ExportCollectionCsv(candidates, delimiter, CollectionTabularExportLabels.German);


    // ═══ EXCEL XML EXPORT ═══════════════════════════════════════════════
    // Port of WpfSlice.ReportPreview.ps1 - Export-WpfSummaryData -Format ExcelXml

    public static string ExportExcelXml(IReadOnlyList<RomCandidate> candidates)
        => CollectionExportService.ExportExcelXml(candidates, CollectionTabularExportLabels.German);


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

        JsonElement rulesElement;
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            rulesElement = doc.RootElement;
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (doc.RootElement.TryGetProperty("rules", out var lowerRules) && lowerRules.ValueKind == JsonValueKind.Array)
            {
                rulesElement = lowerRules;
            }
            else if (doc.RootElement.TryGetProperty("Rules", out var upperRules) && upperRules.ValueKind == JsonValueKind.Array)
            {
                rulesElement = upperRules;
            }
            else
            {
                sb.AppendLine("  Keine Regel-Liste im erwarteten Format gefunden.");
                return sb.ToString();
            }
        }
        else
        {
            sb.AppendLine("  Ungültiges rules.json Format.");
            return sb.ToString();
        }

        int idx = 0;
        foreach (var rule in rulesElement.EnumerateArray())
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


    /// <summary>Build report data for HTML export.</summary>
    public static (ReportSummary Summary, List<ReportEntry> Entries) BuildHtmlReportData(
        IReadOnlyList<RomCandidate> candidates, IReadOnlyList<DedupeGroup> groups,
        RunResult? runResult, bool dryRun)
        => CollectionExportService.BuildReportData(candidates, groups, runResult, dryRun);

}
