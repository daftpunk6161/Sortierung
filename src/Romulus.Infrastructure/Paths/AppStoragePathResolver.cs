using System.Security;
using Romulus.Contracts;

namespace Romulus.Infrastructure.Paths;

/// <summary>
/// Resolves persistent storage roots for standard and portable mode.
/// Portable mode is enabled when a <c>.portable</c> marker exists next to the executable.
/// </summary>
public static class AppStoragePathResolver
{
    private const string PortableMarkerFileName = ".portable";
    private const string PortableDirectoryName = ".romulus";

    public static bool IsPortableMode()
    {
        var markerPath = Path.Combine(AppContext.BaseDirectory, PortableMarkerFileName);
        return File.Exists(markerPath);
    }

    public static string ResolvePortableRootDirectory()
        => Path.Combine(AppContext.BaseDirectory, PortableDirectoryName);

    public static string ResolveRoamingAppDirectory()
    {
        if (IsPortableMode())
            return ResolvePortableRootDirectory();

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppIdentity.AppFolderName);
    }

    public static string ResolveLocalAppDirectory()
    {
        if (IsPortableMode())
            return ResolvePortableRootDirectory();

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppIdentity.AppFolderName);
    }

    public static string ResolveRoamingPath(params string[] segments)
        => CombinePath(ResolveRoamingAppDirectory(), segments);

    public static string ResolveLocalPath(params string[] segments)
        => CombinePath(ResolveLocalAppDirectory(), segments);

    private static string CombinePath(string root, IReadOnlyList<string> segments)
    {
        var fullRoot = Path.GetFullPath(root);

        if (segments.Count == 0)
            return fullRoot;

        var sanitized = segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        if (sanitized.Length == 0)
            return fullRoot;

        var combined = Path.GetFullPath(Path.Combine([fullRoot, .. sanitized]));
        var normalizedRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCombined = combined.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;

        if (!normalizedCombined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(normalizedCombined, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path segments resolved outside application storage root.");
        }

        EnsureNoReparsePointInExistingAncestry(combined);

        return combined;
    }

    private static void EnsureNoReparsePointInExistingAncestry(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                var fileAttributes = File.GetAttributes(fullPath);
                if ((fileAttributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException($"Path resolves to a reparse point: {fullPath}");
            }

            var current = Directory.Exists(fullPath)
                ? fullPath
                : Path.GetDirectoryName(fullPath);

            while (!string.IsNullOrWhiteSpace(current))
            {
                if (Directory.Exists(current))
                {
                    var directoryAttributes = File.GetAttributes(current);
                    if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException($"Path contains a reparse point in ancestry: {current}");
                }

                var trimmedCurrent = current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var root = Path.GetPathRoot(trimmedCurrent);
                if (!string.IsNullOrWhiteSpace(root) &&
                    string.Equals(trimmedCurrent, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = Path.GetDirectoryName(trimmedCurrent);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new InvalidOperationException("Unable to validate reparse-point ancestry for application storage path.", ex);
        }
    }
}