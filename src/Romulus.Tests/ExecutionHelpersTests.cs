using Romulus.Contracts;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

public sealed class ExecutionHelpersTests
{
    // =========================================================================
    //  GetDiscExtensions Tests
    // =========================================================================

    [Fact]
    public void GetDiscExtensions_ContainsCommonFormats()
    {
        var exts = ExecutionHelpers.GetDiscExtensions();
        Assert.Contains(".chd", exts);
        Assert.Contains(".iso", exts);
        Assert.Contains(".cue", exts);
        Assert.Contains(".rvz", exts);
        Assert.Contains(".pbp", exts);
    }

    [Fact]
    public void GetDiscExtensions_CaseInsensitive()
    {
        var exts = ExecutionHelpers.GetDiscExtensions();
        Assert.Contains(".CHD", exts);
        Assert.Contains(".Iso", exts);
    }

    // =========================================================================
    //  IsBlocklisted Tests
    // =========================================================================

    [Theory]
    [InlineData(@"D:\roms\_TRASH_REGION_DEDUPE\game.zip", true)]
    [InlineData(@"D:\roms\_QUARANTINE\bad.rom", true)]
    [InlineData(@"D:\roms\NES\game.zip", false)]
    [InlineData(@"D:\roms\SNES\Zelda.sfc", false)]
    public void IsBlocklisted_DefaultBlocklist(string path, bool expected)
        => Assert.Equal(expected, ExecutionHelpers.IsBlocklisted(path));

    [Fact]
    public void IsBlocklisted_CustomBlocklist()
    {
        Assert.True(ExecutionHelpers.IsBlocklisted(
            @"D:\roms\CUSTOM_BAN\file.zip",
            ["CUSTOM_BAN"]));
    }

    [Theory]
    [InlineData(@"D:\roms\_TRASH_JUNK\game.zip", true)]
    [InlineData(@"D:\roms\_TRASH_JUNK\subfolder\game.zip", true)]
    [InlineData(@"D:\roms\NES\_TRASH_JUNK\game.zip", true)]
    [InlineData(@"D:\roms\NES\game.zip", false)]
    public void IsBlocklisted_TrashJunkFolder(string path, bool expected)
        => Assert.Equal(expected, ExecutionHelpers.IsBlocklisted(path));

    [Fact]
    public void DefaultBlocklist_ContainsExpectedEntries()
    {
        var bl = ExecutionHelpers.DefaultBlocklist;
        Assert.Contains(RunConstants.WellKnownFolders.TrashRegionDedupe, bl);
        Assert.Contains(RunConstants.WellKnownFolders.FolderDupes, bl);
        Assert.Contains(RunConstants.WellKnownFolders.Quarantine, bl);
        Assert.Contains(RunConstants.WellKnownFolders.Ps3Dupes, bl);
        Assert.Contains(RunConstants.WellKnownFolders.Backup, bl);
        Assert.Contains(RunConstants.WellKnownFolders.TrashJunk, bl);
    }
}
