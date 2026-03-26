using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Hashing;
using Xunit;

namespace RomCleanup.Tests;

public sealed class FeatureServiceLargeFileTests : IDisposable
{
    private readonly string _tempDir;

    public FeatureServiceLargeFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RomCleanup_FeatureLarge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void RepairNesHeader_LargeFile_PatchesHeaderInPlace_WithoutLengthChange()
    {
        var path = Path.Combine(_tempDir, "large_dirty.nes");

        // Create a large sparse file and write only the 16-byte iNES header.
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
        {
            fs.SetLength(256L * 1024L * 1024L); // 256 MB
            var header = new byte[16]
            {
                0x4E, 0x45, 0x53, 0x1A, // NES magic
                0x02, 0x01, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0xAA, 0xBB, 0xCC, 0xDD  // dirty bytes 12-15
            };
            fs.Seek(0, SeekOrigin.Begin);
            fs.Write(header, 0, header.Length);
        }

        var beforeLength = new FileInfo(path).Length;

        IHeaderRepairService sut = new HeaderRepairService(new FileSystemAdapter());
        var repaired = sut.RepairNesHeader(path);

        Assert.True(repaired);
        Assert.Equal(beforeLength, new FileInfo(path).Length);

        using var verify = File.OpenRead(path);
        var afterHeader = new byte[16];
        var read = verify.Read(afterHeader, 0, afterHeader.Length);
        Assert.Equal(16, read);
        Assert.Equal(0x00, afterHeader[12]);
        Assert.Equal(0x00, afterHeader[13]);
        Assert.Equal(0x00, afterHeader[14]);
        Assert.Equal(0x00, afterHeader[15]);

        Assert.True(File.Exists(path + ".bak"));
    }
}