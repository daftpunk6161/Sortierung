using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Orchestration;

namespace Romulus.Infrastructure.Conversion;

/// <summary>
/// Standalone conversion service for converting individual files or directories
/// outside of the full run pipeline. Used by CLI convert subcommand and API.
/// </summary>
public sealed class StandaloneConversionService : IDisposable
{
    private readonly IFormatConverter _converter;
    private readonly ICollectionIndex? _collectionIndex;
    private readonly IDisposable? _lifetime;

    public StandaloneConversionService(
        IFormatConverter converter,
        ICollectionIndex? collectionIndex = null,
        IDisposable? lifetime = null)
    {
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _collectionIndex = collectionIndex;
        _lifetime = lifetime;
    }

    /// <summary>
    /// Create a StandaloneConversionService using the standard RunEnvironment setup.
    /// </summary>
    public static StandaloneConversionService? Create(string inputPath, Action<string>? onWarning = null)
        => Create(inputPath, false, onWarning);

    public static StandaloneConversionService? Create(string inputPath, bool approveConversionReview, Action<string>? onWarning = null)
    {
        var rootDir = File.Exists(inputPath)
            ? Path.GetDirectoryName(inputPath) ?? "."
            : inputPath;

        var env = new RunEnvironmentFactory().Create(
            new RunOptions { Roots = [rootDir], ApproveConversionReview = approveConversionReview },
            onWarning);

        if (env.Converter is null)
        {
            env.Dispose();
            return null;
        }

        return new StandaloneConversionService(env.Converter, env.CollectionIndex, env);
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

        var resolvedConsole = ResolveConsoleKey(filePath, consoleKey);
        var ext = SourcePathFormatDetector.ResolveSourceExtension(filePath);

        if (_converter is FormatConverterAdapter advancedConverter)
        {
            var plan = advancedConverter.PlanForConsole(filePath, resolvedConsole);
            if (!string.IsNullOrWhiteSpace(targetFormat))
            {
                var requestedExt = targetFormat.StartsWith('.') ? targetFormat : "." + targetFormat;
                var resolvedTarget = plan?.FinalTargetExtension
                    ?? advancedConverter.GetTargetFormat(resolvedConsole, ext)?.Extension
                    ?? ext;
                if (!string.Equals(resolvedTarget, requestedExt, StringComparison.OrdinalIgnoreCase))
                {
                    return new ConversionResult(filePath, null, ConversionOutcome.Skipped,
                        $"Resolved target {resolvedTarget} does not match requested format {requestedExt}.");
                }
            }

            return advancedConverter.ConvertForConsole(filePath, resolvedConsole, cancellationToken);
        }

        var target = _converter.GetTargetFormat(resolvedConsole, ext);
        if (target is null)
            return new ConversionResult(filePath, null, ConversionOutcome.Skipped, $"No conversion target for {ext} on {resolvedConsole}.");

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

    public void Dispose()
    {
        _lifetime?.Dispose();
    }

    private string ResolveConsoleKey(string filePath, string? consoleKey)
    {
        if (!string.IsNullOrWhiteSpace(consoleKey))
            return consoleKey;

        if (_collectionIndex is not null)
        {
            var entry = _collectionIndex.TryGetByPathAsync(Path.GetFullPath(filePath)).GetAwaiter().GetResult();
            if (entry is not null)
                return CollectionAnalysisService.ResolveConsoleLabel(entry.ConsoleKey, filePath);
        }

        return CollectionAnalysisService.DetectConsoleFromPath(filePath);
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
