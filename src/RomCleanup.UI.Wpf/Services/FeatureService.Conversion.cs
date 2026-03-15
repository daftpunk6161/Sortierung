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


    internal static string? GetTargetFormat(string ext) => ext switch
    {
        "bin" or "cue" or "iso" or "cso" or "pbp" => "chd",
        "gcz" or "wbfs" or "nkit" => "rvz",
        "zip" => "7z",
        "rar" => "7z",
        _ => null
    };


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


    // ═══ EMULATOR COMPAT ════════════════════════════════════════════════
    // Port of EmulatorCompatReport.ps1

    private static Dictionary<string, Dictionary<string, string>> EmulatorMatrix =>
        UiLookupData.Instance.EmulatorMatrix.Count > 0 ? UiLookupData.Instance.EmulatorMatrix
        : new()
    {
        ["nes"] = new() { ["Mesen"] = "Perfect", ["FCEUX"] = "Great", ["Nestopia"] = "Great" },
        ["snes"] = new() { ["bsnes"] = "Perfect", ["Snes9x"] = "Great", ["ZSNES"] = "Good" },
        ["n64"] = new() { ["Mupen64Plus"] = "Great", ["Project64"] = "Great", ["Ares"] = "Good" },
        ["gba"] = new() { ["mGBA"] = "Perfect", ["VBA-M"] = "Great" },
        ["gb"] = new() { ["SameBoy"] = "Perfect", ["Gambatte"] = "Perfect", ["mGBA"] = "Great" },
        ["ps1"] = new() { ["DuckStation"] = "Perfect", ["Mednafen"] = "Perfect", ["PCSX-R"] = "Great" },
        ["ps2"] = new() { ["PCSX2"] = "Great", ["AetherSX2"] = "Good" },
        ["psp"] = new() { ["PPSSPP"] = "Great" },
        ["gc"] = new() { ["Dolphin"] = "Great" },
        ["wii"] = new() { ["Dolphin"] = "Great" },
        ["dreamcast"] = new() { ["Flycast"] = "Great", ["Redream"] = "Great" },
        ["saturn"] = new() { ["Mednafen"] = "Great", ["Kronos"] = "Good" },
        ["genesis"] = new() { ["BlastEm"] = "Perfect", ["Genesis Plus GX"] = "Perfect" },
        ["arcade"] = new() { ["MAME"] = "Great", ["FBNeo"] = "Great" }
    };


    public static string FormatEmulatorCompat()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Emulator-Kompatibilitätsmatrix");
        sb.AppendLine(new string('═', 60));
        foreach (var (console, emus) in EmulatorMatrix.OrderBy(kv => kv.Key))
        {
            sb.AppendLine($"\n  {console.ToUpperInvariant()}:");
            foreach (var (emu, compat) in emus)
                sb.AppendLine($"    {emu,-20} {compat}");
        }
        return sb.ToString();
    }


    // ═══ CONVERT QUEUE REPORT ═══════════════════════════════════════════

    /// <summary>
    /// Build a conversion queue report from conversion estimates.
    /// </summary>
    public static string BuildConvertQueueReport(ConversionEstimateResult est)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Konvert-Warteschlange");
        sb.AppendLine(new string('═', 60));
        sb.AppendLine($"\n  Dateien: {est.Details.Count}");
        sb.AppendLine($"  Quellgröße: {FormatSize(est.TotalSourceBytes)}");
        sb.AppendLine($"  Geschätzte Zielgröße: {FormatSize(est.EstimatedTargetBytes)}");
        sb.AppendLine($"  Ersparnis: {FormatSize(est.SavedBytes)}\n");

        if (est.Details.Count > 0)
        {
            sb.AppendLine($"  {"Datei",-40} {"Quelle",-8} {"Ziel",-8} {"Größe",12}");
            sb.AppendLine($"  {new string('-', 40)} {new string('-', 8)} {new string('-', 8)} {new string('-', 12)}");
            foreach (var d in est.Details)
                sb.AppendLine($"  {d.FileName,-40} {d.SourceFormat,-8} {d.TargetFormat,-8} {FormatSize(d.SourceBytes),12}");
        }
        else
        {
            sb.AppendLine("  Keine konvertierbaren Dateien gefunden.");
        }

        return sb.ToString();
    }


    // ═══ BATCH-3 EXTRACTIONS ════════════════════════════════════════════

    /// <summary>Build NKit conversion info report, including tool detection.</summary>
    public static string BuildNKitConvertReport(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var isNkit = filePath.Contains(".nkit", StringComparison.OrdinalIgnoreCase);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"NKit-Konvertierung\n");
        sb.AppendLine($"  Image:       {fileName}");
        sb.AppendLine($"  NKit-Format: {(isNkit ? "Ja" : "Nein")}");

        try
        {
            var runner = new ToolRunnerAdapter(null);
            var nkitPath = runner.FindTool("nkit");
            if (nkitPath is not null)
            {
                sb.AppendLine($"  NKit-Tool:   {nkitPath}");
                sb.AppendLine("\nKonvertierungs-Anleitung:");
                sb.AppendLine("  NKit → ISO: NKit.exe recover <Datei>");
                sb.AppendLine("  NKit → RVZ: Erst recover, dann dolphintool convert");
                sb.AppendLine("\nEmpfohlenes Zielformat: RVZ (GameCube/Wii)");
            }
            else
            {
                sb.AppendLine("\n  NKit-Tool nicht gefunden.");
                sb.AppendLine("\n  Nach dem Download das Tool in den PATH aufnehmen");
                sb.AppendLine("  oder im Programmverzeichnis ablegen.");
            }
        }
        catch
        {
            sb.AppendLine("\n  NKit-Tool-Suche fehlgeschlagen.");
            sb.AppendLine("  Konvertierung nach ISO/RVZ erfordert das Tool 'NKit'.");
        }
        return sb.ToString();
    }


    /// <summary>Build GPU hashing status report.</summary>
    public static (string Report, bool IsEnabled) BuildGpuHashingStatus()
    {
        var openCl = File.Exists(Path.Combine(Environment.SystemDirectory, "OpenCL.dll"));
        var currentSetting = Environment.GetEnvironmentVariable("ROMCLEANUP_GPU_HASHING") ?? "off";
        var isEnabled = currentSetting.Equals("on", StringComparison.OrdinalIgnoreCase);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("GPU-Hashing Konfiguration\n");
        sb.AppendLine($"  OpenCL verfügbar: {(openCl ? "Ja" : "Nein")}");
        sb.AppendLine($"  CPU-Kerne:        {Environment.ProcessorCount}");
        sb.AppendLine($"  Aktueller Status: {(isEnabled ? "AKTIVIERT" : "Deaktiviert")}");

        if (!openCl)
        {
            sb.AppendLine("\n  GPU-Hashing benötigt OpenCL-Treiber.");
            sb.AppendLine("  Installiere aktuelle GPU-Treiber für Unterstützung.");
        }
        else
        {
            sb.AppendLine("\n  GPU-Hashing kann SHA1/SHA256-Berechnungen");
            sb.AppendLine("  um 5-20x beschleunigen (experimentell).");
        }
        return (sb.ToString(), isEnabled);
    }


    /// <summary>Toggle GPU hashing and return the new state.</summary>
    public static bool ToggleGpuHashing()
    {
        var current = Environment.GetEnvironmentVariable("ROMCLEANUP_GPU_HASHING") ?? "off";
        var isEnabled = current.Equals("on", StringComparison.OrdinalIgnoreCase);
        Environment.SetEnvironmentVariable("ROMCLEANUP_GPU_HASHING", isEnabled ? "off" : "on");
        return !isEnabled;
    }


    /// <summary>Build formatted conversion estimate report.</summary>
    public static string BuildConversionEstimateReport(IReadOnlyList<RomCandidate> candidates)
    {
        var est = GetConversionEstimate(candidates);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Konvertierungs-Schätzung");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"  Quellgröße:     {FormatSize(est.TotalSourceBytes)}");
        sb.AppendLine($"  Geschätzt:      {FormatSize(est.EstimatedTargetBytes)}");
        sb.AppendLine($"  Ersparnis:      {FormatSize(est.SavedBytes)} ({(1 - est.CompressionRatio) * 100:F1}%)");
        sb.AppendLine($"\nDetails ({est.Details.Count} konvertierbare Dateien):");
        foreach (var d in est.Details.Take(20))
            sb.AppendLine($"  {d.FileName}: {d.SourceFormat}→{d.TargetFormat} ({FormatSize(d.SourceBytes)}→{FormatSize(d.EstimatedBytes)})");
        if (est.Details.Count > 20)
            sb.AppendLine($"  … und {est.Details.Count - 20} weitere");
        return sb.ToString();
    }


    /// <summary>Build parallel hashing configuration report.</summary>
    public static string BuildParallelHashingReport(int cores, int newThreads)
    {
        return $"Parallel-Hashing Konfiguration\n\n" +
            $"CPU-Kerne: {cores}\nThreads (neu): {newThreads}\n\n" +
            "Die Änderung wird beim nächsten Hash-Vorgang wirksam.";
    }

}
