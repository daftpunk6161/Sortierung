using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;

namespace Romulus.Infrastructure.Profiles;

public sealed class RunProfileService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IRunProfileStore _store;
    private readonly string _dataDir;

    public RunProfileService(
        IRunProfileStore store,
        string? dataDir = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dataDir = dataDir ?? RunEnvironmentBuilder.ResolveDataDir();
    }

    public async ValueTask<IReadOnlyList<RunProfileSummary>> ListAsync(CancellationToken ct = default)
    {
        var builtIns = await LoadBuiltInsAsync(ct).ConfigureAwait(false);
        var userProfiles = await _store.ListAsync(ct).ConfigureAwait(false);

        return builtIns
            .Concat(userProfiles)
            .OrderBy(static profile => profile.BuiltIn ? 0 : 1)
            .ThenBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static profile => new RunProfileSummary(
                profile.Id,
                profile.Name,
                profile.Description,
                profile.BuiltIn,
                profile.Tags,
                profile.WorkflowScenarioId))
            .ToArray();
    }

    public async ValueTask<RunProfileDocument?> TryGetAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var builtIns = await LoadBuiltInsAsync(ct).ConfigureAwait(false);
        var builtIn = builtIns.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));
        if (builtIn is not null)
            return builtIn;

        return await _store.TryGetAsync(id, ct).ConfigureAwait(false);
    }

    public async ValueTask<RunProfileDocument> ImportAsync(string sourcePath, CancellationToken ct = default)
    {
        var normalized = await LoadExternalAsync(sourcePath, ct).ConfigureAwait(false);
        await SaveAsync(normalized, ct).ConfigureAwait(false);
        return normalized;
    }

    public async ValueTask<RunProfileDocument> SaveAsync(RunProfileDocument profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var normalized = profile with
        {
            BuiltIn = false,
            Id = profile.Id.Trim(),
            Name = profile.Name.Trim(),
            Description = profile.Description?.Trim() ?? string.Empty
        };

        var builtIn = await TryGetBuiltInAsync(normalized.Id, ct).ConfigureAwait(false);
        if (builtIn is not null)
            throw new InvalidOperationException($"Built-in profile '{normalized.Id}' cannot be overwritten.");

        await _store.UpsertAsync(normalized, ct).ConfigureAwait(false);
        return normalized;
    }

    public async ValueTask<RunProfileDocument> LoadExternalAsync(string sourcePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new InvalidOperationException("Profile import path is required.");

        var json = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);
        var document = JsonSerializer.Deserialize<RunProfileDocument>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Profile JSON could not be parsed.");

        var normalized = document with
        {
            BuiltIn = false,
            Id = document.Id.Trim(),
            Name = document.Name.Trim(),
            Description = document.Description?.Trim() ?? string.Empty
        };

        var errors = RunProfileValidator.ValidateDocument(normalized);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        return normalized;
    }

    public async ValueTask<string> ExportAsync(string id, string targetPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new InvalidOperationException("Profile export path is required.");

        var profile = await TryGetAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Profile '{id}' was not found.");

        var fullTargetPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTargetPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(profile, SerializerOptions);
        await File.WriteAllTextAsync(fullTargetPath, json, ct).ConfigureAwait(false);
        return fullTargetPath;
    }

    public async ValueTask<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        if (await TryGetBuiltInAsync(id, ct).ConfigureAwait(false) is not null)
            throw new InvalidOperationException($"Built-in profile '{id}' cannot be deleted.");

        return await _store.DeleteAsync(id, ct).ConfigureAwait(false);
    }

    private async ValueTask<RunProfileDocument?> TryGetBuiltInAsync(string id, CancellationToken ct)
    {
        var builtIns = await LoadBuiltInsAsync(ct).ConfigureAwait(false);
        return builtIns.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private async ValueTask<IReadOnlyList<RunProfileDocument>> LoadBuiltInsAsync(CancellationToken ct)
    {
        var builtInPath = RunProfilePaths.ResolveBuiltInProfilesPath(_dataDir);
        if (!File.Exists(builtInPath))
            return Array.Empty<RunProfileDocument>();

        var json = await File.ReadAllTextAsync(builtInPath, ct).ConfigureAwait(false);
        var documents = JsonSerializer.Deserialize<RunProfileDocument[]>(json, SerializerOptions) ?? Array.Empty<RunProfileDocument>();

        return documents
            .Select(static profile => profile with
            {
                BuiltIn = true,
                Id = profile.Id.Trim(),
                Name = profile.Name.Trim(),
                Description = profile.Description?.Trim() ?? string.Empty
            })
            .Where(profile => RunProfileValidator.ValidateDocument(profile).Count == 0)
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
