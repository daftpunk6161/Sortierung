using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Conversion;

/// <summary>
/// Handles DolphinTool-based conversions: GameCube/Wii ISO/GCM/WBFS to RVZ.
/// Extracted from FormatConverterAdapter for single-responsibility.
/// </summary>
internal sealed class DolphinToolConverter
{
    private readonly IToolRunner _tools;

    public DolphinToolConverter(IToolRunner tools)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    public ConversionResult Convert(string sourcePath, string targetPath, string toolPath, string sourceExt)
    {
        var allowedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".iso", ".gcm", ".wbfs", ".rvz", ".gcz", ".wia" };

        if (!allowedExts.Contains(sourceExt))
            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "dolphintool-unsupported-source");

        var args = new[] { "convert", "-i", sourcePath, "-o", targetPath, "-f", "rvz", "-c", "zstd", "-l", "5", "-b", "131072" };
        var result = _tools.InvokeProcess(toolPath, args, "dolphintool");

        if (!result.Success)
        {
            ChdmanToolConverter.CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                "dolphintool-failed", result.ExitCode);
        }

        if (!ConversionOutputValidator.TryValidateCreatedOutput(targetPath, out var outputFailureReason))
        {
            ChdmanToolConverter.CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, outputFailureReason);
        }

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    public static bool Verify(string targetPath)
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
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
