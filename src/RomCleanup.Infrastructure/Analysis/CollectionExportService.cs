using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RomCleanup.Contracts;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.Reporting;

namespace RomCleanup.Infrastructure.Analysis;

/// <summary>
/// Collection export operations extracted from FeatureService.Export.
/// Pure logic + file I/O, no GUI dependency.
/// </summary>
public static class CollectionExportService
{
    public static CollectionTabularExportLabels EnglishLabels { get; } = new();

    private static readonly (string pattern, string tag, string reason)[] JunkPatterns =
    [
        (@"\(Beta[^)]*\)", "Beta", "Beta version"),
        (@"\(Proto[^)]*\)", "Proto", "Prototype"),
        (@"\(Demo[^)]*\)", "Demo", "Demo version"),
        (@"\(Sample\)", "Sample", "Sample"),
        (@"\(Homebrew\)", "Homebrew", "Homebrew"),
        (@"\(Hack\)", "Hack", "ROM hack"),
        (@"\(Unl\)", "Unlicensed", "Unlicensed"),
        (@"\(Aftermarket\)", "Aftermarket", "Aftermarket"),
        (@"\(Pirate\)", "Pirate", "Pirate"),
        (@"\(Program\)", "Program", "Program/Utility"),
        (@"\[b\d*\]", "[b]", "Bad dump"),
        (@"\[h\d*\]", "[h]", "Hack tag"),
        (@"\[o\d*\]", "[o]", "Overdump"),
        (@"\[t\d*\]", "[t]", "Trainer"),
        (@"\[f\d*\]", "[f]", "Fixed"),
        (@"\[T[\+\-]", "[T]", "Translation")
    ];

    private static readonly (string pattern, string tag, string reason)[] AggressivePatterns =
    [
        (@"\(Alt[^)]*\)", "Alt", "Alternative version"),
        (@"\(Bonus Disc\)", "Bonus", "Bonus disc"),
        (@"\(Reprint\)", "Reprint", "Reprint"),
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
        sb.AppendLine("Junk Classification Report");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine();

        var junkItems = new List<(string file, JunkReportEntry reason)>();
        foreach (var c in candidates.Where(c => c.Category == FileCategory.Junk))
        {
            var name = Path.GetFileNameWithoutExtension(c.MainPath);
            var reason = GetJunkReason(name, aggressive) ?? new JunkReportEntry("JUNK", "Classified as junk", "core");
            junkItems.Add((Path.GetFileName(c.MainPath), reason));
        }

        var byTag = junkItems.GroupBy(j => j.reason.Tag).OrderByDescending(g => g.Count());
        foreach (var group in byTag)
        {
            sb.AppendLine($"-- {group.Key} ({group.Count()} files) --");
            sb.AppendLine($"   Reason: {group.First().reason.Reason} [{group.First().reason.Level}]");
            foreach (var item in group.Take(10))
                sb.AppendLine($"   - {item.file}");
            if (group.Count() > 10)
                sb.AppendLine($"   ... and {group.Count() - 10} more");
            sb.AppendLine();
        }

        sb.AppendLine($"Total: {junkItems.Count} junk files");
        return sb.ToString();
    }

    public static string ExportCollectionCsv(
        IReadOnlyList<RomCandidate> candidates,
        char delimiter = ';',
        CollectionTabularExportLabels? labels = null)
    {
        labels ??= EnglishLabels;
        var sb = new StringBuilder();
        sb.AppendLine($"{labels.FileName}{delimiter}{labels.Console}{delimiter}{labels.Region}{delimiter}{labels.Format}{delimiter}{labels.SizeMb}{delimiter}{labels.Category}{delimiter}{labels.DatStatus}{delimiter}{labels.Path}");
        foreach (var c in candidates)
        {
            sb.Append(AuditCsvParser.SanitizeCsvField(Path.GetFileName(c.MainPath)));
            sb.Append(delimiter);
            sb.Append(AuditCsvParser.SanitizeCsvField(CollectionAnalysisService.ResolveConsoleLabel(c)));
            sb.Append(delimiter);
            sb.Append(AuditCsvParser.SanitizeCsvField(c.Region));
            sb.Append(delimiter);
            sb.Append(AuditCsvParser.SanitizeCsvField(c.Extension));
            sb.Append(delimiter);
            sb.Append((c.SizeBytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture));
            sb.Append(delimiter);
            sb.Append(AuditCsvParser.SanitizeCsvField(CollectionAnalysisService.ToCategoryLabel(c.Category)));
            sb.Append(delimiter);
            sb.Append(c.DatMatch ? "Verified" : "Unverified");
            sb.Append(delimiter);
            sb.AppendLine(AuditCsvParser.SanitizeCsvField(c.MainPath));
        }
        return sb.ToString();
    }

    public static string ExportExcelXml(
        IReadOnlyList<RomCandidate> candidates,
        CollectionTabularExportLabels? labels = null)
    {
        labels ??= EnglishLabels;
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");
        sb.AppendLine("<Worksheet ss:Name=\"ROMs\"><Table>");

        sb.AppendLine("<Row>");
        foreach (var h in new[] { labels.FileName, labels.Console, labels.Region, labels.Format, labels.SizeMb, labels.Category, labels.DatStatusShort, labels.Path })
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(h)}</Data></Cell>");
        sb.AppendLine("</Row>");

        foreach (var c in candidates)
        {
            sb.AppendLine("<Row>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(Path.GetFileName(c.MainPath))}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(CollectionAnalysisService.ResolveConsoleLabel(c))}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(c.Region)}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(c.Extension)}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"Number\">{(c.SizeBytes / 1048576.0).ToString("F2", CultureInfo.InvariantCulture)}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(CollectionAnalysisService.ToCategoryLabel(c.Category))}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{(c.DatMatch ? "Verified" : "Unverified")}</Data></Cell>");
            sb.AppendLine($"<Cell><Data ss:Type=\"String\">{SecurityElement.Escape(c.MainPath)}</Data></Cell>");
            sb.AppendLine("</Row>");
        }

        sb.AppendLine("</Table></Worksheet></Workbook>");
        return sb.ToString();
    }

    public static (ReportSummary Summary, List<ReportEntry> Entries) BuildReportData(
        IReadOnlyList<RomCandidate> candidates, IReadOnlyList<DedupeGroup> groups,
        RunResult? runResult, bool dryRun)
    {
        var summary = new ReportSummary
        {
            Mode = dryRun ? "DryRun" : "Move",
            TotalFiles = candidates.Count,
            KeepCount = groups.Count,
            MoveCount = groups.Sum(g => g.Losers.Count),
            JunkCount = candidates.Count(c => c.Category == FileCategory.Junk),
            GroupCount = groups.Count,
            Duration = TimeSpan.FromMilliseconds(runResult?.DurationMs ?? 0)
        };

        var loserPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
            foreach (var l in g.Losers)
                loserPaths.Add(l.MainPath);

        var entries = candidates.Select(c => new ReportEntry
        {
            GameKey = c.GameKey,
            Action = c.Category == FileCategory.Junk ? "JUNK" : loserPaths.Contains(c.MainPath) ? "MOVE" : "KEEP",
            Category = CollectionAnalysisService.ToCategoryLabel(c.Category),
            Region = c.Region,
            FilePath = c.MainPath,
            FileName = Path.GetFileName(c.MainPath),
            Extension = c.Extension,
            SizeBytes = c.SizeBytes,
            RegionScore = c.RegionScore,
            FormatScore = c.FormatScore,
            VersionScore = (int)c.VersionScore,
            DatMatch = c.DatMatch
        }).ToList();
        return (summary, entries);
    }
}

public sealed record CollectionTabularExportLabels
{
    public string FileName { get; init; } = "FileName";
    public string Console { get; init; } = "Console";
    public string Region { get; init; } = "Region";
    public string Format { get; init; } = "Format";
    public string SizeMb { get; init; } = "Size_MB";
    public string Category { get; init; } = "Category";
    public string DatStatus { get; init; } = "DAT_Status";
    public string DatStatusShort { get; init; } = "DAT";
    public string Path { get; init; } = "Path";

    public static CollectionTabularExportLabels German { get; } = new()
    {
        FileName = "Dateiname",
        Console = "Konsole",
        Region = "Region",
        Format = "Format",
        SizeMb = "Groesse_MB",
        Category = "Kategorie",
        DatStatus = "DAT_Status",
        DatStatusShort = "DAT",
        Path = "Pfad"
    };
}
