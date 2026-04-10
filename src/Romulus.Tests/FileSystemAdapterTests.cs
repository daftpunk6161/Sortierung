using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

public class FileSystemAdapterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemAdapter _fs;

    public FileSystemAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_FSTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _fs = new FileSystemAdapter();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // --- TestPath ---

    [Fact]
    public void TestPath_ExistingFile_ReturnsTrue()
    {
        var file = Path.Combine(_tempDir, "test.rom");
        File.WriteAllText(file, "data");

        Assert.True(_fs.TestPath(file));
        Assert.True(_fs.TestPath(file, "Leaf"));
        Assert.False(_fs.TestPath(file, "Container"));
    }

    [Fact]
    public void TestPath_ExistingDirectory_ReturnsTrue()
    {
        Assert.True(_fs.TestPath(_tempDir));
        Assert.True(_fs.TestPath(_tempDir, "Container"));
        Assert.False(_fs.TestPath(_tempDir, "Leaf"));
    }

    [Fact]
    public void TestPath_NonExistent_ReturnsFalse()
    {
        Assert.False(_fs.TestPath(Path.Combine(_tempDir, "nope.bin")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TestPath_EmptyPath_ReturnsFalse(string? path)
    {
        Assert.False(_fs.TestPath(path!));
    }

    // --- EnsureDirectory ---

    [Fact]
    public void EnsureDirectory_CreatesNestedDirs()
    {
        var nested = Path.Combine(_tempDir, "a", "b", "c");

        var result = _fs.EnsureDirectory(nested);

        Assert.True(Directory.Exists(nested));
        Assert.Equal(Path.GetFullPath(nested), result);
    }

    [Fact]
    public void EnsureDirectory_ExistingDir_ReturnsPath()
    {
        var result = _fs.EnsureDirectory(_tempDir);
        Assert.Equal(Path.GetFullPath(_tempDir), result);
    }

    [Fact]
    public void EnsureDirectory_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => _fs.EnsureDirectory(""));
    }

    // --- GetFilesSafe ---

    [Fact]
    public void GetFilesSafe_FindsAllFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.zip"), "");
        File.WriteAllText(Path.Combine(_tempDir, "b.rom"), "");
        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "c.zip"), "");

        var files = _fs.GetFilesSafe(_tempDir);

        Assert.Equal(3, files.Count);
    }

    [Fact]
    public void GetFilesSafe_FiltersExtensions()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.zip"), "");
        File.WriteAllText(Path.Combine(_tempDir, "b.rom"), "");
        File.WriteAllText(Path.Combine(_tempDir, "c.txt"), "");

        var files = _fs.GetFilesSafe(_tempDir, new[] { ".zip", ".rom" });

        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.True(f.EndsWith(".zip") || f.EndsWith(".rom")));
    }

    [Fact]
    public void GetFilesSafe_NonExistentRoot_ReturnsEmpty()
    {
        var files = _fs.GetFilesSafe(Path.Combine(_tempDir, "nope"));
        Assert.Empty(files);
    }

    [Fact]
    public void GetFilesSafe_EmptyRoot_ReturnsEmpty()
    {
        Assert.Empty(_fs.GetFilesSafe(""));
    }

    // --- MoveItemSafely ---

    [Fact]
    public void MoveItemSafely_MovesFile()
    {
        var src = Path.Combine(_tempDir, "source.rom");
        var dst = Path.Combine(_tempDir, "dest", "target.rom");
        File.WriteAllText(src, "data");

        _fs.MoveItemSafely(src, dst);

        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dst));
    }

    [Fact]
    public void MoveItemSafely_CollisionAddsDupSuffix()
    {
        var src = Path.Combine(_tempDir, "source.rom");
        var dst = Path.Combine(_tempDir, "target.rom");
        File.WriteAllText(src, "new");
        File.WriteAllText(dst, "existing");

        _fs.MoveItemSafely(src, dst);

        Assert.True(File.Exists(dst)); // original still there
        Assert.True(File.Exists(Path.Combine(_tempDir, "target__DUP1.rom")));
    }

    [Fact]
    public void MoveItemSafely_Overwrite_ReplacesExistingDestination()
    {
        var src = Path.Combine(_tempDir, "source-overwrite.rom");
        var dst = Path.Combine(_tempDir, "target-overwrite.rom");
        File.WriteAllText(src, "new-content");
        File.WriteAllText(dst, "old-content");

        var movedPath = _fs.MoveItemSafely(src, dst, overwrite: true);

        Assert.Equal(dst, movedPath);
        Assert.False(File.Exists(src));
        Assert.Equal("new-content", File.ReadAllText(dst));
        Assert.False(File.Exists(Path.Combine(_tempDir, "target-overwrite__DUP1.rom")));
    }

    [Fact]
    public void MoveItemSafely_SamePath_Throws()
    {
        var file = Path.Combine(_tempDir, "test.rom");
        File.WriteAllText(file, "data");

        Assert.Throws<InvalidOperationException>(() => _fs.MoveItemSafely(file, file));
    }

    [Fact]
    public void MoveItemSafely_NonExistentSource_Throws()
    {
        var src = Path.Combine(_tempDir, "nope.rom");
        var dst = Path.Combine(_tempDir, "target.rom");

        Assert.Throws<FileNotFoundException>(() => _fs.MoveItemSafely(src, dst));
    }

    [Fact]
    public void MoveItemSafely_LockedFile_ReturnsNull()
    {
        // SEC-IO-01: Locked files should return null gracefully instead of throwing
        var src = Path.Combine(_tempDir, "locked.rom");
        var dst = Path.Combine(_tempDir, "dest.rom");
        File.WriteAllText(src, "data");

        // Lock the source file by holding it open with FileShare.None
        using var lockHandle = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = _fs.MoveItemSafely(src, dst);
        Assert.Null(result);
    }

    [Fact]
    public void MoveItemSafely_BlocksRootedAdsSource()
    {
        var source = Path.Combine(_tempDir, "game.rom");
        var destination = Path.Combine(_tempDir, "dest.rom");
        File.WriteAllText(source, "data");

        var rootedAdsSource = source + ":hidden";

        Assert.Throws<InvalidOperationException>(() => _fs.MoveItemSafely(rootedAdsSource, destination));
    }

    [Fact]
    public void CopyFile_BlocksAdsDestination()
    {
        var src = Path.Combine(_tempDir, "source.rom");
        File.WriteAllText(src, "data");
        var dst = Path.Combine(_tempDir, "copy.rom:ads");

        Assert.Throws<InvalidOperationException>(() => _fs.CopyFile(src, dst));
    }

    [Fact]
    public void CopyFile_BlocksReservedDeviceNameDestination()
    {
        var src = Path.Combine(_tempDir, "source.rom");
        File.WriteAllText(src, "data");
        var dst = Path.Combine(_tempDir, "CON.txt");

        Assert.Throws<InvalidOperationException>(() => _fs.CopyFile(src, dst));
    }

    [Fact]
    public void CopyFile_BlocksTrailingDotOrSpaceDestinationSegment()
    {
        var src = Path.Combine(_tempDir, "source.rom");
        File.WriteAllText(src, "data");
        var dst = Path.Combine(_tempDir, "unsafe. ");

        Assert.Throws<InvalidOperationException>(() => _fs.CopyFile(src, dst));
    }

    [Fact]
    public void GetAvailableFreeSpace_ValidPath_ReturnsValue()
    {
        var value = _fs.GetAvailableFreeSpace(_tempDir);

        Assert.True(value.HasValue);
        Assert.True(value.Value >= 0);
    }

    // --- ResolveChildPathWithinRoot ---

    [Fact]
    public void ResolveChildPath_ValidRelative_ReturnsFullPath()
    {
        var result = _fs.ResolveChildPathWithinRoot(_tempDir, "sub/file.rom");

        Assert.NotNull(result);
        Assert.StartsWith(Path.GetFullPath(_tempDir), result!);
    }

    [Fact]
    public void ResolveChildPath_TraversalAttempt_ReturnsNull()
    {
        var result = _fs.ResolveChildPathWithinRoot(_tempDir, "../../etc/passwd");
        Assert.Null(result);
    }

    [Fact]
    public void ResolveChildPath_AbsoluteOutsideRoot_ReturnsNull()
    {
        var result = _fs.ResolveChildPathWithinRoot(_tempDir, @"C:\Windows\System32\cmd.exe");
        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ResolveChildPath_EmptyRelative_ReturnsNull(string? rel)
    {
        Assert.Null(_fs.ResolveChildPathWithinRoot(_tempDir, rel!));
    }

    [Fact]
    public void ResolveChildPath_NullRoot_ReturnsNull()
    {
        Assert.Null(_fs.ResolveChildPathWithinRoot(null!, "file.rom"));
    }

    [Fact]
    public void GetFilesSafe_JunctionSubdirectory_IsBlockedAsReparsePoint_Issue9()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(_tempDir, "root");
        var target = Path.Combine(_tempDir, "target");
        var junction = Path.Combine(root, "junction");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(target);

        var regularFile = Path.Combine(root, "regular.rom");
        var linkedFile = Path.Combine(target, "linked.rom");
        File.WriteAllText(regularFile, "regular");
        File.WriteAllText(linkedFile, "linked");

        try
        {
            Directory.CreateSymbolicLink(junction, target);
        }
        catch
        {
            return;
        }

        var files = _fs.GetFilesSafe(root);

        Assert.Contains(regularFile, files, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(files, f => f.Contains("linked.rom", StringComparison.OrdinalIgnoreCase));
    }
}
