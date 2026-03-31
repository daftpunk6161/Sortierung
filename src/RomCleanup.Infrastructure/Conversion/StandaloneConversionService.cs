using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Analysis;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.Infrastructure.Conversion;

/// <summary>
/// Standalone conversion service for converting individual files or directories
/// outside of the full run pipeline. Used by CLI convert subcommand and API.
/// </summary>
public sealed class StandaloneConversionService
{
    private readonly IFormatConverter _converter;

    public StandaloneConversionService(IFormatConverter converter)
    {
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
    }

    /// <summary>
    /// Create a StandaloneConversionService using the standard RunEnvironment setup.
    /// </summary>
    public static StandaloneConversionService? Create(string inputPath, Action<string>? onWarning = null)
    {
        var rootDir = File.Exists(inputPath)
            ? Path.GetDirectoryName(inputPath) ?? "."
            : inputPath;

        var env = new RunEnvironmentFactory().Create(
            new RunOptions { Roots = [rootDir] },
            onWarning);

        return env.Converter is not null
            ? new StandaloneConversionService(env.Converter)
            : null;
    }

    /// <summary>
    /// Convert a single file, auto-detecting console from path if not provided.
    /// </summary>
    public ConversionResult ConvertFile(
        string filePath,
        string? consoleKey = null,
        string? targetFormat = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return new ConversionResult(filePath, null, ConversionOutcome.Error, "Source file not found.");

        var ext = Path.GetExtension(filePath);
        var resolvedConsole = consoleKey ?? CollectionAnalysisService.DetectConsoleFromPath(filePath);

        var target = _converter.GetTargetFormat(resolvedConsole, ext);
        if (target is null)
            return new ConversionResult(filePath, null, ConversionOutcome.Skipped, $"No conversion target for {ext} on {resolvedConsole}.");

        // If a specific target format was requested, verify it matches
        if (!string.IsNullOrWhiteSpace(targetFormat))
        {
            var requestedExt = targetFormat.StartsWith('.') ? targetFormat : "." + targetFormat;
            if (!string.Equals(target.Extension, requestedExt, StringComparison.OrdinalIgnoreCase))
                return new ConversionResult(filePath, null, ConversionOutcome.Skipped,
                    $"Resolved target {target.Extension} does not match requested format {requestedExt}.");
        }

        return _converter.Convert(filePath, target, cancellationToken);
    }

    /// <summary>
    /// Convert all files in a directory (non-recursive by default).
    /// </summary>
    public StandaloneConversionReport ConvertDirectory(
        string directoryPath,
        string? consoleKey = null,
        string? targetFormat = null,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
            return new StandaloneConversionReport([], 0, 0, 0);

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directoryPath, "*", searchOption);

        var results = new List<ConversionResult>();
        int converted = 0, skipped = 0, errors = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = ConvertFile(file, consoleKey, targetFormat, cancellationToken);
            results.Add(result);

            switch (result.Outcome)
            {
                case ConversionOutcome.Success:
                    converted++;
                    break;
                case ConversionOutcome.Skipped:
                    skipped++;
                    break;
                default:
                    errors++;
                    break;
            }
        }

        return new StandaloneConversionReport(results, converted, skipped, errors);
    }
}

/// <summary>
/// Result of a standalone batch conversion operation.
/// </summary>
public sealed record StandaloneConversionReport(
    IReadOnlyList<ConversionResult> Results,
    int Converted,
    int Skipped,
    int Errors);
