using System.Security.Cryptography;
using System.Text;

namespace Romulus.Infrastructure.Paths;

public static class ArtifactPathResolver
{
    public static string GetArtifactDirectory(IReadOnlyList<string> roots, string artifactFolderName)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactFolderName);

        var normalizedRoots = roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(NormalizeRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedRoots.Length == 0)
            throw new ArgumentException("At least one root is required.", nameof(roots));

        if (normalizedRoots.Length == 1)
            return GetSiblingDirectory(normalizedRoots[0], artifactFolderName);

        var artifactRoot = Path.Combine(
            AppStoragePathResolver.ResolveRoamingAppDirectory(),
            "artifacts",
            "multi-root-" + ComputeFingerprint(normalizedRoots));

        return Path.Combine(artifactRoot, artifactFolderName);
    }

    public static string NormalizeRoot(string rootPath)
        => Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Given a file path and a list of already-normalized roots, returns the root
    /// that contains the file, or null if no root matches.
    /// </summary>
    public static string? FindContainingRoot(string filePath, IReadOnlyList<string> normalizedRoots)
    {
        var full = Path.GetFullPath(filePath);
        return normalizedRoots.FirstOrDefault(r =>
            full.Length > r.Length &&
            full.StartsWith(r, StringComparison.OrdinalIgnoreCase) &&
            full[r.Length] is '\\' or '/');
    }

    public static string NormalizeRootForIdentity(string rootPath)
    {
        var normalized = NormalizeRoot(rootPath);
        return OperatingSystem.IsWindows()
            ? normalized.ToUpperInvariant()
            : normalized;
    }

    public static string ComputeRootsFingerprint(IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var normalizedRoots = roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(NormalizeRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedRoots.Length == 0)
            return "no-roots";

        return ComputeFingerprint(normalizedRoots);
    }

    public static string GetSiblingDirectory(string rootPath, string siblingName)
    {
        var parent = Path.GetDirectoryName(rootPath);
        if (string.IsNullOrEmpty(parent))
            return Path.Combine(rootPath, siblingName);

        return Path.Combine(parent, siblingName);
    }

    private static string ComputeFingerprint(IReadOnlyList<string> normalizedRoots)
    {
        var payload = string.Join("\n", normalizedRoots.Select(NormalizeRootForIdentity));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }
}
