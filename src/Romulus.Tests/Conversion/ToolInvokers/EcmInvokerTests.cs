using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion.ToolInvokers;
using Xunit;

namespace Romulus.Tests.Conversion.ToolInvokers;

public sealed class EcmInvokerTests : IDisposable
{
    private readonly string _root;
    private readonly string _toolPath;

    public EcmInvokerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Romulus.EcmInvokerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _toolPath = Path.Combine(_root, "unecm.exe");
        File.WriteAllText(_toolPath, "stub");
    }

    [Fact]
    public void CanHandle_ToolNameOrSourceExtension_ReturnsTrue()
    {
        var sut = new EcmInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.True(sut.CanHandle(Capability("unecm", ".bin.ecm")));
        Assert.True(sut.CanHandle(Capability("other", ".ecm")));
    }

    [Fact]
    public void CanHandle_UnrelatedCapability_ReturnsFalse()
    {
        var sut = new EcmInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.False(sut.CanHandle(Capability("chdman", ".iso")));
    }

    [Fact]
    public void Invoke_MissingSource_ReturnsSourceNotFound()
    {
        var runner = new TestToolRunner(new Dictionary<string, string?> { ["unecm"] = _toolPath });
        var sut = new EcmInvoker(runner);

        var result = sut.Invoke(Path.Combine(_root, "missing.ecm"), Path.Combine(_root, "out.bin"), Capability("unecm", ".ecm"));

        Assert.False(result.Success);
        Assert.Equal("source-not-found", result.StdErr);
    }

    [Fact]
    public void Invoke_MissingTool_ReturnsToolNotFound()
    {
        var sourcePath = Path.Combine(_root, "game.bin.ecm");
        File.WriteAllText(sourcePath, "ecm");

        var sut = new EcmInvoker(new TestToolRunner(new Dictionary<string, string?>()));
        var result = sut.Invoke(sourcePath, Path.Combine(_root, "game.bin"), Capability("unecm", ".ecm"));

        Assert.False(result.Success);
        Assert.Equal("tool-not-found:unecm", result.StdErr);
    }

    [Fact]
    public void Invoke_ProcessSuccess_UsesSourceAndTargetArguments()
    {
        var sourcePath = Path.Combine(_root, "game.bin.ecm");
        var targetPath = Path.Combine(_root, "game.bin");
        File.WriteAllText(sourcePath, "ecm");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["unecm"] = _toolPath });
        runner.Enqueue(new ToolResult(0, "ok", true));

        var sut = new EcmInvoker(runner);
        var result = sut.Invoke(sourcePath, targetPath, Capability("unecm", ".ecm"));

        Assert.True(result.Success);
        Assert.NotNull(runner.LastArguments);
        Assert.Equal(sourcePath, runner.LastArguments![0]);
        Assert.Equal(targetPath, runner.LastArguments![1]);
    }

    [Fact]
    public void Verify_ExistingNonEmptyFile_ReturnsVerified()
    {
        var targetPath = Path.Combine(_root, "verified.bin");
        File.WriteAllBytes(targetPath, [1, 2, 3]);

        var sut = new EcmInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.Equal(VerificationStatus.Verified, sut.Verify(targetPath, Capability("unecm", ".ecm")));
    }

    [Fact]
    public void Verify_MissingOrEmptyFile_ReturnsVerifyFailed()
    {
        var missingPath = Path.Combine(_root, "missing.bin");
        var emptyPath = Path.Combine(_root, "empty.bin");
        File.WriteAllBytes(emptyPath, Array.Empty<byte>());

        var sut = new EcmInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.Equal(VerificationStatus.VerifyFailed, sut.Verify(missingPath, Capability("unecm", ".ecm")));
        Assert.Equal(VerificationStatus.VerifyFailed, sut.Verify(emptyPath, Capability("unecm", ".ecm")));
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

    private static ConversionCapability Capability(string toolName, string sourceExtension)
    {
        return new ConversionCapability
        {
            SourceExtension = sourceExtension,
            TargetExtension = ".bin",
            Tool = new ToolRequirement { ToolName = toolName },
            Command = "decode",
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
