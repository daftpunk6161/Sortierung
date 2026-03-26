using RomCleanup.Contracts.Ports;

namespace RomCleanup.Tests.TestFixtures;

/// <summary>
/// In-memory IFileSystem for unit testing without real I/O.
/// </summary>
internal sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path, string content)
    {
        _files[path] = content;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) _directories.Add(dir);
    }

    public bool TestPath(string literalPath, string pathType = "Any")
    {
        if (pathType == "Container") return _directories.Contains(literalPath);
        return _files.ContainsKey(literalPath) || _directories.Contains(literalPath);
    }

    public string EnsureDirectory(string path) { _directories.Add(path); return path; }

    public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
    {
        var exts = allowedExtensions?.Select(e => e.StartsWith('.') ? e : "." + e)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _files.Keys
            .Where(k => k.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .Where(k => exts is null || exts.Contains(Path.GetExtension(k)))
            .ToList();
    }

    public string? MoveItemSafely(string sourcePath, string destinationPath)
    {
        if (!_files.TryGetValue(sourcePath, out var content)) return null;
        _files.Remove(sourcePath);
        _files[destinationPath] = content;
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir)) _directories.Add(dir);
        return destinationPath;
    }

    public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
    {
        var full = Path.Combine(rootPath, relativePath);
        return full.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    public bool IsReparsePoint(string path) => false;
    public void DeleteFile(string path) => _files.Remove(path);
    public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        if (_files.TryGetValue(sourcePath, out var content))
            _files[destinationPath] = content;
    }
}
