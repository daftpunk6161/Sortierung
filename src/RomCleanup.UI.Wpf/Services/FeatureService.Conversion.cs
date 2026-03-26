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
        UiLookupData.Instance.CompressionRatios.Count > 0 ? UiLookupData.Instance.CompressionRatios
        : new(StringComparer.OrdinalIgnoreCase)
    {
        ["bin_chd"] = 0.50, ["cue_chd"] = 0.50, ["iso_chd"] = 0.60,
        ["iso_rvz"] = 0.40, ["gcz_rvz"] = 0.70, ["zip_7z"] = 0.90,
        ["rar_7z"] = 0.95, ["cso_chd"] = 0.80, ["pbp_chd"] = 0.70,
        ["iso_cso"] = 0.65, ["wbfs_rvz"] = 0.45, ["nkit_rvz"] = 0.50
    };


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
        // TASK-054: Read from externalized ui-lookups.json; fall back to hardcoded map.
        if (UiLookupData.Instance.ExtensionTargetFormats.TryGetValue(ext, out var target))
            return target;

        return ext switch
        {
            "bin" or "cue" or "iso" or "cso" or "pbp" => "chd",
            "gcz" or "wbfs" or "nkit" => "rvz",
            "zip" => "7z",
            "rar" => "7z",
            _ => null
        };
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
        UiLookupData.Instance.ConsoleFormatPriority.Count > 0 ? UiLookupData.Instance.ConsoleFormatPriority
        : new(StringComparer.OrdinalIgnoreCase)
    {
        ["ps1"] = ["chd", "bin/cue", "pbp", "cso", "iso"],
        ["ps2"] = ["chd", "iso"],
        ["psp"] = ["chd", "pbp", "cso", "iso"],
        ["dreamcast"] = ["chd", "gdi", "cdi"],
        ["saturn"] = ["chd", "bin/cue", "iso"],
        ["gc"] = ["rvz", "iso", "nkit", "gcz"],
        ["wii"] = ["rvz", "iso", "nkit", "wbfs"],
        ["nes"] = ["zip", "7z", "nes"],
        ["snes"] = ["zip", "7z", "sfc", "smc"],
        ["gba"] = ["zip", "7z", "gba"],
        ["n64"] = ["zip", "7z", "z64", "v64", "n64"],
        ["gb"] = ["zip", "7z", "gb"],
        ["gbc"] = ["zip", "7z", "gbc"],
        ["nds"] = ["zip", "7z", "nds"],
        ["3ds"] = ["zip", "7z", "3ds"],
        ["genesis"] = ["zip", "7z", "md", "gen"],
        ["arcade"] = ["zip", "7z"]
    };


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
