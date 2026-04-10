using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Profiles;

public sealed class JsonRunProfileStore : IRunProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _profileDirectory;

    public JsonRunProfileStore(RunProfilePathOptions? options = null)
    {
        _profileDirectory = RunProfilePaths.ResolveUserProfileDirectory(options?.DirectoryPath);
    }

    public async ValueTask<IReadOnlyList<RunProfileDocument>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_profileDirectory))
            return Array.Empty<RunProfileDocument>();

        var profiles = new List<RunProfileDocument>();
        foreach (var filePath in Directory.EnumerateFiles(_profileDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var document = await TryReadProfileAsync(filePath, ct).ConfigureAwait(false);
            if (document is not null)
                profiles.Add(document with { BuiltIn = false });
        }

        return profiles;
    }

    public async ValueTask<RunProfileDocument?> TryGetAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var filePath = Path.Combine(_profileDirectory, id + ".json");
        if (!File.Exists(filePath))
            return null;

        var document = await TryReadProfileAsync(filePath, ct).ConfigureAwait(false);
        return document is null ? null : document with { BuiltIn = false };
    }

    public async ValueTask UpsertAsync(RunProfileDocument profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var errors = RunProfileValidator.ValidateDocument(profile);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        Directory.CreateDirectory(_profileDirectory);

        var filePath = Path.Combine(_profileDirectory, profile.Id + ".json");
        var tempPath = filePath + ".tmp";
        var normalizedProfile = profile with { BuiltIn = false };
        var json = JsonSerializer.Serialize(normalizedProfile, SerializerOptions);

        await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
        File.Move(tempPath, filePath, overwrite: true);
    }

    public ValueTask<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return ValueTask.FromResult(false);

        var filePath = Path.Combine(_profileDirectory, id + ".json");
        if (!File.Exists(filePath))
            return ValueTask.FromResult(false);

        File.Delete(filePath);
        return ValueTask.FromResult(true);
    }

    private static async Task<RunProfileDocument?> TryReadProfileAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<RunProfileDocument>(json, SerializerOptions);
            if (document is null)
                return null;

            var errors = RunProfileValidator.ValidateDocument(document);
            return errors.Count == 0 ? document : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
