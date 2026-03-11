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
    bool MoveItemSafely(string sourcePath, string destinationPath);
    string? ResolveChildPathWithinRoot(string rootPath, string relativePath);
}
