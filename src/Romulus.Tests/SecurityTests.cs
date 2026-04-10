using Romulus.Core.GameKeys;
using Romulus.Core.Regions;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Reporting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TEST-SEC: Security tests covering symlink traversal, command injection,
/// CSV injection, path traversal, and related attack vectors.
/// </summary>
public sealed class SecurityTests : IDisposable
{
    private readonly string _tempDir;

    public SecurityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_SEC_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── TEST-SEC-01: Blocklist traversal via path manipulation ──

    [Theory]
    [InlineData(@"D:\roms\..\..\_QUARANTINE\game.zip")]
    [InlineData(@"D:\roms\_TRASH_REGION_DEDUPE\sub\game.zip")]
    [InlineData(@"D:\_BACKUP\game.zip")]
    public void Blocklist_PathTraversal_StillBlocked(string path)
    {
        Assert.True(ExecutionHelpers.IsBlocklisted(path));
    }

    [Theory]
    [InlineData(@"D:\roms\NES\game.zip")]
    [InlineData(@"D:\roms\SNES\Zelda (USA).sfc")]
    public void Blocklist_NormalPaths_NotBlocked(string path)
    {
        Assert.False(ExecutionHelpers.IsBlocklisted(path));
    }

    // ── TEST-SEC-04: CUE prefix bypass ──

    [Theory]
    [InlineData("Game (..\\..\\system\\evil).cue")]
    [InlineData("Game (..%2f..%2fsystem).iso")]
    public void GameKeyNormalizer_PathTraversalInFileName_DoesNotCrash(string input)
    {
        var key = GameKeyNormalizer.Normalize(input);
        Assert.NotNull(key);
        Assert.NotEmpty(key);
        Assert.Equal(key, key.ToLowerInvariant());
        Assert.Equal(key, GameKeyNormalizer.Normalize(key));
        Assert.DoesNotContain('\0', key);
    }

    // ── TEST-SEC-05: Zip-Slip path traversal ──

    [Theory]
    [InlineData(@"..\..\Windows\System32\evil.dll")]
    [InlineData(@"../../../etc/passwd")]
    [InlineData(@"..\..\..\autoexec.bat")]
    public void GameKeyNormalizer_ZipSlipPaths_Normalized(string input)
    {
        var key = GameKeyNormalizer.Normalize(input);
        Assert.NotNull(key);
        Assert.False(string.IsNullOrWhiteSpace(key));
        Assert.Equal(key, key.ToLowerInvariant());
        Assert.Equal(key, GameKeyNormalizer.Normalize(key));
        Assert.DoesNotContain('\0', key);
    }

    // ── TEST-SEC: CSV injection vectors ──

    [Theory]
    [InlineData("=cmd|'/C calc'!A1")]
    [InlineData("+cmd|'/C calc'!A1")]
    [InlineData("-cmd|'/C calc'!A1")]
    [InlineData("@SUM(1+1)*cmd|'/C calc'!A1")]
    public void CsvInjection_AllVectors_Prefixed(string input)
    {
        var entries = new List<ReportEntry>
        {
            new ReportEntry { GameKey = input, FileName = "test.chd", Extension = ".chd" }
        };
        var csv = ReportGenerator.GenerateCsv(entries);
        // Report CSV hardening: dangerous formula prefixes are apostrophe-prefixed and RFC-4180 quoted.
        var escaped = ("'" + input).Replace("\"", "\"\"");
        Assert.Contains($"\"{escaped}\"", csv);
    }

    [Fact]
    public void CsvInjection_EqualsFormula_IsPrefixedWithApostrophe_FindingF06()
    {
        var entries = new List<ReportEntry>
        {
            new ReportEntry { GameKey = "=CMD()", FileName = "test.chd", Extension = ".chd" }
        };

        var csv = ReportGenerator.GenerateCsv(entries);

        Assert.Contains("\"'=CMD()\"", csv, StringComparison.Ordinal);
    }

    // ── TEST-SEC: HTML injection in report ──

    [Fact]
    public void HtmlReport_ScriptInjection_Encoded()
    {
        var entries = new List<ReportEntry>
        {
            new ReportEntry
            {
                GameKey = "<img src=x onerror=alert(1)>",
                Action = "KEEP",
                FileName = "\"onmouseover=\"alert(1)",
                Extension = ".chd"
            }
        };
        var summary = new ReportSummary { Mode = "DryRun", TotalFiles = 1, KeepCount = 1 };
        var html = ReportGenerator.GenerateHtml(summary, entries);
        // The < and > must be encoded — no raw <img tag in output
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;img", html);
        // Quotes in filenames must be encoded
        Assert.Contains("&quot;", html);
    }

    // ── TEST-SEC: Report WriteHtmlToFile path traversal ──

    [Theory]
    [InlineData(@"C:\Windows\System32\evil.html")]
    [InlineData(@"..\..\evil.html")]
    public void WriteHtmlToFile_PathTraversal_Throws(string dangerousPath)
    {
        var summary = new ReportSummary();
        var entries = new List<ReportEntry>();
        Assert.Throws<InvalidOperationException>(() =>
            ReportGenerator.WriteHtmlToFile(dangerousPath, _tempDir, summary, entries));
    }

    // ── TEST-SEC: RegionDetector never throws on adversarial input ──

    [Theory]
    [InlineData("Game (\0\0\0)")]
    [InlineData("Game (\t\r\n) (USA)")]
    public void RegionDetector_AdversarialInput_NeverThrows(string input)
    {
        var ex = Record.Exception(() => RegionDetector.GetRegionTag(input));
        Assert.Null(ex);
    }

    [Fact]
    public void RegionDetector_VeryLongInput_NeverThrows()
    {
        var longInput = "Game (" + new string('A', 10000) + ")";
        var ex = Record.Exception(() => RegionDetector.GetRegionTag(longInput));
        Assert.Null(ex);
    }
}
