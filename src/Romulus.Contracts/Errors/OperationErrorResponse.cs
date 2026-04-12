namespace Romulus.Contracts.Errors;

/// <summary>
/// Standard envelope for transporting structured operation errors over API boundaries.
/// </summary>
public sealed record OperationErrorResponse(
    OperationError Error,
    string? RunId = null,
    IDictionary<string, object>? Meta = null)
{
    // RFC 7807 compatibility fields.
    public string Type { get; init; } = "about:blank";
    public string Title { get; init; } = "Operation failed";
    public int Status { get; init; } = 500;
    public string Detail { get; init; } = Error.Message;
    public string? Instance { get; init; }

    public string Utc { get; init; } = DateTime.UtcNow.ToString("o");
    public bool Retryable => Error.Kind == ErrorKind.Transient;
}
