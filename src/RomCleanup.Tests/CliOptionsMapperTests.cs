using RomCleanup.CLI;
using RomCleanup.Contracts.Models;
using Xunit;

namespace RomCleanup.Tests;

public sealed class CliOptionsMapperTests
{
    [Fact]
    public void Map_EnableDatAudit_IsProjectedToRunOptions_Issue9()
    {
        var settings = new RomCleanupSettings();
        var cli = new CliRunOptions
        {
            Roots = new[] { "C:\\temp" },
            EnableDatAudit = true
        };

        var (runOptions, errors) = CliOptionsMapper.Map(cli, settings);

        Assert.NotNull(runOptions);
        Assert.Null(errors);
        Assert.True(runOptions!.EnableDatAudit);
    }

    [Fact]
    public void Map_ConvertFlags_AreProjectedToRunOptions()
    {
        var settings = new RomCleanupSettings();
        var cli = new CliRunOptions
        {
            Roots = new[] { "C:\\temp" },
            ConvertFormat = true,
            ConvertOnly = true,
            ConflictPolicy = "Skip"
        };

        var (runOptions, errors) = CliOptionsMapper.Map(cli, settings);

        Assert.NotNull(runOptions);
        Assert.Null(errors);
        Assert.Equal("auto", runOptions!.ConvertFormat);
        Assert.True(runOptions.ConvertOnly);
        Assert.Equal("Skip", runOptions.ConflictPolicy);
    }

    [Fact]
    public void Map_WhenExtensionsNotExplicit_MergesSettingsExtensions()
    {
        var settings = new RomCleanupSettings
        {
            General =
            {
                Extensions = ".zip,iso,chd"
            }
        };

        var cli = new CliRunOptions
        {
            Roots = new[] { "C:\\temp" },
            ExtensionsExplicit = false
        };

        var (runOptions, errors) = CliOptionsMapper.Map(cli, settings);

        Assert.NotNull(runOptions);
        Assert.Null(errors);
        Assert.Contains(".zip", runOptions!.Extensions);
        Assert.Contains(".iso", runOptions.Extensions);
        Assert.Contains(".chd", runOptions.Extensions);
    }

    [Fact]
    public void Map_WhenExtensionsExplicit_DoesNotMergeSettingsExtensions()
    {
        var settings = new RomCleanupSettings
        {
            General =
            {
                Extensions = ".zip,.chd"
            }
        };

        var cli = new CliRunOptions
        {
            Roots = new[] { "C:\\temp" },
            ExtensionsExplicit = true,
            Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".rvz" }
        };

        var (runOptions, errors) = CliOptionsMapper.Map(cli, settings);

        Assert.NotNull(runOptions);
        Assert.Null(errors);
        Assert.Single(runOptions!.Extensions);
        Assert.Contains(".rvz", runOptions.Extensions);
    }
}
