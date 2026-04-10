namespace Romulus.Contracts;

/// <summary>
/// Canonical classification for configuration validation failures.
/// Shared across infrastructure and entry points to avoid string-based error matching.
/// </summary>
public enum ConfigurationErrorCode
{
    ProtectedSystemPath,
    DriveRoot,
    UncPath,
    InvalidRegion,
    InvalidExtension,
    InvalidHashType,
    InvalidConvertFormat,
    InvalidConflictPolicy,
    InvalidMode,
    InvalidConsole,
    MissingDatRoot,
    MissingTrashRoot,
    InvalidPath,
    PathTraversal,
    ReparsePoint,
    AccessDenied,
    WorkflowNotFound,
    ProfileNotFound,
    Unknown
}