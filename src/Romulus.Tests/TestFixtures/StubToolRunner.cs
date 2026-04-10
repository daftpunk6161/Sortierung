using Romulus.Contracts.Ports;

namespace Romulus.Tests.TestFixtures;

/// <summary>
/// Stub IToolRunner for unit testing without real tool executables.
/// </summary>
internal sealed class StubToolRunner : IToolRunner
{
    private readonly Dictionary<string, string> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolResult> _results = new(StringComparer.OrdinalIgnoreCase);

    public ToolResult DefaultResult { get; set; } = new(0, "", true);

    public void RegisterTool(string toolName, string path) => _tools[toolName] = path;
    public void RegisterResult(string filePath, ToolResult result) => _results[filePath] = result;

    public string? FindTool(string toolName) => _tools.TryGetValue(toolName, out var p) ? p : null;

    public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
        => _results.TryGetValue(filePath, out var r) ? r : DefaultResult;

    public ToolResult Invoke7z(string sevenZipPath, string[] arguments) => DefaultResult;
}
