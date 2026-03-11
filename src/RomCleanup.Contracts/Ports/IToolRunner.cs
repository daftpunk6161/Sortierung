namespace RomCleanup.Contracts.Ports;

/// <summary>
/// Port interface for external tool execution (chdman, 7z, dolphintool).
/// Maps to New-ToolRunnerPort in PortInterfaces.ps1.
/// </summary>
public interface IToolRunner
{
    string? FindTool(string toolName);
    ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null);
    ToolResult Invoke7z(string sevenZipPath, string[] arguments);
}

public sealed record ToolResult(int ExitCode, string Output, bool Success);
