using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests.Safety;

/// <summary>
/// Property-style coverage of MoveItemSafely(allowedRoot=...) refusing
/// to move outside allowed roots regardless of input shape.
///
/// Invariant: For every adversarial destination path that does NOT resolve into
/// the supplied allowedRoot, MoveItemSafely must return null (refusal) AND
/// leave the source file untouched.
/// </summary>
public sealed class MoveOutsideRootsPropertyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _allowedRoot;
    private readonly string _outsideRoot;

    public MoveOutsideRootsPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_B3_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _allowedRoot = Path.Combine(_tempDir, "allowed");
        _outsideRoot = Path.Combine(_tempDir, "outside");
        Directory.CreateDirectory(_allowedRoot);
        Directory.CreateDirectory(_outsideRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    public static IEnumerable<object[]> AdversarialDestinations()
    {
        // Each tuple: (label, build dest from outsideRoot/allowedRoot)
        yield return new object[] { "absolute-path-outside-root" };
        yield return new object[] { "sibling-via-relative-traversal" };
        yield return new object[] { "deep-traversal-then-outside" };
        yield return new object[] { "different-drive-style-segment" };
        yield return new object[] { "root-prefix-not-equal" };
    }

    [Theory]
    [MemberData(nameof(AdversarialDestinations))]
    public void FileSystemAdapter_AdversarialDestinationOutsideAllowedRoot_RefusesAndPreservesSource(string label)
    {
        var src = Path.Combine(_allowedRoot, $"src_{label}.bin");
        File.WriteAllBytes(src, [1, 2, 3, 4, 5]);

        var dest = label switch
        {
            "absolute-path-outside-root" =>
                Path.Combine(_outsideRoot, "evil.bin"),
            "sibling-via-relative-traversal" =>
                Path.GetFullPath(Path.Combine(_allowedRoot, "..", "outside", "evil.bin")),
            "deep-traversal-then-outside" =>
                Path.GetFullPath(Path.Combine(_allowedRoot, "sub", "..", "..", "outside", "evil.bin")),
            "different-drive-style-segment" =>
                Path.Combine(_outsideRoot, "deep", "evil.bin"),
            "root-prefix-not-equal" =>
                Path.Combine(_tempDir, "allowed-look-alike", "evil.bin"),
            _ => throw new InvalidOperationException(label)
        };

        var fs = new FileSystemAdapter();
        var result = fs.MoveItemSafely(src, dest, allowedRoot: _allowedRoot);

        Assert.Null(result);
        Assert.True(File.Exists(src), $"Source must remain after refused move ({label}).");
        Assert.False(File.Exists(dest), $"Destination must not exist after refused move ({label}).");
    }

    [Fact]
    public void FileSystemAdapter_DestinationInsideAllowedRoot_Succeeds()
    {
        var src = Path.Combine(_allowedRoot, "src-ok.bin");
        var dest = Path.Combine(_allowedRoot, "sub", "dest-ok.bin");
        File.WriteAllBytes(src, [10, 20, 30]);

        var fs = new FileSystemAdapter();
        var result = fs.MoveItemSafely(src, dest, allowedRoot: _allowedRoot);

        Assert.NotNull(result);
        Assert.True(File.Exists(dest));
        Assert.False(File.Exists(src));
    }

    [Fact]
    public void FileSystemAdapter_DirectoryTraversalInDestinationPath_ThrowsInvariantViolation()
    {
        var src = Path.Combine(_allowedRoot, "src-traversal.bin");
        File.WriteAllBytes(src, [1]);

        var dest = Path.Combine(_allowedRoot, "..", "outside", "evil.bin");

        var fs = new FileSystemAdapter();
        Assert.Throws<InvalidOperationException>(() => fs.MoveItemSafely(src, dest));
        Assert.True(File.Exists(src), "Source must remain after traversal-rejected move.");
    }
}
