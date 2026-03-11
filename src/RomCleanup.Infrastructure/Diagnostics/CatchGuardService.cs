using RomCleanup.Contracts.Errors;
using RomCleanup.Contracts.Models;

namespace RomCleanup.Infrastructure.Diagnostics;

/// <summary>
/// CatchGuard service: structured error handling with audit trail records.
/// Port of CatchGuard.ps1. Extends ErrorClassifier with logging + record creation.
/// </summary>
public sealed class CatchGuardService
{
    private readonly Action<string>? _log;

    public CatchGuardService(Action<string>? log = null)
    {
        _log = log;
    }

    /// <summary>
    /// Create a structured CatchGuard record from an exception context.
    /// </summary>
    public static CatchGuardRecord CreateRecord(
        string module,
        string action,
        string root = "",
        string operationId = "",
        Exception? exception = null,
        string message = "",
        string errorCode = "",
        ErrorKind? errorClass = null)
    {
        var resolvedClass = errorClass ?? ErrorClassifier.Classify(exception, errorCode);

        return new CatchGuardRecord
        {
            TimestampUtc = DateTime.UtcNow,
            ErrorClass = resolvedClass,
            Module = module,
            OperationId = operationId,
            Root = root,
            Action = action,
            ErrorCode = errorCode,
            ExceptionType = exception?.GetType().Name ?? "",
            Message = string.IsNullOrWhiteSpace(message) ? exception?.Message ?? "" : message
        };
    }

    /// <summary>
    /// Map an ErrorKind to a severity string for logging.
    /// </summary>
    public static string ToSeverity(ErrorKind errorClass) => errorClass switch
    {
        ErrorKind.Critical => "Critical",
        ErrorKind.Transient => "Warning",
        _ => "Error"
    };

    /// <summary>
    /// Log a catch guard record and return it. Equivalent to Write-CatchGuardLog.
    /// </summary>
    public CatchGuardRecord LogAndCreate(
        string module,
        string action,
        string root = "",
        Exception? exception = null,
        string message = "",
        string errorCode = "",
        string operationId = "",
        string level = "Error")
    {
        var record = CreateRecord(module, action, root, operationId, exception, message, errorCode);

        var logMessage = $"[{level}] [{record.ErrorClass}] {module}.{action}: {record.Message}";
        if (!string.IsNullOrWhiteSpace(errorCode))
            logMessage += $" (Code: {errorCode})";
        if (!string.IsNullOrWhiteSpace(record.ExceptionType))
            logMessage += $" [{record.ExceptionType}]";

        _log?.Invoke(logMessage);

        return record;
    }

    /// <summary>
    /// Safe catch handler: classify, log, and optionally re-throw for Critical errors.
    /// Equivalent to Invoke-SafeCatch.
    /// </summary>
    public CatchGuardRecord SafeCatch(
        string module,
        string action,
        Exception exception,
        string level = "Warning",
        string operationId = "",
        string root = "")
    {
        var record = LogAndCreate(module, action, root, exception, operationId: operationId, level: level);

        // Critical errors should be re-thrown by the caller; we only log here
        return record;
    }

    /// <summary>
    /// Execute an action with automatic catch guard error handling.
    /// Returns the CatchGuardRecord if an error occurred, null otherwise.
    /// </summary>
    public CatchGuardRecord? Guard(
        string module,
        string action,
        Action work,
        string root = "",
        string operationId = "")
    {
        try
        {
            work();
            return null;
        }
        catch (OperationCanceledException)
        {
            throw; // never swallow cancellation
        }
        catch (Exception ex)
        {
            var record = SafeCatch(module, action, ex, operationId: operationId, root: root);

            if (record.ErrorClass == ErrorKind.Critical)
                throw; // re-throw critical

            return record;
        }
    }

    /// <summary>
    /// Execute a function with automatic catch guard error handling.
    /// Returns (result, null) on success, (default, record) on error.
    /// </summary>
    public (T? Result, CatchGuardRecord? Error) Guard<T>(
        string module,
        string action,
        Func<T> work,
        string root = "",
        string operationId = "")
    {
        try
        {
            return (work(), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var record = SafeCatch(module, action, ex, operationId: operationId, root: root);

            if (record.ErrorClass == ErrorKind.Critical)
                throw;

            return (default, record);
        }
    }
}
