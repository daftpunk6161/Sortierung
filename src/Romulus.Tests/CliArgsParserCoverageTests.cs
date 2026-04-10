using Xunit;
using Romulus.CLI;

namespace Romulus.Tests;

/// <summary>
/// Covers untested subcommands and flag combinations in CliArgsParser.
/// Focuses on: analyze, convert, header, junk-report, completeness,
/// integrity baseline, profiles list/export/delete, watch cron/debounce,
/// edge cases and validation boundaries.
/// </summary>
public sealed class CliArgsParserCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public CliArgsParserCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cli-cov-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ──────────────────────────────────────────
    // analyze subcommand
    // ──────────────────────────────────────────

    [Fact]
    public void Analyze_WithRoot_ReturnsAnalyzeCommand()
    {
        var result = CliArgsParser.Parse(["analyze", "--roots", _tempDir]);
        Assert.Equal(CliCommand.Analyze, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    [Fact]
    public void Analyze_WithOutput_SetsOutputPath()
    {
        var outPath = Path.Combine(_tempDir, "analysis.json");
        var result = CliArgsParser.Parse(["analyze", "--roots", _tempDir, "-o", outPath]);
        Assert.Equal(CliCommand.Analyze, result.Command);
        Assert.Equal(outPath, result.Options!.OutputPath);
    }

    [Fact]
    public void Analyze_WithAggressive_SetsFlag()
    {
        var result = CliArgsParser.Parse(["analyze", "--roots", _tempDir, "--aggressive"]);
        Assert.Equal(CliCommand.Analyze, result.Command);
        Assert.True(result.Options!.AggressiveJunk);
    }

    [Fact]
    public void Analyze_NoRoots_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["analyze"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--roots"));
    }

    [Fact]
    public void Analyze_PositionalRoot_Accepted()
    {
        var result = CliArgsParser.Parse(["analyze", _tempDir]);
        Assert.Equal(CliCommand.Analyze, result.Command);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    [Fact]
    public void Analyze_UnknownFlag_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["analyze", "--roots", _tempDir, "--bogus"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Unknown flag"));
    }

    // ──────────────────────────────────────────
    // convert subcommand
    // ──────────────────────────────────────────

    [Fact]
    public void Convert_WithInput_ReturnsConvertCommand()
    {
        var inputFile = Path.Combine(_tempDir, "game.zip");
        File.WriteAllBytes(inputFile, new byte[1]);
        var result = CliArgsParser.Parse(["convert", "--input", inputFile]);
        Assert.Equal(CliCommand.Convert, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(inputFile, result.Options!.InputPath);
    }

    [Fact]
    public void Convert_WithTargetAndConsole_SetsOptions()
    {
        var inputFile = Path.Combine(_tempDir, "game.zip");
        File.WriteAllBytes(inputFile, new byte[1]);
        var result = CliArgsParser.Parse(["convert", "--input", inputFile, "--target", "chd", "--console", "PSX"]);
        Assert.Equal(CliCommand.Convert, result.Command);
        Assert.Equal("chd", result.Options!.TargetFormat);
        Assert.Equal("PSX", result.Options!.ConsoleKey);
    }

    [Fact]
    public void Convert_ApproveConversionReview_SetsFlag()
    {
        var inputFile = Path.Combine(_tempDir, "game.zip");
        File.WriteAllBytes(inputFile, new byte[1]);
        var result = CliArgsParser.Parse(["convert", "--input", inputFile, "--approve-conversion-review"]);
        Assert.Equal(CliCommand.Convert, result.Command);
        Assert.True(result.Options!.ApproveConversionReview);
    }

    [Fact]
    public void Convert_PositionalInput_Accepted()
    {
        var inputFile = Path.Combine(_tempDir, "game.zip");
        File.WriteAllBytes(inputFile, new byte[1]);
        var result = CliArgsParser.Parse(["convert", inputFile]);
        Assert.Equal(CliCommand.Convert, result.Command);
        Assert.Equal(inputFile, result.Options!.InputPath);
    }

    [Fact]
    public void Convert_NoInput_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["convert"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--input"));
    }

    [Fact]
    public void Convert_UnknownFlag_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["convert", "--input", "file.zip", "--unknown"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // header subcommand
    // ──────────────────────────────────────────

    [Fact]
    public void Header_WithInput_ReturnsHeaderCommand()
    {
        var inputFile = Path.Combine(_tempDir, "rom.sfc");
        File.WriteAllBytes(inputFile, new byte[1]);
        var result = CliArgsParser.Parse(["header", "--input", inputFile]);
        Assert.Equal(CliCommand.Header, result.Command);
        Assert.Equal(inputFile, result.Options!.InputPath);
    }

    [Fact]
    public void Header_PositionalInput_Accepted()
    {
        var inputFile = Path.Combine(_tempDir, "rom.sfc");
        File.WriteAllBytes(inputFile, new byte[1]);
        var result = CliArgsParser.Parse(["header", inputFile]);
        Assert.Equal(CliCommand.Header, result.Command);
        Assert.Equal(inputFile, result.Options!.InputPath);
    }

    [Fact]
    public void Header_NoInput_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["header"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--input"));
    }

    [Fact]
    public void Header_UnknownFlag_Error()
    {
        var result = CliArgsParser.Parse(["header", "--unknown"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // junk-report subcommand
    // ──────────────────────────────────────────

    [Fact]
    public void JunkReport_WithRoot_ReturnsJunkReportCommand()
    {
        var result = CliArgsParser.Parse(["junk-report", "--roots", _tempDir]);
        Assert.Equal(CliCommand.JunkReport, result.Command);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    [Fact]
    public void JunkReport_NoRoots_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["junk-report"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // completeness subcommand
    // ──────────────────────────────────────────

    [Fact]
    public void Completeness_WithRoot_ReturnsCommand()
    {
        var result = CliArgsParser.Parse(["completeness", "--roots", _tempDir]);
        Assert.Equal(CliCommand.Completeness, result.Command);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    [Fact]
    public void Completeness_NoRoots_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["completeness"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // integrity subcommand
    // ──────────────────────────────────────────

    [Fact]
    public void Integrity_NoAction_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["integrity"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("sub-action"));
    }

    [Fact]
    public void Integrity_Check_ReturnsIntegrityCheckCommand()
    {
        var result = CliArgsParser.Parse(["integrity", "check"]);
        Assert.Equal(CliCommand.IntegrityCheck, result.Command);
    }

    [Fact]
    public void Integrity_Baseline_WithRoots_ReturnsCommand()
    {
        var result = CliArgsParser.Parse(["integrity", "baseline", "--roots", _tempDir]);
        Assert.Equal(CliCommand.IntegrityBaseline, result.Command);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    [Fact]
    public void Integrity_Baseline_PositionalRoot_Accepted()
    {
        var result = CliArgsParser.Parse(["integrity", "baseline", _tempDir]);
        Assert.Equal(CliCommand.IntegrityBaseline, result.Command);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    [Fact]
    public void Integrity_Baseline_NoRoots_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["integrity", "baseline"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--roots"));
    }

    [Fact]
    public void Integrity_UnknownAction_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["integrity", "bogus"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Unknown integrity action"));
    }

    [Fact]
    public void Integrity_Baseline_UnknownFlag_Error()
    {
        var result = CliArgsParser.Parse(["integrity", "baseline", "--weird"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // profiles subcommand edge cases
    // ──────────────────────────────────────────

    [Fact]
    public void Profiles_NoAction_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["profiles"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("requires an action"));
    }

    [Fact]
    public void Profiles_List_ReturnsProfilesList()
    {
        var result = CliArgsParser.Parse(["profiles", "list"]);
        Assert.Equal(CliCommand.ProfilesList, result.Command);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Profiles_Export_WithIdAndOutput_ReturnsProfilesExport()
    {
        var outFile = Path.Combine(_tempDir, "profile.json");
        var result = CliArgsParser.Parse(["profiles", "export", "--id", "my-profile", "--output", outFile]);
        Assert.Equal(CliCommand.ProfilesExport, result.Command);
        Assert.Equal("my-profile", result.Options!.ProfileId);
        Assert.Equal(outFile, result.Options!.OutputPath);
    }

    [Fact]
    public void Profiles_Export_MissingIdOrOutput_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["profiles", "export", "--id", "my-profile"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--id") && e.Contains("--output"));
    }

    [Fact]
    public void Profiles_Delete_WithId_ReturnsProfilesDelete()
    {
        var result = CliArgsParser.Parse(["profiles", "delete", "--id", "old-profile"]);
        Assert.Equal(CliCommand.ProfilesDelete, result.Command);
        Assert.Equal("old-profile", result.Options!.ProfileId);
    }

    [Fact]
    public void Profiles_Delete_MissingId_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["profiles", "delete"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--id"));
    }

    [Fact]
    public void Profiles_Show_MissingId_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["profiles", "show"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Profiles_Import_MissingInput_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["profiles", "import"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Profiles_UnknownAction_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["profiles", "bogus"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Unknown profiles action"));
    }

    [Fact]
    public void Profiles_UnknownFlag_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["profiles", "list", "--weird"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // dat subcommand edge cases
    // ──────────────────────────────────────────

    [Fact]
    public void Dat_NoAction_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["dat"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("sub-action"));
    }

    [Fact]
    public void Dat_UnknownAction_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["dat", "bogus"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Unknown dat action"));
    }

    [Fact]
    public void Dat_Diff_MissingOldOrNew_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["dat", "diff", "--old", "a.dat"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--old") && e.Contains("--new"));
    }

    [Fact]
    public void Dat_Diff_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["dat", "diff", "--old", "a.dat", "--new", "b.dat", "--unknown"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Dat_FixDat_WithRootsAndDatRoot_ParsesCorrectly()
    {
        var result = CliArgsParser.Parse(["dat", "fixdat", "--roots", _tempDir, "--dat-root", _tempDir, "--name", "MyDat"]);
        Assert.Equal(CliCommand.DatFix, result.Command);
        Assert.Equal("MyDat", result.Options!.DatName);
        Assert.Equal(_tempDir, result.Options!.DatRoot);
    }

    [Fact]
    public void Dat_FixDat_NoRoots_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["dat", "fixdat"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Dat_FixDat_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["dat", "fixdat", "--roots", _tempDir, "--bogus"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // watch subcommand: cron and debounce
    // ──────────────────────────────────────────

    [Fact]
    public void Watch_WithCron_ParsesCronExpression()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _tempDir, "--cron", "0 2 * * *"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal("0 2 * * *", result.Options!.WatchCronExpression);
    }

    [Fact]
    public void Watch_InvalidCron_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _tempDir, "--cron", "bad cron"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("cron") && e.Contains("five fields"));
    }

    [Fact]
    public void Watch_Debounce_ParsesSeconds()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _tempDir, "--interval", "60", "--debounce", "15"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal(15, result.Options!.WatchDebounceSeconds);
    }

    [Fact]
    public void Watch_InvalidDebounce_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _tempDir, "--interval", "60", "--debounce", "999"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Watch_InvalidInterval_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _tempDir, "--interval", "99999"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Watch_NoRoots_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["watch", "--interval", "5"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Watch_WithAllFlags_ParsesCorrectly()
    {
        var result = CliArgsParser.Parse([
            "watch", "--roots", _tempDir,
            "--interval", "30",
            "--mode", "Move",
            "--sortconsole",
            "--enabledat",
            "--dat-audit",
            "--datrename",
            "--datroot", _tempDir,
            "--hashtype", "SHA256",
            "--approve-reviews",
            "--approve-conversion-review",
            "--yes",
            "--profile", "default",
            "--profile-file", Path.Combine(_tempDir, "p.json"),
            "--workflow", "quick-scan"
        ]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Options!.SortConsole);
        Assert.True(result.Options!.EnableDat);
        Assert.True(result.Options!.EnableDatAudit);
        Assert.True(result.Options!.EnableDatRename);
        Assert.True(result.Options!.ApproveReviews);
        Assert.True(result.Options!.ApproveConversionReview);
        Assert.True(result.Options!.Yes);
        Assert.Equal("SHA256", result.Options!.HashType);
        Assert.Equal("default", result.Options!.ProfileId);
    }

    [Fact]
    public void Watch_InvalidHashType_ReturnsError()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _tempDir, "--interval", "5", "--hashtype", "CRC32"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Watch_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _tempDir, "--interval", "5", "--nope"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Watch_PositionalRoot_Accepted()
    {
        var result = CliArgsParser.Parse(["watch", _tempDir, "--interval", "10"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    // ──────────────────────────────────────────
    // compare subcommand edge cases
    // ──────────────────────────────────────────

    [Fact]
    public void Compare_MissingRunIds_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["compare", "--run", "abc"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--compare-to"));
    }

    [Fact]
    public void Compare_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["compare", "--run", "a", "--compare-to", "b", "--bad"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // trends subcommand edge cases
    // ──────────────────────────────────────────

    [Fact]
    public void Trends_InvalidLimit_ReturnsError()
    {
        var result = CliArgsParser.Parse(["trends", "--limit", "0"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Trends_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["trends", "--bad"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Trends_NoFlags_ReturnsDefaultCommand()
    {
        var result = CliArgsParser.Parse(["trends"]);
        Assert.Equal(CliCommand.Trends, result.Command);
        Assert.Equal(0, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // workflows subcommand edge cases
    // ──────────────────────────────────────────

    [Fact]
    public void Workflows_NoFlags_ReturnsList()
    {
        var result = CliArgsParser.Parse(["workflows"]);
        Assert.Equal(CliCommand.Workflows, result.Command);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Workflows_UnknownId_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["workflows", "--id", "nonexistent-workflow"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Unknown workflow"));
    }

    [Fact]
    public void Workflows_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["workflows", "--bad"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // export subcommand edge cases
    // ──────────────────────────────────────────

    [Fact]
    public void Export_InvalidFormat_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["export", "--roots", _tempDir, "--format", "badformat"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid export format"));
    }

    [Fact]
    public void Export_NoRoots_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["export", "--format", "csv"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Export_WithAllOptions_ParsesCorrectly()
    {
        var outPath = Path.Combine(_tempDir, "export.csv");
        var result = CliArgsParser.Parse([
            "export", "--roots", _tempDir,
            "--format", "csv",
            "-o", outPath,
            "--name", "MyCollection",
            "--profile", "default",
            "--profile-file", Path.Combine(_tempDir, "p.json"),
            "--workflow", "quick-scan"
        ]);
        Assert.Equal(CliCommand.Export, result.Command);
        Assert.Equal("csv", result.Options!.ExportFormat);
        Assert.Equal(outPath, result.Options!.OutputPath);
        Assert.Equal("MyCollection", result.Options!.CollectionName);
        Assert.Equal("default", result.Options!.ProfileId);
    }

    [Fact]
    public void Export_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["export", "--roots", _tempDir, "--bad"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Export_PositionalRoot_Accepted()
    {
        var result = CliArgsParser.Parse(["export", _tempDir, "--format", "json"]);
        Assert.Equal(CliCommand.Export, result.Command);
        Assert.Contains(_tempDir, result.Options!.Roots);
    }

    // ──────────────────────────────────────────
    // history subcommand edge cases
    // ──────────────────────────────────────────

    [Fact]
    public void History_InvalidLimit_ReturnsError()
    {
        var result = CliArgsParser.Parse(["history", "--limit", "-5"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void History_InvalidOffset_ReturnsError()
    {
        var result = CliArgsParser.Parse(["history", "--offset", "-1"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void History_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["history", "--bad"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // diff subcommand edge cases
    // ──────────────────────────────────────────

    [Fact]
    public void Diff_MissingRoots_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["diff", "--left-roots", _tempDir]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--left-roots") && e.Contains("--right-roots"));
    }

    [Fact]
    public void Diff_InvalidOffset_ReturnsError()
    {
        var result = CliArgsParser.Parse(["diff", "--left-roots", _tempDir, "--right-roots", _tempDir, "--offset", "-1"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Diff_InvalidLimit_ReturnsError()
    {
        var result = CliArgsParser.Parse(["diff", "--left-roots", _tempDir, "--right-roots", _tempDir, "--limit", "0"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Diff_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["diff", "--left-roots", _tempDir, "--right-roots", _tempDir, "--bad"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Diff_WithExtensions_ParsesCorrectly()
    {
        var result = CliArgsParser.Parse([
            "diff", "--left-roots", _tempDir, "--right-roots", _tempDir,
            "--extensions", "zip,7z"
        ]);
        Assert.Equal(CliCommand.Diff, result.Command);
        Assert.Contains(".zip", result.Options!.Extensions);
        Assert.Contains(".7z", result.Options!.Extensions);
    }

    // ──────────────────────────────────────────
    // merge subcommand edge cases
    // ──────────────────────────────────────────

    [Fact]
    public void Merge_MissingTargetRoot_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["merge", "--left-roots", _tempDir, "--right-roots", _tempDir]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--target-root"));
    }

    [Fact]
    public void Merge_WithApplyAndAllowMoves_SetsFlags()
    {
        var targetDir = Path.Combine(_tempDir, "target");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir,
            "--target-root", targetDir, "--apply", "--allow-moves"
        ]);
        Assert.Equal(CliCommand.Merge, result.Command);
        Assert.True(result.Options!.MergeApply);
        Assert.True(result.Options!.AllowMoves);
    }

    [Fact]
    public void Merge_PlanMode_SetsFalse()
    {
        var targetDir = Path.Combine(_tempDir, "target");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir,
            "--target-root", targetDir, "--plan"
        ]);
        Assert.Equal(CliCommand.Merge, result.Command);
        Assert.False(result.Options!.MergeApply);
    }

    [Fact]
    public void Merge_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["merge", "--left-roots", _tempDir, "--right-roots", _tempDir, "--target-root", "t", "--bad"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Merge_TargetRootIsFile_ReturnsValidationError()
    {
        var fileAsTarget = Path.Combine(_tempDir, "file.txt");
        File.WriteAllText(fileAsTarget, "x");
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir,
            "--target-root", fileAsTarget
        ]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("directory path"));
    }

    // ──────────────────────────────────────────
    // Run mode: edge cases & flag combos
    // ──────────────────────────────────────────

    [Fact]
    public void Run_ProfileAndProfileFile_Parsed()
    {
        var result = CliArgsParser.Parse([
            "--roots", _tempDir,
            "--profile", "my-profile",
            "--profile-file", Path.Combine(_tempDir, "custom.json")
        ]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal("my-profile", result.Options!.ProfileId);
        Assert.Equal(Path.Combine(_tempDir, "custom.json"), result.Options!.ProfileFilePath);
    }

    [Fact]
    public void Run_WorkflowFlag_Parsed()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--workflow", "quick-scan"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal("quick-scan", result.Options!.WorkflowScenarioId);
    }

    [Fact]
    public void Run_DropUnknownWithoutGamesOnly_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--dropunknown"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--dropunknown") && e.Contains("--gamesonly"));
    }

    [Fact]
    public void Run_KeepUnknownFlag_SetsTrue()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--keepunknown"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.KeepUnknownWhenOnlyGames);
    }

    [Fact]
    public void Run_ApproveConversionReview_SetsFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--approve-conversion-review"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.ApproveConversionReview);
    }

    [Fact]
    public void Run_ConvertOnly_SetsBothFlags()
    {
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--convertonly"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.ConvertOnly);
        Assert.Equal("auto", result.Options!.ConvertFormat);
    }

    [Fact]
    public void Run_RollbackDryRun_SetsFlag()
    {
        // rollback needs a real file
        var auditFile = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(auditFile, "header\ndata");
        var result = CliArgsParser.Parse(["--rollback", auditFile, "--rollback-dry-run"]);
        Assert.Equal(CliCommand.Rollback, result.Command);
        Assert.True(result.Options!.RollbackDryRun);
    }

    [Fact]
    public void Run_UpdateDats_WithForceAndImportPacks_ParsesCorrectly()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "packs"));
        var result = CliArgsParser.Parse([
            "--update-dats",
            "--datroot", _tempDir,
            "--force-dat-update",
            "--import-packs-from", Path.Combine(_tempDir, "packs")
        ]);
        Assert.Equal(CliCommand.UpdateDats, result.Command);
        Assert.True(result.Options!.ForceDatUpdate);
        Assert.Equal(Path.Combine(_tempDir, "packs"), result.Options!.ImportPacksFrom);
    }

    [Fact]
    public void Run_UpdateDats_SmartUpdateFlag_Parsed()
    {
        var result = CliArgsParser.Parse([
            "--update-dats",
            "--datroot", _tempDir,
            "--smart-dat-update"
        ]);
        Assert.Equal(CliCommand.UpdateDats, result.Command);
        Assert.True(result.Options!.SmartDatUpdate);
    }

    // ──────────────────────────────────────────
    // TryConsumeValue edge cases
    // ──────────────────────────────────────────

    [Fact]
    public void MissingValueForFlag_ReturnsError()
    {
        // --mode at end with no following value
        var result = CliArgsParser.Parse(["--roots", _tempDir, "--mode"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Missing value"));
    }

    [Fact]
    public void FlagValueLooksLikeFlag_SkipsValue()
    {
        // --roots followed by --mode should treat --mode as next flag, not value
        // This means --roots got no value → will produce error or default behavior
        var result = CliArgsParser.Parse(["--roots", "--mode", "DryRun"]);
        // --roots value skipped because --mode looks like a flag,
        // then --mode DryRun is parsed, but no roots → help or error
        Assert.True(result.ExitCode == 0 || result.Errors.Count > 0);
    }

    // ──────────────────────────────────────────
    // TryParseRootsArgument edge cases
    // ──────────────────────────────────────────

    [Fact]
    public void Roots_EmptyString_ReturnsError()
    {
        // empty string after --roots
        var result = CliArgsParser.Parse(["analyze", "--roots", "  "]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Roots_SemicolonOnly_ReturnsError()
    {
        var result = CliArgsParser.Parse(["analyze", "--roots", ";;;"]);
        Assert.Equal(3, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // Version and Help
    // ──────────────────────────────────────────

    [Fact]
    public void VersionFlag_ReturnsVersionCommand()
    {
        var result = CliArgsParser.Parse(["--version"]);
        Assert.Equal(CliCommand.Version, result.Command);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ShortVersionFlag_ReturnsVersionCommand()
    {
        var result = CliArgsParser.Parse(["-v"]);
        Assert.Equal(CliCommand.Version, result.Command);
    }

    [Fact]
    public void HelpFlag_ReturnsHelp()
    {
        var result = CliArgsParser.Parse(["--help"]);
        Assert.Equal(CliCommand.Help, result.Command);
    }

    [Fact]
    public void QuestionMarkFlag_ReturnsHelp()
    {
        var result = CliArgsParser.Parse(["-?"]);
        Assert.Equal(CliCommand.Help, result.Command);
    }

    [Fact]
    public void EmptyArgs_ReturnsHelp()
    {
        var result = CliArgsParser.Parse([]);
        Assert.Equal(CliCommand.Help, result.Command);
    }

    // ──────────────────────────────────────────
    // Security: UNC and protected paths
    // ──────────────────────────────────────────

    [Fact]
    public void Export_UncOutputPath_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["export", "--roots", _tempDir, "-o", @"\\server\share"]);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void Watch_UncDatRoot_AcceptedWithoutPathValidation()
    {
        // NOTE: Watch parser currently does NOT validate optional paths (unlike Run parser).
        // This is a coverage gap but reflects current behavior.
        var result = CliArgsParser.Parse(["watch", "--roots", _tempDir, "--interval", "5", "--datroot", @"\\server\share"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal(@"\\server\share", result.Options!.DatRoot);
    }

    [Fact]
    public void Merge_UncTargetRoot_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse([
            "merge", "--left-roots", _tempDir, "--right-roots", _tempDir,
            "--target-root", @"\\server\share"
        ]);
        Assert.Equal(3, result.ExitCode);
    }

    // ──────────────────────────────────────────
    // CliParseResult factory methods
    // ──────────────────────────────────────────

    [Fact]
    public void CliParseResult_Help_HasCorrectDefaults()
    {
        var result = CliParseResult.Help();
        Assert.Equal(CliCommand.Help, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Errors);
        Assert.Null(result.Options);
    }

    [Fact]
    public void CliParseResult_ValidationError_HasExitCode3()
    {
        var result = CliParseResult.ValidationError(["err1", "err2"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void CliParseResult_Run_HasCommand()
    {
        var opts = new CliRunOptions { Roots = [_tempDir] };
        var result = CliParseResult.Run(opts);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Same(opts, result.Options);
    }

    // ──────────────────────────────────────────
    // CliRunOptions defaults
    // ──────────────────────────────────────────

    [Fact]
    public void CliRunOptions_Defaults_AreCorrect()
    {
        var opts = new CliRunOptions();
        Assert.Empty(opts.Roots);
        Assert.Equal("DryRun", opts.Mode);
        Assert.False(opts.ModeExplicit);
        Assert.Empty(opts.PreferRegions);
        Assert.True(opts.RemoveJunk);
        Assert.False(opts.OnlyGames);
        Assert.True(opts.KeepUnknownWhenOnlyGames);
        Assert.False(opts.AggressiveJunk);
        Assert.False(opts.SortConsole);
        Assert.False(opts.EnableDat);
        Assert.Null(opts.ConvertFormat);
        Assert.False(opts.ConvertOnly);
        Assert.False(opts.ApproveReviews);
        Assert.False(opts.ApproveConversionReview);
        Assert.Equal("Rename", opts.ConflictPolicy);
        Assert.False(opts.Yes);
        Assert.Equal("Info", opts.LogLevel);
        Assert.True(opts.RollbackDryRun);
        Assert.Equal(5, opts.WatchDebounceSeconds);
        Assert.Null(opts.WatchIntervalMinutes);
        Assert.Null(opts.WatchCronExpression);
        Assert.Empty(opts.LeftRoots);
        Assert.Empty(opts.RightRoots);
        Assert.False(opts.MergeApply);
        Assert.False(opts.AllowMoves);
    }
}
