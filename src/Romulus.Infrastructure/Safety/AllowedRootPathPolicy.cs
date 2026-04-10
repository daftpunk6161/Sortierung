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
            .Select(NormalizeRoot)
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

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception) when (path is not null)
        {
            return false;
        }

        foreach (var root in _allowedRoots)
        {
            if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
                return true;

            if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormalizeRoot(string root)
    {
        var fullPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath;
    }
}
