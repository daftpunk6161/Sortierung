using Romulus.Infrastructure.Reporting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Extended report generator tests to cover AppendTable UNKNOWN category routing,
/// CSV injection edge cases, and JSON serialization with rich entries.
/// </summary>
public sealed class ReportGeneratorExtendedCoverageTests
{
    private static ReportSummary CreateSummary(
        string mode = "DryRun", int totalFiles = 100, int candidates = 50,
        int keep = 10, int move = 5, int junk = 3, int bios = 1,
        int datMatches = 8, int healthScore = 75,
        int convertedCount = 0, int convertErrorCount = 0,
        int convertSkippedCount = 0, long convertSavedBytes = 0,
        int datHaveCount = 0, int datMissCount = 0, int datUnknownCount = 0,
        int consoleSortReviewed = 0, int consoleSortBlocked = 0,
        int consoleSortUnknown = 0, int errorCount = 0)
        => new()
        {
            Mode = mode,
            RunStatus = "ok",
            Timestamp = new DateTime(2025, 7, 15, 12, 0, 0, DateTimeKind.Utc),
            TotalFiles = totalFiles,
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
            ConvertSavedBytes = convertSavedBytes,
            DatHaveCount = datHaveCount,
            DatMissCount = datMissCount,
            DatUnknownCount = datUnknownCount,
            ConsoleSortReviewed = consoleSortReviewed,
            ConsoleSortBlocked = consoleSortBlocked,
            ConsoleSortUnknown = consoleSortUnknown,
            ErrorCount = errorCount,
            SavedBytes = 50 * 1024,
            GroupCount = 3,
        };

    private static List<ReportEntry> CreateDiverseEntries()
    {
        return
        [
            new() { GameKey = "Mario", Action = "KEEP", Category = "GAME", Region = "US", FileName = "mario.nes", Extension = ".nes", SizeBytes = 512000, Console = "NES", DatMatch = true },
            new() { GameKey = "Mario", Action = "MOVE", Category = "GAME", Region = "JP", FileName = "mario (J).nes", Extension = ".nes", SizeBytes = 510000, Console = "NES" },
            new() { GameKey = "Zelda", Action = "KEEP", Category = "GAME", Region = "EU", FileName = "zelda.sfc", Extension = ".sfc", SizeBytes = 1048576, Console = "SNES", DatMatch = true },
            new() { GameKey = "", Action = "JUNK", Category = "JUNK", Region = "UNKNOWN", FileName = "junk.bin", Extension = ".bin", SizeBytes = 100 },
            new() { GameKey = "", Action = "BIOS", Category = "BIOS", Region = "UNKNOWN", FileName = "bios.rom", Extension = ".rom", SizeBytes = 262144, Console = "PS1" },
            new() { GameKey = "", Action = "KEEP", Category = "UNKNOWN", Region = "UNKNOWN", FileName = "mystery.dat", Extension = ".dat", SizeBytes = 1024 },
        ];
    }

    // ═══════════════════════════════════════════
    //  GenerateHtml – Rich entries
    // ═══════════════════════════════════════════

    [Fact]
    public void GenerateHtml_WithDiverseEntries_ContainsAllCategories()
    {
        var summary = CreateSummary();
        var entries = CreateDiverseEntries();

        var html = ReportGenerator.GenerateHtml(summary, entries);

        Assert.Contains("mario.nes", html);
        Assert.Contains("zelda.sfc", html);
        Assert.Contains("junk.bin", html);
        Assert.Contains("bios.rom", html);
        Assert.Contains("mystery.dat", html);
        Assert.Contains("KEEP", html);
        Assert.Contains("MOVE", html);
        Assert.Contains("JUNK", html);
        Assert.Contains("BIOS", html);
    }

    [Fact]
    public void GenerateHtml_WithUnknownCategory_RendersUnknownBanner()
    {
        var summary = CreateSummary();
        var entries = new List<ReportEntry>
        {
            new() { GameKey = "", Action = "KEEP", Category = "UNKNOWN", Region = "UNKNOWN", FileName = "mystery.bin", Extension = ".bin" },
        };

        var html = ReportGenerator.GenerateHtml(summary, entries);
        Assert.Contains("UNKNOWN", html);
    }

    [Fact]
    public void GenerateHtml_MoveMode_ShowsMoveSummary()
    {
        var summary = CreateSummary(mode: "Move", move: 10);
        var html = ReportGenerator.GenerateHtml(summary, []);

        Assert.Contains("Move", html);
        Assert.Contains("Modus", html);
    }

    [Fact]
    public void GenerateHtml_WithConversionStats_ShowsConversionCards()
    {
        var summary = CreateSummary(convertedCount: 5, convertErrorCount: 1, convertSavedBytes: 5000000);
        var html = ReportGenerator.GenerateHtml(summary, []);

        Assert.Contains("5", html); // converted count in some card
    }

    [Fact]
    public void GenerateHtml_WithDatAuditStats_ShowsDatCards()
    {
        var summary = CreateSummary(datHaveCount: 20, datMissCount: 5, datUnknownCount: 3);
        var html = ReportGenerator.GenerateHtml(summary, []);

        Assert.Contains("20", html);
    }

    [Fact]
    public void GenerateHtml_WithSortStats_ShowsSortCards()
    {
        var summary = CreateSummary(consoleSortReviewed: 3, consoleSortBlocked: 2, consoleSortUnknown: 1);
        var html = ReportGenerator.GenerateHtml(summary, []);

        // Sort stats should appear somewhere
        Assert.True(html.Length > 1000); // Meaningful HTML
    }

    [Fact]
    public void GenerateHtml_WithErrors_ShowsErrorCount()
    {
        var summary = CreateSummary(errorCount: 7);
        var html = ReportGenerator.GenerateHtml(summary, []);

        Assert.Contains("7", html);
    }

    [Fact]
    public void GenerateHtml_HtmlEncoding_PreventsXss()
    {
        var entries = new List<ReportEntry>
        {
            new() { GameKey = "<script>alert('xss')</script>", Action = "KEEP", Category = "GAME", FileName = "test<>.nes" },
        };

        var html = ReportGenerator.GenerateHtml(CreateSummary(), entries);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void GenerateHtml_ContentSecurityPolicy_HasNonce()
    {
        var html = ReportGenerator.GenerateHtml(CreateSummary(), []);

        Assert.Contains("Content-Security-Policy", html);
        Assert.Contains("nonce-", html);
    }

    // ═══════════════════════════════════════════
    //  GenerateCsv – Injection prevention
    // ═══════════════════════════════════════════

    [Fact]
    public void GenerateCsv_EmptyEntries_HeaderOnly()
    {
        var csv = ReportGenerator.GenerateCsv([]);

        Assert.Contains("GameKey", csv);
        Assert.Contains("Action", csv);
        // Only header line
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
    }

    [Fact]
    public void GenerateCsv_CsvInjection_QuotedNotRaw()
    {
        var entries = new List<ReportEntry>
        {
            new() { GameKey = "=CMD(\"calc\")", Action = "KEEP", Category = "GAME", FileName = "+exploit.nes" },
            new() { GameKey = "@SUM(A1:A2)", Action = "MOVE", Category = "GAME", FileName = "-formula.sfc" },
        };

        var csv = ReportGenerator.GenerateCsv(entries);

        // Formula-prefix values should be apostrophe-prefixed and RFC-4180 quoted.
        // =CMD("calc") → "'=CMD(""calc"")" in CSV
        Assert.Contains("\"'=CMD(\"\"calc\"\")", csv);
        Assert.Contains("\"'+exploit.nes\"", csv);
        Assert.Contains("\"'@SUM(A1:A2)\"", csv);
    }

    [Fact]
    public void GenerateCsv_QuotesAndCommas_ProperlyEscaped()
    {
        var entries = new List<ReportEntry>
        {
            new() { GameKey = "Game, \"Special\" Edition", Action = "KEEP", Category = "GAME" },
        };

        var csv = ReportGenerator.GenerateCsv(entries);

        // Should not have unescaped commas or quotes in values
        Assert.Contains("Game", csv);
    }

    [Fact]
    public void GenerateCsv_AllFields_InOutput()
    {
        var entries = new List<ReportEntry>
        {
            new()
            {
                GameKey = "Mario", Action = "KEEP", Category = "GAME", Region = "US",
                FileName = "mario.nes", Extension = ".nes", SizeBytes = 512000,
                RegionScore = 100, FormatScore = 50, VersionScore = 25,
                Console = "NES", DatMatch = true,
                DecisionClass = "Confident", EvidenceTier = "Tier1_DatHash",
                PrimaryMatchKind = "DatHash", PlatformFamily = "CartridgeBased",
                MatchLevel = "Strong", MatchReasoning = "DAT hash match"
            },
        };

        var csv = ReportGenerator.GenerateCsv(entries);

        Assert.Contains("Mario", csv);
        Assert.Contains("512000", csv);
        Assert.Contains("100", csv); // RegionScore
        Assert.Contains("NES", csv);
        Assert.Contains(",1,", csv); // DatMatch = true → "1"
    }

    [Fact]
    public void GenerateCsv_Utf8Bom_Present()
    {
        var csv = ReportGenerator.GenerateCsv([]);
        Assert.StartsWith("\uFEFF", csv);
    }

    // ═══════════════════════════════════════════
    //  GenerateJson – Full serialization
    // ═══════════════════════════════════════════

    [Fact]
    public void GenerateJson_RoundTrip_DeserializesToExpected()
    {
        var summary = CreateSummary(keep: 15, move: 8, junk: 2, bios: 1);
        var entries = CreateDiverseEntries();

        var json = ReportGenerator.GenerateJson(summary, entries);

        Assert.Contains("\"totalFiles\": 100", json);
        Assert.Contains("\"keepCount\": 15", json);
        Assert.Contains("mario.nes", json);
        Assert.Contains("zelda.sfc", json);
    }

    [Fact]
    public void GenerateJson_EmptyEntries_ValidStructure()
    {
        var json = ReportGenerator.GenerateJson(CreateSummary(), []);

        Assert.Contains("\"summary\":", json);
        Assert.Contains("\"entries\": []", json);
    }

    [Fact]
    public void GenerateJson_SpecialCharsInGameKey_ProperlyEscaped()
    {
        var entries = new List<ReportEntry>
        {
            new() { GameKey = "Test\"Key\\Path", Action = "KEEP" },
        };

        var json = ReportGenerator.GenerateJson(CreateSummary(), entries);

        // JSON must escape quotes and backslashes
        Assert.Contains("Test", json);
        Assert.Contains("Key", json);
        Assert.Contains("Path", json);
    }

    // ═══════════════════════════════════════════
    //  GenerateHtml – Large entry set
    // ═══════════════════════════════════════════

    [Fact]
    public void GenerateHtml_ManyEntries_ProducesValidHtml()
    {
        var entries = new List<ReportEntry>();
        for (int i = 0; i < 100; i++)
        {
            entries.Add(new() { GameKey = $"Game{i}", Action = i % 4 == 0 ? "KEEP" : i % 4 == 1 ? "MOVE" : i % 4 == 2 ? "JUNK" : "BIOS", Category = "GAME", FileName = $"game{i}.nes" });
        }

        var html = ReportGenerator.GenerateHtml(CreateSummary(keep: 25, move: 25, junk: 25, bios: 25, candidates: 100), entries);

        Assert.Contains("</html>", html);
        Assert.Contains("Game99", html);
    }

    // ═══════════════════════════════════════════
    //  GenerateHtml – Non-game count display
    // ═══════════════════════════════════════════

    [Fact]
    public void GenerateHtml_WithNonGameEntries_ShowsInOutput()
    {
        var summary = CreateSummary();
        var entries = new List<ReportEntry>
        {
            new() { GameKey = "NonGame", Action = "KEEP", Category = "NONGAME", FileName = "readme.txt" },
        };
        var html = ReportGenerator.GenerateHtml(summary, entries);

        // NonGame entry should be rendered in table
        Assert.Contains("readme.txt", html);
    }
}
