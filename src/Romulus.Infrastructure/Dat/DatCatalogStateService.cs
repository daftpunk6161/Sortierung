using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Paths;

namespace Romulus.Infrastructure.Dat;

/// <summary>
/// Central service for computing and persisting DAT catalog state.
/// Infrastructure layer — handles file I/O for state file and DAT root scanning.
/// No GUI dependency.
/// </summary>
public sealed class DatCatalogStateService
{
    /// <summary>Threshold in days after which a local DAT is considered stale.</summary>
    public const int StaleThresholdDays = 365;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Returns the default state file path under <c>%APPDATA%\Romulus\dat-catalog-state.json</c>.
    /// </summary>
    public static string GetDefaultStatePath()
    {
        return AppStoragePathResolver.ResolveRoamingPath("dat-catalog-state.json");
    }

    /// <summary>
    /// Load persisted catalog state from a JSON file.
    /// Returns empty state if file is missing, empty, or corrupt.
    /// </summary>
    public static DatCatalogState LoadState(string statePath)
    {
        if (string.IsNullOrWhiteSpace(statePath) || !File.Exists(statePath))
            return new DatCatalogState();

        try
        {
            var json = File.ReadAllText(statePath);
            if (string.IsNullOrWhiteSpace(json))
                return new DatCatalogState();

            return JsonSerializer.Deserialize<DatCatalogState>(json, JsonOpts) ?? new DatCatalogState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt or inaccessible state → start fresh
            return new DatCatalogState();
        }
    }

    /// <summary>
    /// Persist catalog state to JSON file with atomic backup.
    /// </summary>
    public static void SaveState(string statePath, DatCatalogState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(statePath))
            throw new ArgumentException("State path must not be empty.", nameof(statePath));

        var dir = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(state, JsonOpts);

        // Atomic write with backup
        var bakPath = statePath + ".bak";
        if (File.Exists(statePath))
        {
            if (File.Exists(bakPath))
                File.Delete(bakPath);
            File.Move(statePath, bakPath, overwrite: true);
        }

        try
        {
            File.WriteAllText(statePath, json);
        }
        catch
        {
            // Restore backup on failure
            if (File.Exists(bakPath) && !File.Exists(statePath))
            {
                try { File.Move(bakPath, statePath, overwrite: true); }
                catch (IOException) { /* best-effort restore */ }
            }
            throw;
        }
    }

    /// <summary>
    /// Build the full catalog status by merging catalog entries with file system state.
    /// </summary>
    public static List<DatCatalogStatusEntry> BuildCatalogStatus(
        IReadOnlyList<DatCatalogEntry> catalog,
        string? datRoot,
        DatCatalogState state)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(state);

        // Build lookup of local DAT files by filename stem
        var localFiles = new Dictionary<string, (string Path, DateTime LastWrite, long Size)>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(datRoot) && Directory.Exists(datRoot))
        {
            var datFiles = Directory.EnumerateFiles(datRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                               || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var file in datFiles)
            {
                var stem = Path.GetFileNameWithoutExtension(file)!;
                var info = new FileInfo(file);
                // First match wins with deterministic path ordering.
                localFiles.TryAdd(stem, (file, info.LastWriteTime, info.Length));
            }
        }

        var result = new List<DatCatalogStatusEntry>(catalog.Count);

        foreach (var entry in catalog)
        {
            var strategy = DetermineDownloadStrategy(entry);

            // Try to find matching local file: by Id, by ConsoleKey, by System prefix
            (string Path, DateTime LastWrite, long Size)? localMatch = null;
            if (localFiles.TryGetValue(entry.Id, out var byId))
                localMatch = byId;
            else if (localFiles.TryGetValue(entry.ConsoleKey, out var byKey))
                localMatch = byKey;
            else
            {
                // Redump/No-Intro DATs use filenames like "Acorn - Archimedes - Datfile (77) (2025-10-23).dat"
                // Match by System name prefix (e.g., entry.System = "Acorn - Archimedes")
                if (!string.IsNullOrWhiteSpace(entry.System))
                {
                    // Deterministic policy: choose the alphabetically first stem, then path.
                    // This keeps repeated scans stable across runs and machines.
                    var systemMatch = localFiles
                        .Where(pair => pair.Key.StartsWith(entry.System, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(pair => pair.Value.Path, StringComparer.OrdinalIgnoreCase)
                        .Select(pair => pair.Value)
                        .FirstOrDefault();

                    // Avoid treating the default tuple (no match) as a real local file.
                    if (!string.IsNullOrWhiteSpace(systemMatch.Path))
                        localMatch = systemMatch;
                }
            }

            // Also check state for previously tracked info
            state.Entries.TryGetValue(entry.Id, out var trackedInfo);

            DatInstallStatus status;
            DateTime? installedDate = null;
            string? localPath = null;
            long? fileSizeBytes = null;

            if (localMatch is not null)
            {
                localPath = localMatch.Value.Path;
                fileSizeBytes = localMatch.Value.Size;
                installedDate = trackedInfo?.InstalledDate ?? localMatch.Value.LastWrite;

                var daysSinceWrite = (DateTime.Now - localMatch.Value.LastWrite).TotalDays;
                status = daysSinceWrite > StaleThresholdDays
                    ? DatInstallStatus.Stale
                    : DatInstallStatus.Installed;
            }
            else if (trackedInfo is not null && !string.IsNullOrEmpty(trackedInfo.LocalPath)
                     && File.Exists(trackedInfo.LocalPath))
            {
                // State says it exists but wasn't found via stem search — verify
                localPath = trackedInfo.LocalPath;
                fileSizeBytes = trackedInfo.FileSizeBytes;
                installedDate = trackedInfo.InstalledDate;

                var daysSinceInstall = (DateTime.Now - trackedInfo.InstalledDate).TotalDays;
                status = daysSinceInstall > StaleThresholdDays
                    ? DatInstallStatus.Stale
                    : DatInstallStatus.Installed;
            }
            else
            {
                status = DatInstallStatus.Missing;
            }

            result.Add(new DatCatalogStatusEntry
            {
                Id = entry.Id,
                Group = entry.Group,
                System = entry.System,
                ConsoleKey = entry.ConsoleKey,
                Url = entry.Url,
                Format = entry.Format,
                Status = status,
                DownloadStrategy = strategy,
                InstalledDate = installedDate,
                LocalPath = localPath,
                FileSizeBytes = fileSizeBytes
            });
        }

        return result;
    }

    /// <summary>
    /// Merge built-in catalog with user entries and apply removal filter.
    /// Returns the effective catalog to display and work with.
    /// </summary>
    public static List<DatCatalogEntry> MergeCatalogs(
        IReadOnlyList<DatCatalogEntry> builtinCatalog,
        DatCatalogState state)
    {
        ArgumentNullException.ThrowIfNull(builtinCatalog);
        ArgumentNullException.ThrowIfNull(state);

        var result = new List<DatCatalogEntry>(builtinCatalog.Count + state.UserEntries.Count);

        // Add built-in entries not removed by user
        foreach (var entry in builtinCatalog)
        {
            if (!state.RemovedBuiltinIds.Contains(entry.Id))
                result.Add(entry);
        }

        // Add user-defined entries
        foreach (var ue in state.UserEntries)
        {
            result.Add(new DatCatalogEntry
            {
                Id = ue.Id,
                Group = ue.Group,
                System = ue.System,
                Url = ue.Url,
                Format = ue.Format,
                ConsoleKey = ue.ConsoleKey
            });
        }

        return result;
    }

    /// <summary>
    /// Determine download strategy from catalog format and group.
    /// Matches the decision tree from ADR-0020.
    /// </summary>
    public static DatDownloadStrategy DetermineDownloadStrategy(DatCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Redump always requires manual login
        if (entry.Group.Equals("Redump", StringComparison.OrdinalIgnoreCase))
            return DatDownloadStrategy.ManualLogin;

        // nointro-pack requires pack import
        if (string.Equals(entry.Format, "nointro-pack", StringComparison.OrdinalIgnoreCase))
            return DatDownloadStrategy.PackImport;

        // Auto-downloadable if URL is present and format supports direct download
        if (!string.IsNullOrWhiteSpace(entry.Url)
            && entry.Format is "raw-dat" or "zip-dat" or "7z-dat")
            return DatDownloadStrategy.Auto;

        // Fallback: no URL or unknown format
        return DatDownloadStrategy.ManualLogin;
    }

    /// <summary>
    /// Update state after a successful download. Call this after DatSourceService.DownloadDatByFormatAsync.
    /// </summary>
    public static void UpdateStateAfterDownload(
        DatCatalogState state, string catalogId, string localPath, long sizeBytes)
    {
        ArgumentNullException.ThrowIfNull(state);

        string sha256;
        try
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(localPath);
            sha256 = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
        catch
        {
            sha256 = "";
        }

        state.Entries[catalogId] = new DatLocalInfo
        {
            InstalledDate = DateTime.UtcNow,
            FileSha256 = sha256,
            FileSizeBytes = sizeBytes,
            LocalPath = localPath
        };
    }

    /// <summary>
    /// Perform a full state scan: match all catalog entries against the file system and update state.
    /// Returns the refreshed state.
    /// </summary>
    public static DatCatalogState FullScan(
        IReadOnlyList<DatCatalogEntry> catalog,
        string datRoot,
        DatCatalogState state)
    {
        if (string.IsNullOrWhiteSpace(datRoot) || !Directory.Exists(datRoot))
            return state;

        var statusEntries = BuildCatalogStatus(catalog, datRoot, state);
        foreach (var entry in statusEntries)
        {
            if (entry.Status is DatInstallStatus.Installed or DatInstallStatus.Stale
                && !string.IsNullOrEmpty(entry.LocalPath))
            {
                // Only update state for new entries or when the local path changed
                // (e.g. re-import to different location). This preserves InstalledDate
                // and avoids expensive re-hashing on every periodic scan.
                if (!state.Entries.TryGetValue(entry.Id, out var existing)
                    || !string.Equals(existing.LocalPath, entry.LocalPath, StringComparison.OrdinalIgnoreCase))
                {
                    UpdateStateAfterDownload(state, entry.Id, entry.LocalPath,
                        entry.FileSizeBytes ?? 0);
                }
            }
            else if (entry.Status == DatInstallStatus.Missing)
            {
                state.Entries.Remove(entry.Id);
            }
        }

        state.LastFullScan = DateTime.UtcNow;
        return state;
    }
}
