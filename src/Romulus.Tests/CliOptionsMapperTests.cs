using Romulus.CLI;
using Romulus.Contracts.Models;
using Xunit;

namespace Romulus.Tests;

public sealed class CliOptionsMapperTests
{
    [Fact]
    public void Map_EnableDatAudit_IsProjectedToRunOptions_Issue9()
    {
        var settings = new RomulusSettings();
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
        var settings = new RomulusSettings();
        var cli = new CliRunOptions
        {
            Roots = new[] { "C:\\temp" },
            ConvertFormat = "auto",
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
        var settings = new RomulusSettings
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
    public void Map_ApproveReviews_IsProjectedToRunOptions()
    {
        var settings = new RomulusSettings();
        var cli = new CliRunOptions
        {
            Roots = new[] { "C:\\temp" },
            ApproveReviews = true
        };

        var (runOptions, errors) = CliOptionsMapper.Map(cli, settings);

        Assert.NotNull(runOptions);
        Assert.Null(errors);
        Assert.True(runOptions!.ApproveReviews);
    }

    [Fact]
    public void Map_WhenExtensionsExplicit_DoesNotMergeSettingsExtensions()
    {
        var settings = new RomulusSettings
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

    [Fact]
    public void Map_DefaultMode_IsDryRun()
    {
        var settings = new RomulusSettings();
        var cli = new CliRunOptions { Roots = new[] { "C:\\temp" } };

        var (runOptions, _) = CliOptionsMapper.Map(cli, settings);

        Assert.Equal("DryRun", runOptions!.Mode);
    }

    [Fact]
    public void Map_ConvertFormatMissing_ProducesNullFormat()
    {
        var settings = new RomulusSettings();
        var cli = new CliRunOptions
        {
            Roots = new[] { "C:\\temp" },
            ConvertFormat = null
        };

        var (runOptions, _) = CliOptionsMapper.Map(cli, settings);

        Assert.Null(runOptions!.ConvertFormat);
    }

    [Fact]
    public void Map_HashType_IsNormalizedToUpperCase()
    {
        var settings = new RomulusSettings();
        var cli = new CliRunOptions
        {
            Roots = new[] { "C:\\temp" },
            HashType = "sha256"
        };

        var (runOptions, _) = CliOptionsMapper.Map(cli, settings);

        Assert.Equal("SHA256", runOptions!.HashType);
    }

    [Fact]
    public void Map_EmptyHashType_DefaultsToSHA1()
    {
        var settings = new RomulusSettings();
        var cli = new CliRunOptions
        {
            Roots = new[] { "C:\\temp" },
            HashType = null
        };

        var (runOptions, _) = CliOptionsMapper.Map(cli, settings);

        Assert.Equal("SHA1", runOptions!.HashType);
    }

    [Fact]
    public void Map_SettingsRegions_UsedWhenCliEmpty()
    {
        var settings = new RomulusSettings
        {
            General = { PreferredRegions = new List<string> { "JP", "WORLD" } }
        };
        var cli = new CliRunOptions
        {
            Roots = new[] { "C:\\temp" },
            PreferRegions = Array.Empty<string>()
        };

        var (runOptions, _) = CliOptionsMapper.Map(cli, settings);

        Assert.Equal(new[] { "JP", "WORLD" }, runOptions!.PreferRegions);
    }

    [Fact]
    public void Map_CliRegions_OverrideSettings()
    {
        var settings = new RomulusSettings
        {
            General = { PreferredRegions = new List<string> { "EU", "US" } }
        };
        var cli = new CliRunOptions
        {
            Roots = new[] { "C:\\temp" },
            PreferRegions = new[] { "JP" }
        };

        var (runOptions, _) = CliOptionsMapper.Map(cli, settings);

        Assert.Equal(new[] { "JP" }, runOptions!.PreferRegions);
    }

    [Fact]
    public void Map_SettingsDat_MergedWhenCliEmpty()
    {
        var settings = new RomulusSettings
        {
            Dat = { UseDat = true, HashType = "CRC32", DatRoot = "D:\\dats" }
        };
        var cli = new CliRunOptions
        {
            Roots = new[] { "C:\\temp" },
            EnableDat = false,
            HashType = null,
            DatRoot = null
        };

        var (runOptions, _) = CliOptionsMapper.Map(cli, settings);

        Assert.True(runOptions!.EnableDat);
        Assert.Equal("CRC32", runOptions.HashType);
        Assert.Equal("D:\\dats", runOptions.DatRoot);
    }

    [Fact]
    public void Map_EmptyRoots_ProducesEmptyNormalizedRoots()
    {
        var settings = new RomulusSettings();
        var cli = new CliRunOptions { Roots = Array.Empty<string>() };

        var (runOptions, _) = CliOptionsMapper.Map(cli, settings);

        Assert.NotNull(runOptions);
        Assert.Empty(runOptions!.Roots);
    }

    [Theory]
    [InlineData("move", "Move")]
    [InlineData("MOVE", "Move")]
    [InlineData("Move", "Move")]
    [InlineData("dryrun", "DryRun")]
    [InlineData("DRYRUN", "DryRun")]
    [InlineData("DryRun", "DryRun")]
    [InlineData("garbage", "DryRun")]
    [InlineData("", "DryRun")]
    [InlineData(null, "DryRun")]
    public void Normalize_Mode_CaseNormalized(string? inputMode, string expectedMode)
    {
        var settings = new RomulusSettings();
        var cli = new CliRunOptions { Roots = new[] { "C:\\temp" }, Mode = inputMode! };

        var (runOptions, _) = CliOptionsMapper.Map(cli, settings);

        Assert.Equal(expectedMode, runOptions!.Mode);
    }
}
