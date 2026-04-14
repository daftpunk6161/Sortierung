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
    private static readonly ToolRequirement DolphinRequirement = new() { ToolName = "dolphintool" };

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
        var result = _tools.InvokeProcess(toolPath, args, DolphinRequirement, "dolphintool", null, CancellationToken.None);

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
        return RvzFormatHelper.VerifyMagicBytes(targetPath);
    }
}
