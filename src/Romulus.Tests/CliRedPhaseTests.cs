using System.Reflection;
using System.Text.Json;
using Romulus.CLI;
using Romulus.Infrastructure.Orchestration;
using Xunit;
using CliProgram = Romulus.CLI.Program;

namespace Romulus.Tests;

/// <summary>
/// TDD Red Phase — CLI failing tests exposing gaps in argument validation,
/// flag-eats-flag parsing, stdout/stderr separation, sidecar exports,
/// structural parity, and determinism. Issue #9.
/// </summary>
public sealed class CliRedPhaseTests : IDisposable
{
    private readonly string _tempDir;

    public CliRedPhaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cli_red_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  A: Missing-Value Validation — 9 tests (RED)
    //
    //  Bug: ParseArgs uses `if (++i < args.Length)` for 9 value-bearing
    //  flags, silently ignoring a missing trailing value instead of
    //  returning exit code 3 with an error on stderr.
    //  Contrast with --roots / --mode which correctly fail.
    //
    //  Goal:  Every value-bearing flag must fail with exit code 3 when
    //         invoked as the last argument without a value.
    //  Why RED: The `if` guard silently skips, returns (opts, 0).
    //  Fix target: src/Romulus.CLI/Program.cs  ParseArgs switch cases.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseArgs_Prefer_MissingValue_ShouldReturnExitCode3_Issue9()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsCapture(
            ["--roots", _tempDir, "--prefer"]);

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Missing value", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseArgs_Extensions_MissingValue_ShouldReturnExitCode3_Issue9()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsCapture(
            ["--roots", _tempDir, "--extensions"]);

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Missing value", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseArgs_TrashRoot_MissingValue_ShouldReturnExitCode3_Issue9()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsCapture(
            ["--roots", _tempDir, "--trashroot"]);

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Missing value", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseArgs_Report_MissingValue_ShouldReturnExitCode3_Issue9()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsCapture(
            ["--roots", _tempDir, "--report"]);

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Missing value", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseArgs_Audit_MissingValue_ShouldReturnExitCode3_Issue9()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsCapture(
            ["--roots", _tempDir, "--audit"]);

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Missing value", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseArgs_Log_MissingValue_ShouldReturnExitCode3_Issue9()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsCapture(
            ["--roots", _tempDir, "--log"]);

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Missing value", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseArgs_LogLevel_MissingValue_ShouldReturnExitCode3_Issue9()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsCapture(
            ["--roots", _tempDir, "--loglevel"]);

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Missing value", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseArgs_DatRoot_MissingValue_ShouldReturnExitCode3_Issue9()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsCapture(
            ["--roots", _tempDir, "--datroot"]);

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Missing value", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseArgs_HashType_MissingValue_ShouldReturnExitCode3_Issue9()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsCapture(
            ["--roots", _tempDir, "--hashtype"]);

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Missing value", stderr, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  B: Flag-eats-Flag — 2 tests (RED)
    //
    //  Bug: When a value-bearing flag (--prefer, --extensions) is followed
    //  by another flag (--no-removejunk), the parser swallows the flag
    //  as a plain string value. The subsequent flag is never applied.
    //
    //  Goal:  Values starting with "-" must be rejected or the following
    //         flag must still be recognized.
    //  Why RED: --no-removejunk is consumed as "region" / "extension",
    //           RemoveJunk stays at default true.
    //  Fix target: src/Romulus.CLI/Program.cs  ParseArgs value reads.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseArgs_Prefer_SwallowsNextFlag_ShouldRejectDashValue_Issue9()
    {
        // --prefer --no-removejunk → "--no-removejunk" must NOT become a region value
        var (opts, exitCode, _, _) = ParseArgsCapture(
            ["--roots", _tempDir, "--prefer", "--no-removejunk"]);

        // Bug: --no-removejunk is consumed as a region value "--no-removejunk",
        // so RemoveJunk stays at its default (true) instead of being set to false.
        Assert.NotNull(opts);
        Assert.False(opts!.RemoveJunk,
            "--no-removejunk was swallowed as a region value instead of being recognized as a flag");
    }

    [Fact]
    public void ParseArgs_Extensions_SwallowsNextFlag_ShouldRejectDashValue_Issue9()
    {
        // --extensions --no-removejunk → "--no-removejunk" must NOT become an extension
        var (opts, exitCode, _, _) = ParseArgsCapture(
            ["--roots", _tempDir, "--extensions", "--no-removejunk"]);

        // Bug: --no-removejunk is consumed as an extension string,
        // so RemoveJunk stays at its default (true) instead of being set to false.
        Assert.NotNull(opts);
        Assert.False(opts!.RemoveJunk,
            "--no-removejunk was swallowed as an extension value instead of being recognized as a flag");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  C: stdout/stderr clean separation — 2 tests
    //
    //  Goal:  DryRun writes only valid JSON to stdout; Move writes
    //         nothing to stdout but progress markers to stderr.
    //  Fix target: src/Romulus.CLI/Program.cs  Run method.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DryRun_Stdout_ContainsOnlyValidJson_Issue9()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Game (USA).zip"), "data");

        var opts = new CliRunOptions
        {
            Roots = [_tempDir],
            Mode = "DryRun"
        };

        var (exitCode, stdout, _) = RunCliCapture(opts);

        Assert.Equal(0, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(stdout), "stdout must not be empty for DryRun");

        var trimmed = stdout.Trim();
        Assert.StartsWith("{", trimmed);
        Assert.EndsWith("}", trimmed);
        using var doc = JsonDocument.Parse(trimmed);
        Assert.True(doc.RootElement.TryGetProperty("Status", out _));
    }

    [Fact]
    public void Move_Stderr_ContainsDoneMarker_Stdout_Empty_Issue9()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Game (USA).zip"), "data");
        File.WriteAllText(Path.Combine(_tempDir, "Game (Europe).zip"), "eu");

        var opts = new CliRunOptions
        {
            Roots = [_tempDir],
            Mode = "Move",
            Yes = true,
            PreferRegions = ["US"]
        };

        var (exitCode, stdout, stderr) = RunCliCapture(opts);

        Assert.True(string.IsNullOrWhiteSpace(stdout),
            $"Move mode must not write to stdout, but got: {stdout.Trim()}");
        Assert.Contains("[Done]", stderr);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  D: Sidecar / Export — 2 tests
    //
    //  Goal:  Move with --audit creates a non-empty CSV file;
    //         DryRun with --report creates a non-empty report file.
    //  Fix target: src/Romulus.CLI/Program.cs + RunOrchestrator.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Move_WithAuditPath_CreatesAuditCsvFile_Issue9()
    {
        var runRoot = Path.Combine(_tempDir, "move-audit-root");
        Directory.CreateDirectory(runRoot);
        File.WriteAllText(Path.Combine(runRoot, "Game (USA).zip"), "usa");
        File.WriteAllText(Path.Combine(runRoot, "Game (Europe).zip"), "eu");

        var outputsDir = Path.Combine(_tempDir, "outputs");
        Directory.CreateDirectory(outputsDir);
        var auditPath = Path.Combine(outputsDir, "audit-test.csv");
        var opts = new CliRunOptions
        {
            Roots = [runRoot],
            Mode = "Move",
            Yes = true,
            PreferRegions = ["US"],
            AuditPath = auditPath
        };

        var (exitCode, _, _) = RunCliCapture(opts);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(auditPath), $"Audit CSV must be created at {auditPath}");

        var content = File.ReadAllText(auditPath);
        Assert.False(string.IsNullOrWhiteSpace(content), "Audit CSV must not be empty");
    }

    [Fact]
    public void DryRun_WithReportPath_CreatesReportFile_Issue9()
    {
        var runRoot = Path.Combine(_tempDir, "dryrun-report-root");
        Directory.CreateDirectory(runRoot);
        File.WriteAllText(Path.Combine(runRoot, "Game (USA).zip"), "usa");

        var outputsDir = Path.Combine(_tempDir, "outputs");
        Directory.CreateDirectory(outputsDir);
        var reportPath = Path.Combine(outputsDir, "report-test.html");
        var opts = new CliRunOptions
        {
            Roots = [runRoot],
            Mode = "DryRun",
            ReportPath = reportPath
        };

        var (exitCode, _, _) = RunCliCapture(opts);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(reportPath), $"Report file must be created at {reportPath}");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  E: Structural Parity — 1 test
    //
    //  Goal:  Every RunProjection field must appear in the CLI DryRun JSON.
    //         If a field is added to RunProjection but forgotten in the CLI
    //         anonymous-object serialization, this test catches it.
    //  Fix target: src/Romulus.CLI/Program.cs  DryRun JSON block.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CliDryRunJson_ContainsAllRunProjectionFields_Issue9()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Game (USA).zip"), "data");

        var opts = new CliRunOptions
        {
            Roots = [_tempDir],
            Mode = "DryRun"
        };

        var (exitCode, stdout, _) = RunCliCapture(opts);
        Assert.Equal(0, exitCode);

        using var doc = JsonDocument.Parse(stdout.Trim());
        var jsonProps = doc.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        // RunProjection is a positional record — its constructor parameters
        // mirror its public property names in PascalCase.
        var projectionFields = typeof(RunProjection)
            .GetConstructors()[0]
            .GetParameters()
            .Select(p => p.Name!)
            .ToList();

        foreach (var field in projectionFields)
        {
            Assert.True(jsonProps.Contains(field),
                $"CLI DryRun JSON is missing RunProjection field '{field}'");
        }
    }

    [Fact]
    public void CliDryRunJson_AliasFieldsMatchCanonicalCounters_Issue9()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Game (USA).zip"), "usa");
        File.WriteAllText(Path.Combine(_tempDir, "Game (Europe).zip"), "eu");

        var opts = new CliRunOptions
        {
            Roots = [_tempDir],
            Mode = "DryRun"
        };

        var (exitCode, stdout, _) = RunCliCapture(opts);
        Assert.Equal(0, exitCode);

        using var doc = JsonDocument.Parse(stdout.Trim());
        var root = doc.RootElement;

        Assert.Equal(root.GetProperty("Keep").GetInt32(), root.GetProperty("Winners").GetInt32());
        Assert.Equal(root.GetProperty("Dupes").GetInt32(), root.GetProperty("Losers").GetInt32());
    }

    // ═══════════════════════════════════════════════════════════════════
    //  F: Determinism — 1 test
    //
    //  Goal:  Same inputs produce identical JSON output (except DurationMs).
    //  Fix target: Entire pipeline (Core, Infrastructure, CLI).
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DryRun_SameInputTwice_ProducesIdenticalJsonExceptDuration_Issue9()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Game (USA).zip"), "usa");
        File.WriteAllText(Path.Combine(_tempDir, "Game (Europe).zip"), "eu");

        var opts = new CliRunOptions
        {
            Roots = [_tempDir],
            Mode = "DryRun",
            PreferRegions = ["EU", "US"]
        };

        var (exit1, stdout1, _) = RunCliCapture(opts);
        var (exit2, stdout2, _) = RunCliCapture(opts);

        Assert.Equal(0, exit1);
        Assert.Equal(0, exit2);

        using var doc1 = ParseCliJsonDocument(stdout1);
        using var doc2 = ParseCliJsonDocument(stdout2);

        foreach (var prop in doc1.RootElement.EnumerateObject())
        {
            if (prop.Name == "DurationMs") continue;

            Assert.True(doc2.RootElement.TryGetProperty(prop.Name, out var val2),
                $"Second run missing field '{prop.Name}'");

            Assert.Equal(prop.Value.ToString(), val2.ToString());
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static (CliRunOptions? Options, int ExitCode, string Stdout, string Stderr)
        ParseArgsCapture(string[] args)
    {
        lock (SharedTestLocks.ConsoleLock)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            try
            {
                CliProgram.SetConsoleOverrides(stdout, stderr);
                var (options, exitCode) = CliProgram.ParseArgs(args);
                return (options, exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                CliProgram.SetConsoleOverrides(null, null);
            }
        }
    }

    private static (int ExitCode, string Stdout, string Stderr)
        RunCliCapture(CliRunOptions options)
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

    private static JsonDocument ParseCliJsonDocument(string stdout)
    {
        var trimmed = stdout.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            return JsonDocument.Parse(trimmed[start..(end + 1)]);

        return JsonDocument.Parse(trimmed);
    }

    // --- SEC-CLI-03: Drive root validation (parity with API) ---

    [Fact]
    public void ParseArgs_DriveRoot_ShouldReturnExitCode3()
    {
        var (opts, exitCode, _, stderr) = ParseArgsCapture(
            ["--roots", @"C:\"]);

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Contains("Drive root not allowed", stderr, StringComparison.OrdinalIgnoreCase);
    }
}
