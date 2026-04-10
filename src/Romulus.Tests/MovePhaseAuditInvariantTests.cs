using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Contracts;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

public sealed class MovePhaseAuditInvariantTests : IDisposable
{
    private readonly string _tempDir;

    public MovePhaseAuditInvariantTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MoveInv_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void MovePhase_CountInvariant_HoldsForMixedOutcomes()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);

        var a = CreateFile(root, "a.zip");
        var b = CreateFile(root, "b.zip");
        var c = CreateFile(Path.Combine(_tempDir, "outside"), "c.zip");

        var fs = new InvariantFs();
        fs.MoveResults[a] = Path.Combine(root, "_TRASH_REGION_DEDUPE", "a.zip");

        var existingConflict = Path.Combine(root, "_TRASH_REGION_DEDUPE", "b.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(existingConflict)!);
        File.WriteAllText(existingConflict, "conflict");

        var audit = new InvariantAuditStore();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Mode = "Move",
            Extensions = new[] { ".zip" },
            ConflictPolicy = "Skip",
            AuditPath = Path.Combine(_tempDir, "audit.csv")
        };

        var group = new DedupeGroup
        {
            GameKey = "g",
            Winner = Candidate(Path.Combine(root, "winner.zip")),
            Losers = new[] { Candidate(a), Candidate(b), Candidate(c) }
        };

        var result = new MovePipelinePhase().Execute(
            new MovePhaseInput(new[] { group }, options),
            Context(options, fs, audit),
            CancellationToken.None);

        Assert.Equal(3, result.MoveCount + result.SkipCount + result.FailCount);
        Assert.Equal(1, result.MoveCount);
        Assert.Equal(1, result.SkipCount);
        Assert.Equal(1, result.FailCount);
        // TASK-147: Write-ahead pattern: MOVE_PENDING + Move for successful move, SKIP for skip
        // Loser a: MOVE_PENDING + Move = 2 rows; Loser b: SKIP = 1 row; Loser c: fail (no root) = 0 rows
        Assert.Equal(3, audit.Rows.Count);
    }

    [Fact]
    public void MovePhase_CrossVolumeInsufficientSpace_AbortsBeforeAnyMove()
    {
        var loser = new RomCandidate
        {
            MainPath = @"C:\roms\large.zip",
            GameKey = "space",
            Region = "US",
            RegionScore = 1000,
            FormatScore = 500,
            VersionScore = 100,
            SizeBytes = 500,
            Extension = ".zip",
            ConsoleKey = "GENERIC",
            Category = FileCategory.Game
        };

        var options = new RunOptions
        {
            Roots = [@"C:\roms"],
            Mode = "Move",
            TrashRoot = @"D:\trash",
            ConflictPolicy = "Rename"
        };

        var group = new DedupeGroup
        {
            GameKey = "space",
            Winner = Candidate(@"C:\roms\winner.zip"),
            Losers = [loser]
        };

        var fs = new LowSpaceFs(availableBytes: 100);
        var audit = new InvariantAuditStore();

        var result = new MovePipelinePhase().Execute(
            new MovePhaseInput([group], options),
            Context(options, fs, audit),
            CancellationToken.None);

        Assert.Equal(0, result.MoveCount);
        Assert.Equal(1, result.FailCount);
        Assert.Equal(0, fs.MoveCalls);
    }

    [Fact]
    public void MovePhase_ConflictPolicyOverwrite_ForwardsOverwriteFlag()
    {
        var root = @"C:\roms-overwrite";
        var loser = new RomCandidate
        {
            MainPath = Path.Combine(root, "loser.zip"),
            GameKey = "overwrite",
            Region = "US",
            RegionScore = 1000,
            FormatScore = 500,
            VersionScore = 100,
            SizeBytes = 10,
            Extension = ".zip",
            ConsoleKey = "GENERIC",
            Category = FileCategory.Game
        };

        var group = new DedupeGroup
        {
            GameKey = "overwrite",
            Winner = Candidate(Path.Combine(root, "winner.zip")),
            Losers = [loser]
        };

        var options = new RunOptions
        {
            Roots = [root],
            Mode = "Move",
            ConflictPolicy = "Overwrite"
        };

        var fs = new OverwriteTrackingFs();
        var audit = new InvariantAuditStore();

        var result = new MovePipelinePhase().Execute(
            new MovePhaseInput([group], options),
            Context(options, fs, audit),
            CancellationToken.None);

        Assert.Equal(1, result.MoveCount);
        Assert.Contains(fs.OverwriteFlags, static value => value);
    }

    [Fact]
    public void MovePhase_FlushesAndWritesMetadata_EveryTenMoves()
    {
        var root = Path.Combine(_tempDir, "flush-root");
        Directory.CreateDirectory(root);

        var losers = new List<RomCandidate>();
        var fs = new InvariantFs();
        for (var i = 0; i < 10; i++)
        {
            var source = CreateFile(root, $"loser-{i}.zip");
            losers.Add(Candidate(source));
            fs.MoveResults[source] = Path.Combine(root, "_TRASH_REGION_DEDUPE", Path.GetFileName(source));
        }

        var audit = new InvariantAuditStore();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Mode = "Move",
            Extensions = new[] { ".zip" },
            ConflictPolicy = "Rename",
            AuditPath = Path.Combine(_tempDir, "audit.csv")
        };

        var group = new DedupeGroup
        {
            GameKey = "flush",
            Winner = Candidate(Path.Combine(root, "winner.zip")),
            Losers = losers
        };

        var result = new MovePipelinePhase().Execute(
            new MovePhaseInput(new[] { group }, options),
            Context(options, fs, audit),
            CancellationToken.None);

        Assert.Equal(10, result.MoveCount);
        // TASK-147: Write-ahead pattern adds Flush before each MOVE_PENDING.
        // Flushes: 1 (initial) + 10 (before each PENDING) + 1 (at moveCount=10) + 1 (final) = 13
        Assert.Equal(13, audit.FlushCalls);
        // Sidecars: 1 (initial primed) + 1 (at moveCount=10) + 1 (final) = 3
        Assert.Equal(3, audit.SidecarCalls);
        Assert.Equal("Sidecar", audit.CallOrder[0]);
        Assert.Equal("Append", audit.CallOrder[1]);
        Assert.Equal("Sidecar", audit.CallOrder[^1]);
    }

    [Fact]
    public void MovePhase_PrimesSidecarBeforeFirstMove_WhenAuditEnabled()
    {
        var root = Path.Combine(_tempDir, "prime-root");
        Directory.CreateDirectory(root);

        var source = CreateFile(root, "prime.zip");
        var fs = new InvariantFs();
        fs.MoveResults[source] = Path.Combine(root, "_TRASH_REGION_DEDUPE", "prime.zip");

        var audit = new InvariantAuditStore();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Mode = "Move",
            Extensions = new[] { ".zip" },
            ConflictPolicy = "Rename",
            AuditPath = Path.Combine(_tempDir, "audit-prime.csv")
        };

        var group = new DedupeGroup
        {
            GameKey = "prime",
            Winner = Candidate(Path.Combine(root, "winner.zip")),
            Losers = new[] { Candidate(source) }
        };

        var result = new MovePipelinePhase().Execute(
            new MovePhaseInput(new[] { group }, options),
            Context(options, fs, audit),
            CancellationToken.None);

        Assert.Equal(1, result.MoveCount);
        Assert.Equal("Sidecar", audit.CallOrder[0]);
        Assert.Equal("Append", audit.CallOrder[1]);
        Assert.Equal(2, audit.SidecarCalls);
    }

    [Fact]
    public void MovePhase_WithoutAuditPath_WritesNoAuditRows()
    {
        var root = Path.Combine(_tempDir, "no-audit");
        Directory.CreateDirectory(root);
        var source = CreateFile(root, "loser.zip");

        var fs = new InvariantFs();
        fs.MoveResults[source] = Path.Combine(root, "_TRASH_REGION_DEDUPE", "loser.zip");
        var audit = new InvariantAuditStore();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Mode = "Move",
            Extensions = new[] { ".zip" },
            AuditPath = null
        };

        var group = new DedupeGroup
        {
            GameKey = "g",
            Winner = Candidate(Path.Combine(root, "winner.zip")),
            Losers = new[] { Candidate(source) }
        };

        var result = new MovePipelinePhase().Execute(
            new MovePhaseInput(new[] { group }, options),
            Context(options, fs, audit),
            CancellationToken.None);

        Assert.Equal(1, result.MoveCount);
        Assert.Empty(audit.Rows);
    }

    [Fact]
    public void MovePhase_SetMemberFailure_RollsBackDescriptorAndDoesNotCountMove()
    {
        var root = Path.Combine(_tempDir, "set-atomic");
        Directory.CreateDirectory(root);

        var cue = CreateFile(root, "game.cue");
        var bin1 = CreateFile(root, "game (track 1).bin");
        var bin2 = CreateFile(root, "game (track 2).bin");

        File.WriteAllText(cue,
            "FILE \"game (track 1).bin\" BINARY\n" +
            "  TRACK 01 MODE1/2352\n" +
            "    INDEX 01 00:00:00\n" +
            "FILE \"game (track 2).bin\" BINARY\n" +
            "  TRACK 02 MODE1/2352\n" +
            "    INDEX 01 00:00:00\n");

        var fs = new SetAtomicFs
        {
            FailSourcePath = bin2
        };

        var audit = new InvariantAuditStore();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Mode = "Move",
            ConflictPolicy = "Rename",
            AuditPath = Path.Combine(_tempDir, "audit-set-atomic.csv")
        };

        var group = new DedupeGroup
        {
            GameKey = "set-game",
            Winner = Candidate(Path.Combine(root, "winner.zip")),
            Losers = new[] { Candidate(cue) }
        };

        var result = new MovePipelinePhase().Execute(
            new MovePhaseInput(new[] { group }, options),
            Context(options, fs, audit),
            CancellationToken.None);

        Assert.Equal(0, result.MoveCount);
        Assert.Equal(1, result.FailCount);
        Assert.True(fs.RollbackCalls > 0);
    }

    [Fact]
    public void Move_SetMemberFailure_IncrementsFailCount()
    {
        var root = Path.Combine(_tempDir, "tgap52-root");
        Directory.CreateDirectory(root);

        var cue = CreateSizedFile(root, "game.cue", 32,
            "FILE \"game (track 1).bin\" BINARY\n" +
            "  TRACK 01 MODE1/2352\n" +
            "    INDEX 01 00:00:00\n" +
            "FILE \"game (track 2).bin\" BINARY\n" +
            "  TRACK 02 MODE1/2352\n" +
            "    INDEX 01 00:00:00\n");
        var bin1 = CreateSizedFile(root, "game (track 1).bin", 10);
        var bin2 = CreateSizedFile(root, "game (track 2).bin", 12);

        var fs = new SetAtomicFs { FailSourcePath = bin2 };
        var audit = new InvariantAuditStore();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Mode = "Move",
            ConflictPolicy = "Rename",
            AuditPath = Path.Combine(_tempDir, "audit-tgap52.csv")
        };

        var group = new DedupeGroup
        {
            GameKey = "tgap52",
            Winner = Candidate(Path.Combine(root, "winner.zip")),
            Losers = new[]
            {
                new RomCandidate
                {
                    MainPath = cue,
                    GameKey = "tgap52",
                    Region = "US",
                    RegionScore = 1000,
                    FormatScore = 500,
                    VersionScore = 100,
                    SizeBytes = 32,
                    Extension = ".cue",
                    ConsoleKey = "PSX",
                    Category = FileCategory.Game
                }
            }
        };

        var result = new MovePipelinePhase().Execute(
            new MovePhaseInput(new[] { group }, options),
            Context(options, fs, audit),
            CancellationToken.None);

        Assert.Equal(1, result.FailCount);
        Assert.Equal(0, result.MoveCount);
    }

    [Fact]
    public void Move_SetMembers_AreCountedInSavedBytes()
    {
        var root = Path.Combine(_tempDir, "tgap53-root");
        Directory.CreateDirectory(root);

        var cue = CreateSizedFile(root, "album.cue", 25,
            "FILE \"album (track 1).bin\" BINARY\n" +
            "  TRACK 01 MODE1/2352\n" +
            "    INDEX 01 00:00:00\n" +
            "FILE \"album (track 2).bin\" BINARY\n" +
            "  TRACK 02 MODE1/2352\n" +
            "    INDEX 01 00:00:00\n");
        var bin1 = CreateSizedFile(root, "album (track 1).bin", 10);
        var bin2 = CreateSizedFile(root, "album (track 2).bin", 15);

        var fs = new InvariantFs();
        var trashDir = Path.Combine(root, RunConstants.WellKnownFolders.TrashRegionDedupe);
        fs.MoveResults[cue] = Path.Combine(trashDir, Path.GetFileName(cue));
        fs.MoveResults[bin1] = Path.Combine(trashDir, Path.GetFileName(bin1));
        fs.MoveResults[bin2] = Path.Combine(trashDir, Path.GetFileName(bin2));

        var audit = new InvariantAuditStore();
        var options = new RunOptions
        {
            Roots = new[] { root },
            Mode = "Move",
            ConflictPolicy = "Rename",
            AuditPath = Path.Combine(_tempDir, "audit-tgap53.csv")
        };

        var group = new DedupeGroup
        {
            GameKey = "tgap53",
            Winner = Candidate(Path.Combine(root, "winner.zip")),
            Losers = new[]
            {
                new RomCandidate
                {
                    MainPath = cue,
                    GameKey = "tgap53",
                    Region = "US",
                    RegionScore = 1000,
                    FormatScore = 500,
                    VersionScore = 100,
                    SizeBytes = 25,
                    Extension = ".cue",
                    ConsoleKey = "PSX",
                    Category = FileCategory.Game
                }
            }
        };

        var result = new MovePipelinePhase().Execute(
            new MovePhaseInput(new[] { group }, options),
            Context(options, fs, audit),
            CancellationToken.None);

        Assert.Equal(3, result.MoveCount);
        Assert.Equal(0, result.FailCount);
        Assert.Equal(50, result.SavedBytes);
    }

    private static string CreateFile(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private static string CreateSizedFile(string dir, string name, int sizeBytes, string? contentOverride = null)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        if (contentOverride is not null)
        {
            File.WriteAllText(path, contentOverride);
            return path;
        }

        File.WriteAllBytes(path, Enumerable.Repeat((byte)'x', sizeBytes).ToArray());
        return path;
    }

    private static RomCandidate Candidate(string path)
    {
        return new RomCandidate
        {
            MainPath = path,
            GameKey = "g",
            Region = "US",
            RegionScore = 1000,
            FormatScore = 500,
            VersionScore = 100,
            HeaderScore = 0,
            CompletenessScore = 0,
            SizeTieBreakScore = 0,
            SizeBytes = 100,
            Extension = ".zip",
            ConsoleKey = "GENERIC",
            Category = FileCategory.Game
        };
    }

    private static PipelineContext Context(RunOptions options, IFileSystem fs, IAuditStore audit)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        return new PipelineContext
        {
            Options = options,
            FileSystem = fs,
            AuditStore = audit,
            Metrics = metrics,
            OnProgress = _ => { }
        };
    }

    private sealed class InvariantFs : IFileSystem
    {
        public Dictionary<string, string> MoveResults { get; } = new(StringComparer.OrdinalIgnoreCase);

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
            => Array.Empty<string>();

        public string? MoveItemSafely(string sourcePath, string destinationPath)
            => MoveResults.TryGetValue(sourcePath, out var moved) ? moved : null;

        public bool MoveDirectorySafely(string sourcePath, string destinationPath)
            => true;

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.GetFullPath(Path.Combine(rootPath, relativePath));

        public bool IsReparsePoint(string path)
            => false;

        public void DeleteFile(string path)
        {
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
        }
    }

    private sealed class SetAtomicFs : IFileSystem
    {
        public string? FailSourcePath { get; init; }
        public int RollbackCalls { get; private set; }

        public bool TestPath(string literalPath, string pathType = "Any") => true;

        public string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => Array.Empty<string>();

        public string? MoveItemSafely(string sourcePath, string destinationPath)
        {
            if (!string.IsNullOrEmpty(FailSourcePath)
                && string.Equals(sourcePath, FailSourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (sourcePath.Contains(RunConstants.WellKnownFolders.TrashRegionDedupe, StringComparison.OrdinalIgnoreCase))
                RollbackCalls++;

            return destinationPath;
        }

        public bool MoveDirectorySafely(string sourcePath, string destinationPath) => true;

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.GetFullPath(Path.Combine(rootPath, relativePath));

        public bool IsReparsePoint(string path) => false;

        public void DeleteFile(string path)
        {
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
        }
    }

    private sealed class LowSpaceFs : IFileSystem
    {
        private readonly long _availableBytes;

        public LowSpaceFs(long availableBytes)
            => _availableBytes = availableBytes;

        public int MoveCalls { get; private set; }

        public bool TestPath(string literalPath, string pathType = "Any") => true;

        public string EnsureDirectory(string path) => path;

        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => Array.Empty<string>();

        public string? MoveItemSafely(string sourcePath, string destinationPath)
        {
            MoveCalls++;
            return destinationPath;
        }

        public bool MoveDirectorySafely(string sourcePath, string destinationPath) => true;

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);

        public bool IsReparsePoint(string path) => false;

        public void DeleteFile(string path)
        {
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
        }

        public long? GetAvailableFreeSpace(string path) => _availableBytes;
    }

    private sealed class OverwriteTrackingFs : IFileSystem
    {
        public List<bool> OverwriteFlags { get; } = [];

        public bool TestPath(string literalPath, string pathType = "Any") => true;

        public string EnsureDirectory(string path) => path;

        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => Array.Empty<string>();

        public string? MoveItemSafely(string sourcePath, string destinationPath)
            => MoveItemSafely(sourcePath, destinationPath, overwrite: false);

        public string? MoveItemSafely(string sourcePath, string destinationPath, bool overwrite)
        {
            OverwriteFlags.Add(overwrite);
            return destinationPath;
        }

        public bool MoveDirectorySafely(string sourcePath, string destinationPath) => true;

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);

        public bool IsReparsePoint(string path) => false;

        public void DeleteFile(string path)
        {
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
        }
    }

    private sealed class InvariantAuditStore : IAuditStore
    {
        public List<string> Rows { get; } = new();
        public List<string> CallOrder { get; } = new();
        public int FlushCalls { get; private set; }
        public int SidecarCalls { get; private set; }

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
        {
            SidecarCalls++;
            CallOrder.Add("Sidecar");
        }

        public bool TestMetadataSidecar(string auditCsvPath)
            => true;

        public void Flush(string auditCsvPath)
            => FlushCalls++;

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "")
        {
            Rows.Add(action);
            CallOrder.Add("Append");
        }
    }
}
