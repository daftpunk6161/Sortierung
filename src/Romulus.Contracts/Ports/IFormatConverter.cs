using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Port interface for format conversion operations.
/// Port of Convert.ps1 — orchestrates chdman, dolphintool, 7z, psxtract.
/// </summary>
public interface IFormatConverter
{
    /// <summary>
    /// Get the target conversion format for a console + source extension.
    /// Returns null if no conversion is defined.
    /// </summary>
    ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension);

    /// <summary>
    /// Convert a file to its target format.
    /// </summary>
    ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify a converted file using the appropriate tool.
    /// </summary>
    bool Verify(string targetPath, ConversionTarget target);

    /// <summary>
    /// Returns missing tool names for a requested conversion format before execution starts.
    /// Default implementation returns no missing tools to keep backward compatibility.
    /// </summary>
    IReadOnlyList<string> GetMissingToolsForFormat(string? convertFormat)
        => Array.Empty<string>();
}
