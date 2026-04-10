using Romulus.Contracts;
using Romulus.Infrastructure.Paths;
using Xunit;

namespace Romulus.Tests;

public class AppStoragePathResolverTests
{
    [Fact]
    public void ResolvePortableRootDirectory_IsUnderBaseDirectory()
    {
        var expected = Path.Combine(AppContext.BaseDirectory, ".romulus");
        Assert.Equal(expected, AppStoragePathResolver.ResolvePortableRootDirectory());
    }

    [Fact]
    public void ResolveRoamingAppDirectory_MatchesCurrentMode()
    {
        var resolved = AppStoragePathResolver.ResolveRoamingAppDirectory();

        if (AppStoragePathResolver.IsPortableMode())
        {
            Assert.Equal(AppStoragePathResolver.ResolvePortableRootDirectory(), resolved);
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.StartsWith(appData, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(AppIdentity.AppFolderName, resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveLocalAppDirectory_MatchesCurrentMode()
    {
        var resolved = AppStoragePathResolver.ResolveLocalAppDirectory();

        if (AppStoragePathResolver.IsPortableMode())
        {
            Assert.Equal(AppStoragePathResolver.ResolvePortableRootDirectory(), resolved);
            return;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.StartsWith(localAppData, resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(AppIdentity.AppFolderName, resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveRoamingPath_CombinesAndSkipsEmptySegments()
    {
        var path = AppStoragePathResolver.ResolveRoamingPath("reports", "", "daily.html");
        var expected = Path.Combine(AppStoragePathResolver.ResolveRoamingAppDirectory(), "reports", "daily.html");

        Assert.Equal(expected, path);
    }

    [Fact]
    public void ResolveLocalPath_CombinesAndSkipsEmptySegments()
    {
        var path = AppStoragePathResolver.ResolveLocalPath("cache", "", "hashes.json");
        var expected = Path.Combine(AppStoragePathResolver.ResolveLocalAppDirectory(), "cache", "hashes.json");

        Assert.Equal(expected, path);
    }

    [Fact]
    public void ResolveRoamingPath_PathTraversal_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AppStoragePathResolver.ResolveRoamingPath("..", "escape.txt"));
    }

    [Fact]
    public void ResolveLocalPath_RootedSegment_Throws()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "outside.txt");

        Assert.Throws<InvalidOperationException>(() =>
            AppStoragePathResolver.ResolveLocalPath("cache", rooted));
    }

    [Fact]
    public void ResolveRoamingPath_ReparsePointInAncestry_Throws()
    {
        var root = AppStoragePathResolver.ResolveRoamingAppDirectory();
        Directory.CreateDirectory(root);

        var id = Guid.NewGuid().ToString("N");
        var targetDir = Path.Combine(root, $"reparse-target-{id}");
        var linkDir = Path.Combine(root, $"reparse-link-{id}");
        Directory.CreateDirectory(targetDir);

        try
        {
            try
            {
                _ = Directory.CreateSymbolicLink(linkDir, targetDir);
            }
            catch
            {
                // Symbolic links can require elevated privileges; skip when unavailable.
                return;
            }

            Assert.Throws<InvalidOperationException>(() =>
                AppStoragePathResolver.ResolveRoamingPath(Path.GetFileName(linkDir), "payload.dat"));
        }
        finally
        {
            TryDeleteDirectory(linkDir);
            TryDeleteDirectory(targetDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}