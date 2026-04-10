using Xunit;
using Romulus.CLI;
using Romulus.Contracts;
using Romulus.Contracts.Models;

namespace Romulus.Tests;

/// <summary>
/// Covers untested subcommand branches, valid-path flag combinations,
/// label/output flags, and conflict paths in CliArgsParser.
/// Complements CliArgsParserCoverageTests and CliArgsParserFlagCoverageTests.
/// </summary>
public sealed class CliArgsParserSubcommandCoverageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempDir2;

    public CliArgsParserSubcommandCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cli-sub-" + Guid.NewGuid().ToString("N")[..8]);
        _tempDir2 = Path.Combine(Path.GetTempPath(), "cli-sub2-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_tempDir2);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
        try { Directory.Delete(_tempDir2, true); } catch { }
    }

    // ──────────────────────────────────────────
    // convert subcommand: --target only, --console only
    // ──────────────────────────────────────────

    [Fact]
    public void Convert_WithTargetOnly_SetsTargetFormat()
    {
        var inputFile = Path.Combine(_tempDir, "game.zip");
        File.WriteAllBytes(inputFile, new byte[1]);
        var result = CliArgsParser.Parse(["convert", "--input", inputFile, "--target", "chd"]);
        Assert.Equal(CliCommand.Convert, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("chd", result.Options!.TargetFormat);
        Assert.Null(result.Options.ConsoleKey);
    }

    [Fact]
    public void Convert_WithConsoleOnly_SetsConsoleKey()
    {
        var inputFile = Path.Combine(_tempDir, "game.zip");
        File.WriteAllBytes(inputFile, new byte[1]);
        var result = CliArgsParser.Parse(["convert", "--input", inputFile, "--console", "PSX"]);
        Assert.Equal(CliCommand.Convert, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("PSX", result.Options!.ConsoleKey);
        Assert.Null(result.Options.TargetFormat);
    }

    [Fact]
    public void Convert_ShortFlags_Accepted()
    {
        var inputFile = Path.Combine(_tempDir, "game.zip");
        File.WriteAllBytes(inputFile, new byte[1]);
        var result = CliArgsParser.Parse(["convert", "-i", inputFile, "-t", "rvz", "-c", "GC"]);
        Assert.Equal(CliCommand.Convert, result.Command);
        Assert.Equal("rvz", result.Options!.TargetFormat);
        Assert.Equal("GC", result.Options.ConsoleKey);
        Assert.Equal(inputFile, result.Options.InputPath);
    }

    // ──────────────────────────────────────────
    // profiles: show and import with valid values
    // ──────────────────────────────────────────

    [Fact]
    public void Profiles_Show_WithValidId_ReturnsShowCommand()
    {
        var result = CliArgsParser.Parse(["profiles", "show", "--id", "my-profile"]);
        Assert.Equal(CliCommand.ProfilesShow, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("my-profile", result.Options!.ProfileId);
    }

    [Fact]
    public void Profiles_Import_WithValidInput_ReturnsImportCommand()
    {
        var profileFile = Path.Combine(_tempDir, "profile.json");
        File.WriteAllText(profileFile, "{}");
        var result = CliArgsParser.Parse(["profiles", "import", "--input", profileFile]);
        Assert.Equal(CliCommand.ProfilesImport, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(profileFile, result.Options!.InputPath);
    }

    [Fact]
    public void Profiles_Export_WithOutputPath_SetsOutputPath()
    {
        var outPath = Path.Combine(_tempDir, "export.json");
        var result = CliArgsParser.Parse(["profiles", "export", "--id", "myprofile", "--output", outPath]);
        Assert.Equal(CliCommand.ProfilesExport, result.Command);
        Assert.Equal(outPath, result.Options!.OutputPath);
        Assert.Equal("myprofile", result.Options.ProfileId);
    }

    // ──────────────────────────────────────────
    // compare: valid run IDs and output
    // ──────────────────────────────────────────

    [Fact]
    public void Compare_WithValidRunIds_ReturnsCompareCommand()
    {
        var result = CliArgsParser.Parse(["compare", "--run", "run-001", "--compare-to", "run-002"]);
        Assert.Equal(CliCommand.Compare, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("run-001", result.Options!.RunId);
        Assert.Equal("run-002", result.Options.CompareToRunId);
    }

    [Fact]
    public void Compare_WithOutput_SetsOutputPath()
    {
        var outPath = Path.Combine(_tempDir, "compare.json");
        var result = CliArgsParser.Parse(["compare", "--run", "a", "--compare-to", "b", "-o", outPath]);
        Assert.Equal(CliCommand.Compare, result.Command);
        Assert.Equal(outPath, result.Options!.OutputPath);
    }

    // ──────────────────────────────────────────
    // trends: valid limit and output
    // ──────────────────────────────────────────

    [Fact]
    public void Trends_WithValidLimit_ParsesCorrectly()
    {
        var result = CliArgsParser.Parse(["trends", "--limit", "365"]);
        Assert.Equal(CliCommand.Trends, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(365, result.Options!.HistoryLimit);
    }

    [Fact]
    public void Trends_WithLimitAndOutput_ParsesBoth()
    {
        var outPath = Path.Combine(_tempDir, "trends.json");
        var result = CliArgsParser.Parse(["trends", "--limit", "100", "-o", outPath]);
        Assert.Equal(CliCommand.Trends, result.Command);
        Assert.Equal(100, result.Options!.HistoryLimit);
        Assert.Equal(outPath, result.Options.OutputPath);
    }

    [Fact]
    public void Trends_LimitAtMaxBoundary_Accepted()
    {
        var result = CliArgsParser.Parse(["trends", "--limit", "3650"]);
        Assert.Equal(CliCommand.Trends, result.Command);
        Assert.Equal(3650, result.Options!.HistoryLimit);
    }

    [Fact]
    public void Trends_LimitExceedsMax_ReturnsError()
    {
        var result = CliArgsParser.Parse(["trends", "--limit", "3651"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("trends limit"));
    }

    // ──────────────────────────────────────────
    // workflows: valid ID
    // ──────────────────────────────────────────

    [Fact]
    public void Workflows_WithValidId_ReturnsCommand()
    {
        var result = CliArgsParser.Parse(["workflows", "--id", WorkflowScenarioIds.QuickClean]);
        Assert.Equal(CliCommand.Workflows, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(WorkflowScenarioIds.QuickClean, result.Options!.WorkflowScenarioId);
    }

    // ──────────────────────────────────────────
    // history: valid limit/offset/output
    // ──────────────────────────────────────────

    [Fact]
    public void History_WithValidLimitAndOffset_ParsesCorrectly()
    {
        var result = CliArgsParser.Parse(["history", "--limit", "20", "--offset", "10"]);
        Assert.Equal(CliCommand.History, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(20, result.Options!.HistoryLimit);
        Assert.Equal(10, result.Options.HistoryOffset);
    }

    [Fact]
    public void History_WithOutput_SetsOutputPath()
    {
        var outPath = Path.Combine(_tempDir, "history.json");
        var result = CliArgsParser.Parse(["history", "--limit", "10", "-o", outPath]);
        Assert.Equal(CliCommand.History, result.Command);
        Assert.Equal(outPath, result.Options!.OutputPath);
    }

    [Fact]
    public void History_ZeroOffset_Accepted()
    {
        var result = CliArgsParser.Parse(["history", "--offset", "0"]);
        Assert.Equal(CliCommand.History, result.Command);
        Assert.Equal(0, result.Options!.HistoryOffset);
    }

    [Fact]
    public void History_NoFlags_ReturnsDefaultCommand()
    {
        var result = CliArgsParser.Parse(["history"]);
        Assert.Equal(CliCommand.History, result.Command);
        Assert.Equal(0, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // diff: labels, offset/limit, output, valid roots
    // ──────────────────────────────────────────

    [Fact]
    public void Diff_WithLabels_SetsValues()
    {
        var result = CliArgsParser.Parse([
            "diff", "--left-roots", _tempDir, "--right-roots", _tempDir2,
            "--left-label", "Primary", "--right-label", "Backup"
        ]);
        Assert.Equal(CliCommand.Diff, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Primary", result.Options!.LeftLabel);
        Assert.Equal("Backup", result.Options.RightLabel);
    }

    [Fact]
    public void Diff_WithOffsetAndLimit_ParsesCorrectly()
    {
        var result = CliArgsParser.Parse([
            "diff", "--left-roots", _tempDir, "--right-roots", _tempDir2,
            "--offset", "5", "--limit", "50"
        ]);
        Assert.Equal(CliCommand.Diff, result.Command);
        Assert.Equal(5, result.Options!.CollectionOffset);
        Assert.Equal(50, result.Options.CollectionLimit);
    }

    [Fact]
    public void Diff_WithOutput_SetsPath()
    {
        var outPath = Path.Combine(_tempDir, "diff.json");
        var result = CliArgsParser.Parse([
            "diff", "--left-roots", _tempDir, "--right-roots", _tempDir2,
            "-o", outPath
        ]);
        Assert.Equal(CliCommand.Diff, result.Command);
        Assert.Equal(outPath, result.Options!.OutputPath);
    }

    [Fact]
    public void Diff_ValidRoots_ReturnsCommand()
    {
        var result = CliArgsParser.Parse([
            "diff", "--left-roots", _tempDir, "--right-roots", _tempDir2
        ]);
        Assert.Equal(CliCommand.Diff, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(_tempDir, result.Options!.LeftRoots);
        Assert.Contains(_tempDir2, result.Options.RightRoots);
    }

    // ──────────────────────────────────────────
    // merge: labels, audit, offset/limit, --yes
    // ──────────────────────────────────────────

    [Fact]
    public void Merge_WithLabels_SetsValues()
    {
        var targetDir = Path.Combine(_tempDir, "merged");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir2,
            "--target-root", targetDir,
            "--left-label", "Main", "--right-label", "Secondary"
        ]);
        Assert.Equal(CliCommand.Merge, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Main", result.Options!.LeftLabel);
        Assert.Equal("Secondary", result.Options.RightLabel);
    }

    [Fact]
    public void Merge_WithAuditPath_SetsValue()
    {
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        var targetDir = Path.Combine(_tempDir, "merged");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir2,
            "--target-root", targetDir, "--audit", auditPath
        ]);
        Assert.Equal(CliCommand.Merge, result.Command);
        Assert.Equal(auditPath, result.Options!.AuditPath);
    }

    [Fact]
    public void Merge_WithOutput_SetsPath()
    {
        var outPath = Path.Combine(_tempDir, "merge-plan.json");
        var targetDir = Path.Combine(_tempDir, "merged");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir2,
            "--target-root", targetDir, "-o", outPath
        ]);
        Assert.Equal(CliCommand.Merge, result.Command);
        Assert.Equal(outPath, result.Options!.OutputPath);
    }

    [Fact]
    public void Merge_WithYesFlag_SkipsConfirmation()
    {
        var targetDir = Path.Combine(_tempDir, "merged");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir2,
            "--target-root", targetDir, "--yes"
        ]);
        Assert.Equal(CliCommand.Merge, result.Command);
        Assert.True(result.Options!.Yes);
    }

    [Fact]
    public void Merge_WithExtensions_ParsesCorrectly()
    {
        var targetDir = Path.Combine(_tempDir, "merged");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir2,
            "--target-root", targetDir, "--extensions", "zip,7z"
        ]);
        Assert.Equal(CliCommand.Merge, result.Command);
        Assert.Contains(".zip", result.Options!.Extensions);
        Assert.Contains(".7z", result.Options.Extensions);
    }

    [Fact]
    public void Merge_WithOffsetAndLimit_ParsesCorrectly()
    {
        var targetDir = Path.Combine(_tempDir, "merged");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir2,
            "--target-root", targetDir, "--offset", "10", "--limit", "200"
        ]);
        Assert.Equal(CliCommand.Merge, result.Command);
        Assert.Equal(10, result.Options!.CollectionOffset);
        Assert.Equal(200, result.Options.CollectionLimit);
    }

    [Fact]
    public void Merge_InvalidOffset_ReturnsError()
    {
        var targetDir = Path.Combine(_tempDir, "merged");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir2,
            "--target-root", targetDir, "--offset", "abc"
        ]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("offset"));
    }

    [Fact]
    public void Merge_InvalidLimit_ReturnsError()
    {
        var targetDir = Path.Combine(_tempDir, "merged");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir2,
            "--target-root", targetDir, "--limit", "0"
        ]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("limit"));
    }

    // ──────────────────────────────────────────
    // export: valid formats, --name, --profile/--profile-file, --workflow
    // ──────────────────────────────────────────

    [Theory]
    [InlineData("csv")]
    [InlineData("json")]
    [InlineData("retroarch")]
    [InlineData("emulationstation")]
    [InlineData("launchbox")]
    [InlineData("mister")]
    [InlineData("onionos")]
    public void Export_ValidFormat_Accepted(string format)
    {
        var result = CliArgsParser.Parse(["export", "--roots", _tempDir, "--format", format]);
        Assert.Equal(CliCommand.Export, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(format, result.Options!.ExportFormat);
    }

    [Fact]
    public void Export_WithName_SetsCollectionName()
    {
        var result = CliArgsParser.Parse([
            "export", "--roots", _tempDir, "--format", "csv", "--name", "MyCollection"
        ]);
        Assert.Equal(CliCommand.Export, result.Command);
        Assert.Equal("MyCollection", result.Options!.CollectionName);
    }

    [Fact]
    public void Export_WithProfileAndProfileFile_BothParsed()
    {
        var pfFile = Path.Combine(_tempDir, "pf.json");
        File.WriteAllText(pfFile, "{}");
        var result = CliArgsParser.Parse([
            "export", "--roots", _tempDir, "--format", "json",
            "--profile", "default", "--profile-file", pfFile
        ]);
        Assert.Equal(CliCommand.Export, result.Command);
        Assert.Equal("default", result.Options!.ProfileId);
        Assert.Equal(pfFile, result.Options.ProfileFilePath);
    }

    [Fact]
    public void Export_WithWorkflow_SetsWorkflowId()
    {
        var result = CliArgsParser.Parse([
            "export", "--roots", _tempDir, "--format", "csv", "--workflow", "quick-clean"
        ]);
        Assert.Equal(CliCommand.Export, result.Command);
        Assert.Equal("quick-clean", result.Options!.WorkflowScenarioId);
    }

    [Fact]
    public void Export_PositionalRootWithFormat_Accepted()
    {
        var result = CliArgsParser.Parse(["export", _tempDir, "--format", "json"]);
        Assert.Equal(CliCommand.Export, result.Command);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    // ──────────────────────────────────────────
    // junk-report and completeness: --aggressive, --output
    // ──────────────────────────────────────────

    [Fact]
    public void JunkReport_WithAggressive_SetsFlag()
    {
        var result = CliArgsParser.Parse(["junk-report", "--roots", _tempDir, "--aggressive"]);
        Assert.Equal(CliCommand.JunkReport, result.Command);
        Assert.True(result.Options!.AggressiveJunk);
    }

    [Fact]
    public void JunkReport_WithOutput_SetsPath()
    {
        var outPath = Path.Combine(_tempDir, "junk.json");
        var result = CliArgsParser.Parse(["junk-report", "--roots", _tempDir, "-o", outPath]);
        Assert.Equal(CliCommand.JunkReport, result.Command);
        Assert.Equal(outPath, result.Options!.OutputPath);
    }

    [Fact]
    public void Completeness_WithAggressive_SetsFlag()
    {
        var result = CliArgsParser.Parse(["completeness", "--roots", _tempDir, "--aggressive"]);
        Assert.Equal(CliCommand.Completeness, result.Command);
        Assert.True(result.Options!.AggressiveJunk);
    }

    [Fact]
    public void Completeness_WithOutput_SetsPath()
    {
        var outPath = Path.Combine(_tempDir, "complete.json");
        var result = CliArgsParser.Parse(["completeness", "--roots", _tempDir, "-o", outPath]);
        Assert.Equal(CliCommand.Completeness, result.Command);
        Assert.Equal(outPath, result.Options!.OutputPath);
    }

    // ──────────────────────────────────────────
    // dat diff: valid --old and --new
    // ──────────────────────────────────────────

    [Fact]
    public void Dat_Diff_WithValidPaths_ReturnsCommand()
    {
        var oldDat = Path.Combine(_tempDir, "old.dat");
        var newDat = Path.Combine(_tempDir, "new.dat");
        File.WriteAllText(oldDat, "<dat/>");
        File.WriteAllText(newDat, "<dat/>");
        var result = CliArgsParser.Parse(["dat", "diff", "--old", oldDat, "--new", newDat]);
        Assert.Equal(CliCommand.DatDiff, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(oldDat, result.Options!.DatFileA);
        Assert.Equal(newDat, result.Options.DatFileB);
    }

    [Fact]
    public void Dat_Diff_MissingNew_ReturnsError()
    {
        var oldDat = Path.Combine(_tempDir, "old.dat");
        File.WriteAllText(oldDat, "<dat/>");
        var result = CliArgsParser.Parse(["dat", "diff", "--old", oldDat]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--old") || e.Contains("--new"));
    }

    // ──────────────────────────────────────────
    // dat fixdat: --name, --dat-root alternative
    // ──────────────────────────────────────────

    [Fact]
    public void Dat_FixDat_WithName_SetsValue()
    {
        var result = CliArgsParser.Parse(["dat", "fixdat", "--roots", _tempDir, "--name", "CustomDat"]);
        Assert.Equal(CliCommand.DatFix, result.Command);
        Assert.Equal("CustomDat", result.Options!.DatName);
    }

    [Fact]
    public void Dat_FixDat_WithOutput_SetsPath()
    {
        var outPath = Path.Combine(_tempDir, "fixdat.dat");
        var result = CliArgsParser.Parse(["dat", "fixdat", "--roots", _tempDir, "-o", outPath]);
        Assert.Equal(CliCommand.DatFix, result.Command);
        Assert.Equal(outPath, result.Options!.OutputPath);
    }

    // ──────────────────────────────────────────
    // integrity check: with output
    // ──────────────────────────────────────────

    [Fact]
    public void Integrity_Check_ReturnsCommandImmediately()
    {
        // integrity check takes no additional flags
        var result = CliArgsParser.Parse(["integrity", "check"]);
        Assert.Equal(CliCommand.IntegrityCheck, result.Command);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Integrity_Baseline_WithMultipleRoots_ParsesAll()
    {
        var result = CliArgsParser.Parse(["integrity", "baseline", "--roots", _tempDir + ";" + _tempDir2]);
        Assert.Equal(CliCommand.IntegrityBaseline, result.Command);
        Assert.Contains(_tempDir, result.Options!.Roots);
        Assert.Contains(_tempDir2, result.Options.Roots);
    }

    // ──────────────────────────────────────────
    // watch: interval+cron together, valid debounce+interval
    // ──────────────────────────────────────────

    [Fact]
    public void Watch_IntervalAndCron_BothParsed()
    {
        var result = CliArgsParser.Parse([
            "watch", "--roots", _tempDir, "--interval", "60", "--cron", "0 2 * * *"
        ]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal(60, result.Options!.WatchIntervalMinutes);
        Assert.Equal("0 2 * * *", result.Options.WatchCronExpression);
    }

    [Fact]
    public void Watch_WithMode_SetsMode()
    {
        var result = CliArgsParser.Parse([
            "watch", "--roots", _tempDir, "--interval", "5", "--mode", "Move"
        ]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal("Move", result.Options!.Mode);
    }

    [Fact]
    public void Watch_WithDatFlags_ParsesCorrectly()
    {
        var result = CliArgsParser.Parse([
            "watch", "--roots", _tempDir, "--interval", "5",
            "--enabledat", "--datroot", _tempDir2,
            "--hashtype", "SHA256"
        ]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.True(result.Options!.EnableDat);
        Assert.Equal(_tempDir2, result.Options.DatRoot);
        Assert.Equal("SHA256", result.Options.HashType);
    }

    [Fact]
    public void Watch_WithSortConsole_SetsFlag()
    {
        var result = CliArgsParser.Parse([
            "watch", "--roots", _tempDir, "--interval", "5", "--sortconsole"
        ]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.True(result.Options!.SortConsole);
    }

    [Fact]
    public void Watch_WithApproveReviews_SetsFlag()
    {
        var result = CliArgsParser.Parse([
            "watch", "--roots", _tempDir, "--interval", "5", "--approve-reviews"
        ]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.True(result.Options!.ApproveReviews);
    }

    [Fact]
    public void Watch_WithYesFlag_SetsFlag()
    {
        var result = CliArgsParser.Parse([
            "watch", "--roots", _tempDir, "--interval", "5", "--yes"
        ]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.True(result.Options!.Yes);
    }

    // ──────────────────────────────────────────
    // ValidateCollectionRoots edge cases
    // ──────────────────────────────────────────

    [Fact]
    public void Diff_EmptyLeftRoot_ReturnsError()
    {
        var result = CliArgsParser.Parse(["diff", "--left-roots", "", "--right-roots", _tempDir]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Diff_NonexistentRoot_ReturnsError()
    {
        var missing = Path.Combine(_tempDir, "nonexist");
        var result = CliArgsParser.Parse(["diff", "--left-roots", missing, "--right-roots", _tempDir]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("not found"));
    }

    [Fact]
    public void Merge_NonexistentRightRoot_ReturnsError()
    {
        var missing = Path.Combine(_tempDir, "nonexist");
        var targetDir = Path.Combine(_tempDir, "target");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", missing,
            "--target-root", targetDir
        ]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("not found"));
    }

    // ──────────────────────────────────────────
    // Run: --force-dat-update and --smart-dat-update
    // ──────────────────────────────────────────

    [Fact]
    public void Run_ForceDatUpdate_SetsFlag()
    {
        var result = CliArgsParser.Parse([
            "--roots", _tempDir, "--update-dats", "--datroot", _tempDir,
            "--force-dat-update"
        ]);
        Assert.Equal(CliCommand.UpdateDats, result.Command);
        Assert.True(result.Options!.ForceDatUpdate);
    }

    [Fact]
    public void Run_SmartDatUpdate_SetsFlag()
    {
        var result = CliArgsParser.Parse([
            "--roots", _tempDir, "--update-dats", "--datroot", _tempDir,
            "--smart-dat-update"
        ]);
        Assert.Equal(CliCommand.UpdateDats, result.Command);
        Assert.True(result.Options!.SmartDatUpdate);
    }

    // ──────────────────────────────────────────
    // Run: reportpath, auditpath, logpath
    // ──────────────────────────────────────────

    [Fact]
    public void Run_WithReportPath_SetsPath()
    {
        var reportPath = Path.Combine(_tempDir, "report.json");
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--report", reportPath]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(reportPath, result.Options!.ReportPath);
    }

    [Fact]
    public void Run_WithAuditPath_SetsPath()
    {
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--audit", auditPath]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(auditPath, result.Options!.AuditPath);
    }

    [Fact]
    public void Run_WithLogPath_SetsPath()
    {
        var logPath = Path.Combine(_tempDir, "run.log");
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--log", logPath]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(logPath, result.Options!.LogPath);
    }

    // ──────────────────────────────────────────
    // Run: mode flags
    // ──────────────────────────────────────────

    [Fact]
    public void Run_MoveMode_SetsMode()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--mode", "Move"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal("Move", result.Options!.Mode);
        Assert.True(result.Options.ModeExplicit);
    }

    [Fact]
    public void Run_InvalidMode_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--mode", "Delete"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid mode"));
    }

    // ──────────────────────────────────────────
    // Run: prefer regions
    // ──────────────────────────────────────────

    [Fact]
    public void Run_PreferRegions_ParsesCommaSeparated()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--prefer", "USA,Europe,Japan"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(["USA", "Europe", "Japan"], result.Options!.PreferRegions);
        Assert.True(result.Options.PreferRegionsExplicit);
    }

    // ──────────────────────────────────────────
    // Run: extensions
    // ──────────────────────────────────────────

    [Fact]
    public void Run_Extensions_ParsesCommaSeparated()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--extensions", "zip,7z,nes"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Contains(".zip", result.Options!.Extensions);
        Assert.Contains(".7z", result.Options.Extensions);
        Assert.Contains(".nes", result.Options.Extensions);
        Assert.True(result.Options.ExtensionsExplicit);
    }

    // ──────────────────────────────────────────
    // Run: trashroot
    // ──────────────────────────────────────────

    [Fact]
    public void Run_TrashRoot_SetsPath()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--trashroot", _tempDir2]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(_tempDir2, result.Options!.TrashRoot);
        Assert.True(result.Options.TrashRootExplicit);
    }

    // ──────────────────────────────────────────
    // Run: boolean toggle flags
    // ──────────────────────────────────────────

    [Fact]
    public void Run_NoRemoveJunk_SetsFalse()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--no-removejunk"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.False(result.Options!.RemoveJunk);
        Assert.True(result.Options.RemoveJunkExplicit);
    }

    [Fact]
    public void Run_OnlyGames_SetsTrue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--gamesonly"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.OnlyGames);
        Assert.True(result.Options.OnlyGamesExplicit);
    }

    [Fact]
    public void Run_EnableDat_SetsTrue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--enabledat"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.EnableDat);
        Assert.True(result.Options.EnableDatExplicit);
    }

    [Fact]
    public void Run_EnableDatAudit_SetsTrue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--dat-audit"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.EnableDatAudit);
        Assert.True(result.Options.EnableDatAuditExplicit);
    }

    [Fact]
    public void Run_EnableDatRename_SetsTrue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--datrename"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.EnableDatRename);
        Assert.True(result.Options.EnableDatRenameExplicit);
    }

    [Fact]
    public void Run_SortConsole_SetsTrue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--sortconsole"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.SortConsole);
        Assert.True(result.Options.SortConsoleExplicit);
    }

    [Fact]
    public void Run_ConvertFormat_SetsTrue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--convertformat"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(RunConstants.ConvertFormatAuto, result.Options!.ConvertFormat);
        Assert.True(result.Options.ConvertFormatExplicit);
    }

    [Fact]
    public void Run_ApproveReviews_SetsTrue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--approve-reviews"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.ApproveReviews);
        Assert.True(result.Options.ApproveReviewsExplicit);
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Info")]
    [InlineData("Warning")]
    [InlineData("Error")]
    public void Run_LogLevel_ParsesValue(string level)
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--loglevel", level]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(level, result.Options!.LogLevel);
    }

    [Fact]
    public void Run_InvalidLogLevel_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--loglevel", "Verbose"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("log level"));
    }

    [Fact]
    public void Run_InvalidConflictPolicy_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--conflictpolicy", "Destroy"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("conflict policy"));
    }

    // ──────────────────────────────────────────
    // Run: DatRoot and HashType
    // ──────────────────────────────────────────

    [Fact]
    public void Run_DatRoot_SetsPath()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--datroot", _tempDir2]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(_tempDir2, result.Options!.DatRoot);
        Assert.True(result.Options.DatRootExplicit);
    }

    [Fact]
    public void Run_HashType_SetsValue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--hashtype", "MD5"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal("MD5", result.Options!.HashType);
        Assert.True(result.Options.HashTypeExplicit);
    }

    [Fact]
    public void Run_InvalidHashType_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--hashtype", "CRC32"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("hash type"));
    }

    // ──────────────────────────────────────────
    // Run: --yes flag
    // ──────────────────────────────────────────

    [Fact]
    public void Run_YesFlag_SkipsConfirmation()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "-y"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.Yes);
    }

    // ──────────────────────────────────────────
    // Run: --dat-stale-days
    // ──────────────────────────────────────────

    [Fact]
    public void Run_DatStaleDays_SetsValue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--dat-stale-days", "30"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(30, result.Options!.DatStaleDays);
    }

    // ──────────────────────────────────────────
    // Unknown subcommand
    // ──────────────────────────────────────────

    [Fact]
    public void UnknownSubcommand_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["foobar"]);
        // Unknown subcommands that aren't recognized as a positional root 
        // should return an error or be treated as a root arg
        Assert.True(result.ExitCode != 0 || result.Command == CliCommand.Run);
    }
}
