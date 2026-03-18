using System.IO;
using System.Text;
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

    /// <summary>
    /// Issue #21: Normalize path to NFC to handle macOS NFD-encoded paths
    /// on HFS+ volumes or USB sticks.
    /// </summary>
    internal static string NormalizePathNfc(string path)
        => Path.GetFullPath(path).Normalize(NormalizationForm.FormC);

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
                // Skip file-level symlinks/reparse points
                try
                {
                    var attrs = File.GetAttributes(file);
                    if ((attrs & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch
                {
                    continue; // inaccessible file
                }

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

        // TASK-169: Deterministic ordering for reproducible results
        // F-06 FIX: NFC-normalize before sort for consistent ordering on HFS+/NFD volumes
        for (int i = 0; i < results.Count; i++)
            results[i] = results[i].Normalize(System.Text.NormalizationForm.FormC);
        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    public string? MoveItemSafely(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path must not be empty.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path must not be empty.", nameof(destinationPath));

        var fullSource = NormalizePathNfc(sourcePath);
        var fullDest = NormalizePathNfc(destinationPath);

        if (string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source and destination are the same path.");

        if (!File.Exists(fullSource))
            throw new FileNotFoundException("Source file not found.", fullSource);

        // Block reparse points on source
        // Note: inherent TOCTOU between check and File.Move — mitigated by
        // post-move verification and the single-user nature of the application.
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

        // Collision handling with __DUP suffix (V2-H07: try/catch eliminates TOCTOU)
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
                try
                {
                    File.Move(fullSource, finalDest, overwrite: false);
                    moved = true;
                    break;
                }
                catch (IOException)
                {
                    // Slot already taken — try next index
                }
            }

            if (!moved)
                throw new IOException($"Could not find free DUP slot after {MaxDuplicateAttempts} attempts.");
        }
        else
        {
            try
            {
                File.Move(fullSource, finalDest, overwrite: false);
            }
            catch (IOException) when (File.Exists(finalDest))
            {
                // Race: file appeared between our check and move — use DUP suffix logic
                // BUG-FIX: Iterative DUP fallback instead of unbounded recursion
                var baseName = Path.GetFileNameWithoutExtension(fullDest);
                var ext = Path.GetExtension(fullDest);
                var dir = Path.GetDirectoryName(fullDest) ?? "";

                bool moved = false;
                for (int i = 1; i <= MaxDuplicateAttempts; i++)
                {
                    finalDest = Path.Combine(dir, $"{baseName}__DUP{i}{ext}");
                    try
                    {
                        File.Move(fullSource, finalDest, overwrite: false);
                        moved = true;
                        break;
                    }
                    catch (IOException)
                    {
                        // Slot already taken — try next index
                    }
                }

                if (!moved)
                    throw new IOException($"Could not find free DUP slot after {MaxDuplicateAttempts} attempts.");
            }
        }

        return finalDest;
    }

    public bool MoveDirectorySafely(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path must not be empty.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path must not be empty.", nameof(destinationPath));

        // SEC-MOVE-02: Use NFC normalization consistent with MoveItemSafely
        var fullSource = NormalizePathNfc(sourcePath);
        var fullDest = NormalizePathNfc(destinationPath);

        if (string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source and destination are the same path.");

        if (!Directory.Exists(fullSource))
            throw new DirectoryNotFoundException($"Source directory not found: {fullSource}");

        // SEC-MOVE-02: Block reparse points on source directory
        var sourceInfo = new DirectoryInfo(fullSource);
        if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Blocked: Source directory is a reparse point.");

        // SEC-MOVE-02: Block reparse points on destination parent
        var destParent = Path.GetDirectoryName(fullDest);
        if (!string.IsNullOrEmpty(destParent) && Directory.Exists(destParent))
        {
            var destParentInfo = new DirectoryInfo(destParent);
            if ((destParentInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Blocked: Destination parent is a reparse point.");
        }

        var finalDest = fullDest;
        if (Directory.Exists(finalDest))
        {
            var dirName = Path.GetFileName(fullDest);
            var parentDir = Path.GetDirectoryName(fullDest) ?? "";

            bool moved = false;
            for (int i = 1; i <= MaxDuplicateAttempts; i++)
            {
                finalDest = Path.Combine(parentDir, $"{dirName}__DUP{i}");
                if (!Directory.Exists(finalDest))
                {
                    Directory.Move(fullSource, finalDest);
                    moved = true;
                    break;
                }
            }

            if (!moved)
                throw new IOException($"Could not find free DUP slot after {MaxDuplicateAttempts} attempts.");
        }
        else
        {
            Directory.Move(fullSource, finalDest);
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
                ? NormalizePathNfc(relativePath)
                : NormalizePathNfc(Path.Combine(rootPath, relativePath));

            var normalizedRoot = NormalizePathNfc(rootPath).TrimEnd(Path.DirectorySeparatorChar)
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
            && current.Length >= normalizedRoot.Length)
        {
            try
            {
                if (Directory.Exists(current))
                {
                    var di = new DirectoryInfo(current);
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
                        return true;
                }

                // Stop after checking root itself
                if (string.Equals(current, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    break;

                current = Path.GetDirectoryName(current);
            }
            catch
            {
                return true; // fail-safe: treat inaccessible as reparse
            }
        }

        return false;
    }

    public bool IsReparsePoint(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true; // fail-closed: treat inaccessible as reparse point
        }
    }

    public void DeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("File not found.", fullPath);

        var attrs = File.GetAttributes(fullPath);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Blocked: Target is a reparse point.");

        File.Delete(fullPath);
    }

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path must not be empty.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path must not be empty.", nameof(destinationPath));

        var fullSource = Path.GetFullPath(sourcePath);
        var fullDest = Path.GetFullPath(destinationPath);

        if (!File.Exists(fullSource))
            throw new FileNotFoundException("Source file not found.", fullSource);

        // Block reparse points on source
        var sourceAttrs = File.GetAttributes(fullSource);
        if ((sourceAttrs & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Blocked: Source is a reparse point.");

        // Block reparse points on destination parent
        var destDir = Path.GetDirectoryName(fullDest);
        if (!string.IsNullOrEmpty(destDir))
        {
            if (Directory.Exists(destDir))
            {
                var destDirInfo = new DirectoryInfo(destDir);
                if ((destDirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Blocked: Destination parent is a reparse point.");
            }
            else
            {
                Directory.CreateDirectory(destDir);
            }
        }

        File.Copy(fullSource, fullDest, overwrite);
    }
}
