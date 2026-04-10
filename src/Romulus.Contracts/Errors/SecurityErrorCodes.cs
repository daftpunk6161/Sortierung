namespace Romulus.Contracts.Errors;

/// <summary>
/// Centralized error code constants shared across API, CLI, and GUI.
/// Prevents magic-string divergence and enables consistent test assertions.
/// </summary>
public static class SecurityErrorCodes
{
    // Root-level validation (primary scan roots)
    public const string RootReparsePoint = "SEC-ROOT-REPARSE-POINT";
    public const string RootAttributeCheckFailed = "SEC-ROOT-ATTRIBUTE-CHECK-FAILED";
    public const string SystemDirectoryRoot = "SEC-SYSTEM-DIRECTORY-ROOT";
    public const string DriveRootNotAllowed = "SEC-DRIVE-ROOT-NOT-ALLOWED";

    // Field-level path validation (trashRoot, datRoot, auditRoot)
    public const string InvalidPath = "SEC-INVALID-PATH";
    public const string ReparsePoint = "SEC-REPARSE-POINT";
    public const string AttributeCheckFailed = "SEC-ATTRIBUTE-CHECK-FAILED";
    public const string SystemDirectory = "SEC-SYSTEM-DIRECTORY";
    public const string DriveRoot = "SEC-DRIVE-ROOT";
    public const string OutsideAllowedRoots = "SEC-OUTSIDE-ALLOWED-ROOTS";
}
