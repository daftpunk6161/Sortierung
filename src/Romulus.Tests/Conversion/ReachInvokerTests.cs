using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion.ToolInvokers;
using Xunit;

namespace Romulus.Tests.Conversion;

public sealed class ReachInvokerTests : IDisposable
{
    private readonly string _tempDir;

    public ReachInvokerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ReachInvokers_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void EcmInvoker_InvokesUnecm_WithSourceAndTarget()
    {
        var sourcePath = CreateFile("disc.ecm");
        var targetPath = Path.Combine(_tempDir, "disc.bin");
        var runner = new CapturingToolRunner(CreateToolFile("unecm.exe"), targetPath);
        var invoker = new EcmInvoker(runner);

        var capability = new ConversionCapability
        {
            SourceExtension = ".ecm",
            TargetExtension = ".bin",
            Tool = new ToolRequirement { ToolName = "unecm" },
            Command = "decompress",
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 1,
            Verification = VerificationMethod.FileExistenceCheck
        };

        var result = invoker.Invoke(sourcePath, targetPath, capability);

        Assert.True(result.Success);
        Assert.Equal(new[] { sourcePath, targetPath }, runner.LastArguments);
    }

    [Fact]
    public void NkitInvoker_InvokesProcessingApp_WithExpandAndVerify()
    {
        var sourcePath = CreateFile("disc.nkit.iso");
        var targetPath = Path.Combine(_tempDir, "disc.iso");
        var runner = new CapturingToolRunner(CreateToolFile("NKitProcessingApp.exe"), targetPath);
        var invoker = new NkitInvoker(runner);

        var capability = new ConversionCapability
        {
            SourceExtension = ".nkit.iso",
            TargetExtension = ".iso",
            Tool = new ToolRequirement { ToolName = "nkit" },
            Command = "expand",
            ResultIntegrity = SourceIntegrity.Lossy,
            Lossless = false,
            Cost = 1,
            Verification = VerificationMethod.FileExistenceCheck
        };

        var result = invoker.Invoke(sourcePath, targetPath, capability);

        Assert.True(result.Success);
        Assert.Equal("-task", runner.LastArguments[0]);
        Assert.Contains("-verify", runner.LastArguments);
        Assert.Contains(sourcePath, runner.LastArguments);
        Assert.Contains(Path.GetDirectoryName(targetPath)!, runner.LastArguments);
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "data");
        return path;
    }

    private string CreateToolFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "tool");
        return path;
    }

    private sealed class CapturingToolRunner : IToolRunner
    {
        private readonly string _toolPath;
        private readonly string _expectedTargetPath;

        public CapturingToolRunner(string toolPath, string expectedTargetPath)
        {
            _toolPath = toolPath;
            _expectedTargetPath = expectedTargetPath;
        }

        public string[] LastArguments { get; private set; } = Array.Empty<string>();

        public string? FindTool(string toolName) => _toolPath;

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
        {
            LastArguments = arguments;
            Directory.CreateDirectory(Path.GetDirectoryName(_expectedTargetPath)!);
            File.WriteAllText(_expectedTargetPath, "converted");
            return new ToolResult(0, "ok", true);
        }

        public ToolResult InvokeProcess(
            string filePath, string[] arguments,
            Romulus.Contracts.Models.ToolRequirement? requirement,
            string? errorLabel, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return InvokeProcess(filePath, arguments, errorLabel);
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments) => new(0, "ok", true);
    }
}
