namespace RomCleanup.Contracts.Errors;

/// <summary>
/// Classifies exceptions and error codes into ErrorKind categories.
/// Port of Resolve-CatchErrorClass from CatchGuard.ps1.
/// </summary>
public static class ErrorClassifier
{
    /// <summary>
    /// Resolve the error class from an exception and/or error code.
    /// Error code prefixes take precedence over exception type.
    /// </summary>
    public static ErrorKind Classify(Exception? exception = null, string? errorCode = null,
                                      ErrorKind defaultKind = ErrorKind.Recoverable)
    {
        // Error code prefix rules (highest priority)
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            var code = errorCode.ToUpperInvariant();
            if (code.StartsWith("SEC-") || code.StartsWith("AUTH-"))
                return ErrorKind.Critical;
            if (code.StartsWith("IO-LOCK") || code.StartsWith("NET-"))
                return ErrorKind.Transient;
        }

        if (exception is null)
            return defaultKind;

        // Transient: retry-eligible
        if (exception is TimeoutException
            or System.Net.WebException
            or IOException
            or OperationCanceledException)
            return ErrorKind.Transient;

        // Critical: abort immediately
        if (exception is UnauthorizedAccessException
            or System.Security.SecurityException
            or OutOfMemoryException)
            return ErrorKind.Critical;

        return defaultKind;
    }

    /// <summary>
    /// Create an OperationError from an exception with automatic classification.
    /// </summary>
    public static OperationError FromException(Exception ex, string module, string? errorCode = null)
    {
        var kind = Classify(ex, errorCode);
        var code = errorCode ?? $"RUN-{ex.GetType().Name}";
        return new OperationError(code, ex.Message, kind, module, ex);
    }
}
