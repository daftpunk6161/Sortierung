namespace RomCleanup.Contracts.Models;

/// <summary>
/// Standardized operation result contract.
/// Port of New-OperationResult from RunHelpers.Execution.ps1.
/// All pipeline phases return this shape.
/// </summary>
public sealed record OperationResult
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
