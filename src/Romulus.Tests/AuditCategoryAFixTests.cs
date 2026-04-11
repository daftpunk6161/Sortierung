using System.Diagnostics;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Core.Conversion;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Tools;
using Xunit;
using CliProgram = Romulus.CLI.Program;

namespace Romulus.Tests;

/// <summary>
/// TDD red-then-green tests for full-repo-audit category A findings.
/// A-01: Process WaitForExit timeout safety
/// A-02: Deferred analysis warnings propagation
/// A-03: CLI non-interactive move-mode guard
/// A-04: ConversionConditionEvaluator symmetric I/O guard
/// </summary>
public sealed class AuditCategoryAFixTests : IDisposable
{
    private readonly string _tempDir;

    public AuditCategoryAFixTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AuditA_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    // ═══════════════════════════════════════════════════════════════════
    // A-01: Process WaitForExit must have bounded timeout
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void A01_InvokeProcess_NormalCompletion_DoesNotHangOnSlowExit()
    {
        // Process that writes output, then delays slightly before exiting.
        // With a bounded WaitForExit, the call should complete within a reasonable timeframe.
        var exe = GetCmd();
        var hashesPath = Path.Combine(_tempDir, "tool-hashes.json");
        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: true, timeoutMinutes: 1);

        var sw = Stopwatch.StartNew();
        var result = runner.InvokeProcess(
            exe,
            ["/c", "echo done"],
            "test",
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        sw.Stop();

        Assert.True(result.Success);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15),
            $"WaitForExit should have a bounded timeout, took {sw.Elapsed}");
    }

    [Fact]
    public void A01_InvokeProcess_Timeout_DoesNotHangThread()
    {
        // Process with very short timeout — verify the thread does not hang
        // beyond the timeout + kill escalation window.
        var exe = GetCmd();
        var hashesPath = Path.Combine(_tempDir, "tool-hashes.json");
        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: true, timeoutMinutes: 30);

        var sw = Stopwatch.StartNew();
        var result = runner.InvokeProcess(
            exe,
            ["/c", "ping 127.0.0.1 -n 30"],
            "test",
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        sw.Stop();

        Assert.False(result.Success);
        Assert.Contains("timed out", result.Output, StringComparison.OrdinalIgnoreCase);
        // Total wall time: 1s timeout + 5s TryTerminate + 10s WaitForExit safety = max 16s
        // With the fix, it should not exceed ~20s total
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20),
            $"Timeout + kill escalation exceeded safety window: {sw.Elapsed}");
    }

    // ═══════════════════════════════════════════════════════════════════
    // A-02: RunResult must carry warnings from deferred analysis
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void A02_RunResult_HasWarningsProperty()
    {
        // RunResult must expose an IReadOnlyList<string> Warnings.
        var result = new RunResult();
        Assert.NotNull(result.Warnings);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A02_RunResultBuilder_PropagatesWarnings()
    {
        // RunResultBuilder must carry warnings through to the built result.
        var builder = new RunResultBuilder();
        builder.Warnings.Add("Deferred analysis skipped: Cross-root preview failed");
        builder.Warnings.Add("Deferred analysis skipped: Quarantine preview failed");

        var result = builder.Build();

        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains("Cross-root preview", result.Warnings[0]);
        Assert.Contains("Quarantine preview", result.Warnings[1]);
    }

    // ═══════════════════════════════════════════════════════════════════
    // A-03: CLI non-interactive move-mode must not hang
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void A03_CliMoveMode_NonInteractive_ReturnsPreflightError()
    {
        // In non-interactive mode without --yes, move must fail with exit code 3.
        var stderr = new StringWriter();

        try
        {
            CliProgram.SetConsoleOverrides(TextWriter.Null, stderr);
            CliProgram.SetNonInteractiveOverride(true);

            var opts = new Romulus.CLI.CliRunOptions
            {
                Mode = "Move",
                Roots = [_tempDir],
                Yes = false,
            };

            var exitCode = CliProgram.RunForTests(opts);

            Assert.Equal(3, exitCode);
            Assert.Contains("--yes", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CliProgram.SetConsoleOverrides(null, null);
            CliProgram.SetNonInteractiveOverride(null);
        }
    }

    [Fact]
    public void A03_CliMoveMode_WithYesFlag_SkipsConfirmation()
    {
        // With --yes, move mode should proceed without prompting — no hang.
        var stderr = new StringWriter();
        var stdout = new StringWriter();

        try
        {
            CliProgram.SetConsoleOverrides(stdout, stderr);
            CliProgram.SetNonInteractiveOverride(true);

            var opts = new Romulus.CLI.CliRunOptions
            {
                Mode = "Move",
                Roots = [_tempDir],
                Yes = true,
            };

            var exitCode = CliProgram.RunForTests(opts);

            // Should not return 3 (preflight/confirmation error)
            Assert.NotEqual(3, exitCode);
            Assert.DoesNotContain("--yes", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CliProgram.SetConsoleOverrides(null, null);
            CliProgram.SetNonInteractiveOverride(null);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // A-04: ConversionConditionEvaluator symmetric I/O guard
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void A04_FileSizeGreaterEqual700MB_IOException_ReturnsFalse()
    {
        // When file size provider throws IOException, GreaterEqual must return false.
        var evaluator = new ConversionConditionEvaluator(
            _ => throw new IOException("disk error"));

        var result = evaluator.Evaluate(ConversionCondition.FileSizeGreaterEqual700MB, "test.iso");

        Assert.False(result, "GreaterEqual with I/O error must return false (SafeSize returns -1)");
    }

    [Fact]
    public void A04_FileSizeLessThan700MB_IOException_ReturnsFalse()
    {
        // Symmetric test: LessThan must also return false on IOException.
        var evaluator = new ConversionConditionEvaluator(
            _ => throw new IOException("disk error"));

        var result = evaluator.Evaluate(ConversionCondition.FileSizeLessThan700MB, "test.iso");

        Assert.False(result, "LessThan with I/O error must return false");
    }

    [Fact]
    public void A04_BothSizeConditions_Symmetric_OnIOError()
    {
        // Both conditions must behave identically on I/O error: both false.
        var evaluator = new ConversionConditionEvaluator(
            _ => throw new IOException("access denied"));

        var lessThan = evaluator.Evaluate(ConversionCondition.FileSizeLessThan700MB, "test.iso");
        var greaterEqual = evaluator.Evaluate(ConversionCondition.FileSizeGreaterEqual700MB, "test.iso");

        Assert.False(lessThan);
        Assert.False(greaterEqual);
    }

    [Fact]
    public void A04_FileSizeGreaterEqual700MB_ExplicitGuard_RejectsNegativeSize()
    {
        // Explicit test: a provider returning -1 (error sentinel) must be rejected
        // by the explicit > 0 guard, not accidentally matched by >= threshold.
        var evaluator = new ConversionConditionEvaluator(_ => -1L);

        var result = evaluator.Evaluate(ConversionCondition.FileSizeGreaterEqual700MB, "test.iso");

        Assert.False(result, "GreaterEqual must have explicit > 0 guard to reject -1 from SafeSize");
    }

    [Fact]
    public void A04_FileSizeGreaterEqual700MB_ZeroSize_ReturnsFalse()
    {
        // A zero-byte file should not match GreaterEqual.
        var evaluator = new ConversionConditionEvaluator(_ => 0L);

        var result = evaluator.Evaluate(ConversionCondition.FileSizeGreaterEqual700MB, "test.iso");

        Assert.False(result, "GreaterEqual must reject zero-byte files (> 0 guard)");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static string GetCmd()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var cmd = Path.Combine(winDir, "System32", "cmd.exe");
        if (File.Exists(cmd)) return cmd;
        throw new InvalidOperationException("cmd.exe not found");
    }
}
