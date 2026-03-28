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
