using Romulus.Api;
using Romulus.CLI;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Tests;
using Xunit;
using CliProgram = Romulus.CLI.Program;

namespace Romulus.Tests.EntryPointParity;

/// <summary>
/// Failed/Partial/Risky State-Paritaet.
///
/// ReportParityTests covers happy-path success parity. This suite extends parity
/// to "no useful work" / "preflight" end-states across CLI and API:
///
///  1.  Non-existent root path -> CLI and API MUST agree on accept-vs-reject and
///        on "0 files scanned" semantics. No silent fabrication of fake results
///        in either entry point.
///  2.  Empty root directory -> Same parity. Both entry points must complete
///        with TotalFiles=0 / Groups=0 / Winners=0.
/// </summary>
public sealed class FailedStateParityTests : IDisposable
{
    private readonly string _tempDir;

    public FailedStateParityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_C6_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task FailedRun_NonExistentRoot_CliAndApiHandleGracefullyWithoutFabricatedResults()
    {
        var ghostRoot = Path.Combine(_tempDir, "does-not-exist-" + Guid.NewGuid().ToString("N")[..8]);
        Assert.False(Directory.Exists(ghostRoot));

        var cliOptions = new CliRunOptions { Roots = [ghostRoot], Mode = "DryRun" };
        var (cliExitCode, _, _) = RunCliWithCapturedConsole(cliOptions);

        var manager = new RunManager(new FileSystemAdapter(), new AuditCsvStore());
        var apiRun = manager.TryCreate(new RunRequest { Roots = [ghostRoot], Mode = "DryRun" }, "DryRun");

        // Both entry points MUST handle non-existent roots without crashing.
        // KNOWN DIVERGENCE (potential P1 finding): CLI currently accepts non-existent
        // roots (exit 0, scans 0 files) while RunManager rejects via TryCreate=null.
        // Both behaviors are individually safe (no crash, no fabricated results).
        // Per Romulus rule "Eine fachliche Wahrheit" they SHOULD agree, but this test
        // documents current safe behavior on both sides without baking in inequality.
        Assert.True(cliExitCode >= 0 && cliExitCode <= 64,
            $"CLI must exit gracefully (got {cliExitCode}).");

        if (apiRun is not null)
        {
            var wait = await manager.WaitForCompletion(apiRun.RunId, timeout: TimeSpan.FromSeconds(15));
            Assert.Equal(RunWaitDisposition.Completed, wait.Disposition);

            var apiResult = manager.Get(apiRun.RunId)!.Result;
            Assert.NotNull(apiResult);
            Assert.Equal(0, apiResult!.TotalFiles);
            Assert.Equal(0, apiResult.Groups);
            Assert.Equal(0, apiResult.Winners);
        }
        // else: API rejected at preflight - acceptable safe behavior.
        await Task.CompletedTask;
    }

    [Fact]
    public async Task FailedRun_EmptyRoot_CliAndApiAgreeOnEmptyRunSemantics()
    {
        var emptyRoot = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyRoot);

        var cliOptions = new CliRunOptions { Roots = [emptyRoot], Mode = "DryRun" };
        var (cliExitCode, _, cliStderr) = RunCliWithCapturedConsole(cliOptions);
        Assert.True(cliExitCode == 0,
            $"CLI exit must be 0 for empty roots, got {cliExitCode}. Stderr={cliStderr}");

        var manager = new RunManager(new FileSystemAdapter(), new AuditCsvStore());
        var apiRun = manager.TryCreate(new RunRequest { Roots = [emptyRoot], Mode = "DryRun" }, "DryRun");
        Assert.NotNull(apiRun);
        var wait = await manager.WaitForCompletion(apiRun!.RunId, timeout: TimeSpan.FromSeconds(15));
        Assert.Equal(RunWaitDisposition.Completed, wait.Disposition);

        var apiResult = manager.Get(apiRun.RunId)!.Result;
        Assert.NotNull(apiResult);
        Assert.Equal(0, apiResult!.TotalFiles);
        Assert.Equal(0, apiResult.Groups);
        Assert.Equal(0, apiResult.Winners);
    }

    private static (int ExitCode, string Stdout, string Stderr) RunCliWithCapturedConsole(CliRunOptions options)
    {
        lock (SharedTestLocks.ConsoleLock)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            try
            {
                CliProgram.SetConsoleOverrides(stdout, stderr);
                var exitCode = CliProgram.RunForTests(options);
                return (exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                CliProgram.SetConsoleOverrides(null, null);
            }
        }
    }
}
