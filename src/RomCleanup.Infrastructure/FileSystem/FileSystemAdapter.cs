using System.IO;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.FileSystem;

/// <summary>
/// Production implementation of IFileSystem.
/// Port of FileOps.ps1 — path-traversal protection, reparse-point blocking,
/// collision-safe moves with __DUP suffixes.
/// </summary>
public sealed class FileSystemAdapter : IFileSystem
{
    private const int MaxDuplicateAttempts = 10_000;

    public bool TestPath(string literalPath, string pathType = "Any")
    {
        if (string.IsNullOrWhiteSpace(literalPath)) return false;

        return pathType switch
        {
            "Leaf" => File.Exists(literalPath),
            "Container" => Directory.Exists(literalPath),
            _ => File.Exists(literalPath) || Directory.Exists(literalPath)
        };
    }

    public string EnsureDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        Directory.CreateDirectory(path);
        return Path.GetFullPath(path);
    }

    public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
    {
        if (string.IsNullOrWhiteSpace(root))
            return Array.Empty<string>();

        if (!Directory.Exists(root))
            return Array.Empty<string>();

        var extSet = allowedExtensions is not null
            ? new HashSet<string>(allowedExtensions.Select(e => e.StartsWith(".") ? e : "." + e),
                                  StringComparer.OrdinalIgnoreCase)
            : null;

        var results = new List<string>();

        // Iterative DFS to avoid stack overflow on deep trees
        var stack = new Stack<string>();
        stack.Push(root);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            if (!visited.Add(dir))
                continue;

            // Skip reparse-point directories (symlinks/junctions) except the root itself
            if (!string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch
                {
                    continue; // inaccessible directory
                }
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (extSet is not null)
                {
                    var ext = Path.GetExtension(file);
                    if (!extSet.Contains(ext))
                        continue;
                }
                results.Add(file);
            }

            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var sub in subdirs)
                stack.Push(sub);
        }

        return results;
    }

    public bool MoveItemSafely(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path must not be empty.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path must not be empty.", nameof(destinationPath));

        var fullSource = Path.GetFullPath(sourcePath);
        var fullDest = Path.GetFullPath(destinationPath);

        if (string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source and destination are the same path.");

        if (!File.Exists(fullSource))
            throw new FileNotFoundException("Source file not found.", fullSource);

        // Block reparse points on source
        var sourceAttrs = File.GetAttributes(fullSource);
        if ((sourceAttrs & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Blocked: Source is a reparse point.");

        // Ensure destination directory exists
        var destDir = Path.GetDirectoryName(fullDest);
        if (!string.IsNullOrEmpty(destDir))
        {
            // Block reparse points on destination parent
            if (Directory.Exists(destDir))
            {
                var destDirInfo = new DirectoryInfo(destDir);
                if ((destDirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Blocked: Destination parent is a reparse point.");
            }
            Directory.CreateDirectory(destDir);
        }

        // Collision handling with __DUP suffix
        var finalDest = fullDest;
        if (File.Exists(finalDest))
        {
            var baseName = Path.GetFileNameWithoutExtension(fullDest);
            var ext = Path.GetExtension(fullDest);
            var dir = Path.GetDirectoryName(fullDest) ?? "";

            bool moved = false;
            for (int i = 1; i <= MaxDuplicateAttempts; i++)
            {
                finalDest = Path.Combine(dir, $"{baseName}__DUP{i}{ext}");
                if (!File.Exists(finalDest))
                {
                    File.Move(fullSource, finalDest);
                    moved = true;
                    break;
                }
            }

            if (!moved)
                throw new IOException($"Could not find free DUP slot after {MaxDuplicateAttempts} attempts.");
        }
        else
        {
            File.Move(fullSource, finalDest);
        }

        return true;
    }

    public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        if (string.IsNullOrWhiteSpace(rootPath))
            return null;

        try
        {
            var candidate = Path.IsPathRooted(relativePath)
                ? Path.GetFullPath(relativePath)
                : Path.GetFullPath(Path.Combine(rootPath, relativePath));

            var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar)
                               + Path.DirectorySeparatorChar;
            var normalizedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar)
                                    + Path.DirectorySeparatorChar;

            if (!normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return null;

            // Check for reparse points in ancestry
            if (HasReparsePointInAncestry(candidate, rootPath))
                return null;

            return candidate;
        }
        catch
        {
            return null; // fail-safe
        }
    }

    private static bool HasReparsePointInAncestry(string path, string stopAtRoot)
    {
        var normalizedRoot = Path.GetFullPath(stopAtRoot).TrimEnd(Path.DirectorySeparatorChar);
        var current = Path.GetDirectoryName(Path.GetFullPath(path));

        while (!string.IsNullOrEmpty(current)
            && current.Length > normalizedRoot.Length)
        {
            try
            {
                if (Directory.Exists(current))
                {
                    var di = new DirectoryInfo(current);
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
                        return true;
                }

                current = Path.GetDirectoryName(current);
            }
            catch
            {
                return true; // fail-safe: treat inaccessible as reparse
            }
        }

        return false;
    }
}
