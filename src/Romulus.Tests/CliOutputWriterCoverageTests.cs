using System.Text.Json;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for CliOutputWriter: WriteErrors, WriteUsage, WriteMoveSummary,
/// FormatRunHistoryJson, FormatDryRunJson.
/// </summary>
public sealed class CliOutputWriterCoverageTests
{
    private static RunProjection MinProjection() => new(
        Status: "ok", ExitCode: 0,
        TotalFiles: 100, Candidates: 80, Groups: 40, Keep: 40, Dupes: 20,
        Games: 35, Unknown: 5, Junk: 10, Bios: 2, DatMatches: 30,
        ConvertedCount: 5, ConvertErrorCount: 0, ConvertSkippedCount: 0,
        ConvertBlockedCount: 0, ConvertReviewCount: 0, ConvertLossyWarningCount: 0,
        ConvertVerifyPassedCount: 5, ConvertVerifyFailedCount: 0, ConvertSavedBytes: 1024,
        DatHaveCount: 25, DatHaveWrongNameCount: 2, DatMissCount: 3, DatUnknownCount: 1,
        DatAmbiguousCount: 0, DatRenameProposedCount: 2, DatRenameExecutedCount: 1,
        DatRenameSkippedCount: 1, DatRenameFailedCount: 0,
        JunkRemovedCount: 8, FilteredNonGameCount: 0,
        MoveCount: 15, SkipCount: 2, JunkFailCount: 0,
        ConsoleSortMoved: 10, ConsoleSortFailed: 0, ConsoleSortReviewed: 0,
        ConsoleSortBlocked: 0, ConsoleSortUnknown: 3,
        FailCount: 0, SavedBytes: 50000, DurationMs: 2500, HealthScore: 85);

    // ═══ WriteErrors ═════════════════════════════════════════════════

    [Fact]
    public void WriteErrors_WritesAllErrors()
    {
        using var writer = new StringWriter();
        CliOutputWriter.WriteErrors(writer, ["Error 1", "Error 2", "Error 3"]);
        var output = writer.ToString();
        Assert.Contains("Error 1", output);
        Assert.Contains("Error 2", output);
        Assert.Contains("Error 3", output);
    }

    [Fact]
    public void WriteErrors_EmptyList_WritesNothing()
    {
        using var writer = new StringWriter();
        CliOutputWriter.WriteErrors(writer, []);
        Assert.Equal("", writer.ToString());
    }

    // ═══ WriteUsage ══════════════════════════════════════════════════

    [Fact]
    public void WriteUsage_ContainsRomulus()
    {
        using var writer = new StringWriter();
        CliOutputWriter.WriteUsage(writer);
        var output = writer.ToString();
        Assert.Contains("Romulus", output);
        Assert.Contains("--roots", output);
        Assert.Contains("--help", output);
    }

    [Fact]
    public void WriteUsage_ContainsSubcommands()
    {
        using var writer = new StringWriter();
        CliOutputWriter.WriteUsage(writer);
        var output = writer.ToString();
        Assert.Contains("analyze", output);
        Assert.Contains("export", output);
        Assert.Contains("profiles", output);
        Assert.Contains("history", output);
        Assert.Contains("watch", output);
        Assert.Contains("convert", output);
    }

    [Fact]
    public void WriteUsage_ContainsExitCodes()
    {
        using var writer = new StringWriter();
        CliOutputWriter.WriteUsage(writer);
        var output = writer.ToString();
        Assert.Contains("Exit codes:", output);
        Assert.Contains("0  Success", output);
        Assert.Contains("3  Preflight", output);
    }

    // ═══ WriteMoveSummary ════════════════════════════════════════════

    [Fact]
    public void WriteMoveSummary_WritesBasicSummary()
    {
        using var writer = new StringWriter();
        CliOutputWriter.WriteMoveSummary(writer, MinProjection(), null, null, 0);
        var output = writer.ToString();
        Assert.Contains("[Done]", output);
        Assert.Contains("15 files", output);
    }

    [Fact]
    public void WriteMoveSummary_WithConvertedCount_WritesConvertLine()
    {
        using var writer = new StringWriter();
        CliOutputWriter.WriteMoveSummary(writer, MinProjection(), null, null, 5);
        var output = writer.ToString();
        Assert.Contains("[Convert]", output);
        Assert.Contains("5 files converted", output);
    }

    [Fact]
    public void WriteMoveSummary_WithReportPath_WritesReportLine()
    {
        using var writer = new StringWriter();
        CliOutputWriter.WriteMoveSummary(writer, MinProjection(), null, @"C:\reports\test.html", 0);
        var output = writer.ToString();
        Assert.Contains("[Report]", output);
    }

    // ═══ FormatRunHistoryJson ════════════════════════════════════════

    [Fact]
    public void FormatRunHistoryJson_EmptyPage_ReturnsValidJson()
    {
        var page = new CollectionRunHistoryPage();
        var json = CliOutputWriter.FormatRunHistoryJson(page);
        Assert.NotEmpty(json);
        var parsed = JsonDocument.Parse(json);
        Assert.NotNull(parsed);
    }

    [Fact]
    public void FormatRunHistoryJson_ContainsPageProperties()
    {
        var page = new CollectionRunHistoryPage
        {
            Total = 10,
            Offset = 0,
            Limit = 200,
            Returned = 10,
            HasMore = false,
            Runs = [new CollectionRunHistoryItem { RunId = "run-1", Mode = "DryRun", Status = "ok" }]
        };
        var json = CliOutputWriter.FormatRunHistoryJson(page);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(10, root.GetProperty("Total").GetInt32());
        Assert.Equal("run-1", root.GetProperty("Runs")[0].GetProperty("RunId").GetString());
    }

    // ═══ FormatDryRunJson ════════════════════════════════════════════

    [Fact]
    public void FormatDryRunJson_EmptyGroups_ReturnsValidJson()
    {
        var json = CliOutputWriter.FormatDryRunJson(MinProjection(), []);
        Assert.NotEmpty(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("ok", root.GetProperty("Status").GetString());
        Assert.Equal(0, root.GetProperty("ExitCode").GetInt32());
        Assert.Equal("DryRun", root.GetProperty("Mode").GetString());
        Assert.Equal(100, root.GetProperty("TotalFiles").GetInt32());
    }

    [Fact]
    public void FormatDryRunJson_WithWarnings_IncludesWarnings()
    {
        var json = CliOutputWriter.FormatDryRunJson(MinProjection(), [], preflightWarnings: ["Warning 1"]);
        using var doc = JsonDocument.Parse(json);
        var warnings = doc.RootElement.GetProperty("PreflightWarnings");
        Assert.Equal(1, warnings.GetArrayLength());
        Assert.Equal("Warning 1", warnings[0].GetString());
    }

    [Fact]
    public void FormatDryRunJson_ContainsAllKpiFields()
    {
        var json = CliOutputWriter.FormatDryRunJson(MinProjection(), []);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Verify all major KPI fields are present
        Assert.True(root.TryGetProperty("TotalFiles", out _));
        Assert.True(root.TryGetProperty("Candidates", out _));
        Assert.True(root.TryGetProperty("Groups", out _));
        Assert.True(root.TryGetProperty("Keep", out _));
        Assert.True(root.TryGetProperty("Dupes", out _));
        Assert.True(root.TryGetProperty("Games", out _));
        Assert.True(root.TryGetProperty("Junk", out _));
        Assert.True(root.TryGetProperty("DatMatches", out _));
        Assert.True(root.TryGetProperty("HealthScore", out _));
        Assert.True(root.TryGetProperty("ConvertedCount", out _));
        Assert.True(root.TryGetProperty("DatHaveCount", out _));
        Assert.True(root.TryGetProperty("DatMissCount", out _));
        Assert.True(root.TryGetProperty("MoveCount", out _));
        Assert.True(root.TryGetProperty("SavedBytes", out _));
        Assert.True(root.TryGetProperty("DurationMs", out _));
    }

    // ═══ FormatDryRunJson with ConversionReport ══════════════════════

    [Fact]
    public void FormatDryRunJson_WithConversionReport_IncludesPlansAndBlocked()
    {
        var report = new ConversionReport
        {
            TotalPlanned = 3, Converted = 1, Skipped = 1, Errors = 1, Blocked = 0,
            RequiresReview = 0, TotalSavedBytes = 1024,
            Results =
            [
                new ConversionResult("game.iso", "game.chd", ConversionOutcome.Success) { Safety = ConversionSafety.Safe },
                new ConversionResult("game2.iso", null, ConversionOutcome.Error, "Tool failed") { Safety = ConversionSafety.Blocked },
                new ConversionResult("game3.iso", null, ConversionOutcome.Skipped, "Already optimal") { Safety = ConversionSafety.Safe }
            ]
        };

        var json = CliOutputWriter.FormatDryRunJson(MinProjection(), [], report);
        using var doc = JsonDocument.Parse(json);
        var plans = doc.RootElement.GetProperty("ConversionPlans");
        Assert.True(plans.GetArrayLength() >= 1); // Success + Skipped
        var blocked = doc.RootElement.GetProperty("ConversionBlocked");
        Assert.True(blocked.GetArrayLength() >= 1); // Error
    }

    [Fact]
    public void FormatDryRunJson_NullConversionReport_EmptyArrays()
    {
        var json = CliOutputWriter.FormatDryRunJson(MinProjection(), [], conversionReport: null);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("ConversionPlans").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("ConversionBlocked").GetArrayLength());
    }

    // ═══ FormatDryRunJson with DedupeGroups ══════════════════════════

    [Fact]
    public void FormatDryRunJson_WithGroups_EmitsResultsArray()
    {
        var winner = new RomCandidate
        {
            MainPath = "winner.zip", GameKey = "TestGame", DecisionClass = DecisionClass.DatVerified,
            Region = "USA", Extension = ".zip", SizeBytes = 2048
        };
        var loser = new RomCandidate
        {
            MainPath = "loser.zip", GameKey = "TestGame", DecisionClass = DecisionClass.Unknown,
            Region = "Japan", Extension = ".zip", SizeBytes = 1024
        };
        var groups = new[]
        {
            new DedupeGroup { GameKey = "TestGame", Winner = winner, Losers = [loser] }
        };

        var json = CliOutputWriter.FormatDryRunJson(MinProjection() with { Groups = 1, Keep = 1, Dupes = 1 }, groups);
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("Results");
        Assert.Equal(1, results.GetArrayLength());
        Assert.Equal("TestGame", results[0].GetProperty("GameKey").GetString());
        Assert.Equal("winner.zip", results[0].GetProperty("Winner").GetString());
    }

    // ═══ WriteMoveSummary extended ═══════════════════════════════════

    [Fact]
    public void WriteMoveSummary_WithAuditPath_WritesAuditLine()
    {
        var auditPath = Path.Combine(Path.GetTempPath(), $"CliTest_{Guid.NewGuid():N}.csv");
        File.WriteAllText(auditPath, "dummy");
        try
        {
            using var writer = new StringWriter();
            CliOutputWriter.WriteMoveSummary(writer, MinProjection(), auditPath, null, 0);
            Assert.Contains("[Audit]", writer.ToString());
        }
        finally { try { File.Delete(auditPath); } catch { } }
    }

    [Fact]
    public void WriteMoveSummary_ZeroMoves_StillWritesDone()
    {
        using var writer = new StringWriter();
        CliOutputWriter.WriteMoveSummary(writer, MinProjection() with { MoveCount = 0 }, null, null, 0);
        Assert.Contains("[Done]", writer.ToString());
    }
}
