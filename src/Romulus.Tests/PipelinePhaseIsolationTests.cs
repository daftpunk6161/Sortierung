using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using System.Collections.Concurrent;
using Xunit;

namespace Romulus.Tests;

public sealed class PipelinePhaseIsolationTests : IDisposable
{
    private readonly string _tempDir;

    public PipelinePhaseIsolationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PipelineIso_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void FindRootForPath_DoesNotMatchPrefixCollision()
    {
        var roots = new[] { @"C:\Roms", @"C:\Other" };

        var matched = PipelinePhaseHelpers.FindRootForPath(@"C:\Roms-Other\game.zip", roots);

        Assert.Null(matched);
    }

    [Fact]
    public void FindRootForPath_ReturnsContainingRoot()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "sub", "game.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");

        var matched = PipelinePhaseHelpers.FindRootForPath(path, new[] { root });

        Assert.Equal(root, matched);
    }

    [Fact]
    public void ScanPhase_RemovesReferencedCueSetMembers_AndBlocklistedPaths()
    {
        var root = Path.Combine(_tempDir, "scan");
        var trashDir = Path.Combine(root, "_TRASH_REGION_DEDUPE");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(trashDir);

        var cuePath = Path.Combine(root, "disc.cue");
        var binPath = Path.Combine(root, "track01.bin");
        var zipPath = Path.Combine(root, "other.zip");
        var blocklisted = Path.Combine(trashDir, "old.zip");

        File.WriteAllText(cuePath, "FILE \"track01.bin\" BINARY");
        File.WriteAllText(binPath, "track");
        File.WriteAllText(zipPath, "zip");
        File.WriteAllText(blocklisted, "trash");

        var fs = new TestFileSystem();
        fs.SetFiles(root, cuePath, binPath, zipPath, blocklisted);

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".cue", ".bin", ".zip" }
        };

        var phase = new ScanPipelinePhase();
        var result = phase.Execute(options, CreateContext(options, fs), CancellationToken.None);

        Assert.Contains(result, e => e.Path == Path.GetFullPath(cuePath));
        Assert.Contains(result, e => e.Path == Path.GetFullPath(zipPath));
        Assert.DoesNotContain(result, e => e.Path == Path.GetFullPath(binPath));
        Assert.DoesNotContain(result, e => e.Path == Path.GetFullPath(blocklisted));
    }

    [Fact]
    public void ScanPhase_CueOnlyExtensions_ExpandsSetMemberExtensions_FindingF04()
    {
        var root = Path.Combine(_tempDir, "scan-f04");
        Directory.CreateDirectory(root);

        var cuePath = Path.Combine(root, "disc.cue");
        var binPath = Path.Combine(root, "track01.bin");

        File.WriteAllText(cuePath, "FILE \"track01.bin\" BINARY");
        File.WriteAllText(binPath, "track");

        var fs = new TestFileSystem();
        fs.SetFiles(root, cuePath, binPath);

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".cue" }
        };

        var phase = new ScanPipelinePhase();
        _ = phase.Execute(options, CreateContext(options, fs), CancellationToken.None);

        Assert.True(fs.AllowedExtensionsByRoot.TryGetValue(root, out var requestedExtensions));
        Assert.Contains(requestedExtensions, ext => string.Equals(ext, ".bin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanPhase_EmitsWarning_WhenFileSystemReportsInaccessiblePaths_FindingF35()
    {
        var root = Path.Combine(_tempDir, "scan-f35");
        Directory.CreateDirectory(root);

        var romPath = Path.Combine(root, "game.zip");
        File.WriteAllText(romPath, "game");

        var fs = new TestFileSystem();
        fs.SetFiles(root, romPath);
        fs.PendingScanWarnings.Add("Skipped inaccessible directory: denied-subfolder");

        var progress = new List<string>();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" }
        };

        var phase = new ScanPipelinePhase();
        _ = phase.Execute(options, CreateContext(options, fs, onProgress: progress.Add), CancellationToken.None);

        Assert.Contains(
            progress,
            message => message.Contains("WARNING", StringComparison.OrdinalIgnoreCase)
                       && message.Contains("denied-subfolder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeduplicatePhase_FiltersOutJunkOnlyGroups_ForGameGroupsOutput()
    {
        var gameUs = Candidate("C:/roms/game (US).zip", "game", "US", 1000, FileCategory.Game);
        var gameEu = Candidate("C:/roms/game (EU).zip", "game", "EU", 900, FileCategory.Game);
        var junk = Candidate("C:/roms/demo.zip", "demo", "UNKNOWN", 100, FileCategory.Junk);

        var options = new RunOptions { Roots = new[] { "C:/roms" }, Extensions = new[] { ".zip" } };
        var phase = new DeduplicatePipelinePhase();

        var result = phase.Execute(new[] { gameUs, gameEu, junk }, CreateContext(options, new TestFileSystem()), CancellationToken.None);

        Assert.Equal(2, result.Groups.Count);
        Assert.Single(result.GameGroups);
        Assert.Equal(1, result.LoserCount);
    }

    [Fact]
    public void MovePhase_ProducesExpectedCounts_AndAuditRows()
    {
        var root = Path.Combine(_tempDir, "move-root");
        Directory.CreateDirectory(root);

        var moveLoser = Path.Combine(root, "move.zip");
        var skipLoser = Path.Combine(root, "skip.zip");
        var failLoser = Path.Combine(_tempDir, "outside", "fail.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(failLoser)!);

        File.WriteAllText(moveLoser, "a");
        File.WriteAllText(skipLoser, "b");
        File.WriteAllText(failLoser, "c");

        var expectedSkipDest = Path.Combine(root, "_TRASH_REGION_DEDUPE", "skip.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(expectedSkipDest)!);
        File.WriteAllText(expectedSkipDest, "already-there");

        var fs = new TestFileSystem();
        fs.MoveResults[moveLoser] = Path.Combine(root, "_TRASH_REGION_DEDUPE", "move.zip");

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
            GameKey = "game",
            Winner = Candidate(Path.Combine(root, "winner.zip"), "game", "US", 1000, FileCategory.Game),
            Losers = new[]
            {
                Candidate(moveLoser, "game", "EU", 900, FileCategory.Game),
                Candidate(skipLoser, "game", "JP", 800, FileCategory.Game),
                Candidate(failLoser, "game", "WORLD", 700, FileCategory.Game)
            }
        };

        var phase = new MovePipelinePhase();
        var result = phase.Execute(
            new MovePhaseInput(new[] { group }, options),
            CreateContext(options, fs, audit),
            CancellationToken.None);

        Assert.Equal(1, result.MoveCount);
        Assert.Equal(1, result.SkipCount);
        Assert.Equal(1, result.FailCount);
        Assert.Equal(group.Losers.Count, result.MoveCount + result.SkipCount + result.FailCount);

        Assert.Contains(audit.Rows, r => r.Action == "MOVE");
        Assert.Contains(audit.Rows, r => r.Action == "SKIP");
    }

    [Fact]
    public void JunkRemovalPhase_RemovesOnlyStandaloneJunkWinners()
    {
        var root = Path.Combine(_tempDir, "junk-root");
        Directory.CreateDirectory(root);

        var junkStandalone = Path.Combine(root, "junk1.zip");
        var junkNotStandalone = Path.Combine(root, "junk2.zip");
        File.WriteAllText(junkStandalone, "junk");
        File.WriteAllText(junkNotStandalone, "junk");

        var fs = new TestFileSystem();
        fs.MoveResults[junkStandalone] = Path.Combine(root, "_TRASH_JUNK", "junk1.zip");
        fs.MoveResults[junkNotStandalone] = Path.Combine(root, "_TRASH_JUNK", "junk2.zip");

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            Mode = "Move"
        };

        var groups = new[]
        {
            new DedupeGroup
            {
                GameKey = "junk-1",
                Winner = Candidate(junkStandalone, "junk-1", "UNKNOWN", 100, FileCategory.Junk),
                Losers = Array.Empty<RomCandidate>()
            },
            new DedupeGroup
            {
                GameKey = "junk-2",
                Winner = Candidate(junkNotStandalone, "junk-2", "UNKNOWN", 100, FileCategory.Junk),
                Losers = new[] { Candidate(Path.Combine(root, "other.zip"), "junk-2", "UNKNOWN", 90, FileCategory.Junk) }
            }
        };

        var phase = new JunkRemovalPipelinePhase();
        var result = phase.Execute(new JunkRemovalPhaseInput(groups, options), CreateContext(options, fs), CancellationToken.None);

        Assert.Equal(1, result.MoveResult.MoveCount);
        Assert.Contains(junkStandalone, result.RemovedPaths);
        Assert.DoesNotContain(junkNotStandalone, result.RemovedPaths);
    }

    [Fact]
    public void JunkRemovalPhase_DoesNotRemoveDescriptorReferencedSetMemberJunk_FindingF18()
    {
        var root = Path.Combine(_tempDir, "junk-setmember-root");
        Directory.CreateDirectory(root);

        var descriptor = Path.Combine(root, "game.cue");
        var member = Path.Combine(root, "track01.bin");
        File.WriteAllText(descriptor, "FILE \"track01.bin\" BINARY");
        File.WriteAllText(member, "member");

        var fs = new TestFileSystem();
        fs.MoveResults[member] = Path.Combine(root, "_TRASH_JUNK", "track01.bin");

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".cue", ".bin" },
            Mode = RunConstants.ModeMove
        };

        var groups = new[]
        {
            new DedupeGroup
            {
                GameKey = "set-descriptor",
                Winner = Candidate(descriptor, "set-descriptor", "UNKNOWN", 100, FileCategory.Game),
                Losers = Array.Empty<RomCandidate>()
            },
            new DedupeGroup
            {
                GameKey = "set-member",
                Winner = Candidate(member, "set-member", "UNKNOWN", 100, FileCategory.Junk),
                Losers = Array.Empty<RomCandidate>()
            }
        };

        var phase = new JunkRemovalPipelinePhase();
        var result = phase.Execute(new JunkRemovalPhaseInput(groups, options), CreateContext(options, fs), CancellationToken.None);

        Assert.Equal(0, result.MoveResult.MoveCount);
        Assert.DoesNotContain(member, result.RemovedPaths);
    }

    [Fact]
    public void ConvertOnlyPhase_TracksConvertedSkippedAndErrors()
    {
        var root = Path.Combine(_tempDir, "convert-root");
        Directory.CreateDirectory(root);
        var noTarget = Path.Combine(root, "a.bin");
        var sameExt = Path.Combine(root, "b.chd");
        var ok = Path.Combine(root, "c.zip");
        var error = Path.Combine(root, "d.zip");
        File.WriteAllText(noTarget, "1");
        File.WriteAllText(sameExt, "2");
        File.WriteAllText(ok, "3");
        File.WriteAllText(error, "4");

        var convertedTarget = Path.Combine(root, "c.chd");
        File.WriteAllText(convertedTarget, "converted");

        var converter = new TestFormatConverter(
            targetByExtension: new Dictionary<string, ConversionTarget>(StringComparer.OrdinalIgnoreCase)
            {
                [".chd"] = new ConversionTarget(".chd", "noop", "noop"),
                [".zip"] = new ConversionTarget(".chd", "noop", "noop")
            },
            convertResults: new Dictionary<string, ConversionResult>(StringComparer.OrdinalIgnoreCase)
            {
                [ok] = new ConversionResult(ok, convertedTarget, ConversionOutcome.Success),
                [error] = new ConversionResult(error, null, ConversionOutcome.Error, "tool-failed", 1)
            },
            verifyResults: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                [convertedTarget] = true
            },
            noTargetExtensions: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".bin" });

        var fs = new TestFileSystem();
        fs.MoveResults[ok] = Path.Combine(root, "_TRASH_CONVERTED", "c.zip");

        var audit = new TrackingAuditStore();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip", ".chd", ".bin" },
            ConvertOnly = true,
            ConvertFormat = "chd",
            Mode = RunConstants.ModeMove,
            AuditPath = Path.Combine(_tempDir, "audit.csv")
        };

        var phase = new ConvertOnlyPipelinePhase();
        var output = phase.Execute(
            new ConvertOnlyPhaseInput(
                new[]
                {
                    Candidate(noTarget, "a", "US", 100, FileCategory.Game),
                    Candidate(sameExt, "b", "US", 100, FileCategory.Game),
                    Candidate(ok, "c", "US", 100, FileCategory.Game),
                    Candidate(error, "d", "US", 100, FileCategory.Game)
                },
                options,
                converter),
            CreateContext(options, fs, audit),
            CancellationToken.None);

        Assert.Equal(1, output.Converted);
        Assert.Equal(0, output.ConvertSkipped); // no-target and same-ext are pre-filtered, not converter-reported skips
        Assert.Equal(1, output.ConvertErrors);
        Assert.Contains(audit.Rows, r => r.Action == "CONVERT");
        Assert.Contains(audit.Rows, r => r.Action == "CONVERT_ERROR");
    }

    [Fact]
    public void WinnerConversionPhase_VerifyFailure_IncrementsError_AndWritesFailedAudit()
    {
        var root = Path.Combine(_tempDir, "winner-convert");
        Directory.CreateDirectory(root);
        var winner = Path.Combine(root, "winner.zip");
        File.WriteAllText(winner, "winner");

        var targetPath = Path.Combine(root, "winner.chd");
        File.WriteAllText(targetPath, "bad-conversion");

        var converter = new TestFormatConverter(
            targetByExtension: new Dictionary<string, ConversionTarget>(StringComparer.OrdinalIgnoreCase)
            {
                [".zip"] = new ConversionTarget(".chd", "noop", "noop")
            },
            convertResults: new Dictionary<string, ConversionResult>(StringComparer.OrdinalIgnoreCase)
            {
                [winner] = new ConversionResult(winner, targetPath, ConversionOutcome.Success)
            },
            verifyResults: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                [targetPath] = false
            });

        var fs = new TestFileSystem();
        var audit = new TrackingAuditStore();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            ConvertFormat = "chd",
            Mode = RunConstants.ModeMove,
            AuditPath = Path.Combine(_tempDir, "audit.csv")
        };

        var group = new DedupeGroup
        {
            GameKey = "winner",
            Winner = Candidate(winner, "winner", "US", 1000, FileCategory.Game),
            Losers = Array.Empty<RomCandidate>()
        };

        var phase = new WinnerConversionPipelinePhase();
        var output = phase.Execute(
            new WinnerConversionPhaseInput(new[] { group }, options, new HashSet<string>(StringComparer.OrdinalIgnoreCase), converter),
            CreateContext(options, fs, audit),
            CancellationToken.None);

        Assert.Equal(0, output.Converted);
        Assert.Equal(0, output.ConvertSkipped);
        Assert.Equal(1, output.ConvertErrors);
        Assert.Contains(audit.Rows, r => r.Action == "CONVERT_FAILED");
        Assert.False(File.Exists(targetPath));
    }

    [Fact]
    public void ConvertOnlyPhase_PreservesResultOrder_ForIndependentCandidates()
    {
        var root = Path.Combine(_tempDir, "convert-parallel");
        Directory.CreateDirectory(root);

        var first = Path.Combine(root, "a.iso");
        var second = Path.Combine(root, "b.iso");
        var third = Path.Combine(root, "c.iso");
        File.WriteAllText(first, "a");
        File.WriteAllText(second, "b");
        File.WriteAllText(third, "c");

        var converter = new ParallelTrackingConverter(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [first] = 125,
            [second] = 0,
            [third] = 50
        });

        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".iso" },
            ConvertOnly = true,
            ConvertFormat = "chd",
            Mode = RunConstants.ModeMove,
            AuditPath = Path.Combine(_tempDir, "parallel-audit.csv")
        };
        var fileSystem = new TestFileSystem();
        fileSystem.MoveResults[first] = Path.Combine(root, "_TRASH_CONVERTED", "a.iso");
        fileSystem.MoveResults[second] = Path.Combine(root, "_TRASH_CONVERTED", "b.iso");
        fileSystem.MoveResults[third] = Path.Combine(root, "_TRASH_CONVERTED", "c.iso");

        var phase = new ConvertOnlyPipelinePhase();
        var output = phase.Execute(
            new ConvertOnlyPhaseInput(
                new[]
                {
                    Candidate(first, "a", "US", 100, FileCategory.Game),
                    Candidate(second, "b", "US", 100, FileCategory.Game),
                    Candidate(third, "c", "US", 100, FileCategory.Game)
                },
                options,
                converter),
            CreateContext(options, fileSystem, new TrackingAuditStore()),
            CancellationToken.None);

        Assert.Equal(3, output.Converted);
        Assert.Equal(
            new[] { first, second, third },
            output.ConversionResults.Select(r => r.SourcePath).ToArray());
    }

    [Fact]
    public void ConvertOnlyPhase_EmitsIncrementalProgress_ForSmallBatches()
    {
        var root = Path.Combine(_tempDir, "convert-progress");
        Directory.CreateDirectory(root);

        var first = Path.Combine(root, "a.iso");
        var second = Path.Combine(root, "b.iso");
        var third = Path.Combine(root, "c.iso");
        File.WriteAllText(first, "a");
        File.WriteAllText(second, "b");
        File.WriteAllText(third, "c");

        var progressMessages = new List<string>();
        var converter = new ParallelTrackingConverter();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".iso" },
            ConvertOnly = true,
            ConvertFormat = "chd",
            Mode = RunConstants.ModeMove,
            AuditPath = Path.Combine(_tempDir, "progress-audit.csv")
        };

        var phase = new ConvertOnlyPipelinePhase();
        _ = phase.Execute(
            new ConvertOnlyPhaseInput(
                new[]
                {
                    Candidate(first, "a", "US", 100, FileCategory.Game),
                    Candidate(second, "b", "US", 100, FileCategory.Game),
                    Candidate(third, "c", "US", 100, FileCategory.Game)
                },
                options,
                converter),
            CreateContext(options, new TestFileSystem(), new TrackingAuditStore(), progressMessages.Add),
            CancellationToken.None);

        Assert.Contains(progressMessages, message => message.Contains("[Convert] Fortschritt: 1/3", StringComparison.Ordinal));
        Assert.Contains(progressMessages, message => message.Contains("[Convert] Fortschritt: 2/3", StringComparison.Ordinal));
        Assert.Contains(progressMessages, message => message.Contains("[Convert] Fortschritt: 3/3", StringComparison.Ordinal));
    }

    [Fact]
    public void ConvertOnlyPhase_SameBaseNameCandidates_AreSerializedToPreserveTargetExistsSemantics()
    {
        var root = Path.Combine(_tempDir, "convert-collision");
        Directory.CreateDirectory(root);

        var iso = Path.Combine(root, "game.iso");
        var zip = Path.Combine(root, "game.zip");
        File.WriteAllText(iso, "iso");
        File.WriteAllText(zip, "zip");

        var converter = new CollidingTargetConverter();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".iso", ".zip" },
            ConvertOnly = true,
            ConvertFormat = "chd",
            Mode = RunConstants.ModeMove,
            AuditPath = Path.Combine(_tempDir, "collision-audit.csv")
        };
        var fileSystem = new TestFileSystem();
        fileSystem.MoveResults[iso] = Path.Combine(root, "_TRASH_CONVERTED", "game.iso");
        fileSystem.MoveResults[zip] = Path.Combine(root, "_TRASH_CONVERTED", "game.zip");

        var phase = new ConvertOnlyPipelinePhase();
        var output = phase.Execute(
            new ConvertOnlyPhaseInput(
                new[]
                {
                    Candidate(iso, "game-iso", "US", 100, FileCategory.Game),
                    Candidate(zip, "game-zip", "US", 100, FileCategory.Game)
                },
                options,
                converter),
            CreateContext(options, fileSystem, new TrackingAuditStore()),
            CancellationToken.None);

        Assert.Equal(1, output.Converted);
        Assert.Equal(1, output.ConvertSkipped);
        Assert.Equal(1, converter.MaxActiveObserved);
    }

    [Fact]
    public void WinnerConversionPhase_PreservesResultOrder_ThroughSharedBatchExecution()
    {
        var root = Path.Combine(_tempDir, "winner-convert-parallel");
        Directory.CreateDirectory(root);

        var first = Path.Combine(root, "winner-a.iso");
        var second = Path.Combine(root, "winner-b.iso");
        File.WriteAllText(first, "a");
        File.WriteAllText(second, "b");

        var converter = new ParallelTrackingConverter();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".iso" },
            ConvertFormat = "chd",
            Mode = RunConstants.ModeMove,
            AuditPath = Path.Combine(_tempDir, "winner-parallel-audit.csv")
        };
        var fileSystem = new TestFileSystem();
        fileSystem.MoveResults[first] = Path.Combine(root, "_TRASH_CONVERTED", "winner-a.iso");
        fileSystem.MoveResults[second] = Path.Combine(root, "_TRASH_CONVERTED", "winner-b.iso");

        var output = new WinnerConversionPipelinePhase().Execute(
            new WinnerConversionPhaseInput(
                new[]
                {
                    new DedupeGroup
                    {
                        GameKey = "winner-a",
                        Winner = Candidate(first, "winner-a", "US", 100, FileCategory.Game),
                        Losers = Array.Empty<RomCandidate>()
                    },
                    new DedupeGroup
                    {
                        GameKey = "winner-b",
                        Winner = Candidate(second, "winner-b", "US", 100, FileCategory.Game),
                        Losers = Array.Empty<RomCandidate>()
                    }
                },
                options,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                converter),
            CreateContext(options, fileSystem, new TrackingAuditStore()),
            CancellationToken.None);

        Assert.Equal(2, output.Converted);
        Assert.Equal(
            new[] { first, second },
            output.ConversionResults.Select(r => r.SourcePath).ToArray());
    }

    [Fact]
    public void DeduplicatePhase_RemainsStable_When_DatIndexUnavailable()
    {
        var options = new RunOptions { Roots = new[] { "C:/roms" }, Extensions = new[] { ".zip" } };
        var phase = new DeduplicatePipelinePhase();
        var candidates = new[]
        {
            Candidate("C:/roms/game (US).zip", "game", "US", 1000, FileCategory.Game)
        };

        // DatIndex is intentionally absent in this isolation scenario.
        var result = phase.Execute(candidates, CreateContext(options, new TestFileSystem()), CancellationToken.None);

        Assert.Single(result.Groups);
        Assert.Single(result.GameGroups);
    }

    private static PipelineContext CreateContext(
        RunOptions options,
        IFileSystem fileSystem,
        IAuditStore? auditStore = null,
        Action<string>? onProgress = null)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        return new PipelineContext
        {
            Options = options,
            FileSystem = fileSystem,
            AuditStore = auditStore ?? new TrackingAuditStore(),
            Metrics = metrics,
            OnProgress = onProgress ?? (_ => { })
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

    private sealed class TestFileSystem : IFileSystem
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _filesByRoot = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> MoveResults { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IReadOnlyList<string>> AllowedExtensionsByRoot { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> PendingScanWarnings { get; } = new();

        public void SetFiles(string root, params string[] files)
        {
            _filesByRoot[root] = files;
        }

        public bool TestPath(string literalPath, string pathType = "Any")
        {
            if (string.IsNullOrWhiteSpace(literalPath)) return false;
            return pathType switch
            {
                "Leaf" => File.Exists(literalPath),
                "Container" => Directory.Exists(literalPath),
                _ => File.Exists(literalPath) || Directory.Exists(literalPath)
            };
        }

        public string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
        {
            AllowedExtensionsByRoot[root] = allowedExtensions?.ToArray() ?? Array.Empty<string>();

            if (!_filesByRoot.TryGetValue(root, out var all))
                return Array.Empty<string>();

            if (allowedExtensions is null)
                return all;

            var allowed = new HashSet<string>(allowedExtensions, StringComparer.OrdinalIgnoreCase);
            return all.Where(f => allowed.Contains(Path.GetExtension(f))).ToArray();
        }

        public IReadOnlyList<string> ConsumeScanWarnings()
        {
            var warnings = PendingScanWarnings.ToArray();
            PendingScanWarnings.Clear();
            return warnings;
        }

        public string? MoveItemSafely(string sourcePath, string destinationPath)
        {
            if (MoveResults.TryGetValue(sourcePath, out var actual))
                return actual;

            return null;
        }

        public bool MoveDirectorySafely(string sourcePath, string destinationPath)
            => true;

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.GetFullPath(Path.Combine(rootPath, relativePath));

        public bool IsReparsePoint(string path)
            => false;

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
        {
            Rows.Add(new AuditRow(action, oldPath, newPath, reason));
        }
    }

    private sealed record AuditRow(string Action, string OldPath, string NewPath, string Reason);

    private sealed class TestFormatConverter : IFormatConverter
    {
        private readonly IReadOnlyDictionary<string, ConversionTarget> _targetByExtension;
        private readonly IReadOnlyDictionary<string, ConversionResult> _convertResults;
        private readonly IReadOnlyDictionary<string, bool> _verifyResults;
        private readonly ISet<string> _noTargetExtensions;

        public TestFormatConverter(
            IReadOnlyDictionary<string, ConversionTarget> targetByExtension,
            IReadOnlyDictionary<string, ConversionResult> convertResults,
            IReadOnlyDictionary<string, bool>? verifyResults = null,
            ISet<string>? noTargetExtensions = null)
        {
            _targetByExtension = targetByExtension;
            _convertResults = convertResults;
            _verifyResults = verifyResults ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            _noTargetExtensions = noTargetExtensions ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
        {
            if (_noTargetExtensions.Contains(sourceExtension))
                return null;

            return _targetByExtension.TryGetValue(sourceExtension, out var target)
                ? target
                : null;
        }

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            if (_convertResults.TryGetValue(sourcePath, out var result))
                return result;

            return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "no-mapping", 0);
        }

        public bool Verify(string targetPath, ConversionTarget target)
            => _verifyResults.TryGetValue(targetPath, out var ok) && ok;
    }

    private sealed class ParallelTrackingConverter : IFormatConverter
    {
        private readonly IReadOnlyDictionary<string, int> _delayBySource;
        private readonly ManualResetEventSlim _parallelGate = new(initialState: false);
        private int _active;
        private int _maxActiveObserved;

        public ParallelTrackingConverter(IReadOnlyDictionary<string, int>? delayBySource = null)
        {
            _delayBySource = delayBySource
                ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        public int MaxActiveObserved => Volatile.Read(ref _maxActiveObserved);

        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "noop", "noop");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMax(active);

            if (active >= 2)
                _parallelGate.Set();
            else
                _parallelGate.Wait(TimeSpan.FromMilliseconds(500));

            try
            {
                if (_delayBySource.TryGetValue(sourcePath, out var delayMs) && delayMs > 0)
                    Thread.Sleep(delayMs);

                var targetPath = Path.Combine(
                    Path.GetDirectoryName(sourcePath)!,
                    Path.GetFileNameWithoutExtension(sourcePath) + target.Extension);
                File.WriteAllText(targetPath, "converted");
                return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public bool Verify(string targetPath, ConversionTarget target) => true;

        private void UpdateMax(int active)
        {
            while (true)
            {
                var observed = Volatile.Read(ref _maxActiveObserved);
                if (active <= observed)
                    return;

                if (Interlocked.CompareExchange(ref _maxActiveObserved, active, observed) == observed)
                    return;
            }
        }
    }

    private sealed class CollidingTargetConverter : IFormatConverter
    {
        private readonly ManualResetEventSlim _parallelGate = new(initialState: false);
        private int _active;
        private int _maxActiveObserved;

        public int MaxActiveObserved => Volatile.Read(ref _maxActiveObserved);

        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "noop", "noop");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMax(active);

            if (active >= 2)
                _parallelGate.Set();
            else
                _parallelGate.Wait(TimeSpan.FromMilliseconds(500));

            try
            {
                var targetPath = Path.Combine(
                    Path.GetDirectoryName(sourcePath)!,
                    Path.GetFileNameWithoutExtension(sourcePath) + target.Extension);

                if (File.Exists(targetPath))
                    return new ConversionResult(sourcePath, null, ConversionOutcome.Skipped, "target-exists");

                File.WriteAllText(targetPath, "converted");
                return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public bool Verify(string targetPath, ConversionTarget target) => true;

        private void UpdateMax(int active)
        {
            while (true)
            {
                var observed = Volatile.Read(ref _maxActiveObserved);
                if (active <= observed)
                    return;

                if (Interlocked.CompareExchange(ref _maxActiveObserved, active, observed) == observed)
                    return;
            }
        }
    }
}
