using System.Text.Json.Serialization;

namespace RomCleanup.Contracts.Errors;

/// <summary>
/// Error classification for structured error handling.
/// Maps to PowerShell ErrorContracts.ps1 classes.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ErrorKind>))]
public enum ErrorKind
{
    /// <summary>Automatic retry eligible.</summary>
    Transient,

    /// <summary>Log and continue.</summary>
    Recoverable,

    /// <summary>Abort immediately, notify user.</summary>
    Critical
}

/// <summary>
/// Structured error object mirroring New-OperationError from ErrorContracts.ps1.
/// Namespaces: GUI-*, DAT-*, IO-*, SEC-*, RUN-*
/// </summary>
public sealed record OperationError(
    string Code,
    string Message,
    ErrorKind Kind,
    string? Module = null,
    [property: JsonIgnore] Exception? InnerException = null);
