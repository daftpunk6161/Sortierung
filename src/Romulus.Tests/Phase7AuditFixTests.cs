using Romulus.Contracts.Models;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Configuration;
using Romulus.Infrastructure.Reporting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TASK-173: Korrupte settings.json → Warning + .bak Backup.
/// TASK-174: UNKNOWN-Tooltip contract.
/// TASK-175: Trash-Integrität vor Rollback.
/// TASK-176: Config-Änderung nach DryRun Banner.
/// </summary>
public sealed class Phase7AuditFixTests : IDisposable
{
    private readonly string _tempDir;

    public Phase7AuditFixTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_P7_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ═══ TASK-173: Korrupte Settings Warning + Backup ═══════════════════

    [Fact]
    public void LoadFromSafe_MalformedJson_ReturnsWarning()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(path, "{ not valid json!!! }");

        var result = SettingsLoader.LoadFromSafe(path);

        Assert.True(result.WasCorrupt, "LoadFromSafe should flag corrupt JSON");
        Assert.NotNull(result.Settings);
        Assert.Equal("DryRun", result.Settings.General.Mode);
    }

    [Fact]
    public void LoadFromSafe_ValidJson_NoWarning()
    {
        var json = """{ "general": { "logLevel": "Debug" } }""";
        var path = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(path, json);

        var result = SettingsLoader.LoadFromSafe(path);

        Assert.False(result.WasCorrupt);
        Assert.Equal("Debug", result.Settings.General.LogLevel);
    }

    [Fact]
    public void LoadFromSafe_MissingFile_NoWarning()
    {
        var result = SettingsLoader.LoadFromSafe(Path.Combine(_tempDir, "nope.json"));

        Assert.False(result.WasCorrupt);
        Assert.NotNull(result.Settings);
    }

    [Fact]
    public void LoadFromSafe_CorruptJson_CreatesBackup()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        var corruptContent = "{ broken json content !!!";
        File.WriteAllText(path, corruptContent);

        _ = SettingsLoader.LoadFromSafe(path);

        var bakPath = path + ".bak";
        Assert.True(File.Exists(bakPath), ".bak file should be created for corrupt settings");
        Assert.Equal(corruptContent, File.ReadAllText(bakPath));
    }

    [Fact]
    public void LoadFromSafe_ValidJson_NoBackupCreated()
    {
        var json = """{ "general": { "mode": "Move" } }""";
        var path = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(path, json);

        _ = SettingsLoader.LoadFromSafe(path);

        var bakPath = path + ".bak";
        Assert.False(File.Exists(bakPath), "No .bak should be created for valid settings");
    }

    [Fact]
    public void LoadFromSafe_CorruptMessage_DescribesProblem()
    {
        var path = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(path, "<<<invalid>>>");

        var result = SettingsLoader.LoadFromSafe(path);

        Assert.True(result.WasCorrupt);
        Assert.False(string.IsNullOrWhiteSpace(result.CorruptionMessage),
            "CorruptionMessage should describe the problem");
    }

    // ═══ TASK-173: SettingsLoadResult Record ════════════════════════════

    [Fact]
    public void SettingsLoadResult_DefaultValues()
    {
        var result = new SettingsLoadResult(new RomulusSettings());

        Assert.False(result.WasCorrupt);
        Assert.Null(result.CorruptionMessage);
        Assert.NotNull(result.Settings);
    }

    // ═══ TASK-174: UNKNOWN Tooltip in HTML Report ═══════════════════════

    [Fact]
    public void HtmlReport_UnknownCategory_HasTooltip()
    {
        var entries = new List<ReportEntry>
        {
            new() { GameKey = "test", Category = "UNKNOWN", FileName = "test.rom" }
        };
        var summary = new ReportSummary { TotalFiles = 1 };

        var html = ReportGenerator.GenerateHtml(summary, entries);

        // UNKNOWN category cell must have a title tooltip explaining what it means
        Assert.Matches(@"<td\s+title=""[^""]+""\s*>UNKNOWN</td>", html);
    }

    [Fact]
    public void HtmlReport_GameCategory_NoSpecialTooltip()
    {
        var entries = new List<ReportEntry>
        {
            new() { GameKey = "test", Category = "GAME", FileName = "test.rom" }
        };
        var summary = new ReportSummary { TotalFiles = 1 };

        var html = ReportGenerator.GenerateHtml(summary, entries);

        // GAME category should NOT have a special tooltip
        Assert.DoesNotMatch(@"<td[^>]+title=""[^""]+""[^>]*>\s*GAME", html);
    }

    [Fact]
    public void HtmlReport_WithUnknownEntries_ShowsInfoBanner()
    {
        var entries = new List<ReportEntry>
        {
            new() { GameKey = "test", Category = "UNKNOWN", FileName = "test.rom" },
            new() { GameKey = "game1", Category = "GAME", FileName = "game.zip" }
        };
        var summary = new ReportSummary { TotalFiles = 2 };

        var html = ReportGenerator.GenerateHtml(summary, entries);

        // An info banner about UNKNOWN files must appear before the table
        Assert.Contains("unknown-info", html);
    }

    [Fact]
    public void HtmlReport_WithoutUnknownEntries_NoInfoBanner()
    {
        var entries = new List<ReportEntry>
        {
            new() { GameKey = "game1", Category = "GAME", FileName = "game.zip" }
        };
        var summary = new ReportSummary { TotalFiles = 1 };

        var html = ReportGenerator.GenerateHtml(summary, entries);

        Assert.DoesNotContain("unknown-info", html);
    }

    // ═══ TASK-175: Trash-Integrität vor Rollback ════════════════════════

    [Fact]
    public void VerifyTrashIntegrity_NoAuditFile_ReturnsCleanResult()
    {
        var result = RollbackService.VerifyTrashIntegrity(
            Path.Combine(_tempDir, "nonexistent.csv"), [_tempDir]);

        Assert.Equal(0, result.TotalRows);
        Assert.Equal(0, result.SkippedMissingDest);
        Assert.True(result.DryRun, "Verification must always be DryRun");
    }

    [Fact]
    public void VerifyTrashIntegrity_AlwaysReturnsDryRun()
    {
        // Even with a valid audit file, verification must never move files
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(auditPath, "Timestamp,Action,SourcePath,DestPath\n");

        var result = RollbackService.VerifyTrashIntegrity(auditPath, [_tempDir]);

        Assert.True(result.DryRun, "VerifyTrashIntegrity must always be DryRun");
    }

    // ═══ TASK-176: ShowConfigChangedBanner ══════════════════════════════

    [Fact]
    public void ShowConfigChangedBanner_FalseWhenNoPreviewCompleted()
    {
        // Without a completed DryRun, config changed banner should not show
        // This tests the property logic: banner only shows when fingerprint exists AND mismatches
        Assert.False(ShouldShowConfigChangedBanner(null, "any"),
            "No banner when no preview has been completed");
    }

    [Fact]
    public void ShowConfigChangedBanner_FalseWhenFingerprintsMatch()
    {
        Assert.False(ShouldShowConfigChangedBanner("abc123", "abc123"),
            "No banner when config fingerprints match");
    }

    [Fact]
    public void ShowConfigChangedBanner_TrueWhenFingerprintsMismatch()
    {
        Assert.True(ShouldShowConfigChangedBanner("abc123", "xyz789"),
            "Banner should show when config changed since preview");
    }

    /// <summary>
    /// TASK-176: Extracted logic for ShowConfigChangedBanner.
    /// Banner shows when: preview fingerprint exists AND current != stored.
    /// </summary>
    private static bool ShouldShowConfigChangedBanner(string? storedFingerprint, string currentFingerprint)
    {
        if (string.IsNullOrEmpty(storedFingerprint))
            return false;

        return !string.Equals(storedFingerprint, currentFingerprint, StringComparison.Ordinal);
    }
}
