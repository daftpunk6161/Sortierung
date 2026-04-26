using System.Runtime.InteropServices;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests.Safety;

/// <summary>
/// Reparse-point and hard-link safety invariants.
///
/// Invariants:
///  1.  IsReparsePoint correctly identifies a symlink/junction as a reparse point.
///  2.  MoveItemSafely refuses to move a source that is itself a reparse point
///        (silent symlink-following would let an attacker redirect a move target
///        outside the allowed root).
///  3.  MoveItemSafely refuses to move a source with multiple hard links
///        (data-loss safety: moving the file would orphan its second name and
///        could surprise the user).
///
/// Symlink creation requires SeCreateSymbolicLinkPrivilege or Developer Mode.
/// Tests gracefully no-op when creation fails.
/// </summary>
[Collection("FileSystem")]
public sealed class ReparsePointHardlinkTests : IDisposable
{
    private readonly string _tempDir;

    public ReparsePointHardlinkTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_B7_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void FileSystemAdapter_IsReparsePoint_DetectsSymlink()
    {
        var target = Path.Combine(_tempDir, "real.bin");
        File.WriteAllBytes(target, [1, 2, 3]);

        var link = Path.Combine(_tempDir, "linked.bin");
        if (!TryCreateFileSymbolicLink(link, target))
            return; // privilege missing - skip gracefully

        var fs = new FileSystemAdapter();
        Assert.True(fs.IsReparsePoint(link));
        Assert.False(fs.IsReparsePoint(target));
    }

    [Fact]
    public void FileSystemAdapter_MoveItemSafely_RejectsReparsePointSource()
    {
        var target = Path.Combine(_tempDir, "real.bin");
        File.WriteAllBytes(target, [1, 2, 3]);

        var link = Path.Combine(_tempDir, "linked.bin");
        if (!TryCreateFileSymbolicLink(link, target))
            return; // privilege missing - skip gracefully

        var dest = Path.Combine(_tempDir, "moved.bin");
        var fs = new FileSystemAdapter();
        Assert.Throws<InvalidOperationException>(() => fs.MoveItemSafely(link, dest));
        Assert.True(File.Exists(link), "Symlink source must remain after refused move.");
        Assert.True(File.Exists(target), "Symlink target must remain.");
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void FileSystemAdapter_MoveItemSafely_RejectsHardLinkedSource()
    {
        var first = Path.Combine(_tempDir, "first.bin");
        File.WriteAllBytes(first, [9, 8, 7]);
        var second = Path.Combine(_tempDir, "second.bin");

        if (!TryCreateHardLink(second, first))
            return; // platform/filesystem does not support hard links - skip

        var dest = Path.Combine(_tempDir, "moved.bin");
        var fs = new FileSystemAdapter();
        Assert.Throws<InvalidOperationException>(() => fs.MoveItemSafely(first, dest));
        Assert.True(File.Exists(first), "Source with hard links must remain after refused move.");
        Assert.True(File.Exists(second), "Sibling hard link must remain.");
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void FileSystemAdapter_MoveItemSafely_RejectsDestinationThroughReparsePointAncestor()
    {
        // Build root/realDir + root/linkDir -> realDir, then attempt move into linkDir/x.
        var realDir = Path.Combine(_tempDir, "realDir");
        Directory.CreateDirectory(realDir);

        var linkDir = Path.Combine(_tempDir, "linkDir");
        if (!TryCreateDirectorySymbolicLink(linkDir, realDir))
            return; // privilege missing - skip gracefully

        var src = Path.Combine(_tempDir, "src.bin");
        File.WriteAllBytes(src, [4, 5, 6]);
        var dest = Path.Combine(linkDir, "evil.bin");

        var fs = new FileSystemAdapter();
        Assert.Throws<InvalidOperationException>(() => fs.MoveItemSafely(src, dest));
        Assert.True(File.Exists(src));
        Assert.False(File.Exists(dest));
    }

    // ─── platform helpers ──────────────────────────────────────────────

    private static bool TryCreateFileSymbolicLink(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateDirectorySymbolicLink(string path, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    private static bool TryCreateHardLink(string newLink, string existing)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            return CreateHardLinkW(newLink, existing, IntPtr.Zero);
        }
        catch
        {
            return false;
        }
    }
}
