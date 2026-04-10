using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.UI.Wpf.Services;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests that HeaderSecurityService delegates directly to Core.HeaderAnalyzer
/// (not via FeatureService indirection) and to IHeaderRepairService.
/// </summary>
public sealed class HeaderSecurityServiceDelegationTests
{
    private sealed class FakeHeaderRepairService : IHeaderRepairService
    {
        public int RepairCalls { get; private set; }
        public int RemoveCalls { get; private set; }
        public bool RepairNesHeader(string path) { RepairCalls++; return true; }
        public bool RemoveCopierHeader(string path) { RemoveCalls++; return true; }
    }

    [Fact]
    public void AnalyzeHeader_ReturnsNull_ForNonexistentFile()
    {
        var fake = new FakeHeaderRepairService();
        var service = new HeaderSecurityService(fake);

        var result = service.AnalyzeHeader(@"C:\nonexistent\file.nes");

        Assert.Null(result);
    }

    [Fact]
    public void AnalyzeHeader_DetectsNesHeader_DirectlyViaCore()
    {
        var fake = new FakeHeaderRepairService();
        var service = new HeaderSecurityService(fake);

        var tempFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.nes");
        try
        {
            // Write a valid iNES header
            var header = new byte[16];
            header[0] = 0x4E; // N
            header[1] = 0x45; // E
            header[2] = 0x53; // S
            header[3] = 0x1A; // EOF
            header[4] = 2;    // PRG ROM banks
            header[5] = 1;    // CHR ROM banks
            var data = new byte[16 + 2 * 16384 + 1 * 8192];
            Array.Copy(header, data, 16);
            File.WriteAllBytes(tempFile, data);

            var result = service.AnalyzeHeader(tempFile);

            Assert.NotNull(result);
            Assert.Equal("NES", result.Platform);
            Assert.Equal("iNES", result.Format);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void RepairNesHeader_DelegatesToIHeaderRepairService()
    {
        var fake = new FakeHeaderRepairService();
        var service = new HeaderSecurityService(fake);

        var result = service.RepairNesHeader("test.nes");

        Assert.True(result);
        Assert.Equal(1, fake.RepairCalls);
    }

    [Fact]
    public void RemoveCopierHeader_DelegatesToIHeaderRepairService()
    {
        var fake = new FakeHeaderRepairService();
        var service = new HeaderSecurityService(fake);

        var result = service.RemoveCopierHeader("test.sfc");

        Assert.True(result);
        Assert.Equal(1, fake.RemoveCalls);
    }

    [Fact]
    public void Constructor_ThrowsOnNullRepairService()
    {
        Assert.Throws<ArgumentNullException>(() => new HeaderSecurityService(null!));
    }
}
