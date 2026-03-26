using RomCleanup.Contracts.Ports;

namespace RomCleanup.Tests.Conversion.ToolInvokers;

internal sealed class TestToolRunner : IToolRunner
{
    private readonly Dictionary<string, string?> _toolMap;
    private readonly Queue<ToolResult> _results = new();

    public TestToolRunner(Dictionary<string, string?> toolMap)
    {
        _toolMap = toolMap;
    }

    public string? LastFilePath { get; private set; }
    public string[]? LastArguments { get; private set; }
    public string? LastErrorLabel { get; private set; }

    public void Enqueue(ToolResult result) => _results.Enqueue(result);

    public string? FindTool(string toolName)
        => _toolMap.TryGetValue(toolName, out var path) ? path : null;

    public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
    {
        LastFilePath = filePath;
        LastArguments = arguments;
        LastErrorLabel = errorLabel;

        return _results.Count > 0
            ? _results.Dequeue()
            : new ToolResult(0, string.Empty, true);
    }

    public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
        => InvokeProcess(sevenZipPath, arguments, "7z");
}
