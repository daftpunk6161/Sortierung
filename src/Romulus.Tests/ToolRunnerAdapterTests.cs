using Romulus.Infrastructure.Tools;
using System.Diagnostics;
using Xunit;

namespace Romulus.Tests;

public sealed class ToolRunnerAdapterTests : IDisposable
{
    private readonly string _tempDir;

    public ToolRunnerAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_ToolHash_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void InvokeProcess_MissingHashFile_FailClosed()
    {
        var exe = GetExistingExecutable();
        var missingHashes = Path.Combine(_tempDir, "missing-tool-hashes.json");

        var runner = new ToolRunnerAdapter(missingHashes, allowInsecureHashBypass: false);
        var result = runner.InvokeProcess(exe, new[] { "/?" }, "test");

        Assert.False(result.Success);
        Assert.Contains("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvokeProcess_ToolNotInAllowList_FailClosed()
    {
        var exe = GetExistingExecutable();
        var hashesPath = Path.Combine(_tempDir, "tool-hashes.json");
        File.WriteAllText(hashesPath, """
        {
          "Tools": {
            "other.exe": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
          }
        }
        """);

        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false);
        var result = runner.InvokeProcess(exe, new[] { "/?" }, "test");

        Assert.False(result.Success);
        Assert.Contains("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvokeProcess_BypassEnabled_AllowsExecutionPath()
    {
        var exe = GetExistingExecutable();
        var missingHashes = Path.Combine(_tempDir, "missing-tool-hashes.json");

        var runner = new ToolRunnerAdapter(missingHashes, allowInsecureHashBypass: true);
        var result = runner.InvokeProcess(exe, new[] { "/?" }, "test");

        Assert.DoesNotContain("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvokeProcess_CancelledToken_StopsLongRunningProcess()
    {
        var exe = GetExistingExecutable();
        var missingHashes = Path.Combine(_tempDir, "missing-tool-hashes.json");
        var runner = new ToolRunnerAdapter(missingHashes, allowInsecureHashBypass: true, timeoutMinutes: 30);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var sw = Stopwatch.StartNew();

        var result = runner.InvokeProcess(
            exe,
            ["/c", "ping", "127.0.0.1", "-n", "20"],
            "test",
            TimeSpan.FromSeconds(30),
            cts.Token);

        sw.Stop();

        Assert.False(result.Success);
        Assert.Contains("cancel", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void InvokeProcess_CancelledToken_KillsSpawnedProcess()
    {
        var exe = GetExistingExecutable();
        var missingHashes = Path.Combine(_tempDir, "missing-tool-hashes.json");
        var runner = new ToolRunnerAdapter(missingHashes, allowInsecureHashBypass: true, timeoutMinutes: 30);
        var trackedBefore = ExternalProcessGuard.GetTrackedProcessCountForTests();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));

        var result = runner.InvokeProcess(
            exe,
            ["/c", "ping", "127.0.0.1", "-n", "30"],
            "cancel-leak-test",
            TimeSpan.FromSeconds(30),
            cts.Token);

        Assert.False(result.Success);
        Assert.Contains("cancel", result.Output, StringComparison.OrdinalIgnoreCase);

        SpinWait.SpinUntil(
            () => ExternalProcessGuard.GetTrackedProcessCountForTests() <= trackedBefore,
            TimeSpan.FromSeconds(5));

        Assert.Equal(trackedBefore, ExternalProcessGuard.GetTrackedProcessCountForTests());
    }

    [Fact]
    public void InvokeProcess_Timeout_IncludesInvocationAndToolOutput()
    {
        var exe = GetExistingExecutable();
        var missingHashes = Path.Combine(_tempDir, "missing-tool-hashes.json");
        var runner = new ToolRunnerAdapter(missingHashes, allowInsecureHashBypass: true, timeoutMinutes: 30);

        var result = runner.InvokeProcess(
            exe,
            ["/c", "echo pre-timeout && ping 127.0.0.1 -n 30"],
            "chdman",
            TimeSpan.FromMilliseconds(250),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("timed out", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invocation:", result.Output, StringComparison.Ordinal);
        Assert.Contains("pre-timeout", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvokeProcess_NonZeroExit_IncludesExitCodeInvocationAndToolOutput()
    {
        var exe = GetExistingExecutable();
        var missingHashes = Path.Combine(_tempDir, "missing-tool-hashes.json");
        var runner = new ToolRunnerAdapter(missingHashes, allowInsecureHashBypass: true);

        var result = runner.InvokeProcess(
            exe,
            ["/c", "echo chdman failure 1>&2 & exit /b 7"],
            "chdman");

        Assert.False(result.Success);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("exited with code 7", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Invocation:", result.Output, StringComparison.Ordinal);
        Assert.Contains("chdman failure", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindTool_UsesConversionToolsRootOverride_ForSevenZipSubfolder()
    {
        var toolsRoot = Path.Combine(_tempDir, "conversion-tools");
        var sevenZipPath = Path.Combine(toolsRoot, "7zip", "7z.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(sevenZipPath)!);
        File.WriteAllText(sevenZipPath, "stub");

        string? resolved = null;
        WithEnvironmentVariable(ToolRunnerAdapter.ConversionToolsRootOverrideEnvVar, toolsRoot, () =>
        {
            var runner = new ToolRunnerAdapter();
            resolved = runner.FindTool("7z");
        });

        Assert.Equal(sevenZipPath, resolved);
    }

    [Fact]
    public void FindTool_CisoAcceptsMaxcsoInConversionToolsRootOverride()
    {
        var toolsRoot = Path.Combine(_tempDir, "conversion-tools-ciso");
        var maxcsoPath = Path.Combine(toolsRoot, "maxcso.exe");
        Directory.CreateDirectory(toolsRoot);
        File.WriteAllText(maxcsoPath, "stub");

        string? resolved = null;
        WithEnvironmentVariable(ToolRunnerAdapter.ConversionToolsRootOverrideEnvVar, toolsRoot, () =>
        {
            var runner = new ToolRunnerAdapter();
            resolved = runner.FindTool("ciso");
        });

        Assert.Equal(maxcsoPath, resolved);
    }

    private static void WithEnvironmentVariable(string name, string value, Action action)
    {
        var original = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, value);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }

    private static string GetExistingExecutable()
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

        throw new InvalidOperationException("Kein bekanntes Test-Executable gefunden.");
    }
}
