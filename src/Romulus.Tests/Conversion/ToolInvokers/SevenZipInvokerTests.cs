using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion.ToolInvokers;
using Xunit;

namespace Romulus.Tests.Conversion.ToolInvokers;

public sealed class SevenZipInvokerTests : IDisposable
{
    private readonly string _root;
    private readonly string _toolPath;

    public SevenZipInvokerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Romulus.SevenZipInvokerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _toolPath = Path.Combine(_root, "7z.exe");
        File.WriteAllText(_toolPath, "stub");
    }

    [Fact]
    public void CanHandle_ZipTarget_ReturnsTrue()
    {
        var sut = new SevenZipInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.True(sut.CanHandle(Cap("7z", ".zip")));
    }

    [Fact]
    public void CanHandle_NonZipTarget_ReturnsFalse()
    {
        var sut = new SevenZipInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        Assert.False(sut.CanHandle(Cap("chdman", ".chd")));
    }

    [Fact]
    public void Invoke_UsesZipArguments()
    {
        var sourcePath = Path.Combine(_root, "game.nes");
        File.WriteAllText(sourcePath, "rom");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["7z"] = _toolPath });
        runner.Enqueue(new ToolResult(0, "ok", true));

        var sut = new SevenZipInvoker(runner);
        var result = sut.Invoke(sourcePath, Path.Combine(_root, "game.zip"), Cap("7z", ".zip"));

        Assert.True(result.Success);
        Assert.Equal("a", runner.LastArguments![0]);
        Assert.Equal("-tzip", runner.LastArguments![1]);
        Assert.Equal("-y", runner.LastArguments![2]);
    }

    [Fact]
    public void Invoke_MissingSource_ReturnsSourceNotFound()
    {
        var runner = new TestToolRunner(new Dictionary<string, string?> { ["7z"] = _toolPath });
        var sut = new SevenZipInvoker(runner);

        var result = sut.Invoke(Path.Combine(_root, "missing.nes"), Path.Combine(_root, "game.zip"), Cap("7z", ".zip"));

        Assert.False(result.Success);
        Assert.Equal("source-not-found", result.StdErr);
    }

    [Fact]
    public void Verify_MissingTool_ReturnsVerifyNotAvailable()
    {
        var targetPath = Path.Combine(_root, "game.zip");
        File.WriteAllText(targetPath, "zip");

        var sut = new SevenZipInvoker(new TestToolRunner(new Dictionary<string, string?>()));

        var status = sut.Verify(targetPath, Cap("7z", ".zip"));

        Assert.Equal(VerificationStatus.VerifyNotAvailable, status);
    }

    [Fact]
    public void Verify_FailedToolRun_ReturnsVerifyFailed()
    {
        var targetPath = Path.Combine(_root, "game.zip");
        File.WriteAllText(targetPath, "zip");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["7z"] = _toolPath });
        runner.Enqueue(new ToolResult(2, "bad", false));
        var sut = new SevenZipInvoker(runner);

        var status = sut.Verify(targetPath, Cap("7z", ".zip"));

        Assert.Equal(VerificationStatus.VerifyFailed, status);
    }

    [Fact]
    public void Verify_Success_ReturnsVerified()
    {
        var targetPath = Path.Combine(_root, "game.zip");
        File.WriteAllText(targetPath, "zip");

        var runner = new TestToolRunner(new Dictionary<string, string?> { ["7z"] = _toolPath });
        runner.Enqueue(new ToolResult(0, "ok", true));
        var sut = new SevenZipInvoker(runner);

        var status = sut.Verify(targetPath, Cap("7z", ".zip"));

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

    private static ConversionCapability Cap(string toolName, string targetExtension)
    {
        return new ConversionCapability
        {
            SourceExtension = ".bin",
            TargetExtension = targetExtension,
            Tool = new ToolRequirement { ToolName = toolName },
            Command = "a",
            ApplicableConsoles = null,
            RequiredSourceIntegrity = null,
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 0,
            Verification = VerificationMethod.SevenZipTest,
            Description = "test",
            Condition = ConversionCondition.None
        };
    }
}
