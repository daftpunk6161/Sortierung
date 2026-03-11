using RomCleanup.Contracts.Models;

namespace RomCleanup.Contracts.Ports;

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
    ConversionResult Convert(string sourcePath, ConversionTarget target, string? sevenZipPath = null);

    /// <summary>
    /// Verify a converted file using the appropriate tool.
    /// </summary>
    bool Verify(string targetPath, ConversionTarget target);
}
