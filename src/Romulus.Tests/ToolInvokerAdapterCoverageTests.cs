using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for ToolInvokerAdapter: CanHandle, Invoke, Verify, BuildArguments,
/// guard clauses, tool resolution, verification methods.
/// </summary>
public sealed class ToolInvokerAdapterCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public ToolInvokerAdapterCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"toolinv_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    #region CanHandle

    [Fact]
    public void CanHandle_ValidToolName_ReturnsTrue()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var cap = MakeCapability("chdman", "createcd");

        Assert.True(adapter.CanHandle(cap));
    }

    [Fact]
    public void CanHandle_EmptyToolName_ReturnsFalse()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var cap = MakeCapability("", "createcd");

        Assert.False(adapter.CanHandle(cap));
    }

    [Fact]
    public void CanHandle_WhitespaceToolName_ReturnsFalse()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var cap = MakeCapability("  ", "createcd");

        Assert.False(adapter.CanHandle(cap));
    }

    [Fact]
    public void CanHandle_NullCapability_Throws()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        Assert.Throws<ArgumentNullException>(() => adapter.CanHandle(null!));
    }

    #endregion

    #region Invoke – Guard Clauses

    [Fact]
    public void Invoke_NullSource_Throws()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        Assert.ThrowsAny<ArgumentException>(() =>
            adapter.Invoke(null!, "target.chd", MakeCapability("chdman", "createcd")));
    }

    [Fact]
    public void Invoke_EmptyTarget_Throws()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        Assert.Throws<ArgumentException>(() =>
            adapter.Invoke("source.iso", "", MakeCapability("chdman", "createcd")));
    }

    [Fact]
    public void Invoke_NullCapability_Throws()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        Assert.Throws<ArgumentNullException>(() =>
            adapter.Invoke("source.iso", "target.chd", null!));
    }

    [Fact]
    public void Invoke_CancelledToken_Throws()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            adapter.Invoke("source.iso", "target.chd",
                MakeCapability("chdman", "createcd"), cts.Token));
    }

    #endregion

    #region Invoke – Source Not Found

    [Fact]
    public void Invoke_SourceNotFound_ReturnsFailure()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var result = adapter.Invoke(
            Path.Combine(_tempDir, "nonexistent.iso"),
            Path.Combine(_tempDir, "out.chd"),
            MakeCapability("chdman", "createcd"));

        Assert.False(result.Success);
        Assert.Equal("source-not-found", result.StdErr);
    }

    #endregion

    #region Invoke – Invalid Command

    [Fact]
    public void Invoke_EmptyCommand_ReturnsInvalidCommand()
    {
        var source = CreateFile("input.iso", 100);
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var result = adapter.Invoke(source, Path.Combine(_tempDir, "out.chd"),
            MakeCapability("chdman", ""));

        Assert.False(result.Success);
        Assert.Equal("invalid-command", result.StdErr);
    }

    [Fact]
    public void Invoke_CommandWithSlash_ReturnsInvalidCommand()
    {
        var source = CreateFile("input.iso", 100);
        var runner = new FakeToolRunner();
        runner.RegisterTool("chdman", CreateFile("chdman.exe", 100));
        var adapter = new ToolInvokerAdapter(runner);
        var result = adapter.Invoke(source, Path.Combine(_tempDir, "out.chd"),
            MakeCapability("chdman", "/etc/evil"));

        Assert.False(result.Success);
        Assert.Equal("invalid-command", result.StdErr);
    }

    #endregion

    #region Invoke – Tool Not Found

    [Fact]
    public void Invoke_ToolNotFound_ReturnsToolNotFound()
    {
        var source = CreateFile("input.iso", 100);
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var result = adapter.Invoke(source, Path.Combine(_tempDir, "out.chd"),
            MakeCapability("chdman", "createcd"));

        Assert.False(result.Success);
        Assert.Contains("tool-not-found:chdman", result.StdErr!);
    }

    #endregion

    #region Invoke – Successful Execution

    [Fact]
    public void Invoke_ChdmanSuccess_ReturnsSuccess()
    {
        var source = CreateFile("input.iso", 100);
        var target = Path.Combine(_tempDir, "out.chd");

        var runner = new FakeToolRunner();
        runner.RegisterTool("chdman", source); // tool "exists" at source path (it's a file)
        runner.NextResult = new ToolResult(0, "ok", true);

        var adapter = new ToolInvokerAdapter(runner);
        var cap = MakeCapability("chdman", "createcd");
        var result = adapter.Invoke(source, target, cap);

        Assert.True(result.Success);
        Assert.Equal(target, result.OutputPath);
        Assert.True(result.DurationMs >= 0);
    }

    [Fact]
    public void Invoke_ToolProcessFails_ReturnsError()
    {
        var source = CreateFile("input.iso", 100);
        var target = Path.Combine(_tempDir, "out.chd");

        var runner = new FakeToolRunner();
        runner.RegisterTool("chdman", source);
        runner.NextResult = new ToolResult(1, "disc error", false);

        var adapter = new ToolInvokerAdapter(runner);
        var result = adapter.Invoke(source, target, MakeCapability("chdman", "createcd"));

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
    }

    #endregion

    #region Invoke – Tool-Specific Argument Building

    [Fact]
    public void Invoke_DolphinTool_BuildsCorrectArgs()
    {
        var source = CreateFile("game.iso", 100);
        var target = Path.Combine(_tempDir, "game.rvz");

        var runner = new FakeToolRunner();
        runner.RegisterTool("dolphintool", source);
        runner.NextResult = new ToolResult(0, "ok", true);

        var adapter = new ToolInvokerAdapter(runner);
        var cap = MakeCapability("dolphintool", "convert");
        var result = adapter.Invoke(source, target, cap);

        Assert.True(result.Success);
        // Verify dolphintool-specific args are passed (convert -i source -o target -f rvz -c zstd)
        var args = runner.LastArgs!;
        Assert.Contains("-f", args);
        Assert.Contains("rvz", args);
    }

    [Fact]
    public void Invoke_7z_BuildsCompressArgs()
    {
        var source = CreateFile("game.rom", 100);
        var target = Path.Combine(_tempDir, "game.zip");

        var runner = new FakeToolRunner();
        runner.RegisterTool("7z", source);
        runner.NextResult = new ToolResult(0, "ok", true);

        var adapter = new ToolInvokerAdapter(runner);
        var cap = MakeCapability("7z", "a");
        var result = adapter.Invoke(source, target, cap);

        Assert.True(result.Success);
        // 7z args: a -tzip -y target source
        Assert.Contains("a", runner.LastArgs!);
    }

    [Fact]
    public void Invoke_PsxtractTool_BuildsArgs()
    {
        var source = CreateFile("game.pbp", 100);
        var target = Path.Combine(_tempDir, "game.iso");

        var runner = new FakeToolRunner();
        runner.RegisterTool("psxtract", source);
        runner.NextResult = new ToolResult(0, "ok", true);

        var adapter = new ToolInvokerAdapter(runner);
        var cap = MakeCapability("psxtract", "extract");
        var result = adapter.Invoke(source, target, cap);

        Assert.True(result.Success);
    }

    [Fact]
    public void Invoke_GenericTool_DefaultArgPattern()
    {
        var source = CreateFile("input.dat", 100);
        var target = Path.Combine(_tempDir, "output.dat");

        var runner = new FakeToolRunner();
        runner.RegisterTool("sometool", source);
        runner.NextResult = new ToolResult(0, "ok", true);

        var adapter = new ToolInvokerAdapter(runner);
        var cap = MakeCapability("sometool", "process");
        var result = adapter.Invoke(source, target, cap);

        Assert.True(result.Success);
        // Generic: [command, -i, source, -o, target]
        Assert.Contains("-i", runner.LastArgs!);
        Assert.Contains("-o", runner.LastArgs!);
    }

    #endregion

    #region Verify – All Verification Methods

    [Fact]
    public void Verify_NoneMethod_ReturnsNotAttempted()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var cap = MakeCapability("chdman", "createcd", VerificationMethod.None);
        var target = CreateFile("out.chd", 1024);

        Assert.Equal(VerificationStatus.NotAttempted, adapter.Verify(target, cap));
    }

    [Fact]
    public void Verify_FileExistence_NonEmptyFile_Verified()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var cap = MakeCapability("7z", "a", VerificationMethod.FileExistenceCheck);
        var target = CreateFile("out.zip", 1024);

        Assert.Equal(VerificationStatus.Verified, adapter.Verify(target, cap));
    }

    [Fact]
    public void Verify_FileExistence_EmptyFile_VerifyFailed()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var cap = MakeCapability("7z", "a", VerificationMethod.FileExistenceCheck);
        var target = CreateFile("empty.zip", 0);

        Assert.Equal(VerificationStatus.VerifyFailed, adapter.Verify(target, cap));
    }

    [Fact]
    public void Verify_FileExistence_NonExistent_VerifyFailed()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var cap = MakeCapability("7z", "a", VerificationMethod.FileExistenceCheck);

        Assert.Equal(VerificationStatus.VerifyFailed,
            adapter.Verify(Path.Combine(_tempDir, "nope.zip"), cap));
    }

    [Fact]
    public void Verify_ChdmanVerify_ToolNotFound_NotAvailable()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var cap = MakeCapability("chdman", "createcd", VerificationMethod.ChdmanVerify);
        var target = CreateFile("out.chd", 1024);

        Assert.Equal(VerificationStatus.VerifyNotAvailable, adapter.Verify(target, cap));
    }

    [Fact]
    public void Verify_ChdmanVerify_Success_Verified()
    {
        var runner = new FakeToolRunner();
        runner.RegisterTool("chdman", "chdman.exe");
        runner.NextResult = new ToolResult(0, "verified ok", true);

        var adapter = new ToolInvokerAdapter(runner);
        var cap = MakeCapability("chdman", "createcd", VerificationMethod.ChdmanVerify);
        var target = CreateFile("out.chd", 1024);

        Assert.Equal(VerificationStatus.Verified, adapter.Verify(target, cap));
    }

    [Fact]
    public void Verify_ChdmanVerify_Fails_VerifyFailed()
    {
        var runner = new FakeToolRunner();
        runner.RegisterTool("chdman", "chdman.exe");
        runner.NextResult = new ToolResult(1, "bad", false);

        var adapter = new ToolInvokerAdapter(runner);
        var cap = MakeCapability("chdman", "createcd", VerificationMethod.ChdmanVerify);
        var target = CreateFile("out.chd", 1024);

        Assert.Equal(VerificationStatus.VerifyFailed, adapter.Verify(target, cap));
    }

    [Fact]
    public void Verify_SevenZipTest_ToolNotFound_NotAvailable()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var cap = MakeCapability("7z", "a", VerificationMethod.SevenZipTest);
        var target = CreateFile("out.7z", 1024);

        Assert.Equal(VerificationStatus.VerifyNotAvailable, adapter.Verify(target, cap));
    }

    [Fact]
    public void Verify_SevenZipTest_Success_Verified()
    {
        var runner = new FakeToolRunner();
        runner.RegisterTool("7z", "7z.exe");
        runner.NextResult = new ToolResult(0, "ok", true);

        var adapter = new ToolInvokerAdapter(runner);
        var cap = MakeCapability("7z", "a", VerificationMethod.SevenZipTest);
        var target = CreateFile("out.7z", 1024);

        Assert.Equal(VerificationStatus.Verified, adapter.Verify(target, cap));
    }

    [Fact]
    public void Verify_RvzMagicByte_ValidRvz_Verified()
    {
        var target = Path.Combine(_tempDir, "game.rvz");
        File.WriteAllBytes(target, [(byte)'R', (byte)'V', (byte)'Z', 0x01, 0x00, 0x00]);

        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var cap = MakeCapability("dolphintool", "convert", VerificationMethod.RvzMagicByte);

        Assert.Equal(VerificationStatus.Verified, adapter.Verify(target, cap));
    }

    [Fact]
    public void Verify_RvzMagicByte_InvalidMagic_VerifyFailed()
    {
        var target = Path.Combine(_tempDir, "bad.rvz");
        File.WriteAllBytes(target, [0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var cap = MakeCapability("dolphintool", "convert", VerificationMethod.RvzMagicByte);

        Assert.Equal(VerificationStatus.VerifyFailed, adapter.Verify(target, cap));
    }

    [Fact]
    public void Verify_RvzMagicByte_FileTooSmall_VerifyFailed()
    {
        var target = Path.Combine(_tempDir, "tiny.rvz");
        File.WriteAllBytes(target, [0x01, 0x02]);

        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        var cap = MakeCapability("dolphintool", "convert", VerificationMethod.RvzMagicByte);

        Assert.Equal(VerificationStatus.VerifyFailed, adapter.Verify(target, cap));
    }

    [Fact]
    public void Verify_NullTarget_Throws()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        Assert.ThrowsAny<ArgumentException>(() =>
            adapter.Verify(null!, MakeCapability("chdman", "createcd")));
    }

    [Fact]
    public void Verify_NullCapability_Throws()
    {
        var adapter = new ToolInvokerAdapter(new FakeToolRunner());
        Assert.Throws<ArgumentNullException>(() =>
            adapter.Verify("some.chd", null!));
    }

    #endregion

    #region Constructor

    [Fact]
    public void Ctor_NullTools_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ToolInvokerAdapter(null!));
    }

    #endregion

    #region Helpers

    private string CreateFile(string name, long sizeBytes)
    {
        var path = Path.Combine(_tempDir, name);
        using var fs = File.Create(path);
        if (sizeBytes > 0)
            fs.SetLength(sizeBytes);
        return path;
    }

    private static ConversionCapability MakeCapability(
        string toolName, string command,
        VerificationMethod verification = VerificationMethod.None)
        => new()
        {
            SourceExtension = ".iso",
            TargetExtension = ".chd",
            Tool = new ToolRequirement { ToolName = toolName },
            Command = command,
            ResultIntegrity = SourceIntegrity.Lossy,
            Lossless = false,
            Cost = 1,
            Verification = verification
        };

    private sealed class FakeToolRunner : IToolRunner
    {
        private readonly Dictionary<string, string> _tools = new(StringComparer.OrdinalIgnoreCase);
        public ToolResult NextResult { get; set; } = new(0, "", true);
        public string[]? LastArgs { get; private set; }

        public void RegisterTool(string name, string path) => _tools[name] = path;
        public string? FindTool(string toolName) => _tools.GetValueOrDefault(toolName);

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
        {
            LastArgs = arguments;
            return NextResult;
        }

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel,
            TimeSpan? timeout, CancellationToken cancellationToken)
        {
            LastArgs = arguments;
            return NextResult;
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments) => NextResult;
    }

    #endregion
}
