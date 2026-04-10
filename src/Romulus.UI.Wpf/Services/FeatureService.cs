using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Tools;
using Romulus.Infrastructure.Reporting;

namespace Romulus.UI.Wpf.Services;

/// Backend logic for all WPF feature buttons.
/// Port of PowerShell modules: ConversionEstimate, JunkReport, DuplicateHeatmap,
/// CompletenessTracker, HeaderAnalysis, CollectionCsvExport, FilterBuilder, etc.
/// </summary>
public static partial class FeatureService
{
    internal static string ToCategoryLabel(FileCategory category)
        => CollectionAnalysisService.ToCategoryLabel(category);

    internal static FileCategory ParseCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return FileCategory.Game;

        return Enum.TryParse<FileCategory>(value, ignoreCase: true, out var category)
            ? category
            : FileCategory.Unknown;
    }

    /// <summary>
    /// Safely load an XDocument with XXE/DTD processing disabled.
    /// </summary>
    internal static XDocument SafeLoadXDocument(string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using var reader = XmlReader.Create(path, settings);
        return XDocument.Load(reader);
    }

    internal static List<string> LoadDatGameNames(string path)
        => DatAnalysisService.LoadDatGameNames(path);

    internal static Dictionary<string, string> BuildGameElementMap(XDocument doc)
        => DatAnalysisService.BuildGameElementMap(doc);


    internal static string DetectConsoleFromPath(string path)
        => CollectionAnalysisService.DetectConsoleFromPath(path);

    internal static string ResolveConsoleLabel(RomCandidate candidate)
        => CollectionAnalysisService.ResolveConsoleLabel(candidate);


    internal static string SanitizeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // CSV injection protection (OWASP)
        if (value[0] is '=' or '+' or '@' or '\t' or '\r')
            value = "'" + value;
        else if (value[0] == '-' && !IsPlainNegativeNumber(value))
            value = "'" + value;
        if (value.Contains('"') || value.Contains(';') || value.Contains(','))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    internal static bool IsPlainNegativeNumber(string value)
    {
        if (value.Length < 2 || value[0] != '-') return false;
        for (int i = 1; i < value.Length; i++)
        {
            if (!char.IsDigit(value[i]) && value[i] != '.')
                return false;
        }
        return true;
    }


    internal static string ComputeSha256(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }


    internal static string Truncate(string s, int max) =>
        s.Length <= max ? s : string.Concat(s.AsSpan(0, max - 3), "...");


    internal static int LevenshteinDistance(string s, string t)
    {
        var n = s.Length;
        var m = t.Length;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;
        for (var i = 1; i <= n; i++)
            for (var j = 1; j <= m; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        return d[n, m];
    }


    // ═══ HELPER ═════════════════════════════════════════════════════════

    public static string FormatSize(long bytes)
        => Romulus.Contracts.Formatting.FormatSize(bytes);


    internal static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                { current.Append('"'); i++; }
                else if (ch == '"') inQuotes = false;
                else current.Append(ch);
            }
            else
            {
                if (ch == '"') inQuotes = true;
                else if (ch == ',') { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(ch);
            }
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

}


// DryRunCompareResult stays in WPF layer because it depends on ReportEntry (Infrastructure).
public sealed record DryRunCompareResult(
    IReadOnlyList<Romulus.Infrastructure.Reporting.ReportEntry> OnlyInA,
    IReadOnlyList<Romulus.Infrastructure.Reporting.ReportEntry> OnlyInB,
    IReadOnlyList<(Romulus.Infrastructure.Reporting.ReportEntry left, Romulus.Infrastructure.Reporting.ReportEntry right)> Different,
    int Identical);
