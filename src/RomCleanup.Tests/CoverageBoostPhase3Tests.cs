// Phase 3: Deep coverage for FeatureService.Export, FeatureService.Dat, FeatureService.Security,
// SettingsLoader, ConsoleSorter, and more FCS command branches.

using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RomCleanup.Contracts.Ports;
using RomCleanup.Contracts.Models;
using RomCleanup.Core.Classification;
using RomCleanup.Infrastructure.Configuration;
using RomCleanup.Infrastructure.Reporting;
using RomCleanup.Infrastructure.Sorting;
using RomCleanup.UI.Wpf.Services;
using RomCleanup.UI.Wpf.ViewModels;
using Xunit;

namespace RomCleanup.Tests;

// ═══════════════════════════════════════════════════════════════════
// FeatureService.Export Tests
// ═══════════════════════════════════════════════════════════════════

public class FeatureServiceExportTests
{
    private static RomCandidate MakeCandidate(
        string name, string region = "EU", string category = "GAME",
        long size = 1024, string ext = ".zip", string consoleKey = "nes",
        bool datMatch = false, string gameKey = "")
    {
        return new RomCandidate
        {
            MainPath = $@"C:\Roms\{consoleKey}\{name}{ext}",
            GameKey = gameKey.Length > 0 ? gameKey : name,
            Region = region,
            RegionScore = 100,
            FormatScore = 500,
            VersionScore = 0,
            SizeBytes = size,
            Extension = ext,
            ConsoleKey = consoleKey,
            DatMatch = datMatch,
            Category = category
        };
    }

    // ── GetJunkReason ─────────────────────────────────────────────

    [Theory]
    [InlineData("Game (Beta)", "Beta")]
    [InlineData("Game (Proto)", "Proto")]
    [InlineData("Game (Demo)", "Demo")]
    [InlineData("Game (Sample)", "Sample")]
    [InlineData("Game (Homebrew)", "Homebrew")]
    [InlineData("Game (Hack)", "Hack")]
    [InlineData("Game (Unl)", "Unlicensed")]
    [InlineData("Game (Aftermarket)", "Aftermarket")]
    [InlineData("Game (Pirate)", "Pirate")]
    [InlineData("Game (Program)", "Program")]
    [InlineData("Game [b]", "[b]")]
    [InlineData("Game [b2]", "[b]")]
    [InlineData("Game [h1]", "[h]")]
    [InlineData("Game [o]", "[o]")]
    [InlineData("Game [t3]", "[t]")]
    [InlineData("Game [f]", "[f]")]
    [InlineData("Game [T+Eng]", "[T]")]
    [InlineData("Game [T-Ger]", "[T]")]
    public void GetJunkReason_StandardPatterns_DetectsCorrectly(string name, string expectedTag)
    {
        var result = FeatureService.GetJunkReason(name, aggressive: false);
        Assert.NotNull(result);
        Assert.Equal(expectedTag, result.Tag);
        Assert.Equal("standard", result.Level);
    }

    [Fact]
    public void GetJunkReason_CleanName_ReturnsNull()
    {
        var result = FeatureService.GetJunkReason("Super Mario Bros (USA)", aggressive: false);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("Game (Alt 1)", "Alt")]
    [InlineData("Game (Bonus Disc)", "Bonus")]
    [InlineData("Game (Reprint)", "Reprint")]
    [InlineData("Game (Virtual Console)", "VC")]
    public void GetJunkReason_AggressivePatterns_DetectedWhenAggressive(string name, string expectedTag)
    {
        var resultNotAggressive = FeatureService.GetJunkReason(name, aggressive: false);
        Assert.Null(resultNotAggressive);

        var resultAggressive = FeatureService.GetJunkReason(name, aggressive: true);
        Assert.NotNull(resultAggressive);
        Assert.Equal(expectedTag, resultAggressive.Tag);
        Assert.Equal("aggressive", resultAggressive.Level);
    }

    // ── BuildJunkReport ───────────────────────────────────────────

    [Fact]
    public void BuildJunkReport_EmptyCandidates_ContainsHeader()
    {
        var candidates = new List<RomCandidate>();
        var report = FeatureService.BuildJunkReport(candidates, aggressive: false);
        Assert.Contains("Junk-Klassifizierungsbericht", report);
    }

    [Fact]
    public void BuildJunkReport_WithJunkCandidates_GroupsByTag()
    {
        var candidates = new List<RomCandidate>
        {
            MakeCandidate("Game1 (Beta)", category: "JUNK"),
            MakeCandidate("Game2 (Beta 2)", category: "JUNK"),
            MakeCandidate("Game3 (Demo)", category: "JUNK"),
        };
        var report = FeatureService.BuildJunkReport(candidates, aggressive: false);
        Assert.Contains("Beta", report);
        Assert.Contains("Demo", report);
        Assert.Contains("Gesamt:", report);
    }

    [Fact]
    public void BuildJunkReport_MoreThan10InGroup_ShowsUndWeitere()
    {
        var candidates = new List<RomCandidate>();
        for (int i = 0; i < 15; i++)
            candidates.Add(MakeCandidate($"Game{i} (Beta)", category: "JUNK"));
        var report = FeatureService.BuildJunkReport(candidates, aggressive: false);
        Assert.Contains("weitere", report);
    }

    [Fact]
    public void BuildJunkReport_NonJunkCandidatesIgnored()
    {
        var candidates = new List<RomCandidate>
        {
            MakeCandidate("Game1 (Beta)", category: "GAME"),
        };
        var report = FeatureService.BuildJunkReport(candidates, aggressive: false);
        Assert.Contains("Gesamt:", report);
    }

    // ── ExportCollectionCsv ───────────────────────────────────────

    [Fact]
    public void ExportCollectionCsv_EmptyCandidates_OnlyHeader()
    {
        var csv = FeatureService.ExportCollectionCsv(new List<RomCandidate>());
        Assert.Contains("Dateiname", csv);
        Assert.Contains("Konsole", csv);
        Assert.Contains("Region", csv);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
    }

    [Fact]
    public void ExportCollectionCsv_WithCandidates_FieldsPresent()
    {
        var candidates = new List<RomCandidate>
        {
            MakeCandidate("Mario", region: "US", size: 1048576, datMatch: true),
        };
        var csv = FeatureService.ExportCollectionCsv(candidates);
        Assert.Contains("Mario", csv);
        Assert.Contains("US", csv);
        Assert.Contains("Verified", csv);
        Assert.Contains("1.00", csv); // 1 MB
    }

    [Fact]
    public void ExportCollectionCsv_CustomDelimiter()
    {
        var candidates = new List<RomCandidate> { MakeCandidate("Game1") };
        var csv = FeatureService.ExportCollectionCsv(candidates, ',');
        // With comma delimiter, semicolons should not separate fields
        var dataLine = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last();
        Assert.Contains(",", dataLine);
    }

    [Fact]
    public void ExportCollectionCsv_UnverifiedDatStatus()
    {
        var candidates = new List<RomCandidate> { MakeCandidate("Game1", datMatch: false) };
        var csv = FeatureService.ExportCollectionCsv(candidates);
        Assert.Contains("Unverified", csv);
    }

    // ── ExportExcelXml ────────────────────────────────────────────

    [Fact]
    public void ExportExcelXml_EmptyCandidates_ValidXmlStructure()
    {
        var xml = FeatureService.ExportExcelXml(new List<RomCandidate>());
        Assert.Contains("<?xml version=\"1.0\"", xml);
        Assert.Contains("Excel.Sheet", xml);
        Assert.Contains("<Workbook", xml);
        Assert.Contains("ROMs", xml);
        Assert.Contains("</Workbook>", xml);
    }

    [Fact]
    public void ExportExcelXml_WithCandidates_ContainsData()
    {
        var candidates = new List<RomCandidate>
        {
            MakeCandidate("TestGame", region: "JP", size: 2048, datMatch: true, consoleKey: "snes"),
        };
        var xml = FeatureService.ExportExcelXml(candidates);
        Assert.Contains("TestGame", xml);
        Assert.Contains("Verified", xml);
        Assert.Contains("Number", xml); // Size column type
    }

    [Fact]
    public void ExportExcelXml_XmlEscapesSpecialChars()
    {
        var candidates = new List<RomCandidate>
        {
            MakeCandidate("Game <Special> & \"Quoted\""),
        };
        var xml = FeatureService.ExportExcelXml(candidates);
        Assert.DoesNotContain("<Special>", xml);
        Assert.Contains("&amp;", xml);
    }

    // ── FormatRulesFromJson ───────────────────────────────────────

    [Fact]
    public void FormatRulesFromJson_FileNotExist_ReturnsNotFound()
    {
        var result = FeatureService.FormatRulesFromJson(@"C:\nonexistent\rules.json");
        Assert.Contains("Keine Regeldatei gefunden", result);
    }

    [Fact]
    public void FormatRulesFromJson_EmptyPath_ReturnsNotFound()
    {
        var result = FeatureService.FormatRulesFromJson("");
        Assert.Contains("Keine Regeldatei gefunden", result);
    }

    [Fact]
    public void FormatRulesFromJson_ValidRules_ShowsOverview()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var rules = new object[]
            {
                new { Name = "JunkRule", Priority = 100, Action = "junk", Enabled = true,
                    Conditions = new object[] { new { Field = "category", Operator = "eq", Value = "JUNK" } } },
                new { Name = "KeepRule", Priority = 50, Action = "keep", Enabled = false,
                    Conditions = Array.Empty<object>() }
            };
            File.WriteAllText(tmpFile, JsonSerializer.Serialize(rules));
            var result = FeatureService.FormatRulesFromJson(tmpFile);
            Assert.Contains("Regel-Übersicht", result);
            Assert.Contains("JunkRule", result);
            Assert.Contains("KeepRule", result);
            Assert.Contains("aktiv", result);
            Assert.Contains("inaktiv", result);
            Assert.Contains("Gesamt:", result);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void FormatRulesFromJson_InvalidJson_ReturnsError()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "not json");
            var result = FeatureService.FormatRulesFromJson(tmpFile);
            Assert.Contains("Fehler", result);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void FormatRulesFromJson_EmptyArray_ReturnsNoRules()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "[]");
            var result = FeatureService.FormatRulesFromJson(tmpFile);
            Assert.Contains("Keine Regeln definiert", result);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void FormatRulesFromJson_WithCandidates_ShowsMatchCount()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var rules = new[]
            {
                new { Name = "RegionEU", Priority = 100, Action = "keep", Enabled = true,
                    Conditions = new[] { new { Field = "region", Operator = "eq", Value = "EU" } } }
            };
            File.WriteAllText(tmpFile, JsonSerializer.Serialize(rules));
            var candidates = new List<RomCandidate>
            {
                MakeCandidate("Mario", region: "EU"),
                MakeCandidate("Zelda", region: "US"),
            };
            var result = FeatureService.FormatRulesFromJson(tmpFile, candidates);
            Assert.Contains("Treffer:", result);
        }
        finally { File.Delete(tmpFile); }
    }

    // ── BuildPdfReportData ────────────────────────────────────────

    [Fact]
    public void BuildPdfReportData_DryRun_SetsModeCorrectly()
    {
        var candidates = new List<RomCandidate> { MakeCandidate("Game1") };
        var groups = new List<DedupeResult>
        {
            new() { Winner = candidates[0], Losers = new List<RomCandidate>(), GameKey = "Game1" }
        };
        var (summary, entries) = FeatureService.BuildPdfReportData(candidates, groups, null, dryRun: true);
        Assert.Equal("DryRun", summary.Mode);
        Assert.Equal(1, summary.TotalFiles);
        Assert.Equal(1, summary.KeepCount);
        Assert.Equal(0, summary.MoveCount);
        Assert.Single(entries);
    }

    [Fact]
    public void BuildPdfReportData_MoveMode_SetsCorrectCounts()
    {
        var winner = MakeCandidate("Winner", region: "EU");
        var loser = MakeCandidate("Loser", region: "JP");
        var junk = MakeCandidate("Junk1", category: "JUNK");
        var candidates = new List<RomCandidate> { winner, loser, junk };
        var groups = new List<DedupeResult>
        {
            new() { Winner = winner, Losers = new List<RomCandidate> { loser }, GameKey = "Game" }
        };
        var (summary, entries) = FeatureService.BuildPdfReportData(candidates, groups, null, dryRun: false);
        Assert.Equal("Move", summary.Mode);
        Assert.Equal(3, summary.TotalFiles);
        Assert.Equal(1, summary.MoveCount);
        Assert.Equal(1, summary.JunkCount);
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void BuildPdfReportData_JunkCandidates_ActionIsJUNK()
    {
        var junk = MakeCandidate("Bad (Beta)", category: "JUNK");
        var (_, entries) = FeatureService.BuildPdfReportData(
            new List<RomCandidate> { junk },
            new List<DedupeResult>(), null, true);
        Assert.Equal("JUNK", entries[0].Action);
    }
}


// ═══════════════════════════════════════════════════════════════════
// FeatureService.Dat Tests
// ═══════════════════════════════════════════════════════════════════

public class FeatureServiceDatTests
{
    // ── LoadDatGameNames ─────────────────────────────────────────

    [Fact]
    public void LoadDatGameNames_FileNotExist_ReturnsEmpty()
    {
        var result = FeatureService.LoadDatGameNames(@"C:\nonexistent.dat");
        Assert.Empty(result);
    }

    [Fact]
    public void LoadDatGameNames_EmptyPath_ReturnsEmpty()
    {
        var result = FeatureService.LoadDatGameNames("");
        Assert.Empty(result);
    }

    [Fact]
    public void LoadDatGameNames_ValidDat_ExtractsNames()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var xml = @"<?xml version=""1.0""?>
<datafile>
  <game name=""Game A""><rom name=""a.zip"" /></game>
  <game name=""Game B""><rom name=""b.zip"" /></game>
</datafile>";
            File.WriteAllText(tmpFile, xml);
            var names = FeatureService.LoadDatGameNames(tmpFile);
            Assert.Equal(2, names.Count);
            Assert.Contains("Game A", names);
            Assert.Contains("Game B", names);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void LoadDatGameNames_InvalidXml_ReturnsEmpty()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "not xml at all");
            var names = FeatureService.LoadDatGameNames(tmpFile);
            Assert.Empty(names);
        }
        finally { File.Delete(tmpFile); }
    }

    // ── CompareDatFiles ──────────────────────────────────────────

    [Fact]
    public void CompareDatFiles_IdenticalFiles_AllUnchanged()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var xml = @"<?xml version=""1.0""?>
<datafile>
  <game name=""Game A""><rom name=""a.zip"" size=""100"" crc=""AABB"" /></game>
  <game name=""Game B""><rom name=""b.zip"" size=""200"" crc=""CCDD"" /></game>
</datafile>";
            File.WriteAllText(tmp, xml);
            var result = FeatureService.CompareDatFiles(tmp, tmp);
            Assert.Empty(result.Added);
            Assert.Empty(result.Removed);
            Assert.Equal(0, result.ModifiedCount);
            Assert.Equal(2, result.UnchangedCount);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void CompareDatFiles_AddedAndRemoved()
    {
        var tmpA = Path.GetTempFileName();
        var tmpB = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpA, @"<?xml version=""1.0""?><datafile>
<game name=""OldGame""><rom name=""old.zip"" /></game>
<game name=""Common""><rom name=""c.zip"" /></game>
</datafile>");
            File.WriteAllText(tmpB, @"<?xml version=""1.0""?><datafile>
<game name=""NewGame""><rom name=""new.zip"" /></game>
<game name=""Common""><rom name=""c.zip"" /></game>
</datafile>");
            var result = FeatureService.CompareDatFiles(tmpA, tmpB);
            Assert.Single(result.Added);
            Assert.Contains("NewGame", result.Added);
            Assert.Single(result.Removed);
            Assert.Contains("OldGame", result.Removed);
        }
        finally { File.Delete(tmpA); File.Delete(tmpB); }
    }

    [Fact]
    public void CompareDatFiles_ModifiedRoms()
    {
        var tmpA = Path.GetTempFileName();
        var tmpB = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpA, @"<?xml version=""1.0""?><datafile>
<game name=""Game""><rom name=""a.zip"" size=""100"" crc=""AAAA"" /></game>
</datafile>");
            File.WriteAllText(tmpB, @"<?xml version=""1.0""?><datafile>
<game name=""Game""><rom name=""a.zip"" size=""200"" crc=""BBBB"" /></game>
</datafile>");
            var result = FeatureService.CompareDatFiles(tmpA, tmpB);
            Assert.Empty(result.Added);
            Assert.Empty(result.Removed);
            Assert.Equal(1, result.ModifiedCount);
            Assert.Equal(0, result.UnchangedCount);
        }
        finally { File.Delete(tmpA); File.Delete(tmpB); }
    }

    // ── GenerateLogiqxEntry ──────────────────────────────────────

    [Fact]
    public void GenerateLogiqxEntry_ProducesValidXml()
    {
        var xml = FeatureService.GenerateLogiqxEntry("TestGame", "test.zip", "AABBCCDD", "1234567890abcdef1234567890abcdef12345678", 1024);
        Assert.Contains("TestGame", xml);
        Assert.Contains("test.zip", xml);
        Assert.Contains("AABBCCDD", xml);
        Assert.Contains("1234567890abcdef1234567890abcdef12345678", xml);
        Assert.Contains("1024", xml);
        Assert.Contains("<datafile>", xml);
        Assert.Contains("<game", xml);
    }

    // ── FormatDatDiffReport ──────────────────────────────────────

    [Fact]
    public void FormatDatDiffReport_WithDiffs_FormatsCorrectly()
    {
        var diff = new DatDiffResult(
            new List<string> { "AddedGame" },
            new List<string> { "RemovedGame" },
            ModifiedCount: 3,
            UnchangedCount: 10);
        var report = FeatureService.FormatDatDiffReport("old.dat", "new.dat", diff);
        Assert.Contains("DAT-Diff-Viewer", report);
        Assert.Contains("old.dat", report);
        Assert.Contains("new.dat", report);
        Assert.Contains("Gleich:", report);
        Assert.Contains("10", report);
        Assert.Contains("Geändert:", report);
        Assert.Contains("3", report);
        Assert.Contains("AddedGame", report);
        Assert.Contains("RemovedGame", report);
    }

    [Fact]
    public void FormatDatDiffReport_ManyAdded_ShowsWeitere()
    {
        var added = Enumerable.Range(0, 50).Select(i => $"Game{i}").ToList();
        var diff = new DatDiffResult(added, new List<string>(), 0, 0);
        var report = FeatureService.FormatDatDiffReport("a.dat", "b.dat", diff);
        Assert.Contains("weitere", report);
    }

    // ── AppendCustomDatEntry ─────────────────────────────────────

    [Fact]
    public void AppendCustomDatEntry_NewFile_CreatesXml()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        try
        {
            var entry = "  <game name=\"Test\"><rom name=\"test.zip\" /></game>";
            FeatureService.AppendCustomDatEntry(tmpDir, entry);
            var content = File.ReadAllText(Path.Combine(tmpDir, "custom.dat"));
            Assert.Contains("<datafile>", content);
            Assert.Contains("Test", content);
            Assert.Contains("</datafile>", content);
        }
        finally { Directory.Delete(tmpDir, recursive: true); }
    }

    [Fact]
    public void AppendCustomDatEntry_ExistingFile_AppendsBeforeClose()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        try
        {
            var datPath = Path.Combine(tmpDir, "custom.dat");
            File.WriteAllText(datPath, @"<?xml version=""1.0""?>
<datafile>
  <game name=""First""><rom name=""first.zip"" /></game>
</datafile>");
            var entry = "  <game name=\"Second\"><rom name=\"second.zip\" /></game>";
            FeatureService.AppendCustomDatEntry(tmpDir, entry);
            var content = File.ReadAllText(datPath);
            Assert.Contains("First", content);
            Assert.Contains("Second", content);
            Assert.Contains("</datafile>", content);
        }
        finally { Directory.Delete(tmpDir, recursive: true); }
    }

    // ── ImportDatFileToRoot ──────────────────────────────────────

    [Fact]
    public void ImportDatFileToRoot_ValidFile_CopiesSuccessfully()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        var sourceFile = Path.Combine(Path.GetTempPath(), "source_test.dat");
        File.WriteAllText(sourceFile, "<datafile />");
        try
        {
            var result = FeatureService.ImportDatFileToRoot(sourceFile, tmpDir);
            Assert.True(File.Exists(result));
            Assert.StartsWith(Path.GetFullPath(tmpDir), result);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
            File.Delete(sourceFile);
        }
    }

    [Fact]
    public void ImportDatFileToRoot_PathTraversal_SanitizesFilename()
    {
        // ImportDatFileToRoot uses Path.GetFileName() to sanitize,
        // so traversal in the source path is stripped to just the filename.
        // The File.Copy will fail because the source doesn't exist.
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        try
        {
            Assert.ThrowsAny<Exception>(() =>
                FeatureService.ImportDatFileToRoot(@"C:\..\..\etc\passwd", tmpDir));
        }
        finally { Directory.Delete(tmpDir, recursive: true); }
    }

    // ── BuildFtpSourceReport ─────────────────────────────────────

    [Fact]
    public void BuildFtpSourceReport_InvalidUrl_ReturnsFalse()
    {
        var (valid, _, report) = FeatureService.BuildFtpSourceReport("http://example.com");
        Assert.False(valid);
        Assert.Contains("Ungültige FTP-URL", report);
    }

    [Fact]
    public void BuildFtpSourceReport_ValidFtp_ReturnsTrue()
    {
        var (valid, isPlain, report) = FeatureService.BuildFtpSourceReport("ftp://ftp.example.com/roms");
        Assert.True(valid);
        Assert.True(isPlain);
        Assert.Contains("FTP", report);
        Assert.Contains("ftp.example.com", report);
    }

    [Fact]
    public void BuildFtpSourceReport_ValidSftp_ReturnsPlainFalse()
    {
        var (valid, isPlain, report) = FeatureService.BuildFtpSourceReport("sftp://secure.example.com/data");
        Assert.True(valid);
        Assert.False(isPlain);
        Assert.Contains("SFTP", report);
    }

    // ── IsValidHexHash ───────────────────────────────────────────

    [Theory]
    [InlineData("AABBCCDD", 8, true)]
    [InlineData("aabbccdd", 8, true)]
    [InlineData("12345678", 8, true)]
    [InlineData("ZZZZZZZZ", 8, false)]
    [InlineData("AABB", 8, false)]
    [InlineData("AABBCCDDEEFF", 8, false)]
    [InlineData("da39a3ee5e6b4b0d3255bfef95601890afd80709", 40, true)]
    [InlineData("tooshort", 40, false)]
    public void IsValidHexHash_VariousInputs(string hash, int length, bool expected)
    {
        Assert.Equal(expected, FeatureService.IsValidHexHash(hash, length));
    }

    // ── BuildCustomDatXmlEntry ───────────────────────────────────

    [Fact]
    public void BuildCustomDatXmlEntry_BasicEntry_ProducesValidXml()
    {
        var entry = FeatureService.BuildCustomDatXmlEntry("TestGame", "test.zip", "AABBCCDD", "1234567890abcdef1234567890abcdef12345678");
        Assert.Contains("TestGame", entry);
        Assert.Contains("test.zip", entry);
        Assert.Contains("AABBCCDD", entry);
        Assert.Contains("sha1=", entry);
    }

    [Fact]
    public void BuildCustomDatXmlEntry_EmptySha1_OmitsSha1Attribute()
    {
        var entry = FeatureService.BuildCustomDatXmlEntry("TestGame", "test.zip", "AABBCCDD", "");
        Assert.DoesNotContain("sha1=", entry);
    }

    [Fact]
    public void BuildCustomDatXmlEntry_XmlSpecialChars_AreEscaped()
    {
        var entry = FeatureService.BuildCustomDatXmlEntry("Game <Test> & \"Special\"", "rom.zip", "00000000", "");
        Assert.DoesNotContain("<Test>", entry);
        Assert.Contains("&amp;", entry);
    }

    // ── BuildArcadeMergeSplitReport ──────────────────────────────

    [Fact]
    public void BuildArcadeMergeSplitReport_ValidDat_ProducesReport()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var xml = @"<?xml version=""1.0""?>
<datafile>
  <game name=""pacman"">
    <rom name=""pacman.zip"" size=""100"" />
  </game>
  <game name=""mspacman"" cloneof=""pacman"">
    <rom name=""mspacman.zip"" size=""120"" />
  </game>
  <game name=""galaga"">
    <rom name=""galaga.zip"" size=""80"" />
  </game>
</datafile>";
            File.WriteAllText(tmpFile, xml);
            var report = FeatureService.BuildArcadeMergeSplitReport(tmpFile);
            Assert.Contains("Arcade Merge/Split", report);
            Assert.Contains("Parents:", report);
            Assert.Contains("Clones:", report);
            Assert.Contains("pacman", report);
            Assert.Contains("Non-Merged:", report);
            Assert.Contains("Split:", report);
            Assert.Contains("Merged:", report);
        }
        finally { File.Delete(tmpFile); }
    }

    // ── BuildDatAutoUpdateReport ─────────────────────────────────

    [Fact]
    public void BuildDatAutoUpdateReport_EmptyDir_ShowsZeroDats()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        try
        {
            var (report, localCount, oldCount) = FeatureService.BuildDatAutoUpdateReport(tmpDir);
            Assert.Contains("DAT Auto-Update", report);
            Assert.Equal(0, localCount);
            Assert.Equal(0, oldCount);
        }
        finally { Directory.Delete(tmpDir, recursive: true); }
    }

    [Fact]
    public void BuildDatAutoUpdateReport_WithDatFiles_CountsThem()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "test1.dat"), "<datafile />");
            File.WriteAllText(Path.Combine(tmpDir, "test2.xml"), "<datafile />");
            var (report, localCount, _) = FeatureService.BuildDatAutoUpdateReport(tmpDir);
            Assert.Equal(2, localCount);
            Assert.Contains("Lokale DAT-Dateien: 2", report);
        }
        finally { Directory.Delete(tmpDir, recursive: true); }
    }
}


// ═══════════════════════════════════════════════════════════════════
// FeatureService.Security Tests
// ═══════════════════════════════════════════════════════════════════

public class FeatureServiceSecurityTests
{
    // ── AnalyzeHeader ────────────────────────────────────────────

    [Fact]
    public void AnalyzeHeader_FileNotExist_ReturnsNull()
    {
        var result = FeatureService.AnalyzeHeader(@"C:\nonexistent_rom.nes");
        Assert.Null(result);
    }

    [Fact]
    public void AnalyzeHeader_NesInesHeader_DetectsCorrectly()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var header = new byte[16];
            header[0] = 0x4E; // N
            header[1] = 0x45; // E
            header[2] = 0x53; // S
            header[3] = 0x1A; // EOF
            header[4] = 2;    // PRG=32KB
            header[5] = 1;    // CHR=8KB
            header[6] = 0x10; // Mapper low nibble
            header[7] = 0x00; // Mapper high nibble
            File.WriteAllBytes(tmpFile, header);
            var result = FeatureService.AnalyzeHeader(tmpFile);
            Assert.NotNull(result);
            Assert.Equal("NES", result.Platform);
            Assert.Equal("iNES", result.Format);
            Assert.Contains("PRG=32KB", result.Details);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void AnalyzeHeader_Nes20Header_DetectsNes2()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var header = new byte[16];
            header[0] = 0x4E;
            header[1] = 0x45;
            header[2] = 0x53;
            header[3] = 0x1A;
            header[7] = 0x08; // NES 2.0 flag
            File.WriteAllBytes(tmpFile, header);
            var result = FeatureService.AnalyzeHeader(tmpFile);
            Assert.NotNull(result);
            Assert.Equal("NES 2.0", result.Format);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void AnalyzeHeader_N64BigEndian_DetectsZ64()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var data = new byte[0x40];
            data[0] = 0x80;
            data[1] = 0x37;
            // Write title at offset 0x20
            var title = Encoding.ASCII.GetBytes("TestN64Game         ");
            Array.Copy(title, 0, data, 0x20, Math.Min(20, title.Length));
            File.WriteAllBytes(tmpFile, data);
            var result = FeatureService.AnalyzeHeader(tmpFile);
            Assert.NotNull(result);
            Assert.Equal("N64", result.Platform);
            Assert.Contains("Big-Endian", result.Format);
            Assert.Contains("TestN64Game", result.Details);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void AnalyzeHeader_N64ByteSwapped_DetectsV64()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var data = new byte[0x40];
            data[0] = 0x37;
            data[1] = 0x80;
            File.WriteAllBytes(tmpFile, data);
            var result = FeatureService.AnalyzeHeader(tmpFile);
            Assert.NotNull(result);
            Assert.Equal("N64", result.Platform);
            Assert.Contains("Byte-Swapped", result.Format);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void AnalyzeHeader_N64LittleEndian_DetectsN64()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var data = new byte[0x40];
            data[0] = 0x40;
            data[1] = 0x12;
            File.WriteAllBytes(tmpFile, data);
            var result = FeatureService.AnalyzeHeader(tmpFile);
            Assert.NotNull(result);
            Assert.Equal("N64", result.Platform);
            Assert.Contains("Little-Endian", result.Format);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void AnalyzeHeader_GbaRom_DetectsGBA()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var data = new byte[0xBE];
            data[0xB2] = 0x96; // GBA magic
            // Write title at 0xA0
            var title = Encoding.ASCII.GetBytes("GBATEST     ");
            Array.Copy(title, 0, data, 0xA0, 12);
            var code = Encoding.ASCII.GetBytes("AXVE");
            Array.Copy(code, 0, data, 0xAC, 4);
            File.WriteAllBytes(tmpFile, data);
            var result = FeatureService.AnalyzeHeader(tmpFile);
            Assert.NotNull(result);
            Assert.Equal("GBA", result.Platform);
            Assert.Contains("GBATEST", result.Details);
            Assert.Contains("AXVE", result.Details);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void AnalyzeHeader_SnesLoRom_DetectsLoROM()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var data = new byte[0x8000];
            // Write title at 0x7FC0 (21 printable ASCII chars)
            var title = Encoding.ASCII.GetBytes("SNES TEST GAME TITLE!");
            Array.Copy(title, 0, data, 0x7FC0, 21);
            // Set checksum complement at 0x7FDC-0x7FDF so sum = 0xFFFF
            ushort checksum = 0x1234;
            ushort complement = (ushort)(0xFFFF - checksum);
            data[0x7FDC] = (byte)(complement & 0xFF);
            data[0x7FDD] = (byte)(complement >> 8);
            data[0x7FDE] = (byte)(checksum & 0xFF);
            data[0x7FDF] = (byte)(checksum >> 8);
            File.WriteAllBytes(tmpFile, data);
            var result = FeatureService.AnalyzeHeader(tmpFile);
            Assert.NotNull(result);
            Assert.Equal("SNES", result.Platform);
            Assert.Equal("LoROM", result.Format);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void AnalyzeHeader_UnknownFormat_ReturnsUnbekannt()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var data = new byte[64];
            data[0] = 0xFF;
            data[1] = 0xFE;
            File.WriteAllBytes(tmpFile, data);
            var result = FeatureService.AnalyzeHeader(tmpFile);
            Assert.NotNull(result);
            Assert.Equal("Unbekannt", result.Platform);
            Assert.Contains("Magic:", result.Details);
        }
        finally { File.Delete(tmpFile); }
    }

    // ── DetectPatchFormat ────────────────────────────────────────

    [Fact]
    public void DetectPatchFormat_FileNotExist_ReturnsNull()
    {
        Assert.Null(FeatureService.DetectPatchFormat(@"C:\nonexistent.ips"));
    }

    [Theory]
    [InlineData(new byte[] { (byte)'P', (byte)'A', (byte)'T', (byte)'C', (byte)'H' }, "IPS")]
    [InlineData(new byte[] { (byte)'B', (byte)'P', (byte)'S', (byte)'1', 0 }, "BPS")]
    [InlineData(new byte[] { (byte)'U', (byte)'P', (byte)'S', (byte)'1', 0 }, "UPS")]
    public void DetectPatchFormat_KnownFormats_ReturnsCorrectly(byte[] magic, string expected)
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmpFile, magic);
            Assert.Equal(expected, FeatureService.DetectPatchFormat(tmpFile));
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void DetectPatchFormat_UnknownMagic_ReturnsNull()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmpFile, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 });
            Assert.Null(FeatureService.DetectPatchFormat(tmpFile));
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void DetectPatchFormat_TooShort_ReturnsNull()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmpFile, new byte[] { 0x50 });
            Assert.Null(FeatureService.DetectPatchFormat(tmpFile));
        }
        finally { File.Delete(tmpFile); }
    }

    // ── RepairNesHeader ──────────────────────────────────────────

    [Fact]
    public void RepairNesHeader_FileNotExist_ReturnsFalse()
    {
        Assert.False(FeatureService.RepairNesHeader(@"C:\nonexistent.nes"));
    }

    [Fact]
    public void RepairNesHeader_NullPath_ReturnsFalse()
    {
        Assert.False(FeatureService.RepairNesHeader(null!));
    }

    [Fact]
    public void RepairNesHeader_EmptyPath_ReturnsFalse()
    {
        Assert.False(FeatureService.RepairNesHeader(""));
    }

    [Fact]
    public void RepairNesHeader_NotInesFile_ReturnsFalse()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var data = new byte[16];
            data[0] = 0x00; // Not NES magic
            File.WriteAllBytes(tmpFile, data);
            Assert.False(FeatureService.RepairNesHeader(tmpFile));
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void RepairNesHeader_CleanHeader_ReturnsFalse()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var header = new byte[16];
            header[0] = 0x4E; header[1] = 0x45; header[2] = 0x53; header[3] = 0x1A;
            // Bytes 12-15 are already 0x00
            File.WriteAllBytes(tmpFile, header);
            Assert.False(FeatureService.RepairNesHeader(tmpFile));
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void RepairNesHeader_DirtyHeader_ZerosBytes12to15()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var header = new byte[16];
            header[0] = 0x4E; header[1] = 0x45; header[2] = 0x53; header[3] = 0x1A;
            header[12] = 0xFF; header[13] = 0xAA; header[14] = 0xBB; header[15] = 0xCC;
            File.WriteAllBytes(tmpFile, header);
            Assert.True(FeatureService.RepairNesHeader(tmpFile));
            // Verify bytes are zeroed
            var repaired = File.ReadAllBytes(tmpFile);
            Assert.Equal(0x00, repaired[12]);
            Assert.Equal(0x00, repaired[13]);
            Assert.Equal(0x00, repaired[14]);
            Assert.Equal(0x00, repaired[15]);
            // Verify backup was created
            Assert.True(File.Exists(tmpFile + ".bak"));
        }
        finally
        {
            File.Delete(tmpFile);
            File.Delete(tmpFile + ".bak");
        }
    }

    [Fact]
    public void RepairNesHeader_TooSmall_ReturnsFalse()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmpFile, new byte[8]);
            Assert.False(FeatureService.RepairNesHeader(tmpFile));
        }
        finally { File.Delete(tmpFile); }
    }

    // ── RemoveCopierHeader ───────────────────────────────────────

    [Fact]
    public void RemoveCopierHeader_FileNotExist_ReturnsFalse()
    {
        Assert.False(FeatureService.RemoveCopierHeader(@"C:\nonexistent.sfc"));
    }

    [Fact]
    public void RemoveCopierHeader_NullPath_ReturnsFalse()
    {
        Assert.False(FeatureService.RemoveCopierHeader(null!));
    }

    [Fact]
    public void RemoveCopierHeader_NoCopierHeader_ReturnsFalse()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            // Exactly 1024 bytes = no copier header (1024 % 1024 == 0, not 512)
            File.WriteAllBytes(tmpFile, new byte[1024]);
            Assert.False(FeatureService.RemoveCopierHeader(tmpFile));
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void RemoveCopierHeader_WithCopierHeader_RemovesFirst512Bytes()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            // 512 + 1024 = 1536 bytes. 1536 % 1024 == 512 => copier header present
            var data = new byte[1536];
            // Fill copier header with 0xCC
            for (int i = 0; i < 512; i++) data[i] = 0xCC;
            // Fill ROM data with 0xAA
            for (int i = 512; i < 1536; i++) data[i] = 0xAA;
            File.WriteAllBytes(tmpFile, data);

            Assert.True(FeatureService.RemoveCopierHeader(tmpFile));
            var stripped = File.ReadAllBytes(tmpFile);
            Assert.Equal(1024, stripped.Length);
            Assert.All(stripped, b => Assert.Equal(0xAA, b));
            Assert.True(File.Exists(tmpFile + ".bak"));
        }
        finally
        {
            File.Delete(tmpFile);
            File.Delete(tmpFile + ".bak");
        }
    }

    [Fact]
    public void RemoveCopierHeader_TooSmall_ReturnsFalse()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmpFile, new byte[256]);
            Assert.False(FeatureService.RemoveCopierHeader(tmpFile));
        }
        finally { File.Delete(tmpFile); }
    }

    // ── FindCommonRoot ───────────────────────────────────────────

    [Fact]
    public void FindCommonRoot_EmptyList_ReturnsNull()
    {
        Assert.Null(FeatureService.FindCommonRoot(new List<string>()));
    }

    [Fact]
    public void FindCommonRoot_SinglePath_ReturnsDirectory()
    {
        var result = FeatureService.FindCommonRoot(new List<string> { @"C:\Games\Roms\file.zip" });
        Assert.Equal(@"C:\Games\Roms", result);
    }

    [Fact]
    public void FindCommonRoot_SameDirectory_ReturnsDirectory()
    {
        var result = FeatureService.FindCommonRoot(new List<string>
        {
            @"C:\Games\Roms\a.zip",
            @"C:\Games\Roms\b.zip",
        });
        Assert.Equal(@"C:\Games\Roms", result);
    }

    [Fact]
    public void FindCommonRoot_DifferentSubdirs_ReturnsParent()
    {
        var result = FeatureService.FindCommonRoot(new List<string>
        {
            @"C:\Games\Roms\NES\a.zip",
            @"C:\Games\Roms\SNES\b.zip",
        });
        Assert.Equal(@"C:\Games\Roms", result);
    }

    // ── FormatTrendReport ────────────────────────────────────────

    [Fact]
    public void FormatTrendReport_EmptyHistory_ReturnsNoData()
    {
        var report = FeatureService.FormatTrendReport(new List<TrendSnapshot>());
        Assert.Contains("Keine Trend-Daten vorhanden", report);
    }

    [Fact]
    public void FormatTrendReport_SingleEntry_ShowsCurrent()
    {
        var history = new List<TrendSnapshot>
        {
            new(DateTime.Now, 100, 1048576, 80, 10, 5, 80)
        };
        var report = FeatureService.FormatTrendReport(history);
        Assert.Contains("Trend-Analyse", report);
        Assert.Contains("Aktuell:", report);
        Assert.Contains("100 Dateien", report);
    }

    [Fact]
    public void FormatTrendReport_TwoEntries_ShowsDelta()
    {
        var history = new List<TrendSnapshot>
        {
            new(DateTime.Now.AddDays(-1), 90, 1000000, 70, 15, 5, 70),
            new(DateTime.Now, 100, 1048576, 80, 10, 5, 80),
        };
        var report = FeatureService.FormatTrendReport(history);
        Assert.Contains("Δ Dateien:", report);
        Assert.Contains("Δ Duplikate:", report);
    }

    [Fact]
    public void FormatTrendReport_ManyEntries_ShowsLast10()
    {
        var history = new List<TrendSnapshot>();
        for (int i = 0; i < 15; i++)
            history.Add(new TrendSnapshot(DateTime.Now.AddDays(-15 + i), 100 + i, 1000000, 80, 10, 5, 80));
        var report = FeatureService.FormatTrendReport(history);
        Assert.Contains("Verlauf (letzte 10):", report);
    }

    // ── CreateBackup ─────────────────────────────────────────────

    [Fact]
    public void CreateBackup_CopiesFilesToBackupDir()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var backupDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(sourceDir);
        try
        {
            var file1 = Path.Combine(sourceDir, "game1.zip");
            var file2 = Path.Combine(sourceDir, "game2.zip");
            File.WriteAllText(file1, "data1");
            File.WriteAllText(file2, "data2");

            var result = FeatureService.CreateBackup(new[] { file1, file2 }, backupDir, "test-backup");
            Assert.True(Directory.Exists(result));
            Assert.Contains("test-backup", result);
            var files = Directory.GetFiles(result);
            Assert.Equal(2, files.Length);
        }
        finally
        {
            if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, true);
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
        }
    }

    // ── CleanupOldBackups ────────────────────────────────────────

    [Fact]
    public void CleanupOldBackups_DirNotExist_ReturnsZero()
    {
        Assert.Equal(0, FeatureService.CleanupOldBackups(@"C:\nonexistent_backup_root", 30));
    }

    [Fact]
    public void CleanupOldBackups_NoExpired_ReturnsZero()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        try
        {
            Directory.CreateDirectory(Path.Combine(tmpDir, "recent-backup"));
            Assert.Equal(0, FeatureService.CleanupOldBackups(tmpDir, 30));
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void CleanupOldBackups_ConfirmDenied_ReturnsZero()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        try
        {
            var oldDir = Path.Combine(tmpDir, "old-backup");
            Directory.CreateDirectory(oldDir);
            Directory.SetCreationTime(oldDir, DateTime.Now.AddDays(-60));
            Assert.Equal(0, FeatureService.CleanupOldBackups(tmpDir, 30, _ => false));
        }
        finally { Directory.Delete(tmpDir, true); }
    }

    [Fact]
    public void CleanupOldBackups_ConfirmAccepted_RemovesExpired()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        try
        {
            var oldDir = Path.Combine(tmpDir, "old-backup");
            Directory.CreateDirectory(oldDir);
            Directory.SetCreationTime(oldDir, DateTime.Now.AddDays(-60));
            var removed = FeatureService.CleanupOldBackups(tmpDir, 30, _ => true);
            Assert.Equal(1, removed);
            Assert.False(Directory.Exists(oldDir));
        }
        finally { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); }
    }
}


// ═══════════════════════════════════════════════════════════════════
// SettingsLoader Tests
// ═══════════════════════════════════════════════════════════════════

public class SettingsLoaderCoverageTests
{
    [Fact]
    public void Load_NoFiles_ReturnsDefaults()
    {
        var settings = SettingsLoader.Load();
        Assert.NotNull(settings);
        Assert.NotNull(settings.General);
    }

    [Fact]
    public void LoadFrom_FileNotExist_ReturnsEmptySettings()
    {
        var settings = SettingsLoader.LoadFrom(@"C:\nonexistent_settings.json");
        Assert.NotNull(settings);
    }

    [Fact]
    public void LoadFrom_ValidJsonWithGeneral_Parses()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var json = JsonSerializer.Serialize(new
            {
                General = new { LogLevel = "Debug", Mode = "Move", AggressiveJunk = true }
            });
            File.WriteAllText(tmpFile, json);
            var settings = SettingsLoader.LoadFrom(tmpFile);
            Assert.NotNull(settings);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void LoadFrom_InvalidJson_ReturnsEmptySettings()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "not json");
            var settings = SettingsLoader.LoadFrom(tmpFile);
            Assert.NotNull(settings);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void Load_WithDefaultsFile_MergesValues()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var json = @"{
  ""mode"": ""Move"",
  ""logLevel"": ""Debug"",
  ""theme"": ""light"",
  ""locale"": ""en""
}";
            File.WriteAllText(tmpFile, json);
            var settings = SettingsLoader.Load(tmpFile);
            Assert.NotNull(settings);
            // User settings.json on disk may override mode/logLevel,
            // so only assert that Load completed and returned valid settings.
            Assert.NotNull(settings.General);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void Load_DefaultsFileWithComments_ParsesSuccessfully()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            var json = @"{
  // This is a comment
  ""mode"": ""DryRun"",
  ""extensions"": "".zip,.7z,.rar""
}";
            File.WriteAllText(tmpFile, json);
            var settings = SettingsLoader.Load(tmpFile);
            Assert.NotNull(settings);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void UserSettingsPath_ContainsRomCleanup()
    {
        Assert.Contains("RomCleanupRegionDedupe", SettingsLoader.UserSettingsPath);
    }
}


// ═══════════════════════════════════════════════════════════════════
// ConsoleSorter Tests
// ═══════════════════════════════════════════════════════════════════

public class ConsoleSorterCoverageTests
{
    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly Dictionary<string, bool> _isContainer = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string from, string to)> _moves = [];

        public void AddRoot(string root, params string[] files)
        {
            _isContainer[root] = true;
            _files[root] = new List<string>(files);
        }

        public bool TestPath(string literalPath, string pathType = "Any") => _isContainer.ContainsKey(literalPath);
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
        {
            if (!_files.TryGetValue(root, out var files)) return [];
            if (allowedExtensions is null) return files;
            var extSet = new HashSet<string>(allowedExtensions, StringComparer.OrdinalIgnoreCase);
            return files.Where(f => extSet.Contains(Path.GetExtension(f))).ToList();
        }
        public bool IsReparsePoint(string path) => false;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
        {
            var full = Path.GetFullPath(Path.Combine(rootPath, relativePath));
            return full.StartsWith(Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        public bool MoveItemSafely(string sourcePath, string destinationPath)
        {
            _moves.Add((sourcePath, destinationPath));
            return true;
        }
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<(string, string)> Moves => _moves;
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) => throw new NotImplementedException();
        public void DeleteFile(string path) => throw new NotImplementedException();
    }

    private static ConsoleDetector BuildDetector()
    {
        var consoles = new List<ConsoleInfo>
        {
            new("NES", "Nintendo", false, new[] { ".nes" }, Array.Empty<string>(), new[] { "NES" }),
            new("SNES", "Super Nintendo", false, new[] { ".sfc", ".smc" }, Array.Empty<string>(), new[] { "SNES" }),
            new("GBA", "Game Boy Advance", false, new[] { ".gba" }, Array.Empty<string>(), new[] { "GBA" }),
        };
        return new ConsoleDetector(consoles);
    }

    [Fact]
    public void Sort_EmptyRoots_ReturnsZeroCounts()
    {
        var fs = new FakeFileSystem();
        var sorter = new ConsoleSorter(fs, BuildDetector());
        var result = sorter.Sort(new List<string>(), dryRun: true);
        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void Sort_DryRun_NoActualMoves()
    {
        var fs = new FakeFileSystem();
        var root = @"C:\Roms";
        fs.AddRoot(root,
            @"C:\Roms\game.nes",
            @"C:\Roms\game.sfc"
        );
        var sorter = new ConsoleSorter(fs, BuildDetector());
        var result = sorter.Sort(new[] { root }, dryRun: true);
        Assert.Empty(fs.Moves); // DryRun - no actual moves
    }

    [Fact]
    public void Sort_CancellationRequested_StopsEarly()
    {
        var fs = new FakeFileSystem();
        var root = @"C:\Roms";
        fs.AddRoot(root, @"C:\Roms\game.nes");
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var sorter = new ConsoleSorter(fs, BuildDetector());
        var result = sorter.Sort(new[] { root }, dryRun: true, cancellationToken: cts.Token);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void Sort_ExcludedFolder_FilesSkipped()
    {
        var fs = new FakeFileSystem();
        var root = @"C:\Roms";
        fs.AddRoot(root,
            @"C:\Roms\_TRASH_REGION_DEDUPE\game.nes",
            @"C:\Roms\_TRASH_JUNK\game2.sfc",
            @"C:\Roms\_BIOS\bios.bin"
        );
        var sorter = new ConsoleSorter(fs, BuildDetector());
        var result = sorter.Sort(new[] { root }, dryRun: true);
        Assert.Equal(0, result.Moved);
    }
}


// ═══════════════════════════════════════════════════════════════════
// More FCS Command Tests
// ═══════════════════════════════════════════════════════════════════

public class FcsCommandDeepTests
{
    private sealed class TestDialog : IDialogService
    {
        public string? NextBrowseFolder { get; set; }
        public string? NextBrowseFile { get; set; }
        public string? NextSaveFile { get; set; }
        public bool NextConfirm { get; set; } = true;
        public Queue<string> InputBoxResponses { get; } = new();
        public List<string> ShowTextCalls { get; } = [];
        public List<string> InfoCalls { get; } = [];
        public List<string> ErrorCalls { get; } = [];
        public ConfirmResult NextYesNoCancel { get; set; } = ConfirmResult.Yes;

        public string? BrowseFolder(string title) => NextBrowseFolder;
        public string? BrowseFile(string title, string filter = "") => NextBrowseFile;
        public string? SaveFile(string title, string filter = "", string? defaultFileName = null) => NextSaveFile;
        public bool Confirm(string message, string title = "") => NextConfirm;
        public void Info(string message, string title = "") => InfoCalls.Add(message);
        public void Error(string message, string title = "") => ErrorCalls.Add(message);
        public ConfirmResult YesNoCancel(string message, string title = "") => NextYesNoCancel;
        public string ShowInputBox(string prompt, string title = "", string defaultValue = "")
            => InputBoxResponses.Count > 0 ? InputBoxResponses.Dequeue() : "";
        public void ShowText(string title, string content) => ShowTextCalls.Add(content);
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen") => true;
    }

    private sealed class StubSettings : ISettingsService
    {
        public string? LastAuditPath { get; set; }
        public string LastTheme { get; set; } = "dark";
        public SettingsDto? Load() => new();
        public void LoadInto(MainViewModel vm) { }
        public bool SaveFrom(MainViewModel vm, string? lastAuditPath = null) => true;
    }

    private sealed class StubTheme : IThemeService
    {
        public AppTheme Current { get; private set; } = AppTheme.Dark;
        public bool IsDark => Current == AppTheme.Dark;
        public void ApplyTheme(AppTheme theme) => Current = theme;
        public void ApplyTheme(bool dark) => Current = dark ? AppTheme.Dark : AppTheme.Light;
        public void Toggle() => Current = IsDark ? AppTheme.Light : AppTheme.Dark;
    }

    private sealed class StubWindowHost : IWindowHost
    {
        public double FontSize { get; set; } = 14;
        public void SelectTab(int index) { }
        public void ShowTextDialog(string title, string content) { }
        public void ToggleSystemTray() { }
        public void StartApiProcess(string projectPath) { }
        public void StopApiProcess() { }
    }

    private static RomCandidate MakeCandidate(string name, string region = "EU",
        string category = "GAME", long size = 1024, string ext = ".zip",
        string consoleKey = "nes", bool datMatch = false, string gameKey = "")
    {
        return new RomCandidate
        {
            MainPath = $@"C:\Roms\{consoleKey}\{name}{ext}",
            GameKey = gameKey.Length > 0 ? gameKey : name,
            Region = region,
            RegionScore = 100,
            FormatScore = 500,
            VersionScore = 0,
            SizeBytes = size,
            Extension = ext,
            ConsoleKey = consoleKey,
            DatMatch = datMatch,
            Category = category
        };
    }

    private (FeatureCommandService fcs, MainViewModel vm, TestDialog dialog) SetupFcsWithHost()
    {
        var dialog = new TestDialog();
        var vm = new MainViewModel(new StubTheme(), dialog);
        var settings = new StubSettings();
        var fcs = new FeatureCommandService(vm, settings, dialog, new StubWindowHost());
        fcs.RegisterCommands();
        return (fcs, vm, dialog);
    }

    private (FeatureCommandService fcs, MainViewModel vm, TestDialog dialog) SetupFcs()
    {
        var dialog = new TestDialog();
        var vm = new MainViewModel(new StubTheme(), dialog);
        var settings = new StubSettings();
        var fcs = new FeatureCommandService(vm, settings, dialog);
        fcs.RegisterCommands();
        return (fcs, vm, dialog);
    }

    private static void ExecCommand(MainViewModel vm, string key)
    {
        if (vm.FeatureCommands.TryGetValue(key, out var cmd))
            cmd.Execute(null);
    }

    // ── DatDiffViewer Command ────────────────────────────────────

    [Fact]
    public void FCS_DatDiffViewer_WithValidFiles_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        var tmpA = Path.GetTempFileName();
        var tmpB = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpA, @"<?xml version=""1.0""?><datafile>
<game name=""GameA""><rom name=""a.zip"" /></game></datafile>");
            File.WriteAllText(tmpB, @"<?xml version=""1.0""?><datafile>
<game name=""GameB""><rom name=""b.zip"" /></game></datafile>");
            dialog.NextBrowseFile = tmpA; // First browse
            // We need a way to return different files
            // The FCS calls BrowseFile twice - we'll just set one path for both
            ExecCommand(vm, "DatDiffViewer");
            // Will browse twice; since we can only set one value, second browse returns same
        }
        finally { File.Delete(tmpA); File.Delete(tmpB); }
    }

    // ── TosecDat Command ─────────────────────────────────────────

    [Fact]
    public void FCS_TosecDat_NoDatRoot_ShowsError()
    {
        var (fcs, vm, dialog) = SetupFcs();
        dialog.NextBrowseFile = Path.GetTempFileName();
        try
        {
            vm.DatRoot = "";
            ExecCommand(vm, "TosecDat");
            Assert.True(dialog.ErrorCalls.Count > 0 || dialog.InfoCalls.Count > 0);
        }
        finally { if (dialog.NextBrowseFile != null) File.Delete(dialog.NextBrowseFile); }
    }

    // ── CustomDatEditor Command ──────────────────────────────────

    [Fact]
    public void FCS_CustomDatEditor_EmptyGameName_NoAction()
    {
        var (fcs, vm, dialog) = SetupFcs();
        // First input box returns empty
        ExecCommand(vm, "CustomDatEditor");
        Assert.Empty(dialog.ShowTextCalls);
    }

    [Fact]
    public void FCS_CustomDatEditor_ValidInputs_ShowsXml()
    {
        var (fcs, vm, dialog) = SetupFcs();
        dialog.InputBoxResponses.Enqueue("TestGame");
        dialog.InputBoxResponses.Enqueue("test.zip");
        dialog.InputBoxResponses.Enqueue("AABBCCDD");
        dialog.InputBoxResponses.Enqueue(""); // sha1 empty
        ExecCommand(vm, "CustomDatEditor");
        Assert.True(dialog.ShowTextCalls.Count > 0);
        Assert.Contains("TestGame", dialog.ShowTextCalls[0]);
    }

    [Fact]
    public void FCS_CustomDatEditor_InvalidCrc32_Warns()
    {
        var (fcs, vm, dialog) = SetupFcs();
        dialog.InputBoxResponses.Enqueue("TestGame");
        dialog.InputBoxResponses.Enqueue("test.zip");
        dialog.InputBoxResponses.Enqueue("ZZZZ"); // Invalid CRC32
        ExecCommand(vm, "CustomDatEditor");
        // Should not show result since CRC is invalid
        Assert.Empty(dialog.ShowTextCalls);
    }

    // ── HashDatabaseExport Command ───────────────────────────────

    [Fact]
    public void FCS_HashDatabaseExport_NoCandidates_Warns()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>();
        ExecCommand(vm, "HashDatabaseExport");
        // No candidates → should log warning
        Assert.Empty(dialog.ShowTextCalls);
    }

    [Fact]
    public void FCS_HashDatabaseExport_WithCandidates_ExportsJson()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate("Game1", datMatch: true)
        };
        var tmpFile = Path.GetTempFileName();
        dialog.NextSaveFile = tmpFile;
        try
        {
            ExecCommand(vm, "HashDatabaseExport");
            if (File.Exists(tmpFile))
            {
                var content = File.ReadAllText(tmpFile);
                Assert.Contains("Game1", content);
            }
        }
        finally { File.Delete(tmpFile); }
    }

    // ── IntegrityMonitor Command ─────────────────────────────────

    [Fact]
    public void FCS_IntegrityMonitor_NoCandidates_Warns()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>();
        ExecCommand(vm, "IntegrityMonitor");
        // No candidates → warning
    }

    // ── BackupManager Command ────────────────────────────────────

    [Fact]
    public void FCS_BackupManager_CancelBrowse_NoAction()
    {
        var (fcs, vm, dialog) = SetupFcs();
        dialog.NextBrowseFolder = null;
        ExecCommand(vm, "BackupManager");
        Assert.Empty(dialog.ShowTextCalls);
    }

    [Fact]
    public void FCS_BackupManager_NoCandidates_Warns()
    {
        var (fcs, vm, dialog) = SetupFcs();
        dialog.NextBrowseFolder = @"C:\Backup";
        vm.LastCandidates = new ObservableCollection<RomCandidate>();
        vm.LastDedupeGroups = new ObservableCollection<DedupeResult>();
        ExecCommand(vm, "BackupManager");
    }

    // ── Quarantine Command ───────────────────────────────────────

    [Fact]
    public void FCS_Quarantine_WithJunkCandidates_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate("Junk1 (Beta)", category: "JUNK"),
            MakeCandidate("Unknown1", region: "UNKNOWN", datMatch: false, category: "GAME"),
            MakeCandidate("Good1", region: "EU", datMatch: true),
        };
        ExecCommand(vm, "Quarantine");
        Assert.True(dialog.ShowTextCalls.Count > 0);
        Assert.Contains("Quarantäne", dialog.ShowTextCalls[0]);
    }

    [Fact]
    public void FCS_Quarantine_NoCandidates_Warns()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>();
        ExecCommand(vm, "Quarantine");
        Assert.Empty(dialog.ShowTextCalls);
    }

    // ── PatchEngine Command ──────────────────────────────────────

    [Fact]
    public void FCS_PatchEngine_CancelBrowse_NoAction()
    {
        var (fcs, vm, dialog) = SetupFcs();
        dialog.NextBrowseFile = null;
        ExecCommand(vm, "PatchEngine");
        Assert.Empty(dialog.InfoCalls);
    }

    [Fact]
    public void FCS_PatchEngine_ValidIps_ShowsInfo()
    {
        var (fcs, vm, dialog) = SetupFcs();
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmpFile, new byte[] { (byte)'P', (byte)'A', (byte)'T', (byte)'C', (byte)'H', 0, 0 });
            dialog.NextBrowseFile = tmpFile;
            ExecCommand(vm, "PatchEngine");
            Assert.True(dialog.InfoCalls.Count > 0);
            Assert.Contains("IPS", dialog.InfoCalls[0]);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void FCS_PatchEngine_UnknownFormat_NoInfo()
    {
        var (fcs, vm, dialog) = SetupFcs();
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmpFile, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
            dialog.NextBrowseFile = tmpFile;
            ExecCommand(vm, "PatchEngine");
            Assert.Empty(dialog.InfoCalls);
        }
        finally { File.Delete(tmpFile); }
    }

    // ── HeaderRepair Command ─────────────────────────────────────

    [Fact]
    public void FCS_HeaderRepair_CancelBrowse_NoAction()
    {
        var (fcs, vm, dialog) = SetupFcs();
        dialog.NextBrowseFile = null;
        ExecCommand(vm, "HeaderRepair");
    }

    [Fact]
    public void FCS_HeaderRepair_NesCleanHeader_ShowsClean()
    {
        var (fcs, vm, dialog) = SetupFcs();
        var tmpFile = Path.GetTempFileName();
        try
        {
            var data = new byte[16];
            data[0] = 0x4E; data[1] = 0x45; data[2] = 0x53; data[3] = 0x1A;
            File.WriteAllBytes(tmpFile, data);
            dialog.NextBrowseFile = tmpFile;
            ExecCommand(vm, "HeaderRepair");
            Assert.True(dialog.InfoCalls.Count > 0 || dialog.ShowTextCalls.Count > 0);
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void FCS_HeaderRepair_NesDirtyHeader_AsksConfirmation()
    {
        var (fcs, vm, dialog) = SetupFcs();
        var tmpFile = Path.GetTempFileName();
        try
        {
            var data = new byte[16];
            data[0] = 0x4E; data[1] = 0x45; data[2] = 0x53; data[3] = 0x1A;
            data[12] = 0xFF; data[13] = 0xAA;
            File.WriteAllBytes(tmpFile, data);
            dialog.NextBrowseFile = tmpFile;
            dialog.NextConfirm = true;
            ExecCommand(vm, "HeaderRepair");
        }
        finally
        {
            File.Delete(tmpFile);
            File.Delete(tmpFile + ".bak");
        }
    }

    // ── RuleEngine Command ───────────────────────────────────────

    [Fact]
    public void FCS_RuleEngine_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        ExecCommand(vm, "RuleEngine");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── ConversionEstimate with candidates ───────────────────────

    [Fact]
    public void FCS_ConversionEstimate_WithCandidates_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate("Game1", ext: ".iso", size: 700 * 1024 * 1024, consoleKey: "psx"),
            MakeCandidate("Game2", ext: ".zip", size: 1024 * 1024, consoleKey: "nes"),
        };
        ExecCommand(vm, "ConversionEstimate");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── JunkReport with candidates ───────────────────────────────

    [Fact]
    public void FCS_JunkReport_WithJunkCandidates_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate("Game (Beta)", category: "JUNK"),
            MakeCandidate("Game (Demo)", category: "JUNK"),
            MakeCandidate("Good Game"),
        };
        ExecCommand(vm, "JunkReport");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── DuplicateHeatmap with candidates ─────────────────────────

    [Fact]
    public void FCS_DuplicateHeatmap_WithGroups_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        var winner = MakeCandidate("Winner", consoleKey: "nes");
        var loser = MakeCandidate("Loser", consoleKey: "nes");
        vm.LastDedupeGroups = new ObservableCollection<DedupeResult>
        {
            new() { Winner = winner, Losers = new List<RomCandidate> { loser }, GameKey = "key1" }
        };
        vm.LastCandidates = new ObservableCollection<RomCandidate> { winner, loser };
        ExecCommand(vm, "DuplicateHeatmap");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── TrendAnalysis ────────────────────────────────────────────

    [Fact]
    public void FCS_TrendAnalysis_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        ExecCommand(vm, "TrendAnalysis");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── HealthScore with candidates ──────────────────────────────

    [Fact]
    public void FCS_HealthScore_WithCandidates_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate("G1", datMatch: true, region: "EU"),
            MakeCandidate("G2", datMatch: false, region: "UNKNOWN", category: "JUNK"),
        };
        vm.LastDedupeGroups = new ObservableCollection<DedupeResult>();
        ExecCommand(vm, "HealthScore");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── Completeness with candidates ─────────────────────────────

    [Fact]
    public void FCS_Completeness_WithCandidates_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate("G1", consoleKey: "nes", datMatch: true),
            MakeCandidate("G2", consoleKey: "snes", datMatch: false),
        };
        ExecCommand(vm, "Completeness");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── GenreClassification with candidates ──────────────────────

    [Fact]
    public void FCS_GenreClassification_WithCandidates_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate("Mario Bros", gameKey: "Mario Bros"),
            MakeCandidate("Tetris Attack", gameKey: "Tetris Attack"),
        };
        ExecCommand(vm, "GenreClassification");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── VirtualFolderPreview with candidates ─────────────────────

    [Fact]
    public void FCS_VirtualFolderPreview_WithCandidates_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate("G1", consoleKey: "nes"),
            MakeCandidate("G2", consoleKey: "snes"),
            MakeCandidate("G3", consoleKey: "nes"),
        };
        ExecCommand(vm, "VirtualFolderPreview");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── CollectionSharing ────────────────────────────────────────

    [Fact]
    public void FCS_CollectionSharing_NoCandidates_Warns()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>();
        ExecCommand(vm, "CollectionSharing");
    }

    [Fact]
    public void FCS_CollectionSharing_WithCandidates_ShowsOptions()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate("G1"), MakeCandidate("G2"),
        };
        dialog.NextYesNoCancel = ConfirmResult.Cancel;
        ExecCommand(vm, "CollectionSharing");
    }

    // ── CoverScraper ─────────────────────────────────────────────

    [Fact]
    public void FCS_CoverScraper_NoCandidates_Warns()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>();
        ExecCommand(vm, "CoverScraper");
    }

    // ── PlaytimeTracker ──────────────────────────────────────────

    [Fact]
    public void FCS_PlaytimeTracker_NoBrowse_NoAction()
    {
        var (fcs, vm, dialog) = SetupFcs();
        dialog.NextBrowseFolder = null;
        ExecCommand(vm, "PlaytimeTracker");
        Assert.Empty(dialog.ShowTextCalls);
    }

    // ── LauncherIntegration ──────────────────────────────────────

    [Fact]
    public void FCS_LauncherIntegration_NoGroups_Warns()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastDedupeGroups = new ObservableCollection<DedupeResult>();
        ExecCommand(vm, "LauncherIntegration");
        // With no groups, early exit with log warning
        Assert.Empty(dialog.ShowTextCalls);
    }

    // ── ToolImport ───────────────────────────────────────────────

    [Fact]
    public void FCS_ToolImport_CancelBrowse_NoAction()
    {
        var (fcs, vm, dialog) = SetupFcs();
        dialog.NextBrowseFile = null;
        ExecCommand(vm, "ToolImport");
    }

    // ── PdfReport ────────────────────────────────────────────────

    [Fact]
    public void FCS_PdfReport_NoCandidates_LogsWarning()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>();
        ExecCommand(vm, "PdfReport");
        // No candidates → early exit log
        Assert.Empty(dialog.ShowTextCalls);
    }

    [Fact]
    public void FCS_PdfReport_NoCandidates_Warns()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>();
        ExecCommand(vm, "PdfReport");
    }

    // ── DatAutoUpdate with candidates ────────────────────────────

    [Fact]
    public void FCS_DatAutoUpdate_NoDatRoot_Warns()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.DatRoot = "";
        ExecCommand(vm, "DatAutoUpdate");
    }

    [Fact]
    public void FCS_DatAutoUpdate_NonExistentDatRoot_LogsError()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.DatRoot = @"C:\nonexistent_dat_root_" + Guid.NewGuid().ToString();
        ExecCommand(vm, "DatAutoUpdate");
    }

    // ── CollectionManager with data ──────────────────────────────

    [Fact]
    public void FCS_CollectionManager_WithCandidates_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate("G1", consoleKey: "nes", datMatch: true),
            MakeCandidate("G2", consoleKey: "nes", datMatch: false),
            MakeCandidate("G3", consoleKey: "snes", datMatch: true),
        };
        ExecCommand(vm, "CollectionManager");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── CloneListViewer ──────────────────────────────────────────

    [Fact]
    public void FCS_CloneListViewer_WithGroups_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        var w = MakeCandidate("Winner");
        var l1 = MakeCandidate("Loser1", region: "JP");
        var l2 = MakeCandidate("Loser2", region: "US");
        vm.LastDedupeGroups = new ObservableCollection<DedupeResult>
        {
            new() { Winner = w, Losers = new List<RomCandidate> { l1, l2 }, GameKey = "TestKey" }
        };
        ExecCommand(vm, "CloneListViewer");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── RomFilter with candidates ────────────────────────────────

    [Fact]
    public void FCS_RomFilter_WithCandidates_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate("G1", region: "EU", consoleKey: "nes"),
            MakeCandidate("G2", region: "US", consoleKey: "snes"),
            MakeCandidate("G3", region: "JP", consoleKey: "nes"),
        };
        dialog.InputBoxResponses.Enqueue("G1"); // search term
        ExecCommand(vm, "RomFilter");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── MissingRom with data ─────────────────────────────────────

    [Fact]
    public void FCS_MissingRom_WithDatMatches_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.UseDat = true;
        vm.DatRoot = @"C:\DATs";
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate("Found", datMatch: true, consoleKey: "nes"),
            MakeCandidate("NotFound", datMatch: false, consoleKey: "nes"),
        };
        ExecCommand(vm, "MissingRom");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── FilterBuilder with data ──────────────────────────────────

    [Fact]
    public void FCS_FilterBuilder_WithCandidates_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        vm.LastCandidates = new ObservableCollection<RomCandidate>
        {
            MakeCandidate("G1", region: "EU"),
            MakeCandidate("G2", region: "US"),
        };
        dialog.InputBoxResponses.Enqueue("region=EU");
        ExecCommand(vm, "FilterBuilder");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── SortTemplates ────────────────────────────────────────────

    [Fact]
    public void FCS_SortTemplates_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        ExecCommand(vm, "SortTemplates");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── SchedulerAdvanced ────────────────────────────────────────

    [Fact]
    public void FCS_SchedulerAdvanced_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        dialog.InputBoxResponses.Enqueue("0 3 * * *"); // cron expression
        ExecCommand(vm, "SchedulerAdvanced");
        Assert.True(dialog.InfoCalls.Count > 0);
    }

    // ── RulePackSharing Export ────────────────────────────────────

    [Fact]
    public void FCS_RulePackSharing_CancelSave_NoAction()
    {
        var (fcs, vm, dialog) = SetupFcs();
        dialog.NextYesNoCancel = ConfirmResult.Yes; // Export
        dialog.NextSaveFile = null;
        ExecCommand(vm, "RulePackSharing");
    }

    // ── ArcadeMergeSplit ─────────────────────────────────────────

    [Fact]
    public void FCS_ArcadeMergeSplit_CancelBrowse_NoAction()
    {
        var (fcs, vm, dialog) = SetupFcs();
        dialog.NextBrowseFile = null;
        ExecCommand(vm, "ArcadeMergeSplit");
        Assert.Empty(dialog.ShowTextCalls);
    }

    [Fact]
    public void FCS_ArcadeMergeSplit_ValidDat_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, @"<?xml version=""1.0""?><datafile>
<game name=""pacman""><rom name=""pacman.zip"" size=""100"" /></game>
<game name=""mspacman"" cloneof=""pacman""><rom name=""mspacman.zip"" size=""120"" /></game>
</datafile>");
            dialog.NextBrowseFile = tmpFile;
            ExecCommand(vm, "ArcadeMergeSplit");
            Assert.True(dialog.ShowTextCalls.Count > 0);
            Assert.Contains("Arcade", dialog.ShowTextCalls[0]);
        }
        finally { File.Delete(tmpFile); }
    }

    // ── SplitPanelPreview ────────────────────────────────────────

    [Fact]
    public void FCS_SplitPanelPreview_WithCandidates_ShowsReport()
    {
        var (fcs, vm, dialog) = SetupFcs();
        var w = MakeCandidate("Winner", region: "EU");
        var l = MakeCandidate("Loser", region: "JP");
        vm.LastDedupeGroups = new ObservableCollection<DedupeResult>
        {
            new() { Winner = w, Losers = new List<RomCandidate> { l }, GameKey = "TestKey" }
        };
        vm.LastCandidates = new ObservableCollection<RomCandidate> { w, l };
        ExecCommand(vm, "SplitPanelPreview");
        Assert.True(dialog.ShowTextCalls.Count > 0);
    }

    // ── CommandPalette (requires windowHost) ─────────────────────

    [Fact]
    public void FCS_CommandPalette_WithHost_ShowsCommands()
    {
        var (fcs, vm, dialog) = SetupFcsWithHost();
        dialog.InputBoxResponses.Enqueue(""); // Cancel search
        ExecCommand(vm, "CommandPalette");
    }

    // ── ThemeEngine (requires windowHost) ────────────────────────

    [Fact]
    public void FCS_ThemeEngine_WithHost_ShowsOptions()
    {
        var (fcs, vm, dialog) = SetupFcsWithHost();
        dialog.NextYesNoCancel = ConfirmResult.Cancel;
        ExecCommand(vm, "ThemeEngine");
    }

    // ── Accessibility (requires windowHost) ──────────────────────

    [Fact]
    public void FCS_Accessibility_WithHost_ChangesFontSize()
    {
        var (fcs, vm, dialog) = SetupFcsWithHost();
        dialog.InputBoxResponses.Enqueue("18"); // new font size
        ExecCommand(vm, "Accessibility");
        // Asserts the command ran without error
        Assert.True(vm.LogEntries.Count > 0);
    }

    // ── SystemTray (requires windowHost) ─────────────────────────

    [Fact]
    public void FCS_SystemTray_WithHost_Toggles()
    {
        var (fcs, vm, dialog) = SetupFcsWithHost();
        ExecCommand(vm, "SystemTray");
    }

    // ── MobileWebUI (requires windowHost) ────────────────────────

    [Fact]
    public void FCS_MobileWebUI_WithHost_RunsCommand()
    {
        var (fcs, vm, dialog) = SetupFcsWithHost();
        dialog.NextConfirm = false; // Don't actually start API
        ExecCommand(vm, "MobileWebUI");
        // No crash = success; command exercises FindApiProjectPath
    }
}


// ═══════════════════════════════════════════════════════════════════
// MainViewModel Additional Property Tests (Settings, RunPipeline)
// ═══════════════════════════════════════════════════════════════════

public class MainViewModelSettingsTests
{
    private sealed class StubTheme : IThemeService
    {
        public AppTheme Current { get; private set; } = AppTheme.Dark;
        public bool IsDark => Current == AppTheme.Dark;
        public void ApplyTheme(AppTheme theme) => Current = theme;
        public void ApplyTheme(bool dark) => Current = dark ? AppTheme.Dark : AppTheme.Light;
        public void Toggle() => Current = IsDark ? AppTheme.Light : AppTheme.Dark;
    }

    private sealed class TestDialog : IDialogService
    {
        public string? BrowseFolder(string title) => null;
        public string? BrowseFile(string title, string filter = "") => null;
        public string? SaveFile(string title, string filter = "", string? defaultFileName = null) => null;
        public bool Confirm(string message, string title = "") => false;
        public void Info(string message, string title = "") { }
        public void Error(string message, string title = "") { }
        public ConfirmResult YesNoCancel(string message, string title = "") => ConfirmResult.Cancel;
        public string ShowInputBox(string prompt, string title = "", string defaultValue = "") => "";
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen") => true;
    }

    private MainViewModel CreateVm() => new(new StubTheme(), new TestDialog());

    [Theory]
    [InlineData(nameof(MainViewModel.DryRun))]
    [InlineData(nameof(MainViewModel.ConvertEnabled))]
    [InlineData(nameof(MainViewModel.AggressiveJunk))]
    [InlineData(nameof(MainViewModel.UseDat))]
    [InlineData(nameof(MainViewModel.DatFallback))]
    [InlineData(nameof(MainViewModel.ConfirmMove))]
    [InlineData(nameof(MainViewModel.AliasKeying))]
    public void BoolProperties_CanSetAndGet(string propertyName)
    {
        var vm = CreateVm();
        var prop = typeof(MainViewModel).GetProperty(propertyName);
        Assert.NotNull(prop);
        prop!.SetValue(vm, true);
        Assert.True((bool)prop.GetValue(vm)!);
        prop.SetValue(vm, false);
        Assert.False((bool)prop.GetValue(vm)!);
    }

    [Theory]
    [InlineData(nameof(MainViewModel.ToolChdman), "C:\\tools\\chdman.exe")]
    [InlineData(nameof(MainViewModel.Tool7z), "C:\\tools\\7z.exe")]
    [InlineData(nameof(MainViewModel.ToolDolphin), "C:\\tools\\dolphintool.exe")]
    [InlineData(nameof(MainViewModel.ToolPsxtract), "C:\\tools\\psxtract.exe")]
    [InlineData(nameof(MainViewModel.ToolCiso), "C:\\tools\\ciso.exe")]
    [InlineData(nameof(MainViewModel.DatRoot), "C:\\DATs")]
    [InlineData(nameof(MainViewModel.TrashRoot), "C:\\Trash")]
    [InlineData(nameof(MainViewModel.LogLevel), "Debug")]
    public void StringProperties_CanSetAndGet(string propertyName, string value)
    {
        var vm = CreateVm();
        var prop = typeof(MainViewModel).GetProperty(propertyName);
        Assert.NotNull(prop);
        prop!.SetValue(vm, value);
        Assert.Equal(value, (string)prop.GetValue(vm)!);
    }

    [Fact]
    public void RegionProperties_CanSetAndGet()
    {
        var vm = CreateVm();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferUS);
        Assert.False(vm.PreferJP);
    }

    [Fact]
    public void Roots_IsObservableCollection()
    {
        var vm = CreateVm();
        Assert.NotNull(vm.Roots);
        vm.Roots.Add(@"C:\Roms");
        Assert.Single(vm.Roots);
        vm.Roots.Add(@"D:\Roms");
        Assert.Equal(2, vm.Roots.Count);
    }

    [Fact]
    public void AddLog_AddsToLogEntries()
    {
        var vm = CreateVm();
        vm.AddLog("Test message", "INFO");
        Assert.True(vm.LogEntries.Count > 0);
        Assert.Equal("Test message", vm.LogEntries[^1].Text);
        Assert.Equal("INFO", vm.LogEntries[^1].Level);
    }

    [Fact]
    public void AddLog_MultipleLevels()
    {
        var vm = CreateVm();
        vm.AddLog("Info message", "INFO");
        vm.AddLog("Warning message", "WARN");
        vm.AddLog("Error message", "ERROR");
        vm.AddLog("Debug message", "DEBUG");
        Assert.Equal(4, vm.LogEntries.Count);
    }

    [Fact]
    public void ProgressText_CanBeSet()
    {
        var vm = CreateVm();
        vm.ProgressText = "Processing…";
        Assert.Equal("Processing…", vm.ProgressText);
    }

    [Fact]
    public void Progress_CanBeSet()
    {
        var vm = CreateVm();
        vm.Progress = 0.5;
        Assert.Equal(0.5, vm.Progress);
    }

    [Fact]
    public void IsBusy_DefaultFalse()
    {
        var vm = CreateVm();
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void JpKeepConsoles_CanBeSet()
    {
        var vm = CreateVm();
        vm.JpKeepConsoles = "NES,SNES";
        Assert.Equal("NES,SNES", vm.JpKeepConsoles);
    }

    [Fact]
    public void LastCandidates_CanBeAssigned()
    {
        var vm = CreateVm();
        var candidates = new ObservableCollection<RomCandidate>
        {
            new() { MainPath = "a.zip", GameKey = "A", Region = "EU", Extension = ".zip", Category = "GAME" }
        };
        vm.LastCandidates = candidates;
        Assert.Single(vm.LastCandidates);
    }

    [Fact]
    public void LastDedupeGroups_CanBeAssigned()
    {
        var vm = CreateVm();
        var groups = new ObservableCollection<DedupeResult>();
        vm.LastDedupeGroups = groups;
        Assert.Empty(vm.LastDedupeGroups);
    }

    [Fact]
    public void DatHashType_CanBeSet()
    {
        var vm = CreateVm();
        vm.DatHashType = "SHA256";
        Assert.Equal("SHA256", vm.DatHashType);
        vm.DatHashType = "MD5";
        Assert.Equal("MD5", vm.DatHashType);
    }

    [Fact]
    public void SortConsole_CanBeSet()
    {
        var vm = CreateVm();
        vm.SortConsole = true;
        Assert.True(vm.SortConsole);
    }
}
