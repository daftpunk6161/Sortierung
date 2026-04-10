using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Romulus.Contracts;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Safety;

namespace Romulus.Infrastructure.Reporting;

/// <summary>
/// Represents a single row in the cleanup report.
/// </summary>
public sealed record ReportEntry
{
    public string GameKey { get; init; } = "";
    public string Action { get; init; } = "KEEP";  // KEEP, MOVE, JUNK, BIOS
    public string Category { get; init; } = "GAME";
    public string Region { get; init; } = "UNKNOWN";
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Extension { get; init; } = "";
    public long SizeBytes { get; init; }
    public int RegionScore { get; init; }
    public int FormatScore { get; init; }
    public int VersionScore { get; init; }
    public string Console { get; init; } = "";
    public bool DatMatch { get; init; }
    public string MatchLevel { get; init; } = "None";
    public string DecisionClass { get; init; } = "Unknown";
    public string EvidenceTier { get; init; } = "Tier4_Unknown";
    public string PrimaryMatchKind { get; init; } = "None";
    public string PlatformFamily { get; init; } = "Unknown";
    public string MatchReasoning { get; init; } = "";
}

/// <summary>
/// Summary statistics for a cleanup run.
/// </summary>
public sealed record ReportSummary
{
    public string Mode { get; init; } = "DryRun";
    public string RunStatus { get; init; } = "ok";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int TotalFiles { get; init; }
    public int Candidates { get; init; }
    public int KeepCount { get; init; }
    public int DupesCount { get; init; }
    public int GamesCount { get; init; }
    public int MoveCount { get; init; }
    public int JunkCount { get; init; }
    public int BiosCount { get; init; }
    public int DatMatches { get; init; }
    public int HealthScore { get; init; }
    public int ConvertedCount { get; init; }
    public int ConvertErrorCount { get; init; }
    public int ConvertSkippedCount { get; init; }
    public int ConvertBlockedCount { get; init; }
    public int ConvertReviewCount { get; init; }
    public long ConvertSavedBytes { get; init; }
    public int DatHaveCount { get; init; }
    public int DatHaveWrongNameCount { get; init; }
    public int DatMissCount { get; init; }
    public int DatUnknownCount { get; init; }
    public int DatAmbiguousCount { get; init; }
    public int DatRenameProposedCount { get; init; }
    public int DatRenameExecutedCount { get; init; }
    public int DatRenameSkippedCount { get; init; }
    public int DatRenameFailedCount { get; init; }
    public int JunkRemovedCount { get; init; }
    public int JunkFailCount { get; init; }
    public int SkipCount { get; init; }
    public int ConsoleSortMoved { get; init; }
    public int ConsoleSortFailed { get; init; }
    public int ConsoleSortReviewed { get; init; }
    public int ConsoleSortBlocked { get; init; }
    public int ConsoleSortUnknown { get; init; }
    public int FailCount { get; init; }
    public int ErrorCount { get; init; }
    public int SkippedCount { get; init; }
    public long SavedBytes { get; init; }
    public int GroupCount { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Generates HTML and CSV reports for ROM cleanup runs.
/// Mirrors ConvertTo-HtmlReport and CSV export from Report.ps1.
/// Security: HTML-encodes all values, CSP meta-tag, no inline handlers.
/// </summary>
public static class ReportGenerator
{
    /// <summary>
    /// Generates a full HTML report.
    /// </summary>
    public static string GenerateHtml(ReportSummary summary, IReadOnlyList<ReportEntry> entries)
    {
        var sb = new StringBuilder(64 * 1024);
        var nonceBytes = RandomNumberGenerator.GetBytes(16);
        var nonce = Convert.ToBase64String(nonceBytes);

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"de\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine($"<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'nonce-{Enc(nonce)}'; script-src 'nonce-{Enc(nonce)}'; img-src data:;\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine($"<title>Romulus Report \u2014 {Enc(summary.Mode)} \u2014 {summary.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")}</title>");
        sb.AppendLine($"<style nonce=\"{Enc(nonce)}\">");
        AppendCss(sb);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Header
        sb.AppendLine("<header>");
        sb.AppendLine("<h1>� Romulus Report</h1>");
        sb.AppendLine($"<p>Modus: <strong>{Enc(summary.Mode)}</strong> \u2022 {summary.Timestamp.ToString("dd.MM.yyyy HH:mm:ss")}</p>");
        sb.AppendLine("</header>");

        // Summary cards
        AppendSummaryCards(sb, summary);

        // Category chart (simple text-based, no SVG needed)
        AppendCategoryChart(sb, summary);

        // Main table
        AppendTable(sb, entries, nonce);

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a CSV report with CSV-injection protection.
    /// </summary>
    public static string GenerateCsv(IReadOnlyList<ReportEntry> entries)
    {
        var sb = new StringBuilder();
        // V2-L15: UTF-8 BOM for Excel compatibility
        sb.Append('\uFEFF');
        sb.AppendLine("GameKey,Action,Category,Region,FilePath,FileName,Extension,SizeBytes,RegionScore,FormatScore,VersionScore,Console,DatMatch,DecisionClass,EvidenceTier,PrimaryMatchKind,PlatformFamily,MatchLevel,MatchReasoning");

        foreach (var e in entries)
        {
            sb.AppendLine(string.Join(",",
                CsvSafe(e.GameKey),
                CsvSafe(e.Action),
                CsvSafe(e.Category),
                CsvSafe(e.Region),
                CsvSafe(e.FilePath),
                CsvSafe(e.FileName),
                CsvSafe(e.Extension),
                e.SizeBytes.ToString(),
                e.RegionScore.ToString(),
                e.FormatScore.ToString(),
                e.VersionScore.ToString(),
                CsvSafe(e.Console),
                e.DatMatch ? "1" : "0",
                CsvSafe(e.DecisionClass),
                CsvSafe(e.EvidenceTier),
                CsvSafe(e.PrimaryMatchKind),
                CsvSafe(e.PlatformFamily),
                CsvSafe(e.MatchLevel),
                CsvSafe(e.MatchReasoning)));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a JSON report.
    /// </summary>
    public static string GenerateJson(ReportSummary summary, IReadOnlyList<ReportEntry> entries)
    {
        var report = new JsonReport
        {
            Summary = summary,
            Entries = entries
        };

        return JsonSerializer.Serialize(report, JsonReportContext.Default.JsonReport);
    }

    /// <summary>
    /// Writes JSON report to file with path-traversal validation.
    /// </summary>
    public static void WriteJsonToFile(string jsonPath, string workingDir, ReportSummary summary, IReadOnlyList<ReportEntry> entries)
    {
        var fullPath = SafetyValidator.EnsureSafeOutputPath(jsonPath);
        var fullDir = SafetyValidator.EnsureSafeOutputPath(workingDir).TrimEnd(Path.DirectorySeparatorChar)
                      + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Report path '{jsonPath}' is outside working directory.");

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = GenerateJson(summary, entries);
        File.WriteAllText(fullPath, json, Encoding.UTF8);
    }

    /// <summary>
    /// Writes HTML report to file with path-traversal validation.
    /// </summary>
    public static void WriteHtmlToFile(string htmlPath, string workingDir, ReportSummary summary, IReadOnlyList<ReportEntry> entries)
    {
        var fullPath = SafetyValidator.EnsureSafeOutputPath(htmlPath);
        var fullDir = SafetyValidator.EnsureSafeOutputPath(workingDir).TrimEnd(Path.DirectorySeparatorChar)
                      + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Report path '{htmlPath}' is outside working directory.");

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var html = GenerateHtml(summary, entries);
        File.WriteAllText(fullPath, html, Encoding.UTF8);
    }

    private static void AppendCss(StringBuilder sb)
    {
        sb.AppendLine(@"
:root { --bg: #1e1e2e; --surface: #313244; --text: #cdd6f4; --blue: #89b4fa;
        --green: #a6e3a1; --red: #f38ba8; --yellow: #f9e2af; --mauve: #cba6f7; }
* { margin: 0; padding: 0; box-sizing: border-box; }
body { background: var(--bg); color: var(--text); font-family: 'Segoe UI',system-ui,sans-serif; padding: 2rem; }
header { margin-bottom: 2rem; }
h1 { color: var(--blue); font-size: 1.8rem; }
h2 { color: var(--mauve); margin: 1.5rem 0 0.8rem; }
.cards { display: flex; gap: 1rem; flex-wrap: wrap; margin-bottom: 2rem; }
.card { background: var(--surface); border-radius: 8px; padding: 1rem 1.5rem; min-width: 120px; text-align: center; }
.card .value { font-size: 2rem; font-weight: bold; }
.card .label { font-size: 0.85rem; opacity: 0.7; }
.card.keep .value { color: var(--green); }
.card.move .value { color: var(--yellow); }
.card.junk .value { color: var(--red); }
.card.bios .value { color: var(--mauve); }
.card.dat .value { color: var(--blue); }
.bar-chart { margin: 1rem 0 2rem; }
.bar-row { display: flex; align-items: center; margin: 4px 0; }
.bar-label { width: 60px; text-align: right; padding-right: 8px; font-size: 0.85rem; }
.bar { height: 22px; border-radius: 4px; min-width: 2px; transition: width 0.3s; }
.bar-value { padding-left: 8px; font-size: 0.85rem; }
table { width: 100%; border-collapse: collapse; margin-top: 1rem; }
th, td { padding: 6px 10px; text-align: left; border-bottom: 1px solid #45475a; font-size: 0.85rem; }
th { background: var(--surface); position: sticky; top: 0; cursor: pointer; user-select: none; }
th:hover { color: var(--blue); }
tr:hover { background: rgba(137,180,250,0.05); }
.action-keep { color: var(--green); }
.action-move { color: var(--yellow); }
.action-junk { color: var(--red); }
.action-bios { color: var(--mauve); }
");
    }

    private static void AppendSummaryCards(StringBuilder sb, ReportSummary s)
    {
        sb.AppendLine("<div class=\"cards\">");
        AppendCard(sb, "", $"{s.HealthScore}%", "Health");
        AppendCard(sb, "keep", s.KeepCount.ToString(), "Spiele (KEEP)");
        AppendCard(sb, "move", s.MoveCount.ToString(), "Duplikate");
        AppendCard(sb, "junk", s.JunkCount.ToString(), "Junk");
        AppendCard(sb, "bios", s.BiosCount.ToString(), "BIOS");
        AppendCard(sb, "", s.GamesCount.ToString(), "Games");
        AppendCard(sb, "dat", s.DatMatches.ToString(), "DAT Matches");
        if (s.DatHaveCount > 0)
            AppendCard(sb, "dat", s.DatHaveCount.ToString(), "DAT Have");
        if (s.DatHaveWrongNameCount > 0)
            AppendCard(sb, "dat", s.DatHaveWrongNameCount.ToString(), "DAT WrongName");
        if (s.DatMissCount > 0)
            AppendCard(sb, "dat", s.DatMissCount.ToString(), "DAT Miss");
        if (s.DatUnknownCount > 0)
            AppendCard(sb, "dat", s.DatUnknownCount.ToString(), "DAT Unknown");
        if (s.DatAmbiguousCount > 0)
            AppendCard(sb, "dat", s.DatAmbiguousCount.ToString(), "DAT Ambiguous");
        if (s.DatRenameProposedCount > 0)
            AppendCard(sb, "dat", s.DatRenameProposedCount.ToString(), "DAT Rename Proposed");
        if (s.DatRenameExecutedCount > 0)
            AppendCard(sb, "dat", s.DatRenameExecutedCount.ToString(), "DAT Rename Executed");
        if (s.DatRenameFailedCount > 0)
            AppendCard(sb, "dat", s.DatRenameFailedCount.ToString(), "DAT Rename Failed");
        AppendCard(sb, "", s.Candidates.ToString(), "Kandidaten");
        if (s.ConvertedCount > 0)
            AppendCard(sb, "", s.ConvertedCount.ToString(), "Konvertiert");
        if (s.ConvertSkippedCount > 0)
            AppendCard(sb, "", s.ConvertSkippedCount.ToString(), "Convert-Skip");
        if (s.ConvertBlockedCount > 0)
            AppendCard(sb, "", s.ConvertBlockedCount.ToString(), "Convert-Blocked");
        if (s.ConvertReviewCount > 0)
            AppendCard(sb, "", s.ConvertReviewCount.ToString(), "Convert-Review");
        if (s.ConvertSavedBytes != 0)
            AppendCard(sb, "", FormatSize(s.ConvertSavedBytes), "Convert-Gespart");
        if (s.ConvertErrorCount > 0)
            AppendCard(sb, "junk", s.ConvertErrorCount.ToString(), "Convert-Fehler");
        if (s.ConsoleSortReviewed > 0)
            AppendCard(sb, "", s.ConsoleSortReviewed.ToString(), "Sort-Review");
        if (s.ConsoleSortBlocked > 0)
            AppendCard(sb, "", s.ConsoleSortBlocked.ToString(), "Sort-Blocked");
        if (s.ConsoleSortUnknown > 0)
            AppendCard(sb, "", s.ConsoleSortUnknown.ToString(), "Sort-Unknown");
        if (s.ErrorCount > 0)
            AppendCard(sb, "junk", s.ErrorCount.ToString(), "Fehler");
        AppendCard(sb, "", s.RunStatus, "Status");
        AppendCard(sb, "", FormatSize(s.SavedBytes), "Gespart");
        AppendCard(sb, "", s.TotalFiles.ToString(), "Gescannt");
        AppendCard(sb, "", s.Duration.ToString(@"mm\:ss"), "Laufzeit");
        sb.AppendLine("</div>");
    }

    private static void AppendCard(StringBuilder sb, string cls, string value, string label)
    {
        sb.AppendLine($"<div class=\"card {cls}\"><div class=\"value\">{Enc(value)}</div><div class=\"label\">{Enc(label)}</div></div>");
    }

    private static void AppendCategoryChart(StringBuilder sb, ReportSummary s)
    {
        sb.AppendLine("<h2>Verteilung</h2>");
        sb.AppendLine("<div class=\"bar-chart\">");

        int max = Math.Max(1, Math.Max(s.KeepCount, Math.Max(s.MoveCount, Math.Max(s.JunkCount, s.BiosCount))));
        AppendBar(sb, "Keep", s.KeepCount, max, "var(--green)");
        AppendBar(sb, "Move", s.MoveCount, max, "var(--yellow)");
        AppendBar(sb, "Junk", s.JunkCount, max, "var(--red)");
        AppendBar(sb, "BIOS", s.BiosCount, max, "var(--mauve)");

        sb.AppendLine("</div>");
    }

    private static void AppendBar(StringBuilder sb, string label, int count, int max, string color)
    {
        int pct = max > 0 ? (int)(count * 100.0 / max) : 0;
        sb.AppendLine($"<div class=\"bar-row\"><span class=\"bar-label\">{Enc(label)}</span><div class=\"bar\" style=\"width:{Math.Max(pct, 1)}%;background:{color}\"></div><span class=\"bar-value\">{count}</span></div>");
    }

    private static void AppendTable(StringBuilder sb, IReadOnlyList<ReportEntry> entries, string nonce)
    {
        // TASK-174: Info banner for UNKNOWN files
        var unknownCount = entries.Count(e => string.Equals(e.Category, "UNKNOWN", StringComparison.OrdinalIgnoreCase));
        if (unknownCount > 0)
        {
            sb.AppendLine($"<div class=\"unknown-info\" style=\"background:var(--surface-1);border-left:4px solid var(--yellow);padding:12px 16px;margin:0 0 16px 0;border-radius:6px\">");
            sb.AppendLine($"<strong>&#9432; {unknownCount} Datei(en) mit Klassifizierung UNKNOWN</strong><br>");
            sb.AppendLine("UNKNOWN bedeutet, dass die Datei keiner bekannten Konsole oder Kategorie zugeordnet werden konnte. ");
            sb.AppendLine("M\u00f6gliche Ursachen: nicht standardkonformer Dateiname, unbekanntes Format oder fehlende DAT-Abdeckung. ");
            sb.AppendLine("Empfehlung: Dateiname pr\u00fcfen, DAT-Verzeichnis aktualisieren oder Datei manuell zuordnen.");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("<h2>Details</h2>");
        sb.AppendLine("<table id=\"reportTable\">");
        sb.AppendLine("<thead><tr>");
        sb.AppendLine("<th>GameKey</th><th>Action</th><th>Category</th><th>Region</th><th>FileName</th><th>Ext</th><th>Size</th><th>Console</th><th>DAT</th><th>Decision</th><th>Tier</th><th>Kind</th><th>Family</th><th>MatchLevel</th><th>Reasoning</th>");
        sb.AppendLine("</tr></thead>");
        sb.AppendLine("<tbody>");

        foreach (var e in entries)
        {
            var actionClass = e.Action.ToUpperInvariant() switch
            {
                "KEEP" => "action-keep",
                "MOVE" => "action-move",
                "JUNK" => "action-junk",
                "BIOS" => "action-bios",
                _ => ""
            };

            sb.Append("<tr>");
            sb.Append($"<td>{Enc(e.GameKey)}</td>");
            sb.Append($"<td class=\"{actionClass}\">{Enc(e.Action)}</td>");
            // TASK-174: Tooltip on UNKNOWN category cells
            if (string.Equals(e.Category, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
                sb.Append($"<td title=\"Keine Konsole/Kategorie erkannt. Dateiname pr\u00fcfen, DAT aktualisieren oder manuell zuordnen.\">{Enc(e.Category)}</td>");
            else
                sb.Append($"<td>{Enc(e.Category)}</td>");
            sb.Append($"<td>{Enc(e.Region)}</td>");
            sb.Append($"<td title=\"{Enc(e.FilePath)}\">{Enc(e.FileName)}</td>");
            sb.Append($"<td>{Enc(e.Extension)}</td>");
            sb.Append($"<td>{Enc(FormatSize(e.SizeBytes))}</td>");
            sb.Append($"<td>{Enc(e.Console)}</td>");
            sb.Append($"<td>{(e.DatMatch ? "✓" : "")}</td>");
            sb.Append($"<td>{Enc(e.DecisionClass)}</td>");
            sb.Append($"<td>{Enc(e.EvidenceTier)}</td>");
            sb.Append($"<td>{Enc(e.PrimaryMatchKind)}</td>");
            sb.Append($"<td>{Enc(e.PlatformFamily)}</td>");
            sb.Append($"<td>{Enc(e.MatchLevel)}</td>");
            sb.Append($"<td title=\"{Enc(e.MatchReasoning)}\">{Enc(e.MatchReasoning)}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table>");

        // Sort script (event delegation, no inline handlers)
        sb.AppendLine($"<script nonce=\"{Enc(nonce)}\">");
        sb.AppendLine(@"document.getElementById('reportTable').addEventListener('click',function(e){
  if(e.target.tagName!=='TH')return;
  var t=e.target.closest('table'),b=t.tBodies[0],rows=Array.from(b.rows),
      ci=Array.from(e.target.parentNode.children).indexOf(e.target),
      asc=e.target.dataset.sort!=='asc';
  rows.sort(function(a,b){var x=a.cells[ci].textContent,y=b.cells[ci].textContent;
    return asc?x.localeCompare(y,undefined,{numeric:true}):y.localeCompare(x,undefined,{numeric:true});});
  rows.forEach(function(r){b.appendChild(r);});
  e.target.dataset.sort=asc?'asc':'desc';
});");
        sb.AppendLine("</script>");
    }

    /// <summary>HTML-encode a value to prevent XSS.</summary>
    private static string Enc(string value) =>
        WebUtility.HtmlEncode(value ?? "");

    /// <summary>CSV-safe value: delegates to central AuditCsvParser for consistent CSV injection prevention.</summary>
    private static string CsvSafe(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        var valueForCsv = HasDangerousFormulaPrefix(value)
            ? "'" + value
            : value;

        var sanitized = AuditCsvParser.SanitizeCsvField(valueForCsv);
        // Ensure quoted for CSV consistency
        if (!sanitized.StartsWith('"'))
            return "\"" + sanitized.Replace("\"", "\"\"") + "\"";
        return sanitized;
    }

    private static bool HasDangerousFormulaPrefix(string value)
        => !string.IsNullOrEmpty(value)
            && value[0] is '=' or '+' or '-' or '@';

    private static string FormatSize(long bytes)
        => Formatting.FormatSize(bytes);
}

/// <summary>
/// Wrapper record for JSON report serialization.
/// </summary>
public sealed record JsonReport
{
    public ReportSummary Summary { get; init; } = new();
    public IReadOnlyList<ReportEntry> Entries { get; init; } = [];
}

/// <summary>
/// Source-generated JSON serializer context for ReportGenerator.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(JsonReport))]
internal partial class JsonReportContext : JsonSerializerContext
{
}
