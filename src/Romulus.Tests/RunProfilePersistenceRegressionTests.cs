using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Profiles;
using Xunit;

namespace Romulus.Tests;

public sealed class RunProfilePersistenceRegressionTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _profileDir;
    private readonly string _dataDir;

    public RunProfilePersistenceRegressionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Romulus_ProfilePersistence_" + Guid.NewGuid().ToString("N"));
        _profileDir = Path.Combine(_tempRoot, "profiles");
        _dataDir = Path.Combine(_tempRoot, "data");
        Directory.CreateDirectory(_profileDir);
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task JsonRunProfileStore_RoundTripsProfilesAndSkipsCorruptOrInvalidDocuments()
    {
        var store = CreateStore();
        await store.UpsertAsync(Profile("custom-profile", "Custom Profile"));
        await store.UpsertAsync(Profile("alpha-profile", "Alpha Profile"));
        await File.WriteAllTextAsync(Path.Combine(_profileDir, "broken.json"), "{ not-json");
        await File.WriteAllTextAsync(Path.Combine(_profileDir, "invalid.json"), JsonSerializer.Serialize(
            Profile("invalid-profile", "") with { Name = "" }));

        var profiles = await store.ListAsync();

        Assert.Equal(["alpha-profile", "custom-profile"], profiles.Select(static profile => profile.Id).ToArray());
        Assert.All(profiles, static profile => Assert.False(profile.BuiltIn));

        var custom = await store.TryGetAsync("custom-profile");
        Assert.NotNull(custom);
        Assert.Equal("Custom Profile", custom!.Name);
        Assert.True(custom.Settings.SortConsole);
        Assert.False(custom.BuiltIn);

        Assert.Null(await store.TryGetAsync("broken"));
        Assert.False(await store.DeleteAsync("missing-profile"));
        Assert.True(await store.DeleteAsync("custom-profile"));
        Assert.Null(await store.TryGetAsync("custom-profile"));
    }

    [Fact]
    public async Task JsonRunProfileStore_ListSynchronouslyMatchesAsyncFilteringAndOrdering()
    {
        var store = CreateStore();
        await store.UpsertAsync(Profile("zeta-profile", "Zeta Profile") with { BuiltIn = true });
        await store.UpsertAsync(Profile("alpha-profile", "Alpha Profile"));
        await File.WriteAllTextAsync(Path.Combine(_profileDir, "broken.json"), "{ not-json");
        await File.WriteAllTextAsync(Path.Combine(_profileDir, "invalid.json"), JsonSerializer.Serialize(
            Profile("invalid-profile", "") with { Name = "" }));

        var asyncProfiles = await store.ListAsync();
        var syncProfiles = store.ListSynchronously();

        Assert.Equal(["alpha-profile", "zeta-profile"], syncProfiles.Select(static profile => profile.Id).ToArray());
        Assert.Equal(asyncProfiles.Select(static profile => profile.Id), syncProfiles.Select(static profile => profile.Id));
        Assert.All(syncProfiles, static profile => Assert.False(profile.BuiltIn));
    }

    [Fact]
    public async Task JsonRunProfileStore_MissingDirectoryAndInvalidIdsAreSafeNoOps()
    {
        var missingDir = Path.Combine(_tempRoot, "missing-profiles");
        var store = new JsonRunProfileStore(new RunProfilePathOptions { DirectoryPath = missingDir });

        Assert.Empty(await store.ListAsync());
        Assert.Empty(store.ListSynchronously());
        Assert.Null(await store.TryGetAsync("bad id with spaces"));
        Assert.False(await store.DeleteAsync("bad id with spaces"));
        Assert.False(Directory.Exists(missingDir));
    }

    [Fact]
    public async Task RunProfileService_ProtectsBuiltInsAndExportsUserProfiles()
    {
        WriteBuiltInProfiles();
        var service = CreateService();

        var saved = await service.SaveAsync(Profile("custom-profile", "  Custom Profile  ") with
        {
            Description = "  local profile  "
        });

        Assert.Equal("custom-profile", saved.Id);
        Assert.False(saved.BuiltIn);
        Assert.Equal("Custom Profile", saved.Name);
        Assert.Equal("local profile", saved.Description);

        var summaries = await service.ListAsync();
        Assert.Equal(["default", "custom-profile"], summaries.Select(static profile => profile.Id).ToArray());
        Assert.True(summaries[0].BuiltIn);
        Assert.False(summaries[1].BuiltIn);

        var builtIn = await service.TryGetAsync("DEFAULT");
        Assert.NotNull(builtIn);
        Assert.True(builtIn!.BuiltIn);

        var exportPath = Path.Combine(_tempRoot, "exports", "custom-profile.json");
        var exportedPath = await service.ExportAsync("custom-profile", exportPath);
        Assert.Equal(exportPath, exportedPath);
        var exported = JsonSerializer.Deserialize<RunProfileDocument>(
            await File.ReadAllTextAsync(exportPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(exported);
        Assert.Equal("custom-profile", exported!.Id);
        Assert.False(exported.BuiltIn);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.SaveAsync(Profile("default", "Attempted BuiltIn Overwrite")));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.DeleteAsync("default"));

        Assert.True(await service.DeleteAsync("custom-profile"));
        Assert.Null(await service.TryGetAsync("custom-profile"));
    }

    [Fact]
    public async Task RunProfileService_ImportAsync_NormalizesExternalProfileAndPersistsItAsUserProfile()
    {
        var service = CreateService();
        var externalPath = Path.Combine(_tempRoot, "external-profile.json");
        await File.WriteAllTextAsync(externalPath, JsonSerializer.Serialize(Profile("imported-profile", "  Imported Profile  ") with
        {
            BuiltIn = true,
            Description = "  imported from disk  "
        }));

        var imported = await service.ImportAsync(externalPath);
        var stored = await service.TryGetAsync("imported-profile");

        Assert.Equal("imported-profile", imported.Id);
        Assert.False(imported.BuiltIn);
        Assert.Equal("Imported Profile", imported.Name);
        Assert.Equal("imported from disk", imported.Description);
        Assert.NotNull(stored);
        Assert.False(stored!.BuiltIn);
        Assert.Equal("Imported Profile", stored.Name);
    }

    private JsonRunProfileStore CreateStore()
        => new(new RunProfilePathOptions { DirectoryPath = _profileDir });

    private RunProfileService CreateService()
        => new(CreateStore(), _dataDir);

    private void WriteBuiltInProfiles()
    {
        File.WriteAllText(Path.Combine(_dataDir, RunProfilePaths.BuiltInProfilesFileName),
            """
            [
              {
                "version": 1,
                "id": "default",
                "name": "Default",
                "description": "Built-in profile",
                "builtIn": true,
                "tags": ["default"],
                "settings": {
                  "mode": "DryRun",
                  "removeJunk": true,
                  "sortConsole": true,
                  "hashType": "SHA1"
                }
              }
            ]
            """);
    }

    private static RunProfileDocument Profile(string id, string name)
        => new()
        {
            Version = 1,
            Id = id,
            Name = name,
            Description = "profile",
            Settings = new RunProfileSettings
            {
                Mode = "DryRun",
                SortConsole = true,
                EnableDat = true,
                EnableDatAudit = true,
                HashType = "SHA1"
            }
        };
}
