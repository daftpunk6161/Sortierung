using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Profiles;
using Romulus.Infrastructure.Safety;
using Romulus.Infrastructure.Workflow;

namespace Romulus.Api;

internal static class DashboardDataBuilder
{
    public static DashboardBootstrapResponse BuildBootstrap(
        HeadlessApiOptions options,
        AllowedRootPathPolicy allowedRootPolicy,
        string version)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(allowedRootPolicy);

        return new DashboardBootstrapResponse
        {
            Version = version,
            DashboardEnabled = options.DashboardEnabled,
            AllowRemoteClients = options.AllowRemoteClients,
            AllowedRootsEnforced = allowedRootPolicy.IsEnforced,
            AllowedRoots = Array.Empty<string>(),
            PublicBaseUrl = options.PublicBaseUrl
        };
    }

    public static async Task<DashboardSummaryResponse> BuildSummaryAsync(
        RunLifecycleManager lifecycleManager,
        ApiAutomationService automationService,
        ICollectionIndex collectionIndex,
        RunProfileService profileService,
        AllowedRootPathPolicy allowedRootPolicy,
        string requesterClientId,
        string version,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lifecycleManager);
        ArgumentNullException.ThrowIfNull(automationService);
        ArgumentNullException.ThrowIfNull(collectionIndex);
        ArgumentNullException.ThrowIfNull(profileService);
        ArgumentNullException.ThrowIfNull(allowedRootPolicy);

        var activeRun = lifecycleManager.GetActive();
        if (activeRun is not null && !Program.CanAccessRun(activeRun, requesterClientId))
            activeRun = null;

        var snapshots = await collectionIndex.ListRunSnapshotsAsync(int.MaxValue, ct).ConfigureAwait(false);
        var visibleSnapshots = snapshots
            .Where(snapshot => Program.CanAccessSnapshot(snapshot, requesterClientId))
            .ToArray();
        var trends = RunHistoryInsightsService.BuildStorageInsights(visibleSnapshots, 30);
        var profiles = await profileService.ListAsync(ct).ConfigureAwait(false);
        var workflows = WorkflowScenarioCatalog.List();
        var datStatus = await BuildDatStatusAsync(allowedRootPolicy, ct).ConfigureAwait(false);

        return new DashboardSummaryResponse
        {
            Version = version,
            HasActiveRun = activeRun is not null,
            ActiveRun = activeRun?.ToDto(),
            WatchStatus = automationService.GetStatus(),
            DatStatus = datStatus,
            Trends = trends,
            RecentRuns = CollectionRunHistoryPageBuilder.Build(visibleSnapshots, visibleSnapshots.Length, 0, 10)
                .Runs
                .Select(MapRunHistoryEntry)
                .ToArray(),
            Profiles = profiles.ToArray(),
            Workflows = workflows.ToArray()
        };
    }

    public static Task<DashboardDatStatusResponse> BuildDatStatusAsync(
        AllowedRootPathPolicy allowedRootPolicy,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var dataDir = RunEnvironmentBuilder.TryResolveDataDir()
            ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var datRootResolution = RunEnvironmentBuilder.ResolveEffectiveDatRoot(
            runOptionDatRoot: null,
            settingsDatRoot: settings.Dat?.DatRoot,
            dataDir: dataDir);
        var datRoot = datRootResolution.Path;

        if (string.IsNullOrWhiteSpace(datRoot) || !Directory.Exists(datRoot))
        {
            return Task.FromResult(new DashboardDatStatusResponse
            {
                Configured = false,
                DatRoot = datRoot ?? string.Empty,
                Message = "DatRoot is not configured, does not exist, or no DAT files were auto-detected.",
                TotalFiles = 0,
                Consoles = Array.Empty<DashboardDatConsoleStatus>(),
                OldFileCount = 0,
                CatalogEntries = 0,
                WithinAllowedRoots = !string.IsNullOrWhiteSpace(datRoot) && allowedRootPolicy.IsPathAllowed(datRoot)
            });
        }

        return BuildDatStatusAsync(datRoot, dataDir, allowedRootPolicy, ct);
    }

    /// <summary>
    /// Testable overload that accepts explicit paths instead of resolving them from
    /// static environment state. Wraps I/O calls in try/catch to handle
    /// <see cref="UnauthorizedAccessException"/> gracefully (F3+F6 fix).
    /// </summary>
    internal static Task<DashboardDatStatusResponse> BuildDatStatusAsync(
        string datRoot,
        string dataDir,
        AllowedRootPathPolicy allowedRootPolicy,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(datRoot) || !Directory.Exists(datRoot))
        {
            return Task.FromResult(new DashboardDatStatusResponse
            {
                Configured = false,
                DatRoot = datRoot ?? string.Empty,
                Message = "DatRoot is not configured or does not exist.",
                TotalFiles = 0,
                Consoles = Array.Empty<DashboardDatConsoleStatus>(),
                OldFileCount = 0,
                CatalogEntries = 0,
                WithinAllowedRoots = !string.IsNullOrWhiteSpace(datRoot) && allowedRootPolicy.IsPathAllowed(datRoot)
            });
        }

        try
        {
            _ = Directory.EnumerateFileSystemEntries(datRoot).Take(1).ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Task.FromResult(new DashboardDatStatusResponse
            {
                Configured = true,
                DatRoot = datRoot,
                Message = $"Cannot access DAT root: {ex.GetType().Name}",
                TotalFiles = 0,
                Consoles = Array.Empty<DashboardDatConsoleStatus>(),
                WithinAllowedRoots = allowedRootPolicy.IsPathAllowed(datRoot)
            });
        }

        var datFiles = DatCatalogStateService.EnumerateLocalDatFilesSafe(datRoot);

        var consoleStats = datFiles
            .GroupBy(file =>
            {
                var dir = Path.GetDirectoryName(file.Path);
                return dir is not null && !string.Equals(Path.GetFullPath(dir), Path.GetFullPath(datRoot), StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(dir)
                    : "root";
            }, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                DateTime newest = DateTime.MinValue, oldest = DateTime.MaxValue;
                foreach (var file in group)
                {
                    var mtime = file.LastWriteUtc;
                    if (mtime > newest) newest = mtime;
                    if (mtime < oldest) oldest = mtime;
                }

                return new DashboardDatConsoleStatus
                {
                    Console = group.Key,
                    FileCount = group.Count(),
                    NewestFileUtc = newest == DateTime.MinValue ? string.Empty : newest.ToString("o"),
                    OldestFileUtc = oldest == DateTime.MaxValue ? string.Empty : oldest.ToString("o")
                };
            })
            .OrderBy(static item => item.Console, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var oldFileCount = 0;
        var staleThresholdDays = DatCatalogStateService.StaleThresholdDays;
        foreach (var file in datFiles)
        {
            if ((DateTime.UtcNow - file.LastWriteUtc).TotalDays > staleThresholdDays)
                oldFileCount++;
        }

        var catalogPath = Path.Combine(dataDir, "dat-catalog.json");
        var catalogEntries = 0;
        if (File.Exists(catalogPath))
        {
            try
            {
                catalogEntries = DatSourceService.LoadCatalog(catalogPath).Count;
            }
            catch
            {
                // status endpoint must stay best-effort
            }
        }

        return Task.FromResult(new DashboardDatStatusResponse
        {
            Configured = true,
            DatRoot = datRoot,
            TotalFiles = datFiles.Count,
            Consoles = consoleStats,
            OldFileCount = oldFileCount,
            CatalogEntries = catalogEntries,
            StaleWarning = oldFileCount > 0
                ? $"{oldFileCount} DAT files are older than {staleThresholdDays} days"
                : null,
            WithinAllowedRoots = allowedRootPolicy.IsPathAllowed(datRoot)
        });
    }

    private static ApiRunHistoryEntry MapRunHistoryEntry(CollectionRunHistoryItem item)
        => new()
        {
            RunId = item.RunId,
            StartedUtc = item.StartedUtc,
            CompletedUtc = item.CompletedUtc,
            Mode = item.Mode,
            Status = item.Status,
            RootCount = item.RootCount,
            RootFingerprint = item.RootFingerprint,
            DurationMs = item.DurationMs,
            TotalFiles = item.TotalFiles,
            CollectionSizeBytes = item.CollectionSizeBytes,
            Games = item.Games,
            Dupes = item.Dupes,
            Junk = item.Junk,
            DatMatches = item.DatMatches,
            ConvertedCount = item.ConvertedCount,
            FailCount = item.FailCount,
            SavedBytes = item.SavedBytes,
            ConvertSavedBytes = item.ConvertSavedBytes,
            HealthScore = item.HealthScore
        };
}
