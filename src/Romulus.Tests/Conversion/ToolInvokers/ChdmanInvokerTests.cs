using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion.ToolInvokers;
using Xunit;

namespace Romulus.Tests.Conversion.ToolInvokers;

public sealed class ChdmanInvokerTests : IDisposable
{
    private readonly string _root;
    private readonly string _toolPath;

    public ChdmanInvokerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Romulus.ChdmanInvokerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _toolPath = Path.Combine(_root, "chdman.exe");
        File.WriteAllText(_toolPath, "stub");
    }

    [Fact]
    public void CanHandle_ChdCapability_ReturnsTrue()
    {
        var sut = new ChdmanInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.True(sut.CanHandle(Cap("chdman", ".chd", "createcd")));
    }

    [Fact]
    public void CanHandle_NonChdCapability_ReturnsFalse()
    {
        var sut = new ChdmanInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.False(sut.CanHandle(Cap("7z", ".zip", "a")));
    }

    [Fact]
    public void Invoke_MissingSource_ReturnsSourceNotFound()
    {
        var runner = new TestToolRunner(new Dictionary<string, string?> { ["chdman"] = _toolPath });
        var sut = new ChdmanInvoker(runner);

        var result = sut.Invoke(Path.Combine(_root, "missing.iso"), Path.Combine(_root, "out.chd"), Cap("chdman", ".chd", "createdvd"));

        Assert.False(result.Success);
        Assert.Equal("source-not-found", result.StdErr);
    }

    [Fact]
    public void Invoke_InvalidCommand_ReturnsInvalidCommand()
    {
        var sourcePath = Path.Combine(_root, "game.iso");
        File.WriteAllText(sourcePath, "iso");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["chdman"] = _toolPath });
        var sut = new ChdmanInvoker(runner);

        var result = sut.Invoke(sourcePath, Path.Combine(_root, "game.chd"), Cap("chdman", ".chd", "../bad"));

        Assert.False(result.Success);
        Assert.Equal("invalid-command", result.StdErr);
    }

    [Fact]
    public void Invoke_CreatedvdWithSmallIso_DowngradesToCreatecd()
    {
        var sourcePath = Path.Combine(_root, "small.iso");
        File.WriteAllText(sourcePath, "iso");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["chdman"] = _toolPath });
        runner.Enqueue(new ToolResult(0, "ok", true));

        var sut = new ChdmanInvoker(runner);
        var result = sut.Invoke(sourcePath, Path.Combine(_root, "small.chd"), Cap("chdman", ".chd", "createdvd"));

        Assert.True(result.Success);
        Assert.NotNull(runner.LastArguments);
        Assert.Equal("createcd", runner.LastArguments![0]);
    }

    [Fact]
    public void Verify_ToolMissing_ReturnsVerifyNotAvailable()
    {
        var targetPath = Path.Combine(_root, "game.chd");
        File.WriteAllText(targetPath, "data");

        var sut = new ChdmanInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        var status = sut.Verify(targetPath, Cap("chdman", ".chd", "createcd"));

        Assert.Equal(VerificationStatus.VerifyNotAvailable, status);
    }

    [Fact]
    public void Verify_ToolSuccess_ReturnsVerified()
    {
        var targetPath = Path.Combine(_root, "game.chd");
        File.WriteAllText(targetPath, "data");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["chdman"] = _toolPath });
        runner.Enqueue(new ToolResult(0, "ok", true));
        var sut = new ChdmanInvoker(runner);

        var status = sut.Verify(targetPath, Cap("chdman", ".chd", "createcd"));

        Assert.Equal(VerificationStatus.Verified, status);
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
            Verification = VerificationMethod.ChdmanVerify,
            Description = "test",
            Condition = ConversionCondition.None
        };
    }
}
