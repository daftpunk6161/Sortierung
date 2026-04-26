using System.IO.Compression;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.FileSystem;

namespace Romulus.Infrastructure.Conversion;

/// <summary>
/// Handles chdman-based conversions: direct disc image conversion and archive-to-CHD extraction.
/// Extracted from FormatConverterAdapter for single-responsibility.
/// </summary>
internal sealed class ChdmanToolConverter
{
    private readonly IToolRunner _tools;
    private static readonly ToolRequirement ChdmanRequirement = new() { ToolName = "chdman" };
    private static readonly ToolRequirement SevenZipRequirement = new() { ToolName = "7z" };

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

    public ConversionResult Convert(
        string sourcePath,
        string targetPath,
        string toolPath,
        string command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();

        // ZIP/7Z containing .cue/.bin → extract first, then convert the .cue
        if (DiscFormats.IsChdmanArchiveExtension(sourceExt))
            return ConvertArchiveToChdman(sourcePath, targetPath, toolPath, command, sourceExt, cancellationToken);

        // chdman only accepts .cue, .gdi, .iso, .bin as direct input
        if (!DiscFormats.ChdmanDirectInputExtensions.Contains(sourceExt))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped,
                $"chdman-unsupported-source:{sourceExt}");

        var effectiveCommand = ToolInvokers.ToolInvokerSupport.ResolveEffectiveChdmanCommand(command, sourcePath);

        var args = new[] { effectiveCommand, "-i", sourcePath, "-o", targetPath };
        var result = _tools.InvokeProcess(toolPath, args, ChdmanRequirement, "chdman", TimeSpan.FromMinutes(30), cancellationToken);

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
        var result = _tools.InvokeProcess(chdmanPath, ["verify", "-i", targetPath], ChdmanRequirement, "chdman verify", null, CancellationToken.None);
        return result.Success;
    }

    /// <summary>
    /// Extract a ZIP/7Z archive, find the .cue file inside, convert to CHD, then clean up.
    /// Handles the common case of disc-based ROMs distributed as ZIP containing .bin/.cue.
    /// SEC-CONV-01: Per-entry Zip-Slip-safe extraction (no ZipFile.ExtractToDirectory).
    /// SEC-CONV-02/03: Entry count + total size limits to block zip bombs.
    /// </summary>
    private ConversionResult ConvertArchiveToChdman(
        string sourcePath,
        string targetPath,
        string toolPath,
        string command,
        string sourceExt,
        CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(sourcePath)!;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        // R5-017: Use system temp directory instead of source directory for extraction
        var extractDir = Path.Combine(Path.GetTempPath(), $"_extract_{baseName}_{Guid.NewGuid():N}");

        try
        {
            // Step 1: Extract archive with Zip-Slip protection
            if (sourceExt == ".zip")
            {
                var extractError = ExtractZipSafe(sourcePath, extractDir, cancellationToken);
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
                    new[] { "x", "-y", "-snl-", $"-o{extractDir}", sourcePath },
                    SevenZipRequirement,
                    "7z extract",
                    TimeSpan.FromMinutes(10),
                    cancellationToken);
                if (!extractResult.Success)
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "7z-extract-failed");

                // SEC-CONV-07: Post-extraction validation for 7z (parity with zip bomb protection)
                if (!ValidateExtractedContents(extractDir))
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "archive-path-traversal-detected");

                var extractedFiles = new FileSystemAdapter().GetFilesSafe(extractDir);
                if (extractedFiles.Count > MaxZipEntryCount)
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
            var extractedDiscFiles = new FileSystemAdapter().GetFilesSafe(extractDir, [".cue", ".gdi", ".iso", ".bin"]);
            var cueFiles = extractedDiscFiles.Where(static file => file.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)).ToArray();
            var gdiFiles = extractedDiscFiles.Where(static file => file.EndsWith(".gdi", StringComparison.OrdinalIgnoreCase)).ToArray();
            var isoFiles = extractedDiscFiles.Where(static file => file.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)).ToArray();
            var binFiles = extractedDiscFiles.Where(static file => file.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)).ToArray();

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
            var safeBinFiles = binFiles.Where(f => IsWithinDir(f, extractDir)).ToArray();

            // TASK-012: Multi-CUE atomicity — if multiple .cue files exist, each needs conversion.
            if (safeCueFiles.Length > 1)
            {
                return ConvertMultiCueArchive(sourcePath, safeCueFiles, dir, toolPath, command, cancellationToken);
            }

            string? inputFile = null;
            if (safeCueFiles.Length == 1)
                inputFile = safeCueFiles[0];
            else if (safeGdiFiles.Length > 0)
                inputFile = safeGdiFiles[0];
            else if (safeIsoFiles.Length > 0)
                inputFile = safeIsoFiles[0];
            else if (safeBinFiles.Length > 0)
                inputFile = safeBinFiles[0];

            if (inputFile is null)
                return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped,
                    "archive-no-disc-image");

            // Step 3: Convert via chdman
            var effectiveCommand = ToolInvokers.ToolInvokerSupport.ResolveEffectiveChdmanCommand(command, inputFile);
            var args = new[] { effectiveCommand, "-i", inputFile, "-o", targetPath };
            var result = _tools.InvokeProcess(toolPath, args, ChdmanRequirement, "chdman", TimeSpan.FromMinutes(30), cancellationToken);

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
        string sourcePath,
        string[] cueFiles,
        string outputDir,
        string toolPath,
        string command,
        CancellationToken cancellationToken)
    {
        var outputs = new List<string>();

        for (int i = 0; i < cueFiles.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cueBaseName = Path.GetFileNameWithoutExtension(cueFiles[i]);
            var targetPath = Path.Combine(outputDir, cueBaseName + ".chd");

            var effectiveCommand = ToolInvokers.ToolInvokerSupport.ResolveEffectiveChdmanCommand(command, cueFiles[i]);
            var args = new[] { effectiveCommand, "-i", cueFiles[i], "-o", targetPath };
            var result = _tools.InvokeProcess(toolPath, args, ChdmanRequirement, "chdman", TimeSpan.FromMinutes(30), cancellationToken);

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
    internal static string? ExtractZipSafe(string zipPath, string extractDir, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(extractDir);
        var normalizedBase = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);

        if (archive.Entries.Count > MaxZipEntryCount)
            return $"archive-too-many-entries:{archive.Entries.Count}";

        foreach (var entry in archive.Entries)
        {
            if (entry.CompressedLength > 0 && entry.Length > 1_048_576 &&
                entry.Length / (double)entry.CompressedLength > MaxCompressionRatio)
                return "archive-compression-ratio-exceeded";
        }

        long totalExtractedBytes = 0;
        var buffer = new byte[81920];
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destPath = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));

            if (!destPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return "archive-zip-slip-detected";

            var entryDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(entryDir))
                Directory.CreateDirectory(entryDir);

            using var source = entry.Open();
            using var target = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = source.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;

                totalExtractedBytes += read;
                if (totalExtractedBytes > MaxExtractedTotalBytes)
                    return "archive-extraction-size-exceeded";

                target.Write(buffer, 0, read);
            }
        }

        return ValidateExtractedContents(extractDir)
            ? null
            : "archive-path-traversal-detected";
    }

    /// <summary>
    /// Post-extraction validation for reparse points and path traversal.
    /// </summary>
    internal static bool ValidateExtractedContents(string extractDir)
    {
        var normalizedBase = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;

        foreach (var dir in EnumerateDirectoriesWithoutFollowingReparsePoints(extractDir))
        {
            if (!Path.GetFullPath(dir).StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return false;
            var dirInfo = new DirectoryInfo(dir);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                return false;
        }

        foreach (var file in new FileSystemAdapter().GetFilesSafe(extractDir))
        {
            if (!Path.GetFullPath(file).StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return false;
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
                return false;
        }

        return true;
    }

    private static IEnumerable<string> EnumerateDirectoriesWithoutFollowingReparsePoints(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            string[] children;
            try
            {
                children = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                yield break;
            }

            Array.Sort(children, StringComparer.OrdinalIgnoreCase);
            foreach (var child in children)
            {
                yield return child;
                FileAttributes attrs;
                try
                {
                    attrs = File.GetAttributes(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if ((attrs & FileAttributes.ReparsePoint) == 0)
                    stack.Push(child);
            }
        }
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
