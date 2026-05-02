using System.Text.Json;
using Romulus.Api;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Profiles;
using Xunit;

namespace Romulus.Tests;

public sealed class ApiRunConfigurationMapperPreferredDatSourcesTests : IDisposable
{
    private readonly string _tempRoot;

    public ApiRunConfigurationMapperPreferredDatSourcesTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "api-run-config-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task ResolveAsync_UsesSettingsPreferredDatSources_WhenRequestOmitsOverride()
    {
        var settings = CreateSettings(["No-Intro", "Redump"]);
        var request = new RunRequest { Roots = [_tempRoot] };
        using var document = CreateRequestDocument(new { roots = new[] { _tempRoot } });

        var resolved = await ApiRunConfigurationMapper.ResolveAsync(
            request,
            document.RootElement,
            settings,
            CreateMaterializer());

        Assert.Equal(["No-Intro", "Redump"], resolved.Request.PreferredDatSources!);
        Assert.Equal(["No-Intro", "Redump"], resolved.Materialized.Options.PreferredDatSources);
    }

    [Fact]
    public async Task ResolveAsync_ExplicitPreferredDatSourcesOverrideSettings()
    {
        var settings = CreateSettings(["No-Intro"]);
        var request = new RunRequest
        {
            Roots = [_tempRoot],
            PreferredDatSources = ["Redump", "No-Intro"]
        };
        using var document = CreateRequestDocument(new
        {
            roots = new[] { _tempRoot },
            preferredDatSources = new[] { "Redump", "No-Intro" }
        });

        var resolved = await ApiRunConfigurationMapper.ResolveAsync(
            request,
            document.RootElement,
            settings,
            CreateMaterializer());

        Assert.Equal(["Redump", "No-Intro"], resolved.Request.PreferredDatSources!);
        Assert.Equal(["Redump", "No-Intro"], resolved.Materialized.Options.PreferredDatSources);
    }

    private RunConfigurationMaterializer CreateMaterializer()
    {
        var profileStore = new JsonRunProfileStore(new RunProfilePathOptions
        {
            DirectoryPath = Path.Combine(_tempRoot, "profiles")
        });
        var profileService = new RunProfileService(profileStore, Path.Combine(_tempRoot, "data"));
        return new RunConfigurationMaterializer(new RunConfigurationResolver(profileService));
    }

    private static RomulusSettings CreateSettings(string[] preferredSources)
    {
        var settings = new RomulusSettings();
        settings.Dat.PreferredSources.AddRange(preferredSources);
        return settings;
    }

    private static JsonDocument CreateRequestDocument<T>(T payload)
        => JsonDocument.Parse(JsonSerializer.Serialize(payload));
}
