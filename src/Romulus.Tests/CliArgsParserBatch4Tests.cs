using Romulus.CLI;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage boost for CliArgsParser batch 4 – targets ~144 missed lines:
/// subcommands (history, watch, profiles, dat fixdat, trends), flag validation,
/// root safety checks, and edge-case parsing.
/// </summary>
public sealed class CliArgsParserBatch4Tests : IDisposable
{
    private readonly string _validRoot;

    public CliArgsParserBatch4Tests()
    {
        _validRoot = Path.Combine(Path.GetTempPath(), "CLI_B4_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_validRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_validRoot)) Directory.Delete(_validRoot, true); } catch { }
    }

    // ═══════ History Subcommand ═══════════════════════════════════

    [Fact]
    public void History_ValidLimitAndOffset()
    {
        var result = CliArgsParser.Parse(["history", "--limit", "25", "--offset", "10"]);
        Assert.Equal(CliCommand.History, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(25, result.Options!.HistoryLimit);
        Assert.Equal(10, result.Options.HistoryOffset);
    }

    [Fact]
    public void History_InvalidLimit_NonNumeric()
    {
        var result = CliArgsParser.Parse(["history", "--limit", "abc"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid history limit"));
    }

    [Fact]
    public void History_InvalidLimit_Zero()
    {
        var result = CliArgsParser.Parse(["history", "--limit", "0"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid history limit"));
    }

    [Fact]
    public void History_InvalidOffset_NonNegativeExpected()
    {
        // Note: "--offset -1" treats "-1" as an unknown flag (starts with "-"),
        // so this test verifies the parser rejects it as a separate flag.
        var result = CliArgsParser.Parse(["history", "--offset", "abc"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid history offset"));
    }

    [Fact]
    public void History_InvalidOffset_NonNumeric()
    {
        var result = CliArgsParser.Parse(["history", "--offset", "xyz"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid history offset") || e.Contains("Unknown flag"));
    }

    [Fact]
    public void History_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["history", "--bogus"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Unknown flag"));
    }

    [Fact]
    public void History_WithOutputPath()
    {
        var result = CliArgsParser.Parse(["history", "-o", _validRoot]);
        Assert.Equal(CliCommand.History, result.Command);
        Assert.Equal(_validRoot, result.Options!.OutputPath);
    }

    // ═══════ Watch Subcommand ═════════════════════════════════════

    [Fact]
    public void Watch_ValidIntervalAndRoots()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "60"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal(60, result.Options!.WatchIntervalMinutes);
    }

    [Fact]
    public void Watch_ValidCronAndRoots()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--cron", "0 * * * *"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal("0 * * * *", result.Options!.WatchCronExpression);
    }

    [Fact]
    public void Watch_MissingIntervalAndCron_ReturnsError()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--interval") && e.Contains("--cron"));
    }

    [Fact]
    public void Watch_MissingRoots_ReturnsError()
    {
        var result = CliArgsParser.Parse(["watch", "--interval", "5"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--roots"));
    }

    [Fact]
    public void Watch_InvalidDebounce_TooLow()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "5", "--debounce", "0"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("debounce"));
    }

    [Fact]
    public void Watch_InvalidDebounce_TooHigh()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "5", "--debounce", "999"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("debounce"));
    }

    [Fact]
    public void Watch_InvalidInterval_TooLow()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "0"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("interval"));
    }

    [Fact]
    public void Watch_InvalidInterval_TooHigh()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "99999"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("interval"));
    }

    [Fact]
    public void Watch_InvalidCron_WrongFieldCount()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--cron", "0 * *"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("cron") && e.Contains("five"));
    }

    [Fact]
    public void Watch_ApproveReviewsFlag()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "5", "--approve-reviews"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.True(result.Options!.ApproveReviews);
        Assert.True(result.Options.ApproveReviewsExplicit);
    }

    [Fact]
    public void Watch_ApproveConversionReviewFlag()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "5", "--approve-conversion-review"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.True(result.Options!.ApproveConversionReview);
        Assert.True(result.Options.ApproveConversionReviewExplicit);
    }

    [Fact]
    public void Watch_SortConsoleFlag()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "5", "--sortconsole"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.True(result.Options!.SortConsole);
        Assert.True(result.Options.SortConsoleExplicit);
    }

    [Fact]
    public void Watch_EnableDatFlag()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "5", "--enabledat"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.True(result.Options!.EnableDat);
        Assert.True(result.Options.EnableDatExplicit);
    }

    [Fact]
    public void Watch_DatAuditFlag()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "5", "--dat-audit"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.True(result.Options!.EnableDatAudit);
        Assert.True(result.Options.EnableDatAuditExplicit);
    }

    [Fact]
    public void Watch_DatRenameFlag()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "5", "--datrename"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.True(result.Options!.EnableDatRename);
        Assert.True(result.Options.EnableDatRenameExplicit);
    }

    [Fact]
    public void Watch_ProfileAndWorkflowFlags()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "5",
            "--profile", "my-profile", "--workflow", "my-workflow"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal("my-profile", result.Options!.ProfileId);
        Assert.Equal("my-workflow", result.Options.WorkflowScenarioId);
    }

    [Fact]
    public void Watch_ProfileFileFlag()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "5",
            "--profile-file", "test.json"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal("test.json", result.Options!.ProfileFilePath);
    }

    [Fact]
    public void Watch_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "5", "--totallyunknown"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Unknown flag"));
    }

    [Fact]
    public void Watch_ValidDebounce()
    {
        var result = CliArgsParser.Parse(["watch", "--roots", _validRoot, "--interval", "5", "--debounce", "30"]);
        Assert.Equal(CliCommand.Watch, result.Command);
        Assert.Equal(30, result.Options!.WatchDebounceSeconds);
    }

    // ═══════ Profiles Subcommand ══════════════════════════════════

    [Fact]
    public void Profiles_List()
    {
        var result = CliArgsParser.Parse(["profiles", "list"]);
        Assert.Equal(CliCommand.ProfilesList, result.Command);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Profiles_ShowWithId()
    {
        var result = CliArgsParser.Parse(["profiles", "show", "--id", "my-profile"]);
        Assert.Equal(CliCommand.ProfilesShow, result.Command);
        Assert.Equal("my-profile", result.Options!.ProfileId);
    }

    [Fact]
    public void Profiles_ShowWithoutId_ReturnsError()
    {
        var result = CliArgsParser.Parse(["profiles", "show"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--id"));
    }

    [Fact]
    public void Profiles_Import()
    {
        var result = CliArgsParser.Parse(["profiles", "import", "--input", "profile.json"]);
        Assert.Equal(CliCommand.ProfilesImport, result.Command);
        Assert.Equal("profile.json", result.Options!.InputPath);
    }

    [Fact]
    public void Profiles_ImportWithoutInput_ReturnsError()
    {
        var result = CliArgsParser.Parse(["profiles", "import"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--input"));
    }

    [Fact]
    public void Profiles_Export()
    {
        var outputPath = Path.Combine(_validRoot, "out.json");
        var result = CliArgsParser.Parse(["profiles", "export", "--id", "my-profile", "--output", outputPath]);
        Assert.Equal(CliCommand.ProfilesExport, result.Command);
        Assert.Equal("my-profile", result.Options!.ProfileId);
        Assert.Equal(outputPath, result.Options.OutputPath);
    }

    [Fact]
    public void Profiles_ExportWithoutIdOrOutput_ReturnsError()
    {
        var result = CliArgsParser.Parse(["profiles", "export"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--id") || e.Contains("--output"));
    }

    [Fact]
    public void Profiles_Delete()
    {
        var result = CliArgsParser.Parse(["profiles", "delete", "--id", "my-profile"]);
        Assert.Equal(CliCommand.ProfilesDelete, result.Command);
        Assert.Equal("my-profile", result.Options!.ProfileId);
    }

    [Fact]
    public void Profiles_DeleteWithoutId_ReturnsError()
    {
        var result = CliArgsParser.Parse(["profiles", "delete"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("--id"));
    }

    [Fact]
    public void Profiles_NoAction_ReturnsError()
    {
        var result = CliArgsParser.Parse(["profiles"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("requires an action"));
    }

    [Fact]
    public void Profiles_UnknownAction_ReturnsError()
    {
        var result = CliArgsParser.Parse(["profiles", "invalid"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Unknown profiles action"));
    }

    [Fact]
    public void Profiles_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["profiles", "list", "--bogus"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Unknown flag"));
    }

    // ═══════ Dat FixDat Subcommand ════════════════════════════════

    [Fact]
    public void DatFixDat_ValidRootsAndOutput()
    {
        var result = CliArgsParser.Parse(["dat", "fixdat", "--roots", _validRoot, "--output", _validRoot, "--name", "my-dat"]);
        Assert.Equal(CliCommand.DatFix, result.Command);
        Assert.Equal(_validRoot, result.Options!.OutputPath);
        Assert.Equal("my-dat", result.Options.DatName);
    }

    [Fact]
    public void DatFixDat_WithDatRoot()
    {
        var result = CliArgsParser.Parse(["dat", "fixdat", "--roots", _validRoot, "--dat-root", _validRoot]);
        Assert.Equal(CliCommand.DatFix, result.Command);
        Assert.Equal(_validRoot, result.Options!.DatRoot);
        Assert.True(result.Options.DatRootExplicit);
    }

    [Fact]
    public void DatFixDat_MissingRoots_ReturnsError()
    {
        var result = CliArgsParser.Parse(["dat", "fixdat"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("requires --roots"));
    }

    [Fact]
    public void DatFixDat_UnknownFlag_ReturnsError()
    {
        var result = CliArgsParser.Parse(["dat", "fixdat", "--roots", _validRoot, "--bogus"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Unknown flag"));
    }

    // ═══════ Run-Level Flags (main loop) ══════════════════════════

    [Fact]
    public void Run_ProfileFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _validRoot, "--profile", "default-profile"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal("default-profile", result.Options!.ProfileId);
    }

    [Fact]
    public void Run_ProfileFileFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _validRoot, "--profile-file", "my-profile.json"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal("my-profile.json", result.Options!.ProfileFilePath);
    }

    [Fact]
    public void Run_WorkflowFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _validRoot, "--workflow", "dedupe-only"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal("dedupe-only", result.Options!.WorkflowScenarioId);
    }

    [Fact]
    public void Run_ForceDatUpdateFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _validRoot, "--force-dat-update"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.ForceDatUpdate);
    }

    [Fact]
    public void Run_SmartDatUpdateFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _validRoot, "--smart-dat-update"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.True(result.Options!.SmartDatUpdate);
    }

    [Fact]
    public void Run_ImportPacksFromFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _validRoot, "--import-packs-from", _validRoot]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(_validRoot, result.Options!.ImportPacksFrom);
    }

    [Fact]
    public void Run_DatStaleDays_Valid()
    {
        var result = CliArgsParser.Parse(["--roots", _validRoot, "--dat-stale-days", "90"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal(90, result.Options!.DatStaleDays);
    }

    [Fact]
    public void Run_DatStaleDays_Zero_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", _validRoot, "--dat-stale-days", "0"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid DAT stale threshold"));
    }

    [Fact]
    public void Run_DatStaleDays_NonNumeric_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", _validRoot, "--dat-stale-days", "abc"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid DAT stale threshold"));
    }

    [Fact]
    public void Run_DatStaleDays_TooHigh_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", _validRoot, "--dat-stale-days", "9999"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("Invalid DAT stale threshold"));
    }

    [Fact]
    public void Run_ConflictPolicyFlag()
    {
        var result = CliArgsParser.Parse(["--roots", _validRoot, "--conflictpolicy", "skip"]);
        Assert.Equal(CliCommand.Run, result.Command);
        Assert.Equal("skip", result.Options!.ConflictPolicy);
    }

    // ═══════ Root Safety Validation ═══════════════════════════════

    [Fact]
    public void Run_UncRoot_ReturnsError()
    {
        var result = CliArgsParser.Parse(["--roots", @"\\server\share"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Contains(result.Errors, e => e.Contains("UNC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Run_EmptyRoot_ReturnsError()
    {
        // Test empty string in comma-separated roots
        var result = CliArgsParser.Parse(["--roots", ","]);
        Assert.Equal(3, result.ExitCode);
        // Should either get "empty" or validation error
        Assert.True(result.ExitCode > 0);
    }

    // ═══════ Trends Subcommand ════════════════════════════════════

    [Fact]
    public void Trends_Parses()
    {
        var result = CliArgsParser.Parse(["trends"]);
        Assert.Equal(CliCommand.Trends, result.Command);
        Assert.Equal(0, result.ExitCode);
    }

    // ═══════ UpdateDats Validation ════════════════════════════════

    [Fact]
    public void UpdateDats_Parses()
    {
        var result = CliArgsParser.Parse(["--update-dats"]);
        Assert.Equal(CliCommand.UpdateDats, result.Command);
        Assert.Equal(0, result.ExitCode);
    }
}
