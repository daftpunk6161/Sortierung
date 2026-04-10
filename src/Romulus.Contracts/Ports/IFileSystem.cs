namespace Romulus.Contracts.Ports;

/// <summary>
/// Port interface for file system operations.
/// Maps to New-FileSystemPort in PortInterfaces.ps1.
/// </summary>
public interface IFileSystem
{
    bool TestPath(string literalPath, string pathType = "Any");
    string EnsureDirectory(string path);
    IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null);

    /// <summary>
    /// Returns scan warnings collected during the last GetFilesSafe call.
    /// Default implementation returns no warnings.
    /// </summary>
    IReadOnlyList<string> ConsumeScanWarnings() => Array.Empty<string>();

    bool FileExists(string literalPath)
    {
        if (string.IsNullOrWhiteSpace(literalPath))
            return false;

        return TestPath(literalPath, "Leaf");
    }

    bool DirectoryExists(string literalPath)
    {
        if (string.IsNullOrWhiteSpace(literalPath))
            return false;

        return TestPath(literalPath, "Container");
    }

    IReadOnlyList<string> GetDirectoryFiles(string directoryPath, string searchPattern)
    {
        // Contract-level default intentionally performs no direct I/O.
        // Implementations with real filesystem access should override this method.
        return Array.Empty<string>();
    }
    string? MoveItemSafely(string sourcePath, string destinationPath);

    string? MoveItemSafely(string sourcePath, string destinationPath, bool overwrite)
    {
        // Default contract behavior preserves historical rename-on-conflict semantics.
        return MoveItemSafely(sourcePath, destinationPath);
    }

    long? GetAvailableFreeSpace(string path)
    {
        // Contract-level default intentionally performs no direct I/O.
        // Implementations with real filesystem access should override this method.
        return null;
    }

    /// <summary>
    /// SEC-MOVE-04: Move with explicit root containment validation.
    /// Returns null if destination is outside allowedRoot.
    /// </summary>
    string? MoveItemSafely(string sourcePath, string destinationPath, string allowedRoot)
    {
        if (string.IsNullOrWhiteSpace(allowedRoot))
            throw new ArgumentException("Allowed root must not be empty.", nameof(allowedRoot));

        var normalizedRoot = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;
        var normalizedDest = Path.GetFullPath(destinationPath).TrimEnd(Path.DirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;

        if (!normalizedDest.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return null;

        return MoveItemSafely(sourcePath, destinationPath);
    }
    string? RenameItemSafely(string sourcePath, string newFileName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(newFileName))
            return null;

        // SEC-RENAME-01: Block path segments in newFileName (prevents traversal via "..\..\")
        if (!string.Equals(Path.GetFileName(newFileName), newFileName, StringComparison.Ordinal))
            throw new InvalidOperationException("Blocked: Rename target must be a file name without path segments.");

        // SEC-RENAME-02: Block NTFS Alternate Data Streams
        if (newFileName.Contains(':'))
            throw new InvalidOperationException("Blocked: Rename target contains ADS separator.");

        // SEC-RENAME-03: Block invalid filename characters
        if (newFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("Blocked: Rename target contains invalid filename characters.");

        var sourceDir = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceDir))
            return null;

        var targetPath = Path.Combine(sourceDir, newFileName);
        return MoveItemSafely(sourcePath, targetPath);
    }
    bool MoveDirectorySafely(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
            return false;

        // SEC-DIRMOVE-01: Block directory traversal in destination
        var destSegments = destinationPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (destSegments.Any(s => s == ".."))
            throw new InvalidOperationException("Blocked: Destination path contains directory traversal.");

        // SEC-DIRMOVE-02: Normalize and validate paths
        var fullSource = Path.GetFullPath(sourcePath);
        var fullDest = Path.GetFullPath(destinationPath);

        if (string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source and destination are the same path.");

        if (!DirectoryExists(fullSource))
            return false;

        // SEC-DIRMOVE-03: Block reparse points on source directory
        if (IsReparsePoint(fullSource))
            throw new InvalidOperationException("Blocked: Source directory is a reparse point.");

        // SEC-DIRMOVE-04: Block reparse points on destination parent
        var destParent = Path.GetDirectoryName(fullDest);
        if (!string.IsNullOrEmpty(destParent)
            && DirectoryExists(destParent)
            && IsReparsePoint(destParent))
        {
            throw new InvalidOperationException("Blocked: Destination parent is a reparse point.");
        }

        // Contract-level default intentionally performs no direct I/O move.
        // Implementations with real filesystem access should override this method.
        return false;
    }
    string? ResolveChildPathWithinRoot(string rootPath, string relativePath);
    bool IsReparsePoint(string path);
    void DeleteFile(string path);
    void CopyFile(string sourcePath, string destinationPath, bool overwrite = false);
}
