using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Tests.TestFixtures;

/// <summary>
/// Configurable IFormatConverter for unit testing conversion logic without real tool invocations.
/// </summary>
internal sealed class ConfigurableConverter : IFormatConverter
{
    private readonly Dictionary<string, ConversionTarget> _targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ConversionResult> _results = new(StringComparer.OrdinalIgnoreCase);
    public bool VerifyAlwaysSucceeds { get; set; } = true;

    public void RegisterTarget(string consoleKey, string sourceExtension, ConversionTarget target)
        => _targets[$"{consoleKey}|{sourceExtension}"] = target;

    public void RegisterResult(string sourcePath, ConversionResult result)
        => _results[sourcePath] = result;

    public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
        => _targets.TryGetValue($"{consoleKey}|{sourceExtension}", out var t) ? t : null;

    public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        => _results.TryGetValue(sourcePath, out var r) ? r
            : new ConversionResult(sourcePath, Path.ChangeExtension(sourcePath, target.Extension), ConversionOutcome.Success);

    public bool Verify(string targetPath, ConversionTarget target) => VerifyAlwaysSucceeds;
}
