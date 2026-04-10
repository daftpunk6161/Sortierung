using Romulus.Infrastructure.Reporting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for ReportGenerator pure methods: HTML, CSV, JSON generation.
/// Covers HTML-encoding, CSV-injection protection, JSON serialization.
/// </summary>
public sealed class ReportGeneratorCoverageTests
{
    private static ReportSummary MakeSummary() => new()
    {
        Mode = "DryRun",
        RunStatus = "ok",
        TotalFiles = 100,
        Candidates = 80,
        KeepCount = 60,
        DupesCount = 15,
        GamesCount = 70,
        MoveCount = 10,
        JunkCount = 5,
        BiosCount = 2,
        DatMatches = 50,
        HealthScore = 85,
        GroupCount = 12,
        Duration = TimeSpan.FromSeconds(42)
    };

    private static ReportEntry MakeEntry(string gameKey = "Mario", string action = "KEEP") => new()
    {
        GameKey = gameKey,
        Action = action,
        Region = "EU",
        FilePath = @"C:\Roms\mario.zip",
        FileName = "mario.zip",
        Extension = ".zip",
        SizeBytes = 1024,
        Console = "SNES"
    };

    #region HTML generation

    [Fact]
    public void GenerateHtml_ContainsDoctype()
    {
        var html = ReportGenerator.GenerateHtml(MakeSummary(), [MakeEntry()]);
        Assert.StartsWith("<!DOCTYPE html>", html);
    }

    [Fact]
    public void GenerateHtml_ContainsCspMeta()
    {
        var html = ReportGenerator.GenerateHtml(MakeSummary(), [MakeEntry()]);
        Assert.Contains("Content-Security-Policy", html);
    }

    [Fact]
    public void GenerateHtml_HtmlEncodesGameKey()
    {
        var entry = MakeEntry(gameKey: "<script>alert('xss')</script>");
        var html = ReportGenerator.GenerateHtml(MakeSummary(), [entry]);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void GenerateHtml_ContainsSummaryStats()
    {
        var html = ReportGenerator.GenerateHtml(MakeSummary(), [MakeEntry()]);
        Assert.Contains("100", html); // TotalFiles
        Assert.Contains("85", html);  // HealthScore
    }

    [Fact]
    public void GenerateHtml_EmptyEntries_StillGenerates()
    {
        var html = ReportGenerator.GenerateHtml(MakeSummary(), []);
        Assert.Contains("<!DOCTYPE html>", html);
    }

    [Fact]
    public void GenerateHtml_ConvertStats_WhenPresent()
    {
        var summary = MakeSummary() with { ConvertedCount = 5, ConvertErrorCount = 1, ConvertBlockedCount = 2 };
        var html = ReportGenerator.GenerateHtml(summary, [MakeEntry()]);
        Assert.Contains("5", html);
    }

    [Fact]
    public void GenerateHtml_DatStats_WhenPresent()
    {
        var summary = MakeSummary() with { DatHaveCount = 40, DatMissCount = 3, DatHaveWrongNameCount = 2 };
        var html = ReportGenerator.GenerateHtml(summary, [MakeEntry()]);
        Assert.Contains("40", html);
    }

    #endregion

    #region CSV generation

    [Fact]
    public void GenerateCsv_ContainsHeader()
    {
        var csv = ReportGenerator.GenerateCsv([MakeEntry()]);
        Assert.StartsWith("GameKey", csv);
    }

    [Fact]
    public void GenerateCsv_ContainsEntryData()
    {
        var csv = ReportGenerator.GenerateCsv([MakeEntry()]);
        Assert.Contains("Mario", csv);
        Assert.Contains("KEEP", csv);
    }

    [Fact]
    public void GenerateCsv_CsvInjection_Escaped()
    {
        var entry = MakeEntry(gameKey: "=SUM(A1)");
        var csv = ReportGenerator.GenerateCsv([entry]);
        // Should not start with = in a CSV field
        Assert.DoesNotContain("\n=SUM", csv);
    }

    [Fact]
    public void GenerateCsv_EmptyEntries_HeaderOnly()
    {
        var csv = ReportGenerator.GenerateCsv([]);
        Assert.Contains("GameKey", csv);
    }

    [Fact]
    public void GenerateCsv_MultipleEntries_AllPresent()
    {
        var entries = new[]
        {
            MakeEntry("Game1", "KEEP"),
            MakeEntry("Game2", "MOVE"),
            MakeEntry("Game3", "JUNK")
        };
        var csv = ReportGenerator.GenerateCsv(entries);
        Assert.Contains("Game1", csv);
        Assert.Contains("Game2", csv);
        Assert.Contains("Game3", csv);
    }

    #endregion

    #region JSON generation

    [Fact]
    public void GenerateJson_ValidJson()
    {
        var json = ReportGenerator.GenerateJson(MakeSummary(), [MakeEntry()]);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, doc.RootElement.GetProperty("summary").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.GetProperty("entries").ValueKind);
    }

    [Fact]
    public void GenerateJson_CamelCaseProperties()
    {
        var json = ReportGenerator.GenerateJson(MakeSummary(), [MakeEntry()]);
        Assert.Contains("\"gameKey\"", json);
        Assert.Contains("\"totalFiles\"", json);
    }

    [Fact]
    public void GenerateJson_ContainsEntryData()
    {
        var json = ReportGenerator.GenerateJson(MakeSummary(), [MakeEntry()]);
        Assert.Contains("Mario", json);
    }

    [Fact]
    public void GenerateJson_EmptyEntries_EmptyArray()
    {
        var json = ReportGenerator.GenerateJson(MakeSummary(), []);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("entries").GetArrayLength());
    }

    #endregion
}
