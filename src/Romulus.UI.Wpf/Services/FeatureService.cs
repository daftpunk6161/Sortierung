using System.Globalization;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Audit;
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
        => AuditCsvParser.SanitizeLegacyUiCsvField(value);

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
        => StringUtils.LevenshteinDistance(s, t);

    internal static bool ResolveStrictDatSidecarValidation()
    {
        var dataDir = ResolveDataDirectory()
            ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        return settings.Dat.StrictSidecarValidation;
    }


    // ═══ HELPER ═════════════════════════════════════════════════════════

    public static string FormatSize(long bytes)
        => Romulus.Contracts.Formatting.FormatSize(bytes);


    internal static string[] ParseCsvLine(string line)
        => AuditCsvParser.ParseCsvLine(line);

}


// DryRunCompareResult stays in WPF layer because it depends on ReportEntry (Infrastructure).
public sealed record DryRunCompareResult(
    IReadOnlyList<Romulus.Infrastructure.Reporting.ReportEntry> OnlyInA,
    IReadOnlyList<Romulus.Infrastructure.Reporting.ReportEntry> OnlyInB,
    IReadOnlyList<(Romulus.Infrastructure.Reporting.ReportEntry left, Romulus.Infrastructure.Reporting.ReportEntry right)> Different,
    int Identical);
