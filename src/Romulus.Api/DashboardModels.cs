using Romulus.Contracts.Models;
using Romulus.Infrastructure.Analysis;

namespace Romulus.Api;

public sealed class DashboardBootstrapResponse
{
    public string Version { get; init; } = string.Empty;
    public bool RequiresApiKey { get; init; } = true;
    public string ApiKeyHeader { get; init; } = "X-Api-Key";
    public bool DashboardEnabled { get; init; } = true;
    public bool AllowRemoteClients { get; init; }
    public bool AllowedRootsEnforced { get; init; }
    public string[] AllowedRoots { get; init; } = Array.Empty<string>();
    public string? PublicBaseUrl { get; init; }
    public string DashboardPath { get; init; } = "/dashboard/";
}

public sealed class DashboardSummaryResponse
{
    public string Version { get; init; } = string.Empty;
    public bool HasActiveRun { get; init; }
    public RunStatusDto? ActiveRun { get; init; }
    public ApiWatchStatus? WatchStatus { get; init; }
    public DashboardDatStatusResponse DatStatus { get; init; } = new();
    public StorageInsightReport Trends { get; init; } = new(0, null, new(0, 0, 0), new(0, 0, 0), new(0, 0, 0), 0, 0, 0, 0, 0);
    public ApiRunHistoryEntry[] RecentRuns { get; init; } = Array.Empty<ApiRunHistoryEntry>();
    public RunProfileSummary[] Profiles { get; init; } = Array.Empty<RunProfileSummary>();
    public WorkflowScenarioDefinition[] Workflows { get; init; } = Array.Empty<WorkflowScenarioDefinition>();
}

public sealed class DashboardDatStatusResponse
{
    public bool Configured { get; init; }
    public string DatRoot { get; init; } = string.Empty;
    public string? Message { get; init; }
    public int TotalFiles { get; init; }
    public DashboardDatConsoleStatus[] Consoles { get; init; } = Array.Empty<DashboardDatConsoleStatus>();
    public int OldFileCount { get; init; }
    public int CatalogEntries { get; init; }
    public string? StaleWarning { get; init; }
    public bool WithinAllowedRoots { get; init; } = true;
}

public sealed class DashboardDatConsoleStatus
{
    public string Console { get; init; } = string.Empty;
    public int FileCount { get; init; }
    public string NewestFileUtc { get; init; } = string.Empty;
    public string OldestFileUtc { get; init; } = string.Empty;
}
