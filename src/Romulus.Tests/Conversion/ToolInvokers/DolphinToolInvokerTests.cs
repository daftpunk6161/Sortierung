using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion.ToolInvokers;
using Xunit;

namespace Romulus.Tests.Conversion.ToolInvokers;

public sealed class DolphinToolInvokerTests : IDisposable
{
    private readonly string _root;
    private readonly string _toolPath;

    public DolphinToolInvokerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Romulus.DolphinInvokerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _toolPath = Path.Combine(_root, "dolphintool.exe");
        File.WriteAllText(_toolPath, "stub");
    }

    [Fact]
    public void CanHandle_RvzTarget_ReturnsTrue()
    {
        var sut = new DolphinToolInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.True(sut.CanHandle(Cap("dolphintool", ".rvz", "convert")));
    }

    [Fact]
    public void CanHandle_OtherTool_ReturnsFalse()
    {
        var sut = new DolphinToolInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.False(sut.CanHandle(Cap("chdman", ".chd", "createcd")));
    }

    [Fact]
    public void Invoke_UsesExpectedArguments()
    {
        var sourcePath = Path.Combine(_root, "game.iso");
        File.WriteAllText(sourcePath, "iso");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["dolphintool"] = _toolPath });
        runner.Enqueue(new ToolResult(0, "ok", true));

        var sut = new DolphinToolInvoker(runner);
        var result = sut.Invoke(sourcePath, Path.Combine(_root, "game.rvz"), Cap("dolphintool", ".rvz", "convert"));

        Assert.True(result.Success);
        Assert.Equal("convert", runner.LastArguments![0]);
        Assert.Contains("-f", runner.LastArguments!);
        Assert.Contains("rvz", runner.LastArguments!);
    }

    [Fact]
    public void Invoke_InvalidCommand_ReturnsInvalidCommand()
    {
        var sourcePath = Path.Combine(_root, "game.iso");
        File.WriteAllText(sourcePath, "iso");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["dolphintool"] = _toolPath });
        var sut = new DolphinToolInvoker(runner);

        var result = sut.Invoke(sourcePath, Path.Combine(_root, "game.rvz"), Cap("dolphintool", ".rvz", "..\\convert"));

        Assert.False(result.Success);
        Assert.Equal("invalid-command", result.StdErr);
    }

    [Fact]
    public void Verify_ValidMagic_ReturnsVerified()
    {
        var targetPath = Path.Combine(_root, "game.rvz");
        File.WriteAllBytes(targetPath, new byte[] { (byte)'R', (byte)'V', (byte)'Z', 0x01, 0x99 });

        var sut = new DolphinToolInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        var status = sut.Verify(targetPath, Cap("dolphintool", ".rvz", "convert"));

        Assert.Equal(VerificationStatus.Verified, status);
    }

    [Fact]
    public void Verify_InvalidMagic_ReturnsVerifyFailed()
    {
        var targetPath = Path.Combine(_root, "game.rvz");
        File.WriteAllBytes(targetPath, new byte[] { 0x01, 0x02, 0x03, 0x04 });

        var sut = new DolphinToolInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        var status = sut.Verify(targetPath, Cap("dolphintool", ".rvz", "convert"));

        Assert.Equal(VerificationStatus.VerifyFailed, status);
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

    private static ConversionCapability Cap(string toolName, string targetExtension, string command)
    {
        return new ConversionCapability
        {
            SourceExtension = ".iso",
            TargetExtension = targetExtension,
            Tool = new ToolRequirement { ToolName = toolName },
            Command = command,
            ApplicableConsoles = null,
            RequiredSourceIntegrity = null,
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 0,
            Verification = VerificationMethod.RvzMagicByte,
            Description = "test",
            Condition = ConversionCondition.None
        };
    }
}
