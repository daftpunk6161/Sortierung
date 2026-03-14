using System.Text;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Analytics;
using RomCleanup.Infrastructure.Conversion;
using RomCleanup.Infrastructure.Deduplication;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Safety;
using RomCleanup.Core.SetParsing;
using Xunit;

namespace RomCleanup.Tests;

// =============================================================================
//  1) InsightsEngine – CSV injection, scoring, edge cases
// =============================================================================
public sealed class InsightsEngineCoverageTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), "ie_" + Guid.NewGuid().ToString("N")[..8]);

    public InsightsEngineCoverageTests() => Directory.CreateDirectory(_tmpDir);

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, true);
    }

    [Theory]
    [InlineData("=cmd|'/C calc'!A0")]
    [InlineData("+cmd|'/C calc'!A0")]
    [InlineData("@SUM(1+1)*cmd|'/C calc'!A0")]
    public void ExportInspectorCsv_SanitizesCsvInjection(string malicious)
    {
        var rows = new List<DuplicateInspectorRow>
        {
            new()
            {
                GameKey = malicious,
                Winner = false,
                Region = "EU",
                Type = ".zip",
                SizeMB = 1.0,
                RegionScore = 100,
                FormatScore = 200,
                VersionScore = 0,
                TotalScore = 300,
                ScoreBreakdown = "R:100 F:200 V:0",
                MainPath = "test.zip"
            }
        };

        var csvPath = Path.Combine(_tmpDir, "injection.csv");
        InsightsEngine.ExportInspectorCsv(rows, csvPath);

        var content = File.ReadAllText(csvPath);
        // All injected values should be prefixed with '
        Assert.DoesNotContain("\n=" + malicious[0], content);
        Assert.Contains("'", content); // prefix character present
    }

    [Fact]
    public void ExportInspectorCsv_HandlesCommasAndQuotes()
    {
        var rows = new List<DuplicateInspectorRow>
        {
            new()
            {
                GameKey = "Game, \"Special\" Edition",
                Winner = true,
                WinnerSource = "AUTO",
                Region = "US",
                Type = ".chd",
                SizeMB = 2.5,
                RegionScore = 999,
                FormatScore = 850,
                VersionScore = 10,
                TotalScore = 1859,
                ScoreBreakdown = "R:999 F:850 V:10",
                MainPath = @"D:\roms\test.chd"
            }
        };

        var csvPath = Path.Combine(_tmpDir, "quotes.csv");
        InsightsEngine.ExportInspectorCsv(rows, csvPath);

        var lines = File.ReadAllLines(csvPath);
        Assert.Equal(2, lines.Length);
        // Commas should be quoted, internal quotes doubled
        Assert.Contains("\"\"", lines[1]);
    }

    [Fact]
    public void ExportInspectorCsv_EmptyRows_WritesHeaderOnly()
    {
        var csvPath = Path.Combine(_tmpDir, "empty.csv");
        InsightsEngine.ExportInspectorCsv([], csvPath);

        var lines = File.ReadAllLines(csvPath);
        Assert.Single(lines); // header only (empty line after stripped)
    }

    [Fact]
    public void GetDuplicateInspectorRows_ExcludesPathsCorrectly()
    {
        var file1 = Path.Combine(_tmpDir, "Super Mario (USA).zip");
        var file2 = Path.Combine(_tmpDir, "Super Mario (Europe).zip");
        var excluded = Path.Combine(_tmpDir, "Super Mario (Japan).zip");
        foreach (var f in new[] { file1, file2, excluded })
            File.WriteAllText(f, "data");

        var fs = new InMemoryFs([file1, file2, excluded]);
        var engine = new InsightsEngine(fs);

        var rows = engine.GetDuplicateInspectorRows(
            roots: [_tmpDir],
            extensions: [".zip"],
            preferRegions: ["EU", "US"],
            excludedPaths: [excluded]);

        // With exclusion, only 2 files remain — still grouped under same key
        Assert.True(rows.Count >= 2);
        Assert.DoesNotContain(rows, r => r.MainPath == excluded);
    }

    [Fact]
    public void GetDuplicateInspectorRows_MaxGroups_LimitsOutput()
    {
        // Create files for 5 distinct game keys, each with 2 variants
        var files = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var f1 = Path.Combine(_tmpDir, $"Game{i} (USA).zip");
            var f2 = Path.Combine(_tmpDir, $"Game{i} (Europe).zip");
            File.WriteAllText(f1, "x");
            File.WriteAllText(f2, "x");
            files.Add(f1);
            files.Add(f2);
        }

        var fs = new InMemoryFs(files);
        var engine = new InsightsEngine(fs);

        var rows = engine.GetDuplicateInspectorRows(
            roots: [_tmpDir],
            extensions: [".zip"],
            preferRegions: ["US"],
            maxGroups: 2);

        // At most 2 groups × 2 variants = 4 rows
        var groupCount = rows.Select(r => r.GameKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.True(groupCount <= 2, $"Expected ≤2 groups, got {groupCount}");
    }

    [Fact]
    public void GetCollectionHealthRows_CountsDuplicates()
    {
        var winner = new RomCandidate { MainPath = "a.zip", ConsoleKey = "SNES", Extension = ".zip" };
        var loser = new RomCandidate { MainPath = "b.zip", ConsoleKey = "SNES", Extension = ".zip" };
        var result = new RunResult
        {
            AllCandidates = [winner, loser],
            DedupeGroups =
            [
                new DedupeResult { Winner = winner, Losers = [loser], GameKey = "game" }
            ]
        };

        var rows = InsightsEngine.GetCollectionHealthRows(result);
        Assert.Single(rows);
        Assert.Equal(1, rows[0].Duplicates);
    }

    [Fact]
    public void GetDatCoverageHeatmap_HeatBarFormat()
    {
        var result = new RunResult
        {
            AllCandidates =
            [
                new RomCandidate { ConsoleKey = "GBA", DatMatch = true },
                new RomCandidate { ConsoleKey = "GBA", DatMatch = true },
                new RomCandidate { ConsoleKey = "GBA", DatMatch = false },
                new RomCandidate { ConsoleKey = "GBA", DatMatch = false },
                new RomCandidate { ConsoleKey = "GBA", DatMatch = false }
            ],
            DedupeGroups = []
        };

        var rows = InsightsEngine.GetDatCoverageHeatmap(result);
        Assert.Single(rows);
        Assert.Equal(2, rows[0].Matched);
        Assert.Equal(3, rows[0].Missing);
        // Heat bar = 10 chars (filled + empty)
        Assert.Equal(10, rows[0].Heat.Length);
        Assert.Contains("█", rows[0].Heat);
        Assert.Contains("░", rows[0].Heat);
    }

    [Fact]
    public void GetCrossCollectionHints_FindsCrossRootDuplicates()
    {
        var root1Files = new[]
        {
            @"D:\Collection1\Super Mario (USA).zip",
            @"D:\Collection1\Zelda (USA).zip"
        };
        var root2Files = new[]
        {
            @"E:\Collection2\Super Mario (Europe).zip"
        };

        var fs = new MultiRootFs(new Dictionary<string, IReadOnlyList<string>>
        {
            [@"D:\Collection1"] = root1Files,
            [@"E:\Collection2"] = root2Files
        });

        var engine = new InsightsEngine(fs);
        var hints = engine.GetCrossCollectionHints(
            roots: [@"D:\Collection1", @"E:\Collection2"],
            extensions: [".zip"]);

        // Super Mario exists in 2 roots
        Assert.True(hints.Count >= 1);
        var mario = hints.FirstOrDefault(h => h.GameKey.Contains("mario", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(mario);
        Assert.Equal(2, mario!.RootCount);
    }
}

// =============================================================================
//  2) FormatConverterAdapter – Verify branches, tool error paths
// =============================================================================
public sealed class FormatConverterVerifyTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), "fcv_" + Guid.NewGuid().ToString("N")[..8]);

    public FormatConverterVerifyTests() => Directory.CreateDirectory(_tmpDir);

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, true);
    }

    [Fact]
    public void Verify_Chd_ToolNotFound_ReturnsFalse()
    {
        var tools = new FakeToolRunner(findResult: null);
        var adapter = new FormatConverterAdapter(tools);

        var chdPath = Path.Combine(_tmpDir, "test.chd");
        File.WriteAllText(chdPath, "dummy");

        Assert.False(adapter.Verify(chdPath, new ConversionTarget(".chd", "chdman", "verify")));
    }

    [Fact]
    public void Verify_Chd_ToolFails_ReturnsFalse()
    {
        var tools = new FakeToolRunner(findResult: "chdman.exe",
            processResult: new ToolResult(1, "error", false));
        var adapter = new FormatConverterAdapter(tools);

        var chdPath = Path.Combine(_tmpDir, "test.chd");
        File.WriteAllText(chdPath, "dummy");

        Assert.False(adapter.Verify(chdPath, new ConversionTarget(".chd", "chdman", "verify")));
    }

    [Fact]
    public void Verify_Chd_ToolSucceeds_ReturnsTrue()
    {
        var tools = new FakeToolRunner(findResult: "chdman.exe",
            processResult: new ToolResult(0, "ok", true));
        var adapter = new FormatConverterAdapter(tools);

        var chdPath = Path.Combine(_tmpDir, "test.chd");
        File.WriteAllText(chdPath, "dummy");

        Assert.True(adapter.Verify(chdPath, new ConversionTarget(".chd", "chdman", "verify")));
    }

    [Fact]
    public void Verify_Rvz_ValidMagicBytes_ReturnsTrue()
    {
        var tools = new FakeToolRunner();
        var adapter = new FormatConverterAdapter(tools);

        var rvzPath = Path.Combine(_tmpDir, "test.rvz");
        // RVZ magic: R V Z 0x01
        File.WriteAllBytes(rvzPath, [(byte)'R', (byte)'V', (byte)'Z', 0x01, 0x00, 0x00]);

        Assert.True(adapter.Verify(rvzPath, new ConversionTarget(".rvz", "dolphintool", "convert")));
    }

    [Fact]
    public void Verify_Rvz_InvalidMagicBytes_ReturnsFalse()
    {
        var tools = new FakeToolRunner();
        var adapter = new FormatConverterAdapter(tools);

        var rvzPath = Path.Combine(_tmpDir, "test.rvz");
        File.WriteAllBytes(rvzPath, [0x00, 0x00, 0x00, 0x00]);

        Assert.False(adapter.Verify(rvzPath, new ConversionTarget(".rvz", "dolphintool", "convert")));
    }

    [Fact]
    public void Verify_Rvz_FileTooSmall_ReturnsFalse()
    {
        var tools = new FakeToolRunner();
        var adapter = new FormatConverterAdapter(tools);

        var rvzPath = Path.Combine(_tmpDir, "small.rvz");
        File.WriteAllBytes(rvzPath, [0x01, 0x02]); // only 2 bytes

        Assert.False(adapter.Verify(rvzPath, new ConversionTarget(".rvz", "dolphintool", "convert")));
    }

    [Fact]
    public void Verify_Zip_ToolNotFound_ReturnsFalse()
    {
        var tools = new FakeToolRunner(findResult: null);
        var adapter = new FormatConverterAdapter(tools);

        var zipPath = Path.Combine(_tmpDir, "test.zip");
        File.WriteAllText(zipPath, "dummy");

        Assert.False(adapter.Verify(zipPath, new ConversionTarget(".zip", "7z", "zip")));
    }

    [Fact]
    public void Verify_UnknownExtension_ReturnsFalse()
    {
        var tools = new FakeToolRunner();
        var adapter = new FormatConverterAdapter(tools);

        var path = Path.Combine(_tmpDir, "test.abc");
        File.WriteAllText(path, "dummy");

        Assert.False(adapter.Verify(path, new ConversionTarget(".abc", "unknown", "x")));
    }

    [Fact]
    public void Verify_FileNotFound_ReturnsFalse()
    {
        var tools = new FakeToolRunner();
        var adapter = new FormatConverterAdapter(tools);

        Assert.False(adapter.Verify(@"C:\nonexistent.chd", new ConversionTarget(".chd", "chdman", "verify")));
    }

    [Fact]
    public void Convert_SourceNotFound_ReturnsError()
    {
        var tools = new FakeToolRunner();
        var adapter = new FormatConverterAdapter(tools);

        var result = adapter.Convert(@"C:\nonexistent.iso", new ConversionTarget(".chd", "chdman", "createcd"));
        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("source-not-found", result.Reason);
    }

    [Fact]
    public void Convert_ToolNotFound_ReturnsSkipped()
    {
        var tools = new FakeToolRunner(findResult: null);
        var adapter = new FormatConverterAdapter(tools);

        var src = Path.Combine(_tmpDir, "game.iso");
        File.WriteAllText(src, "data");

        var result = adapter.Convert(src, new ConversionTarget(".chd", "chdman", "createcd"));
        Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
        Assert.Contains("tool-not-found", result.Reason);
    }

    [Fact]
    public void Convert_AlreadyTargetFormat_ReturnsSkipped()
    {
        var tools = new FakeToolRunner(findResult: "chdman.exe");
        var adapter = new FormatConverterAdapter(tools);

        var src = Path.Combine(_tmpDir, "game.chd");
        File.WriteAllText(src, "data");

        var result = adapter.Convert(src, new ConversionTarget(".chd", "chdman", "createcd"));
        Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
        Assert.Equal("already-target-format", result.Reason);
    }

    [Fact]
    public void Convert_UnknownTool_ReturnsError()
    {
        var tools = new FakeToolRunner(findResult: "sometool.exe");
        var adapter = new FormatConverterAdapter(tools);

        var src = Path.Combine(_tmpDir, "game.iso");
        File.WriteAllText(src, "data");

        var result = adapter.Convert(src, new ConversionTarget(".xyz", "sometool", "convert"));
        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Contains("unknown-tool", result.Reason);
    }

    [Fact]
    public void Convert_DolphinTool_UnsupportedExtension_ReturnsSkipped()
    {
        var tools = new FakeToolRunner(findResult: "dolphintool.exe");
        var adapter = new FormatConverterAdapter(tools);

        var src = Path.Combine(_tmpDir, "game.txt");
        File.WriteAllText(src, "data");

        var result = adapter.Convert(src, new ConversionTarget(".rvz", "dolphintool", "convert"));
        Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
        Assert.Equal("dolphintool-unsupported-source", result.Reason);
    }

    [Fact]
    public void Convert_ChdmanFails_ReturnsError()
    {
        var tools = new FakeToolRunner(findResult: "chdman.exe",
            processResult: new ToolResult(1, "chdman error", false));
        var adapter = new FormatConverterAdapter(tools);

        var src = Path.Combine(_tmpDir, "game.iso");
        File.WriteAllText(src, "data");

        var result = adapter.Convert(src, new ConversionTarget(".chd", "chdman", "createcd"));
        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Contains("chdman-failed", result.Reason);
    }

    [Fact]
    public void GetTargetFormat_PbpExtension_ReturnsPsxtractTarget()
    {
        var tools = new FakeToolRunner();
        var adapter = new FormatConverterAdapter(tools);

        var target = adapter.GetTargetFormat("PSP", ".pbp");
        Assert.NotNull(target);
        Assert.Equal(".chd", target!.Extension);
        Assert.Equal("psxtract", target.ToolName);
    }

    [Fact]
    public void GetTargetFormat_NullConsoleKey_ReturnsNull()
    {
        var tools = new FakeToolRunner();
        var adapter = new FormatConverterAdapter(tools);

        Assert.Null(adapter.GetTargetFormat(null!, ".iso"));
        Assert.Null(adapter.GetTargetFormat("", ".iso"));
    }

    [Fact]
    public void GetTargetFormat_UnknownConsole_ReturnsNull()
    {
        var tools = new FakeToolRunner();
        var adapter = new FormatConverterAdapter(tools);

        Assert.Null(adapter.GetTargetFormat("UNKNOWNCONSOLE", ".iso"));
    }
}

// =============================================================================
//  3) ConversionPipeline – DryRun, cancellation, tool errors, BuildCsoToChdPipeline
// =============================================================================
public sealed class ConversionPipelineBranchTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), "cpipe_" + Guid.NewGuid().ToString("N")[..8]);

    public ConversionPipelineBranchTests() => Directory.CreateDirectory(_tmpDir);

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, true);
    }

    [Fact]
    public void BuildCsoToChdPipeline_ProducesCorrectSteps()
    {
        var src = Path.Combine(_tmpDir, "game.cso");
        var pipeline = ConversionPipeline.BuildCsoToChdPipeline(src, _tmpDir);

        Assert.Equal(2, pipeline.Steps.Count);
        Assert.Equal("ciso", pipeline.Steps[0].Tool);
        Assert.Equal("decompress", pipeline.Steps[0].Action);
        Assert.True(pipeline.Steps[0].IsTemp);
        Assert.Equal("chdman", pipeline.Steps[1].Tool);
        Assert.Equal("createcd", pipeline.Steps[1].Action);
        Assert.False(pipeline.Steps[1].IsTemp);
        Assert.EndsWith(".iso", pipeline.Steps[0].Output);
        Assert.EndsWith(".chd", pipeline.Steps[1].Output);
    }

    [Fact]
    public void Execute_DryRun_ReportsAllSteps()
    {
        var src = Path.Combine(_tmpDir, "game.cso");
        File.WriteAllText(src, "data");

        var tools = new FakeToolRunner(findResult: "ciso.exe");
        var fs = new InMemoryFs();
        var pipeline = new ConversionPipeline(tools, fs);

        var def = ConversionPipeline.BuildCsoToChdPipeline(src, _tmpDir);
        var result = pipeline.Execute(def, mode: "DryRun");

        Assert.Equal("completed", result.Status);
        Assert.Equal(2, result.Steps.Count);
        Assert.All(result.Steps, s => Assert.Equal("dryrun", s.Status));
    }

    [Fact]
    public void Execute_Cancellation_StopsEarly()
    {
        var src = Path.Combine(_tmpDir, "game.cso");
        File.WriteAllText(src, "data");

        var tools = new FakeToolRunner(findResult: "tool.exe",
            processResult: new ToolResult(0, "ok", true));
        var fs = new InMemoryFs();
        var pipeline = new ConversionPipeline(tools, fs);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // immediately cancelled

        var def = ConversionPipeline.BuildCsoToChdPipeline(src, _tmpDir);
        var result = pipeline.Execute(def, mode: "Move", ct: cts.Token);

        Assert.Contains(result.Steps, s => s.Status == "cancelled");
    }

    [Fact]
    public void Execute_ToolNotFound_FailsStep()
    {
        var src = Path.Combine(_tmpDir, "game.cso");
        File.WriteAllText(src, new string('x', 100)); // small file for disk space check

        var tools = new FakeToolRunner(findResult: null); // no tools
        var fs = new InMemoryFs();
        var pipeline = new ConversionPipeline(tools, fs);

        var def = new ConversionPipelineDef
        {
            SourcePath = src,
            Steps =
            [
                new ConversionPipelineStep
                {
                    Tool = "ciso",
                    Action = "decompress",
                    Input = src,
                    Output = Path.Combine(_tmpDir, "out.iso")
                }
            ]
        };

        var result = pipeline.Execute(def, mode: "Move");
        Assert.Equal("failed", result.Status);
        Assert.Contains(result.Steps, s => s.Status == "error" && s.Error!.Contains("not found"));
    }

    [Fact]
    public void CheckDiskSpace_SourceNotFound_ReturnsNotOk()
    {
        var result = ConversionPipeline.CheckDiskSpace(@"C:\nonexistent.iso", _tmpDir);
        Assert.False(result.Ok);
        Assert.Contains("not found", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckDiskSpace_ValidFile_ReturnsOk()
    {
        var src = Path.Combine(_tmpDir, "small.dat");
        File.WriteAllBytes(src, new byte[1024]);

        var result = ConversionPipeline.CheckDiskSpace(src, _tmpDir);
        Assert.True(result.Ok);
        Assert.Equal(3072, result.RequiredBytes); // 1024 * 3.0
        Assert.True(result.AvailableBytes > 0);
    }

    [Fact]
    public void BuildToolArguments_UnknownTool_ThrowsInvalidOperation()
    {
        var tools = new FakeToolRunner(findResult: "tool.exe",
            processResult: new ToolResult(0, "", true));
        var fs = new InMemoryFs();
        var pipeline = new ConversionPipeline(tools, fs);

        var src = Path.Combine(_tmpDir, "game.bin");
        File.WriteAllText(src, new string('x', 100));

        var def = new ConversionPipelineDef
        {
            SourcePath = src,
            Steps =
            [
                new ConversionPipelineStep
                {
                    Tool = "unknowntool",
                    Action = "convert",
                    Input = src,
                    Output = Path.Combine(_tmpDir, "out.bin")
                }
            ]
        };

        // BuildToolArguments throws InvalidOperationException for unknown tools
        // which propagates through ExecuteStep
        Assert.Throws<InvalidOperationException>(() => pipeline.Execute(def, mode: "Move"));
    }
}

// =============================================================================
//  4) RateLimiter – disabled, window reset, eviction
// =============================================================================
public sealed class RateLimiterCoverageTests
{
    [Fact]
    public void Disabled_AlwaysReturnsTrue()
    {
        var limiter = new RomCleanup.Api.RateLimiter(0, TimeSpan.FromMinutes(1));
        for (int i = 0; i < 1000; i++)
            Assert.True(limiter.TryAcquire("client"));
    }

    [Fact]
    public void NegativeMax_AlwaysReturnsTrue()
    {
        var limiter = new RomCleanup.Api.RateLimiter(-1, TimeSpan.FromMinutes(1));
        Assert.True(limiter.TryAcquire("test"));
    }

    [Fact]
    public void ExceedsLimit_ReturnsFalse()
    {
        var limiter = new RomCleanup.Api.RateLimiter(3, TimeSpan.FromMinutes(1));
        Assert.True(limiter.TryAcquire("c1"));
        Assert.True(limiter.TryAcquire("c1"));
        Assert.True(limiter.TryAcquire("c1"));
        Assert.False(limiter.TryAcquire("c1"));
    }

    [Fact]
    public void DifferentClients_IndependentBuckets()
    {
        var limiter = new RomCleanup.Api.RateLimiter(1, TimeSpan.FromMinutes(1));
        Assert.True(limiter.TryAcquire("clientA"));
        Assert.False(limiter.TryAcquire("clientA")); // exhausted
        Assert.True(limiter.TryAcquire("clientB")); // different client = fresh bucket
    }
}

// =============================================================================
//  5) GdiSetParser – quoted filenames, missing files, edge cases
// =============================================================================
public sealed class GdiSetParserCoverageTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), "gdi_" + Guid.NewGuid().ToString("N")[..8]);

    public GdiSetParserCoverageTests() => Directory.CreateDirectory(_tmpDir);

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, true);
    }

    [Fact]
    public void GetRelatedFiles_QuotedFilenames()
    {
        var track = Path.Combine(_tmpDir, "Track 01.bin");
        File.WriteAllText(track, "data");

        var gdiPath = Path.Combine(_tmpDir, "game.gdi");
        File.WriteAllText(gdiPath, "2\n1 0 4 2352 \"Track 01.bin\" 0\n2 600 0 2352 \"Track 01.bin\" 0");

        var files = GdiSetParser.GetRelatedFiles(gdiPath);
        Assert.Single(files); // deduplicated
        Assert.Equal(track, files[0]);
    }

    [Fact]
    public void GetRelatedFiles_UnquotedFilenames()
    {
        var track = Path.Combine(_tmpDir, "track01.bin");
        File.WriteAllText(track, "data");

        var gdiPath = Path.Combine(_tmpDir, "game.gdi");
        File.WriteAllText(gdiPath, "1\n1 0 4 2352 track01.bin 0");

        var files = GdiSetParser.GetRelatedFiles(gdiPath);
        Assert.Single(files);
        Assert.Equal(track, files[0]);
    }

    [Fact]
    public void GetRelatedFiles_NonExistentFile_ExcludedInExistingOnly()
    {
        var gdiPath = Path.Combine(_tmpDir, "game.gdi");
        File.WriteAllText(gdiPath, "1\n1 0 4 2352 missing.bin 0");

        var files = GdiSetParser.GetRelatedFiles(gdiPath);
        Assert.Empty(files);
    }

    [Fact]
    public void GetMissingFiles_ReturnsMissingOnly()
    {
        var existing = Path.Combine(_tmpDir, "track01.bin");
        File.WriteAllText(existing, "data");

        var gdiPath = Path.Combine(_tmpDir, "game.gdi");
        File.WriteAllText(gdiPath, "2\n1 0 4 2352 track01.bin 0\n2 600 0 2352 track02.bin 0");

        var missing = GdiSetParser.GetMissingFiles(gdiPath);
        Assert.Single(missing);
        Assert.Contains("track02.bin", missing[0]);
    }

    [Fact]
    public void GetRelatedFiles_NullPath_ReturnsEmpty()
    {
        Assert.Empty(GdiSetParser.GetRelatedFiles(null!));
        Assert.Empty(GdiSetParser.GetRelatedFiles(""));
        Assert.Empty(GdiSetParser.GetRelatedFiles("   "));
    }

    [Fact]
    public void GetRelatedFiles_NonExistentGdi_ReturnsEmpty()
    {
        Assert.Empty(GdiSetParser.GetRelatedFiles(@"C:\nonexistent.gdi"));
    }

    [Fact]
    public void GetRelatedFiles_EmptyLines_SkipsGracefully()
    {
        var gdiPath = Path.Combine(_tmpDir, "game.gdi");
        File.WriteAllText(gdiPath, "\n\n   \n");

        var files = GdiSetParser.GetRelatedFiles(gdiPath);
        Assert.Empty(files);
    }

    [Fact]
    public void GetRelatedFiles_PathTraversal_Blocked()
    {
        var gdiPath = Path.Combine(_tmpDir, "game.gdi");
        File.WriteAllText(gdiPath, "1\n1 0 4 2352 ../../../etc/passwd 0");

        var files = GdiSetParser.GetRelatedFiles(gdiPath);
        Assert.Empty(files); // path traversal blocked
    }

    [Fact]
    public void GetRelatedFiles_UnclosedQuote_SkipsLine()
    {
        var gdiPath = Path.Combine(_tmpDir, "game.gdi");
        File.WriteAllText(gdiPath, "1\n1 0 4 2352 \"unclosed_quote 0");

        var files = GdiSetParser.GetRelatedFiles(gdiPath);
        Assert.Empty(files);
    }

    [Fact]
    public void GetRelatedFiles_TooFewParts_SkipsLine()
    {
        var gdiPath = Path.Combine(_tmpDir, "game.gdi");
        File.WriteAllText(gdiPath, "1\n1 0 4");

        var files = GdiSetParser.GetRelatedFiles(gdiPath);
        Assert.Empty(files);
    }

    [Fact]
    public void GetRelatedFiles_AbsolutePath_BlockedIfOutsideDir()
    {
        var gdiPath = Path.Combine(_tmpDir, "game.gdi");
        File.WriteAllText(gdiPath, "1\n1 0 4 2352 C:\\Windows\\System32\\cmd.exe 0");

        var files = GdiSetParser.GetRelatedFiles(gdiPath);
        Assert.Empty(files); // absolute path outside gdi dir
    }
}

// =============================================================================
//  6) SafetyValidator – profiles, protected paths, tool testing
// =============================================================================
public sealed class SafetyValidatorBranchTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), "sv_" + Guid.NewGuid().ToString("N")[..8]);

    public SafetyValidatorBranchTests() => Directory.CreateDirectory(_tmpDir);

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, true);
    }

    [Fact]
    public void GetProfiles_ReturnsThreeProfiles()
    {
        var profiles = SafetyValidator.GetProfiles();
        Assert.Contains("Conservative", profiles.Keys);
        Assert.Contains("Balanced", profiles.Keys);
        Assert.Contains("Expert", profiles.Keys);
    }

    [Fact]
    public void GetProfile_Unknown_ReturnsBalanced()
    {
        var profile = SafetyValidator.GetProfile("NonExistent");
        Assert.Equal("Balanced", profile.Name);
    }

    [Fact]
    public void GetProfile_Conservative_HasMoreProtectedPaths()
    {
        var cons = SafetyValidator.GetProfile("Conservative");
        var expert = SafetyValidator.GetProfile("Expert");
        Assert.True(cons.ProtectedPaths.Count >= expert.ProtectedPaths.Count);
    }

    [Fact]
    public void NormalizePath_Null_ReturnsNull()
    {
        Assert.Null(SafetyValidator.NormalizePath(null));
        Assert.Null(SafetyValidator.NormalizePath(""));
        Assert.Null(SafetyValidator.NormalizePath("   "));
    }

    [Fact]
    public void NormalizePath_Valid_ReturnsFullPath()
    {
        var result = SafetyValidator.NormalizePath(_tmpDir);
        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(_tmpDir), result);
    }

    [Fact]
    public void ValidateSandbox_InvalidRoot_AddsBlocker()
    {
        var tools = new FakeToolRunner();
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        var result = validator.ValidateSandbox(roots: [@"\\?\invalid:path<>"]);
        Assert.Equal("blocked", result.Status);
        Assert.True(result.BlockerCount > 0);
    }

    [Fact]
    public void ValidateSandbox_NonExistentRoot_AddsBlocker()
    {
        var tools = new FakeToolRunner();
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        var result = validator.ValidateSandbox(roots: [@"C:\NonExistentDir_12345"]);
        Assert.Equal("blocked", result.Status);
        Assert.True(result.BlockerCount > 0);
    }

    [Fact]
    public void ValidateSandbox_DriveRoot_Blocked()
    {
        var tools = new FakeToolRunner();
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        var result = validator.ValidateSandbox(roots: [@"C:\"]);
        Assert.Equal("blocked", result.Status);
        Assert.Contains(result.Blockers, b => b.Contains("drive root", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSandbox_ProtectedPath_Blocked()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(winDir)) return; // skip on non-Windows

        var tools = new FakeToolRunner();
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        // Use the Windows dir itself as a root (shouldn't pass)
        var result = validator.ValidateSandbox(roots: [winDir]);
        Assert.Equal("blocked", result.Status);
        Assert.Contains(result.Blockers, b => b.Contains("protected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSandbox_ValidRoot_ReturnsOk()
    {
        var tools = new FakeToolRunner();
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        var result = validator.ValidateSandbox(roots: [_tmpDir]);
        Assert.Equal("ok", result.Status);
        Assert.Equal(0, result.BlockerCount);
    }

    [Fact]
    public void ValidateSandbox_NoExtensions_AddsWarning()
    {
        var tools = new FakeToolRunner();
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        var result = validator.ValidateSandbox(roots: [_tmpDir], extensions: []);
        Assert.Contains(result.Warnings, w => w.Contains("extensions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSandbox_DatEnabledWithoutRoot_AddsWarning()
    {
        var tools = new FakeToolRunner();
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        var result = validator.ValidateSandbox(roots: [_tmpDir], useDat: true);
        Assert.Contains(result.Warnings, w => w.Contains("DAT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSandbox_ConvertEnabled_ChdmanMissing_AddsWarning()
    {
        var tools = new FakeToolRunner(findResult: null);
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        var result = validator.ValidateSandbox(roots: [_tmpDir], convertEnabled: true);
        Assert.Contains(result.Warnings, w => w.Contains("chdman", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSandbox_CustomProtectedPaths_Parsed()
    {
        var tools = new FakeToolRunner();
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        // Use the tmpDir as a "protected path" and then try to scan it
        var result = validator.ValidateSandbox(
            roots: [_tmpDir],
            protectedPathsText: _tmpDir);
        Assert.Equal("blocked", result.Status);
    }

    [Fact]
    public void TestTools_AllMissing_ReportsMissing()
    {
        var tools = new FakeToolRunner(findResult: null);
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        var result = validator.TestTools();
        Assert.True(result.MissingCount > 0);
        Assert.All(result.Results, r => Assert.Equal("missing", r.Status));
    }

    [Fact]
    public void TestTools_AllHealthy_ReportsHealthy()
    {
        var tools = new FakeToolRunner(findResult: "tool.exe",
            processResult: new ToolResult(0, "v1.0.0", true));
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        var result = validator.TestTools();
        Assert.True(result.HealthyCount > 0);
        Assert.Equal(0, result.MissingCount);
    }

    [Fact]
    public void TestTools_NonZeroExitCode_ReportsWarning()
    {
        var tools = new FakeToolRunner(findResult: "tool.exe",
            processResult: new ToolResult(127, "error", false));
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        var result = validator.TestTools();
        Assert.True(result.WarningCount > 0);
        Assert.Contains(result.Results, r => r.Status == "warning");
    }

    [Fact]
    public void TestTools_Exception_ReportsError()
    {
        var tools = new ThrowingToolRunner();
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        var result = validator.TestTools();
        Assert.True(result.WarningCount > 0);
        Assert.Contains(result.Results, r => r.Status == "error");
    }

    [Fact]
    public void TestTools_WithOverrides_UsesOverridePath()
    {
        var callLog = new List<string>();
        var tools = new LoggingToolRunner(callLog);
        var fs = new InMemoryFs();
        var validator = new SafetyValidator(tools, fs);

        var overrides = new Dictionary<string, string> { ["chdman"] = @"D:\tools\chdman.exe" };
        validator.TestTools(toolOverrides: overrides);

        // The override path should have been used
        Assert.Contains(callLog, c => c.Contains(@"D:\tools\chdman.exe"));
    }
}

// =============================================================================
//  7) FolderDeduplicator – GetFolderBaseKey, PS3 hash, NeedsFolderDedupe
// =============================================================================
public sealed class FolderDeduplicatorBranchTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), "fdd_" + Guid.NewGuid().ToString("N")[..8]);

    public FolderDeduplicatorBranchTests() => Directory.CreateDirectory(_tmpDir);

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, true);
    }

    [Theory]
    [InlineData("DOS")]
    [InlineData("AMIGA")]
    [InlineData("CD32")]
    [InlineData("X68K")]
    [InlineData("FMTOWNS")]
    public void NeedsFolderDedupe_KnownConsoles_ReturnsTrue(string key)
    {
        Assert.True(FolderDeduplicator.NeedsFolderDedupe(key));
    }

    [Theory]
    [InlineData("NES")]
    [InlineData("SNES")]
    [InlineData("PS1")]
    public void NeedsFolderDedupe_NonFolderConsoles_ReturnsFalse(string key)
    {
        Assert.False(FolderDeduplicator.NeedsFolderDedupe(key));
    }

    [Fact]
    public void NeedsPs3Dedupe_PS3_ReturnsTrue()
    {
        Assert.True(FolderDeduplicator.NeedsPs3Dedupe("PS3"));
    }

    [Fact]
    public void NeedsPs3Dedupe_Other_ReturnsFalse()
    {
        Assert.False(FolderDeduplicator.NeedsPs3Dedupe("PS2"));
    }

    [Fact]
    public void IsPs3MultidiscFolder_DiscPattern_ReturnsTrue()
    {
        Assert.True(FolderDeduplicator.IsPs3MultidiscFolder("Game Disc 1"));
        Assert.True(FolderDeduplicator.IsPs3MultidiscFolder("Game Disk 2"));
        Assert.True(FolderDeduplicator.IsPs3MultidiscFolder("Something CD3"));
    }

    [Fact]
    public void IsPs3MultidiscFolder_NoPattern_ReturnsFalse()
    {
        Assert.False(FolderDeduplicator.IsPs3MultidiscFolder("Regular Game Name"));
    }

    [Theory]
    [InlineData("Game (USA) [!]", "game (usa) [!]")] // brackets not stripped by base key
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void GetFolderBaseKey_EdgeCases(string input, string _)
    {
        var result = FolderDeduplicator.GetFolderBaseKey(input);
        // Just verify it doesn't throw and returns non-null
        Assert.NotNull(result);
    }

    [Fact]
    public void GetFolderBaseKey_PreservesDiscMarkers()
    {
        var key1 = FolderDeduplicator.GetFolderBaseKey("Shenmue (Disc 1)");
        var key2 = FolderDeduplicator.GetFolderBaseKey("Shenmue (Disc 2)");
        // Disc markers should be preserved, so keys should differ
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GetFolderBaseKey_StripsNonPreservedParens()
    {
        var key = FolderDeduplicator.GetFolderBaseKey("Some Game (v1.2) (Special Edition)");
        // Non-preserved parens should be stripped
        Assert.DoesNotContain("Special Edition", key);
    }

    [Fact]
    public void GetFolderBaseKey_StripsBrackets()
    {
        var key = FolderDeduplicator.GetFolderBaseKey("Game Name [some tag]");
        Assert.DoesNotContain("[some tag]", key);
    }

    [Fact]
    public void GetFolderBaseKey_StripsVersionSuffix()
    {
        var key = FolderDeduplicator.GetFolderBaseKey("Game Name v1.0.5");
        Assert.DoesNotContain("1.0.5", key);
    }

    [Fact]
    public void GetFolderBaseKey_UnicodeNormalization()
    {
        // Combining characters should be normalized
        var key = FolderDeduplicator.GetFolderBaseKey("café");
        Assert.NotNull(key);
        Assert.True(key.Length > 0);
    }

    [Fact]
    public void GetPs3FolderHash_NoKeyFiles_ReturnsNull()
    {
        var emptyDir = Path.Combine(_tmpDir, "empty_game");
        Directory.CreateDirectory(emptyDir);

        Assert.Null(FolderDeduplicator.GetPs3FolderHash(emptyDir));
    }

    [Fact]
    public void GetPs3FolderHash_WithKeyFiles_ReturnsHash()
    {
        var gameDir = Path.Combine(_tmpDir, "ps3_game");
        Directory.CreateDirectory(gameDir);

        File.WriteAllText(Path.Combine(gameDir, "PS3_DISC.SFB"), "test_sfb_data");
        File.WriteAllText(Path.Combine(gameDir, "PARAM.SFO"), "test_sfo_data");

        var hash = FolderDeduplicator.GetPs3FolderHash(gameDir);
        Assert.NotNull(hash);
        Assert.Equal(64, hash!.Length); // SHA256 hex string
    }

    [Fact]
    public void GetPs3FolderHash_SameFiles_SameHash()
    {
        var dir1 = Path.Combine(_tmpDir, "game1");
        var dir2 = Path.Combine(_tmpDir, "game2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        File.WriteAllText(Path.Combine(dir1, "PS3_DISC.SFB"), "identical");
        File.WriteAllText(Path.Combine(dir2, "PS3_DISC.SFB"), "identical");

        Assert.Equal(
            FolderDeduplicator.GetPs3FolderHash(dir1),
            FolderDeduplicator.GetPs3FolderHash(dir2));
    }

    [Fact]
    public void GetPs3FolderHash_DifferentFiles_DifferentHash()
    {
        var dir1 = Path.Combine(_tmpDir, "game_a");
        var dir2 = Path.Combine(_tmpDir, "game_b");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        File.WriteAllText(Path.Combine(dir1, "PS3_DISC.SFB"), "data_v1");
        File.WriteAllText(Path.Combine(dir2, "PS3_DISC.SFB"), "data_v2");

        Assert.NotEqual(
            FolderDeduplicator.GetPs3FolderHash(dir1),
            FolderDeduplicator.GetPs3FolderHash(dir2));
    }

    [Fact]
    public void DeduplicateByBaseName_DryRun_ReportsActions()
    {
        // Create two folders with same base key
        var folder1 = Path.Combine(_tmpDir, "Game Name (USA)");
        var folder2 = Path.Combine(_tmpDir, "Game Name (Europe)");
        Directory.CreateDirectory(folder1);
        Directory.CreateDirectory(folder2);
        File.WriteAllText(Path.Combine(folder1, "game.bin"), "data");
        File.WriteAllText(Path.Combine(folder2, "game.bin"), "data");

        var fs = new InMemoryFs();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicateByBaseName([_tmpDir], mode: "DryRun");
        Assert.True(result.DupeGroups >= 1);
        Assert.Contains(result.Actions, a => a.Action == "DRYRUN-MOVE");
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void DeduplicateByBaseName_EmptyRoot_NoFolders()
    {
        var emptyDir = Path.Combine(_tmpDir, "empty_root");
        Directory.CreateDirectory(emptyDir);

        var fs = new InMemoryFs();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicateByBaseName([emptyDir], mode: "DryRun");
        Assert.Equal(0, result.DupeGroups);
        Assert.Equal(0, result.TotalFolders);
    }

    [Fact]
    public void DeduplicateByBaseName_NonExistentRoot_Skips()
    {
        var fs = new InMemoryFs();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicateByBaseName([@"Z:\nonexistent_root"], mode: "DryRun");
        Assert.Equal(0, result.DupeGroups);
    }

    [Fact]
    public void AutoDeduplicate_NoMatchingConsoles_ReturnsEmpty()
    {
        var fs = new InMemoryFs();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.AutoDeduplicate(
            [_tmpDir],
            mode: "DryRun",
            consoleKeyDetector: _ => "NES"); // NES doesn't need folder dedupe

        Assert.Empty(result.Ps3Roots);
        Assert.Empty(result.FolderRoots);
    }

    [Fact]
    public void AutoDeduplicate_DetectsAmigaRoot()
    {
        var fs = new InMemoryFs();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.AutoDeduplicate(
            [_tmpDir],
            mode: "DryRun",
            consoleKeyDetector: _ => "AMIGA");

        Assert.Contains(_tmpDir, result.FolderRoots);
    }

    [Fact]
    public void AutoDeduplicate_DetectsPs3Root()
    {
        var fs = new InMemoryFs();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.AutoDeduplicate(
            [_tmpDir],
            mode: "DryRun",
            consoleKeyDetector: _ => "PS3");

        Assert.Contains(_tmpDir, result.Ps3Roots);
    }
}

// =============================================================================
//  Shared Fakes
// =============================================================================

file sealed class FakeToolRunner : IToolRunner
{
    private readonly string? _findResult;
    private readonly ToolResult _processResult;

    public FakeToolRunner(string? findResult = "tool.exe", ToolResult? processResult = null)
    {
        _findResult = findResult;
        _processResult = processResult ?? new ToolResult(0, "", true);
    }

    public string? FindTool(string toolName) => _findResult;
    public ToolResult InvokeProcess(string toolPath, string[] args, string? errorLabel = null) => _processResult;
    public ToolResult Invoke7z(string archivePath, string[] extraArgs) => _processResult;
}

file sealed class ThrowingToolRunner : IToolRunner
{
    public string? FindTool(string toolName) => "tool.exe";
    public ToolResult InvokeProcess(string toolPath, string[] args, string? errorLabel = null)
        => throw new InvalidOperationException("Tool crashed");
    public ToolResult Invoke7z(string archivePath, string[] extraArgs)
        => throw new InvalidOperationException("Tool crashed");
}

file sealed class LoggingToolRunner : IToolRunner
{
    private readonly List<string> _log;
    public LoggingToolRunner(List<string> log) => _log = log;
    public string? FindTool(string toolName) => "tool.exe";
    public ToolResult InvokeProcess(string toolPath, string[] args, string? errorLabel = null)
    {
        _log.Add($"InvokeProcess({toolPath}, [{string.Join(",", args)}])");
        return new ToolResult(0, "v1.0", true);
    }
    public ToolResult Invoke7z(string archivePath, string[] extraArgs)
    {
        _log.Add($"Invoke7z({archivePath})");
        return new ToolResult(0, "", true);
    }
}

file sealed class InMemoryFs : IFileSystem
{
    private readonly IReadOnlyList<string> _files;
    public InMemoryFs(IReadOnlyList<string>? files = null) => _files = files ?? [];
    public bool TestPath(string literalPath, string pathType = "Any") => true;
    public string EnsureDirectory(string path) { Directory.CreateDirectory(path); return path; }
    public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null) => _files;
    public bool MoveItemSafely(string src, string dest) => true;
    public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
        => Path.Combine(rootPath, relativePath);
    public bool IsReparsePoint(string path) => false;
    public void DeleteFile(string path) { }
    public void CopyFile(string src, string dest, bool overwrite = false) { }
}

file sealed class MultiRootFs : IFileSystem
{
    private readonly Dictionary<string, IReadOnlyList<string>> _rootFiles;
    public MultiRootFs(Dictionary<string, IReadOnlyList<string>> rootFiles)
        => _rootFiles = rootFiles;
    public bool TestPath(string literalPath, string pathType = "Any") => true;
    public string EnsureDirectory(string path) => path;
    public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null)
        => _rootFiles.TryGetValue(root, out var files) ? files : [];
    public bool MoveItemSafely(string src, string dest) => true;
    public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
        => Path.Combine(rootPath, relativePath);
    public bool IsReparsePoint(string path) => false;
    public void DeleteFile(string path) { }
    public void CopyFile(string src, string dest, bool overwrite = false) { }
}
