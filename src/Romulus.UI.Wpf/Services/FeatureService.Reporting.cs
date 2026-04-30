using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Tools;
using Romulus.Infrastructure.Watch;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Romulus.UI.Wpf.Services;

public static partial class FeatureService
{

    // ═══ JUNK REPORT ════════════════════════════════════════════════════
    // Port of JunkReport.ps1
    // Classification reason codes come from FileClassifier via CollectionExportService.

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
            return GetLocalizedString(
                "Cmd.RuleEngine.DefaultHelp",
                "Custom Rules\n\n" +
                "Create rules with conditions and actions:\n\n" +
                "Conditions: Region, Format, Size, Name, Console, DAT status\n" +
                "Operators: eq, neq, contains, gt, lt, regex\n" +
                "Actions: junk, keep, quarantine\n\n" +
                "Rules are evaluated by priority (higher = first).\n" +
                "The first matching rule wins.\n\n" +
                "No rules.json found.\n" +
                "Configuration in data/rules.json");
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



    // ═══ HEADER ANALYSIS ════════════════════════════════════════════════
    // Port of HeaderAnalysis.ps1

    public static RomHeaderInfo? AnalyzeHeader(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            using var fs = File.OpenRead(filePath);
            var header = new byte[Math.Min(65536, fs.Length)];
            _ = fs.Read(header, 0, header.Length);
            var analyzed = HeaderAnalyzer.AnalyzeHeader(header, fs.Length);
            return analyzed is null
                ? null
                : new RomHeaderInfo(analyzed.Platform, analyzed.Format, analyzed.Details);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            System.Diagnostics.Debug.WriteLine($"[FeatureService] AnalyzeHeader failed for '{filePath}': {ex.Message}");
            return null;
        }
    }


    // ═══ TREND ANALYSIS ═════════════════════════════════════════════════
    // Port of TrendAnalysis.ps1

    public static void SaveTrendSnapshot(int totalFiles, long sizeBytes, int verified, int dupes, int junk)
        => IntegrityService.SaveTrendSnapshot(totalFiles, sizeBytes, verified, dupes, junk);


    public static List<TrendSnapshot> LoadTrendHistory()
        => IntegrityService.LoadTrendHistory();


    public static string FormatTrendReport(List<TrendSnapshot> history)
        => RunHistoryTrendService.FormatTrendReport(
            history,
            title: "Trend-Analyse",
            emptyMessage: "Keine Trend-Daten vorhanden.",
            currentLabel: "Aktuell",
            deltaFilesLabel: "Δ Dateien",
            deltaDuplicatesLabel: "Δ Duplikate",
            historyLabel: "Verlauf (letzte 10):",
            filesLabel: "Dateien",
            qualityLabel: "Quality");


    // ═══ INTEGRITY MONITOR ══════════════════════════════════════════════
    // Port of IntegrityMonitor.ps1
    // Baseline stores paths relative to the common root for portability.

    public static async Task<Dictionary<string, IntegrityEntry>> CreateBaseline(
        IReadOnlyList<string> filePaths, IProgress<string>? progress = null, CancellationToken ct = default)
        => await IntegrityService.CreateBaseline(filePaths, progress, ct);


    public static async Task<IntegrityCheckResult> CheckIntegrity(IProgress<string>? progress = null, CancellationToken ct = default)
        => await IntegrityService.CheckIntegrity(progress, ct);


    // ═══ BACKUP MANAGER ═════════════════════════════════════════════════
    // Port of BackupManager.ps1

    public static string CreateBackup(IReadOnlyList<string> filePaths, string backupRoot, string label)
        => IntegrityService.CreateBackup(filePaths, backupRoot, label);


    internal static string? FindCommonRoot(IReadOnlyList<string> paths)
        => IntegrityService.FindCommonRoot(paths);


    public static int CleanupOldBackups(string backupRoot, int retentionDays, Func<int, bool>? confirmDelete = null)
        => IntegrityService.CleanupOldBackups(backupRoot, retentionDays, confirmDelete);


    // ═══ SORT TEMPLATES ═════════════════════════════════════════════════
    // Port of SortTemplates.ps1

    public static Dictionary<string, string> GetSortTemplates()
    {
        var ext = UiLookupData.Instance.SortTemplates;
        if (ext.Count > 0) return new(ext);
        return new()
        {
            ["Standard"] = "{console}/{filename}",
            ["Lowercase"] = "roms/{console_lower}/{filename}",
            ["Capitalized"] = "Games/{console}/{filename}",
            ["LowercasePath"] = "share/roms/{console_lower}/{filename}",
            ["Flat"] = "{filename}"
        };
    }


    // ═══ CRON TESTER ═════════════════════════════════════════════════════
    // Port of CronTester (formerly SchedulerAdvanced.ps1)

    public static bool TestCronMatch(string cronExpression, DateTime dt)
        => CronScheduleEvaluator.TestCronMatch(cronExpression, dt);


    internal static bool CronFieldMatch(string field, int value)
        => CronScheduleEvaluator.CronFieldMatch(field, value);


    // ═══ CSV FIELD EXTRACTION ══════════════════════════════════════════

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



    // ═══ CONFIG DIFF ════════════════════════════════════════════════════
    // Port of ConfigMerge.ps1

    public static List<ConfigDiffEntry> GetConfigDiff(Dictionary<string, string> current, Dictionary<string, string> saved)
    {
        var result = new List<ConfigDiffEntry>();
        var allKeys = current.Keys.Union(saved.Keys).Distinct();
        foreach (var key in allKeys)
        {
            current.TryGetValue(key, out var curVal);
            saved.TryGetValue(key, out var savedVal);
            if (curVal != savedVal)
                result.Add(new ConfigDiffEntry(key, savedVal ?? "(fehlt)", curVal ?? "(fehlt)"));
        }
        return result;
    }


    // ═══ LOCALIZATION ═══════════════════════════════════════════════════
    // Port of Localization.ps1

    public static Dictionary<string, string> LoadLocale(string locale)
    {
        var dataDir = ResolveDataDirectory("i18n");
        if (dataDir is null)
            return new Dictionary<string, string>();

        var path = Path.Combine(dataDir, $"{locale}.json");
        if (!File.Exists(path))
            return new Dictionary<string, string>();

        try
        {
            var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
            if (json is null) return new Dictionary<string, string>();
            return json.Where(kv => kv.Value.ValueKind == JsonValueKind.String)
                       .ToDictionary(kv => kv.Key, kv => kv.Value.GetString() ?? "");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[FeatureService] Locale load failed for '{locale}': {ex.Message}");
            return new Dictionary<string, string>();
        }
    }

    internal static string GetLocalizedString(string key, string fallback)
    {
        var locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var localized = LoadLocale(locale);
        if (localized.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        var englishFallback = LoadLocale("en");
        if (englishFallback.TryGetValue(key, out var englishValue) && !string.IsNullOrWhiteSpace(englishValue))
            return englishValue;

        return fallback;
    }

    internal static string GetLocalizedFormat(string key, string fallback, params object[] args)
    {
        var template = GetLocalizedString(key, fallback);
        return string.Format(CultureInfo.CurrentCulture, template, args);
    }


    /// <summary>Resolve the data/ subdirectory, probing from BaseDirectory upward (max 5 levels).</summary>
    internal static string? ResolveDataDirectory(string? subFolder = null)
    {
        var baseDataDir = RunEnvironmentBuilder.TryResolveDataDir();
        if (string.IsNullOrWhiteSpace(baseDataDir))
            return null;

        if (subFolder is null)
            return baseDataDir;

        var resolvedSubFolder = Path.Combine(baseDataDir, subFolder);
        return Directory.Exists(resolvedSubFolder) ? resolvedSubFolder : null;
    }


    // ═══ PORTABLE MODE CHECK ════════════════════════════════════════════
    // Port of PortableMode.ps1
    // NOTE: The marker file ".portable" is checked relative to AppContext.BaseDirectory,
    // which is the directory containing the executable (e.g. bin/Debug/net10.0-windows/).
    // Place ".portable" next to the .exe, NOT the workspace root.

    public static bool IsPortableMode()
    {
        return AppStoragePathResolver.IsPortableMode();
    }


    // ═══ ACCESSIBILITY ══════════════════════════════════════════════════
    // Port of Accessibility.ps1

    public static bool IsHighContrastActive()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Control Panel\Accessibility\HighContrast");
            var flags = key?.GetValue("Flags") as string;
            return flags is not null && int.TryParse(flags, out var f) && (f & 1) != 0;
        }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"High-contrast detection failed: {ex.GetType().Name}"); return false; }
    }


    // ═══ MOBILE WEB UI ══════════════════════════════════════════════════

    /// <summary>Try to find the API project path.</summary>
    public static string? FindApiProjectPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Romulus.Api", "Romulus.Api.csproj"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Romulus.Api", "Romulus.Api.csproj")
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }



}
