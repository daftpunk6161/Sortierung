using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Conversion;

/// <summary>
/// Format conversion orchestrator. Port of Convert.ps1.
/// Maps console types to target formats and drives chdman/dolphintool/7z conversions.
/// </summary>
public sealed class FormatConverterAdapter : IFormatConverter
{
    private readonly IToolRunner _tools;

    /// <summary>
    /// Best target format per console type.
    /// Port of $script:BEST_FORMAT from Convert.ps1.
    /// </summary>
    private static readonly Dictionary<string, ConversionTarget> BestFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        // CD-based → CHD (chdman createcd)
        ["PS1"]   = new(".chd", "chdman", "createcd"),
        ["SAT"]   = new(".chd", "chdman", "createcd"),
        ["DC"]    = new(".chd", "chdman", "createcd"),
        ["SCD"]   = new(".chd", "chdman", "createcd"),
        ["PCECD"] = new(".chd", "chdman", "createcd"),
        ["NEOCD"] = new(".chd", "chdman", "createcd"),
        ["3DO"]   = new(".chd", "chdman", "createcd"),
        ["JAGCD"] = new(".chd", "chdman", "createcd"),
        // DVD-based → CHD (chdman createdvd)
        ["PS2"] = new(".chd", "chdman", "createdvd"),
        // PSP → CHD (UMD)
        ["PSP"] = new(".chd", "chdman", "createcd"),
        // GameCube/Wii → RVZ (DolphinTool)
        ["GC"]  = new(".rvz", "dolphintool", "convert"),
        ["WII"] = new(".rvz", "dolphintool", "convert"),
        // Cartridge → ZIP (7z)
        ["NES"]    = new(".zip", "7z", "zip"),
        ["SNES"]   = new(".zip", "7z", "zip"),
        ["N64"]    = new(".zip", "7z", "zip"),
        ["GB"]     = new(".zip", "7z", "zip"),
        ["GBC"]    = new(".zip", "7z", "zip"),
        ["GBA"]    = new(".zip", "7z", "zip"),
        ["NDS"]    = new(".zip", "7z", "zip"),
        ["MD"]     = new(".zip", "7z", "zip"),
        ["SMS"]    = new(".zip", "7z", "zip"),
        ["GG"]     = new(".zip", "7z", "zip"),
        ["PCE"]    = new(".zip", "7z", "zip"),
        ["NEOGEO"] = new(".zip", "7z", "zip"),
        ["ARCADE"] = new(".zip", "7z", "zip"),
    };

    private static readonly ConversionTarget PbpTarget = new(".chd", "psxtract", "pbp2chd");

    public FormatConverterAdapter(IToolRunner tools)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
    {
        var ext = sourceExtension?.ToLowerInvariant() ?? "";
        if (ext == ".pbp")
            return PbpTarget;

        if (string.IsNullOrWhiteSpace(consoleKey))
            return null;

        return BestFormats.TryGetValue(consoleKey.Trim(), out var target) ? target : null;
    }

    public ConversionResult Convert(string sourcePath, ConversionTarget target, string? sevenZipPath = null)
    {
        if (!File.Exists(sourcePath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "source-not-found");

        var toolPath = _tools.FindTool(target.ToolName);
        if (toolPath is null)
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, $"tool-not-found:{target.ToolName}");

        var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();
        var dir = Path.GetDirectoryName(sourcePath)!;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var targetPath = Path.Combine(dir, baseName + target.Extension);

        // Skip if already in target format
        if (string.Equals(sourceExt, target.Extension, StringComparison.OrdinalIgnoreCase))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "already-target-format");

        // Don't overwrite existing target
        if (File.Exists(targetPath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "target-exists");

        return target.ToolName.ToLowerInvariant() switch
        {
            "chdman" => ConvertWithChdman(sourcePath, targetPath, toolPath, target.Command),
            "dolphintool" => ConvertWithDolphinTool(sourcePath, targetPath, toolPath, sourceExt),
            "7z" => ConvertWithSevenZip(sourcePath, targetPath, toolPath, sevenZipPath),
            _ => new ConversionResult(sourcePath, null, ConversionOutcome.Error, $"unknown-tool:{target.ToolName}")
        };
    }

    public bool Verify(string targetPath, ConversionTarget target)
    {
        if (!File.Exists(targetPath))
            return false;

        var ext = target.Extension.ToLowerInvariant();

        if (ext == ".chd")
        {
            var chdmanPath = _tools.FindTool("chdman");
            if (chdmanPath is null) return false;
            var result = _tools.InvokeProcess(chdmanPath, new[] { "verify", "-i", targetPath }, "chdman verify");
            return result.Success;
        }

        if (ext == ".rvz")
        {
            var dolphinPath = _tools.FindTool("dolphintool");
            if (dolphinPath is null) return false;
            var result = _tools.InvokeProcess(dolphinPath, new[] { "verify", "-i", targetPath }, "dolphintool verify");
            return result.Success;
        }

        if (ext == ".zip")
        {
            var zipPath = _tools.FindTool("7z");
            if (zipPath is null) return false;
            var result = _tools.InvokeProcess(zipPath, new[] { "t", "-y", targetPath }, "7z verify");
            return result.Success;
        }

        return false;
    }

    private ConversionResult ConvertWithChdman(string sourcePath, string targetPath, string toolPath, string command)
    {
        var args = new[] { command, "-i", sourcePath, "-o", targetPath };
        var result = _tools.InvokeProcess(toolPath, args, "chdman");

        if (!result.Success)
        {
            CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                "chdman-failed", result.ExitCode);
        }

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    private ConversionResult ConvertWithDolphinTool(string sourcePath, string targetPath, string toolPath, string sourceExt)
    {
        var allowedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".iso", ".gcm", ".wbfs", ".rvz", ".gcz", ".wia" };

        if (!allowedExts.Contains(sourceExt))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "dolphintool-unsupported-source");

        var args = new[] { "convert", "-i", sourcePath, "-o", targetPath, "-f", "rvz", "-c", "zstd", "-l", "5", "-b", "131072" };
        var result = _tools.InvokeProcess(toolPath, args, "dolphintool");

        if (!result.Success)
        {
            CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                "dolphintool-failed", result.ExitCode);
        }

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    private ConversionResult ConvertWithSevenZip(string sourcePath, string targetPath, string toolPath, string? fallbackZipPath)
    {
        var zipTool = toolPath;
        if (!File.Exists(zipTool) && fallbackZipPath is not null && File.Exists(fallbackZipPath))
            zipTool = fallbackZipPath;

        var args = new[] { "a", "-tzip", "-y", targetPath, sourcePath };
        var result = _tools.InvokeProcess(zipTool, args, "7z");

        if (!result.Success)
        {
            CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                "7z-failed", result.ExitCode);
        }

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    private static void CleanupPartialOutput(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
