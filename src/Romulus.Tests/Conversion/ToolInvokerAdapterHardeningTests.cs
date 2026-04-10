using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion;
using System.Security.Cryptography;
using Xunit;

namespace Romulus.Tests.Conversion;

public sealed class ToolInvokerAdapterHardeningTests
{
    [Fact]
    public void Invoke_SourceMissing_ReturnsError()
    {
        var runner = new RecordingToolRunner();
        var invoker = new ToolInvokerAdapter(runner);

        var result = invoker.Invoke(
            Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.iso"),
            Path.Combine(Path.GetTempPath(), $"out_{Guid.NewGuid():N}.chd"),
            Capability("chdman", "createcd"));

        Assert.False(result.Success);
        Assert.Equal("source-not-found", result.StdErr);
        Assert.False(runner.WasInvokeCalled);
    }

    [Fact]
    public void Invoke_CommandWithPathSeparator_ReturnsInvalidCommand()
    {
        var source = CreateTempFile(".iso");
        var target = Path.ChangeExtension(source, ".chd");
        var runner = new RecordingToolRunner { ToolPath = GetExistingExecutablePath() };
        var invoker = new ToolInvokerAdapter(runner);

        try
        {
            var result = invoker.Invoke(source, target, Capability("chdman", "..\\createcd"));

            Assert.False(result.Success);
            Assert.Equal("invalid-command", result.StdErr);
            Assert.False(runner.WasInvokeCalled);
        }
        finally
        {
            if (File.Exists(source))
                File.Delete(source);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public void Invoke_OnlyUsesFirstCommandToken()
    {
        var source = CreateTempFile(".iso");
        var target = Path.ChangeExtension(source, ".chd");
        var runner = new RecordingToolRunner { ToolPath = GetExistingExecutablePath() };
        var invoker = new ToolInvokerAdapter(runner);

        try
        {
            var result = invoker.Invoke(source, target, Capability("chdman", "createcd --bogus"));

            Assert.True(result.Success);
            Assert.NotNull(runner.LastArgs);
            Assert.Equal("createcd", runner.LastArgs![0]);
        }
        finally
        {
            if (File.Exists(source))
                File.Delete(source);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public void Invoke_CreatedvdWithSmallIso_UsesCreatecd()
    {
        var source = CreateSizedTempIso(699L * 1024 * 1024);
        var target = Path.ChangeExtension(source, ".chd");
        var runner = new RecordingToolRunner { ToolPath = GetExistingExecutablePath() };
        var invoker = new ToolInvokerAdapter(runner);

        try
        {
            var result = invoker.Invoke(source, target, Capability("chdman", "createdvd"));

            Assert.True(result.Success);
            Assert.NotNull(runner.LastArgs);
            Assert.Equal("createcd", runner.LastArgs![0]);
        }
        finally
        {
            if (File.Exists(source))
                File.Delete(source);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public void Invoke_ForwardsCancellationTokenAndTimeout_ToToolRunner()
    {
        var source = CreateTempFile(".iso");
        var target = Path.ChangeExtension(source, ".chd");
        var runner = new RecordingToolRunner { ToolPath = GetExistingExecutablePath() };
        var invoker = new ToolInvokerAdapter(runner);
        using var cts = new CancellationTokenSource();

        try
        {
            var result = invoker.Invoke(source, target, Capability("chdman", "createcd"), cts.Token);

            Assert.True(result.Success);
            Assert.True(runner.AdvancedInvokeCalled);
            Assert.True(runner.LastCancellationToken.CanBeCanceled);
            Assert.NotNull(runner.LastTimeout);
        }
        finally
        {
            if (File.Exists(source))
                File.Delete(source);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public void Invoke_ExpectedHashMismatch_BlocksInvocation()
    {
        var source = CreateTempFile(".iso");
        var target = Path.ChangeExtension(source, ".chd");
        var runner = new RecordingToolRunner { ToolPath = GetExistingExecutablePath() };
        var invoker = new ToolInvokerAdapter(runner);

        try
        {
            var capability = Capability("chdman", "createcd");
            capability = capability with
            {
                Tool = new ToolRequirement
                {
                    ToolName = "chdman",
                    ExpectedHash = new string('0', 64)
                }
            };

            var result = invoker.Invoke(source, target, capability);

            Assert.False(result.Success);
            Assert.Equal("tool-hash-mismatch", result.StdErr);
            Assert.False(runner.WasInvokeCalled);
        }
        finally
        {
            if (File.Exists(source))
                File.Delete(source);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public void Invoke_ExpectedHashMatch_AllowsInvocation()
    {
        var source = CreateTempFile(".iso");
        var target = Path.ChangeExtension(source, ".chd");
        var toolPath = GetExistingExecutablePath();
        var runner = new RecordingToolRunner { ToolPath = toolPath };
        var invoker = new ToolInvokerAdapter(runner);

        try
        {
            var capability = Capability("chdman", "createcd");
            capability = capability with
            {
                Tool = new ToolRequirement
                {
                    ToolName = "chdman",
                    ExpectedHash = ComputeSha256(toolPath)
                }
            };

            var result = invoker.Invoke(source, target, capability);

            Assert.True(result.Success);
            Assert.True(runner.WasInvokeCalled);
        }
        finally
        {
            if (File.Exists(source))
                File.Delete(source);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    [Fact]
    public void Invoke_MinVersionTooHigh_BlocksInvocation()
    {
        var source = CreateTempFile(".iso");
        var target = Path.ChangeExtension(source, ".chd");
        var runner = new RecordingToolRunner { ToolPath = GetExistingExecutablePath() };
        var invoker = new ToolInvokerAdapter(runner);

        try
        {
            var capability = Capability("chdman", "createcd");
            capability = capability with
            {
                Tool = new ToolRequirement
                {
                    ToolName = "chdman",
                    MinVersion = "99.0.0.0"
                }
            };

            var result = invoker.Invoke(source, target, capability);

            Assert.False(result.Success);
            Assert.Equal("tool-version-too-old", result.StdErr);
            Assert.False(runner.WasInvokeCalled);
        }
        finally
        {
            if (File.Exists(source))
                File.Delete(source);
            if (File.Exists(target))
                File.Delete(target);
        }
    }

    private static ConversionCapability Capability(string toolName, string command)
    {
        return new ConversionCapability
        {
            SourceExtension = ".iso",
            TargetExtension = ".chd",
            Tool = new ToolRequirement { ToolName = toolName },
            Command = command,
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 0,
            Verification = VerificationMethod.FileExistenceCheck,
            Condition = ConversionCondition.None
        };
    }

    private static string CreateTempFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tool_invoker_hardening_{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return path;
    }

    private static string CreateSizedTempIso(long sizeBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tool_invoker_hardening_{Guid.NewGuid():N}.iso");
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.SetLength(sizeBytes);
        return path;
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

        throw new InvalidOperationException("No suitable tool executable found for hash/version test.");
    }

    private static string ComputeSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class RecordingToolRunner : IToolRunner
    {
        public bool WasInvokeCalled { get; private set; }
        public bool AdvancedInvokeCalled { get; private set; }
        public string[]? LastArgs { get; private set; }
        public TimeSpan? LastTimeout { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public string? ToolPath { get; init; }

        public string? FindTool(string toolName) => ToolPath ?? $"C:\\mock\\{toolName}.exe";

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
        {
            WasInvokeCalled = true;
            LastArgs = arguments;

            var outIndex = Array.IndexOf(arguments, "-o");
            if (outIndex >= 0 && outIndex < arguments.Length - 1)
            {
                var outputPath = arguments[outIndex + 1];
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(outputPath, [1, 2, 3, 4]);
            }

            return new ToolResult(0, "ok", true);
        }

        public ToolResult InvokeProcess(
            string filePath,
            string[] arguments,
            string? errorLabel,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            AdvancedInvokeCalled = true;
            LastTimeout = timeout;
            LastCancellationToken = cancellationToken;
            return InvokeProcess(filePath, arguments, errorLabel);
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(0, "ok", true);
    }
}
