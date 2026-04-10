using System.Text.Json;
using System.Text.Json.Serialization;

namespace Romulus.Contracts.Models;

/// <summary>
/// Safety profile for sandbox validation.
/// Port of Get-SafetyPolicyProfiles from SafetyToolsService.ps1.
/// </summary>
public sealed class SafetyProfile
{
    public string Name { get; init; } = "";
    public bool Strict { get; init; }
    public IReadOnlyList<string> ProtectedPaths { get; init; } = [];
    public string ProtectedPathsText { get; init; } = "";
}

/// <summary>
/// Sandbox validation result.
/// Port of Invoke-SafetySandboxRun from SafetyToolsService.ps1.
/// </summary>
public sealed class SandboxValidationResult
{
    public string Status { get; init; } = "ok"; // ok, blocked
    public int BlockerCount { get; init; }
    public int WarningCount { get; init; }
    public int RootCount { get; init; }
    public bool StrictSafety { get; init; }
    public bool UseDat { get; init; }
    public bool ConvertEnabled { get; init; }
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Recommendations { get; init; } = [];
    public IReadOnlyList<PathCheckEntry> PathChecks { get; init; } = [];
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}

public sealed class PathCheckEntry
{
    public string Path { get; init; } = "";
    public string Status { get; init; } = ""; // ok, blocked, warning
    public string? Reason { get; init; }
}

/// <summary>
/// Tool self-test result.
/// </summary>
public sealed class ToolSelfTestResult
{
    public IReadOnlyList<ToolTestEntry> Results { get; init; } = [];
    public int HealthyCount { get; init; }
    public int MissingCount { get; init; }
    public int WarningCount { get; init; }
}

public sealed class ToolTestEntry
{
    public string Tool { get; init; } = "";
    public string Status { get; init; } = ""; // healthy, missing, warning, error
    public string? Path { get; init; }
    public string? Version { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Audit metadata sidecar content.
/// Port of Write-AuditMetadataSidecar from RunHelpers.Audit.ps1.
/// </summary>
public sealed class AuditMetadata
{
    public string Version { get; init; } = "v1";
    public string AuditFileName { get; init; } = "";
    public string CsvSha256 { get; init; } = "";
    public int RowCount { get; init; }
    public string CreatedUtc { get; init; } = "";
    public string? HmacSha256 { get; init; }
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalMetadata { get; init; }
}

/// <summary>
/// Rollback result from audit-based undo.
/// </summary>
public sealed class AuditRollbackResult
{
    public string AuditCsvPath { get; init; } = "";
    public int TotalRows { get; init; }
    public int EligibleRows { get; init; }
    public int SkippedUnsafe { get; init; }
    public int RolledBack { get; init; }
    public int DryRunPlanned { get; init; }
    public int SkippedMissingDest { get; init; }
    public int SkippedCollision { get; init; }
    public int Failed { get; init; }
    public bool DryRun { get; init; }
    public string? RollbackAuditPath { get; init; }
    public string? RollbackTrailPath { get; init; }
    public IReadOnlyList<string> RestoredPaths { get; init; } = [];
    public IReadOnlyList<string> PlannedPaths { get; init; } = [];
}

/// <summary>
/// Immutable audit row payload used for single-row and batched audit commits.
/// </summary>
public sealed record AuditAppendRow(
    string RootPath,
    string OldPath,
    string NewPath,
    string Action,
    string Category = "",
    string Hash = "",
    string Reason = "");

/// <summary>
/// Parallel hashing result.
/// </summary>
public sealed class ParallelHashResult
{
    public IReadOnlyList<FileHashEntry> Results { get; init; } = [];
    public int TotalFiles { get; init; }
    public int Errors { get; init; }
    public string Method { get; init; } = "SingleThread"; // Parallel, SingleThread
}

public sealed class FileHashEntry
{
    public string Path { get; init; } = "";
    public string? Hash { get; init; }
    public string? Error { get; init; }
}
