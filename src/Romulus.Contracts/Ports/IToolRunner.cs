namespace Romulus.Contracts.Ports;

/// <summary>
/// Port interface for external tool execution (chdman, 7z, dolphintool).
/// Maps to New-ToolRunnerPort in PortInterfaces.ps1.
/// </summary>
public interface IToolRunner
{
    string? FindTool(string toolName);
    ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null);
    ToolResult InvokeProcess(
        string filePath,
        string[] arguments,
        Romulus.Contracts.Models.ToolRequirement? requirement,
        string? errorLabel = null)
        => InvokeProcess(
            filePath,
            arguments,
            requirement,
            errorLabel,
            timeout: null,
            cancellationToken: CancellationToken.None);

    ToolResult InvokeProcess(
        string filePath,
        string[] arguments,
        string? errorLabel,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => InvokeProcess(
            filePath,
            arguments,
            requirement: null,
            errorLabel,
            timeout,
            cancellationToken);

    ToolResult InvokeProcess(
        string filePath,
        string[] arguments,
        Romulus.Contracts.Models.ToolRequirement? requirement,
        string? errorLabel,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        // Default fallback for lightweight test doubles that only implement
        // the 3-argument signature. Do not silently drop advanced semantics.
        var hasRequirement = requirement is not null;
        var hasTimeout = timeout is not null;
        var hasCancellation = cancellationToken.CanBeCanceled;

        if (hasRequirement || hasTimeout || hasCancellation)
        {
            return new ToolResult(
                -1,
                "Tool invocation requires advanced overload support (requirement/timeout/cancellation), but current IToolRunner implementation exposes only the basic InvokeProcess signature.",
                false);
        }

        return InvokeProcess(filePath, arguments, errorLabel);
    }
    ToolResult Invoke7z(string sevenZipPath, string[] arguments);
}

public sealed record ToolResult(int ExitCode, string Output, bool Success);
