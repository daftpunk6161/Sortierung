using System.Text.Json;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// T-W5-BEFORE-AFTER-SIMULATOR pass 3 — pin tests for the CLI <c>simulate</c>
/// subcommand. The subcommand is the headless equivalent of the GUI Simulator
/// view: it runs the canonical pipeline in DryRun mode (single source of truth
/// via <see cref="Romulus.Infrastructure.Analysis.BeforeAfterSimulator"/>) and
/// emits a deterministic before/after JSON projection. These tests cover the
/// argument parser plus an end-to-end smoke test of the subcommand handler so
/// future refactors cannot silently break the wiring or skip the DryRun chokepoint.
/// </summary>
public sealed class CliSimulateSubcommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _collectionDbPath;
    private readonly string _auditKeyPath;

    public CliSimulateSubcommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cli-sim-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        // Pre-W7 isolation (Fix #2): redirect persistent CLI state into a
        // sibling state/ subdir so SubcommandSimulateAsync never touches the real
        // %APPDATA%\Romulus\collection.db (LiteDB exclusive lock + slow open)
        // or the user's audit-signing key. Sibling dir keeps the override files
        // out of the scanned ROM root.
        var stateDir = Path.Combine(_tempDir, "..", "cli-sim-state-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(stateDir);
        _collectionDbPath = Path.Combine(stateDir, "collection.db");
        _auditKeyPath = Path.Combine(stateDir, "audit-signing.key");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        try
        {
            var stateDir = Path.GetDirectoryName(_collectionDbPath);
            if (!string.IsNullOrWhiteSpace(stateDir)) Directory.Delete(stateDir, recursive: true);
        }
        catch { /* best-effort */ }
    }

    private IDisposable IsolateCliPaths()
        => Romulus.CLI.Program.SetTestPathOverrides(new Romulus.CLI.CliPathOverrides
        {
            CollectionDbPath = _collectionDbPath,
            AuditSigningKeyPath = _auditKeyPath,
        });

    // ──────────────────────────────────────────
    // Parser pin tests
    // ──────────────────────────────────────────

    [Fact]
    public void Simulate_WithRoots_ReturnsSimulateCommand()
    {
        var result = CliArgsParser.Parse(["simulate", "--roots", _tempDir]);
        Assert.Equal(CliCommand.Simulate, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    [Fact]
    public void Simulate_WithOutput_SetsOutputPath()
    {
        var outPath = Path.Combine(_tempDir, "sim.json");
        var result = CliArgsParser.Parse(["simulate", "--roots", _tempDir, "-o", outPath]);
        Assert.Equal(CliCommand.Simulate, result.Command);
        Assert.Equal(outPath, result.Options!.OutputPath);
    }

    [Fact]
    public void Simulate_NoRoots_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["simulate"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--roots"));
    }

    [Fact]
    public void Simulate_PositionalRoot_Accepted()
    {
        var result = CliArgsParser.Parse(["simulate", _tempDir]);
        Assert.Equal(CliCommand.Simulate, result.Command);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    [Fact]
    public void Simulate_UnknownFlag_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["simulate", "--roots", _tempDir, "--bogus"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Unknown flag"));
    }

    // ──────────────────────────────────────────
    // Usage / discoverability
    // ──────────────────────────────────────────

    [Fact]
    public void Usage_ListsSimulateSubcommand()
    {
        using var sw = new StringWriter();
        CliOutputWriter.WriteUsage(sw);
        var usage = sw.ToString();
        Assert.Contains("simulate", usage, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────
    // End-to-end smoke: empty library
    //
    // Drives the real subcommand handler through Romulus.CLI.Program.SubcommandSimulateAsync
    // (internals visible to Romulus.Tests). Empty library => no items, summary
    // zeroed. We assert exit code 0, valid JSON shape, and the emitted JSON
    // carries the BeforeAfter projection contract (items + summary keys).
    // ──────────────────────────────────────────

    [Fact]
    public async Task SimulateSubcommand_EmptyLibrary_EmitsZeroedJsonProjection()
    {
        using var scope = IsolateCliPaths();
        var (exit, stdout, _) = await ProgramTestRunner.RunSubcommandAsync(async () =>
        {
            var opts = new CliRunOptions
            {
                Roots = new[] { _tempDir },
                Mode = "DryRun",
            };
            return await Romulus.CLI.Program.SubcommandSimulateAsync(opts).ConfigureAwait(false);
        }).ConfigureAwait(false);

        Assert.Equal(0, exit);
        Assert.False(string.IsNullOrWhiteSpace(stdout), "simulate must emit JSON to stdout");

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(0, items.GetArrayLength());
        Assert.True(root.TryGetProperty("summary", out var summary));
        Assert.True(summary.TryGetProperty("totalBefore", out var totalBefore));
        Assert.Equal(0, totalBefore.GetInt32());
        Assert.True(summary.TryGetProperty("totalAfter", out var totalAfter));
        Assert.Equal(0, totalAfter.GetInt32());
        Assert.True(summary.TryGetProperty("kept", out _));
        Assert.True(summary.TryGetProperty("removed", out _));
        Assert.True(summary.TryGetProperty("converted", out _));
        Assert.True(summary.TryGetProperty("renamed", out _));
        Assert.True(summary.TryGetProperty("potentialSavedBytes", out _));
    }

    // ──────────────────────────────────────────
    // SoT invariant: simulate must be DryRun side-effect-free
    // even when the caller passes Mode=Move on the CLI.
    //
    // The CLI handler MUST NOT perform any move; the BeforeAfterSimulator
    // ForceDryRun chokepoint is single-source-of-truth.
    // ──────────────────────────────────────────

    [Fact]
    public async Task SimulateSubcommand_ModeMove_StillSideEffectFree()
    {
        using var scope = IsolateCliPaths();
        // Drop a file we can later assert was not moved/touched.
        var rom = Path.Combine(_tempDir, "decoy.zip");
        File.WriteAllBytes(rom, new byte[16]);
        var beforeWrite = File.GetLastWriteTimeUtc(rom);

        var (exit, _, _) = await ProgramTestRunner.RunSubcommandAsync(async () =>
        {
            var opts = new CliRunOptions
            {
                Roots = new[] { _tempDir },
                Mode = "Move",
                Yes = true, // would normally be required for Move; simulate must ignore Mode
            };
            return await Romulus.CLI.Program.SubcommandSimulateAsync(opts).ConfigureAwait(false);
        }).ConfigureAwait(false);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(rom), "simulate must never delete or move source files");
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(rom));
    }
}

/// <summary>
/// Helper that captures stdout/stderr around a <see cref="Program"/>
/// subcommand call. The Program class redirects Console writes through
/// AsyncLocal&lt;TextWriter&gt; overrides; this helper wires them up so tests
/// can assert on emitted JSON without leaking into the test runner's console.
/// </summary>
internal static class ProgramTestRunner
{
    public static async Task<(int ExitCode, string Stdout, string Stderr)> RunSubcommandAsync(
        Func<Task<int>> body)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var prevOut = Console.Out;
        var prevErr = Console.Error;
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await body().ConfigureAwait(false);
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
        }
    }
}
