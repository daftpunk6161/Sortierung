namespace Romulus.Infrastructure.Safety;

/// <summary>
/// Central allowlist for API-exposed filesystem paths in headless/remote mode.
/// Local loopback mode can keep legacy permissive behavior by leaving the allowlist empty.
/// </summary>
public sealed class AllowedRootPathPolicy
{
    private readonly string[] _allowedRoots;

    public AllowedRootPathPolicy(IEnumerable<string>? allowedRoots)
    {
        _allowedRoots = (allowedRoots ?? Array.Empty<string>())
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(static root => SafetyValidator.NormalizePath(root))
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(static root => Path.TrimEndingDirectorySeparator(root!))
            .Where(static root => !string.IsNullOrEmpty(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> AllowedRoots => _allowedRoots;

    public bool IsEnforced => _allowedRoots.Length > 0;

    public bool IsPathAllowed(string path)
    {
        if (!IsEnforced)
            return true;

        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = SafetyValidator.NormalizePath(path);
        if (normalized is null)
            return false;

        var fullPath = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // R2-005 FIX: Reject paths containing reparse points (symlinks/junctions)
        // to prevent symlink-based root-escape attacks.
        if (ContainsReparsePoint(fullPath))
            return false;

        foreach (var root in _allowedRoots)
        {
            if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
                return true;

            if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Validates a path against the provided root allowlist using the same policy rules
    /// as API security checks (normalization + reparse-point rejection).
    /// </summary>
    public static bool Validate(string path, IReadOnlyList<string>? roots)
    {
        var policy = new AllowedRootPathPolicy(roots ?? Array.Empty<string>());
        return policy.IsPathAllowed(path);
    }

    /// <summary>
    /// R2-005: Walk each segment of the resolved path and reject if any directory
    /// is a reparse point (symlink, junction, mount point).
    /// </summary>
    private static bool ContainsReparsePoint(string fullPath)
    {
        try
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(fullPath) ?? fullPath);
            while (dir is not null)
            {
                if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                    return true;
                dir = dir.Parent;
            }

            if (File.Exists(fullPath))
            {
                var fileAttributes = File.GetAttributes(fullPath);
                if ((fileAttributes & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }
}
