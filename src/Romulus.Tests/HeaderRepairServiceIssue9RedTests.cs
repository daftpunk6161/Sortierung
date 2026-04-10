using Romulus.Contracts.Ports;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Hashing;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-19): Infrastructure HeaderRepairService extracted from WPF security service.
/// </summary>
public sealed class HeaderRepairServiceIssue9RedTests : IDisposable
{
    private readonly string _tempDir;

    public HeaderRepairServiceIssue9RedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus", "HeaderRepairServiceIssue9", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void RepairNesHeader_DirtyHeader_CleansAndCreatesBackup_Issue9A19()
    {
        var path = Path.Combine(_tempDir, "dirty.nes");
        var header = new byte[16] { 0x4E, 0x45, 0x53, 0x1A, 2, 1, 0, 0, 0, 0, 0, 0, 0xAA, 0xBB, 0xCC, 0xDD };
        var body = new byte[32768];
        using (var fs = File.Create(path))
        {
            fs.Write(header);
            fs.Write(body);
        }

        IHeaderRepairService sut = new HeaderRepairService(new FileSystemAdapter());

        Assert.True(sut.RepairNesHeader(path));
        Assert.True(File.Exists(path + ".bak"));

        var buf = new byte[16];
        using var verify = File.OpenRead(path);
        verify.ReadExactly(buf, 0, 16);
        Assert.Equal(0, buf[12]);
        Assert.Equal(0, buf[13]);
        Assert.Equal(0, buf[14]);
        Assert.Equal(0, buf[15]);
    }

    [Fact]
    public void RemoveCopierHeader_WithHeader_RemovesAndCreatesBackup_Issue9A19()
    {
        var path = Path.Combine(_tempDir, "copier.sfc");
        var copierHeader = new byte[512];
        var romData = new byte[1024];
        romData[0] = 0xAB;
        using (var fs = File.Create(path))
        {
            fs.Write(copierHeader);
            fs.Write(romData);
        }

        IHeaderRepairService sut = new HeaderRepairService(new FileSystemAdapter());

        Assert.True(sut.RemoveCopierHeader(path));
        Assert.True(File.Exists(path + ".bak"));

        var newContent = File.ReadAllBytes(path);
        Assert.Equal(1024, newContent.Length);
        Assert.Equal(0xAB, newContent[0]);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best effort cleanup for test temp files.
        }
    }
}
