using RomCleanup.CLI;
using Xunit;
using CliProgram = RomCleanup.CLI.Program;
using System.Reflection;

namespace RomCleanup.Tests;

/// <summary>
/// V2-TEST-H02: CLI argument parsing, exit codes, and validation tests.
/// Tests ParseArgs directly via InternalsVisibleTo.
/// </summary>
public sealed class CliProgramTests : IDisposable
{
    private readonly string _tempDir;

    public CliProgramTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cli_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ═══ ParseArgs: No arguments ═══════════════════════════════════════

    [Fact]
    public void ParseArgs_NoArgs_ReturnsNullWithExitCode0()
    {
        var (opts, exitCode) = CliProgram.ParseArgs(Array.Empty<string>());
        Assert.Null(opts);
        Assert.Equal(0, exitCode);
    }

    // ═══ ParseArgs: Help flags ═════════════════════════════════════════

    [Theory]
    [InlineData("-help")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void ParseArgs_HelpFlag_ReturnsNullWithExitCode0(string flag)
    {
        var (opts, exitCode) = CliProgram.ParseArgs(new[] { flag });
        Assert.Null(opts);
        Assert.Equal(0, exitCode);
    }

    // ═══ ParseArgs: Mode parsing ═══════════════════════════════════════

    [Fact]
    public void ParseArgs_ModeDryRun_SetsMode()
    {
        var (opts, exitCode) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--mode", "DryRun" });
        Assert.NotNull(opts);
        Assert.Equal(0, exitCode);
        Assert.Equal("DryRun", opts!.Mode);
    }

    [Fact]
    public void ParseArgs_ModeMove_SetsMode()
    {
        var (opts, exitCode) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--mode", "Move" });
        Assert.NotNull(opts);
        Assert.Equal("Move", opts!.Mode);
    }

    [Fact]
    public void ParseArgs_InvalidMode_ReturnsExitCode3()
    {
        var (opts, exitCode) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--mode", "Delete" });
        Assert.Null(opts);
        Assert.Equal(3, exitCode);
    }

    [Fact]
    public void ParseArgs_MissingModeValue_ReturnsExitCode3_AndWriteOnlyStderr()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsWithCapturedConsole(new[] { "--roots", _tempDir, "--mode" });

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Missing value for --mode", stderr, StringComparison.OrdinalIgnoreCase);
    }

    // ═══ ParseArgs: Default mode ═══════════════════════════════════════

    [Fact]
    public void ParseArgs_NoMode_DefaultsToDryRun()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir });
        Assert.NotNull(opts);
        Assert.Equal("DryRun", opts!.Mode);
    }

    // ═══ ParseArgs: Roots ══════════════════════════════════════════════

    [Fact]
    public void ParseArgs_SingleRoot_Parsed()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir });
        Assert.NotNull(opts);
        Assert.Single(opts!.Roots);
        Assert.Contains(_tempDir, opts.Roots);
    }

    [Fact]
    public void ParseArgs_EmptyRoot_ReturnsExitCode3()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsWithCapturedConsole(new[] { "--roots", ";" });

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("No valid root paths", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(" ; ")]
    [InlineData(" ; ; ")]
    public void ParseArgs_DegenerateRoots_ReturnExitCode3_AndWriteOnlyStderr(string rootsArg)
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsWithCapturedConsole(new[] { "--roots", rootsArg });

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("No valid root paths", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseArgs_MissingRootsValue_ReturnsExitCode3_AndWriteOnlyStderr()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsWithCapturedConsole(new[] { "--roots" });

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Missing value for --roots", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseArgs_MultipleRoots_SemicolonSeparated()
    {
        var dir2 = Path.Combine(Path.GetTempPath(), $"cli_test2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir2);
        try
        {
            var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", $"{_tempDir};{dir2}" });
            Assert.NotNull(opts);
            Assert.Equal(2, opts!.Roots.Length);
        }
        finally
        {
            Directory.Delete(dir2, true);
        }
    }

    [Fact]
    public void ParseArgs_NonexistentRoot_ReturnsExitCode3()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}");
        var (opts, exitCode) = CliProgram.ParseArgs(new[] { "--roots", fakePath });
        Assert.Null(opts);
        Assert.Equal(3, exitCode);
    }

    // ═══ ParseArgs: System path protection ═════════════════════════════

    [Fact]
    public void ParseArgs_WindowsDir_Blocked()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(winDir) && Directory.Exists(winDir))
        {
            var (opts, exitCode) = CliProgram.ParseArgs(new[] { "--roots", winDir });
            Assert.Null(opts);
            Assert.Equal(3, exitCode);
        }
    }

    [Fact]
    public void ParseArgs_ProgramFilesDir_Blocked()
    {
        var progDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(progDir) && Directory.Exists(progDir))
        {
            var (opts, exitCode) = CliProgram.ParseArgs(new[] { "--roots", progDir });
            Assert.Null(opts);
            Assert.Equal(3, exitCode);
        }
    }

    // ═══ ParseArgs: Regions ════════════════════════════════════════════

    [Fact]
    public void ParseArgs_PreferRegions_CommaSeparated()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--prefer", "US,JP,EU" });
        Assert.NotNull(opts);
        Assert.Equal(new[] { "US", "JP", "EU" }, opts!.PreferRegions);
    }

    [Fact]
    public void ParseArgs_NoPreferOverride_LeavesRegionsEmptyForSettingsFallback()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir });
        Assert.NotNull(opts);
        Assert.Empty(opts!.PreferRegions);
    }

    // ═══ ParseArgs: Extensions ═════════════════════════════════════════

    [Fact]
    public void ParseArgs_Extensions_AutoPrefixesDot()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--extensions", ".chd,iso,zip" });
        Assert.NotNull(opts);
        Assert.Contains(".chd", opts!.Extensions);
        Assert.Contains(".iso", opts!.Extensions);
        Assert.Contains(".zip", opts!.Extensions);
        Assert.True(opts.ExtensionsExplicit);
    }

    [Fact]
    public void ParseArgs_NoExtensions_UsesDefaults()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir });
        Assert.NotNull(opts);
        Assert.False(opts!.ExtensionsExplicit);
        Assert.True(opts.Extensions.Count > 0, "Default extensions must be populated");
    }

    // ═══ ParseArgs: Boolean flags ══════════════════════════════════════

    [Theory]
    [InlineData("--removejunk")]
    [InlineData("-removejunk")]
    public void ParseArgs_RemoveJunk_SetsFlag(string flag)
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, flag });
        Assert.NotNull(opts);
        Assert.True(opts!.RemoveJunk);
    }

    [Theory]
    [InlineData("--no-removejunk")]
    [InlineData("-no-removejunk")]
    public void ParseArgs_NoRemoveJunk_ClearsFlag(string flag)
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, flag });
        Assert.NotNull(opts);
        Assert.False(opts!.RemoveJunk);
    }

    [Theory]
    [InlineData("--aggressivejunk")]
    [InlineData("-aggressivejunk")]
    public void ParseArgs_AggressiveJunk_SetsFlag(string flag)
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, flag });
        Assert.NotNull(opts);
        Assert.True(opts!.AggressiveJunk);
    }

    [Fact]
    public void ParseArgs_SortConsole_SetsFlag()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--sortconsole" });
        Assert.NotNull(opts);
        Assert.True(opts!.SortConsole);
    }

    [Fact]
    public void ParseArgs_EnableDat_SetsFlag()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--enabledat" });
        Assert.NotNull(opts);
        Assert.True(opts!.EnableDat);
    }

    [Fact]
    public void ParseArgs_EnableDatRename_SetsFlag_Issue9()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--datrename" });
        Assert.NotNull(opts);
        Assert.True(opts!.EnableDatRename);
    }

    [Fact]
    public void ParseArgs_EnableDatAudit_SetsFlag_Issue9()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--dat-audit" });
        Assert.NotNull(opts);
        Assert.True(opts!.EnableDatAudit);
    }

    [Fact]
    public void ParseArgs_ConvertFormat_SetsFlag()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--convertformat" });
        Assert.NotNull(opts);
        Assert.True(opts!.ConvertFormat);
    }

    [Fact]
    public void ParseArgs_ConvertOnly_SetsFlags()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--convertonly" });
        Assert.NotNull(opts);
        Assert.True(opts!.ConvertOnly);
        Assert.True(opts.ConvertFormat);
    }

    [Fact]
    public void ParseArgs_ApproveReviews_SetsFlag()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--approve-reviews" });
        Assert.NotNull(opts);
        Assert.True(opts!.ApproveReviews);
    }

    [Fact]
    public void ParseArgs_Yes_SetsFlag()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--yes" });
        Assert.NotNull(opts);
        Assert.True(opts!.Yes);
    }

    [Fact]
    public void ParseArgs_GamesOnly_SetsFlag()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--gamesonly" });
        Assert.NotNull(opts);
        Assert.True(opts!.OnlyGames);
    }

    [Fact]
    public void ParseArgs_DropUnknown_DisablesUnknownKeepPolicy()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--gamesonly", "--dropunknown" });
        Assert.NotNull(opts);
        Assert.True(opts!.OnlyGames);
        Assert.False(opts.KeepUnknownWhenOnlyGames);
    }

    [Theory]
    [InlineData("Rename")]
    [InlineData("Skip")]
    [InlineData("Overwrite")]
    public void ParseArgs_ConflictPolicy_SetsValue(string policy)
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--conflictpolicy", policy });
        Assert.NotNull(opts);
        Assert.Equal(policy, opts!.ConflictPolicy);
    }

    [Fact]
    public void ParseArgs_InvalidConflictPolicy_ReturnsExitCode3()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsWithCapturedConsole(
            new[] { "--roots", _tempDir, "--conflictpolicy", "Delete" });

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Invalid conflict policy", stderr, StringComparison.OrdinalIgnoreCase);
    }

    // ═══ ParseArgs: Path options ═══════════════════════════════════════

    [Fact]
    public void ParseArgs_TrashRoot_SetsPath()
    {
        var trash = Path.Combine(_tempDir, "trash");
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--trashroot", trash });
        Assert.NotNull(opts);
        Assert.Equal(trash, opts!.TrashRoot);
    }

    [Fact]
    public void ParseArgs_DatRoot_SetsPath()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--datroot", _tempDir });
        Assert.NotNull(opts);
        Assert.Equal(_tempDir, opts!.DatRoot);
    }

    [Fact]
    public void ParseArgs_ReportPath_SetsPath()
    {
        var report = Path.Combine(_tempDir, "report.html");
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--report", report });
        Assert.NotNull(opts);
        Assert.Equal(report, opts!.ReportPath);
    }

    [Fact]
    public void ParseArgs_AuditPath_SetsPath()
    {
        var audit = Path.Combine(_tempDir, "audit.csv");
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--audit", audit });
        Assert.NotNull(opts);
        Assert.Equal(audit, opts!.AuditPath);
    }

    [Fact]
    public void ParseArgs_LogPath_SetsPath()
    {
        var log = Path.Combine(_tempDir, "run.jsonl");
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--log", log });
        Assert.NotNull(opts);
        Assert.Equal(log, opts!.LogPath);
    }

    [Fact]
    public void ParseArgs_UncRootPath_ReturnsExitCode3()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsWithCapturedConsole(
            new[] { "--roots", "\\\\server\\share\\roms" });

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("UNC", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseArgs_LogPath_InProtectedSystemPath_ReturnsExitCode3()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(winDir) || !Directory.Exists(winDir))
            return;

        var protectedLog = Path.Combine(winDir, "Temp", "romcleanup-test.jsonl");
        var (opts, exitCode, stdout, stderr) = ParseArgsWithCapturedConsole(
            new[] { "--roots", _tempDir, "--log", protectedLog });

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("protected system path", stderr, StringComparison.OrdinalIgnoreCase);
    }

    // ═══ ParseArgs: LogLevel ═══════════════════════════════════════════

    [Theory]
    [InlineData("Debug")]
    [InlineData("Info")]
    [InlineData("Warning")]
    [InlineData("Error")]
    public void ParseArgs_LogLevel_SetsLevel(string level)
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--loglevel", level });
        Assert.NotNull(opts);
        Assert.Equal(level, opts!.LogLevel);
    }

    [Fact]
    public void ParseArgs_DefaultLogLevel_IsInfo()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir });
        Assert.NotNull(opts);
        Assert.Equal("Info", opts!.LogLevel);
    }

    [Fact]
    public void ParseArgs_InvalidLogLevel_ReturnsExitCode3()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsWithCapturedConsole(
            new[] { "--roots", _tempDir, "--loglevel", "Verbose" });

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Invalid log level", stderr, StringComparison.OrdinalIgnoreCase);
    }

    // ═══ ParseArgs: HashType ═══════════════════════════════════════════

    [Theory]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    [InlineData("MD5")]
    public void ParseArgs_HashType_SetsType(string hashType)
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--hashtype", hashType });
        Assert.NotNull(opts);
        Assert.Equal(hashType, opts!.HashType);
    }

    [Fact]
    public void ParseArgs_InvalidHashType_ReturnsExitCode3()
    {
        var (opts, exitCode, stdout, stderr) = ParseArgsWithCapturedConsole(
            new[] { "--roots", _tempDir, "--hashtype", "SHA2" });

        Assert.Null(opts);
        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("Invalid hash type", stderr, StringComparison.OrdinalIgnoreCase);
    }

    // ═══ ParseArgs: Unknown flag ═══════════════════════════════════════

    [Fact]
    public void ParseArgs_UnknownFlag_ReturnsExitCode3()
    {
        var (opts, exitCode) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--unknownflag" });
        Assert.Null(opts);
        Assert.Equal(3, exitCode);
    }

    // ═══ ParseArgs: Positional root ════════════════════════════════════

    [Fact]
    public void ParseArgs_PositionalArg_TreatedAsRoot()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { _tempDir });
        Assert.NotNull(opts);
        Assert.Contains(_tempDir, opts!.Roots);
    }

    // ═══ ParseArgs: Combined flags ═════════════════════════════════════

    [Fact]
    public void ParseArgs_AllFlags_CombinedCorrectly()
    {
        var report = Path.Combine(_tempDir, "report.html");
        var (opts, exitCode) = CliProgram.ParseArgs(new[]
        {
            "--roots", _tempDir,
            "--mode", "DryRun",
            "--prefer", "JP,US",
            "--removejunk",
            "--aggressivejunk",
            "--sortconsole",
            "--enabledat",
            "--hashtype", "SHA256",
            "--report", report,
            "--loglevel", "Debug"
        });

        Assert.NotNull(opts);
        Assert.Equal(0, exitCode);
        Assert.Equal("DryRun", opts!.Mode);
        Assert.Equal(new[] { "JP", "US" }, opts.PreferRegions);
        Assert.True(opts.RemoveJunk);
        Assert.True(opts.AggressiveJunk);
        Assert.True(opts.SortConsole);
        Assert.True(opts.EnableDat);
        Assert.Equal("SHA256", opts.HashType);
        Assert.Equal(report, opts.ReportPath);
        Assert.Equal("Debug", opts.LogLevel);
    }

    // ═══ ParseArgs: Case insensitivity ═════════════════════════════════

    [Theory]
    [InlineData("-Roots")]
    [InlineData("-ROOTS")]
    [InlineData("--ROOTS")]
    public void ParseArgs_FlagsCaseInsensitive(string rootsFlag)
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { rootsFlag, _tempDir });
        Assert.NotNull(opts);
        Assert.Contains(_tempDir, opts!.Roots);
    }

    // ═══ CliOptions: Default values ════════════════════════════════════

    [Fact]
    public void CliOptions_Defaults_AreCorrect()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir });
        Assert.NotNull(opts);
        Assert.Equal("DryRun", opts!.Mode);
        Assert.True(opts.RemoveJunk);
        Assert.False(opts.AggressiveJunk);
        Assert.False(opts.SortConsole);
        Assert.False(opts.EnableDat);
        Assert.False(opts.ConvertFormat);
        Assert.False(opts.Yes);
        Assert.Null(opts.TrashRoot);
        Assert.Null(opts.DatRoot);
        Assert.Null(opts.HashType);
        Assert.Null(opts.ReportPath);
        Assert.Null(opts.AuditPath);
        Assert.Null(opts.LogPath);
        Assert.Equal("Info", opts.LogLevel);
    }

    [Fact]
    public void RunForTests_MoveNonInteractive_WithoutYes_ReturnsExitCode3()
    {
        lock (SharedTestLocks.ConsoleLock)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var originalOut = Console.Out;
            var originalErr = Console.Error;

            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                CliProgram.SetNonInteractiveOverride(true);

                var exitCode = CliProgram.RunForTests(new CliRunOptions
                {
                    Roots = new[] { _tempDir },
                    Mode = "Move"
                });

                Assert.Equal(3, exitCode);
                Assert.Contains("requires --yes", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CliProgram.SetNonInteractiveOverride(null);
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
    }

    [Fact]
    public void RunForTests_WithLogPath_CreatesJsonlLog()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Game (USA).zip"), "data");
        var logPath = Path.Combine(_tempDir, "run.jsonl");

        var exitCode = CliProgram.RunForTests(new CliRunOptions
        {
            Roots = new[] { _tempDir },
            Mode = "DryRun",
            LogPath = logPath,
            LogLevel = "Info"
        });

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(logPath));
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(logPath)));
    }

    [Fact]
    public void Main_Version_WritesAssemblyVersion()
    {
        using var stdout = new StringWriter();

        try
        {
            CliProgram.SetConsoleOverrides(stdout, null);
            var main = typeof(CliProgram).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(main);

            var exitCode = (int)(main!.Invoke(null, new object[] { new[] { "--version" } }) ?? -1);
            var output = stdout.ToString().Trim();

            Assert.Equal(0, exitCode);
            Assert.False(string.IsNullOrWhiteSpace(output));
            Assert.Matches("^[0-9]+\\.[0-9]+\\.[0-9]+", output);
        }
        finally
        {
            CliProgram.SetConsoleOverrides(null, null);
        }
    }

    private static (CliRunOptions? Options, int ExitCode, string Stdout, string Stderr) ParseArgsWithCapturedConsole(string[] args)
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
