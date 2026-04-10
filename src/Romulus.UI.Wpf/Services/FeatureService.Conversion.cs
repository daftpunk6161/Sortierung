using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Tools;
using Romulus.Infrastructure.Reporting;

namespace Romulus.UI.Wpf.Services;

public static partial class FeatureService
{

    // ═══ CONVERSION ESTIMATE ════════════════════════════════════════════
    // Port of ConversionEstimate.ps1

    private static Dictionary<string, double> CompressionRatios =>
        UiLookupData.Instance.CompressionRatios;


    public static ConversionEstimateResult GetConversionEstimate(IReadOnlyList<RomCandidate> candidates)
    {
        long totalSource = 0, totalEstimated = 0;
        var details = new List<ConversionDetail>();

        foreach (var c in candidates)
        {
            var ext = c.Extension.TrimStart('.').ToLowerInvariant();
            var target = GetTargetFormat(ext);
            if (target is null) continue;

            var key = $"{ext}_{target}";
            var ratio = CompressionRatios.GetValueOrDefault(key, 0.75);
            var estimated = (long)(c.SizeBytes * ratio);
            totalSource += c.SizeBytes;
            totalEstimated += estimated;
            details.Add(new ConversionDetail(Path.GetFileName(c.MainPath), ext, target, c.SizeBytes, estimated));
        }

        return new ConversionEstimateResult(totalSource, totalEstimated, totalSource - totalEstimated,
            totalSource > 0 ? (double)totalEstimated / totalSource : 1.0, details);
    }


    public static ConversionAdvisorResult GetConversionAdvisor(IReadOnlyList<RomCandidate> candidates)
    {
        long totalSource = 0;
        long totalEstimated = 0;
        var grouped = new Dictionary<string, List<ConversionDetail>>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var ext = candidate.Extension.TrimStart('.').ToLowerInvariant();
            var target = GetTargetFormat(ext);
            if (target is null)
                continue;

            var ratioKey = $"{ext}_{target}";
            var ratio = CompressionRatios.GetValueOrDefault(ratioKey, 0.75);
            var estimated = (long)(candidate.SizeBytes * ratio);
            var detail = new ConversionDetail(Path.GetFileName(candidate.MainPath), ext, target, candidate.SizeBytes, estimated);

            totalSource += candidate.SizeBytes;
            totalEstimated += estimated;

            var consoleKey = string.IsNullOrWhiteSpace(candidate.ConsoleKey)
                ? "unknown"
                : candidate.ConsoleKey.Trim().ToLowerInvariant();

            if (!grouped.TryGetValue(consoleKey, out var details))
            {
                details = [];
                grouped[consoleKey] = details;
            }

            details.Add(detail);
        }

        var consoles = new List<ConsoleConversionEstimate>(grouped.Count);
        foreach (var (consoleKey, details) in grouped)
        {
            long sourceBytes = 0;
            long estimatedBytes = 0;
            foreach (var detail in details)
            {
                sourceBytes += detail.SourceBytes;
                estimatedBytes += detail.EstimatedBytes;
            }

            var savedBytes = sourceBytes - estimatedBytes;
            var compressionRatio = sourceBytes > 0 ? (double)estimatedBytes / sourceBytes : 1.0;
            consoles.Add(new ConsoleConversionEstimate(
                consoleKey,
                details.Count,
                sourceBytes,
                estimatedBytes,
                savedBytes,
                compressionRatio,
                details));
        }

        consoles.Sort(static (left, right) =>
        {
            var bySavings = right.SavedBytes.CompareTo(left.SavedBytes);
            return bySavings != 0
                ? bySavings
                : string.Compare(left.ConsoleKey, right.ConsoleKey, StringComparison.OrdinalIgnoreCase);
        });

        var recommendations = BuildConversionRecommendations(consoles, totalSource - totalEstimated);
        return new ConversionAdvisorResult(
            totalSource,
            totalEstimated,
            totalSource - totalEstimated,
            totalSource > 0 ? (double)totalEstimated / totalSource : 1.0,
            consoles,
            recommendations);
    }


    private static IReadOnlyList<string> BuildConversionRecommendations(
        IReadOnlyList<ConsoleConversionEstimate> consoles,
        long totalSavedBytes)
    {
        if (consoles.Count == 0)
        {
            return ["Keine konvertierbaren Dateien gefunden."];
        }

        var recommendations = new List<string>();
        var topCount = Math.Min(3, consoles.Count);
        for (var i = 0; i < topCount; i++)
        {
            var entry = consoles[i];
            recommendations.Add(
                $"{entry.ConsoleKey}: ~{FormatSize(entry.SavedBytes)} Ersparnis bei {entry.FileCount} Datei(en) (Ziel: {entry.CompressionRatio:P0} der Originalgroesse)");
        }

        const long oneGb = 1024L * 1024 * 1024;
        if (totalSavedBytes >= oneGb * 20)
        {
            recommendations.Add("Gesamtersparnis ist sehr hoch. Priorisiere diese Konvertierung vor dem naechsten Move-Lauf.");
        }
        else if (totalSavedBytes <= oneGb)
        {
            recommendations.Add("Gesamtersparnis ist gering. Konvertierung ist optional und kann auf spaeter verschoben werden.");
        }

        return recommendations;
    }


    internal static string? GetTargetFormat(string ext)
    {
        return UiLookupData.Instance.ExtensionTargetFormats.TryGetValue(ext, out var target)
            ? target
            : null;
    }


    // ═══ CONVERSION VERIFY ══════════════════════════════════════════════
    // Port of ConversionVerify.ps1

    public static (int passed, int failed, int missing) VerifyConversions(IReadOnlyList<string> targetPaths, long minSize = 1)
    {
        int passed = 0, failed = 0, missing = 0;
        foreach (var path in targetPaths)
        {
            if (!File.Exists(path)) { missing++; continue; }
            var fi = new FileInfo(path);
            if (fi.Length >= minSize) passed++;
            else failed++;
        }
        return (passed, failed, missing);
    }


    // ═══ FORMAT PRIORITY ════════════════════════════════════════════════
    // Port of FormatPriority.ps1

    private static Dictionary<string, string[]> ConsoleFormatPriority =>
        UiLookupData.Instance.ConsoleFormatPriority;


    public static string FormatFormatPriority()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Format-Prioritäten nach Konsole");
        sb.AppendLine(new string('═', 50));
        foreach (var (console, formats) in ConsoleFormatPriority.OrderBy(kv => kv.Key))
        {
            sb.AppendLine($"  {console,-15} → {string.Join(" > ", formats)}");
        }
        return sb.ToString();
    }


}
