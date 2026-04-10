using Romulus.Contracts.Models;
using Romulus.Infrastructure.Dat;
using Xunit;

namespace Romulus.Tests;

public sealed class FamilyDatStrategyTests
{
    [Fact]
    public void ResolvePolicy_Cartridge_HeaderlessSha1_UsesHeaderlessHash()
    {
        var resolver = new FamilyDatStrategyResolver();

        var policy = resolver.ResolvePolicy(PlatformFamily.NoIntroCartridge, ".nes", "headerless-sha1");

        Assert.True(policy.UseHeaderlessHash);
        Assert.True(policy.UseContainerHash);
        Assert.False(policy.AllowNameOnlyDatMatch);
    }

    [Fact]
    public void ResolvePolicy_Disc_Chd_AllowsNameOnlyFallback()
    {
        var resolver = new FamilyDatStrategyResolver();

        var policy = resolver.ResolvePolicy(PlatformFamily.RedumpDisc, ".chd", "track-sha1");

        Assert.False(policy.UseHeaderlessHash);
        Assert.True(policy.UseContainerHash);
        Assert.True(policy.AllowNameOnlyDatMatch);
    }

    [Fact]
    public void ResolvePolicy_Hybrid_DisablesNameOnly()
    {
        var resolver = new FamilyDatStrategyResolver();

        var policy = resolver.ResolvePolicy(PlatformFamily.Hybrid, ".rvz", "container-sha1");

        Assert.False(policy.UseHeaderlessHash);
        Assert.True(policy.UseContainerHash);
        Assert.False(policy.AllowNameOnlyDatMatch);
    }

    [Fact]
    public void ResolvePolicy_Arcade_RequiresStrictNameWhenNameFallbackAllowed()
    {
        var resolver = new FamilyDatStrategyResolver();

        var policy = resolver.ResolvePolicy(PlatformFamily.Arcade, ".zip", "set-archive-sha1");

        Assert.True(policy.PreferArchiveInnerHash);
        Assert.True(policy.AllowNameOnlyDatMatch);
        Assert.True(policy.RequireStrictNameForNameOnly);
    }

    [Fact]
    public void ResolvePolicy_FolderSignature_DisablesNameOnlyFallback()
    {
        var resolver = new FamilyDatStrategyResolver();

        var policy = resolver.ResolvePolicy(PlatformFamily.FolderBased, ".pkg", "folder-signature");

        Assert.False(policy.AllowNameOnlyDatMatch);
        Assert.False(policy.UseHeaderlessHash);
    }

    [Fact]
    public void ResolvePolicy_UnknownFamily_UsesGenericDiscLikeFallback()
    {
        var resolver = new FamilyDatStrategyResolver();

        var policy = resolver.ResolvePolicy(PlatformFamily.Unknown, ".iso", null);

        Assert.True(policy.UseContainerHash);
        Assert.True(policy.AllowNameOnlyDatMatch);
    }
}
