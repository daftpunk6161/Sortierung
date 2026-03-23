namespace RomCleanup.Contracts.Ports;

/// <summary>
/// Port interface for file system operations.
/// Maps to New-FileSystemPort in PortInterfaces.ps1.
/// </summary>
public interface IFileSystem
{
    bool TestPath(string literalPath, string pathType = "Any");
    string EnsureDirectory(string path);
    IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null);
    string? MoveItemSafely(string sourcePath, string destinationPath);
    string? RenameItemSafely(string sourcePath, string newFileName)
        => throw new NotSupportedException("RenameItemSafely not implemented.");
    bool MoveDirectorySafely(string sourcePath, string destinationPath)
        => throw new NotSupportedException("MoveDirectorySafely not implemented.");
    string? ResolveChildPathWithinRoot(string rootPath, string relativePath);
    bool IsReparsePoint(string path);
    void DeleteFile(string path);
    void CopyFile(string sourcePath, string destinationPath, bool overwrite = false);
}
