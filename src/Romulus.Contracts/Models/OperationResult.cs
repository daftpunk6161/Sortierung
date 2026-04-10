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
    public const string StatusOk = "ok";
    public const string StatusCompleted = "completed";
    public const string StatusSkipped = "skipped";
    public const string StatusBlocked = "blocked";
    public const string StatusError = "error";
    public const string StatusContinue = "continue";

    public string Status { get; init; } = StatusOk;
    public string? Reason { get; init; }
    public object? Value { get; init; }
    public Dictionary<string, object> Meta { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public Dictionary<string, double> Metrics { get; init; } = new();
    public List<string> Artifacts { get; init; } = new();

    public bool ShouldReturn => Status is StatusBlocked or StatusError;
    public string Outcome => Status switch
    {
        StatusSkipped => "SKIP",
        StatusBlocked or StatusError => "ERROR",
        _ => "OK"
    };

    public static OperationResult Ok(string? reason = null, object? value = null)
        => new() { Status = StatusOk, Reason = reason, Value = value };

    public static OperationResult Completed(string? reason = null, object? value = null)
        => new() { Status = StatusCompleted, Reason = reason, Value = value };

    public static OperationResult Skipped(string reason)
        => new() { Status = StatusSkipped, Reason = reason };

    public static OperationResult Blocked(string reason)
        => new() { Status = StatusBlocked, Reason = reason };

    public static OperationResult Error(string reason)
        => new() { Status = StatusError, Reason = reason };
}
