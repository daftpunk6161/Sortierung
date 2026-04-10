using System.Text.Json;
using Romulus.Api;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Core.Deduplication;
using Romulus.Core.GameKeys;
using Romulus.Core.Regions;
using Romulus.Core.Scoring;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class Issue9InvariantRegressionRedPhaseTests : IDisposable
{
    private readonly string _tempDir;

    public Issue9InvariantRegressionRedPhaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Issue9Inv_" + Guid.NewGuid().ToString("N"));
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
            // best effort cleanup
        }
    }

    [Fact]
    public void Should_MatchGoldenGameKeys_When_Normalized_Issue9_INV01()
    {
        var entries = LoadGolden<GoldenPair>("inv01-gamekey-golden.json");

        foreach (var entry in entries)
            Assert.Equal(entry.Expected, GameKeyNormalizer.Normalize(entry.Input));
    }

    [Fact]
    public void Should_SelectDeterministicWinner_When_Deduplicating_Issue9_INV02()
    {
        var winner = Candidate("g-us.zip", "game-a", "US", 1000, FileCategory.Game);
        var loser = Candidate("g-eu.zip", "game-a", "EU", 900, FileCategory.Game);

        var run1 = DeduplicationEngine.Deduplicate(new[] { winner, loser });
        var run2 = DeduplicationEngine.Deduplicate(new[] { winner, loser });

        Assert.Single(run1);
        Assert.Single(run2);
        Assert.Equal(run1[0].Winner.MainPath, run2[0].Winner.MainPath);
    }

    [Fact]
    public void Should_MatchGoldenRegions_When_Detected_Issue9_INV03()
    {
        var entries = LoadGolden<GoldenPair>("inv03-region-golden.json");

        foreach (var entry in entries)
            Assert.Equal(entry.Expected, RegionDetector.GetRegionTag(entry.Input));
    }

    [Fact]
    public void Should_KeepScoringDeterministic_When_SameInputRepeated_Issue9_INV04()
    {
        var versionScorer = new VersionScorer();
        var f1 = FormatScorer.GetFormatScore(".chd");
        var f2 = FormatScorer.GetFormatScore(".chd");
        var v1 = versionScorer.GetVersionScore("Game (Rev A)");
        var v2 = versionScorer.GetVersionScore("Game (Rev A)");

        Assert.Equal(f1, f2);
        Assert.Equal(v1, v2);
    }

    [Fact]
    public void Should_KeepPreviewExecuteParity_When_DryRunVsMove_Issue9_INV05_P04()
    {
        var dryRoot = Path.Combine(_tempDir, "dry");
        var moveRoot = Path.Combine(_tempDir, "move");
        Directory.CreateDirectory(dryRoot);
        Directory.CreateDirectory(moveRoot);

        SeedDataset(dryRoot);
        SeedDataset(moveRoot);

        var fs = new FileSystemAdapter();

        var dry = new RunOrchestrator(fs, new NullAuditStore()).Execute(new RunOptions
        {
            Roots = new[] { dryRoot },
            Mode = "DryRun",
            Extensions = new[] { ".zip" },
            PreferRegions = new[] { "US", "EU", "JP", "WORLD" }
        });

        var move = new RunOrchestrator(fs, new NullAuditStore()).Execute(new RunOptions
        {
            Roots = new[] { moveRoot },
            Mode = "Move",
            Extensions = new[] { ".zip" },
            PreferRegions = new[] { "US", "EU", "JP", "WORLD" }
        });

        Assert.Equal(dry.GroupCount, move.GroupCount);
        Assert.Equal(dry.WinnerCount, move.WinnerCount);
        Assert.Equal(dry.LoserCount, move.LoserCount);
    }

    [Fact]
    public void Should_KeepProjectionAdditiveInvariant_When_CalculatingKpis_Issue9_INV06_P03()
    {
        var run = new RunResult
        {
            TotalFilesScanned = 5,
            UnknownCount = 1,
            FilteredNonGameCount = 1,
            DedupeGroups = new[]
            {
                new DedupeGroup
                {
                    GameKey = "g",
                    Winner = Candidate("winner.zip", "g", "US", 1000, FileCategory.Game),
                    Losers = new[]
                    {
                        Candidate("loser.zip", "g", "EU", 900, FileCategory.Game)
                    }
                }
            },
            AllCandidates = new[]
            {
                Candidate("winner.zip", "g", "US", 1000, FileCategory.Game),
                Candidate("loser.zip", "g", "EU", 900, FileCategory.Game),
                Candidate("junk.zip", "j", "UNKNOWN", 0, FileCategory.Junk),
                Candidate("bios.bin", "b", "UNKNOWN", 0, FileCategory.Bios),
                Candidate("unknown.zip", "u", "UNKNOWN", 0, FileCategory.Unknown)
            }
        };

        var projection = RunProjectionFactory.Create(run);
        var accounted = projection.Keep + projection.Dupes + projection.Junk + projection.Unknown + projection.FilteredNonGameCount;

        Assert.Equal(projection.TotalFiles, accounted);
    }

    [Fact]
    public void Should_BlockMoveOutsideRoot_When_UsingSafeFilesystem_Issue9_INV07()
    {
        var fs = new FileSystemAdapter();
        var root = Path.Combine(_tempDir, "root");
        Directory.CreateDirectory(root);

        var escaped = fs.ResolveChildPathWithinRoot(root, Path.Combine("..", "outside", "moved.zip"));
        Assert.Null(escaped);

        var safe = fs.ResolveChildPathWithinRoot(root, Path.Combine("_TRASH_REGION_DEDUPE", "moved.zip"));
        Assert.NotNull(safe);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[!]")]
    [InlineData("(USA)")]
    public void Should_NeverReturnEmptyGameKey_When_NormalizingDegenerateInput_Issue9_INV08(string input)
    {
        var key = GameKeyNormalizer.Normalize(input);
        Assert.False(string.IsNullOrWhiteSpace(key));
    }

    [Fact]
    public void Should_WriteOneAuditRowPerMoveOrSkip_When_MovePhaseExecutes_Issue9_INV09()
    {
        var root = Path.Combine(_tempDir, "audit-move");
        Directory.CreateDirectory(root);

        var moveFile = Path.Combine(root, "move.zip");
        var skipFile = Path.Combine(root, "skip.zip");
        File.WriteAllText(moveFile, "x");
        File.WriteAllText(skipFile, "x");

        var fs = new TestFileSystem();
        fs.MoveResults[moveFile] = Path.Combine(root, "_TRASH_REGION_DEDUPE", "move.zip");

        var existingSkipDest = Path.Combine(root, "_TRASH_REGION_DEDUPE", "skip.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(existingSkipDest)!);
        File.WriteAllText(existingSkipDest, "exists");

        var audit = new TrackingAuditStore();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            ConflictPolicy = "Skip",
            AuditPath = Path.Combine(_tempDir, "audit.csv")
        };

        var group = new DedupeGroup
        {
            GameKey = "g",
            Winner = Candidate(Path.Combine(root, "winner.zip"), "g", "US", 1000, FileCategory.Game),
            Losers = new[]
            {
                Candidate(moveFile, "g", "EU", 900, FileCategory.Game),
                Candidate(skipFile, "g", "JP", 800, FileCategory.Game)
            }
        };

        var result = new MovePipelinePhase().Execute(
            new MovePhaseInput(new[] { group }, options),
            CreateContext(options, fs, audit),
            CancellationToken.None);

        // TASK-147: Write-ahead pattern: each successful move writes 2 rows (MOVE_PENDING + Move),
        // each Skip writes 1 row. So total = MoveCount * 2 + SkipCount.
        Assert.Equal(result.MoveCount * 2 + result.SkipCount, audit.Rows.Count);
    }

    [Fact]
    public void Should_AllowRollbackOnlyWithinAllowedRoots_When_ReplayingAudit_Issue9_INV10()
    {
        var audit = new AuditCsvStore(keyFilePath: Path.Combine(_tempDir, "audit.key"));
        var csvPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllLines(csvPath, new[]
        {
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            @"C:\Other,C:\Other\old.rom,C:\Other\new.rom,Move,GAME,abc,dedupe,2025-01-01"
        });

        var restored = audit.Rollback(csvPath, new[] { _tempDir }, new[] { _tempDir }, dryRun: true);

        Assert.Empty(restored);
    }

    [Theory]
    [InlineData("=SUM(A1:A2)")]
    [InlineData("+calc")]
    [InlineData("-1+2")]
    [InlineData("@cmd")]
    [InlineData("\tformula")]
    [InlineData("\rformula")]
    public void Should_PreventCsvInjection_When_SanitizingAuditFields_Issue9_INV11(string payload)
    {
        // BUG-14: RFC-4180 quoting — dangerous values wrapped in double quotes
        var sanitized = AuditSigningService.SanitizeCsvField(payload);
        Assert.StartsWith("\"", sanitized, StringComparison.Ordinal);
        Assert.EndsWith("\"", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_BuildEquivalentRunOptionsAcrossEntryPoints_When_SameIntentGiven_Issue9_INV12_P01()
    {
        var root = Path.Combine(_tempDir, "roots");
        var datRoot = Path.Combine(_tempDir, "dat");
        var trashRoot = Path.Combine(_tempDir, "trash");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(datRoot);
        Directory.CreateDirectory(trashRoot);

        var cli = new CliRunOptions
        {
            Roots = new[] { root },
            Mode = "Move",
            PreferRegions = new[] { "EU", "US", "WORLD", "JP" },
            ExtensionsExplicit = true,
            Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip" },
            RemoveJunk = true,
            AggressiveJunk = false,
            SortConsole = true,
            EnableDat = true,
            DatRoot = datRoot,
            HashType = "sha1",
            ConvertFormat = "auto",
            ConvertOnly = false,
            ConflictPolicy = "Rename",
            TrashRoot = trashRoot
        };

        var cliSettings = new RomulusSettings();
        var (cliRunOptions, errors) = CliOptionsMapper.Map(cli, cliSettings);
        Assert.Null(errors);
        Assert.NotNull(cliRunOptions);

        var manager = new RunManager(new FileSystemAdapter(), new AuditCsvStore(keyFilePath: Path.Combine(_tempDir, "api.key")),
            (_, _, _, _) => new RunExecutionOutcome("completed", new ApiRunResult { OrchestratorStatus = "ok", ExitCode = 0 }));

        var apiCreate = manager.TryCreateOrReuse(new RunRequest
        {
            Roots = new[] { root },
            Mode = "Move",
            PreferRegions = new[] { "EU", "US", "WORLD", "JP" },
            Extensions = new[] { ".zip" },
            RemoveJunk = true,
            AggressiveJunk = false,
            SortConsole = true,
            EnableDat = true,
            DatRoot = datRoot,
            HashType = "sha1",
            ConvertFormat = "auto",
            ConvertOnly = false,
            ConflictPolicy = "Rename",
            TrashRoot = trashRoot
        }, "Move", idempotencyKey: "issue9-inv12");

        var api = apiCreate.Run!;

        var vm = CreateViewModel(root);
        vm.DryRun = false;
        vm.RemoveJunk = true;
        vm.AggressiveJunk = false;
        vm.SortConsole = true;
        vm.UseDat = true;
        vm.DatRoot = datRoot;
        vm.DatHashType = "SHA1";
        vm.ConvertEnabled = true;
        vm.ConvertOnly = false;
        vm.ConflictPolicy = ConflictPolicy.Rename;
        vm.TrashRoot = trashRoot;

        var (_, wpfRunOptions, _, _) = new RunService().BuildOrchestrator(vm);

        var cliFingerprint = ToFingerprint(cliRunOptions!);
        var apiFingerprint = ToFingerprint(api);
        var wpfFingerprint = ToFingerprint(wpfRunOptions);

        Assert.Equal(cliFingerprint, apiFingerprint);
        Assert.Equal(apiFingerprint, wpfFingerprint);
    }

    [Fact]
    public void Should_MatchRunResultGolden_When_FixedDatasetExecuted_Issue9_P02()
    {
        var root = Path.Combine(_tempDir, "p02-runresult");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Mega Game (US).zip"), "us");
        File.WriteAllText(Path.Combine(root, "Mega Game (EU).zip"), "eu");

        var run = new RunOrchestrator(new FileSystemAdapter(), new NullAuditStore()).Execute(new RunOptions
        {
            Roots = new[] { root },
            Mode = "DryRun",
            Extensions = new[] { ".zip" },
            PreferRegions = new[] { "US", "EU", "JP", "WORLD" }
        });

        var goldenPath = Path.Combine(GetRepoRoot(), "src", "Romulus.Tests", "Snapshots", "inv02-runresult-golden.json");
        Assert.True(File.Exists(goldenPath));

        using var golden = JsonDocument.Parse(File.ReadAllText(goldenPath));
        var expectedStatus = golden.RootElement.GetProperty("Status").GetString();
        var expectedGroups = golden.RootElement.GetProperty("ExpectedGroups").GetInt32();
        var expectedWinners = golden.RootElement.GetProperty("ExpectedWinners").GetInt32();
        var expectedLosers = golden.RootElement.GetProperty("ExpectedLosers").GetInt32();

        Assert.Equal(expectedStatus, run.Status);
        Assert.Equal(expectedGroups, run.GroupCount);
        Assert.Equal(expectedWinners, run.WinnerCount);
        Assert.Equal(expectedLosers, run.LoserCount);
    }

    [Fact]
    public async Task Should_KeepApiStatusLifecycleConsistent_When_CancelRetryResumeRequested_Issue9_StatusInvariants()
    {
        var manager = new RunManager(new FileSystemAdapter(), new AuditCsvStore(keyFilePath: Path.Combine(_tempDir, "status.key")),
            (_, _, _, ct) =>
            {
                try
                {
                    Task.Delay(120, ct).GetAwaiter().GetResult();
                    return new RunExecutionOutcome("completed", new ApiRunResult { OrchestratorStatus = "ok", ExitCode = 0 });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            });

        var run = manager.TryCreateOrReuse(new RunRequest { Roots = new[] { _tempDir }, Mode = "Move" }, "Move", idempotencyKey: "issue9-status").Run!;

        var cancelResult = manager.Cancel(run.RunId);
        Assert.True(cancelResult.Disposition is RunCancelDisposition.Accepted or RunCancelDisposition.NoOp);

        var waited = await manager.WaitForCompletion(run.RunId, timeout: TimeSpan.FromSeconds(2));
        Assert.True(waited.Run!.Status is "cancelled" or "completed" or "failed");
        Assert.False(waited.Run.ResumeSupported);
        Assert.True(waited.Run.CanRetry);
    }

    [Fact]
    public void Should_KeepConvertCountsInvariant_When_ConversionResultProjected_Issue9_P03_ConvertCounts()
    {
        var result = new RunResult
        {
            ConvertedCount = 2,
            ConvertErrorCount = 1,
            ConvertSkippedCount = 3,
            TotalFilesScanned = 10,
            AllCandidates = Array.Empty<RomCandidate>()
        };

        var projection = RunProjectionFactory.Create(result);
        var total = projection.ConvertedCount + projection.ConvertErrorCount + projection.ConvertSkippedCount;

        Assert.Equal(6, total);
    }

    [Fact]
    public void Should_AvoidDuplicateEnumeration_When_RootsOverlap_Issue9_P04_OverlappingRoots()
    {
        var root = Path.Combine(_tempDir, "root");
        var child = Path.Combine(root, "child");
        Directory.CreateDirectory(child);
        var file = Path.Combine(child, "Game (US).zip");
        File.WriteAllText(file, "x");

        var options = new RunOptions
        {
            Roots = new[] { root, child },
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
        };

        var result = new ScanPipelinePhase().Execute(options, CreateContext(options, new FileSystemAdapter(), new NullAuditStore()), CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public void Should_ProvideRoundtripRollback_When_MoveAuditExists_Issue9_P05_RestoreRollback()
    {
        var audit = new AuditCsvStore(keyFilePath: Path.Combine(_tempDir, "rollback.key"));
        var csv = Path.Combine(_tempDir, "rollback.csv");

        var oldPath = Path.Combine(_tempDir, "original", "game.rom");
        var newPath = Path.Combine(_tempDir, "moved", "game.rom");
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.WriteAllText(newPath, "payload");

        File.WriteAllLines(csv, new[]
        {
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_tempDir},{oldPath},{newPath},Move,GAME,abc,dedupe,2025-01-01"
        });

        audit.WriteMetadataSidecar(csv, new Dictionary<string, object> { ["Mode"] = "Move" });

        var restored = audit.Rollback(csv, new[] { _tempDir }, new[] { _tempDir }, dryRun: false);

        Assert.Single(restored);
        Assert.True(File.Exists(oldPath));
        Assert.False(File.Exists(newPath));
    }

    [Fact]
    public void Should_ExposeAuditMetadataConsistency_When_SidecarTampered_Issue9_P06_AuditConsistency()
    {
        var audit = new AuditCsvStore(keyFilePath: Path.Combine(_tempDir, "tamper.key"));
        var csv = Path.Combine(_tempDir, "tamper.csv");

        File.WriteAllLines(csv, new[]
        {
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_tempDir},{Path.Combine(_tempDir, "a")},{Path.Combine(_tempDir, "b")},Move,GAME,abc,dedupe,2025-01-01"
        });

        audit.WriteMetadataSidecar(csv, new Dictionary<string, object> { ["Mode"] = "Move" });
        File.AppendAllText(csv, "tampered\n");

        Assert.False(audit.TestMetadataSidecar(csv));
    }

    [Fact]
    public void Should_LoadRunResultGoldenSnapshot_When_CheckingRegression_Issue9_P07_SnapshotGolden()
    {
        var goldenFile = Path.Combine(GetRepoRoot(), "src", "Romulus.Tests", "Snapshots", "inv02-runresult-golden.json");
        Assert.True(File.Exists(goldenFile));

        using var doc = JsonDocument.Parse(File.ReadAllText(goldenFile));
        Assert.True(doc.RootElement.TryGetProperty("Status", out _));
        Assert.True(doc.RootElement.TryGetProperty("ExpectedGroups", out _));
    }

    [Fact]
    public void Should_HaveSharedTestFixturesFolder_When_M01ConsolidationDone_Issue9_M01()
    {
        var fixturesPath = Path.Combine(GetRepoRoot(), "src", "Romulus.Tests", "TestFixtures");
        Assert.True(Directory.Exists(fixturesPath));
    }

    [Fact]
    public void Should_HaveSnapshotSuiteFiles_When_M02SnapshotsReady_Issue9_M02()
    {
        var snapshots = new[]
        {
            "inv01-gamekey-golden.json",
            "inv02-runresult-golden.json",
            "inv03-region-golden.json",
            "inv04-score-golden.json",
            "inv05-classification-golden.json"
        };

        foreach (var snapshot in snapshots)
        {
            var path = Path.Combine(GetRepoRoot(), "src", "Romulus.Tests", "Snapshots", snapshot);
            Assert.True(File.Exists(path), $"Missing snapshot: {snapshot}");
        }
    }

    [Fact]
    public void Should_HandleNullInjectableDependenciesWithoutCrash_When_M03NullInjectionGates_Issue9_M03()
    {
        var root = Path.Combine(_tempDir, "m03-null");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Game (US).zip"), "x");

        var options = new RunOptions
        {
            Roots = new[] { root },
            Mode = "Move",
            Extensions = new[] { ".zip" },
            ConvertFormat = "auto",
            SortConsole = true,
            EnableDat = true
        };

        var orchestrator = new RunOrchestrator(new FileSystemAdapter(), new NullAuditStore(),
            consoleDetector: null,
            hashService: null,
            converter: null,
            datIndex: null);

        var ex = Record.Exception(() => orchestrator.Execute(options));
        Assert.Null(ex);
    }

    [Fact]
    public void Should_ContainExpandedPipelineIsolationScenarios_When_M04PhaseIsolationExpanded_Issue9_M04()
    {
        var file = Path.Combine(GetRepoRoot(), "src", "Romulus.Tests", "PipelinePhaseIsolationTests.cs");
        Assert.True(File.Exists(file));
        var content = File.ReadAllText(file);

        Assert.Contains("DatIndex", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Verify", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FindRoot", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_ContainAuditRoundtripScenarios_When_M05RoundtripAuditExpanded_Issue9_M05()
    {
        var file = Path.Combine(GetRepoRoot(), "src", "Romulus.Tests", "AuditCsvStoreTests.cs");
        Assert.True(File.Exists(file));
        var content = File.ReadAllText(file);

        Assert.Contains("Rollback_DryRun_DoesNotMoveFiles", content, StringComparison.Ordinal);
        Assert.Contains("Rollback_ActualMove_RestoresFile", content, StringComparison.Ordinal);
        Assert.Contains("TestMetadataSidecar_ReturnsFalseIfTampered", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_RequireNoDirectFileIOInCoreSetParsers_When_M06Done_Issue9_M06()
    {
        var coreSetParsers = new[]
        {
            Path.Combine(GetRepoRoot(), "src", "Romulus.Core", "SetParsing", "CueSetParser.cs"),
            Path.Combine(GetRepoRoot(), "src", "Romulus.Core", "SetParsing", "GdiSetParser.cs"),
            Path.Combine(GetRepoRoot(), "src", "Romulus.Core", "SetParsing", "CcdSetParser.cs"),
            Path.Combine(GetRepoRoot(), "src", "Romulus.Core", "SetParsing", "M3uPlaylistParser.cs"),
            Path.Combine(GetRepoRoot(), "src", "Romulus.Core", "SetParsing", "MdsSetParser.cs")
        };

        foreach (var parserPath in coreSetParsers)
        {
            var content = File.ReadAllText(parserPath);
            Assert.DoesNotContain("File.", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Directory.", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Should_HaveUnifiedRunOptionsBuilder_When_M07Done_Issue9_M07()
    {
        var type = Type.GetType("Romulus.Infrastructure.Orchestration.RunOptionsBuilder, Romulus.Infrastructure");
        Assert.NotNull(type);
    }

    [Fact]
    public void Should_UseSharedTestDoubleInfrastructure_When_M08ConsolidationDone_Issue9_M08()
    {
        var fixturesPath = Path.Combine(GetRepoRoot(), "src", "Romulus.Tests", "TestFixtures");
        Assert.True(Directory.Exists(fixturesPath));

        var files = Directory.GetFiles(fixturesPath, "*.cs", SearchOption.TopDirectoryOnly);
        Assert.Contains(files, p => p.EndsWith("InMemoryFileSystem.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, p => p.EndsWith("TrackingAuditStore.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, p => p.EndsWith("ConfigurableConverter.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Should_RemoveCoverageBoostFiles_When_M09CleanupDone_Issue9_M09()
    {
        var testsDir = Path.Combine(GetRepoRoot(), "src", "Romulus.Tests");
        var boostFiles = Directory.GetFiles(testsDir, "CoverageBoostPhase*Tests.cs", SearchOption.TopDirectoryOnly);
        Assert.Empty(boostFiles);
    }

    private static string GetRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        if (callerPath is not null)
        {
            dir = new DirectoryInfo(Path.GetDirectoryName(callerPath)!);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private T[] LoadGolden<T>(string fileName)
    {
        var path = Path.Combine(GetRepoRoot(), "src", "Romulus.Tests", "Snapshots", fileName);
        Assert.True(File.Exists(path), $"Golden file missing: {fileName}");

        var json = File.ReadAllText(path);
        var items = JsonSerializer.Deserialize<T[]>(json);
        Assert.NotNull(items);
        return items!;
    }

    private static void SeedDataset(string root)
    {
        File.WriteAllText(Path.Combine(root, "Mega Game (US).zip"), "us");
        File.WriteAllText(Path.Combine(root, "Mega Game (EU).zip"), "eu");
        File.WriteAllText(Path.Combine(root, "Another Game (JP).zip"), "jp");
    }

    private static MainViewModel CreateViewModel(string root)
    {
        var vm = new MainViewModel(new StubThemeService(), new StubDialogService());
        vm.Roots.Add(root);
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferWORLD = true;
        vm.PreferJP = true;
        foreach (var filter in vm.ExtensionFilters)
            filter.IsChecked = string.Equals(filter.Extension, ".zip", StringComparison.OrdinalIgnoreCase);
        return vm;
    }

    private static PipelineContext CreateContext(RunOptions options, IFileSystem fileSystem, IAuditStore auditStore)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        return new PipelineContext
        {
            Options = options,
            FileSystem = fileSystem,
            AuditStore = auditStore,
            Metrics = metrics,
            OnProgress = _ => { }
        };
    }

    private static RomCandidate Candidate(string path, string gameKey, string region, int regionScore, FileCategory category)
    {
        return new RomCandidate
        {
            MainPath = path,
            GameKey = gameKey,
            Region = region,
            RegionScore = regionScore,
            FormatScore = 100,
            VersionScore = 100,
            HeaderScore = 0,
            CompletenessScore = 0,
            SizeTieBreakScore = 0,
            SizeBytes = 1024,
            Extension = Path.GetExtension(path),
            ConsoleKey = "PSX",
            DatMatch = false,
            Category = category,
            ClassificationReasonCode = "test",
            ClassificationConfidence = 100
        };
    }

    private static RunOptionsFingerprint ToFingerprint(RunOptions options)
    {
        return new RunOptionsFingerprint(
            Mode: options.Mode,
            PreferRegions: string.Join("|", options.PreferRegions),
            Extensions: string.Join("|", options.Extensions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            RemoveJunk: options.RemoveJunk,
            AggressiveJunk: options.AggressiveJunk,
            SortConsole: options.SortConsole,
            EnableDat: options.EnableDat,
            DatRoot: options.DatRoot,
            HashType: options.HashType,
            ConvertFormat: options.ConvertFormat,
            ConvertOnly: options.ConvertOnly,
            ConflictPolicy: options.ConflictPolicy,
            TrashRoot: options.TrashRoot);
    }

    private static RunOptionsFingerprint ToFingerprint(RunRecord record)
    {
        return new RunOptionsFingerprint(
            Mode: record.Mode,
            PreferRegions: string.Join("|", record.PreferRegions),
            Extensions: string.Join("|", record.Extensions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            RemoveJunk: record.RemoveJunk,
            AggressiveJunk: record.AggressiveJunk,
            SortConsole: record.SortConsole,
            EnableDat: record.EnableDat,
            DatRoot: record.DatRoot,
            HashType: record.HashType,
            ConvertFormat: record.ConvertFormat,
            ConvertOnly: record.ConvertOnly,
            ConflictPolicy: record.ConflictPolicy,
            TrashRoot: record.TrashRoot);
    }

    private sealed record RunOptionsFingerprint(
        string Mode,
        string PreferRegions,
        string Extensions,
        bool RemoveJunk,
        bool AggressiveJunk,
        bool SortConsole,
        bool EnableDat,
        string? DatRoot,
        string? HashType,
        string? ConvertFormat,
        bool ConvertOnly,
        string? ConflictPolicy,
        string? TrashRoot);

    private sealed record GoldenPair(string Input, string Expected);

    private sealed class TestFileSystem : IFileSystem
    {
        public Dictionary<string, string> MoveResults { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool TestPath(string literalPath, string pathType = "Any") => true;

        public string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => Array.Empty<string>();

        public string? MoveItemSafely(string sourcePath, string destinationPath)
            => MoveResults.TryGetValue(sourcePath, out var moved) ? moved : null;

        public bool MoveDirectorySafely(string sourcePath, string destinationPath) => true;

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.GetFullPath(Path.Combine(rootPath, relativePath));

        public bool IsReparsePoint(string path) => false;

        public void DeleteFile(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
            => File.Copy(sourcePath, destinationPath, overwrite);
    }

    private sealed class TrackingAuditStore : IAuditStore
    {
        public List<AuditRow> Rows { get; } = new();

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
        {
        }

        public bool TestMetadataSidecar(string auditCsvPath) => true;

        public void Flush(string auditCsvPath)
        {
        }

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "")
            => Rows.Add(new AuditRow(action, oldPath, newPath));
    }

    private sealed record AuditRow(string Action, string OldPath, string NewPath);

    private sealed class NullAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
        {
        }

        public bool TestMetadataSidecar(string auditCsvPath) => true;

        public void Flush(string auditCsvPath)
        {
        }

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "")
        {
        }
    }

    private sealed class StubThemeService : IThemeService
    {
        public AppTheme Current => AppTheme.Dark;
        public bool IsDark => true;
        public IReadOnlyList<AppTheme> AvailableThemes => [AppTheme.Dark];
        public void ApplyTheme(AppTheme theme) { }
        public void ApplyTheme(bool dark) { }
        public void Toggle() { }
    }

    private sealed class StubDialogService : IDialogService
    {
        public string? BrowseFolder(string title = "Ordner auswählen") => null;
        public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*") => null;
        public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null) => null;
        public bool Confirm(string message, string title = "Bestätigung") => true;
        public void Info(string message, string title = "Information") { }
        public void Error(string message, string title = "Fehler") { }
        public ConfirmResult YesNoCancel(string message, string title = "Frage") => ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "") => defaultValue;
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen") => true;
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<Romulus.Contracts.Models.ConversionReviewEntry> entries) => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => true;
    }
}
