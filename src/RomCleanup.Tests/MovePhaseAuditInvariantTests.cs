using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Metrics;
using RomCleanup.Infrastructure.Orchestration;
using Xunit;

namespace RomCleanup.Tests;

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

    private static string CreateFile(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
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
