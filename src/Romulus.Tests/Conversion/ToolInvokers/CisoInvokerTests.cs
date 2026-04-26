using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion.ToolInvokers;
using Xunit;

namespace Romulus.Tests.Conversion.ToolInvokers;

public sealed class CisoInvokerTests : IDisposable
{
    private readonly string _root;
    private readonly string _toolPath;

    public CisoInvokerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Romulus.CisoInvokerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _toolPath = Path.Combine(_root, "ciso.exe");
        File.WriteAllText(_toolPath, "stub");
    }

    [Fact]
    public void CanHandle_ToolNameOrSourceExtension_ReturnsTrue()
    {
        var sut = new CisoInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.True(sut.CanHandle(Capability("ciso", ".cso")));
        Assert.True(sut.CanHandle(Capability("other", ".cso")));
    }

    [Fact]
    public void Invoke_MissingSource_ReturnsSourceNotFound()
    {
        var runner = new TestToolRunner(new Dictionary<string, string?> { ["ciso"] = _toolPath });
        var sut = new CisoInvoker(runner);

        var result = sut.Invoke(Path.Combine(_root, "missing.cso"), Path.Combine(_root, "out.iso"), Capability("ciso", ".cso"));

        Assert.False(result.Success);
        Assert.Equal("source-not-found", result.StdErr);
    }

    [Fact]
    public void Invoke_MissingTool_ReturnsToolNotFound()
    {
        var sourcePath = Path.Combine(_root, "game.cso");
        File.WriteAllText(sourcePath, "cso");

        var sut = new CisoInvoker(new TestToolRunner(new Dictionary<string, string?>()));
        var result = sut.Invoke(sourcePath, Path.Combine(_root, "game.iso"), Capability("ciso", ".cso"));

        Assert.False(result.Success);
        Assert.Equal("tool-not-found:ciso", result.StdErr);
    }

    [Fact]
    public void Invoke_ProcessSuccess_UsesDecompressArguments()
    {
        var sourcePath = Path.Combine(_root, "game.cso");
        var targetPath = Path.Combine(_root, "game.iso");
        File.WriteAllText(sourcePath, "cso");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["ciso"] = _toolPath });
        runner.Enqueue(new ToolResult(0, "ok", true));

        var sut = new CisoInvoker(runner);
        var result = sut.Invoke(sourcePath, targetPath, Capability("ciso", ".cso"));

        Assert.True(result.Success);
        Assert.NotNull(runner.LastArguments);
        Assert.Equal("0", runner.LastArguments![0]);
        Assert.Equal(sourcePath, runner.LastArguments[1]);
        Assert.Equal(targetPath, runner.LastArguments[2]);
    }

    [Fact]
    public void Verify_RejectsOutputThatStillLooksLikeCso()
    {
        var targetPath = Path.Combine(_root, "bad.iso");
        File.WriteAllBytes(targetPath, [(byte)'C', (byte)'I', (byte)'S', (byte)'O', 0, 0, 0, 0]);

        var sut = new CisoInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.Equal(VerificationStatus.VerifyFailed, sut.Verify(targetPath, Capability("ciso", ".cso")));
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
            TargetExtension = ".iso",
            Tool = new ToolRequirement { ToolName = toolName },
            Command = "decompress",
            ApplicableConsoles = null,
            RequiredSourceIntegrity = null,
            ResultIntegrity = SourceIntegrity.Lossy,
            Lossless = false,
            Cost = 0,
            Verification = VerificationMethod.FileExistenceCheck,
            Description = "test",
            Condition = ConversionCondition.None
        };
    }
}
