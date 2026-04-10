using Romulus.Contracts.Ports;
using Romulus.UI.Wpf.Services;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-20): HeaderSecurityService should delegate repair methods to IHeaderRepairService.
/// </summary>
public sealed class HeaderSecurityServiceIssue9RedTests
{
    [Fact]
    public void RepairNesHeader_ShouldDelegateToPort_Issue9A20()
    {
        var fake = new FakeHeaderRepairService(repairResult: true, removeResult: false);
        IHeaderService sut = new HeaderSecurityService(fake);

        var result = sut.RepairNesHeader("C:/roms/game.nes");

        Assert.True(result);
        Assert.Equal(1, fake.RepairCalls);
    }

    [Fact]
    public void RemoveCopierHeader_ShouldDelegateToPort_Issue9A20()
    {
        var fake = new FakeHeaderRepairService(repairResult: false, removeResult: true);
        IHeaderService sut = new HeaderSecurityService(fake);

        var result = sut.RemoveCopierHeader("C:/roms/game.sfc");

        Assert.True(result);
        Assert.Equal(1, fake.RemoveCalls);
    }

    private sealed class FakeHeaderRepairService : IHeaderRepairService
    {
        private readonly bool _repairResult;
        private readonly bool _removeResult;

        public FakeHeaderRepairService(bool repairResult, bool removeResult)
        {
            _repairResult = repairResult;
            _removeResult = removeResult;
        }

        public int RepairCalls { get; private set; }
        public int RemoveCalls { get; private set; }

        public bool RepairNesHeader(string path)
        {
            RepairCalls++;
            return _repairResult;
        }

        public bool RemoveCopierHeader(string path)
        {
            RemoveCalls++;
            return _removeResult;
        }
    }
}
