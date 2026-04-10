using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage boost for FileSystemAdapter: MoveDirectorySafely, RenameItemSafely,
/// DeleteFile, CopyFile, IsReparsePoint, and ResolveChildPathWithinRoot edge cases.
/// Targets ~108 uncovered lines across safety checks, collision handling, and I/O paths.
/// </summary>
public sealed class FileSystemAdapterEdgeCaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemAdapter _fs;

    public FileSystemAdapterEdgeCaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_FSEdge_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _fs = new FileSystemAdapter();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* best-effort */ }
    }

    // ===== MoveDirectorySafely =====

    [Fact]
    public void MoveDirectorySafely_BasicMove_Succeeds()
    {
        var src = Path.Combine(_tempDir, "srcdir");
        var dst = Path.Combine(_tempDir, "dstdir");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "file.txt"), "data");

        var result = _fs.MoveDirectorySafely(src, dst);

        Assert.True(result);
        Assert.False(Directory.Exists(src));
        Assert.True(Directory.Exists(dst));
        Assert.True(File.Exists(Path.Combine(dst, "file.txt")));
    }

    [Fact]
    public void MoveDirectorySafely_Collision_AddsDupSuffix()
    {
        var src = Path.Combine(_tempDir, "moveme");
        var dst = Path.Combine(_tempDir, "target");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "a.bin"), "src");
        Directory.CreateDirectory(dst);
        File.WriteAllText(Path.Combine(dst, "b.bin"), "existing");

        var result = _fs.MoveDirectorySafely(src, dst);

        Assert.True(result);
        Assert.True(Directory.Exists(dst)); // original kept
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "target__DUP1"))); // moved here
    }

    [Fact]
    public void MoveDirectorySafely_SamePath_Throws()
    {
        var dir = Path.Combine(_tempDir, "same");
        Directory.CreateDirectory(dir);

        Assert.Throws<InvalidOperationException>(() => _fs.MoveDirectorySafely(dir, dir));
    }

    [Fact]
    public void MoveDirectorySafely_NonExistentSource_Throws()
    {
        var src = Path.Combine(_tempDir, "nope");
        var dst = Path.Combine(_tempDir, "dst");

        Assert.Throws<DirectoryNotFoundException>(() => _fs.MoveDirectorySafely(src, dst));
    }

    [Fact]
    public void MoveDirectorySafely_TraversalInDest_Throws()
    {
        var src = Path.Combine(_tempDir, "moveable");
        Directory.CreateDirectory(src);

        Assert.Throws<InvalidOperationException>(
            () => _fs.MoveDirectorySafely(src, Path.Combine(_tempDir, "..", "escape")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MoveDirectorySafely_EmptySource_Throws(string? src)
    {
        Assert.Throws<ArgumentException>(
            () => _fs.MoveDirectorySafely(src!, Path.Combine(_tempDir, "dst")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MoveDirectorySafely_EmptyDest_Throws(string? dst)
    {
        var src = Path.Combine(_tempDir, "exist");
        Directory.CreateDirectory(src);

        Assert.Throws<ArgumentException>(() => _fs.MoveDirectorySafely(src, dst!));
    }

    // ===== RenameItemSafely =====

    [Fact]
    public void RenameItemSafely_BasicRename_Succeeds()
    {
        var src = Path.Combine(_tempDir, "old.rom");
        File.WriteAllText(src, "data");

        var result = _fs.RenameItemSafely(src, "new.rom");

        Assert.NotNull(result);
        Assert.False(File.Exists(src));
        Assert.True(File.Exists(Path.Combine(_tempDir, "new.rom")));
    }

    [Fact]
    public void RenameItemSafely_ReservedDeviceName_Throws()
    {
        var src = Path.Combine(_tempDir, "test.rom");
        File.WriteAllText(src, "data");

        Assert.Throws<InvalidOperationException>(() => _fs.RenameItemSafely(src, "CON.txt"));
    }

    [Fact]
    public void RenameItemSafely_InvalidChars_Throws()
    {
        var src = Path.Combine(_tempDir, "test.rom");
        File.WriteAllText(src, "data");

        Assert.Throws<InvalidOperationException>(() => _fs.RenameItemSafely(src, "bad<name>.rom"));
    }

    [Fact]
    public void RenameItemSafely_PathSegment_Throws()
    {
        var src = Path.Combine(_tempDir, "test.rom");
        File.WriteAllText(src, "data");

        Assert.Throws<InvalidOperationException>(() => _fs.RenameItemSafely(src, "sub\\name.rom"));
    }

    [Fact]
    public void RenameItemSafely_SameName_Throws()
    {
        var src = Path.Combine(_tempDir, "same.rom");
        File.WriteAllText(src, "data");

        Assert.Throws<InvalidOperationException>(() => _fs.RenameItemSafely(src, "same.rom"));
    }

    [Fact]
    public void RenameItemSafely_NonExistent_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => _fs.RenameItemSafely(
            Path.Combine(_tempDir, "nope.rom"), "new.rom"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RenameItemSafely_EmptySource_Throws(string? src)
    {
        Assert.Throws<ArgumentException>(() => _fs.RenameItemSafely(src!, "new.rom"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RenameItemSafely_EmptyNewName_Throws(string? name)
    {
        var src = Path.Combine(_tempDir, "test.rom");
        File.WriteAllText(src, "data");

        Assert.Throws<ArgumentException>(() => _fs.RenameItemSafely(src, name!));
    }

    [Fact]
    public void RenameItemSafely_CollisionAddsDupSuffix()
    {
        var src = Path.Combine(_tempDir, "orig.rom");
        File.WriteAllText(src, "moving");
        File.WriteAllText(Path.Combine(_tempDir, "target.rom"), "existing");

        var result = _fs.RenameItemSafely(src, "target.rom");

        Assert.NotNull(result);
        Assert.Contains("__DUP1", result!);
    }

    [Fact]
    public void RenameItemSafely_LockedFile_ReturnsNull()
    {
        var src = Path.Combine(_tempDir, "locked.rom");
        File.WriteAllText(src, "data");

        using var lockHandle = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = _fs.RenameItemSafely(src, "renamed.rom");

        Assert.Null(result);
    }

    // ===== DeleteFile =====

    [Fact]
    public void DeleteFile_ReadOnlyFile_ClearsAndDeletes()
    {
        var file = Path.Combine(_tempDir, "readonly.rom");
        File.WriteAllText(file, "data");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        _fs.DeleteFile(file);

        Assert.False(File.Exists(file));
    }

    [Fact]
    public void DeleteFile_NormalFile_Deletes()
    {
        var file = Path.Combine(_tempDir, "normal.rom");
        File.WriteAllText(file, "data");

        _fs.DeleteFile(file);

        Assert.False(File.Exists(file));
    }

    [Fact]
    public void DeleteFile_NonExistent_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => _fs.DeleteFile(Path.Combine(_tempDir, "nope.rom")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DeleteFile_EmptyPath_Throws(string? path)
    {
        Assert.Throws<ArgumentException>(() => _fs.DeleteFile(path!));
    }

    // ===== CopyFile =====

    [Fact]
    public void CopyFile_BasicCopy_Succeeds()
    {
        var src = Path.Combine(_tempDir, "source.rom");
        var dst = Path.Combine(_tempDir, "copy.rom");
        File.WriteAllText(src, "data");

        _fs.CopyFile(src, dst);

        Assert.True(File.Exists(src)); // source preserved
        Assert.True(File.Exists(dst));
        Assert.Equal("data", File.ReadAllText(dst));
    }

    [Fact]
    public void CopyFile_Overwrite_ReplacesExisting()
    {
        var src = Path.Combine(_tempDir, "source.rom");
        var dst = Path.Combine(_tempDir, "existing.rom");
        File.WriteAllText(src, "new");
        File.WriteAllText(dst, "old");

        _fs.CopyFile(src, dst, overwrite: true);

        Assert.Equal("new", File.ReadAllText(dst));
    }

    [Fact]
    public void CopyFile_NonExistentSource_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            _fs.CopyFile(Path.Combine(_tempDir, "nope.rom"), Path.Combine(_tempDir, "dst.rom")));
    }

    [Fact]
    public void CopyFile_AdsInSource_Throws()
    {
        var src = Path.Combine(_tempDir, "source.rom");
        File.WriteAllText(src, "data");

        Assert.Throws<InvalidOperationException>(
            () => _fs.CopyFile(src + ":hidden", Path.Combine(_tempDir, "dst.rom")));
    }

    [Fact]
    public void CopyFile_CreatesDestinationDirectory()
    {
        var src = Path.Combine(_tempDir, "source.rom");
        var dst = Path.Combine(_tempDir, "sub", "deep", "copy.rom");
        File.WriteAllText(src, "data");

        _fs.CopyFile(src, dst);

        Assert.True(File.Exists(dst));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void CopyFile_EmptySource_Throws(string? src)
    {
        Assert.Throws<ArgumentException>(() => _fs.CopyFile(src!, Path.Combine(_tempDir, "dst.rom")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void CopyFile_EmptyDest_Throws(string? dst)
    {
        Assert.Throws<ArgumentException>(() => _fs.CopyFile(Path.Combine(_tempDir, "src.rom"), dst!));
    }

    // ===== IsReparsePoint =====

    [Fact]
    public void IsReparsePoint_NormalFile_ReturnsFalse()
    {
        var file = Path.Combine(_tempDir, "normal.rom");
        File.WriteAllText(file, "data");

        Assert.False(_fs.IsReparsePoint(file));
    }

    [Fact]
    public void IsReparsePoint_NormalDirectory_ReturnsFalse()
    {
        Assert.False(_fs.IsReparsePoint(_tempDir));
    }

    [Fact]
    public void IsReparsePoint_NonExistent_ReturnsTrueFailClosed()
    {
        // SEC: Fail-closed — inaccessible/non-existent treated as reparse point
        Assert.True(_fs.IsReparsePoint(Path.Combine(_tempDir, "nonexistent")));
    }

    // ===== ResolveChildPathWithinRoot edge cases =====

    [Theory]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT1")]
    [InlineData("NUL.txt")]
    [InlineData("COM9.rom")]
    public void ResolveChildPath_ReservedDeviceNames_ReturnsNull(string name)
    {
        Assert.Null(_fs.ResolveChildPathWithinRoot(_tempDir, name));
    }

    [Theory]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("sub/file.")]
    [InlineData("sub/name ")]
    public void ResolveChildPath_TrailingDotOrSpace_ReturnsNull(string rel)
    {
        Assert.Null(_fs.ResolveChildPathWithinRoot(_tempDir, rel));
    }

    [Fact]
    public void ResolveChildPath_AdsColon_ReturnsNull()
    {
        Assert.Null(_fs.ResolveChildPathWithinRoot(_tempDir, "file:stream"));
    }

    [Fact]
    public void ResolveChildPath_DeepNestedValid_ReturnsPath()
    {
        var result = _fs.ResolveChildPathWithinRoot(_tempDir, "a/b/c/d.rom");
        Assert.NotNull(result);
        Assert.StartsWith(Path.GetFullPath(_tempDir), result!);
    }

    // ===== MoveItemSafely with AllowedRoot =====

    [Fact]
    public void MoveItemSafely_WithinAllowedRoot_Succeeds()
    {
        var src = Path.Combine(_tempDir, "src.rom");
        var dst = Path.Combine(_tempDir, "dst.rom");
        File.WriteAllText(src, "data");

        var result = _fs.MoveItemSafely(src, dst, _tempDir);

        Assert.NotNull(result);
        Assert.True(File.Exists(dst));
    }

    [Fact]
    public void MoveItemSafely_OutsideAllowedRoot_ReturnsNull()
    {
        var src = Path.Combine(_tempDir, "src.rom");
        File.WriteAllText(src, "data");
        var otherDir = Path.Combine(Path.GetTempPath(), "OtherRoot_" + Guid.NewGuid().ToString("N")[..8]);
        var dst = Path.Combine(otherDir, "dst.rom");

        try
        {
            var result = _fs.MoveItemSafely(src, dst, _tempDir);
            Assert.Null(result);
            Assert.True(File.Exists(src)); // source untouched
        }
        finally
        {
            try { Directory.Delete(otherDir, true); } catch { }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MoveItemSafely_EmptyAllowedRoot_Throws(string? root)
    {
        var src = Path.Combine(_tempDir, "src.rom");
        File.WriteAllText(src, "data");

        Assert.Throws<ArgumentException>(() => _fs.MoveItemSafely(src, src + ".bak", root!));
    }

    // ===== MoveItemSafely path traversal =====

    [Fact]
    public void MoveItemSafely_TraversalInDest_Throws()
    {
        var src = Path.Combine(_tempDir, "file.rom");
        File.WriteAllText(src, "data");

        Assert.Throws<InvalidOperationException>(
            () => _fs.MoveItemSafely(src, Path.Combine(_tempDir, "..", "escape.rom")));
    }

    [Fact]
    public void MoveItemSafely_AdsInDest_Throws()
    {
        var src = Path.Combine(_tempDir, "file.rom");
        File.WriteAllText(src, "data");

        Assert.Throws<InvalidOperationException>(
            () => _fs.MoveItemSafely(src, Path.Combine(_tempDir, "dest.rom:hidden")));
    }

    // ===== IsWindowsReservedDeviceName static helper =====

    [Theory]
    [InlineData("CON", true)]
    [InlineData("PRN", true)]
    [InlineData("AUX", true)]
    [InlineData("NUL", true)]
    [InlineData("COM0", true)]
    [InlineData("COM9", true)]
    [InlineData("LPT0", true)]
    [InlineData("LPT9", true)]
    [InlineData("con", true)]
    [InlineData("NUL.txt", true)]
    [InlineData("COM1.rom", true)]
    [InlineData("CONNIE", false)]
    [InlineData("GAME", false)]
    [InlineData("", false)]
    [InlineData("CO", false)]
    [InlineData("COMA", false)]
    public void IsWindowsReservedDeviceName_Detects(string segment, bool expected)
    {
        Assert.Equal(expected, FileSystemAdapter.IsWindowsReservedDeviceName(segment));
    }

    // ===== GetFilesSafe extension filter edge cases =====

    [Fact]
    public void GetFilesSafe_EmptyExtensionSet_ReturnsNothing()
    {
        File.WriteAllText(Path.Combine(_tempDir, "test.rom"), "data");

        var files = _fs.GetFilesSafe(_tempDir, Array.Empty<string>());

        Assert.Empty(files);
    }

    [Fact]
    public void GetFilesSafe_CaseInsensitiveExtension()
    {
        File.WriteAllText(Path.Combine(_tempDir, "game.ROM"), "data");

        var files = _fs.GetFilesSafe(_tempDir, new[] { ".rom" });

        // Extension matching is case-insensitive on Windows
        Assert.Single(files);
    }

    [Fact]
    public void GetFilesSafe_DeepNesting_Succeeds()
    {
        var deep = _tempDir;
        for (int i = 0; i < 10; i++)
            deep = Path.Combine(deep, $"level{i}");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "deep.rom"), "data");

        var files = _fs.GetFilesSafe(_tempDir);

        Assert.Single(files);
        Assert.Contains("deep.rom", files[0]);
    }

    [Fact]
    public void GetFilesSafe_ResultsAreDeterministicallySorted()
    {
        File.WriteAllText(Path.Combine(_tempDir, "z.rom"), "");
        File.WriteAllText(Path.Combine(_tempDir, "a.rom"), "");
        File.WriteAllText(Path.Combine(_tempDir, "m.rom"), "");

        var files = _fs.GetFilesSafe(_tempDir);

        Assert.Equal(3, files.Count);
        Assert.EndsWith("a.rom", files[0]);
        Assert.EndsWith("m.rom", files[1]);
        Assert.EndsWith("z.rom", files[2]);
    }

    // ===== NormalizePathNfc static helper =====

    [Fact]
    public void NormalizePathNfc_ReturnsNfcNormalized()
    {
        // NFD é = e + \u0301, NFC é = \u00E9
        var nfd = Path.Combine(_tempDir, "caf\u0065\u0301.rom");
        var result = FileSystemAdapter.NormalizePathNfc(nfd);

        // Should contain NFC form of é
        Assert.Contains("caf\u00E9.rom", result);
    }

    [Fact]
    public void NormalizePathNfc_AlreadyNfc_ReturnsSameFullPath()
    {
        var nfc = Path.Combine(_tempDir, "game.rom");
        var result = FileSystemAdapter.NormalizePathNfc(nfc);

        Assert.Equal(Path.GetFullPath(nfc), result);
    }
}
