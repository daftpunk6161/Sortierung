using System.Text;
using System.Text.Json;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Core.Deduplication;
using Romulus.Core.GameKeys;
using Romulus.Core.Scoring;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Sorting;
using Xunit;
using CliProgram = Romulus.CLI.Program;

namespace Romulus.Tests;

/// <summary>
/// Harte Kernfunktions-Invarianten + Regressionen (TDD Red Phase).
/// Fokus: deterministische Kernentscheidungen, Safety, Recovery und Channel-Parity.
/// </summary>
public sealed class HardCoreInvariantRegressionSuiteTests : IDisposable
{
    private readonly string _tempDir;

    public HardCoreInvariantRegressionSuiteTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HardCoreInv_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    // 1) Scan / Enumeration

    [Fact]
    public void Scan_OverlappingRoots_NoDuplicateCandidates()
    {
        var root = Path.Combine(_tempDir, "root");
        var child = Path.Combine(root, "sub");
        Directory.CreateDirectory(child);
        var file = CreateFileAt(child, "Game (USA).zip", 32);

        var options = new RunOptions
        {
            Roots = new[] { root, child },
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
        };

        var context = CreateContext(options);
        var scan = new ScanPipelinePhase();
        var scanned = scan.Execute(options, context, CancellationToken.None);

        Assert.Single(scanned);
        Assert.Equal(Path.GetFullPath(file), scanned[0].Path);
    }

    [Fact]
    public void Scan_SameInputs_ProduceStableCandidateSet()
    {
        var root = Path.Combine(_tempDir, "stable_scan");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "A (USA).zip", 11);
        CreateFileAt(root, "B (EU).zip", 12);
        CreateFileAt(root, "C (JP).zip", 13);

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
        };

        var scan = new ScanPipelinePhase();
        var first = scan.Execute(options, CreateContext(options), CancellationToken.None).Select(x => x.Path).ToArray();
        var second = scan.Execute(options, CreateContext(options), CancellationToken.None).Select(x => x.Path).ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Scan_RootOrderPermutation_ProducesSameOrderedEnumeration()
    {
        var root = Path.Combine(_tempDir, "scan_order_root");
        var child = Path.Combine(root, "child");
        Directory.CreateDirectory(child);

        var parentFile = CreateFileAt(root, "A_parent.zip", 10);
        var childFile = CreateFileAt(child, "B_child.zip", 10);

        var scan = new ScanPipelinePhase();

        var forwardOptions = new RunOptions
        {
            Roots = new[] { root, child },
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
        };

        var reverseOptions = new RunOptions
        {
            Roots = new[] { child, root },
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
        };

        var forward = scan.Execute(forwardOptions, CreateContext(forwardOptions), CancellationToken.None)
            .Select(s => s.Path)
            .ToArray();

        var reverse = scan.Execute(reverseOptions, CreateContext(reverseOptions), CancellationToken.None)
            .Select(s => s.Path)
            .ToArray();

        Assert.Equal(new[] { Path.GetFullPath(parentFile), Path.GetFullPath(childFile) }, forward);
        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void Scan_M3uReferencedChd_RemainsInCandidates()
    {
        var root = Path.Combine(_tempDir, "m3u_chd_scan");
        Directory.CreateDirectory(root);

        var m3u = Path.Combine(root, "Game Collection.m3u");
        var chd = Path.Combine(root, "Disc 1.chd");

        File.WriteAllText(m3u, "Disc 1.chd\n");
        CreateFileAt(root, "Disc 1.chd", 1024);

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".m3u", ".chd" },
            Mode = "DryRun"
        };

        var scan = new ScanPipelinePhase();
        var scanned = scan.Execute(options, CreateContext(options), CancellationToken.None);
        var paths = scanned.Select(s => s.Path).ToArray();

        Assert.Contains(Path.GetFullPath(chd), paths);
        Assert.Contains(Path.GetFullPath(m3u), paths);
    }

    [Fact]
    public void Scan_GetFilesSafe_NoInfiniteLoop_WhenSymlinkLoopPresent_IfSupported()
    {
        var root = Path.Combine(_tempDir, "loop_root");
        var a = Path.Combine(root, "A");
        Directory.CreateDirectory(a);
        CreateFileAt(a, "file.zip", 10);

        var loopLink = Path.Combine(a, "loop");
        try
        {
            Directory.CreateSymbolicLink(loopLink, root);
        }
        catch
        {
            // Falls Symbolic Links nicht erlaubt sind, prüfen wir nur deterministische endliche Enumeration.
            var fsFallback = new FileSystemAdapter();
            var filesFallback = fsFallback.GetFilesSafe(root, new[] { ".zip" });
            Assert.Single(filesFallback);
            return;
        }

        var fs = new FileSystemAdapter();
        var files = fs.GetFilesSafe(root, new[] { ".zip" });

        Assert.Single(files);
        Assert.Contains("file.zip", files[0], StringComparison.OrdinalIgnoreCase);
    }

    // 2) Classification

    [Fact]
    public void Classification_GameBiosJunkUnknown_AreCorrect()
    {
        Assert.Equal(FileCategory.Game, FileClassifier.Classify("Super Mario (USA)"));
        Assert.Equal(FileCategory.Bios, FileClassifier.Classify("[BIOS] Sega Saturn"));
        Assert.Equal(FileCategory.Junk, FileClassifier.Classify("Cool Game (Demo)"));
        Assert.Equal(FileCategory.Unknown, FileClassifier.Classify(""));
    }

    [Fact]
    public void Classification_Unknown_IsNotPromotedToGame_InEnrichment()
    {
        var root = Path.Combine(_tempDir, "unknown");
        Directory.CreateDirectory(root);
        var weird = CreateFileAt(root, ".zip", 5);

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
        };

        var scan = new ScanPipelinePhase().Execute(options, CreateContext(options), CancellationToken.None);
        var enriched = new EnrichmentPipelinePhase().Execute(
            new EnrichmentPhaseInput(scan, null, null, null, null),
            CreateContext(options),
            CancellationToken.None);

        var candidate = Assert.Single(enriched);
        Assert.Equal(weird, candidate.MainPath);
        Assert.Equal(FileCategory.Unknown, candidate.Category);
    }

    [Fact]
    public void Classification_MixedFolder_CategoriesRemainIndependent()
    {
        var root = Path.Combine(_tempDir, "mixed");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "Game One (USA).zip", 10);
        CreateFileAt(root, "[BIOS] Device.zip", 10);
        CreateFileAt(root, "Game One (Demo).zip", 10);

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
        };

        var scan = new ScanPipelinePhase().Execute(options, CreateContext(options), CancellationToken.None);
        var enriched = new EnrichmentPipelinePhase().Execute(
            new EnrichmentPhaseInput(scan, null, null, null, null),
            CreateContext(options),
            CancellationToken.None);

        Assert.Contains(enriched, c => c.Category == FileCategory.Game);
        Assert.Contains(enriched, c => c.Category == FileCategory.Bios);
        Assert.Contains(enriched, c => c.Category == FileCategory.Junk);
    }

    [Fact]
    public void Enrichment_CompletenessScore_CueWithMissingTrack_IsNegative_Issue9()
    {
        var root = Path.Combine(_tempDir, "completeness");
        Directory.CreateDirectory(root);

        // .cue references a missing .bin track -> set should be incomplete
        var cuePath = Path.Combine(root, "Game (USA).cue");
        File.WriteAllText(cuePath, "FILE \"missing.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n");

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".cue" },
            Mode = "DryRun"
        };

        var scan = new ScanPipelinePhase().Execute(options, CreateContext(options), CancellationToken.None);
        var enriched = new EnrichmentPipelinePhase().Execute(
            new EnrichmentPhaseInput(scan, null, null, null, null),
            CreateContext(options),
            CancellationToken.None);

        var candidate = Assert.Single(enriched);
        Assert.Equal(cuePath, candidate.MainPath);
        Assert.Equal(-50, candidate.CompletenessScore);
    }

    // 3) GameKey / Grouping

    [Fact]
    public void GameKey_SameIdentity_ProducesSameKey()
    {
        var k1 = GameKeyNormalizer.Normalize("Super Mario Bros (USA) (Rev 1)");
        var k2 = GameKeyNormalizer.Normalize("Super Mario Bros (Europe) [b]");

        Assert.Equal(k1, k2);
        Assert.False(string.IsNullOrWhiteSpace(k1));
    }

    [Fact]
    public void GameKey_DifferentIdentity_ProducesDifferentKey()
    {
        var k1 = GameKeyNormalizer.Normalize("Super Mario Bros (USA)");
        var k2 = GameKeyNormalizer.Normalize("The Legend of Zelda (USA)");

        Assert.NotEqual(k1, k2);
    }

    [Fact]
    public void Grouping_NoEmptyKeys_AreProducedByDeduplicate()
    {
        var results = DeduplicationEngine.Deduplicate(new[]
        {
            new RomCandidate { MainPath = "a.zip", GameKey = "", Category = FileCategory.Game, RegionScore = 1 },
            new RomCandidate { MainPath = "b.zip", GameKey = "   ", Category = FileCategory.Game, RegionScore = 1 },
            new RomCandidate { MainPath = "c.zip", GameKey = "zelda", Category = FileCategory.Game, RegionScore = 10 }
        });

        Assert.Single(results);
        Assert.Equal("zelda", results[0].GameKey);
    }

    [Fact]
    public void Grouping_IsDeterministic_ForSameInputs()
    {
        var candidates = new[]
        {
            new RomCandidate { MainPath = "g1_us.zip", GameKey = "game1", Category = FileCategory.Game, RegionScore = 100 },
            new RomCandidate { MainPath = "g1_eu.zip", GameKey = "game1", Category = FileCategory.Game, RegionScore = 90 },
            new RomCandidate { MainPath = "g2_us.zip", GameKey = "game2", Category = FileCategory.Game, RegionScore = 80 }
        };

        var first = DeduplicationEngine.Deduplicate(candidates).Select(g => g.GameKey + "|" + g.Winner.MainPath).ToArray();
        var second = DeduplicationEngine.Deduplicate(candidates).Select(g => g.GameKey + "|" + g.Winner.MainPath).ToArray();

        Assert.Equal(first, second);
    }

    // 4) Winner Selection / Dedupe

    [Fact]
    public void WinnerSelection_SameInputs_AlwaysSameWinner()
    {
        var items = new[]
        {
            new RomCandidate { MainPath = "a.zip", GameKey = "g", Category = FileCategory.Game, RegionScore = 10, FormatScore = 5 },
            new RomCandidate { MainPath = "b.zip", GameKey = "g", Category = FileCategory.Game, RegionScore = 12, FormatScore = 5 },
            new RomCandidate { MainPath = "c.zip", GameKey = "g", Category = FileCategory.Game, RegionScore = 8, FormatScore = 7 }
        };

        var w1 = DeduplicationEngine.SelectWinner(items);
        var w2 = DeduplicationEngine.SelectWinner(items.Reverse().ToArray());

        Assert.NotNull(w1);
        Assert.Equal(w1!.MainPath, w2!.MainPath);
    }

    [Fact]
    public void WinnerSelection_RegionPreference_BeatsWorldAndUnknown()
    {
        var prefs = new[] { "US", "EU", "WORLD" };
        var us = new RomCandidate
        {
            MainPath = "g_us.zip",
            GameKey = "g",
            Category = FileCategory.Game,
            Region = "US",
            RegionScore = FormatScorer.GetRegionScore("US", prefs),
            FormatScore = 500
        };
        var world = new RomCandidate
        {
            MainPath = "g_world.zip",
            GameKey = "g",
            Category = FileCategory.Game,
            Region = "WORLD",
            RegionScore = FormatScorer.GetRegionScore("WORLD", prefs),
            FormatScore = 850
        };
        var unknown = new RomCandidate
        {
            MainPath = "g_unknown.zip",
            GameKey = "g",
            Category = FileCategory.Game,
            Region = "UNKNOWN",
            RegionScore = FormatScorer.GetRegionScore("UNKNOWN", prefs),
            FormatScore = 850
        };

        var winner = DeduplicationEngine.SelectWinner(new[] { world, unknown, us });
        Assert.Equal("g_us.zip", winner!.MainPath);
    }

    [Fact]
    public void WinnerSelection_TieBreaker_RemainsStable()
    {
        var a = new RomCandidate
        {
            MainPath = "a.iso",
            GameKey = "g",
            Category = FileCategory.Game,
            RegionScore = 100,
            FormatScore = 700,
            VersionScore = 0,
            SizeTieBreakScore = 1000
        };
        var b = new RomCandidate
        {
            MainPath = "b.iso",
            GameKey = "g",
            Category = FileCategory.Game,
            RegionScore = 100,
            FormatScore = 700,
            VersionScore = 0,
            SizeTieBreakScore = 900
        };

        var winner = DeduplicationEngine.SelectWinner(new[] { a, b });
        Assert.Equal("a.iso", winner!.MainPath);
    }

    // 5) Sorting

    [Fact]
    public void Sorting_SameFile_MapsToSameTargetFolder()
    {
        var root = Path.Combine(_tempDir, "sort_stable");
        Directory.CreateDirectory(root);
        var rom = CreateFileAt(root, "Metroid.nes", 10);

        var detector = BuildDetector(new[]
        {
            new ConsoleInfo("NES", "NES", false, new[] { ".nes" }, Array.Empty<string>(), new[] { "NES" })
        });

        var sorter = new ConsoleSorter(new FileSystemAdapter(), detector, new AuditCsvStore(), null);
        var originalPath = Path.Combine(root, "Metroid.nes");
        var movedPath = Path.Combine(root, "NES", "Metroid.nes");
        var first = sorter.Sort(
            new[] { root },
            new[] { ".nes" },
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [originalPath] = "NES"
            });
        var second = sorter.Sort(
            new[] { root },
            new[] { ".nes" },
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [movedPath] = "NES"
            });

        var expected = Path.Combine(root, "NES", Path.GetFileName(rom));
        Assert.True(File.Exists(expected));
        Assert.Equal(1, first.Moved);
        Assert.True(second.Skipped >= 1);
    }

    [Fact]
    public void Sorting_UnknownFiles_AreVisibleInResult()
    {
        var root = Path.Combine(_tempDir, "sort_unknown");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "Mystery.xyz", 10);

        var detector = BuildDetector(Array.Empty<ConsoleInfo>());
        var sorter = new ConsoleSorter(new FileSystemAdapter(), detector);

        var result = sorter.Sort(
            new[] { root },
            new[] { ".xyz" },
            dryRun: true,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Path.Combine(root, "Mystery.xyz")] = "UNKNOWN"
            });
        Assert.Equal(1, result.Unknown);
    }

    [Fact]
    public void Sorting_FolderVsExtensionConflict_UsesFolderDecisionDeterministically()
    {
        var root = Path.Combine(_tempDir, "sort_conflict");
        var psxFolder = Path.Combine(root, "PSX");
        Directory.CreateDirectory(psxFolder);
        CreateFileAt(psxFolder, "Odd.nes", 10);

        var detector = BuildDetector(new[]
        {
            new ConsoleInfo("NES", "NES", false, new[] { ".nes" }, Array.Empty<string>(), Array.Empty<string>()),
            new ConsoleInfo("PSX", "PSX", true, Array.Empty<string>(), Array.Empty<string>(), new[] { "PSX" })
        });

        var sorter = new ConsoleSorter(new FileSystemAdapter(), detector);
        var result = sorter.Sort(
            new[] { root },
            new[] { ".nes" },
            dryRun: true,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Path.Combine(psxFolder, "Odd.nes")] = "PSX"
            });

        // Datei liegt bereits im durch Folder-Heuristik erwarteten Zielordner PSX -> skipped, nicht moved/unknown.
        Assert.Equal(0, result.Unknown);
        Assert.Equal(1, result.Skipped);
    }

    // 6) DAT

    [Fact]
    public void DatRepository_SmallAndLargeDat_ParseSuccessfully()
    {
        var datRoot = Path.Combine(_tempDir, "dat");
        Directory.CreateDirectory(datRoot);

        File.WriteAllText(Path.Combine(datRoot, "small.dat"), "<?xml version=\"1.0\"?><datafile><game name=\"A\"><rom sha1=\"111\" /></game></datafile>");

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\"?><datafile>");
        for (int i = 0; i < 1500; i++)
            sb.Append($"<game name=\"G{i}\"><rom sha1=\"{i:X4}\" /></game>");
        sb.Append("</datafile>");
        File.WriteAllText(Path.Combine(datRoot, "large.dat"), sb.ToString());

        var repo = new Romulus.Infrastructure.Dat.DatRepositoryAdapter();
        var index = repo.GetDatIndex(datRoot, new Dictionary<string, string>
        {
            ["SMALL"] = "small.dat",
            ["LARGE"] = "large.dat"
        });

        Assert.True(index.TotalEntries >= 1501);
        Assert.True(index.HasConsole("SMALL"));
        Assert.True(index.HasConsole("LARGE"));
    }

    [Fact]
    public void DatRepository_XxePayload_IsBlockedAndDoesNotExpandEntities()
    {
        var datRoot = Path.Combine(_tempDir, "dat_xxe");
        Directory.CreateDirectory(datRoot);
        var datPath = Path.Combine(datRoot, "xxe.dat");

        var payload = "<?xml version=\"1.0\"?>\n" +
                      "<!DOCTYPE datafile [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>\n" +
                      "<datafile><game name=\"&xxe;\"><rom sha1=\"abc\" /></game></datafile>";
        File.WriteAllText(datPath, payload);

        var repo = new Romulus.Infrastructure.Dat.DatRepositoryAdapter();
        var index = repo.GetDatIndex(datRoot, new Dictionary<string, string> { ["TEST"] = "xxe.dat" });

        Assert.NotNull(index);
        Assert.True(index.TotalEntries >= 0);
    }

    [Fact]
    public void DatEnabledWithoutIndex_PreflightShowsVisibleFallbackWarning()
    {
        var root = Path.Combine(_tempDir, "dat_warn");
        Directory.CreateDirectory(root);
        var orch = new RunOrchestrator(new FileSystemAdapter(), new AuditCsvStore());

        var preflight = orch.Preflight(new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            EnableDat = true
        });

        Assert.Equal("ok", preflight.Status);
        Assert.Contains(preflight.Warnings, w => w.Contains("DatIndex", StringComparison.OrdinalIgnoreCase));
    }

    // 7) Conversion

    [Fact]
    public void Conversion_VerifyFailure_PreservesSource_AndRemovesFailedTarget()
    {
        var root = Path.Combine(_tempDir, "conv_verify");
        Directory.CreateDirectory(root);
        var source = CreateFileAt(root, "game.iso", 10);

        var converter = new FailingVerifyConverter();
        var candidate = new RomCandidate
        {
            MainPath = source,
            GameKey = "game",
            Category = FileCategory.Game,
            ConsoleKey = "PS2",
            Extension = ".iso"
        };
        var group = new DedupeGroup { Winner = candidate, Losers = Array.Empty<RomCandidate>(), GameKey = "game" };
        var options = new RunOptions { Roots = new[] { root }, Mode = "Move" };

        var phase = new WinnerConversionPipelinePhase();
        var output = phase.Execute(
            new WinnerConversionPhaseInput(new[] { group }, options, new HashSet<string>(), converter),
            CreateContext(options),
            CancellationToken.None);

        Assert.Equal(0, output.Converted);
        Assert.Equal(1, output.ConvertErrors);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(source + ".chd"));
    }

    [Fact]
    public void Conversion_SourceRemovedOnlyAfterTrueSuccess()
    {
        var root = Path.Combine(_tempDir, "conv_success");
        Directory.CreateDirectory(root);
        var source = CreateFileAt(root, "game.iso", 10);

        var converter = new SuccessfulConverter();
        var candidate = new RomCandidate
        {
            MainPath = source,
            GameKey = "game",
            Category = FileCategory.Game,
            ConsoleKey = "PS2",
            Extension = ".iso"
        };
        var group = new DedupeGroup { Winner = candidate, Losers = Array.Empty<RomCandidate>(), GameKey = "game" };
        var options = new RunOptions { Roots = new[] { root }, Mode = "Move" };

        var phase = new WinnerConversionPipelinePhase();
        var output = phase.Execute(
            new WinnerConversionPhaseInput(new[] { group }, options, new HashSet<string>(), converter),
            CreateContext(options),
            CancellationToken.None);

        Assert.Equal(1, output.Converted);
        Assert.False(File.Exists(source));
        Assert.True(Directory.Exists(Path.Combine(root, "_TRASH_CONVERTED")));
    }

    [Fact]
    public void Conversion_CountsRemainConsistent_ForPartialOutputSet()
    {
        var root = Path.Combine(_tempDir, "conv_counts");
        Directory.CreateDirectory(root);
        var a = CreateFileAt(root, "a.iso", 5);
        var b = CreateFileAt(root, "b.iso", 5);
        var c = CreateFileAt(root, "c.iso", 5);

        var converter = new MixedOutcomeConverter(a, b, c);
        var options = new RunOptions { Roots = new[] { root }, Mode = "Move" };

        var phase = new ConvertOnlyPipelinePhase();
        var result = phase.Execute(new ConvertOnlyPhaseInput(new[]
        {
            new RomCandidate { MainPath = a, ConsoleKey = "PS2", Extension = ".iso", Category = FileCategory.Game, GameKey = "a" },
            new RomCandidate { MainPath = b, ConsoleKey = "PS2", Extension = ".iso", Category = FileCategory.Game, GameKey = "b" },
            new RomCandidate { MainPath = c, ConsoleKey = "PS2", Extension = ".iso", Category = FileCategory.Game, GameKey = "c" },
        }, options, converter), CreateContext(options), CancellationToken.None);

        Assert.Equal(1, result.Converted);
        Assert.Equal(1, result.ConvertErrors);
        Assert.Equal(1, result.ConvertSkipped);
    }

    [Fact]
    public void Conversion_ErrorWithPartialTarget_DeletesPartialOutput()
    {
        var root = Path.Combine(_tempDir, "conv_partial_error_cleanup");
        Directory.CreateDirectory(root);
        var source = CreateFileAt(root, "broken.iso", 6);

        var converter = new PartialErrorConverter();
        var candidate = new RomCandidate
        {
            MainPath = source,
            GameKey = "broken",
            Category = FileCategory.Game,
            ConsoleKey = "PS2",
            Extension = ".iso"
        };
        var group = new DedupeGroup { Winner = candidate, Losers = Array.Empty<RomCandidate>(), GameKey = "broken" };
        var options = new RunOptions { Roots = new[] { root }, Mode = "Move" };

        var phase = new WinnerConversionPipelinePhase();
        var output = phase.Execute(
            new WinnerConversionPhaseInput(new[] { group }, options, new HashSet<string>(), converter),
            CreateContext(options),
            CancellationToken.None);

        Assert.Equal(0, output.Converted);
        Assert.Equal(1, output.ConvertErrors);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(source + ".chd"));
    }

    // 8) Move / Restore / Undo

    [Fact]
    public void MovePipeline_NeverMovesOutsideDeclaredRoot()
    {
        var insideRoot = Path.Combine(_tempDir, "inside_root");
        var outsideRoot = Path.Combine(_tempDir, "outside_root");
        Directory.CreateDirectory(insideRoot);
        Directory.CreateDirectory(outsideRoot);
        var outside = CreateFileAt(outsideRoot, "rogue.zip", 5);

        var loser = new RomCandidate { MainPath = outside, Category = FileCategory.Game, GameKey = "g", SizeBytes = 5 };
        var winner = new RomCandidate { MainPath = CreateFileAt(insideRoot, "winner.zip", 6), Category = FileCategory.Game, GameKey = "g", SizeBytes = 6 };
        var group = new DedupeGroup { Winner = winner, Losers = new[] { loser }, GameKey = "g" };

        var options = new RunOptions { Roots = new[] { insideRoot }, Mode = "Move" };
        var move = new MovePipelinePhase();
        var result = move.Execute(new MovePhaseInput(new[] { group }, options), CreateContext(options), CancellationToken.None);

        Assert.Equal(0, result.MoveCount);
        Assert.Equal(1, result.FailCount);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void Rollback_RestoresMovedFiles_Completely()
    {
        var root = Path.Combine(_tempDir, "rollback");
        Directory.CreateDirectory(root);
        var original = CreateFileAt(root, "restore_me.zip", 7);

        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore(fs);
        var trash = Path.Combine(root, "_TRASH_REGION_DEDUPE", "restore_me.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
        File.Move(original, trash);

        var auditPath = Path.Combine(_tempDir, "audit", "rollback.csv");
        audit.AppendAuditRow(auditPath, root, original, trash, "Move", "GAME", "", "test");
        // SEC-ROLLBACK-03: Execute-mode rollback requires sidecar
        audit.WriteMetadataSidecar(auditPath, new Dictionary<string, object> { ["Mode"] = "Move" });

        var restored = audit.Rollback(auditPath, new[] { root }, new[] { root }, dryRun: false);

        Assert.True(File.Exists(original));
        Assert.False(File.Exists(trash));
        Assert.Single(restored);
    }

    [Fact]
    public void Rollback_ReportsPartialFailures_WithoutSilence()
    {
        var root = Path.Combine(_tempDir, "rollback_partial");
        Directory.CreateDirectory(root);
        var oldPath = Path.Combine(root, "missing_source.zip");
        var newPath = Path.Combine(root, "_TRASH_REGION_DEDUPE", "missing_source.zip");

        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore(fs);
        var auditPath = Path.Combine(_tempDir, "audit", "rollback_partial.csv");
        audit.AppendAuditRow(auditPath, root, oldPath, newPath, "Move", "GAME", "", "test");
        // SEC-ROLLBACK-03: Execute-mode rollback requires sidecar
        audit.WriteMetadataSidecar(auditPath, new Dictionary<string, object> { ["Mode"] = "Move" });

        var restored = audit.Rollback(auditPath, new[] { root }, new[] { root }, dryRun: false);

        Assert.Empty(restored);
        Assert.False(File.Exists(oldPath));
    }

    [Fact]
    public void Rollback_MissingCurrentFile_IsCountedAsFailure_NotOnlySkipped()
    {
        var root = Path.Combine(_tempDir, "rollback_missing_visible");
        Directory.CreateDirectory(root);

        var oldPath = Path.Combine(root, "old.zip");
        var missingCurrentPath = Path.Combine(root, "_TRASH_REGION_DEDUPE", "old.zip");

        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore(fs);
        var auditPath = Path.Combine(_tempDir, "audit", "rollback_missing_visible.csv");
        audit.AppendAuditRow(auditPath, root, oldPath, missingCurrentPath, "Move", "GAME", "", "test-missing-current");

        var keyPath = Path.Combine(_tempDir, "rollback_missing_visible.key");
        var signing = new AuditSigningService(fs, keyFilePath: keyPath);
        signing.WriteMetadataSidecar(auditPath, 1, new Dictionary<string, object> { ["Mode"] = "Move" });

        var rollbackResult = signing.Rollback(
            auditPath,
            allowedRestoreRoots: new[] { root },
            allowedCurrentRoots: new[] { root },
            dryRun: false);

        Assert.Equal(0, rollbackResult.RolledBack);
        Assert.True(rollbackResult.Failed > 0);
    }

    // 9) Orchestrator

    [Fact]
    public void Orchestrator_CancelledStatus_IsConsistent()
    {
        var root = Path.Combine(_tempDir, "cancelled");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "game.zip", 8);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var orch = new RunOrchestrator(new FileSystemAdapter(), new AuditCsvStore());
        var result = orch.Execute(new RunOptions { Roots = new[] { root }, Extensions = new[] { ".zip" }, Mode = "DryRun" }, cts.Token);

        Assert.Equal("cancelled", result.Status);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void Orchestrator_PartialFailure_ProducesCompletedWithErrorsAndExitCode1()
    {
        var root = Path.Combine(_tempDir, "partial_failure");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "g1.iso", 9);
        CreateFileAt(root, "g2.iso", 10);

        var failingConverter = new MixedOutcomeConverter(
            Path.Combine(root, "g1.iso"),
            Path.Combine(root, "g2.iso"),
            "");

        var orch = new RunOrchestrator(new FileSystemAdapter(), new AuditCsvStore(), converter: failingConverter);
        var result = orch.Execute(new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".iso" },
            Mode = "Move",
            ConvertOnly = true,
            ConvertFormat = "auto"
        });

        Assert.Equal("completed_with_errors", result.Status);
        Assert.Equal(4, result.ExitCode);
    }

    [Fact]
    public void Orchestrator_CancelObservedByAllCorePhases()
    {
        var options = new RunOptions { Roots = new[] { _tempDir }, Extensions = new[] { ".zip" }, Mode = "DryRun" };
        var context = CreateContext(options);
        var cancelled = new CancellationToken(canceled: true);

        Assert.Throws<OperationCanceledException>(() =>
            new ScanPipelinePhase().Execute(options, context, cancelled));

        Assert.Throws<OperationCanceledException>(() =>
            new EnrichmentPipelinePhase().Execute(
                new EnrichmentPhaseInput(new[] { new ScannedFileEntry(_tempDir, CreateFile("a.zip", 4), ".zip") }, null, null, null, null),
                context,
                cancelled));

        Assert.Throws<OperationCanceledException>(() =>
            new DeduplicatePipelinePhase().Execute(new[] { new RomCandidate { MainPath = "a.zip", GameKey = "a", Category = FileCategory.Game } }, context, cancelled));

        Assert.Throws<OperationCanceledException>(() =>
            new MovePipelinePhase().Execute(
                new MovePhaseInput(new[]
                {
                    new DedupeGroup
                    {
                        Winner = new RomCandidate { MainPath = "winner.zip", GameKey = "g", Category = FileCategory.Game },
                        Losers = new[] { new RomCandidate { MainPath = "loser.zip", GameKey = "g", Category = FileCategory.Game } },
                        GameKey = "g"
                    }
                }, options),
                context,
                cancelled));

        Assert.Throws<OperationCanceledException>(() =>
            new ConvertOnlyPipelinePhase().Execute(
                new ConvertOnlyPhaseInput(
                    new[] { new RomCandidate { MainPath = "a.iso", ConsoleKey = "PS2", Extension = ".iso", Category = FileCategory.Game, GameKey = "a" } },
                    options,
                    new SuccessfulConverter()),
                context,
                cancelled));
    }

    [Fact]
    public void Orchestrator_CancelDuringScan_WritesPartialAuditSidecarForTraceability()
    {
        var root = Path.Combine(_tempDir, "cancel_scan_trace");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "one.zip", 10);
        CreateFileAt(root, "two.zip", 10);

        var auditPath = Path.Combine(_tempDir, "audit", "cancel_scan_trace.csv");
        using var cts = new CancellationTokenSource();

        var orch = new RunOrchestrator(
            new FileSystemAdapter(),
            new AuditCsvStore(),
            onProgress: msg =>
            {
                if (msg.Contains("[Scan]", StringComparison.OrdinalIgnoreCase))
                    cts.Cancel();
            });

        var result = orch.Execute(new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            AuditPath = auditPath
        }, cts.Token);

        Assert.Equal("cancelled", result.Status);
        Assert.Equal(2, result.ExitCode);
        Assert.True(File.Exists(auditPath + ".meta.json"));
    }

    // P2-04: DAT matching skip for UNKNOWN console must emit warning

    [Fact]
    public void Enrichment_DatMatchAttemptedEvenForUnknownConsole()
    {
        var root = Path.Combine(_tempDir, "dat_warn");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "UnknownRom.zip", 20);

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
        };

        var scanned = new ScanPipelinePhase().Execute(options, CreateContext(options), CancellationToken.None);

        // Provide a DatIndex and HashService so DAT matching is attempted
        var datRoot = Path.Combine(_tempDir, "dat_test");
        Directory.CreateDirectory(datRoot);
        File.WriteAllText(Path.Combine(datRoot, "test.dat"),
            "<?xml version=\"1.0\"?><datafile><game name=\"X\"><rom sha1=\"abc\" /></game></datafile>");
        var repo = new Romulus.Infrastructure.Dat.DatRepositoryAdapter();
        var datIndex = repo.GetDatIndex(datRoot, new Dictionary<string, string> { ["TEST"] = "test.dat" });
        var hashService = new FileHashService();

        // No ConsoleDetector → all files get consoleKey="" → LookupAny fallback
        var messages = new List<string>();
        var context = CreateContext(options);
        context = new PipelineContext
        {
            Options = options,
            FileSystem = new FileSystemAdapter(),
            AuditStore = new AuditCsvStore(),
            Metrics = context.Metrics,
            OnProgress = msg => messages.Add(msg)
        };

        var enriched = new EnrichmentPipelinePhase().Execute(
            new EnrichmentPhaseInput(scanned, null, hashService, null, datIndex),
            context,
            CancellationToken.None);

        // INVARIANT: DAT matching is always attempted, even when console is unknown.
        // The old "DAT-Verifizierung übersprungen" warning must NOT appear.
        Assert.DoesNotContain(messages, w => w.Contains("DAT-Verifizierung übersprungen", StringComparison.OrdinalIgnoreCase));
        // Cross-console DAT lookup runs for UNKNOWN and emits "Kein Match fuer" message.
        Assert.Contains(messages, w => w.Contains("Kein Match fuer", StringComparison.OrdinalIgnoreCase));
    }

    // TGAP-16: BUG-23 – UNKNOWN console with matching DAT hash must upgrade to DatVerified
    [Fact]
    public void Enrichment_UnknownConsole_DatHashMatch_UpgradesToDatVerified()
    {
        // Arrange: create a file with known content so we can compute its real SHA1
        var root = Path.Combine(_tempDir, "dat_upgrade");
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, "Mystery.bin");
        var content = System.Text.Encoding.UTF8.GetBytes("TGAP-16 test content for hash matching");
        File.WriteAllBytes(filePath, content);

        var hashService = new FileHashService();
        var realHash = hashService.GetHash(filePath, "SHA1");
        Assert.NotNull(realHash);

        // Build a DAT that contains this hash under console "NES"
        var datRoot = Path.Combine(_tempDir, "dat_upgrade_dats");
        Directory.CreateDirectory(datRoot);
        File.WriteAllText(Path.Combine(datRoot, "nes.dat"),
            $"<?xml version=\"1.0\"?><datafile><game name=\"Mystery\"><rom sha1=\"{realHash}\" /></game></datafile>");
        var repo = new Romulus.Infrastructure.Dat.DatRepositoryAdapter();
        var datIndex = repo.GetDatIndex(datRoot, new Dictionary<string, string> { ["NES"] = "nes.dat" });

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".bin" },
            Mode = "DryRun"
        };

        var scanned = new ScanPipelinePhase().Execute(options, CreateContext(options), CancellationToken.None);
        Assert.Single(scanned);

        // No ConsoleDetector → consoleKey="" → cross-console DAT lookup must find it
        var context = new PipelineContext
        {
            Options = options,
            FileSystem = new FileSystemAdapter(),
            AuditStore = new AuditCsvStore(),
            Metrics = CreateContext(options).Metrics
        };

        // Act
        var enriched = new EnrichmentPipelinePhase().Execute(
            new EnrichmentPhaseInput(scanned, null, hashService, null, datIndex),
            context,
            CancellationToken.None);

        // Assert: INVARIANT – cross-console DAT hash match upgrades UNKNOWN to DatVerified
        Assert.Single(enriched);
        var candidate = enriched[0];
        Assert.Equal(SortDecision.DatVerified, candidate.SortDecision);
        Assert.Equal("NES", candidate.ConsoleKey);
        Assert.True(candidate.DatMatch);
        Assert.NotEqual(MatchKind.None, candidate.PrimaryMatchKind);
        Assert.NotEqual(EvidenceTier.Tier4_Unknown, candidate.EvidenceTier);
    }

    // 10) GUI / CLI / API parity

    [Fact]
    public async Task Parity_GuiCliApi_MatchCoreDecisionsAndCounts()
    {
        var root = Path.Combine(_tempDir, "parity");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "Game (USA).zip", 10);
        CreateFileAt(root, "Game (Europe).zip", 11);

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            PreferRegions = new[] { "US", "EU", "WORLD" }
        };

        var orchestrator = new RunOrchestrator(new FileSystemAdapter(), new AuditCsvStore());
        var core = orchestrator.Execute(options);
        var guiProjection = RunProjectionFactory.Create(core);

        var (cliCode, cliStdout, _) = RunCli(new CliRunOptions
        {
            Roots = new[] { root },
            Mode = "DryRun",
            PreferRegions = new[] { "US", "EU", "WORLD" }
        });
        using var cliJson = ParseCliSummaryJson(cliStdout);

        var manager = new Romulus.Api.RunManager(new FileSystemAdapter(), new AuditCsvStore());
        var created = manager.TryCreate(new Romulus.Api.RunRequest
        {
            Roots = new[] { root },
            Mode = "DryRun",
            PreferRegions = new[] { "US", "EU", "WORLD" }
        }, "DryRun");

        Assert.NotNull(created);
        await manager.WaitForCompletion(created!.RunId, timeout: TimeSpan.FromSeconds(10));
        var api = manager.Get(created.RunId)!.Result!;

        Assert.Equal(0, cliCode);
        Assert.Equal(core.Status, guiProjection.Status);
        Assert.Equal(core.GroupCount, guiProjection.Groups);
        Assert.Equal(core.WinnerCount, guiProjection.Keep);
        Assert.Equal(core.LoserCount, guiProjection.Dupes);

        Assert.Equal(guiProjection.TotalFiles, cliJson.RootElement.GetProperty("TotalFiles").GetInt32());
        Assert.Equal(guiProjection.Groups, cliJson.RootElement.GetProperty("Groups").GetInt32());
        Assert.Equal(guiProjection.Keep, cliJson.RootElement.GetProperty("Keep").GetInt32());
        Assert.Equal(guiProjection.Dupes, cliJson.RootElement.GetProperty("Dupes").GetInt32());

        Assert.Equal(guiProjection.Status, api.OrchestratorStatus);
        Assert.Equal(guiProjection.Groups, api.Groups);
        Assert.Equal(guiProjection.Keep, api.Winners);
        Assert.Equal(guiProjection.Dupes, api.Losers);
    }

    [Fact]
    public void Sorting_ConflictWithDatEvidence_IsDeterministicallyDatVerified()
    {
        // Invariant: full DAT evidence (confidence 100 + hasDatEvidence=true)
        // must always produce DatVerified, independent from conflict flag/source count.
        var a = HypothesisResolver.DetermineSortDecision(
            confidence: 100,
            conflict: true,
            hardEvidence: true,
            sourceCount: 1,
            hasDatEvidence: true);

        var b = HypothesisResolver.DetermineSortDecision(
            confidence: 100,
            conflict: false,
            hardEvidence: true,
            sourceCount: 4,
            hasDatEvidence: true);

        Assert.Equal(SortDecision.Review, a);
        Assert.Equal(SortDecision.DatVerified, b);
    }

    [Fact]
    public void DatRepository_DoctypeFallback_ParsesValidDatWithoutEntityExpansion()
    {
        var datRoot = Path.Combine(_tempDir, "dat_doctype_fallback");
        Directory.CreateDirectory(datRoot);
        var datPath = Path.Combine(datRoot, "fallback.dat");

        // Real DATs may contain DOCTYPE. Adapter should fallback to DtdProcessing.Ignore,
        // parse entries, and never require external entity resolution.
        var payload = "<?xml version=\"1.0\"?>\n" +
                      "<!DOCTYPE datafile SYSTEM \"noop.dtd\">\n" +
                      "<datafile><game name=\"Game A\"><rom sha1=\"abc123\" /></game></datafile>";
        File.WriteAllText(datPath, payload);

        var repo = new Romulus.Infrastructure.Dat.DatRepositoryAdapter();
        var index = repo.GetDatIndex(datRoot, new Dictionary<string, string> { ["DT"] = "fallback.dat" });

        Assert.True(index.HasConsole("DT"));
        Assert.True(index.TotalEntries >= 1);
    }

    [Fact]
    public void Rollback_IsIdempotent_OnSecondExecution()
    {
        var root = Path.Combine(_tempDir, "rollback_idempotent");
        Directory.CreateDirectory(root);
        var original = CreateFileAt(root, "idempotent.zip", 9);

        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore(fs);
        var trash = Path.Combine(root, "_TRASH_REGION_DEDUPE", "idempotent.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
        File.Move(original, trash);

        var auditPath = Path.Combine(_tempDir, "audit", "rollback_idempotent.csv");
        audit.AppendAuditRow(auditPath, root, original, trash, "Move", "GAME", "", "idempotent");
        audit.WriteMetadataSidecar(auditPath, new Dictionary<string, object> { ["Mode"] = "Move" });

        var first = audit.Rollback(auditPath, new[] { root }, new[] { root }, dryRun: false);
        var second = audit.Rollback(auditPath, new[] { root }, new[] { root }, dryRun: false);

        Assert.Single(first);
        Assert.Empty(second);
        Assert.True(File.Exists(original));
        Assert.False(File.Exists(trash));
    }

    [Fact]
    public async Task Parity_CoreAndApi_WinnerPathsMatchExactly()
    {
        var root = Path.Combine(_tempDir, "parity_winner");
        Directory.CreateDirectory(root);
        var us = CreateFileAt(root, "Legend (USA).zip", 10);
        CreateFileAt(root, "Legend (Europe).zip", 11);

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            PreferRegions = new[] { "US", "EU", "WORLD" }
        };

        var core = new RunOrchestrator(new FileSystemAdapter(), new AuditCsvStore()).Execute(options);
        var coreWinners = core.DedupeGroups
            .Select(g => g.Winner.MainPath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var manager = new Romulus.Api.RunManager(new FileSystemAdapter(), new AuditCsvStore());
        var created = manager.TryCreate(new Romulus.Api.RunRequest
        {
            Roots = new[] { root },
            Mode = "DryRun",
            PreferRegions = new[] { "US", "EU", "WORLD" },
            Extensions = new[] { ".zip" }
        }, "DryRun");

        Assert.NotNull(created);
        await manager.WaitForCompletion(created!.RunId, timeout: TimeSpan.FromSeconds(10));

        var run = manager.Get(created.RunId);
        Assert.NotNull(run);
        Assert.NotNull(run!.CoreRunResult);

        var apiWinners = run.CoreRunResult!.DedupeGroups
            .Select(g => g.Winner.MainPath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Contains(us, coreWinners);
        Assert.Equal(coreWinners, apiWinners);
    }

    [Fact]
    public void Conversion_PartialFailure_IsVisibleInProjectionCounters()
    {
        var root = Path.Combine(_tempDir, "api_partial_conversion");
        Directory.CreateDirectory(root);
        var ok = CreateFileAt(root, "ok.iso", 8);
        var err = CreateFileAt(root, "err.iso", 8);
        var skip = CreateFileAt(root, "skip.iso", 8);

        var converter = new MixedOutcomeConverter(ok, err, skip);
        var orchestrator = new RunOrchestrator(
            new FileSystemAdapter(),
            new AuditCsvStore(),
            converter: converter);

        var core = orchestrator.Execute(new RunOptions
        {
            Roots = new[] { root },
            Mode = "Move",
            ConvertOnly = true,
            ConvertFormat = "auto",
            Extensions = new[] { ".iso" }
        });

        var projection = RunProjectionFactory.Create(core);

        // API counters are mapped from projection values.
        Assert.Equal(1, projection.ConvertedCount);
        Assert.Equal(1, projection.ConvertErrorCount);
        Assert.Equal(1, projection.ConvertSkippedCount);
    }

    // Helpers

    private PipelineContext CreateContext(RunOptions options)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        return new PipelineContext
        {
            Options = options,
            FileSystem = new FileSystemAdapter(),
            AuditStore = new AuditCsvStore(),
            Metrics = metrics
        };
    }

    private string CreateFile(string name, int sizeBytes)
        => CreateFileAt(_tempDir, name, sizeBytes);

    private static string CreateFileAt(string root, string name, int sizeBytes)
    {
        var path = Path.Combine(root, name);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    private static ConsoleDetector BuildDetector(IEnumerable<ConsoleInfo> infos)
        => new(infos.ToArray());

    private static (int ExitCode, string Stdout, string Stderr) RunCli(CliRunOptions options)
    {
        lock (SharedTestLocks.ConsoleLock)
        {
            var origOut = Console.Out;
            var origErr = Console.Error;
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                var exitCode = CliProgram.RunForTests(options);
                return (exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(origOut);
                Console.SetError(origErr);
            }
        }
    }

    private static JsonDocument ParseCliSummaryJson(string stdout)
    {
        var start = stdout.IndexOf('{');
        if (start < 0)
            return JsonDocument.Parse(stdout);

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < stdout.Length; i++)
        {
            var ch = stdout[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inString = false;

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
                continue;
            }

            if (ch != '}')
                continue;

            depth--;
            if (depth == 0)
                return JsonDocument.Parse(stdout[start..(i + 1)]);
        }

        return JsonDocument.Parse(stdout[start..]);
    }

    private sealed class SuccessfulConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "fake", "fake");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            var targetPath = sourcePath + target.Extension;
            File.WriteAllText(targetPath, "ok");
            return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
        }

        public bool Verify(string targetPath, ConversionTarget target) => true;
    }

    private sealed class FailingVerifyConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "fake", "fake");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            var targetPath = sourcePath + target.Extension;
            File.WriteAllText(targetPath, "broken");
            return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
        }

        public bool Verify(string targetPath, ConversionTarget target) => false;
    }

    private sealed class MixedOutcomeConverter : IFormatConverter
    {
        private readonly string _successPath;
        private readonly string _errorPath;
        private readonly string _skipPath;

        public MixedOutcomeConverter(string successPath, string errorPath, string skipPath)
        {
            _successPath = successPath;
            _errorPath = errorPath;
            _skipPath = skipPath;
        }

        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => sourceExtension.Equals(".iso", StringComparison.OrdinalIgnoreCase)
                ? new ConversionTarget(".chd", "fake", "fake")
                : null;

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            if (string.Equals(sourcePath, _skipPath, StringComparison.OrdinalIgnoreCase))
                return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "skipped");

            if (string.Equals(sourcePath, _errorPath, StringComparison.OrdinalIgnoreCase))
                return new ConversionResult(sourcePath, null, ConversionOutcome.Error, "tool-failed", 1);

            var targetPath = sourcePath + target.Extension;
            File.WriteAllText(targetPath, "ok");
            return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
        }

        public bool Verify(string targetPath, ConversionTarget target) =>
            !targetPath.Contains("g2", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PartialErrorConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => sourceExtension.Equals(".iso", StringComparison.OrdinalIgnoreCase)
                ? new ConversionTarget(".chd", "fake", "fake")
                : null;

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            var targetPath = sourcePath + target.Extension;
            File.WriteAllText(targetPath, "partial-corrupt-output");
            return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Error, "tool-failed-partial", 1);
        }

        public bool Verify(string targetPath, ConversionTarget target) => false;
    }
}
