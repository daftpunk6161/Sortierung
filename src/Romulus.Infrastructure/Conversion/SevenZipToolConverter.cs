using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Conversion;

/// <summary>
/// Handles 7z-based conversions: cartridge ROM compression to ZIP.
/// Extracted from FormatConverterAdapter for single-responsibility.
/// </summary>
internal sealed class SevenZipToolConverter
{
    private readonly IToolRunner _tools;
    private static readonly ToolRequirement SevenZipRequirement = new() { ToolName = "7z" };

    public SevenZipToolConverter(IToolRunner tools)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    public ConversionResult Convert(string sourcePath, string targetPath, string toolPath)
    {
        var args = new[] { "a", "-tzip", "-y", targetPath, sourcePath };
        var result = _tools.InvokeProcess(toolPath, args, SevenZipRequirement, "7z", null, CancellationToken.None);

        if (!result.Success)
        {
            ChdmanToolConverter.CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                "7z-failed", result.ExitCode);
        }

        if (!ConversionOutputValidator.TryValidateCreatedOutput(targetPath, out var outputFailureReason))
        {
            ChdmanToolConverter.CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, outputFailureReason);
        }

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }

    public bool Verify(string targetPath)
    {
        var zipPath = _tools.FindTool("7z");
        if (zipPath is null) return false;
        var result = _tools.InvokeProcess(zipPath, new[] { "t", "-y", targetPath }, SevenZipRequirement, "7z verify", null, CancellationToken.None);
        return result.Success;
    }
}
