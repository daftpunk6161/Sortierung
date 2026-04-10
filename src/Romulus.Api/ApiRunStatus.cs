namespace Romulus.Api;

internal static class ApiRunStatus
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string CompletedWithErrors = "completed_with_errors";
    public const string Blocked = "blocked";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}
