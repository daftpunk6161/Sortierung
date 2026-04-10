namespace Romulus.Contracts.Models;

/// <summary>
/// Result of a safe settings load operation. Contains the loaded settings plus
/// corruption metadata when the source file was malformed (TASK-173).
/// </summary>
public sealed record SettingsLoadResult(
    RomulusSettings Settings,
    bool WasCorrupt = false,
    string? CorruptionMessage = null);
