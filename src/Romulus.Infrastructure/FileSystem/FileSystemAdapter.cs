using System.IO;
using System.Text;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Safety;

namespace Romulus.Infrastructure.FileSystem;

/// <summary>
/// Production implementation of IFileSystem.
/// Port of FileOps.ps1 — path-traversal protection, reparse-point blocking,
/// collision-safe moves with __DUP suffixes.
/// </summary>
public sealed class FileSystemAdapter : IFileSystem
{
    private const int MaxDuplicateAttempts = 10_000;
    private const int MaxScanWarningsPerCall = 200;
    private readonly object _scanWarningsGate = new();
    private readonly List<string> _scanWarnings = new();

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
        ClearScanWarnings();

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
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    RecordScanWarning($"Skipped inaccessible directory '{dir}': {ex.Message}");
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
                RecordScanWarning($"Skipped inaccessible directory '{dir}': unauthorized");
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                RecordScanWarning($"Skipped missing directory '{dir}' during scan");
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
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    RecordScanWarning($"Skipped inaccessible file '{file}': {ex.Message}");
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
                RecordScanWarning($"Skipped subdirectory listing for '{dir}': unauthorized");
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                RecordScanWarning($"Skipped subdirectory listing for missing '{dir}'");
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

    public IReadOnlyList<string> ConsumeScanWarnings()
    {
        lock (_scanWarningsGate)
        {
            var warnings = _scanWarnings.ToArray();
            _scanWarnings.Clear();
            return warnings;
        }
    }

    private void ClearScanWarnings()
    {
        lock (_scanWarningsGate)
            _scanWarnings.Clear();
    }

    private void RecordScanWarning(string warning)
    {
        lock (_scanWarningsGate)
        {
            if (_scanWarnings.Count >= MaxScanWarningsPerCall)
                return;

            _scanWarnings.Add(warning);
        }
    }

    public string? MoveItemSafely(string sourcePath, string destinationPath)
        => MoveItemSafely(sourcePath, destinationPath, overwrite: false);

    public string? MoveItemSafely(string sourcePath, string destinationPath, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path must not be empty.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path must not be empty.", nameof(destinationPath));

        // SEC-MOVE-01: Block directory traversal in destination path
        var destSegments = destinationPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (destSegments.Any(s => s == ".."))
            throw new InvalidOperationException("Blocked: Destination path contains directory traversal.");

        // SEC-MOVE-03: Block NTFS Alternate Data Streams (parity with ResolveChildPathWithinRoot)
        if (HasAlternateDataStreamReference(sourcePath))
            throw new InvalidOperationException("Blocked: Source path contains NTFS ADS reference.");
        if (HasAlternateDataStreamReference(destinationPath))
            throw new InvalidOperationException("Blocked: Destination filename contains NTFS ADS reference.");

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
            // F27: validate the full destination ancestry, not only the immediate parent.
            var root = Path.GetPathRoot(fullDest) ?? destDir;
            if (HasReparsePointInAncestry(fullDest, root))
                throw new InvalidOperationException("Blocked: Destination path targets a reparse-point directory.");

            Directory.CreateDirectory(destDir);
        }

        // SEC-IO-01: Catch locked-file/IO errors gracefully → return null
        try
        {
            return MoveItemSafelyCore(fullSource, fullDest, overwrite);
        }
        catch (IOException) when (File.Exists(fullSource))
        {
            // Source still in place (locked/inaccessible) — graceful null return
            return null;
        }
    }

    /// <summary>
    /// SEC-MOVE-04: Move with explicit root containment validation.
    /// Verifies destination resolves within allowedRoot before proceeding.
    /// </summary>
    public string? MoveItemSafely(string sourcePath, string destinationPath, string allowedRoot)
    {
        if (string.IsNullOrWhiteSpace(allowedRoot))
            throw new ArgumentException("Allowed root must not be empty.", nameof(allowedRoot));

        var normalizedRoot = NormalizePathNfc(allowedRoot).TrimEnd(Path.DirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;
        var normalizedDest = NormalizePathNfc(destinationPath).TrimEnd(Path.DirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;

        if (!normalizedDest.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return null;

        return MoveItemSafely(sourcePath, destinationPath, overwrite: false);
    }

    public string? RenameItemSafely(string sourcePath, string newFileName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path must not be empty.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(newFileName))
            throw new ArgumentException("New filename must not be empty.", nameof(newFileName));

        // Rename must stay in the same directory; no subpaths are allowed.
        if (!string.Equals(Path.GetFileName(newFileName), newFileName, StringComparison.Ordinal))
            throw new InvalidOperationException("Blocked: Rename target must be a file name without path segments.");

        if (newFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("Blocked: Rename target contains invalid filename characters.");

        if (IsWindowsReservedDeviceName(newFileName))
            throw new InvalidOperationException("Blocked: Rename target uses a reserved Windows device name.");

        var fullSource = NormalizePathNfc(sourcePath);
        if (!File.Exists(fullSource))
            throw new FileNotFoundException("Source file not found.", fullSource);

        var sourceAttrs = File.GetAttributes(fullSource);
        if ((sourceAttrs & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Blocked: Source is a reparse point.");

        var sourceDir = Path.GetDirectoryName(fullSource)
                        ?? throw new InvalidOperationException("Blocked: Source has no parent directory.");

        var resolvedTarget = ResolveChildPathWithinRoot(sourceDir, newFileName);
        if (resolvedTarget is null)
            throw new InvalidOperationException("Blocked: Rename target failed root/path safety validation.");

        var fullTarget = NormalizePathNfc(resolvedTarget);
        if (string.Equals(fullSource, fullTarget, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source and destination are the same path.");

        try
        {
            return MoveItemSafelyCore(fullSource, fullTarget, overwrite: false);
        }
        catch (IOException) when (File.Exists(fullSource))
        {
            // Locked/inaccessible source should behave like MoveItemSafely.
            return null;
        }
    }

    /// <summary>
    /// Core move logic with collision handling. Separated for IOException catch scope.
    /// </summary>
    private static string MoveItemSafelyCore(string fullSource, string fullDest, bool overwrite)
    {
        if (overwrite)
        {
            File.Move(fullSource, fullDest, overwrite: true);
            return fullDest;
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

    public long? GetAvailableFreeSpace(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
                return null;

            var drive = new DriveInfo(root);
            return drive.AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public bool MoveDirectorySafely(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path must not be empty.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path must not be empty.", nameof(destinationPath));

        // SEC-MOVE-01: Block directory traversal in destination path (parity with MoveItemSafely)
        var destSegments = destinationPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (destSegments.Any(s => s == ".."))
            throw new InvalidOperationException("Blocked: Destination path contains directory traversal.");

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
                try
                {
                    Directory.Move(fullSource, finalDest);
                    moved = true;
                    break;
                }
                catch (IOException)
                {
                    // Slot already taken — try next index (eliminates TOCTOU with Directory.Exists)
                }
            }

            if (!moved)
                throw new IOException($"Could not find free DUP slot after {MaxDuplicateAttempts} attempts.");
        }
        else
        {
            try
            {
                Directory.Move(fullSource, finalDest);
            }
            catch (IOException) when (Directory.Exists(finalDest))
            {
                // Race: directory appeared between our check and move — use DUP suffix logic
                var dirName = Path.GetFileName(fullDest);
                var parentDir = Path.GetDirectoryName(fullDest) ?? "";

                bool moved = false;
                for (int i = 1; i <= MaxDuplicateAttempts; i++)
                {
                    finalDest = Path.Combine(parentDir, $"{dirName}__DUP{i}");
                    try
                    {
                        Directory.Move(fullSource, finalDest);
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

        return true;
    }

    public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        if (string.IsNullOrWhiteSpace(rootPath))
            return null;

        // SEC-PATH-01: Block NTFS Alternate Data Streams (colon in filename portion)
        if (relativePath.Contains(':'))
            return null;

        // SEC-PATH-02: Block segments with trailing dots/spaces (Windows silently strips them → path bypass)
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var seg in segments)
        {
            if (seg.Length > 0 && (seg[^1] == '.' || seg[^1] == ' '))
                return null;

            // SEC-PATH-03: Block Windows reserved device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9)
            if (IsWindowsReservedDeviceName(seg))
                return null;
        }

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
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or System.Security.SecurityException)
        {
            return null; // fail-safe
        }
    }

    /// <summary>
    /// SEC-PATH-03: Check if a filename (without extension) matches a Windows reserved device name.
    /// Reserved names: CON, PRN, AUX, NUL, COM0-COM9, LPT0-LPT9.
    /// These are reserved regardless of extension (e.g., "NUL.txt" is still reserved).
    /// </summary>
    internal static bool IsWindowsReservedDeviceName(string segment)
    {
        if (string.IsNullOrEmpty(segment))
            return false;

        // Strip extension if present (e.g., "NUL.txt" → "NUL")
        var dotIndex = segment.IndexOf('.');
        var nameOnly = dotIndex >= 0 ? segment[..dotIndex] : segment;

        return nameOnly.Length switch
        {
            3 => nameOnly.Equals("CON", StringComparison.OrdinalIgnoreCase)
              || nameOnly.Equals("PRN", StringComparison.OrdinalIgnoreCase)
              || nameOnly.Equals("AUX", StringComparison.OrdinalIgnoreCase)
              || nameOnly.Equals("NUL", StringComparison.OrdinalIgnoreCase),
            4 => (nameOnly.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                  || nameOnly.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                 && char.IsAsciiDigit(nameOnly[3]),
            _ => false
        };
    }

    private static bool HasAlternateDataStreamReference(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmedPath.Length == 0)
            return false;

        var fileName = Path.GetFileName(trimmedPath);
        if (string.IsNullOrEmpty(fileName))
            return false;

        return fileName.Contains(':');
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

        // SEC-IO-02: Clear ReadOnly attribute before delete to handle protected files robustly
        if ((attrs & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(fullPath, attrs & ~FileAttributes.ReadOnly);

        File.Delete(fullPath);
    }

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path must not be empty.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path must not be empty.", nameof(destinationPath));

        var validatedSource = SafetyValidator.NormalizePath(sourcePath)
            ?? throw new InvalidOperationException("Blocked: Source path failed safety validation.");
        var validatedDest = SafetyValidator.NormalizePath(destinationPath)
            ?? throw new InvalidOperationException("Blocked: Destination path failed safety validation.");

        var fullSource = NormalizePathNfc(validatedSource);
        var fullDest = NormalizePathNfc(validatedDest);

        if (HasAlternateDataStreamReference(fullSource))
            throw new InvalidOperationException("Blocked: Source path contains NTFS ADS reference.");
        if (HasAlternateDataStreamReference(fullDest))
            throw new InvalidOperationException("Blocked: Destination filename contains NTFS ADS reference.");

        var destFileName = Path.GetFileName(fullDest);
        if (IsWindowsReservedDeviceName(destFileName))
            throw new InvalidOperationException("Blocked: Destination filename uses a reserved Windows device name.");

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
            var root = Path.GetPathRoot(fullDest) ?? destDir;
            if (HasReparsePointInAncestry(fullDest, root))
                throw new InvalidOperationException("Blocked: Destination path targets a reparse-point directory.");

            Directory.CreateDirectory(destDir);
        }

        File.Copy(fullSource, fullDest, overwrite);
    }
}
