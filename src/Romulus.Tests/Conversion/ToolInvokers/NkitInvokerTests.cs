using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion.ToolInvokers;
using Xunit;

namespace Romulus.Tests.Conversion.ToolInvokers;

public sealed class NkitInvokerTests : IDisposable
{
    private readonly string _root;
    private readonly string _toolPath;

    public NkitInvokerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Romulus.NkitInvokerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _toolPath = Path.Combine(_root, "nkit.exe");
        File.WriteAllText(_toolPath, "stub");
    }

    [Fact]
    public void CanHandle_ToolNameOrNkitExtension_ReturnsTrue()
    {
        var sut = new NkitInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.True(sut.CanHandle(Capability("nkit", ".iso")));
        Assert.True(sut.CanHandle(Capability("other", ".nkit.iso")));
    }

    [Fact]
    public void CanHandle_UnrelatedCapability_ReturnsFalse()
    {
        var sut = new NkitInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.False(sut.CanHandle(Capability("chdman", ".iso")));
    }

    [Fact]
    public void Invoke_MissingSource_ReturnsSourceNotFound()
    {
        var runner = new TestToolRunner(new Dictionary<string, string?> { ["nkit"] = _toolPath });
        var sut = new NkitInvoker(runner);

        var result = sut.Invoke(Path.Combine(_root, "missing.nkit.iso"), Path.Combine(_root, "out.iso"), Capability("nkit", ".nkit.iso"));

        Assert.False(result.Success);
        Assert.Equal("source-not-found", result.StdErr);
    }

    [Fact]
    public void Invoke_InvalidCommand_ReturnsInvalidCommand()
    {
        var sourcePath = Path.Combine(_root, "game.nkit.iso");
        File.WriteAllText(sourcePath, "nkit");

        var sut = new NkitInvoker(new TestToolRunner(new Dictionary<string, string?> { ["nkit"] = _toolPath }));
        var result = sut.Invoke(sourcePath, Path.Combine(_root, "out.iso"), Capability("nkit", ".nkit.iso", command: "../expand"));

        Assert.False(result.Success);
        Assert.Equal("invalid-command", result.StdErr);
    }

    [Fact]
    public void Invoke_MissingTool_ReturnsToolNotFound()
    {
        var sourcePath = Path.Combine(_root, "game.nkit.iso");
        File.WriteAllText(sourcePath, "nkit");

        var sut = new NkitInvoker(new TestToolRunner(new Dictionary<string, string?>()));
        var result = sut.Invoke(sourcePath, Path.Combine(_root, "out.iso"), Capability("nkit", ".nkit.iso"));

        Assert.False(result.Success);
        Assert.Equal("tool-not-found:nkit", result.StdErr);
    }

    [Fact]
    public void Invoke_ProcessFailure_ReturnsFailure()
    {
        var sourcePath = Path.Combine(_root, "game.nkit.iso");
        var targetPath = Path.Combine(_root, "out", "game.iso");
        File.WriteAllText(sourcePath, "nkit");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["nkit"] = _toolPath });
        runner.Enqueue(new ToolResult(2, "bad", false));

        var sut = new NkitInvoker(runner);
        var result = sut.Invoke(sourcePath, targetPath, Capability("nkit", ".nkit.iso"));

        Assert.False(result.Success);
        Assert.Equal(2, result.ExitCode);
        Assert.Equal(VerificationStatus.NotAttempted, result.Verification);
    }

    [Fact]
    public void Invoke_SuccessButMissingOutput_ReturnsVerifyFailed()
    {
        var sourcePath = Path.Combine(_root, "game.nkit.iso");
        var targetPath = Path.Combine(_root, "out", "game.iso");
        File.WriteAllText(sourcePath, "nkit");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["nkit"] = _toolPath });
        runner.Enqueue(new ToolResult(0, "ok", true));

        var sut = new NkitInvoker(runner);
        var result = sut.Invoke(sourcePath, targetPath, Capability("nkit", ".nkit.iso"));

        Assert.False(result.Success);
        Assert.Equal("nkit-output-not-found", result.StdErr);
        Assert.Equal(VerificationStatus.VerifyFailed, result.Verification);
    }

    [Fact]
    public void Invoke_SuccessWithOutput_ReturnsVerifiedAndUsesExpectedArgs()
    {
        var sourcePath = Path.Combine(_root, "game.nkit.iso");
        var targetPath = Path.Combine(_root, "out", "game.iso");
        File.WriteAllText(sourcePath, "nkit");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["nkit"] = _toolPath });
        runner.Enqueue(new ToolResult(0, "ok", true));

        var sut = new NkitInvoker(runner);

        var targetDir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDir);
        File.WriteAllText(targetPath, "expanded");

        var result = sut.Invoke(sourcePath, targetPath, Capability("nkit", ".nkit.iso"));

        Assert.True(result.Success);
        Assert.Equal(VerificationStatus.Verified, result.Verification);
        Assert.NotNull(runner.LastArguments);
        Assert.Equal("-task", runner.LastArguments![0]);
        Assert.Equal("expand", runner.LastArguments![1]);
    }

    [Fact]
    public void Verify_ExistingNonEmptyFile_ReturnsVerified()
    {
        var targetPath = Path.Combine(_root, "verified.iso");
        File.WriteAllBytes(targetPath, [1, 2, 3]);

        var sut = new NkitInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.Equal(VerificationStatus.Verified, sut.Verify(targetPath, Capability("nkit", ".nkit.iso")));
    }

    [Fact]
    public void Verify_MissingOrEmptyFile_ReturnsVerifyFailed()
    {
        var missingPath = Path.Combine(_root, "missing.iso");
        var emptyPath = Path.Combine(_root, "empty.iso");
        File.WriteAllBytes(emptyPath, Array.Empty<byte>());

        var sut = new NkitInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.Equal(VerificationStatus.VerifyFailed, sut.Verify(missingPath, Capability("nkit", ".nkit.iso")));
        Assert.Equal(VerificationStatus.VerifyFailed, sut.Verify(emptyPath, Capability("nkit", ".nkit.iso")));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private static ConversionCapability Capability(string toolName, string sourceExtension, string command = "expand")
    {
        return new ConversionCapability
        {
            SourceExtension = sourceExtension,
            TargetExtension = ".iso",
            Tool = new ToolRequirement { ToolName = toolName },
            Command = command,
            ApplicableConsoles = null,
            RequiredSourceIntegrity = null,
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 0,
            Verification = VerificationMethod.FileExistenceCheck,
            Description = "test",
            Condition = ConversionCondition.None
        };
    }
}
