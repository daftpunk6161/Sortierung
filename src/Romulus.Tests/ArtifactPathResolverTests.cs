using Romulus.Infrastructure.Paths;
using Xunit;

namespace Romulus.Tests;

public sealed class ArtifactPathResolverTests : IDisposable
{
    private readonly string _tempRoot;

    public ArtifactPathResolverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ArtifactPaths_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, true);
    }

    [Fact]
    public void NormalizeRoot_TrimsTrailingSeparators()
    {
        var withTrailing = _tempRoot + Path.DirectorySeparatorChar;

        var normalized = ArtifactPathResolver.NormalizeRoot(withTrailing);

        Assert.Equal(Path.GetFullPath(_tempRoot), normalized);
    }

    [Fact]
    public void NormalizeRootForIdentity_IsStableForSameInput()
    {
        var a = ArtifactPathResolver.NormalizeRootForIdentity(_tempRoot);
        var b = ArtifactPathResolver.NormalizeRootForIdentity(_tempRoot + Path.DirectorySeparatorChar);

        Assert.Equal(a, b);
    }

    [Fact]
    public void GetSiblingDirectory_UsesParentDirectory()
    {
        var root = Path.Combine(_tempRoot, "roms");
        Directory.CreateDirectory(root);

        var sibling = ArtifactPathResolver.GetSiblingDirectory(root, "reports");

        Assert.Equal(Path.Combine(_tempRoot, "reports"), sibling);
    }

    [Fact]
    public void GetSiblingDirectory_WhenRootHasNoParent_AppendsSibling()
    {
        var driveRoot = Path.GetPathRoot(_tempRoot)!;

        var sibling = ArtifactPathResolver.GetSiblingDirectory(driveRoot, "audit");

        Assert.Equal(Path.Combine(driveRoot, "audit"), sibling);
    }

    [Fact]
    public void GetArtifactDirectory_SingleRoot_ReturnsSiblingDirectory()
    {
        var root = Path.Combine(_tempRoot, "roms");
        Directory.CreateDirectory(root);

        var artifact = ArtifactPathResolver.GetArtifactDirectory(new[] { root }, "reports");

        Assert.Equal(Path.Combine(_tempRoot, "reports"), artifact);
    }

    [Fact]
    public void GetArtifactDirectory_MultiRoot_ReturnsAppDataArtifactDirectory()
    {
        var r1 = Path.Combine(_tempRoot, "r1");
        var r2 = Path.Combine(_tempRoot, "r2");
        Directory.CreateDirectory(r1);
        Directory.CreateDirectory(r2);

        var artifact = ArtifactPathResolver.GetArtifactDirectory(new[] { r1, r2 }, "audit");

        Assert.Contains(Path.Combine("Romulus", "artifacts"), artifact, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("audit"), artifact, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetArtifactDirectory_MultiRoot_OrderIndependent_IsDeterministic()
    {
        var r1 = Path.Combine(_tempRoot, "order-a");
        var r2 = Path.Combine(_tempRoot, "order-b");
        Directory.CreateDirectory(r1);
        Directory.CreateDirectory(r2);

        var first = ArtifactPathResolver.GetArtifactDirectory(new[] { r1, r2 }, "reports");
        var second = ArtifactPathResolver.GetArtifactDirectory(new[] { r2, r1 }, "reports");

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetArtifactDirectory_MultiRoot_DeduplicatesDuplicateRoots()
    {
        var r1 = Path.Combine(_tempRoot, "dup");
        var r2 = Path.Combine(_tempRoot, "other");
        Directory.CreateDirectory(r1);
        Directory.CreateDirectory(r2);

        var a = ArtifactPathResolver.GetArtifactDirectory(new[] { r1, r1, r2 }, "reports");
        var b = ArtifactPathResolver.GetArtifactDirectory(new[] { r1, r2 }, "reports");

        Assert.Equal(a, b);
    }

    [Fact]
    public void GetArtifactDirectory_DifferentRootSets_ProduceDifferentFingerprints()
    {
        var r1 = Path.Combine(_tempRoot, "set1-a");
        var r2 = Path.Combine(_tempRoot, "set1-b");
        var r3 = Path.Combine(_tempRoot, "set2-c");
        Directory.CreateDirectory(r1);
        Directory.CreateDirectory(r2);
        Directory.CreateDirectory(r3);

        var set1 = ArtifactPathResolver.GetArtifactDirectory(new[] { r1, r2 }, "reports");
        var set2 = ArtifactPathResolver.GetArtifactDirectory(new[] { r1, r3 }, "reports");

        Assert.NotEqual(set1, set2);
    }

    [Fact]
    public void GetArtifactDirectory_Throws_WhenNoRootsGiven()
    {
        var ex = Assert.Throws<ArgumentException>(() => ArtifactPathResolver.GetArtifactDirectory(Array.Empty<string>(), "reports"));

        Assert.Contains("At least one root", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetArtifactDirectory_Throws_WhenOnlyWhitespaceRootsGiven()
    {
        var ex = Assert.Throws<ArgumentException>(() => ArtifactPathResolver.GetArtifactDirectory(new[] { " ", "" }, "reports"));

        Assert.Contains("At least one root", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetArtifactDirectory_Throws_WhenArtifactFolderIsBlank()
    {
        var root = Path.Combine(_tempRoot, "root");
        Directory.CreateDirectory(root);

        Assert.Throws<ArgumentException>(() => ArtifactPathResolver.GetArtifactDirectory(new[] { root }, ""));
    }
}
