using Romulus.Infrastructure.Reporting;
using Xunit;

namespace Romulus.Tests;

public class ReportGeneratorTests
{
    private static ReportSummary MakeSummary(int keep = 10, int move = 5, int junk = 2, int bios = 1) =>
        new()
        {
            Mode = "DryRun",
            TotalFiles = keep + move + junk + bios,
            KeepCount = keep,
            MoveCount = move,
            JunkCount = junk,
            BiosCount = bios,
            GroupCount = keep,
            Duration = TimeSpan.FromSeconds(42)
        };

    private static List<ReportEntry> MakeEntries() =>
        new()
        {
            new ReportEntry { GameKey = "Super Mario", Action = "KEEP", Category = "GAME", Region = "EU",
                FileName = "Super Mario (Europe).chd", Extension = ".chd", SizeBytes = 500_000_000, Console = "PS1" },
            new ReportEntry { GameKey = "Super Mario", Action = "MOVE", Category = "GAME", Region = "US",
                FileName = "Super Mario (USA).chd", Extension = ".chd", SizeBytes = 510_000_000, Console = "PS1" },
            new ReportEntry { GameKey = "Demo Game", Action = "JUNK", Category = "JUNK", Region = "US",
                FileName = "Demo Game (Demo).iso", Extension = ".iso", SizeBytes = 100_000, Console = "PS2" }
        };

    // ===== HTML =====

    [Fact]
    public void GenerateHtml_ContainsDoctype()
    {
        var html = ReportGenerator.GenerateHtml(MakeSummary(), MakeEntries());
        Assert.StartsWith("<!DOCTYPE html>", html);
    }

    [Fact]
    public void GenerateHtml_ContainsCspMetaTag()
    {
        var html = ReportGenerator.GenerateHtml(MakeSummary(), MakeEntries());
        Assert.Contains("Content-Security-Policy", html);
        Assert.Contains("default-src 'none'", html);
    }

    [Fact]
    public void GenerateHtml_ContainsSummaryCards()
    {
        var html = ReportGenerator.GenerateHtml(MakeSummary(keep: 42), MakeEntries());
        Assert.Contains("42", html); // keep count
        Assert.Contains("Spiele (KEEP)", html);
    }

    [Fact]
    public void GenerateHtml_HtmlEncodesValues()
    {
        var entries = new List<ReportEntry>
        {
            new ReportEntry { GameKey = "<script>alert('xss')</script>", Action = "KEEP",
                FileName = "test.chd", Extension = ".chd" }
        };
        var html = ReportGenerator.GenerateHtml(MakeSummary(), entries);
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void GenerateHtml_ContainsTable()
    {
        var html = ReportGenerator.GenerateHtml(MakeSummary(), MakeEntries());
        Assert.Contains("<table", html);
        Assert.Contains("Super Mario", html);
    }

    [Fact]
    public void GenerateHtml_ContainsSortScript()
    {
        var html = ReportGenerator.GenerateHtml(MakeSummary(), MakeEntries());
        Assert.Contains("<script nonce=", html);
        Assert.Contains("addEventListener", html);
    }

    [Fact]
    public void GenerateHtml_NoInlineHandlers()
    {
        var html = ReportGenerator.GenerateHtml(MakeSummary(), MakeEntries());
        Assert.DoesNotContain("onclick=", html);
        Assert.DoesNotContain("onload=", html);
    }

    // ===== CSV =====

    [Fact]
    public void GenerateCsv_HasHeader()
    {
        var csv = ReportGenerator.GenerateCsv(MakeEntries());
        var firstLine = csv.Split('\n')[0].Trim();
        Assert.Contains("GameKey", firstLine);
        Assert.Contains("Action", firstLine);
        Assert.Contains("SizeBytes", firstLine);
    }

    [Fact]
    public void GenerateCsv_HasCorrectRowCount()
    {
        var csv = ReportGenerator.GenerateCsv(MakeEntries());
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length); // header + 3 entries
    }

    [Fact]
    public void GenerateCsv_PreventsInjection()
    {
        var entries = new List<ReportEntry>
        {
            new ReportEntry { GameKey = "=CMD()", FileName = "test.chd", Extension = ".chd" }
        };
        var csv = ReportGenerator.GenerateCsv(entries);
        // CSV-injection hardening: dangerous formula prefixes are apostrophe-prefixed.
        Assert.Contains("\"'=CMD()\"", csv);
    }

    [Fact]
    public void GenerateCsv_QuotesFieldsWithComma()
    {
        var entries = new List<ReportEntry>
        {
            new ReportEntry { GameKey = "Game, The", FileName = "test.chd", Extension = ".chd" }
        };
        var csv = ReportGenerator.GenerateCsv(entries);
        Assert.Contains("\"Game, The\"", csv);
    }

    // ===== WriteHtmlToFile =====

    [Fact]
    public void WriteHtmlToFile_PathTraversal_Throws()
    {
        var workDir = Path.GetTempPath();
        Assert.Throws<InvalidOperationException>(() =>
            ReportGenerator.WriteHtmlToFile("C:\\Windows\\evil.html", workDir,
                MakeSummary(), MakeEntries()));
    }
}
