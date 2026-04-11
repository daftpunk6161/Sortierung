using System.IO.Compression;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests that classification detectors behave correctly when I/O is injected
/// with deterministic test doubles.
/// </summary>
public sealed class ClassificationIoMockingTests
{
    // ── CartridgeHeaderDetector ──────────────────────────────────────

    [Fact]
    public void CartridgeDetect_FileNotFound_ReturnsNull()
    {
        var io = CreateIo(fileExists: _ => false);
        var detector = new CartridgeHeaderDetector(cacheSize: 4, classificationIo: io);

        Assert.Null(detector.Detect(@"C:\fake\rom.nes"));
    }

    [Fact]
    public void CartridgeDetect_OpenReadThrowsIOException_ReturnsNull()
    {
        var io = CreateIo(
            fileExists: _ => true,
            openRead: _ => throw new IOException("disk error"));

        var detector = new CartridgeHeaderDetector(cacheSize: 4, classificationIo: io);

        Assert.Null(detector.Detect(@"C:\fake\rom.nes"));
    }

    [Fact]
    public void CartridgeDetect_OpenReadThrowsUnauthorized_ReturnsNull()
    {
        var io = CreateIo(
            fileExists: _ => true,
            openRead: _ => throw new UnauthorizedAccessException("no access"));

        var detector = new CartridgeHeaderDetector(cacheSize: 4, classificationIo: io);

        Assert.Null(detector.Detect(@"C:\fake\rom.nes"));
    }

    [Fact]
    public void CartridgeDetect_EmptyStream_ReturnsNull()
    {
        var io = CreateIo(
            fileExists: _ => true,
            openRead: _ => new MemoryStream(Array.Empty<byte>()));

        var detector = new CartridgeHeaderDetector(cacheSize: 4, classificationIo: io);

        Assert.Null(detector.Detect(@"C:\fake\rom.nes"));
    }

    [Fact]
    public void CartridgeDetect_ValidNesHeader_ReturnsNES()
    {
        var header = new byte[512];
        header[0] = 0x4E;
        header[1] = 0x45;
        header[2] = 0x53;
        header[3] = 0x1A;

        var io = CreateIo(
            fileExists: _ => true,
            openRead: _ => new MemoryStream(header));

        var detector = new CartridgeHeaderDetector(cacheSize: 4, classificationIo: io);

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

        var io = CreateIo(
            fileExists: _ => true,
            openRead: _ => new MemoryStream(header));

        var detector = new CartridgeHeaderDetector(cacheSize: 4, classificationIo: io);

        Assert.Equal("N64", detector.Detect(@"C:\fake\rom.n64"));
    }

    [Fact]
    public void CartridgeDetect_TooFewBytes_ReturnsNull()
    {
        var io = CreateIo(
            fileExists: _ => true,
            openRead: _ => new MemoryStream(new byte[] { 0x00, 0x00, 0x00 }));

        var detector = new CartridgeHeaderDetector(cacheSize: 4, classificationIo: io);

        Assert.Null(detector.Detect(@"C:\fake\rom.bin"));
    }

    // ── DiscHeaderDetector ──────────────────────────────────────────

    [Fact]
    public void DiscDetect_FileNotFound_ReturnsNull()
    {
        var io = CreateIo(fileExists: _ => false);
        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4, classificationIo: io);

        Assert.Null(detector.DetectFromDiscImage(@"C:\fake\game.iso"));
    }

    [Fact]
    public void DiscDetect_OpenReadThrowsIOException_ReturnsNull()
    {
        var io = CreateIo(
            fileExists: _ => true,
            openRead: _ => throw new IOException("disk error"));

        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4, classificationIo: io);

        Assert.Null(detector.DetectFromDiscImage(@"C:\fake\game.iso"));
    }

    [Fact]
    public void DiscDetect_EmptyStream_ReturnsNull()
    {
        var io = CreateIo(
            fileExists: _ => true,
            openRead: _ => new MemoryStream(Array.Empty<byte>()));

        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4, classificationIo: io);

        Assert.Null(detector.DetectFromDiscImage(@"C:\fake\game.iso"));
    }

    [Fact]
    public void DiscDetectChd_FileNotFound_ReturnsNull()
    {
        var io = CreateIo(fileExists: _ => false);
        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4, classificationIo: io);

        Assert.Null(detector.DetectFromChd(@"C:\fake\game.chd"));
    }

    [Fact]
    public void DiscDetectChd_OpenReadThrowsIOException_ReturnsNull()
    {
        var io = CreateIo(
            fileExists: _ => true,
            openRead: _ => throw new IOException("disk error"));

        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4, classificationIo: io);

        Assert.Null(detector.DetectFromChd(@"C:\fake\game.chd"));
    }

    [Fact]
    public void DiscDetectBatch_GetAttributesThrows_SkipsFile()
    {
        var io = CreateIo(getAttributes: _ => throw new IOException("no attrs"));

        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4, classificationIo: io);
        var paths = new[] { @"C:\fake\game.iso" };

        var results = detector.DetectBatch(paths);

        Assert.NotNull(results);
        Assert.True(results.ContainsKey(paths[0]));
        Assert.Null(results[paths[0]]);
    }

    [Fact]
    public void DiscDetectBatch_ReparsePoint_SkipsFile()
    {
        var io = CreateIo(
            fileExists: _ => true,
            getAttributes: _ => FileAttributes.ReparsePoint);

        var detector = new DiscHeaderDetector(isoCacheSize: 4, chdCacheSize: 4, classificationIo: io);
        var paths = new[] { @"C:\fake\symlink.iso" };

        var results = detector.DetectBatch(paths);

        Assert.NotNull(results);
        Assert.Null(results[paths[0]]);
    }

    // ── ConsoleDetector ─────────────────────────────────────────────

    private static ConsoleDetector CreateConsoleDetector(IClassificationIo io)
    {
        var consoles = new[]
        {
            new ConsoleInfo("NES", "Nintendo Entertainment System", false,
                new[] { ".nes" }, Array.Empty<string>(),
                new[] { "nes", "famicom" }),
        };
        return new ConsoleDetector(consoles, classificationIo: io);
    }

    [Fact]
    public void ConsoleDetector_ZeroLengthFile_DetectedAsInvalid()
    {
        var io = CreateIo(
            fileExists: _ => true,
            fileLength: _ => 0,
            openZipRead: _ => throw new InvalidDataException("not a zip"));

        var detector = CreateConsoleDetector(io);
        var result = detector.DetectByArchiveContent(@"C:\fake\game.zip", ".zip");

        Assert.Null(result);
    }

    [Fact]
    public void ConsoleDetector_ZipOpenThrowsInvalidData_ReturnsNull()
    {
        var io = CreateIo(
            fileExists: _ => true,
            fileLength: _ => 1024,
            openZipRead: _ => throw new InvalidDataException("not a zip"));

        var detector = CreateConsoleDetector(io);

        var result = detector.DetectByArchiveContent(@"C:\fake\game.zip", @"C:\fake");

        Assert.Null(result);
    }

    [Fact]
    public void ConsoleDetector_ZipOpenThrowsIOException_ReturnsNull()
    {
        var io = CreateIo(
            fileExists: _ => true,
            fileLength: _ => 1024,
            openZipRead: _ => throw new IOException("disk error"));

        var detector = CreateConsoleDetector(io);

        var result = detector.DetectByArchiveContent(@"C:\fake\game.zip", @"C:\fake");

        Assert.Null(result);
    }

    private static TestClassificationIo CreateIo(
        Func<string, bool>? fileExists = null,
        Func<string, Stream>? openRead = null,
        Func<string, long>? fileLength = null,
        Func<string, FileAttributes>? getAttributes = null,
        Func<string, ZipArchive>? openZipRead = null)
    {
        return new TestClassificationIo
        {
            FileExistsFunc = fileExists ?? (_ => false),
            OpenReadFunc = openRead ?? (_ => throw new InvalidOperationException("openRead not configured")),
            FileLengthFunc = fileLength ?? (_ => 0),
            GetAttributesFunc = getAttributes ?? (_ => FileAttributes.Normal),
            OpenZipReadFunc = openZipRead ?? (_ => throw new InvalidOperationException("openZipRead not configured"))
        };
    }
}
