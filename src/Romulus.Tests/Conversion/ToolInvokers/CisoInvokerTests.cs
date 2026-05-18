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
    public void CanHandle_UnrelatedCapability_ReturnsFalse()
    {
        var sut = new CisoInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.False(sut.CanHandle(Capability("chdman", ".iso")));
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
    public void Invoke_InvalidCommand_ReturnsInvalidCommand()
    {
        var sourcePath = Path.Combine(_root, "game.cso");
        File.WriteAllText(sourcePath, "cso");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["ciso"] = _toolPath });
        var sut = new CisoInvoker(runner);

        var result = sut.Invoke(sourcePath, Path.Combine(_root, "game.iso"), Capability("ciso", ".cso", command: "../decompress"));

        Assert.False(result.Success);
        Assert.Equal("invalid-command", result.StdErr);
        Assert.Null(runner.LastArguments);
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
    public void Invoke_MaxcsoExecutable_UsesMaxcsoDecompressArguments()
    {
        var sourcePath = Path.Combine(_root, "game.cso");
        var targetPath = Path.Combine(_root, "out", "game.iso");
        var maxcsoPath = Path.Combine(_root, "maxcso.exe");
        File.WriteAllText(sourcePath, "cso");
        File.WriteAllText(maxcsoPath, "stub");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["ciso"] = maxcsoPath });
        runner.Enqueue(new ToolResult(0, "ok", true));

        var sut = new CisoInvoker(runner);
        var result = sut.Invoke(sourcePath, targetPath, Capability("ciso", ".cso"));

        Assert.True(result.Success);
        Assert.True(Directory.Exists(Path.GetDirectoryName(targetPath)));
        Assert.NotNull(runner.LastArguments);
        Assert.Equal("--decompress", runner.LastArguments![0]);
        Assert.Equal(sourcePath, runner.LastArguments[1]);
        Assert.Equal("-o", runner.LastArguments[2]);
        Assert.Equal(targetPath, runner.LastArguments[3]);
    }

    [Fact]
    public void Invoke_ProcessFailure_ReturnsToolOutputWithoutVerification()
    {
        var sourcePath = Path.Combine(_root, "failed.cso");
        var targetPath = Path.Combine(_root, "failed.iso");
        File.WriteAllText(sourcePath, "cso");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["ciso"] = _toolPath });
        runner.Enqueue(new ToolResult(3, "disk-full", false));

        var sut = new CisoInvoker(runner);
        var result = sut.Invoke(sourcePath, targetPath, Capability("ciso", ".cso"));

        Assert.False(result.Success);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("disk-full", result.StdErr);
        Assert.Equal(VerificationStatus.NotAttempted, result.Verification);
    }

    [Fact]
    public void Verify_RejectsOutputThatStillLooksLikeCso()
    {
        var targetPath = Path.Combine(_root, "bad.iso");
        File.WriteAllBytes(targetPath, [(byte)'C', (byte)'I', (byte)'S', (byte)'O', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        var sut = new CisoInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.Equal(VerificationStatus.VerifyFailed, sut.Verify(targetPath, Capability("ciso", ".cso")));
    }

    [Fact]
    public void Verify_MissingShortAndPlainIsoOutputs_ReturnExpectedStatus()
    {
        var missingPath = Path.Combine(_root, "missing.iso");
        var shortPath = Path.Combine(_root, "short.iso");
        var plainIsoPath = Path.Combine(_root, "plain.iso");
        File.WriteAllBytes(shortPath, [1, 2, 3, 4]);
        File.WriteAllBytes(plainIsoPath, Enumerable.Range(0, 16).Select(i => (byte)(i + 1)).ToArray());

        var sut = new CisoInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.Equal(VerificationStatus.VerifyFailed, sut.Verify(missingPath, Capability("ciso", ".cso")));
        Assert.Equal(VerificationStatus.VerifyFailed, sut.Verify(shortPath, Capability("ciso", ".cso")));
        Assert.Equal(VerificationStatus.Verified, sut.Verify(plainIsoPath, Capability("ciso", ".cso")));
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

    private static ConversionCapability Capability(string toolName, string sourceExtension, string command = "decompress")
    {
        return new ConversionCapability
        {
            SourceExtension = sourceExtension,
            TargetExtension = ".iso",
            Tool = new ToolRequirement { ToolName = toolName },
            Command = command,
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
