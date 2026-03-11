namespace RomCleanup.Contracts.Models;

/// <summary>
/// Target format specification for a console type.
/// Port of $script:BEST_FORMAT from Convert.ps1.
/// </summary>
public sealed record ConversionTarget(
    string Extension,
    string ToolName,
    string Command);

/// <summary>
/// Result of a single file conversion operation.
/// </summary>
public sealed record ConversionResult(
    string SourcePath,
    string? TargetPath,
    ConversionOutcome Outcome,
    string? Reason = null,
    int ExitCode = 0);

/// <summary>
/// Outcome classification for a conversion operation.
/// </summary>
public enum ConversionOutcome
{
    /// <summary>Successfully converted.</summary>
    Success,
    /// <summary>Skipped (already target format, unsupported source, etc.).</summary>
    Skipped,
    /// <summary>Conversion failed (tool error, verification failure).</summary>
    Error
}
