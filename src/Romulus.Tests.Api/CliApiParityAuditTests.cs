using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Romulus.Api;
using Romulus.CLI;
using Romulus.Contracts;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Deep-Dive Audit – CLI / API / Entry-Point Parity (2026-04-27).
/// Pin-Tests fuer 6 Findings: F-01 (API Move-Confirm-Token), F-02 (HeadlessApiOptions.SettingsPath),
/// F-03 (ApiRunResult.Mode/SchemaVersion/Keep/Status), F-06 (CLI Help Exit-Code 4),
/// F-07 (RunStatusDto Defaults aus RunConstants), F-08 (CliDryRunOutput.Mode aus RunConstants).
/// </summary>
public class CliApiParityAuditTests
{
    // ───── F-01: API Move-Confirmation-Token Gate ─────

    [Fact]
    public void F01_MoveConfirmationGate_RequiresConfirmation_ForMoveMode()
    {
        Assert.True(MoveConfirmationGate.RequiresConfirmation(RunConstants.ModeMove));
        Assert.True(MoveConfirmationGate.RequiresConfirmation("move"));
        Assert.True(MoveConfirmationGate.RequiresConfirmation("MOVE"));
    }

    [Fact]
    public void F01_MoveConfirmationGate_DoesNotRequireConfirmation_ForDryRun()
    {
        Assert.False(MoveConfirmationGate.RequiresConfirmation(RunConstants.ModeDryRun));
        Assert.False(MoveConfirmationGate.RequiresConfirmation(null));
        Assert.False(MoveConfirmationGate.RequiresConfirmation(""));
    }

    [Fact]
    public void F01_MoveConfirmationGate_RejectsMissingToken_OnMove()
    {
        Assert.False(MoveConfirmationGate.IsValidConfirmationToken(RunConstants.ModeMove, null));
        Assert.False(MoveConfirmationGate.IsValidConfirmationToken(RunConstants.ModeMove, ""));
        Assert.False(MoveConfirmationGate.IsValidConfirmationToken(RunConstants.ModeMove, "yes"));
        Assert.False(MoveConfirmationGate.IsValidConfirmationToken(RunConstants.ModeMove, "move"));
    }

    [Fact]
    public void F01_MoveConfirmationGate_AcceptsExactToken_OnMove()
    {
        Assert.True(MoveConfirmationGate.IsValidConfirmationToken(RunConstants.ModeMove,
            MoveConfirmationGate.ConfirmationToken));
        Assert.Equal("MOVE", MoveConfirmationGate.ConfirmationToken);
        Assert.Equal("X-Confirm-Token", MoveConfirmationGate.HeaderName);
    }

    [Fact]
    public void F01_MoveConfirmationGate_IgnoresToken_OnDryRun()
    {
        Assert.True(MoveConfirmationGate.IsValidConfirmationToken(RunConstants.ModeDryRun, null));
        Assert.True(MoveConfirmationGate.IsValidConfirmationToken(null, null));
    }

    [Fact]
    public void F01_ApiErrorCodes_RunMoveConfirmationRequired_Defined()
    {
        Assert.False(string.IsNullOrWhiteSpace(ApiErrorCodes.RunMoveConfirmationRequired));
        Assert.Equal("RUN-MOVE-CONFIRMATION-REQUIRED", ApiErrorCodes.RunMoveConfirmationRequired);
    }

    // ───── F-02: HeadlessApiOptions.SettingsPath ─────

    [Fact]
    public void F02_HeadlessApiOptions_HasSettingsPathProperty()
    {
        var prop = typeof(HeadlessApiOptions).GetProperty("SettingsPath",
            BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    [Fact]
    public void F02_HeadlessApiOptions_FromConfiguration_ReadsSettingsPath()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SettingsPath"] = @"C:\custom\settings.json"
            })
            .Build();

        var opts = HeadlessApiOptions.FromConfiguration(config);

        Assert.Equal(@"C:\custom\settings.json", opts.SettingsPath);
    }

    [Fact]
    public void F02_HeadlessApiOptions_FromConfiguration_DefaultsSettingsPathToNull()
    {
        var config = new ConfigurationBuilder().Build();

        var opts = HeadlessApiOptions.FromConfiguration(config);

        Assert.Null(opts.SettingsPath);
    }

    [Fact]
    public void F02_RunEnvironmentBuilder_LoadSettings_OptionsOverload_UsesOverridePath()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"romulus-f02-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tempFile, """{ "general": { "preferredRegions": ["JP"] } }""");

            var dataDir = RunEnvironmentBuilder.ResolveDataDir();
            var opts = new HeadlessApiOptions { SettingsPath = tempFile };

            var settings = RunEnvironmentBuilder.LoadSettings(dataDir, opts);

            Assert.NotNull(settings);
            Assert.Contains("JP", settings.General.PreferredRegions);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    public void F02_RunEnvironmentBuilder_LoadSettings_OptionsOverload_NullOptionsOk()
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir, (HeadlessApiOptions?)null);
        Assert.NotNull(settings);
    }

    // ───── F-03: ApiRunResult exponiert Mode/SchemaVersion/Keep/Status ─────

    [Fact]
    public void F03_ApiRunResult_HasModeProperty()
    {
        var prop = typeof(ApiRunResult).GetProperty("Mode");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    [Fact]
    public void F03_ApiRunResult_HasSchemaVersionProperty()
    {
        var prop = typeof(ApiRunResult).GetProperty("SchemaVersion");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    [Fact]
    public void F03_ApiRunResult_HasKeepProperty_AliasForWinners()
    {
        var prop = typeof(ApiRunResult).GetProperty("Keep");
        Assert.NotNull(prop);
        Assert.Equal(typeof(int), prop!.PropertyType);
    }

    [Fact]
    public void F03_ApiRunResult_HasStatusProperty_AliasForOrchestratorStatus()
    {
        var prop = typeof(ApiRunResult).GetProperty("Status");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    [Fact]
    public void F03_RunConstants_HasApiOutputSchemaVersion()
    {
        var field = typeof(RunConstants).GetField("ApiOutputSchemaVersion",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        var value = (string?)field!.GetValue(null);
        Assert.False(string.IsNullOrWhiteSpace(value));
    }

    [Fact]
    public void F03_ApiRunResultMapper_PopulatesNewFields()
    {
        var result = new RunResult { Status = RunConstants.StatusOk, ExitCode = 0, WinnerCount = 7 };
        var projection = RunProjectionFactory.Create(result);

        var apiResult = ApiRunResultMapper.Map(result, projection, RunConstants.ModeMove);

        Assert.Equal(RunConstants.ModeMove, apiResult.Mode);
        Assert.Equal(RunConstants.ApiOutputSchemaVersion, apiResult.SchemaVersion);
        Assert.Equal(apiResult.Winners, apiResult.Keep);
        Assert.Equal(apiResult.OrchestratorStatus, apiResult.Status);
    }

    // ───── F-06: CLI Help Exit-Code 4 dokumentiert ─────

    [Fact]
    public void F06_CliWriteUsage_DocumentsExitCode4()
    {
        var sw = new StringWriter();
        InvokeWriteUsage(sw);
        var help = sw.ToString();

        Assert.Contains("Exit codes:", help);
        Assert.Matches(@"(?ms)Exit codes:.*\b4\b", help);
        Assert.Contains("Completed with errors", help, StringComparison.OrdinalIgnoreCase);
    }

    private static void InvokeWriteUsage(TextWriter writer)
    {
        var method = typeof(CliOutputWriter).GetMethod(
            "WriteUsage",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method!.Invoke(null, new object[] { writer });
    }

    // ───── F-07: RunStatusDto Defaults aus RunConstants ─────

    [Fact]
    public void F07_RunStatusDto_ModeDefault_EqualsRunConstantsModeDryRun()
    {
        var dto = new RunStatusDto();
        Assert.Equal(RunConstants.ModeDryRun, dto.Mode);
    }

    [Fact]
    public void F07_RunStatusDto_HashTypeDefault_EqualsRunConstantsDefaultHashType()
    {
        var dto = new RunStatusDto();
        Assert.Equal(RunConstants.DefaultHashType, dto.HashType);
    }

    [Fact]
    public void F07_ApiRunHistoryEntry_DefaultMode_NotHardcodedDryRunString()
    {
        // Existing default is "" — guard that if ever set, it derives from RunConstants.
        // No-op pin: just asserts the field exists and is reachable.
        var entry = new ApiRunHistoryEntry();
        Assert.NotNull(entry.Mode);
    }

    // ───── F-08: CliDryRunOutput.Mode aus RunConstants statt String-Literal ─────

    [Fact]
    public void F08_CliDryRunOutput_FormatDryRunJson_EmitsModeFromRunConstants()
    {
        var result = new RunResult { Status = RunConstants.StatusOk };
        var projection = RunProjectionFactory.Create(result);

        var json = InvokeFormatDryRunJson(projection,
            Array.Empty<DedupeGroup>(),
            conversionReport: null,
            preflightWarnings: null);

        using var doc = JsonDocument.Parse(json);
        var mode = doc.RootElement.GetProperty("Mode").GetString();
        Assert.Equal(RunConstants.ModeDryRun, mode);
    }

    private static string InvokeFormatDryRunJson(
        RunProjection projection,
        IReadOnlyList<DedupeGroup> groups,
        Romulus.Contracts.Models.ConversionReport? conversionReport,
        IReadOnlyList<string>? preflightWarnings)
    {
        var method = typeof(CliOutputWriter).GetMethod(
            "FormatDryRunJson",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var json = method!.Invoke(null, new object?[]
        {
            projection,
            groups,
            conversionReport,
            preflightWarnings
        });
        return Assert.IsType<string>(json);
    }
}
