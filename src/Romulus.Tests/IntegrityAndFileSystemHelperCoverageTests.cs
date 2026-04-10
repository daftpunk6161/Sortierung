using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for IntegrityService pure helpers (FindCommonRoot,
/// ResolvePatchFormat, ReadUInt16/24, EnsureOutputLength, DetectPatchFormat)
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

    // ── DetectPatchFormat ───────────────────────────────────────────

    [Fact]
    public void DetectPatchFormat_IpsMagic()
    {
        var path = Path.Combine(_tempDir, "test.ips");
        File.WriteAllBytes(path, "PATCH"u8.ToArray());
        Assert.Equal("IPS", IntegrityService.DetectPatchFormat(path));
    }

    [Fact]
    public void DetectPatchFormat_BpsMagic()
    {
        var path = Path.Combine(_tempDir, "test.bps");
        File.WriteAllBytes(path, [0x42, 0x50, 0x53, 0x31, 0x00]); // BPS1\0
        Assert.Equal("BPS", IntegrityService.DetectPatchFormat(path));
    }

    [Fact]
    public void DetectPatchFormat_UpsMagic()
    {
        var path = Path.Combine(_tempDir, "test.ups");
        File.WriteAllBytes(path, [0x55, 0x50, 0x53, 0x31, 0x00]); // UPS1\0
        Assert.Equal("UPS", IntegrityService.DetectPatchFormat(path));
    }

    [Fact]
    public void DetectPatchFormat_UnknownMagic_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "test.bin");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0x04]);
        Assert.Null(IntegrityService.DetectPatchFormat(path));
    }

    [Fact]
    public void DetectPatchFormat_TooShort_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "short.bin");
        File.WriteAllBytes(path, [0x00, 0x01]);
        Assert.Null(IntegrityService.DetectPatchFormat(path));
    }

    [Fact]
    public void DetectPatchFormat_FileNotFound_ReturnsNull()
    {
        Assert.Null(IntegrityService.DetectPatchFormat(Path.Combine(_tempDir, "nonexistent.ips")));
    }

    // ── ResolvePatchFormat ──────────────────────────────────────────

    [Fact]
    public void ResolvePatchFormat_IpsByMagic()
    {
        var path = Path.Combine(_tempDir, "test.dat"); // non-standard extension
        File.WriteAllBytes(path, "PATCH"u8.ToArray());
        Assert.Equal("IPS", IntegrityService.ResolvePatchFormat(path));
    }

    [Theory]
    [InlineData(".ips", "IPS")]
    [InlineData(".bps", "BPS")]
    [InlineData(".ups", "UPS")]
    [InlineData(".xdelta", "XDELTA")]
    [InlineData(".xdelta3", "XDELTA")]
    [InlineData(".vcdiff", "XDELTA")]
    public void ResolvePatchFormat_ByExtension(string ext, string expected)
    {
        var path = Path.Combine(_tempDir, $"test{ext}");
        File.WriteAllBytes(path, [0x00, 0x00, 0x00, 0x00, 0x00]); // unknown magic
        Assert.Equal(expected, IntegrityService.ResolvePatchFormat(path));
    }

    [Fact]
    public void ResolvePatchFormat_UnsupportedExtension_Throws()
    {
        var path = Path.Combine(_tempDir, "test.unknown");
        File.WriteAllBytes(path, [0x00, 0x00, 0x00, 0x00, 0x00]);
        Assert.Throws<InvalidOperationException>(() => IntegrityService.ResolvePatchFormat(path));
    }

    // ── ReadUInt16BigEndian ──────────────────────────────────────────

    [Fact]
    public void ReadUInt16BigEndian_CorrectValue()
    {
        using var ms = new MemoryStream(new byte[] { 0x01, 0x00 }); // 256
        using var reader = new BinaryReader(ms);
        Assert.Equal(256, IntegrityService.ReadUInt16BigEndian(reader));
    }

    [Fact]
    public void ReadUInt16BigEndian_MaxValue()
    {
        using var ms = new MemoryStream(new byte[] { 0xFF, 0xFF }); // 65535
        using var reader = new BinaryReader(ms);
        Assert.Equal(65535, IntegrityService.ReadUInt16BigEndian(reader));
    }

    [Fact]
    public void ReadUInt16BigEndian_Zero()
    {
        using var ms = new MemoryStream(new byte[] { 0x00, 0x00 });
        using var reader = new BinaryReader(ms);
        Assert.Equal(0, IntegrityService.ReadUInt16BigEndian(reader));
    }

    // ── ReadUInt24BigEndian ──────────────────────────────────────────

    [Fact]
    public void ReadUInt24BigEndian_CorrectValue()
    {
        using var ms = new MemoryStream(new byte[] { 0x01, 0x00, 0x00 }); // 65536
        using var reader = new BinaryReader(ms);
        Assert.Equal(65536, IntegrityService.ReadUInt24BigEndian(reader));
    }

    [Fact]
    public void ReadUInt24BigEndian_MaxValue()
    {
        using var ms = new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF }); // 16777215
        using var reader = new BinaryReader(ms);
        Assert.Equal(16777215, IntegrityService.ReadUInt24BigEndian(reader));
    }

    [Fact]
    public void ReadUInt24BigEndian_Zero()
    {
        using var ms = new MemoryStream(new byte[] { 0x00, 0x00, 0x00 });
        using var reader = new BinaryReader(ms);
        Assert.Equal(0, IntegrityService.ReadUInt24BigEndian(reader));
    }

    // ── EnsureOutputLength ──────────────────────────────────────────

    [Fact]
    public void EnsureOutputLength_AlreadyLongEnough_NoChange()
    {
        var output = new List<byte> { 1, 2, 3, 4, 5 };
        IntegrityService.EnsureOutputLength(output, 3);
        Assert.Equal(5, output.Count);
    }

    [Fact]
    public void EnsureOutputLength_ShorterThanTarget_ExtendsWithZeros()
    {
        var output = new List<byte> { 1, 2 };
        IntegrityService.EnsureOutputLength(output, 5);
        Assert.Equal(5, output.Count);
        Assert.Equal(1, output[0]);
        Assert.Equal(2, output[1]);
        Assert.Equal(0, output[2]);
        Assert.Equal(0, output[3]);
        Assert.Equal(0, output[4]);
    }

    [Fact]
    public void EnsureOutputLength_Empty_ExtendsToTarget()
    {
        var output = new List<byte>();
        IntegrityService.EnsureOutputLength(output, 3);
        Assert.Equal(3, output.Count);
        Assert.All(output, b => Assert.Equal(0, b));
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
