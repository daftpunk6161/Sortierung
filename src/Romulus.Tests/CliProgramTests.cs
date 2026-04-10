using System.Reflection;
using System.Text.Json;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Xunit;
using CliProgram = Romulus.CLI.Program;

namespace Romulus.Tests;

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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(99, 1)]
    public void NormalizeProcessExitCode_PreservesDocumentedRange(int raw, int expected)
    {
        Assert.Equal(expected, CliProgram.NormalizeProcessExitCode(raw));
    }

    [Fact]
    public void BuildRunMutexName_NormalizesRootScopeDeterministically()
    {
        var nestedRoot = Path.Combine(_tempDir, "Nested");
        Directory.CreateDirectory(nestedRoot);

        var first = CliProgram.BuildRunMutexName(new[] { _tempDir, nestedRoot });
        var second = CliProgram.BuildRunMutexName(new[] { nestedRoot, _tempDir.ToUpperInvariant() });

        Assert.Equal(first, second);
        Assert.StartsWith("Global\\Romulus.Cli.Run.", first, StringComparison.Ordinal);
    }

    [Fact]
    public void RunForTests_WhenRunMutexAlreadyHeld_ReturnsExitCode3()
    {
        lock (SharedTestLocks.ConsoleLock)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            try
            {
                CliProgram.SetConsoleOverrides(stdout, stderr);

                var mutexName = CliProgram.BuildRunMutexName(new[] { _tempDir });
                using var heldMutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
                Assert.True(createdNew);

                var exitCode = CliProgram.RunForTests(new CliRunOptions
                {
                    Roots = new[] { _tempDir },
                    Mode = "DryRun"
                });

                Assert.Equal(3, exitCode);
                Assert.Contains("already active", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CliProgram.SetConsoleOverrides(null, null);
            }
        }
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
    public void CliArgsParser_UpdateDats_NoRoots_ReturnsUpdateDatsCommand()
    {
        var result = CliArgsParser.Parse(new[] { "--update-dats", "--datroot", _tempDir });

        Assert.Equal(CliCommand.UpdateDats, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.True(result.Options!.UpdateDats);
        Assert.Equal(_tempDir, result.Options.DatRoot);
    }

    [Fact]
    public void CliArgsParser_UpdateDats_WithMissingImportFolder_ReturnsValidationError()
    {
        var missingImport = Path.Combine(_tempDir, "missing-import");

        var result = CliArgsParser.Parse(new[]
        {
            "--update-dats",
            "--datroot", _tempDir,
            "--import-packs-from", missingImport
        });

        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Import-Packs directory not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CliArgsParser_UpdateDats_SmartMode_ParsesStaleThreshold()
    {
        var result = CliArgsParser.Parse(new[]
        {
            "--update-dats",
            "--datroot", _tempDir,
            "--smart-dat-update",
            "--dat-stale-days", "90"
        });

        Assert.Equal(CliCommand.UpdateDats, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.True(result.Options!.SmartDatUpdate);
        Assert.Equal(90, result.Options.DatStaleDays);
    }

    [Fact]
    public void CliArgsParser_UpdateDats_InvalidStaleThreshold_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(new[]
        {
            "--update-dats",
            "--datroot", _tempDir,
            "--smart-dat-update",
            "--dat-stale-days", "0"
        });

        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid DAT stale threshold", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CliArgsParser_History_ParsesLimitOffsetAndOutput()
    {
        var outputPath = Path.Combine(_tempDir, "history.json");

        var result = CliArgsParser.Parse(new[]
        {
            "history",
            "--offset", "5",
            "--limit", "25",
            "--output", outputPath
        });

        Assert.Equal(CliCommand.History, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.Equal(5, result.Options!.HistoryOffset);
        Assert.Equal(25, result.Options.HistoryLimit);
        Assert.Equal(outputPath, result.Options.OutputPath);
    }

    [Fact]
    public void CliArgsParser_Watch_ParsesIntervalDebounceAndMode()
    {
        var result = CliArgsParser.Parse(new[]
        {
            "watch",
            "--roots", _tempDir,
            "--interval", "15",
            "--debounce", "7",
            "--mode", "Move",
            "--yes",
            "--approve-reviews"
        });

        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.Equal(15, result.Options!.WatchIntervalMinutes);
        Assert.Equal(7, result.Options.WatchDebounceSeconds);
        Assert.Equal("Move", result.Options.Mode);
        Assert.True(result.Options.Yes);
        Assert.True(result.Options.ApproveReviews);
    }

    [Fact]
    public void CliArgsParser_Watch_RequiresSchedule()
    {
        var result = CliArgsParser.Parse(new[]
        {
            "watch",
            "--roots", _tempDir
        });

        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, error => error.Contains("--interval <minutes> or --cron", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HistoryForTests_WritesPagedSnapshotJson_ToStdout()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var index = new FakeCollectionIndex(
        [
            new CollectionRunSnapshot
            {
                RunId = "run-new",
                StartedUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                CompletedUtc = new DateTime(2026, 4, 1, 10, 1, 0, DateTimeKind.Utc),
                Mode = "Move",
                Status = "completed_with_errors",
                Roots = [@"C:\Roms\SNES"],
                RootFingerprint = "abc123",
                DurationMs = 60000,
                TotalFiles = 100,
                CollectionSizeBytes = 333000000,
                Games = 80,
                Dupes = 20,
                Junk = 5,
                DatMatches = 70,
                ConvertedCount = 10,
                FailCount = 2,
                SavedBytes = 1234,
                ConvertSavedBytes = 5678,
                HealthScore = 90
            },
            new CollectionRunSnapshot
            {
                RunId = "run-old",
                StartedUtc = new DateTime(2026, 3, 31, 10, 0, 0, DateTimeKind.Utc),
                CompletedUtc = new DateTime(2026, 3, 31, 10, 1, 0, DateTimeKind.Utc),
                Mode = "DryRun",
                Status = "ok",
                Roots = [@"D:\Roms\NES"],
                RootFingerprint = "def456",
                DurationMs = 30000,
                TotalFiles = 50,
                CollectionSizeBytes = 111000000,
                Games = 40,
                Dupes = 10,
                Junk = 0,
                DatMatches = 35,
                ConvertedCount = 0,
                FailCount = 0,
                SavedBytes = 0,
                ConvertSavedBytes = 0,
                HealthScore = 95
            }
        ]);

        try
        {
            CliProgram.SetConsoleOverrides(stdout, stderr);

            var exitCode = CliProgram.HistoryForTests(new CliRunOptions
            {
                HistoryOffset = 0,
                HistoryLimit = 1
            }, index);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var doc = JsonDocument.Parse(stdout.ToString());
            var root = doc.RootElement;
            Assert.Equal(2, root.GetProperty("Total").GetInt32());
            Assert.Equal(1, root.GetProperty("Returned").GetInt32());
            Assert.True(root.GetProperty("HasMore").GetBoolean());
            Assert.Equal("run-new", root.GetProperty("Runs")[0].GetProperty("RunId").GetString());
            Assert.Equal(333000000L, root.GetProperty("Runs")[0].GetProperty("CollectionSizeBytes").GetInt64());
        }
        finally
        {
            CliProgram.SetConsoleOverrides(null, null);
        }
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
        Assert.Equal("auto", opts!.ConvertFormat);
    }

    [Fact]
    public void ParseArgs_ConvertOnly_SetsFlags()
    {
        var (opts, _) = CliProgram.ParseArgs(new[] { "--roots", _tempDir, "--convertonly" });
        Assert.NotNull(opts);
        Assert.True(opts!.ConvertOnly);
        Assert.Equal("auto", opts.ConvertFormat);
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

        var protectedLog = Path.Combine(winDir, "Temp", "romulus-test.jsonl");
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
        Assert.Null(opts.ConvertFormat);
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
    public void Main_MoveInteractive_WithoutYes_UserDeclines_ReturnsExitCode2()
    {
        lock (SharedTestLocks.ConsoleLock)
        {
            using var stdin = new StringReader("n" + Environment.NewLine);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var originalIn = Console.In;
            var originalOut = Console.Out;
            var originalErr = Console.Error;

            try
            {
                Console.SetIn(stdin);
                Console.SetOut(stdout);
                Console.SetError(stderr);
                CliProgram.SetNonInteractiveOverride(false);

                var main = typeof(CliProgram).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.NotNull(main);

                var exitCode = UnwrapMainResult(main!.Invoke(null, new object[] { new[] { "--roots", _tempDir, "--mode", "Move" } }));

                Assert.Equal(2, exitCode);
                Assert.Contains("Execute mode will move files", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Aborted by user", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CliProgram.SetNonInteractiveOverride(null);
                Console.SetIn(originalIn);
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

            var exitCode = UnwrapMainResult(main!.Invoke(null, new object[] { new[] { "--version" } }));
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

    private static int UnwrapMainResult(object? invocationResult)
        => invocationResult switch
        {
            int exitCode => exitCode,
            Task<int> exitCodeTask => exitCodeTask.GetAwaiter().GetResult(),
            _ => -1
        };

    // ═══ TGAP-06: BUG-10 – ExtractFirstCsvField with RFC-4180 quoting ═══

    [Theory]
    [InlineData("C:\\Games\\NES,MOVE,file.zip", "C:\\Games\\NES")]
    [InlineData("\"C:\\Games,ROMs\\NES\",MOVE,file.zip", "C:\\Games,ROMs\\NES")]
    [InlineData("\"C:\\Path \"\"quoted\"\"\",MOVE,file.zip", "C:\\Path \"quoted\"")]
    [InlineData("SimpleRoot,MOVE,file.zip", "SimpleRoot")]
    [InlineData("", "")]
    public void ExtractFirstCsvField_HandlesQuotedPaths(string line, string expected)
    {
        var result = CliProgram.ExtractFirstCsvField(line);
        Assert.Equal(expected, result);
    }

    private sealed class FakeCollectionIndex : ICollectionIndex
    {
        private readonly IReadOnlyList<CollectionRunSnapshot> _snapshots;

        public FakeCollectionIndex(IReadOnlyList<CollectionRunSnapshot> snapshots)
        {
            _snapshots = snapshots;
        }

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new CollectionIndexMetadata());

        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(0);

        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => ValueTask.FromResult<CollectionIndexEntry?>(null);

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(Array.Empty<CollectionIndexEntry>());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(Array.Empty<CollectionIndexEntry>());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(IReadOnlyList<string> roots, IReadOnlyCollection<string> extensions, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(Array.Empty<CollectionIndexEntry>());

        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc, CancellationToken ct = default)
            => ValueTask.FromResult<CollectionHashCacheEntry?>(null);

        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_snapshots.Count);

        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionRunSnapshot>>(_snapshots.Take(limit).ToArray());
    }
}
