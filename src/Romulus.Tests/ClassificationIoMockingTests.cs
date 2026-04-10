using System.IO.Compression;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests that classification detectors behave correctly when ClassificationIo
/// delegates are replaced with mocks — ensuring graceful failure on I/O issues.
/// </summary>
public sealed class ClassificationIoMockingTests : IDisposable
{
    public ClassificationIoMockingTests()
    {
        // Start clean each test
        ClassificationIo.ResetDefaults();
    }

    public void Dispose()
    {
        ClassificationIo.ResetDefaults();
    }

    // ── CartridgeHeaderDetector ──────────────────────────────────────

    [Fact]
    public void CartridgeDetect_FileNotFound_ReturnsNull()
    {
        ClassificationIo.Configure(fileExists: _ => false);
        var detector = new CartridgeHeaderDetector(cacheSize: 4);

        Assert.Null(detector.Detect(@"C:\fake\rom.nes"));
    }

    [Fact]
    public void CartridgeDetect_OpenReadThrowsIOException_ReturnsNull()
    {
        ClassificationIo.Configure(
            fileExists: _ => true,
            openRead: _ => throw new IOException("disk error"));

        var detector = new CartridgeHeaderDetector(cacheSize: 4);

        Assert.Null(detector.Detect(@"C:\fake\rom.nes"));
    }

    [Fact]
    public void CartridgeDetect_OpenReadThrowsUnauthorized_ReturnsNull()
    {
        ClassificationIo.Configure(
            fileExists: _ => true,
            openRead: _ => throw new UnauthorizedAccessException("no access"));

        var detector = new CartridgeHeaderDetector(cacheSize: 4);

        Assert.Null(detector.Detect(@"C:\fake\rom.nes"));
    }

    [Fact]
    public void CartridgeDetect_EmptyStream_ReturnsNull()
    {
        ClassificationIo.Configure(
            fileExists: _ => true,
            openRead: _ => new MemoryStream(Array.Empty<byte>()));

        var detector = new CartridgeHeaderDetector(cacheSize: 4);

        Assert.Null(detector.Detect(@"C:\fake\rom.nes"));
    }

    [Fact]
    public void CartridgeDetect_ValidNesHeader_ReturnsNES()
    {
        // iNES magic: 4E 45 53 1A followed by enough bytes
        var header = new byte[512];
        header[0] = 0x4E; // N
        header[1] = 0x45; // E
        header[2] = 0x53; // S
        header[3] = 0x1A; // ^Z

        ClassificationIo.Configure(
            fileExists: _ => true,
            openRead: _ => new MemoryStream(header));

        var detector = new CartridgeHeaderDetector(cacheSize: 4);

        Assert.Equal("NES", detector.Detect(@"C:\fake\rom.nes"));
    }

    [Fact]
    public void CartridgeDetect_ValidN64Header_ReturnsN64()
    {
        var header = new byte[512];
        header[0] = 0x80;
        header[1] = 0x37;
        header[2] = 0x12;
        header[3] = 0x40;

        ClassificationIo.Configure(
            fileExists: _ => true,
            openRead: _ => new MemoryStream(header));

        var detector = new CartridgeHeaderDetector(cacheSize: 4);

        Assert.Equal("N64", detector.Detect(@"C:\fake\rom.n64"));
    }

    [Fact]
    public void CartridgeDetect_TooFewBytes_ReturnsNull()
    {
        // Only 3 bytes — below the 4-byte minimum
        ClassificationIo.Configure(
            fileExists: _ => true,
            openRead: _ => new MemoryStream(new byte[] { 0x00, 0x00, 0x00 }));

        var detector = new CartridgeHeaderDetector(cacheSize: 4);

        Assert.Null(detector.Detect(@"C:\fake\rom.bin"));
    }

    // ── DiscHeaderDetector ──────────────────────────────────────────

    [Fact]
    public void DiscDetect_FileNotFound_ReturnsNull()
    {
        ClassificationIo.Configure(fileExists: _ => false);
        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4);

        Assert.Null(detector.DetectFromDiscImage(@"C:\fake\game.iso"));
    }

    [Fact]
    public void DiscDetect_OpenReadThrowsIOException_ReturnsNull()
    {
        ClassificationIo.Configure(
            fileExists: _ => true,
            openRead: _ => throw new IOException("disk error"));

        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4);

        Assert.Null(detector.DetectFromDiscImage(@"C:\fake\game.iso"));
    }

    [Fact]
    public void DiscDetect_EmptyStream_ReturnsNull()
    {
        ClassificationIo.Configure(
            fileExists: _ => true,
            openRead: _ => new MemoryStream(Array.Empty<byte>()));

        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4);

        Assert.Null(detector.DetectFromDiscImage(@"C:\fake\game.iso"));
    }

    [Fact]
    public void DiscDetectChd_FileNotFound_ReturnsNull()
    {
        ClassificationIo.Configure(fileExists: _ => false);
        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4);

        Assert.Null(detector.DetectFromChd(@"C:\fake\game.chd"));
    }

    [Fact]
    public void DiscDetectChd_OpenReadThrowsIOException_ReturnsNull()
    {
        ClassificationIo.Configure(
            fileExists: _ => true,
            openRead: _ => throw new IOException("disk error"));

        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4);

        Assert.Null(detector.DetectFromChd(@"C:\fake\game.chd"));
    }

    [Fact]
    public void DiscDetectBatch_GetAttributesThrows_SkipsFile()
    {
        ClassificationIo.Configure(
            getAttributes: _ => throw new IOException("no attrs"));

        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4);
        var paths = new[] { @"C:\fake\game.iso" };

        var results = detector.DetectBatch(paths);

        Assert.NotNull(results);
        Assert.True(results.ContainsKey(paths[0]));
        Assert.Null(results[paths[0]]);
    }

    [Fact]
    public void DiscDetectBatch_ReparsePoint_SkipsFile()
    {
        ClassificationIo.Configure(
            fileExists: _ => true,
            getAttributes: _ => FileAttributes.ReparsePoint);

        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4);
        var paths = new[] { @"C:\fake\symlink.iso" };

        var results = detector.DetectBatch(paths);

        Assert.NotNull(results);
        Assert.Null(results[paths[0]]);
    }

    // ── ConsoleDetector ─────────────────────────────────────────────

    private static ConsoleDetector CreateConsoleDetector()
    {
        var consoles = new[]
        {
            new ConsoleInfo("NES", "Nintendo Entertainment System", false,
                new[] { ".nes" }, Array.Empty<string>(),
                new[] { "nes", "famicom" }),
        };
        return new ConsoleDetector(consoles);
    }

    [Fact]
    public void ConsoleDetector_ZeroLengthFile_DetectedAsInvalid()
    {
        // IsClearlyInvalidFile checks FileLength == 0 → returns true → DetectByArchiveContent returns null
        ClassificationIo.Configure(
            fileExists: _ => true,
            fileLength: _ => 0);

        var detector = CreateConsoleDetector();
        // .zip triggers DetectByZipContent path which calls IsClearlyInvalidFile first
        var result = detector.DetectByArchiveContent(@"C:\fake\game.zip", ".zip");

        // Zero-length file should not produce a match
        Assert.Null(result);
    }

    [Fact]
    public void ConsoleDetector_ZipOpenThrowsInvalidData_ReturnsNull()
    {
        ClassificationIo.Configure(
            fileExists: _ => true,
            fileLength: _ => 1024,
            openZipRead: _ => throw new InvalidDataException("not a zip"));

        var detector = CreateConsoleDetector();

        // DetectByArchiveContent with .zip extension triggers OpenZipRead
        var result = detector.DetectByArchiveContent(@"C:\fake\game.zip", @"C:\fake");

        Assert.Null(result);
    }

    [Fact]
    public void ConsoleDetector_ZipOpenThrowsIOException_ReturnsNull()
    {
        ClassificationIo.Configure(
            fileExists: _ => true,
            fileLength: _ => 1024,
            openZipRead: _ => throw new IOException("disk error"));

        var detector = CreateConsoleDetector();

        var result = detector.DetectByArchiveContent(@"C:\fake\game.zip", @"C:\fake");

        Assert.Null(result);
    }

    // ── ClassificationIo.Configure and ResetDefaults ────────────────

    [Fact]
    public void Configure_NullParams_KeepExistingDelegates()
    {
        // Setting all params null should not crash or change behavior
        ClassificationIo.Configure(
            fileExists: null,
            openRead: null,
            fileLength: null,
            getAttributes: null,
            openZipRead: null);

        // After Configure(all null), default delegates should still work for non-existent path
        Assert.False(ClassificationIo.FileExists(@"C:\definitely\not\a\real\path\abc123.txt"));
    }

    [Fact]
    public void ResetDefaults_RestoresOriginalBehavior()
    {
        ClassificationIo.Configure(fileExists: _ => true);
        Assert.True(ClassificationIo.FileExists("nonexistent"));

        ClassificationIo.ResetDefaults();
        Assert.False(ClassificationIo.FileExists(@"C:\definitely\not\a\real\path\abc123.txt"));
    }
}
