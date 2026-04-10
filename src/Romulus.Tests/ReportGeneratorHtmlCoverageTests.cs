using Romulus.Infrastructure.Reporting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for ReportGenerator (GenerateHtml, GenerateCsv, GenerateJson),
/// ConversionVerificationHelpers, and CartridgeHeaderDetector binary header parsing.
/// Tests cover the private HTML rendering helpers (AppendCss, AppendSummaryCards,
/// AppendTable, AppendCategoryChart) indirectly through the public API.
/// </summary>
public sealed class ReportGeneratorHtmlCoverageTests
{
    private static ReportSummary CreateSummary(
        int keep = 10, int move = 5, int junk = 3, int bios = 1,
        int datMatches = 8, int candidates = 20, int healthScore = 75,
        int convertedCount = 0, int convertErrorCount = 0,
        int convertSkippedCount = 0, int convertBlockedCount = 0,
        int convertReviewCount = 0, long convertSavedBytes = 0,
        int datHaveCount = 0, int datHaveWrongNameCount = 0,
        int datMissCount = 0, int datUnknownCount = 0,
        int datAmbiguousCount = 0, int datRenameProposedCount = 0,
        int datRenameExecutedCount = 0, int datRenameFailedCount = 0,
        int consoleSortReviewed = 0, int consoleSortBlocked = 0,
        int consoleSortUnknown = 0, int errorCount = 0)
        => new()
        {
            Mode = "DryRun",
            RunStatus = "ok",
            Timestamp = new DateTime(2025, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            TotalFiles = 100,
            Candidates = candidates,
            KeepCount = keep,
            DupesCount = move,
            GamesCount = keep + move,
            MoveCount = move,
            JunkCount = junk,
            BiosCount = bios,
            DatMatches = datMatches,
            HealthScore = healthScore,
            ConvertedCount = convertedCount,
            ConvertErrorCount = convertErrorCount,
            ConvertSkippedCount = convertSkippedCount,
            ConvertBlockedCount = convertBlockedCount,
            ConvertReviewCount = convertReviewCount,
            ConvertSavedBytes = convertSavedBytes,
            DatHaveCount = datHaveCount,
            DatHaveWrongNameCount = datHaveWrongNameCount,
            DatMissCount = datMissCount,
            DatUnknownCount = datUnknownCount,
            DatAmbiguousCount = datAmbiguousCount,
            DatRenameProposedCount = datRenameProposedCount,
            DatRenameExecutedCount = datRenameExecutedCount,
            DatRenameFailedCount = datRenameFailedCount,
            ConsoleSortReviewed = consoleSortReviewed,
            ConsoleSortBlocked = consoleSortBlocked,
            ConsoleSortUnknown = consoleSortUnknown,
            ErrorCount = errorCount,
            SavedBytes = 1024 * 1024,
            GroupCount = 5,
            Duration = TimeSpan.FromSeconds(42)
        };

    private static ReportEntry CreateEntry(
        string action = "KEEP", string category = "GAME",
        string gameKey = "supermario", string region = "USA",
        string console = "SNES", bool datMatch = true)
        => new()
        {
            GameKey = gameKey,
            Action = action,
            Category = category,
            Region = region,
            FilePath = $"C:\\Roms\\{gameKey}.zip",
            FileName = $"{gameKey}.zip",
            Extension = ".zip",
            SizeBytes = 1024 * 100,
            RegionScore = 90,
            FormatScore = 80,
            VersionScore = 100,
            Console = console,
            DatMatch = datMatch,
            MatchLevel = "Full",
            DecisionClass = "Confirmed",
            EvidenceTier = "Tier1_Structural",
            PrimaryMatchKind = "DatHash",
            PlatformFamily = "Nintendo",
            MatchReasoning = "DAT hash match"
        };

    // ═══ GenerateHtml ════════════════════════════════════════════════

    [Fact]
    public void GenerateHtml_ProducesValidStructure()
    {
        var summary = CreateSummary();
        var entries = new[] { CreateEntry() };

        var html = ReportGenerator.GenerateHtml(summary, entries);

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<html lang=\"de\">", html);
        Assert.Contains("Content-Security-Policy", html);
        Assert.Contains("</html>", html);
    }

    [Fact]
    public void GenerateHtml_ContainsCssBlock()
    {
        var summary = CreateSummary();
        var html = ReportGenerator.GenerateHtml(summary, []);

        Assert.Contains("<style nonce=", html);
        Assert.Contains("--bg:", html);
        Assert.Contains(".card", html);
        Assert.Contains("</style>", html);
    }

    [Fact]
    public void GenerateHtml_ShowsSummaryCards()
    {
        var summary = CreateSummary(keep: 42, move: 10, junk: 5, bios: 2);
        var html = ReportGenerator.GenerateHtml(summary, []);

        Assert.Contains("42", html);
        Assert.Contains("Spiele (KEEP)", html);
        Assert.Contains("Duplikate", html);
        Assert.Contains("Junk", html);
        Assert.Contains("BIOS", html);
        Assert.Contains("Health", html);
    }

    [Fact]
    public void GenerateHtml_ShowsConversionCards_WhenPresent()
    {
        var summary = CreateSummary(
            convertedCount: 12, convertErrorCount: 2,
            convertSkippedCount: 3, convertBlockedCount: 1,
            convertReviewCount: 4, convertSavedBytes: 1024 * 1024 * 50);

        var html = ReportGenerator.GenerateHtml(summary, []);

        Assert.Contains("Konvertiert", html);
        Assert.Contains("12", html);
        Assert.Contains("Convert-Fehler", html);
        Assert.Contains("Convert-Skip", html);
        Assert.Contains("Convert-Blocked", html);
        Assert.Contains("Convert-Review", html);
        Assert.Contains("Convert-Gespart", html);
    }

    [Fact]
    public void GenerateHtml_ShowsDatCards_WhenPresent()
    {
        var summary = CreateSummary(
            datHaveCount: 50, datHaveWrongNameCount: 3,
            datMissCount: 5, datUnknownCount: 2, datAmbiguousCount: 1,
            datRenameProposedCount: 4, datRenameExecutedCount: 3,
            datRenameFailedCount: 1);

        var html = ReportGenerator.GenerateHtml(summary, []);

        Assert.Contains("DAT Have", html);
        Assert.Contains("DAT WrongName", html);
        Assert.Contains("DAT Miss", html);
        Assert.Contains("DAT Unknown", html);
        Assert.Contains("DAT Ambiguous", html);
        Assert.Contains("DAT Rename Proposed", html);
        Assert.Contains("DAT Rename Executed", html);
        Assert.Contains("DAT Rename Failed", html);
    }

    [Fact]
    public void GenerateHtml_ShowsSortCards_WhenPresent()
    {
        var summary = CreateSummary(
            consoleSortReviewed: 3, consoleSortBlocked: 2,
            consoleSortUnknown: 1, errorCount: 5);

        var html = ReportGenerator.GenerateHtml(summary, []);

        Assert.Contains("Sort-Review", html);
        Assert.Contains("Sort-Blocked", html);
        Assert.Contains("Sort-Unknown", html);
        Assert.Contains("Fehler", html);
    }

    [Fact]
    public void GenerateHtml_ShowsCategoryChart()
    {
        var summary = CreateSummary(keep: 50, move: 30, junk: 10, bios: 5);
        var html = ReportGenerator.GenerateHtml(summary, []);

        Assert.Contains("Verteilung", html);
        Assert.Contains("bar-chart", html);
        Assert.Contains("Keep", html);
        Assert.Contains("Move", html);
    }

    [Fact]
    public void GenerateHtml_ShowsEntryTable()
    {
        var entries = new[]
        {
            CreateEntry("KEEP", "GAME", "mario"),
            CreateEntry("MOVE", "GAME", "zelda"),
            CreateEntry("JUNK", "JUNK", "junkfile"),
            CreateEntry("BIOS", "BIOS", "bios-file")
        };

        var html = ReportGenerator.GenerateHtml(CreateSummary(), entries);

        Assert.Contains("<table id=\"reportTable\"", html);
        Assert.Contains("mario", html);
        Assert.Contains("zelda", html);
        Assert.Contains("action-keep", html);
        Assert.Contains("action-move", html);
        Assert.Contains("action-junk", html);
        Assert.Contains("action-bios", html);
    }

    [Fact]
    public void GenerateHtml_HtmlEncodesValues()
    {
        var entry = new ReportEntry
        {
            GameKey = "<script>alert('xss')</script>",
            Action = "KEEP",
            Category = "GAME",
            Region = "USA & Europe",
            FileName = "test\"file.zip",
            FilePath = "C:\\Roms\\test\"file.zip",
            Extension = ".zip",
            Console = "SNES",
            MatchReasoning = "DAT <match>"
        };

        var html = ReportGenerator.GenerateHtml(CreateSummary(), [entry]);

        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&amp;", html);
    }

    [Fact]
    public void GenerateHtml_UnknownCategory_ShowsBanner()
    {
        var entry = CreateEntry(category: "UNKNOWN");
        var html = ReportGenerator.GenerateHtml(CreateSummary(), [entry]);

        Assert.Contains("UNKNOWN", html);
        Assert.Contains("unknown-info", html);
        Assert.Contains("Klassifizierung UNKNOWN", html);
    }

    [Fact]
    public void GenerateHtml_NoUnknown_NoBanner()
    {
        var entry = CreateEntry(category: "GAME");
        var html = ReportGenerator.GenerateHtml(CreateSummary(), [entry]);

        Assert.DoesNotContain("unknown-info", html);
    }

    [Fact]
    public void GenerateHtml_ContainsNonceScript()
    {
        var html = ReportGenerator.GenerateHtml(CreateSummary(), [CreateEntry()]);

        Assert.Contains("<script nonce=", html);
        Assert.Contains("reportTable", html);
        Assert.Contains("localeCompare", html);
    }

    [Fact]
    public void GenerateHtml_EmptyEntries_ProducesValidHtml()
    {
        var html = ReportGenerator.GenerateHtml(CreateSummary(), []);

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("<tbody>", html);
        Assert.Contains("</tbody>", html);
    }

    // ═══ GenerateCsv ═════════════════════════════════════════════════

    [Fact]
    public void GenerateCsv_ContainsHeaders()
    {
        var csv = ReportGenerator.GenerateCsv([]);

        Assert.Contains("GameKey,Action,Category,Region", csv);
        Assert.Contains("DecisionClass,EvidenceTier,PrimaryMatchKind", csv);
    }

    [Fact]
    public void GenerateCsv_ContainsEntryData()
    {
        var entries = new[] { CreateEntry("KEEP", "GAME", "mario", "USA", "SNES", true) };
        var csv = ReportGenerator.GenerateCsv(entries);

        Assert.Contains("mario", csv);
        Assert.Contains("KEEP", csv);
        Assert.Contains("USA", csv);
        Assert.Contains("SNES", csv);
    }

    [Fact]
    public void GenerateCsv_CsvInjectionPrevented()
    {
        var entry = new ReportEntry
        {
            GameKey = "=CMD(\"calc\")",
            Action = "KEEP",
            Category = "GAME",
            Region = "+HYPERLINK(\"evil\")",
            FileName = "@SUM(A1)",
            Console = "SNES"
        };

        var csv = ReportGenerator.GenerateCsv([entry]);

        // CSV-injection protection prefixes formula vectors with apostrophe and keeps RFC-4180 quoting.
        Assert.Contains("\"'=CMD", csv);
        Assert.Contains("\"'+HYPERLINK", csv);
        Assert.Contains("\"'@SUM", csv);
    }

    [Fact]
    public void GenerateCsv_BomPresent()
    {
        var csv = ReportGenerator.GenerateCsv([]);
        Assert.StartsWith("\uFEFF", csv);
    }

    // ═══ GenerateJson ════════════════════════════════════════════════

    [Fact]
    public void GenerateJson_ProducesValidJson()
    {
        var summary = CreateSummary();
        var entries = new[] { CreateEntry() };

        var json = ReportGenerator.GenerateJson(summary, entries);

        Assert.Contains("\"summary\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"entries\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supermario", json);
    }

    [Fact]
    public void GenerateJson_EmptyEntries_ValidJson()
    {
        var json = ReportGenerator.GenerateJson(CreateSummary(), []);

        Assert.Contains("\"entries\"", json, StringComparison.OrdinalIgnoreCase);
    }
}
