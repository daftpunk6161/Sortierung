namespace RomCleanup.Contracts.Models;

/// <summary>
/// UI-facing item for conversion review confirmation dialogs.
/// </summary>
public sealed record ConversionReviewEntry(
    string SourcePath,
    string? TargetExtension,
    string SafetyReason);
