using Romulus.Infrastructure.Version;
using Xunit;

namespace Romulus.Tests;

public sealed class VersionHelperTests
{
    // =========================================================================
    //  ParseCsvList Tests
    // =========================================================================

    [Fact]
    public void ParseCsvList_Null_ReturnsEmpty()
        => Assert.Empty(VersionHelper.ParseCsvList(null));

    [Fact]
    public void ParseCsvList_Empty_ReturnsEmpty()
        => Assert.Empty(VersionHelper.ParseCsvList(""));

    [Fact]
    public void ParseCsvList_Whitespace_ReturnsEmpty()
        => Assert.Empty(VersionHelper.ParseCsvList("   "));

    [Fact]
    public void ParseCsvList_SingleItem_ReturnsTrimmed()
    {
        var result = VersionHelper.ParseCsvList("  EU  ");
        Assert.Single(result);
        Assert.Equal("EU", result[0]);
    }

    [Fact]
    public void ParseCsvList_MultipleItems_AllTrimmed()
    {
        var result = VersionHelper.ParseCsvList(" EU , US , JP ");
        Assert.Equal(3, result.Length);
        Assert.Equal("EU", result[0]);
        Assert.Equal("US", result[1]);
        Assert.Equal("JP", result[2]);
    }

    [Fact]
    public void ParseCsvList_SkipsEmptySegments()
    {
        var result = VersionHelper.ParseCsvList("EU,,US,,,JP");
        Assert.Equal(3, result.Length);
    }

    // =========================================================================
    //  NormalizeExtensionList Tests
    // =========================================================================

    [Fact]
    public void NormalizeExtensionList_Null_ReturnsEmpty()
        => Assert.Empty(VersionHelper.NormalizeExtensionList(null));

    [Fact]
    public void NormalizeExtensionList_Empty_ReturnsEmpty()
        => Assert.Empty(VersionHelper.NormalizeExtensionList(""));

    [Fact]
    public void NormalizeExtensionList_AddsDots()
    {
        var result = VersionHelper.NormalizeExtensionList("zip,iso,chd");
        Assert.Contains(".zip", result);
        Assert.Contains(".iso", result);
        Assert.Contains(".chd", result);
    }

    [Fact]
    public void NormalizeExtensionList_PreservesDots()
    {
        var result = VersionHelper.NormalizeExtensionList(".zip,.iso");
        Assert.Contains(".zip", result);
        Assert.Contains(".iso", result);
    }

    [Fact]
    public void NormalizeExtensionList_Deduplicates()
    {
        var result = VersionHelper.NormalizeExtensionList("zip,ZIP,Zip,.zip");
        Assert.Single(result);
        Assert.Equal(".zip", result[0]);
    }

    [Fact]
    public void NormalizeExtensionList_LowerCased()
    {
        var result = VersionHelper.NormalizeExtensionList("ZIP,ISO");
        Assert.All(result, ext => Assert.Equal(ext, ext.ToLowerInvariant()));
    }

    [Fact]
    public void NormalizeExtensionList_AcceptsSemicolon()
    {
        var result = VersionHelper.NormalizeExtensionList("zip;iso;chd");
        Assert.Equal(3, result.Length);
    }

    [Fact]
    public void NormalizeExtensionList_AcceptsSpaces()
    {
        var result = VersionHelper.NormalizeExtensionList("zip iso chd");
        Assert.Equal(3, result.Length);
    }

    // =========================================================================
    //  CurrentVersion Test
    // =========================================================================

    [Fact]
    public void CurrentVersion_IsSet()
        => Assert.False(string.IsNullOrEmpty(VersionHelper.CurrentVersion));
}
