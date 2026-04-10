using System.IO.Compression;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Conversion;

/// <summary>
/// Handles chdman-based conversions: direct disc image conversion and archive-to-CHD extraction.
/// Extracted from FormatConverterAdapter for single-responsibility.
/// </summary>
internal sealed class ChdmanToolConverter
{
    private readonly IToolRunner _tools;

    /// <summary>Maximum number of entries allowed in a ZIP archive during conversion extraction.</summary>
    internal const int MaxZipEntryCount = 10_000;

    /// <summary>Maximum total uncompressed size for archive extraction (10 GB, generous for DVD images).</summary>
    internal const long MaxExtractedTotalBytes = 10L * 1024 * 1024 * 1024;

    /// <summary>SEC-CONV-04: Maximum allowed compression ratio per entry (zip bomb protection).</summary>
    internal static readonly double MaxCompressionRatio = 50.0;

    public ChdmanToolConverter(IToolRunner tools)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    public ConversionResult Convert(string sourcePath, string targetPath, string toolPath, string command)
    {
        var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();

        // ZIP/7Z containing .cue/.bin → extract first, then convert the .cue
        if (sourceExt is ".zip" or ".7z")
            return ConvertArchiveToChdman(sourcePath, targetPath, toolPath, command, sourceExt);

        // chdman only accepts .cue, .gdi, .iso, .bin as direct input
        if (sourceExt is not (".cue" or ".gdi" or ".iso" or ".bin" or ".img"))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped,
                $"chdman-unsupported-source:{sourceExt}");

        // PS2 CD/DVD safety heuristic: images under 700MB should be treated as CD images.
        // This avoids createDVD on CD-based PS2 titles which can produce invalid outputs.
        var effectiveCommand = command;
        if (string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase) && sourceExt is ".iso" or ".bin" or ".img")
        {
            try
            {
                var size = new FileInfo(sourcePath).Length;
                if (size > 0 && size < ToolInvokers.ToolInvokerSupport.CdImageThresholdBytes)
                    effectiveCommand = "createcd";
            }
            catch (IOException)
            {
                // Best effort only; keep caller-selected command if size cannot be read.
            }
        }

        var args = new[] { effectiveCommand, "-i", sourcePath, "-o", targetPath };
        var result = _tools.InvokeProcess(toolPath, args, "chdman");

        if (!result.Success)
        {
            CleanupPartialOutput(targetPath);
            var detail = string.IsNullOrWhiteSpace(result.Output) ? "" : $" ({result.Output.Trim().Split('\n')[0]})";
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                $"chdman-failed{detail}", result.ExitCode);
        }

        if (!ConversionOutputValidator.TryValidateCreatedOutput(targetPath, out var outputFailureReason))
        {
            CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, outputFailureReason);
        }

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    public bool Verify(string targetPath)
    {
        var chdmanPath = _tools.FindTool("chdman");
        if (chdmanPath is null) return false;
        var result = _tools.InvokeProcess(chdmanPath, new[] { "verify", "-i", targetPath }, "chdman verify");
        return result.Success;
    }

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

                // SEC-CONV-07: Post-extraction validation for 7z (parity with zip bomb protection)
                if (!ValidateExtractedContents(extractDir))
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "archive-path-traversal-detected");

                var extractedFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
                if (extractedFiles.Length > MaxZipEntryCount)
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "archive-too-many-entries");

                long totalExtractedSize = 0;
                foreach (var f in extractedFiles)
                {
                    totalExtractedSize += new FileInfo(f).Length;
                    if (totalExtractedSize > MaxExtractedTotalBytes)
                        return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "archive-too-large");
                }
            }

            // Step 2: Find the .cue file (preferred) or .gdi, or fall back to .iso/.bin
            var cueFiles = Directory.GetFiles(extractDir, "*.cue", SearchOption.AllDirectories);
            var gdiFiles = Directory.GetFiles(extractDir, "*.gdi", SearchOption.AllDirectories);
            var isoFiles = Directory.GetFiles(extractDir, "*.iso", SearchOption.AllDirectories);

            // TASK-012/TASK-149: Deterministic CUE selection — sort alphabetically before selecting.
            Array.Sort(cueFiles, StringComparer.OrdinalIgnoreCase);
            Array.Sort(gdiFiles, StringComparer.OrdinalIgnoreCase);
            Array.Sort(isoFiles, StringComparer.OrdinalIgnoreCase);

            // Path traversal guard: Ensure selected files are within extractDir
            static bool IsWithinDir(string filePath, string baseDir)
            {
                var fullBase = Path.GetFullPath(baseDir) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(filePath).StartsWith(fullBase, StringComparison.OrdinalIgnoreCase);
            }

            // Filter to safe files within extractDir
            var safeCueFiles = cueFiles.Where(f => IsWithinDir(f, extractDir)).ToArray();
            var safeGdiFiles = gdiFiles.Where(f => IsWithinDir(f, extractDir)).ToArray();
            var safeIsoFiles = isoFiles.Where(f => IsWithinDir(f, extractDir)).ToArray();

            // TASK-012: Multi-CUE atomicity — if multiple .cue files exist, each needs conversion.
            if (safeCueFiles.Length > 1)
            {
                return ConvertMultiCueArchive(sourcePath, safeCueFiles, dir, toolPath, command);
            }

            string? inputFile = null;
            if (safeCueFiles.Length == 1)
                inputFile = safeCueFiles[0];
            else if (safeGdiFiles.Length > 0)
                inputFile = safeGdiFiles[0];
            else if (safeIsoFiles.Length > 0)
                inputFile = safeIsoFiles[0];

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

            if (!ConversionOutputValidator.TryValidateCreatedOutput(targetPath, out var outputFailureReason))
            {
                CleanupPartialOutput(targetPath);
                return new ConversionResult(sourcePath, null, ConversionOutcome.Error, outputFailureReason);
            }

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
            catch (IOException) { /* best-effort cleanup — dir may be locked */ }
        }
    }

    /// <summary>
    /// TASK-012: Convert all CUE files from a multi-disc archive atomically.
    /// All must succeed or the entire conversion is rolled back.
    /// </summary>
    private ConversionResult ConvertMultiCueArchive(
        string sourcePath, string[] cueFiles, string outputDir, string toolPath, string command)
    {
        var outputs = new List<string>();

        for (int i = 0; i < cueFiles.Length; i++)
        {
            var cueBaseName = Path.GetFileNameWithoutExtension(cueFiles[i]);
            var targetPath = Path.Combine(outputDir, cueBaseName + ".chd");

            var args = new[] { command, "-i", cueFiles[i], "-o", targetPath };
            var result = _tools.InvokeProcess(toolPath, args, "chdman");

            if (!result.Success)
            {
                // Rollback: delete all already-created CHDs
                foreach (var output in outputs)
                    CleanupPartialOutput(output);
                CleanupPartialOutput(targetPath);

                return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                    $"multi-cue-failed:disc{i + 1}of{cueFiles.Length}");
            }

            if (!ConversionOutputValidator.TryValidateCreatedOutput(targetPath, out var outputFailureReason))
            {
                foreach (var output in outputs)
                    CleanupPartialOutput(output);
                CleanupPartialOutput(targetPath);

                return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                    $"{outputFailureReason}:disc{i + 1}of{cueFiles.Length}");
            }

            outputs.Add(targetPath);
        }

        return new ConversionResult(sourcePath, outputs[0], ConversionOutcome.Success,
            $"multi-disc:{cueFiles.Length}")
        {
            AdditionalTargetPaths = outputs.Skip(1).ToArray()
        };
    }

    /// <summary>
    /// SEC-CONV-01: Safe per-entry ZIP extraction with Zip-Slip protection.
    /// </summary>
    internal static string? ExtractZipSafe(string zipPath, string extractDir)
    {
        Directory.CreateDirectory(extractDir);
        var normalizedBase = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);

        if (archive.Entries.Count > MaxZipEntryCount)
            return $"archive-too-many-entries:{archive.Entries.Count}";

        long totalUncompressed = 0;
        foreach (var entry in archive.Entries)
        {
            totalUncompressed += entry.Length;
            if (totalUncompressed > MaxExtractedTotalBytes)
                return "archive-extraction-size-exceeded";

            if (entry.CompressedLength > 0 && entry.Length > 1_048_576 &&
                entry.Length / (double)entry.CompressedLength > MaxCompressionRatio)
                return "archive-compression-ratio-exceeded";
        }

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destPath = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));

            if (!destPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return "archive-zip-slip-detected";

            var entryDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(entryDir))
                Directory.CreateDirectory(entryDir);

            entry.ExtractToFile(destPath, overwrite: false);
        }

        return null;
    }

    /// <summary>
    /// Post-extraction validation for reparse points and path traversal.
    /// </summary>
    internal static bool ValidateExtractedContents(string extractDir)
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

    internal static void CleanupPartialOutput(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort cleanup
        }
    }
}
