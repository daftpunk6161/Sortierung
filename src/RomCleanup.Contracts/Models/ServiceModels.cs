using RomCleanup.Contracts.Errors;

namespace RomCleanup.Contracts.Models;

/// <summary>
/// Multi-step conversion pipeline definition.
/// Port of New-ConversionPipeline from ConversionPipeline.ps1.
/// </summary>
public sealed class ConversionPipelineDef
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string SourcePath { get; init; } = "";
    public IReadOnlyList<ConversionPipelineStep> Steps { get; init; } = [];
    public bool CleanupTemps { get; init; } = true;
    public DateTime Created { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Result of executing a conversion pipeline.
/// </summary>
public sealed class ConversionPipelineResult
{
    public string Status { get; init; } = "pending";
    public IReadOnlyList<PipelineStepResult> Steps { get; init; } = [];
}

/// <summary>
/// A single step in a conversion pipeline.
/// </summary>
public sealed class ConversionPipelineStep
{
    public string Tool { get; init; } = "";
    public string Action { get; init; } = "";
    public string Input { get; init; } = "";
    public string Output { get; init; } = "";
    public bool IsTemp { get; init; }
}

/// <summary>
/// Result of a single pipeline step execution.
/// </summary>
public sealed class PipelineStepResult
{
    public string Status { get; init; } = "pending"; // ok, error, skipped
    public string Tool { get; init; } = "";
    public string Action { get; init; } = "";
    public string Input { get; init; } = "";
    public string Output { get; init; } = "";
    public bool Skipped { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Disk space validation result for conversion.
/// </summary>
public sealed class DiskSpaceCheckResult
{
    public bool Ok { get; init; }
    public string? Reason { get; init; }
    public long RequiredBytes { get; init; }
    public long AvailableBytes { get; init; }
}

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
/// CatchGuard error record for audit trail.
/// Port of New-CatchGuardRecord from CatchGuard.ps1.
/// </summary>
public sealed class CatchGuardRecord
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public ErrorKind ErrorClass { get; init; }
    public string Module { get; init; } = "";
    public string OperationId { get; init; } = "";
    public string Root { get; init; } = "";
    public string Action { get; init; } = "";
    public string ErrorCode { get; init; } = "";
    public string ExceptionType { get; init; } = "";
    public string Message { get; init; } = "";
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
}

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

/// <summary>
/// Duplicate inspector row for analytics display.
/// </summary>
public sealed class DuplicateInspectorRow
{
    public string GameKey { get; init; } = "";
    public bool Winner { get; init; }
    public string WinnerSource { get; init; } = "";
    public string Region { get; init; } = "";
    public string Type { get; init; } = "";
    public double SizeMB { get; init; }
    public int RegionScore { get; init; }
    public int HeaderScore { get; init; }
    public int VersionScore { get; init; }
    public int FormatScore { get; init; }
    public int CompletenessScore { get; init; }
    public long SizeTieBreakScore { get; init; }
    public int TotalScore { get; init; }
    public string ScoreBreakdown { get; init; } = "";
    public string MainPath { get; init; } = "";
}

/// <summary>
/// Collection health row for dashboard display.
/// </summary>
public sealed class CollectionHealthRow
{
    public string Console { get; init; } = "";
    public int Roms { get; init; }
    public int Duplicates { get; init; }
    public int MissingDat { get; init; }
    public int Corrupt { get; init; }
    public string Formats { get; init; } = "";
}

/// <summary>
/// DAT coverage heatmap row.
/// </summary>
public sealed class DatCoverageRow
{
    public string Console { get; init; } = "";
    public int Matched { get; init; }
    public int Expected { get; init; }
    public int Missing { get; init; }
    public double Coverage { get; init; }
    public string Heat { get; init; } = "";
}
