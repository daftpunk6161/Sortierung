namespace Romulus.Contracts.Models;

public enum RunOutcome
{
    Ok,
    CompletedWithErrors,
    Blocked,
    Cancelled,
    Failed
}

public static class RunOutcomeExtensions
{
    public static string ToStatusString(this RunOutcome outcome) => outcome switch
    {
        RunOutcome.Ok => RunConstants.StatusOk,
        RunOutcome.CompletedWithErrors => RunConstants.StatusCompletedWithErrors,
        RunOutcome.Blocked => RunConstants.StatusBlocked,
        RunOutcome.Cancelled => RunConstants.StatusCancelled,
        RunOutcome.Failed => RunConstants.StatusFailed,
        _ => RunConstants.StatusFailed
    };

    public static RunOutcome ParseRunOutcome(string? status) => status switch
    {
        RunConstants.StatusOk => RunOutcome.Ok,
        RunConstants.StatusCompleted => RunOutcome.Ok,
        RunConstants.StatusCompletedWithErrors => RunOutcome.CompletedWithErrors,
        RunConstants.StatusBlocked => RunOutcome.Blocked,
        RunConstants.StatusCancelled => RunOutcome.Cancelled,
        RunConstants.StatusFailed => RunOutcome.Failed,
        _ => RunOutcome.Failed
    };

    public static int ToExitCode(this RunOutcome outcome) => outcome switch
    {
        RunOutcome.Ok => 0,
        RunOutcome.Failed => 1,
        RunOutcome.Cancelled => 2,
        RunOutcome.Blocked => 3,
        RunOutcome.CompletedWithErrors => 4,
        _ => 1
    };
}

/// <summary>
/// Standardized operation result contract.
/// Port of New-OperationResult from RunHelpers.Execution.ps1.
/// All pipeline phases return this shape.
/// Note: Uses class instead of record because Meta/Warnings/Metrics/Artifacts are mutable by design.
/// V2-BUG-M06: init + mutable List is intentional — callers accumulate warnings during pipeline phases.
/// </summary>
public sealed class OperationResult
{
    // ── Operation-level status constants ─────────────────────────────
    public const string StatusCompleted = RunConstants.StatusCompleted;
    public const string StatusSkipped = "skipped";
    public const string StatusError = "error";
    public const string StatusContinue = "continue";

    public string Status { get; init; } = RunConstants.StatusOk;
    public string? Reason { get; init; }
    public object? Value { get; init; }
    private readonly Dictionary<string, object> _meta = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];
    private readonly Dictionary<string, double> _metrics = new(StringComparer.Ordinal);
    private readonly List<string> _artifacts = [];

    public IReadOnlyDictionary<string, object> Meta => _meta;
    public IReadOnlyList<string> Warnings => _warnings;
    public IReadOnlyDictionary<string, double> Metrics => _metrics;
    public IReadOnlyList<string> Artifacts => _artifacts;

    public bool ShouldReturn => Status is RunConstants.StatusBlocked or StatusError;
    public string Outcome => Status switch
    {
        StatusSkipped => "SKIP",
        RunConstants.StatusBlocked or StatusError => "ERROR",
        _ => "OK"
    };

    public static OperationResult Ok(string? reason = null, object? value = null)
        => new() { Status = RunConstants.StatusOk, Reason = reason, Value = value };

    public static OperationResult Completed(string? reason = null, object? value = null)
        => new() { Status = StatusCompleted, Reason = reason, Value = value };

    public static OperationResult Skipped(string reason)
        => new() { Status = StatusSkipped, Reason = reason };

    public static OperationResult Blocked(string reason)
        => new() { Status = RunConstants.StatusBlocked, Reason = reason };

    public static OperationResult Error(string reason)
        => new() { Status = StatusError, Reason = reason };

    public OperationResult SetMeta(string key, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _meta[key] = value;
        return this;
    }

    public OperationResult AddWarning(string warning)
    {
        if (!string.IsNullOrWhiteSpace(warning))
            _warnings.Add(warning);
        return this;
    }

    public OperationResult SetMetric(string key, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _metrics[key] = value;
        return this;
    }

    public OperationResult AddArtifact(string artifactPath)
    {
        if (!string.IsNullOrWhiteSpace(artifactPath))
            _artifacts.Add(artifactPath);
        return this;
    }
}
