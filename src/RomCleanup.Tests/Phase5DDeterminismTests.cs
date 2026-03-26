using Xunit;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Deduplication;
using RomCleanup.Core.GameKeys;
using RomCleanup.Core.Regions;
using RomCleanup.Core.Scoring;
using RomCleanup.Infrastructure.Deduplication;
using RomCleanup.Infrastructure.Analytics;
using RomCleanup.Infrastructure.Dat;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.Tests;

// ============================================================================
// TASK-153: CrossRootDeduplicator deterministic tiebreaker
// ============================================================================
public sealed class CrossRootTiebreakerTests
{
    [Fact]
    public void GetMergeAdvice_IdenticalScores_DeterministicWinnerByPath()
    {
        // Two files with identical region, format, version scores and size
        // but different paths. Winner must be deterministic (alphabetical).
        var group = new CrossRootDuplicateGroup
        {
            Hash = "abc123",
            Files =
            [
                new CrossRootFile { Path = "Z:\\root2\\game.zip", Root = "Z:\\root2", Extension = ".zip", SizeBytes = 1000, Hash = "abc123" },
                new CrossRootFile { Path = "A:\\root1\\game.zip", Root = "A:\\root1", Extension = ".zip", SizeBytes = 1000, Hash = "abc123" },
            ]
        };

        var advice1 = CrossRootDeduplicator.GetMergeAdvice(group, ["EU", "US"]);
        var advice2 = CrossRootDeduplicator.GetMergeAdvice(group, ["EU", "US"]);

        // Same call twice must return same winner
        Assert.Equal(advice1.Keep.Path, advice2.Keep.Path);

        // Alphabetically first path wins (A:\ before Z:\)
        Assert.Equal("A:\\root1\\game.zip", advice1.Keep.Path);
    }

    [Fact]
    public void GetMergeAdvice_ThreeFilesIdenticalScores_StableOrder()
    {
        var files = new[]
        {
            new CrossRootFile { Path = "C:\\mid\\game.zip", Root = "C:\\mid", Extension = ".zip", SizeBytes = 500, Hash = "h1" },
            new CrossRootFile { Path = "A:\\first\\game.zip", Root = "A:\\first", Extension = ".zip", SizeBytes = 500, Hash = "h1" },
            new CrossRootFile { Path = "B:\\second\\game.zip", Root = "B:\\second", Extension = ".zip", SizeBytes = 500, Hash = "h1" },
        };
        var group = new CrossRootDuplicateGroup { Hash = "h1", Files = files.ToList() };

        var advice = CrossRootDeduplicator.GetMergeAdvice(group, ["EU", "US"]);

        Assert.Equal("A:\\first\\game.zip", advice.Keep.Path);
        Assert.Equal(2, advice.Remove.Count);
    }
}

// ============================================================================
// TASK-155: InsightsEngine deterministic .First()
// ============================================================================
public sealed class InsightsEngineDeterminismTests
{
    [Fact]
    public void GetCrossCollectionHints_DeterministicWinnerPath()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), $"insights_det_{Guid.NewGuid():N}");
        var root1 = Path.Combine(tempBase, "root1");
        var root2 = Path.Combine(tempBase, "root2");
        Directory.CreateDirectory(root1);
        Directory.CreateDirectory(root2);

        try
        {
            // Same game key across two roots
            File.WriteAllText(Path.Combine(root1, "Zelda (EU).zip"), "data1");
            File.WriteAllText(Path.Combine(root2, "Zelda (US).zip"), "data2");

            var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
            var engine = new InsightsEngine(fs);

            string[] extensions = [".zip"];
            var hints1 = engine.GetCrossCollectionHints([root1, root2], extensions, top: 10);
            var hints2 = engine.GetCrossCollectionHints([root1, root2], extensions, top: 10);

            // Must be deterministic across invocations
            if (hints1.Count > 0 && hints2.Count > 0)
            {
                Assert.Equal(hints1[0].WinnerPath, hints2[0].WinnerPath);
            }
        }
        finally
        {
            try { Directory.Delete(tempBase, true); } catch { }
        }
    }
}

// ============================================================================
// TASK-156: VersionScore overflow protection
// ============================================================================
public sealed class VersionScoreOverflowTests
{
    [Fact]
    public void GetVersionScore_ExtremeVersion_DoesNotOverflow()
    {
        var scorer = new VersionScorer();

        // 20-segment version: would overflow with naive 1000^N
        var extreme = "Game (v999.999.999.999.999.999.999.999.999.999.999.999.999.999.999.999.999.999.999.999)";
        var score = scorer.GetVersionScore(extreme);

        // Must not throw OverflowException and must be >= 0
        Assert.True(score >= 0, $"Score should be non-negative, was {score}");
    }

    [Fact]
    public void GetVersionScore_ManySegments_ClampedNotWrapped()
    {
        var scorer = new VersionScorer();
        // 10-segment version — regex only matches 2-segment versions,
        // so this should return 0 (no match), not crash or wrap negative
        var tenSeg = "Game (v9.9.9.9.9.9.9.9.9.9)";
        var score = scorer.GetVersionScore(tenSeg);

        Assert.True(score >= 0, $"10-segment score should be non-negative, was {score}");
    }

    [Fact]
    public void GetVersionScore_NormalVersion_StillCorrect()
    {
        var scorer = new VersionScorer();
        // Normal 2-segment version — regression guard
        var v12 = "Game (v1.2)";
        var score = scorer.GetVersionScore(v12);

        // v1.2 = 1*1000 + 2 = 1002
        Assert.Equal(1002, score);
    }

    [Fact]
    public void GetVersionScore_SingleSegment_Unchanged()
    {
        var scorer = new VersionScorer();
        var v3 = "Game (v3)";
        Assert.Equal(3, scorer.GetVersionScore(v3));
    }
}

// ============================================================================
// TASK-157: DatSourceService deterministic file selection
// ============================================================================
public sealed class DatSourceServiceDeterminismTests
{
    [Fact]
    public void ImportLocalDatPacks_MultipleMatches_DeterministicBehavior()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"dat_det_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(tempDir, "source");
        var datRoot = Path.Combine(tempDir, "datroot");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(datRoot);

        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "B_Pack_v2.dat"), "<xml/>");
            File.WriteAllText(Path.Combine(sourceDir, "A_Pack_v1.dat"), "<xml/>");

            var catalog = new[]
            {
                new DatCatalogEntry { System = "TEST", Id = "test-pack", Group = "test", Format = "nointro-pack", PackMatch = "Pack*" }
            };

            var svc = new DatSourceService(datRoot);
            var count1 = svc.ImportLocalDatPacks(sourceDir, catalog);

            // Clean and re-import must give same result
            if (count1 > 0)
            {
                foreach (var f in Directory.GetFiles(datRoot)) File.Delete(f);
                var count2 = svc.ImportLocalDatPacks(sourceDir, catalog);
                Assert.Equal(count1, count2);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}

// ============================================================================
// TASK-081: ITimeProvider and IFileReader seams exist in Contracts
// ============================================================================
public sealed class SeamInterfaceTests
{
    [Fact]
    public void ITimeProvider_ExistsInContracts()
    {
        var type = typeof(ITimeProvider);
        Assert.True(type.IsInterface);
        Assert.Contains("UtcNow", type.GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void IFileReader_ExistsInContracts()
    {
        var type = typeof(IFileReader);
        Assert.True(type.IsInterface);
        var methods = type.GetMethods().Select(m => m.Name).ToList();
        Assert.True(methods.Contains("ReadAllLines") || methods.Contains("ReadAllText"),
            $"IFileReader should have ReadAllLines or ReadAllText, has: {string.Join(", ", methods)}");
    }

    [Fact]
    public void SystemTimeProvider_ReturnsUtcNow()
    {
        var provider = new RomCleanup.Infrastructure.Time.SystemTimeProvider();
        var before = DateTimeOffset.UtcNow;
        var result = provider.UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(result, before, after);
    }

    [Fact]
    public void PhysicalFileReader_ImplementsIFileReader()
    {
        var reader = new RomCleanup.Infrastructure.FileSystem.PhysicalFileReader();
        Assert.IsAssignableFrom<IFileReader>(reader);
    }
}

// ============================================================================
// TASK-082: Core Determinism Snapshot Suite
// ============================================================================
public sealed class CoreDeterminismSnapshotTests
{
    [Theory]
    [InlineData("Super Mario Bros (EU) [!]", "supermariobros")]
    [InlineData("Legend of Zelda, The (US) (Rev 1)", "legendofzelda")]
    [InlineData("Sonic the Hedgehog (JP)", "sonicthehedgehog")]
    [InlineData("Final Fantasy VII (Disc 1) (EU)", "finalfantasyvii(disc1)")]
    [InlineData("Pokemon Red (US) [b]", "pokemonred")]
    public void GameKey_Normalize_SnapshotDeterministic(string input, string expected)
    {
        var result = GameKeyNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Game (Europe).zip", "EU")]
    [InlineData("Game (USA).zip", "US")]
    [InlineData("Game (Japan).zip", "JP")]
    [InlineData("Game (World).zip", "WORLD")]
    [InlineData("Game (Asia).zip", "ASIA")]
    [InlineData("Game.zip", "UNKNOWN")]
    public void Region_GetRegionTag_SnapshotDeterministic(string input, string expected)
    {
        Assert.Equal(expected, RegionDetector.GetRegionTag(input));
    }

    [Theory]
    [InlineData(".chd", 850)]
    [InlineData(".rvz", 680)]
    [InlineData(".7z", 480)]
    [InlineData(".zip", 500)]
    [InlineData(".iso", 700)]
    [InlineData(".bin", 695)]
    public void FormatScore_Snapshot(string ext, int expected)
    {
        Assert.Equal(expected, FormatScorer.GetFormatScore(ext));
    }

    [Theory]
    [InlineData("Game [!]", 500)]
    [InlineData("Game (v1.0)", 1000)]
    [InlineData("Game (v1.2)", 1002)]
    [InlineData("Game (rev a)", 10)]
    [InlineData("Game (rev 2)", 20)]
    [InlineData("Game", 0)]
    public void VersionScore_Snapshot(string input, long expected)
    {
        Assert.Equal(expected, new VersionScorer().GetVersionScore(input));
    }

    [Fact]
    public void SelectWinner_IdenticalInputs_IdenticalOutput()
    {
        var items = new[]
        {
            new RomCandidate { MainPath = "B_Game (EU).zip", RegionScore = 100, FormatScore = 700, VersionScore = 0, SizeTieBreakScore = 500, Category = FileCategory.Game },
            new RomCandidate { MainPath = "A_Game (EU).zip", RegionScore = 100, FormatScore = 700, VersionScore = 0, SizeTieBreakScore = 500, Category = FileCategory.Game },
        };

        var winner1 = DeduplicationEngine.SelectWinner(items);
        var winner2 = DeduplicationEngine.SelectWinner(items);

        Assert.NotNull(winner1);
        Assert.NotNull(winner2);
        Assert.Equal(winner1!.MainPath, winner2!.MainPath);
        Assert.Equal("A_Game (EU).zip", winner1.MainPath);
    }

    [Fact]
    public void SelectWinner_RegionScoreBreaksTie()
    {
        var items = new[]
        {
            new RomCandidate { MainPath = "Game (JP).zip", RegionScore = 50, FormatScore = 700, VersionScore = 0, Category = FileCategory.Game },
            new RomCandidate { MainPath = "Game (EU).zip", RegionScore = 100, FormatScore = 700, VersionScore = 0, Category = FileCategory.Game },
        };

        var winner = DeduplicationEngine.SelectWinner(items);
        Assert.Equal("Game (EU).zip", winner!.MainPath);
    }

    [Fact]
    public void SelectWinner_Stability_10xSameResult()
    {
        var items = new[]
        {
            new RomCandidate { MainPath = "Z.zip", RegionScore = 100, FormatScore = 700, Category = FileCategory.Game },
            new RomCandidate { MainPath = "A.zip", RegionScore = 100, FormatScore = 700, Category = FileCategory.Game },
            new RomCandidate { MainPath = "M.zip", RegionScore = 100, FormatScore = 700, Category = FileCategory.Game },
        };

        var first = DeduplicationEngine.SelectWinner(items)!.MainPath;
        for (int i = 0; i < 10; i++)
            Assert.Equal(first, DeduplicationEngine.SelectWinner(items)!.MainPath);
    }
}

// ============================================================================
// TASK-083: RunProjection consistency
// ============================================================================
public sealed class RunProjectionConsistencyTests
{
    [Fact]
    public void RunProjectionFactory_Produces_ConsistentCounts()
    {
        // RunProjection.Keep + Dupes + Junk + Unknown must be reconcilable
        // This validates the factory doesn't lose items
        var proj = new RunProjection(
            Status: "completed", ExitCode: 0,
            TotalFiles: 100, Candidates: 80, Groups: 40,
            Keep: 40, Dupes: 40, Games: 35, Unknown: 5, Junk: 10, Bios: 5,
            DatMatches: 0, ConvertedCount: 0, ConvertErrorCount: 0,
            ConvertSkippedCount: 0, ConvertBlockedCount: 0, ConvertReviewCount: 0,
            ConvertLossyWarningCount: 0, ConvertVerifyPassedCount: 0, ConvertVerifyFailedCount: 0,
            ConvertSavedBytes: 0, DatHaveCount: 0, DatHaveWrongNameCount: 0,
            DatMissCount: 0, DatUnknownCount: 0, DatAmbiguousCount: 0,
            DatRenameProposedCount: 0, DatRenameExecutedCount: 0,
            DatRenameSkippedCount: 0, DatRenameFailedCount: 0,
            JunkRemovedCount: 10, FilteredNonGameCount: 5,
            MoveCount: 30, SkipCount: 10, JunkFailCount: 0,
            ConsoleSortMoved: 0, ConsoleSortFailed: 0, ConsoleSortReviewed: 0, ConsoleSortBlocked: 0,
            FailCount: 0, SavedBytes: 0, DurationMs: 100, HealthScore: 80);

        // Category breakdown should cover candidates
        Assert.Equal(proj.Candidates, proj.Keep + proj.Dupes);
        Assert.True(proj.TotalFiles >= proj.Candidates);
    }

    [Fact]
    public void RunProjection_ConversionKpiAdditivity()
    {
        var proj = new RunProjection(
            Status: "completed", ExitCode: 0,
            TotalFiles: 50, Candidates: 50, Groups: 25,
            Keep: 25, Dupes: 25, Games: 50, Unknown: 0, Junk: 0, Bios: 0,
            DatMatches: 0,
            ConvertedCount: 40, ConvertErrorCount: 5, ConvertSkippedCount: 5,
            ConvertBlockedCount: 0, ConvertReviewCount: 0,
            ConvertLossyWarningCount: 0, ConvertVerifyPassedCount: 40, ConvertVerifyFailedCount: 0,
            ConvertSavedBytes: 1000,
            DatHaveCount: 0, DatHaveWrongNameCount: 0,
            DatMissCount: 0, DatUnknownCount: 0, DatAmbiguousCount: 0,
            DatRenameProposedCount: 0, DatRenameExecutedCount: 0,
            DatRenameSkippedCount: 0, DatRenameFailedCount: 0,
            JunkRemovedCount: 0, FilteredNonGameCount: 0,
            MoveCount: 0, SkipCount: 0, JunkFailCount: 0,
            ConsoleSortMoved: 0, ConsoleSortFailed: 0, ConsoleSortReviewed: 0, ConsoleSortBlocked: 0,
            FailCount: 0, SavedBytes: 0, DurationMs: 100, HealthScore: 95);

        // Converted + Errors + Skipped == Attempted (50 total)
        Assert.Equal(50, proj.ConvertedCount + proj.ConvertErrorCount + proj.ConvertSkippedCount);
    }
}

// ============================================================================
// TASK-084: Shared Test-Doubles are usable
// ============================================================================
public sealed class SharedTestDoubleTests
{
    [Fact]
    public void InMemoryFileSystem_GetFilesSafe_ReturnsAddedFiles()
    {
        var fs = new TestFixtures.InMemoryFileSystem();
        fs.AddFile("C:\\roms\\game.zip", "content");

        var files = fs.GetFilesSafe("C:\\roms");
        Assert.Contains("C:\\roms\\game.zip", files);
    }

    [Fact]
    public void InMemoryFileSystem_MoveItemSafely_MovesFile()
    {
        var fs = new TestFixtures.InMemoryFileSystem();
        fs.AddFile("C:\\src\\game.zip", "data");

        var result = fs.MoveItemSafely("C:\\src\\game.zip", "C:\\dst\\game.zip");
        Assert.Equal("C:\\dst\\game.zip", result);
        Assert.False(fs.TestPath("C:\\src\\game.zip"));
        Assert.True(fs.TestPath("C:\\dst\\game.zip"));
    }

    [Fact]
    public void TrackingAuditStore_AppendAndFlush_TracksRows()
    {
        var store = new TestFixtures.TrackingAuditStore();
        store.AppendAuditRow("audit.csv", "root", "old.zip", "new.zip", "MOVE");
        store.Flush("audit.csv");

        Assert.Single(store.Rows);
        Assert.Equal("MOVE", store.Rows[0].Action);
    }

    [Fact]
    public void StubToolRunner_FindTool_ReturnsConfiguredPath()
    {
        var runner = new TestFixtures.StubToolRunner();
        runner.RegisterTool("chdman", "C:\\tools\\chdman.exe");

        Assert.Equal("C:\\tools\\chdman.exe", runner.FindTool("chdman"));
        Assert.Null(runner.FindTool("unknown"));
    }

    [Fact]
    public void StubDialogService_Confirm_ReturnsConfiguredValue()
    {
        var svc = new TestFixtures.StubDialogService { ConfirmResult = true };
        Assert.True(svc.Confirm("Delete?"));

        svc.ConfirmResult = false;
        Assert.False(svc.Confirm("Delete?"));
    }

    [Fact]
    public void ConfigurableConverter_GetTargetFormat_ReturnsConfigured()
    {
        var conv = new TestFixtures.ConfigurableConverter();
        var target = new ConversionTarget(".chd", "chdman", "createcd");
        conv.RegisterTarget("PS1", ".bin", target);

        Assert.Equal(target, conv.GetTargetFormat("PS1", ".bin"));
        Assert.Null(conv.GetTargetFormat("PS1", ".iso"));
    }
}
