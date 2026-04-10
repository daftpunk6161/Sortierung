using Romulus.Api;
using Romulus.Infrastructure.Safety;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for DashboardDataBuilder.BuildBootstrap — pure DTO projection.
/// </summary>
public sealed class DashboardDataBuilderCoverageTests
{
    [Fact]
    public void BuildBootstrap_ProjectsAllFields()
    {
        var options = new HeadlessApiOptions
        {
            DashboardEnabled = true,
            AllowRemoteClients = true,
            PublicBaseUrl = "https://example.com"
        };
        var policy = new AllowedRootPathPolicy(["C:\\Roms", "D:\\Games"]);

        var result = DashboardDataBuilder.BuildBootstrap(options, policy, "1.0.0");

        Assert.Equal("1.0.0", result.Version);
        Assert.True(result.DashboardEnabled);
        Assert.True(result.AllowRemoteClients);
        Assert.True(result.AllowedRootsEnforced);
        Assert.Equal(2, result.AllowedRoots.Length);
        Assert.Equal("https://example.com", result.PublicBaseUrl);
    }

    [Fact]
    public void BuildBootstrap_EmptyRoots_NotEnforced()
    {
        var options = new HeadlessApiOptions();
        var policy = new AllowedRootPathPolicy(null);

        var result = DashboardDataBuilder.BuildBootstrap(options, policy, "2.0");

        Assert.False(result.AllowedRootsEnforced);
        Assert.Empty(result.AllowedRoots);
    }

    [Fact]
    public void BuildBootstrap_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DashboardDataBuilder.BuildBootstrap(null!, new AllowedRootPathPolicy(null), "1"));
    }

    [Fact]
    public void BuildBootstrap_NullPolicy_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DashboardDataBuilder.BuildBootstrap(new HeadlessApiOptions(), null!, "1"));
    }

    [Fact]
    public void BuildBootstrap_DefaultOptions_LocalSettings()
    {
        var options = new HeadlessApiOptions();
        var policy = new AllowedRootPathPolicy(null);

        var result = DashboardDataBuilder.BuildBootstrap(options, policy, "dev");

        Assert.True(result.DashboardEnabled);
        Assert.False(result.AllowRemoteClients);
        Assert.Null(result.PublicBaseUrl);
        Assert.Equal("/dashboard/", result.DashboardPath);
    }
}
