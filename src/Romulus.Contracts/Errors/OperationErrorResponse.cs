namespace Romulus.Contracts.Errors;

/// <summary>
/// Standard envelope for transporting structured operation errors over API boundaries.
/// </summary>
public sealed record OperationErrorResponse(
    OperationError Error,
    string? RunId = null,
    IDictionary<string, object>? Meta = null)
{
    public string Utc { get; init; } = DateTime.UtcNow.ToString("o");
    public bool Retryable => Error.Kind == ErrorKind.Transient;
}