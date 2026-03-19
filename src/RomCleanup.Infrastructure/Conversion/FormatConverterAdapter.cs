using System.IO.Compression;
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
    private readonly IReadOnlyDictionary<string, ConversionTarget> _bestFormats;

    /// <summary>
    /// Default best target format per console type.
    /// Port of $script:BEST_FORMAT from Convert.ps1.
    /// Injected via constructor to allow configuration override (ADR-0007 §3.3).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ConversionTarget> DefaultBestFormats =
        new Dictionary<string, ConversionTarget>(StringComparer.OrdinalIgnoreCase)
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

    /// <summary>
    /// Creates a FormatConverterAdapter with default format mappings.
    /// </summary>
    public FormatConverterAdapter(IToolRunner tools)
        : this(tools, null) { }

    /// <summary>
    /// Creates a FormatConverterAdapter with optional custom format mappings.
    /// Falls back to <see cref="DefaultBestFormats"/> when <paramref name="bestFormats"/> is null.
    /// </summary>
    public FormatConverterAdapter(IToolRunner tools, IReadOnlyDictionary<string, ConversionTarget>? bestFormats)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _bestFormats = bestFormats ?? DefaultBestFormats;
    }

    public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
    {
        var ext = sourceExtension?.ToLowerInvariant() ?? "";
        if (ext == ".pbp")
            return PbpTarget;

        if (string.IsNullOrWhiteSpace(consoleKey))
            return null;

        return _bestFormats.TryGetValue(consoleKey.Trim(), out var target) ? target : null;
    }

    public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "source-not-found");

        cancellationToken.ThrowIfCancellationRequested();

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
            "7z" => ConvertWithSevenZip(sourcePath, targetPath, toolPath),
            "psxtract" => ConvertWithPsxtract(sourcePath, targetPath, toolPath, target.Command),
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
            // DolphinTool does not have a verify command.
            // Verify by checking file existence, non-zero size, and RVZ magic bytes.
            var info = new FileInfo(targetPath);
            if (!info.Exists || info.Length < 4) return false;
            try
            {
                using var fs = File.OpenRead(targetPath);
                Span<byte> magic = stackalloc byte[4];
                if (fs.ReadAtLeast(magic, 4, throwOnEndOfStream: false) < 4) return false;
                // RVZ magic: "RVZ\x01"
                return magic[0] == 'R' && magic[1] == 'V' && magic[2] == 'Z' && magic[3] == 0x01;
            }
            catch { return false; }
        }

        if (ext == ".zip")
        {
            var zipPath = _tools.FindTool("7z");
            if (zipPath is null) return false;
            var result = _tools.InvokeProcess(zipPath, new[] { "t", "-y", targetPath }, "7z verify");
            return result.Success;
        }

        // PBP→CHD conversion produces a .chd output; verify via chdman.
        // This path handles the case where Verify is called with the PbpTarget
        // but the actual output file is .chd (already handled above via ext==".chd").
        // If the target extension is something else for PBP, fall through.

        return false;
    }

    private ConversionResult ConvertWithChdman(string sourcePath, string targetPath, string toolPath, string command)
    {
        var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();

        // ZIP/7Z containing .cue/.bin → extract first, then convert the .cue
        if (sourceExt is ".zip" or ".7z")
            return ConvertArchiveToChdman(sourcePath, targetPath, toolPath, command, sourceExt);

        // chdman only accepts .cue, .gdi, .iso, .bin as direct input
        if (sourceExt is not (".cue" or ".gdi" or ".iso" or ".bin" or ".img"))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped,
                $"chdman-unsupported-source:{sourceExt}");

        var args = new[] { command, "-i", sourcePath, "-o", targetPath };
        var result = _tools.InvokeProcess(toolPath, args, "chdman");

        if (!result.Success)
        {
            CleanupPartialOutput(targetPath);
            var detail = string.IsNullOrWhiteSpace(result.Output) ? "" : $" ({result.Output.Trim().Split('\n')[0]})";
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                $"chdman-failed{detail}", result.ExitCode);
        }

        if (!File.Exists(targetPath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "output-not-created");

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    /// <summary>Maximum number of entries allowed in a ZIP archive during conversion extraction.</summary>
    private const int MaxZipEntryCount = 10_000;

    /// <summary>Maximum total uncompressed size for archive extraction (10 GB, generous for DVD images).</summary>
    private const long MaxExtractedTotalBytes = 10L * 1024 * 1024 * 1024;

    /// <summary>SEC-CONV-04: Maximum allowed compression ratio per entry (zip bomb protection).</summary>
    internal static readonly double MaxCompressionRatio = 50.0;

    /// <summary>
    /// Extract a ZIP/7Z archive, find the .cue file inside, convert to CHD, then clean up.
    /// Handles the common case of disc-based ROMs distributed as ZIP containing .bin/.cue.
    /// SEC-CONV-01: Per-entry Zip-Slip-safe extraction (no ZipFile.ExtractToDirectory).
    /// SEC-CONV-02/03: Entry count + total size limits to block zip bombs.
    /// </summary>
    private ConversionResult ConvertArchiveToChdman(
        string sourcePath, string targetPath, string toolPath, string command, string sourceExt)
    {
        var dir = Path.GetDirectoryName(sourcePath)!;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var extractDir = Path.Combine(dir, $"_extract_{baseName}_{Guid.NewGuid():N}");

        try
        {
            // Step 1: Extract archive with Zip-Slip protection
            if (sourceExt == ".zip")
            {
                var extractError = ExtractZipSafe(sourcePath, extractDir);
                if (extractError is not null)
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Error, extractError);
            }
            else
            {
                // .7z — use 7z tool to extract
                var sevenZipPath = _tools.FindTool("7z");
                if (sevenZipPath is null)
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "tool-not-found:7z");

                Directory.CreateDirectory(extractDir);
                var extractResult = _tools.InvokeProcess(sevenZipPath,
                    new[] { "x", "-y", $"-o{extractDir}", sourcePath }, "7z extract");
                if (!extractResult.Success)
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "7z-extract-failed");

                // Post-extraction: validate all files are within extractDir
                if (!ValidateExtractedContents(extractDir))
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "archive-path-traversal-detected");
            }

            // Step 2: Find the .cue file (preferred) or .gdi, or fall back to .iso/.bin
            var cueFiles = Directory.GetFiles(extractDir, "*.cue", SearchOption.AllDirectories);
            var gdiFiles = Directory.GetFiles(extractDir, "*.gdi", SearchOption.AllDirectories);
            var isoFiles = Directory.GetFiles(extractDir, "*.iso", SearchOption.AllDirectories);

            // Path traversal guard: Ensure selected files are within extractDir
            static bool IsWithinDir(string filePath, string baseDir)
            {
                var fullBase = Path.GetFullPath(baseDir) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(filePath).StartsWith(fullBase, StringComparison.OrdinalIgnoreCase);
            }

            string? inputFile = null;
            if (cueFiles.Length > 0 && IsWithinDir(cueFiles[0], extractDir))
                inputFile = cueFiles[0];
            else if (gdiFiles.Length > 0 && IsWithinDir(gdiFiles[0], extractDir))
                inputFile = gdiFiles[0];
            else if (isoFiles.Length > 0 && IsWithinDir(isoFiles[0], extractDir))
                inputFile = isoFiles[0];

            if (inputFile is null)
                return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped,
                    "archive-no-disc-image");

            // Step 3: Convert via chdman
            var args = new[] { command, "-i", inputFile, "-o", targetPath };
            var result = _tools.InvokeProcess(toolPath, args, "chdman");

            if (!result.Success)
            {
                CleanupPartialOutput(targetPath);
                var detail = string.IsNullOrWhiteSpace(result.Output) ? "" : $" ({result.Output.Trim().Split('\n')[0]})";
                return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                    $"chdman-failed{detail}", result.ExitCode);
            }

            if (!File.Exists(targetPath))
                return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "output-not-created");

            return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
        }
        catch (InvalidDataException)
        {
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "archive-corrupt");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                $"extract-failed:{ex.Message}");
        }
        finally
        {
            // Clean up extracted files
            try
            {
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, recursive: true);
            }
            catch { /* best-effort cleanup */ }
        }
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

        if (!File.Exists(targetPath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "output-not-created");

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    private ConversionResult ConvertWithSevenZip(string sourcePath, string targetPath, string toolPath)
    {
        var zipTool = toolPath;

        var args = new[] { "a", "-tzip", "-y", targetPath, sourcePath };
        var result = _tools.InvokeProcess(zipTool, args, "7z");

        if (!result.Success)
        {
            CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                "7z-failed", result.ExitCode);
        }

        if (!File.Exists(targetPath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "output-not-created");

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    private ConversionResult ConvertWithPsxtract(string sourcePath, string targetPath, string toolPath, string command)
    {
        var args = new[] { command, "-i", sourcePath, "-o", targetPath };
        var result = _tools.InvokeProcess(toolPath, args, "psxtract");

        if (!result.Success)
        {
            CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                "psxtract-failed", result.ExitCode);
        }

        if (!File.Exists(targetPath))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "output-not-created");

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    /// <summary>
    /// SEC-CONV-01: Safe per-entry ZIP extraction with Zip-Slip protection.
    /// Validates each entry path before extraction, enforces entry count and total size limits.
    /// </summary>
    private static string? ExtractZipSafe(string zipPath, string extractDir)
    {
        Directory.CreateDirectory(extractDir);
        var normalizedBase = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);

        // SEC-CONV-02: Entry count limit (zip bomb protection)
        if (archive.Entries.Count > MaxZipEntryCount)
            return $"archive-too-many-entries:{archive.Entries.Count}";

        // SEC-CONV-03: Total uncompressed size limit
        long totalUncompressed = 0;
        foreach (var entry in archive.Entries)
        {
            totalUncompressed += entry.Length;
            if (totalUncompressed > MaxExtractedTotalBytes)
                return "archive-extraction-size-exceeded";

            // SEC-CONV-04: Per-entry compression ratio check (zip bomb detection)
            // Only check entries >1 MB uncompressed to avoid false positives on small legitimate files
            if (entry.CompressedLength > 0 && entry.Length > 1_048_576 &&
                entry.Length / (double)entry.CompressedLength > MaxCompressionRatio)
                return "archive-compression-ratio-exceeded";
        }

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue; // Skip directory entries

            var destPath = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));

            // Zip-Slip protection: reject entries that escape extractDir
            if (!destPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return "archive-zip-slip-detected";

            // Ensure parent directory exists
            var entryDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(entryDir))
                Directory.CreateDirectory(entryDir);

            entry.ExtractToFile(destPath, overwrite: false);
        }

        return null; // success
    }

    /// <summary>
    /// Post-extraction validation: ensure all files and directories are within the expected root
    /// and no reparse points were created during extraction.
    /// </summary>
    private static bool ValidateExtractedContents(string extractDir)
    {
        var normalizedBase = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;

        foreach (var dir in Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories))
        {
            if (!Path.GetFullPath(dir).StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return false;
            var dirInfo = new DirectoryInfo(dir);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                return false;
        }

        foreach (var file in Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories))
        {
            if (!Path.GetFullPath(file).StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return false;
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
                return false;
        }

        return true;
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
