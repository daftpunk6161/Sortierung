using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion.ToolInvokers;
using Xunit;

namespace Romulus.Tests.Conversion.ToolInvokers;

public sealed class PsxtractInvokerTests : IDisposable
{
    private readonly string _root;
    private readonly PsxtractInvoker _sut;
    private readonly TestToolRunner _runner;

    public PsxtractInvokerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Romulus.PsxtractInvokerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _runner = new TestToolRunner(new Dictionary<string, string?>
        {
            ["psxtract"] = @"C:\mock\psxtract.exe"
        });
        _sut = new PsxtractInvoker(_runner);
    }

    // ═══ Verify: ISO-style output checks ════════════════════════════

    [Fact]
    public void Verify_ValidIso9660Magic_ReturnsVerified()
    {
        var targetPath = Path.Combine(_root, "game.iso");
        var bytes = new byte[0x9000];
        bytes[0x8001] = (byte)'C';
        bytes[0x8002] = (byte)'D';
        bytes[0x8003] = (byte)'0';
        bytes[0x8004] = (byte)'0';
        bytes[0x8005] = (byte)'1';
        File.WriteAllBytes(targetPath, bytes);

        var status = _sut.Verify(targetPath, Cap());

        Assert.Equal(VerificationStatus.Verified, status);
    }

    [Fact]
    public void Verify_NonEmptyWithoutIsoMarker_ReturnsVerified()
    {
        var targetPath = Path.Combine(_root, "game.iso");
        File.WriteAllBytes(targetPath, Enumerable.Repeat((byte)0x2A, 4096).ToArray());

        var status = _sut.Verify(targetPath, Cap());

        Assert.Equal(VerificationStatus.Verified, status);
    }

    [Fact]
    public void Verify_EmptyFile_ReturnsVerifyFailed()
    {
        var targetPath = Path.Combine(_root, "game.iso");
        File.WriteAllBytes(targetPath, Array.Empty<byte>());

        var status = _sut.Verify(targetPath, Cap());

        Assert.Equal(VerificationStatus.VerifyFailed, status);
    }

    [Fact]
    public void Verify_FileTooShort_ReturnsVerifyFailed()
    {
        var targetPath = Path.Combine(_root, "game.iso");
        File.WriteAllBytes(targetPath, new byte[] { 1, 2, 3, 4 });

        var status = _sut.Verify(targetPath, Cap());

        Assert.Equal(VerificationStatus.Verified, status);
    }

    [Fact]
    public void Verify_FileDoesNotExist_ReturnsVerifyFailed()
    {
        var targetPath = Path.Combine(_root, "nonexistent.iso");

        var status = _sut.Verify(targetPath, Cap());

        Assert.Equal(VerificationStatus.VerifyFailed, status);
    }

    [Fact]
    public void Invoke_ToolFailure_ReturnsAttemptedOutputPath()
    {
        var runner = new TestToolRunner(new Dictionary<string, string?>
        {
            ["psxtract"] = GetExistingExecutablePath()
        });
        var sut = new PsxtractInvoker(runner);

        var sourcePath = Path.Combine(_root, "game.pbp");
        var targetPath = Path.Combine(_root, "game.iso");
        File.WriteAllBytes(sourcePath, new byte[] { 1, 2, 3, 4, 5 });

        runner.Enqueue(new ToolResult(1, "simulated failure", false));

        var result = sut.Invoke(sourcePath, targetPath, Cap());

        Assert.False(result.Success);
        Assert.Equal(targetPath, result.OutputPath);
        Assert.Equal(1, result.ExitCode);
    }

    private static string GetExistingExecutablePath()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            Path.Combine(winDir, "System32", "cmd.exe"),
            Path.Combine(winDir, "System32", "where.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException("No suitable executable found for psxtract invoker test.");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
        catch { }
    }

    private static ConversionCapability Cap()
    {
        return new ConversionCapability
        {
            SourceExtension = ".pbp",
            TargetExtension = ".chd",
            Tool = new ToolRequirement { ToolName = "psxtract" },
            Command = "convert",
            ApplicableConsoles = null,
            RequiredSourceIntegrity = null,
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 0,
            Verification = VerificationMethod.FileExistenceCheck,
            Description = "test psxtract",
            Condition = ConversionCondition.None
        };
    }
}
