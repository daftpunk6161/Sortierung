using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Conversion;

/// <summary>
/// Handles psxtract-based conversions: PBP to CHD.
/// Extracted from FormatConverterAdapter for single-responsibility.
/// </summary>
internal sealed class PsxtractToolConverter
{
    private readonly IToolRunner _tools;

    public PsxtractToolConverter(IToolRunner tools)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    public ConversionResult Convert(string sourcePath, string targetPath, string toolPath, string command)
    {
        var args = new[] { command, "-i", sourcePath, "-o", targetPath };
        var result = _tools.InvokeProcess(toolPath, args, "psxtract");

        if (!result.Success)
        {
            ChdmanToolConverter.CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error,
                "psxtract-failed", result.ExitCode);
        }

        if (!ConversionOutputValidator.TryValidateCreatedOutput(targetPath, out var outputFailureReason))
        {
            ChdmanToolConverter.CleanupPartialOutput(targetPath);
            return new ConversionResult(sourcePath, null, ConversionOutcome.Error, outputFailureReason);
        }

        return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
    }
}
