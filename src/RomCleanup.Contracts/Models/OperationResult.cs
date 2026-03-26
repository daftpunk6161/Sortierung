namespace RomCleanup.Contracts.Models;

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
        RunOutcome.Ok => "ok",
        RunOutcome.CompletedWithErrors => "completed_with_errors",
        RunOutcome.Blocked => "blocked",
        RunOutcome.Cancelled => "cancelled",
        RunOutcome.Failed => "failed",
        _ => "failed"
    };

    public static RunOutcome ParseRunOutcome(string? status) => status switch
    {
        "ok" => RunOutcome.Ok,
        "completed_with_errors" => RunOutcome.CompletedWithErrors,
        "blocked" => RunOutcome.Blocked,
        "cancelled" => RunOutcome.Cancelled,
        "failed" => RunOutcome.Failed,
        _ => RunOutcome.Failed
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
    public string Status { get; init; } = "ok"; // ok | completed | skipped | blocked | error | continue
    public string? Reason { get; init; }
    public object? Value { get; init; }
    public Dictionary<string, object> Meta { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public Dictionary<string, double> Metrics { get; init; } = new();
    public List<string> Artifacts { get; init; } = new();

    public bool ShouldReturn => Status is "blocked" or "error";
    public string Outcome => Status switch
    {
        "skipped" => "SKIP",
        "blocked" or "error" => "ERROR",
        _ => "OK"
    };

    public static OperationResult Ok(string? reason = null, object? value = null)
        => new() { Status = "ok", Reason = reason, Value = value };

    public static OperationResult Completed(string? reason = null, object? value = null)
        => new() { Status = "completed", Reason = reason, Value = value };

    public static OperationResult Skipped(string reason)
        => new() { Status = "skipped", Reason = reason };

    public static OperationResult Blocked(string reason)
        => new() { Status = "blocked", Reason = reason };

    public static OperationResult Error(string reason)
        => new() { Status = "error", Reason = reason };
}
