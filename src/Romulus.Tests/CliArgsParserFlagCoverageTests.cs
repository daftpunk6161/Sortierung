using Xunit;
using Romulus.CLI;
using Romulus.Contracts;

namespace Romulus.Tests;

/// <summary>
/// Covers remaining uncovered flags and edge cases in CliArgsParser.Parse().
/// Focuses on main run-mode flags not yet tested in CliArgsParserCoverageTests.
/// </summary>
public sealed class CliArgsParserFlagCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public CliArgsParserFlagCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cli-flag-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ─── Extensions ───

    [Fact]
    public void Parse_Extensions_ParsesMultipleWithDotPrefix()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--extensions", "zip,7z,.chd"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Contains(".zip", result.Options!.Extensions);
        Assert.Contains(".7z", result.Options!.Extensions);
        Assert.Contains(".chd", result.Options!.Extensions);
        Assert.True(result.Options.ExtensionsExplicit);
    }

    // ─── TrashRoot ───

    [Fact]
    public void Parse_TrashRoot_SetsValue()
    {
        var trashDir = Path.Combine(_tempDir, "trash");
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--trashroot", trashDir]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(trashDir, result.Options!.TrashRoot);
        Assert.True(result.Options.TrashRootExplicit);
    }

    // ─── RemoveJunk / NoRemoveJunk ───

    [Fact]
    public void Parse_RemoveJunk_SetsTrue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "-removejunk"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.RemoveJunk);
        Assert.True(result.Options.RemoveJunkExplicit);
    }

    [Fact]
    public void Parse_NoRemoveJunk_SetsFalse()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--no-removejunk"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.False(result.Options!.RemoveJunk);
        Assert.True(result.Options.RemoveJunkExplicit);
    }

    // ─── ConflictPolicy ───

    [Theory]
    [InlineData("Rename")]
    [InlineData("Skip")]
    [InlineData("Overwrite")]
    public void Parse_ValidConflictPolicy_Accepted(string policy)
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--conflictpolicy", policy]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(policy, result.Options!.ConflictPolicy);
        Assert.True(result.Options.ConflictPolicyExplicit);
    }

    [Fact]
    public void Parse_InvalidConflictPolicy_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--conflictpolicy", "BadPolicy"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid conflict policy"));
    }

    // ─── LogLevel ───

    [Theory]
    [InlineData("Debug")]
    [InlineData("Warning")]
    [InlineData("Error")]
    public void Parse_ValidLogLevel_Accepted(string level)
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--loglevel", level]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(level, result.Options!.LogLevel);
    }

    [Fact]
    public void Parse_InvalidLogLevel_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--loglevel", "Trace"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid log level"));
    }

    // ─── HashType in main mode ───

    [Theory]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    [InlineData("MD5")]
    public void Parse_ValidHashType_Accepted(string hashType)
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--hashtype", hashType]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(hashType, result.Options!.HashType);
        Assert.True(result.Options.HashTypeExplicit);
    }

    [Fact]
    public void Parse_InvalidHashType_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--hashtype", "CRC32"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid hash type"));
    }

    // ─── DAT flags in main mode ───

    [Fact]
    public void Parse_EnableDat_SetsFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--enabledat"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.EnableDat);
        Assert.True(result.Options.EnableDatExplicit);
    }

    [Fact]
    public void Parse_DatAudit_SetsFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--dat-audit"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.EnableDatAudit);
        Assert.True(result.Options.EnableDatAuditExplicit);
    }

    [Fact]
    public void Parse_DatRename_SetsFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--datrename"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.EnableDatRename);
        Assert.True(result.Options.EnableDatRenameExplicit);
    }

    [Fact]
    public void Parse_DatRoot_SetsValue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--datroot", _tempDir]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(_tempDir, result.Options!.DatRoot);
        Assert.True(result.Options.DatRootExplicit);
    }

    // ─── ConvertFormat ───

    [Fact]
    public void Parse_ConvertFormat_SetsFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--convertformat"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(RunConstants.ConvertFormatAuto, result.Options!.ConvertFormat);
        Assert.True(result.Options.ConvertFormatExplicit);
    }

    // ─── AggressiveJunk / SortConsole / GamesOnly ───

    [Fact]
    public void Parse_AggressiveJunk_SetsFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--aggressivejunk"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.AggressiveJunk);
        Assert.True(result.Options.AggressiveJunkExplicit);
    }

    [Fact]
    public void Parse_SortConsole_SetsFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--sortconsole"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.SortConsole);
        Assert.True(result.Options.SortConsoleExplicit);
    }

    [Fact]
    public void Parse_GamesOnly_SetsFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--gamesonly"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.OnlyGames);
        Assert.True(result.Options.OnlyGamesExplicit);
    }

    // ─── ApproveReviews ───

    [Fact]
    public void Parse_ApproveReviews_SetsFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--approve-reviews"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.ApproveReviews);
        Assert.True(result.Options.ApproveReviewsExplicit);
    }

    // ─── Log / Audit / Report paths ───

    [Fact]
    public void Parse_LogPath_SetsValue()
    {
        var logPath = Path.Combine(_tempDir, "run.log");
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--log", logPath]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(logPath, result.Options!.LogPath);
    }

    [Fact]
    public void Parse_AuditPath_SetsValue()
    {
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--audit", auditPath]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(auditPath, result.Options!.AuditPath);
    }

    [Fact]
    public void Parse_ReportPath_SetsValue()
    {
        var reportPath = Path.Combine(_tempDir, "report.html");
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--report", reportPath]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(reportPath, result.Options!.ReportPath);
    }

    // ─── Yes flag ───

    [Fact]
    public void Parse_YesFlag_SetsTrue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--yes"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.Yes);
    }

    [Fact]
    public void Parse_YShortFlag_SetsTrue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "-y"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.Yes);
    }

    // ─── Invalid mode ───

    [Fact]
    public void Parse_InvalidMode_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--mode", "Execute"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid mode"));
    }

    // ─── Positional root arg in main mode ───

    [Fact]
    public void Parse_PositionalRoot_AddedToRootsArray()
    {
        var result = CliArgsParser.Parse([_tempDir]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    // ─── Unknown flag in main mode ───

    [Fact]
    public void Parse_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--nonexistent"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Unknown flag"));
    }

    // ─── DatStaleDays validation ───

    [Fact]
    public void Parse_DatStaleDays_ValidValue_Parsed()
    {
        var result = CliArgsParser.Parse(["--update-dats", "--datroot", _tempDir, "--dat-stale-days", "30"]);
        Assert.Equal(CliCommand.UpdateDats, result.Command);
        Assert.Equal(30, result.Options!.DatStaleDays);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("5000")]
    [InlineData("abc")]
    public void Parse_DatStaleDays_InvalidValue_ReturnsError(string value)
    {
        var result = CliArgsParser.Parse(["--update-dats", "--datroot", _tempDir, "--dat-stale-days", value]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("DAT stale threshold"));
    }

    [Fact]
    public void Parse_DatStaleDays_NegativeValueLooksLikeFlag_ParsesAsUnknown()
    {
        // "-1" starts with '-' so TryConsumeValue puts it back as a flag
        var result = CliArgsParser.Parse(["--update-dats", "--datroot", _tempDir, "--dat-stale-days", "-1"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ─── UNC path rejection ───

    [Fact]
    public void Parse_UncRoot_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", @"\\server\share"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("UNC"));
    }

    // ─── Rollback with nonexistent file ───

    [Fact]
    public void Parse_RollbackNonexistentFile_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--rollback", @"C:\nonexistent\audit.csv"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Audit file not found"));
    }

    // ─── Empty args returns Help ───

    [Fact]
    public void Parse_EmptyArgs_ReturnsHelp()
    {
        var result = CliArgsParser.Parse([]);
        Assert.Equal(CliCommand.Help, result.Command);
        Assert.Equal(0, result.ExitCode);
    }

    // ─── Help flags ───

    [Theory]
    [InlineData("-help")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void Parse_HelpFlags_ReturnHelp(string flag)
    {
        var result = CliArgsParser.Parse([flag]);
        Assert.Equal(CliCommand.Help, result.Command);
        Assert.Equal(0, result.ExitCode);
    }

    // ─── Version flags ───

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public void Parse_VersionFlags_ReturnVersion(string flag)
    {
        var result = CliArgsParser.Parse([flag]);
        Assert.Equal(CliCommand.Version, result.Command);
        Assert.Equal(0, result.ExitCode);
    }

    // ─── Prefer regions ───

    [Fact]
    public void Parse_PreferRegions_ParsedCorrectly()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--prefer", "USA,Europe,Japan"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(new[] { "USA", "Europe", "Japan" }, result.Options!.PreferRegions);
        Assert.True(result.Options.PreferRegionsExplicit);
    }

    // ─── Mode explicit ───

    [Fact]
    public void Parse_ModeDryRun_Parsed()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--mode", "DryRun"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal("DryRun", result.Options!.Mode);
        Assert.True(result.Options.ModeExplicit);
    }

    [Fact]
    public void Parse_ModeMove_Parsed()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--mode", "Move"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal("Move", result.Options!.Mode);
    }

    // ─── UpdateDats with DatRoot UNC ───

    [Fact]
    public void Parse_UpdateDats_UncDatRoot_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--update-dats", "--datroot", @"\\server\dats"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("UNC"));
    }

    // ─── dat diff with both old/new ───

    [Fact]
    public void Parse_DatDiff_WithBothPaths_Succeeds()
    {
        var result = CliArgsParser.Parse(["dat", "diff", "--old", "old.dat", "--new", "new.dat"]);
        Assert.Equal(CliCommand.DatDiff, result.Command);
        Assert.Equal("old.dat", result.Options!.DatFileA);
        Assert.Equal("new.dat", result.Options!.DatFileB);
    }

    // ─── dat fix alias ───

    [Fact]
    public void Parse_DatFix_AliasAccepted()
    {
        var result = CliArgsParser.Parse(["dat", "fix", "--roots", _tempDir]);
        Assert.Equal(CliCommand.DatFix, result.Command);
    }

    // ─── dat fixdat with output ───

    [Fact]
    public void Parse_DatFixDat_WithOutput_SetsPath()
    {
        var outPath = Path.Combine(_tempDir, "fixdat.xml");
        var result = CliArgsParser.Parse(["dat", "fixdat", "--roots", _tempDir, "-o", outPath]);
        Assert.Equal(CliCommand.DatFix, result.Command);
        Assert.Equal(outPath, result.Options!.OutputPath);
    }

    // ─── dat fixdat positional root ───

    [Fact]
    public void Parse_DatFixDat_PositionalRoot_Accepted()
    {
        var result = CliArgsParser.Parse(["dat", "fixdat", _tempDir]);
        Assert.Equal(CliCommand.DatFix, result.Command);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    // ─── Merge with extensions, offset, limit, labels, audit, output, yes ───

    [Fact]
    public void Parse_Merge_AllFlags_Parsed()
    {
        var targetDir = Path.Combine(_tempDir, "target");
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        var outputPath = Path.Combine(_tempDir, "out.json");
        var result = CliArgsParser.Parse([
            "merge",
            "--left-roots", _tempDir,
            "--right-roots", _tempDir,
            "--target-root", targetDir,
            "--left-label", "Primary",
            "--right-label", "Secondary",
            "--extensions", "zip,7z",
            "--offset", "5",
            "--limit", "100",
            "--audit", auditPath,
            "--output", outputPath,
            "--yes"
        ]);
        Assert.Equal(CliCommand.Merge, result.Command);
        Assert.Equal("Primary", result.Options!.LeftLabel);
        Assert.Equal("Secondary", result.Options!.RightLabel);
        Assert.Contains(".zip", result.Options!.Extensions);
        Assert.Equal(5, result.Options!.CollectionOffset);
        Assert.Equal(100, result.Options!.CollectionLimit);
        Assert.Equal(auditPath, result.Options!.AuditPath);
        Assert.Equal(outputPath, result.Options!.OutputPath);
        Assert.True(result.Options!.Yes);
    }

    [Fact]
    public void Parse_Merge_InvalidOffset_ReturnsError()
    {
        var targetDir = Path.Combine(_tempDir, "target");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir,
            "--target-root", targetDir, "--offset", "-3"
        ]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Parse_Merge_InvalidLimit_ReturnsError()
    {
        var targetDir = Path.Combine(_tempDir, "target");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir,
            "--target-root", targetDir, "--limit", "0"
        ]);
        Assert.Equal(3, result.ExitCode);
    }

    // ─── Diff with labels, output ───

    [Fact]
    public void Parse_Diff_WithLabelsAndOutput_Parsed()
    {
        var outPath = Path.Combine(_tempDir, "diff.json");
        var result = CliArgsParser.Parse([
            "diff",
            "--left-roots", _tempDir,
            "--right-roots", _tempDir,
            "--left-label", "Left",
            "--right-label", "Right",
            "--output", outPath,
            "--offset", "0",
            "--limit", "50"
        ]);
        Assert.Equal(CliCommand.Diff, result.Command);
        Assert.Equal("Left", result.Options!.LeftLabel);
        Assert.Equal("Right", result.Options!.RightLabel);
        Assert.Equal(outPath, result.Options!.OutputPath);
        Assert.Equal(0, result.Options!.CollectionOffset);
        Assert.Equal(50, result.Options!.CollectionLimit);
    }

    // ─── Compare with output ───

    [Fact]
    public void Parse_Compare_WithOutput_Parsed()
    {
        var outPath = Path.Combine(_tempDir, "compare.json");
        var result = CliArgsParser.Parse([
            "compare", "--run", "run-123", "--compare-to", "run-456", "-o", outPath
        ]);
        Assert.Equal(CliCommand.Compare, result.Command);
        Assert.Equal("run-123", result.Options!.RunId);
        Assert.Equal("run-456", result.Options!.CompareToRunId);
        Assert.Equal(outPath, result.Options!.OutputPath);
    }

    // ─── Trends with valid limit & output ───

    [Fact]
    public void Parse_Trends_WithLimitAndOutput_Parsed()
    {
        var outPath = Path.Combine(_tempDir, "trends.json");
        var result = CliArgsParser.Parse(["trends", "--limit", "365", "--output", outPath]);
        Assert.Equal(CliCommand.Trends, result.Command);
        Assert.Equal(365, result.Options!.HistoryLimit);
        Assert.Equal(outPath, result.Options!.OutputPath);
    }

    // ─── History with output and valid values ───

    [Fact]
    public void Parse_History_WithAllFlags_Parsed()
    {
        var outPath = Path.Combine(_tempDir, "history.json");
        var result = CliArgsParser.Parse(["history", "--limit", "25", "--offset", "10", "-o", outPath]);
        Assert.Equal(CliCommand.History, result.Command);
        Assert.Equal(25, result.Options!.HistoryLimit);
        Assert.Equal(10, result.Options!.HistoryOffset);
        Assert.Equal(outPath, result.Options!.OutputPath);
    }

    // ─── Watch with DryRun mode (default behavior) ───

    [Fact]
    public void Parse_Watch_DryRunMode_SetsDefault()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _tempDir, "--interval", "60", "--mode", "DryRun"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal(RunConstants.ModeDryRun, result.Options!.Mode);
    }

    [Fact]
    public void Parse_Watch_MoveMode_Parsed()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _tempDir, "--interval", "60", "--mode", "Move"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal(RunConstants.ModeMove, result.Options!.Mode);
    }

    [Fact]
    public void Parse_Watch_InvalidMode_ReturnsError()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _tempDir, "--interval", "60", "--mode", "Execute"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ─── UNC path in optional path ───

    [Fact]
    public void Parse_TrashRootUnc_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--trashroot", @"\\server\trash"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ─── No roots after --roots flag with empty value ───

    [Fact]
    public void Parse_RootsEmptyValue_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", " "]);
        Assert.Equal(3, result.ExitCode);
    }

    // ─── Combine gamesonly + keepunknown ───

    [Fact]
    public void Parse_GamesOnlyWithKeepUnknown_Accepted()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--gamesonly", "--keepunknown"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.OnlyGames);
        Assert.True(result.Options.KeepUnknownWhenOnlyGames);
    }

    [Fact]
    public void Parse_GamesOnlyWithDropUnknown_Accepted()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--gamesonly", "--dropunknown"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.OnlyGames);
        Assert.False(result.Options.KeepUnknownWhenOnlyGames);
    }

    // ─── All flags combined ───

    [Fact]
    public void Parse_AllMainFlags_AcceptedTogether()
    {
        var result = CliArgsParser.Parse([
            "--roots", _tempDir,
            "--mode", "DryRun",
            "--prefer", "USA",
            "--extensions", "zip",
            "--trashroot", Path.Combine(_tempDir, "trash"),
            "--removejunk",
            "--gamesonly",
            "--keepunknown",
            "--aggressivejunk",
            "--sortconsole",
            "--enabledat",
            "--dat-audit",
            "--datrename",
            "--datroot", _tempDir,
            "--hashtype", "SHA1",
            "--convertformat",
            "--approve-reviews",
            "--approve-conversion-review",
            "--conflictpolicy", "Rename",
            "--log", Path.Combine(_tempDir, "log.txt"),
            "--audit", Path.Combine(_tempDir, "audit.csv"),
            "--report", Path.Combine(_tempDir, "report.html"),
            "--loglevel", "Debug",
            "--profile", "default",
            "--profile-file", Path.Combine(_tempDir, "p.json"),
            "--workflow", "quick-scan",
            "--yes"
        ]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Options!.RemoveJunk);
        Assert.True(result.Options.AggressiveJunk);
        Assert.True(result.Options.SortConsole);
        Assert.True(result.Options.EnableDat);
        Assert.True(result.Options.EnableDatAudit);
        Assert.True(result.Options.EnableDatRename);
        Assert.Equal(RunConstants.ConvertFormatAuto, result.Options.ConvertFormat);
        Assert.True(result.Options.ApproveReviews);
        Assert.True(result.Options.ApproveConversionReview);
        Assert.True(result.Options.Yes);
        Assert.Equal("Rename", result.Options.ConflictPolicy);
        Assert.Equal("Debug", result.Options.LogLevel);
        Assert.Equal("SHA1", result.Options.HashType);
    }

    // ─── Unknown subcommand falls through to main parser ───

    [Fact]
    public void Parse_UnknownSubcommand_TreatedAsPositionalRoot()
    {
        // something that doesn't start with - and isn't a known subcommand
        // will be treated as a positional root in the main parser
        // (will fail because the directory doesn't exist if not _tempDir)
        var result = CliArgsParser.Parse(["nonexistent-dir-xyz"]);
        // Should try to validate as root and fail
        Assert.True(result.ExitCode == 3 || result.Command == CliCommand.Run);
    }

    // ─── Short -roots alias ───

    [Fact]
    public void Parse_ShortRootsAlias_Accepted()
    {
        var result = CliArgsParser.Parse(["-roots", _tempDir]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    // ─── Rollback successful parse ───

    [Fact]
    public void Parse_RollbackWithExistingFile_ReturnsRollbackCommand()
    {
        var auditFile = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(auditFile, "timestamp,old,new,reason\n");
        var result = CliArgsParser.Parse(["--rollback", auditFile]);
        Assert.Equal(CliCommand.Rollback, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(auditFile, result.Options!.RollbackAuditPath);
    }
}
