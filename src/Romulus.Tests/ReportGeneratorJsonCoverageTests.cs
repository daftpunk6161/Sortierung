using System.Text.Json;
using Romulus.Infrastructure.Reporting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for ReportGenerator JSON paths and JsonReportContext source-generated serializer.
/// Exercising all properties of ReportSummary + ReportEntry to trigger source-generated code paths.
/// </summary>
public sealed class ReportGeneratorJsonCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public ReportGeneratorJsonCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RptJson_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    #region Helpers

    private static ReportSummary MakeFullSummary() => new()
    {
        Mode = "Execute",
        RunStatus = "completed",
        Timestamp = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc),
        TotalFiles = 1000,
        Candidates = 800,
        KeepCount = 400,
        DupesCount = 200,
        GamesCount = 600,
        MoveCount = 150,
        JunkCount = 50,
        BiosCount = 10,
        DatMatches = 350,
        DatHaveCount = 300,
        DatHaveWrongNameCount = 5,
        DatMissCount = 40,
        DatUnknownCount = 20,
        DatAmbiguousCount = 3,
        DatRenameProposedCount = 15,
        DatRenameExecutedCount = 12,
        DatRenameSkippedCount = 2,
        DatRenameFailedCount = 1,
        ConvertedCount = 25,
        ConvertErrorCount = 3,
        ConvertSkippedCount = 7,
        ConvertBlockedCount = 2,
        ConvertReviewCount = 1,
        ConvertSavedBytes = 5_000_000_000L,
        JunkRemovedCount = 45,
        JunkFailCount = 5,
        SkipCount = 30,
        ConsoleSortMoved = 100,
        ConsoleSortFailed = 2,
        ConsoleSortReviewed = 5,
        ConsoleSortBlocked = 1,
        ConsoleSortUnknown = 3,
        FailCount = 10,
        ErrorCount = 8,
        SkippedCount = 20,
        SavedBytes = 10_000_000_000L,
        GroupCount = 350,
        Duration = TimeSpan.FromMinutes(5.5),
        HealthScore = 85
    };

    private static List<ReportEntry> MakeFullEntries() =>
    [
        new ReportEntry
        {
            GameKey = "Super Mario World",
            Action = "KEEP",
            Category = "GAME",
            Region = "EU",
            FilePath = @"C:\roms\snes\Super Mario World (Europe).sfc",
            FileName = "Super Mario World (Europe).sfc",
            Extension = ".sfc",
            SizeBytes = 512_000,
            RegionScore = 95,
            FormatScore = 100,
            VersionScore = 1,
            Console = "SNES",
            DecisionClass = "winner",
            EvidenceTier = "high",
            PrimaryMatchKind = "DatHash",
            PlatformFamily = "Nintendo",
            MatchLevel = "exact",
            MatchReasoning = "SHA1 match in dat",
            DatMatch = true
        },
        new ReportEntry
        {
            GameKey = "Super Mario World",
            Action = "MOVE",
            Category = "GAME",
            Region = "US",
            FilePath = @"C:\roms\snes\Super Mario World (USA).sfc",
            FileName = "Super Mario World (USA).sfc",
            Extension = ".sfc",
            SizeBytes = 512_000,
            RegionScore = 80,
            FormatScore = 100,
            VersionScore = 1,
            Console = "SNES",
            DecisionClass = "dupe",
            EvidenceTier = "high",
            PrimaryMatchKind = "DatHash",
            PlatformFamily = "Nintendo",
            MatchLevel = "exact",
            MatchReasoning = "SHA1 match in dat",
            DatMatch = true
        },
        new ReportEntry
        {
            GameKey = "Demo Game",
            Action = "JUNK",
            Category = "JUNK",
            Region = "",
            FilePath = @"C:\roms\snes\Demo Game (Sample).sfc",
            FileName = "Demo Game (Sample).sfc",
            Extension = ".sfc",
            SizeBytes = 100,
            RegionScore = 0,
            FormatScore = 0,
            VersionScore = 0,
            Console = "SNES",
            DatMatch = false
        },
        new ReportEntry
        {
            GameKey = "BIOS System",
            Action = "BIOS",
            Category = "BIOS",
            Region = "JP",
            FilePath = @"C:\roms\snes\bios.bin",
            FileName = "bios.bin",
            Extension = ".bin",
            SizeBytes = 262_144,
            Console = "SNES",
            DatMatch = true
        }
    ];

    #endregion

    // =================================================================
    //  GenerateJson — full property coverage
    // =================================================================

    [Fact]
    public void GenerateJson_AllSummaryProperties_SerializedCorrectly()
    {
        var summary = MakeFullSummary();
        var entries = MakeFullEntries();

        var json = ReportGenerator.GenerateJson(summary, entries);

        Assert.NotEmpty(json);

        // Verify camelCase naming convention
        Assert.Contains("\"mode\"", json, StringComparison.Ordinal);
        Assert.Contains("\"runStatus\"", json, StringComparison.Ordinal);
        Assert.Contains("\"totalFiles\"", json, StringComparison.Ordinal);
        Assert.Contains("\"keepCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"dupesCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"gamesCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"moveCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"junkCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"biosCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"datMatches\"", json, StringComparison.Ordinal);
        Assert.Contains("\"datHaveCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"datHaveWrongNameCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"datMissCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"datUnknownCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"datAmbiguousCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"datRenameProposedCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"datRenameExecutedCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"datRenameSkippedCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"datRenameFailedCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"convertedCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"convertErrorCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"convertSkippedCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"convertBlockedCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"convertReviewCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"convertSavedBytes\"", json, StringComparison.Ordinal);
        Assert.Contains("\"junkRemovedCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"junkFailCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"consoleSortMoved\"", json, StringComparison.Ordinal);
        Assert.Contains("\"consoleSortFailed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"consoleSortReviewed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"consoleSortBlocked\"", json, StringComparison.Ordinal);
        Assert.Contains("\"consoleSortUnknown\"", json, StringComparison.Ordinal);
        Assert.Contains("\"healthScore\"", json, StringComparison.Ordinal);
        Assert.Contains("\"savedBytes\"", json, StringComparison.Ordinal);
        Assert.Contains("\"groupCount\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateJson_AllEntryProperties_SerializedCorrectly()
    {
        var json = ReportGenerator.GenerateJson(MakeFullSummary(), MakeFullEntries());

        // Entry-level camelCase properties
        Assert.Contains("\"gameKey\"", json, StringComparison.Ordinal);
        Assert.Contains("\"action\"", json, StringComparison.Ordinal);
        Assert.Contains("\"category\"", json, StringComparison.Ordinal);
        Assert.Contains("\"region\"", json, StringComparison.Ordinal);
        Assert.Contains("\"filePath\"", json, StringComparison.Ordinal);
        Assert.Contains("\"fileName\"", json, StringComparison.Ordinal);
        Assert.Contains("\"extension\"", json, StringComparison.Ordinal);
        Assert.Contains("\"sizeBytes\"", json, StringComparison.Ordinal);
        Assert.Contains("\"regionScore\"", json, StringComparison.Ordinal);
        Assert.Contains("\"formatScore\"", json, StringComparison.Ordinal);
        Assert.Contains("\"versionScore\"", json, StringComparison.Ordinal);
        Assert.Contains("\"console\"", json, StringComparison.Ordinal);
        Assert.Contains("\"decisionClass\"", json, StringComparison.Ordinal);
        Assert.Contains("\"evidenceTier\"", json, StringComparison.Ordinal);
        Assert.Contains("\"primaryMatchKind\"", json, StringComparison.Ordinal);
        Assert.Contains("\"platformFamily\"", json, StringComparison.Ordinal);
        Assert.Contains("\"matchLevel\"", json, StringComparison.Ordinal);
        Assert.Contains("\"matchReasoning\"", json, StringComparison.Ordinal);
        Assert.Contains("\"datMatch\"", json, StringComparison.Ordinal);

        // Verify actual values
        Assert.Contains("Super Mario World", json, StringComparison.Ordinal);
        Assert.Contains("KEEP", json, StringComparison.Ordinal);
        Assert.Contains("MOVE", json, StringComparison.Ordinal);
        Assert.Contains("JUNK", json, StringComparison.Ordinal);
        Assert.Contains("BIOS", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateJson_RoundTrip_DeserializesCorrectly()
    {
        var summary = MakeFullSummary();
        var entries = MakeFullEntries();

        var json = ReportGenerator.GenerateJson(summary, entries);

        // Deserialize via same source-generated context to trigger deserialization code paths
        var deserialized = JsonSerializer.Deserialize(json, JsonReportContext.Default.JsonReport);

        Assert.NotNull(deserialized);
        Assert.Equal(summary.Mode, deserialized!.Summary.Mode);
        Assert.Equal(summary.TotalFiles, deserialized.Summary.TotalFiles);
        Assert.Equal(summary.KeepCount, deserialized.Summary.KeepCount);
        Assert.Equal(summary.DupesCount, deserialized.Summary.DupesCount);
        Assert.Equal(summary.MoveCount, deserialized.Summary.MoveCount);
        Assert.Equal(summary.JunkCount, deserialized.Summary.JunkCount);
        Assert.Equal(summary.BiosCount, deserialized.Summary.BiosCount);
        Assert.Equal(summary.DatMatches, deserialized.Summary.DatMatches);
        Assert.Equal(summary.ConvertedCount, deserialized.Summary.ConvertedCount);
        Assert.Equal(summary.ConvertSavedBytes, deserialized.Summary.ConvertSavedBytes);
        Assert.Equal(summary.HealthScore, deserialized.Summary.HealthScore);
        Assert.Equal(summary.SavedBytes, deserialized.Summary.SavedBytes);
        Assert.Equal(summary.GroupCount, deserialized.Summary.GroupCount);
        Assert.Equal(entries.Count, deserialized.Entries.Count);
    }

    [Fact]
    public void GenerateJson_RoundTrip_EntryValuesPreserved()
    {
        var entries = MakeFullEntries();
        var json = ReportGenerator.GenerateJson(MakeFullSummary(), entries);

        var deserialized = JsonSerializer.Deserialize(json, JsonReportContext.Default.JsonReport);

        var first = deserialized!.Entries[0];
        Assert.Equal("Super Mario World", first.GameKey);
        Assert.Equal("KEEP", first.Action);
        Assert.Equal("GAME", first.Category);
        Assert.Equal("EU", first.Region);
        Assert.Equal(".sfc", first.Extension);
        Assert.Equal(512_000, first.SizeBytes);
        Assert.Equal(95, first.RegionScore);
        Assert.Equal(100, first.FormatScore);
        Assert.Equal("SNES", first.Console);
        Assert.Equal("winner", first.DecisionClass);
        Assert.Equal("high", first.EvidenceTier);
        Assert.Equal("DatHash", first.PrimaryMatchKind);
        Assert.Equal("Nintendo", first.PlatformFamily);
        Assert.True(first.DatMatch);
    }

    [Fact]
    public void GenerateJson_EmptyEntries_ProducesValidJson()
    {
        var json = ReportGenerator.GenerateJson(new ReportSummary { Mode = "DryRun" }, []);

        Assert.NotEmpty(json);
        var deserialized = JsonSerializer.Deserialize(json, JsonReportContext.Default.JsonReport);
        Assert.NotNull(deserialized);
        Assert.Empty(deserialized!.Entries);
    }

    [Fact]
    public void GenerateJson_DefaultSummary_AllDefaultsSerializedExplicitly()
    {
        // DefaultIgnoreCondition.Never means all properties appear even with default values
        var json = ReportGenerator.GenerateJson(new ReportSummary(), []);

        // Verify 0-valued counts still appear
        Assert.Contains("\"keepCount\": 0", json, StringComparison.Ordinal);
        Assert.Contains("\"moveCount\": 0", json, StringComparison.Ordinal);
        Assert.Contains("\"junkCount\": 0", json, StringComparison.Ordinal);
        Assert.Contains("\"biosCount\": 0", json, StringComparison.Ordinal);
        Assert.Contains("\"healthScore\": 0", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateJson_LargeByteValues_NotTruncated()
    {
        var summary = new ReportSummary
        {
            Mode = "Execute",
            SavedBytes = long.MaxValue,
            ConvertSavedBytes = long.MaxValue - 1
        };

        var json = ReportGenerator.GenerateJson(summary, []);
        var deserialized = JsonSerializer.Deserialize(json, JsonReportContext.Default.JsonReport);

        Assert.Equal(long.MaxValue, deserialized!.Summary.SavedBytes);
        Assert.Equal(long.MaxValue - 1, deserialized.Summary.ConvertSavedBytes);
    }

    // =================================================================
    //  WriteJsonToFile
    // =================================================================

    [Fact]
    public void WriteJsonToFile_CreatesValidFile()
    {
        var reportPath = Path.Combine(_tempDir, "report.json");

        ReportGenerator.WriteJsonToFile(reportPath, _tempDir, MakeFullSummary(), MakeFullEntries());

        Assert.True(File.Exists(reportPath));
        var content = File.ReadAllText(reportPath);
        var deserialized = JsonSerializer.Deserialize(content, JsonReportContext.Default.JsonReport);
        Assert.NotNull(deserialized);
        Assert.Equal(4, deserialized!.Entries.Count);
    }

    [Fact]
    public void WriteJsonToFile_CreatesSubdirectory()
    {
        var subDir = Path.Combine(_tempDir, "reports", "sub");
        var reportPath = Path.Combine(subDir, "output.json");

        ReportGenerator.WriteJsonToFile(reportPath, _tempDir, new ReportSummary { Mode = "Test" }, []);

        Assert.True(File.Exists(reportPath));
    }

    [Fact]
    public void WriteJsonToFile_PathOutsideWorkingDir_Throws()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "outside-report.json");

        Assert.Throws<InvalidOperationException>(() =>
            ReportGenerator.WriteJsonToFile(outsidePath, _tempDir, new ReportSummary(), []));
    }

    // =================================================================
    //  WriteHtmlToFile
    // =================================================================

    [Fact]
    public void WriteHtmlToFile_CreatesValidFile()
    {
        var htmlPath = Path.Combine(_tempDir, "report.html");

        ReportGenerator.WriteHtmlToFile(htmlPath, _tempDir, MakeFullSummary(), MakeFullEntries());

        Assert.True(File.Exists(htmlPath));
        var content = File.ReadAllText(htmlPath);
        Assert.StartsWith("<!DOCTYPE html>", content);
    }

    [Fact]
    public void WriteHtmlToFile_PathOutsideWorkingDir_Throws()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "outside-report.html");

        Assert.Throws<InvalidOperationException>(() =>
            ReportGenerator.WriteHtmlToFile(outsidePath, _tempDir, new ReportSummary(), []));
    }

    // =================================================================
    //  GenerateJson with DAT rename values populated
    // =================================================================

    [Fact]
    public void GenerateJson_DatRenameFields_AllSerialized()
    {
        var summary = MakeFullSummary();
        var json = ReportGenerator.GenerateJson(summary, []);

        Assert.Contains("\"datRenameProposedCount\": 15", json, StringComparison.Ordinal);
        Assert.Contains("\"datRenameExecutedCount\": 12", json, StringComparison.Ordinal);
        Assert.Contains("\"datRenameSkippedCount\": 2", json, StringComparison.Ordinal);
        Assert.Contains("\"datRenameFailedCount\": 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateJson_ConsoleSortFields_AllSerialized()
    {
        var summary = MakeFullSummary();
        var json = ReportGenerator.GenerateJson(summary, []);

        Assert.Contains("\"consoleSortMoved\": 100", json, StringComparison.Ordinal);
        Assert.Contains("\"consoleSortFailed\": 2", json, StringComparison.Ordinal);
        Assert.Contains("\"consoleSortReviewed\": 5", json, StringComparison.Ordinal);
        Assert.Contains("\"consoleSortBlocked\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"consoleSortUnknown\": 3", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateJson_ConversionFields_AllSerialized()
    {
        var summary = MakeFullSummary();
        var json = ReportGenerator.GenerateJson(summary, []);

        Assert.Contains("\"convertedCount\": 25", json, StringComparison.Ordinal);
        Assert.Contains("\"convertErrorCount\": 3", json, StringComparison.Ordinal);
        Assert.Contains("\"convertSkippedCount\": 7", json, StringComparison.Ordinal);
        Assert.Contains("\"convertBlockedCount\": 2", json, StringComparison.Ordinal);
        Assert.Contains("\"convertReviewCount\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"convertSavedBytes\": 5000000000", json, StringComparison.Ordinal);
    }
}
