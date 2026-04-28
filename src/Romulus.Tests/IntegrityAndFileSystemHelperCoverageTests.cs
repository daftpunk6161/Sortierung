using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for IntegrityService pure helpers (FindCommonRoot)
/// and FileSystemAdapter helpers (IsWindowsReservedDeviceName, NormalizePathNfc).
/// </summary>
public sealed class IntegrityAndFileSystemHelperCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public IntegrityAndFileSystemHelperCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "IntFsTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── FindCommonRoot ──────────────────────────────────────────────

    [Fact]
    public void FindCommonRoot_EmptyList_ReturnsNull()
    {
        Assert.Null(IntegrityService.FindCommonRoot(Array.Empty<string>()));
    }

    [Fact]
    public void FindCommonRoot_SinglePath()
    {
        var file = Path.Combine(_tempDir, "sub", "file.txt");
        var result = IntegrityService.FindCommonRoot(new[] { file });
        Assert.NotNull(result);
        Assert.Equal(Path.Combine(_tempDir, "sub"), result);
    }

    [Fact]
    public void FindCommonRoot_SameDirectory()
    {
        var paths = new[]
        {
            Path.Combine(_tempDir, "file1.txt"),
            Path.Combine(_tempDir, "file2.txt")
        };
        var result = IntegrityService.FindCommonRoot(paths);
        Assert.Equal(_tempDir, result);
    }

    [Fact]
    public void FindCommonRoot_DifferentSubdirs()
    {
        var paths = new[]
        {
            Path.Combine(_tempDir, "a", "file1.txt"),
            Path.Combine(_tempDir, "b", "file2.txt")
        };
        var result = IntegrityService.FindCommonRoot(paths);
        Assert.Equal(_tempDir, result);
    }

    [Fact]
    public void FindCommonRoot_TotallyDifferentRoots()
    {
        var paths = new[] { @"C:\path1\file.txt", @"D:\path2\file.txt" };
        var result = IntegrityService.FindCommonRoot(paths);
        Assert.Null(result);
    }

    // ── IsWindowsReservedDeviceName ─────────────────────────────────

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT0")]
    public void IsWindowsReservedDeviceName_Reserved_ReturnsTrue(string name)
    {
        Assert.True(FileSystemAdapter.IsWindowsReservedDeviceName(name));
    }

    [Theory]
    [InlineData("NUL.txt")]
    [InlineData("CON.log")]
    [InlineData("COM1.dat")]
    public void IsWindowsReservedDeviceName_WithExtension_StillReserved(string name)
    {
        Assert.True(FileSystemAdapter.IsWindowsReservedDeviceName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("file.txt")]
    [InlineData("CONSOLE")]
    [InlineData("COMX")]
    [InlineData("COM")]
    [InlineData("COMPETITION")]
    [InlineData("LPTX")]
    public void IsWindowsReservedDeviceName_NotReserved_ReturnsFalse(string name)
    {
        Assert.False(FileSystemAdapter.IsWindowsReservedDeviceName(name));
    }

    // ── NormalizePathNfc ────────────────────────────────────────────

    [Fact]
    public void NormalizePathNfc_NormalPath_ReturnsFullPath()
    {
        var path = Path.Combine(_tempDir, "hello.txt");
        var result = FileSystemAdapter.NormalizePathNfc(path);
        Assert.Equal(Path.GetFullPath(path), result);
    }

    [Fact]
    public void NormalizePathNfc_DecomposedUnicode_NormalizedToNfc()
    {
        // ä decomposed (a + combining umlaut) → ä composed in NFC
        var decomposed = Path.Combine(_tempDir, "a\u0308.txt");
        var result = FileSystemAdapter.NormalizePathNfc(decomposed);
        Assert.Contains("\u00E4", result); // ä composed
        Assert.DoesNotContain("\u0308", result); // combining char removed
    }

    [Fact]
    public void NormalizePathNfc_AlreadyComposed_Unchanged()
    {
        var composed = Path.Combine(_tempDir, "\u00E4.txt"); // ä
        var result = FileSystemAdapter.NormalizePathNfc(composed);
        Assert.Contains("\u00E4", result);
    }
}
