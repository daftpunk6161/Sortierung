using System.Text;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Behavioral tests for audit findings F-1, F-3, F-4, F-6, F-13.
/// These require file system interaction and temp directories.
/// </summary>
public sealed class AuditEFBehavioralTests : IDisposable
{
    private readonly string _tempDir;

    public AuditEFBehavioralTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AuditEF_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ═══ F-1 (P0): MovePipelinePhase Partial-Failure + Rollback ════
    // N Moves, error bei Move N/2 → korrekte Rollback aller bisherigen + Audit-Status

    [Fact]
    public void F01_MovePhase_MultiGroup_PartialFailure_AuditTracksAllOutcomes()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);

        // Group 1: loser a → should succeed
        var a = CreateFile(root, "loser-a.zip");
        // Group 2: loser b → will fail (FailSourceFs simulates failure)
        var b = CreateFile(root, "loser-b.zip");
        // Group 3: loser c → should succeed
        var c = CreateFile(root, "loser-c.zip");

        var trashDir = Path.Combine(root, RunConstants.WellKnownFolders.TrashRegionDedupe);
        var fs = new PartialFailureFs
        {
            FailSourcePath = b,
            MoveResults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [a] = Path.Combine(trashDir, "loser-a.zip"),
                [c] = Path.Combine(trashDir, "loser-c.zip")
            }
        };

        var audit = new TrackingAuditStore();
        var options = new RunOptions
        {
            Roots = [root],
            Mode = "Move",
            Extensions = [".zip"],
            ConflictPolicy = "Rename",
            AuditPath = Path.Combine(_tempDir, "audit.csv")
        };

        var groups = new[]
        {
            new DedupeGroup
            {
                GameKey = "game-a",
                Winner = MakeCandidate(Path.Combine(root, "winner-a.zip")),
                Losers = [MakeCandidate(a)]
            },
            new DedupeGroup
            {
                GameKey = "game-b",
                Winner = MakeCandidate(Path.Combine(root, "winner-b.zip")),
                Losers = [MakeCandidate(b)]
            },
            new DedupeGroup
            {
                GameKey = "game-c",
                Winner = MakeCandidate(Path.Combine(root, "winner-c.zip")),
                Losers = [MakeCandidate(c)]
            }
        };

        var result = new MovePipelinePhase().Execute(
            new MovePhaseInput(groups, options),
            MakeContext(options, fs, audit),
            CancellationToken.None);

        // 2 successful + 1 failed = 3 total
        Assert.Equal(2, result.MoveCount);
        Assert.Equal(1, result.FailCount);
        Assert.Equal(3, result.MoveCount + result.SkipCount + result.FailCount);

        // Audit must contain entries for BOTH successful moves AND the failed one
        // Successful moves: MOVE_PENDING + action rows
        var moveRows = audit.Rows.Count(r =>
            r.Equals("MOVE", StringComparison.OrdinalIgnoreCase) ||
            r.Equals("Move", StringComparison.OrdinalIgnoreCase));
        Assert.True(moveRows >= 2, $"Expected at least 2 Move audit rows, got {moveRows}");

        // MOVE_PENDING rows from write-ahead pattern
        var pendingRows = audit.Rows.Count(r =>
            r.Equals("MOVE_PENDING", StringComparison.OrdinalIgnoreCase));
        Assert.True(pendingRows >= 2, $"Expected at least 2 MOVE_PENDING audit rows, got {pendingRows}");
    }

    [Fact]
    public void F01_MovePhase_FailureMidway_CountInvariantHolds()
    {
        // With 5 groups where group 3 fails, the count invariant must hold
        var root = Path.Combine(_tempDir, "roms-5");
        Directory.CreateDirectory(root);

        var losers = new List<(string path, string gameKey)>();
        var moveResults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trashDir = Path.Combine(root, RunConstants.WellKnownFolders.TrashRegionDedupe);
        string? failPath = null;

        for (var i = 0; i < 5; i++)
        {
            var path = CreateFile(root, $"loser-{i}.zip");
            losers.Add((path, $"game-{i}"));

            if (i == 2) // Group 3 fails
                failPath = path;
            else
                moveResults[path] = Path.Combine(trashDir, $"loser-{i}.zip");
        }

        var fs = new PartialFailureFs
        {
            FailSourcePath = failPath!,
            MoveResults = moveResults
        };

        var audit = new TrackingAuditStore();
        var options = new RunOptions
        {
            Roots = [root],
            Mode = "Move",
            Extensions = [".zip"],
            ConflictPolicy = "Rename",
            AuditPath = Path.Combine(_tempDir, "audit-5.csv")
        };

        var groups = losers.Select(l => new DedupeGroup
        {
            GameKey = l.gameKey,
            Winner = MakeCandidate(Path.Combine(root, $"winner-{l.gameKey}.zip")),
            Losers = [MakeCandidate(l.path)]
        }).ToArray();

        var result = new MovePipelinePhase().Execute(
            new MovePhaseInput(groups, options),
            MakeContext(options, fs, audit),
            CancellationToken.None);

        // Count invariant: Move + Skip + Fail = total losers
        Assert.Equal(5, result.MoveCount + result.SkipCount + result.FailCount);
        Assert.Equal(4, result.MoveCount);
        Assert.Equal(1, result.FailCount);
    }

    // ═══ F-3 (P1): Concurrent Audit-File Locking ════
    // 10 parallele Audit-Writes → keine korrupten CSV-Zeilen

    [Fact]
    public async Task F03_AuditCsvStore_ConcurrentWrites_NoCorruptedRows()
    {
        var auditPath = Path.Combine(_tempDir, "concurrent-audit.csv");
        var store = new AuditCsvStore();
        const int writerCount = 10;

        var tasks = Enumerable.Range(0, writerCount).Select(i =>
            Task.Run(() =>
            {
                store.AppendAuditRow(
                    auditPath,
                    rootPath: @"C:\roms",
                    oldPath: $@"C:\roms\file_{i}.zip",
                    newPath: $@"C:\roms\trash\file_{i}.zip",
                    action: "MOVE",
                    category: "Game",
                    hash: $"hash_{i}",
                    reason: $"dedupe_{i}");
            })).ToArray();

        await Task.WhenAll(tasks);

        // Verify: file exists with header + exactly N data rows
        Assert.True(File.Exists(auditPath));

        var lines = File.ReadAllLines(auditPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        // 1 header + 10 data rows
        Assert.Equal(writerCount + 1, lines.Length);

        // Header must be first line
        Assert.StartsWith("RootPath,OldPath,NewPath,Action", lines[0]);

        // Every data row must have exactly 8 comma-separated fields (no corruption)
        for (var i = 1; i < lines.Length; i++)
        {
            var fields = AuditCsvParser.ParseCsvLine(lines[i]);
            Assert.Equal(8, fields.Length);
            Assert.Equal("MOVE", fields[3]); // action field intact
        }
    }

    // ═══ F-4 (P1): RollbackService Partial-Failure Counting ════
    // Failed count must equal parseable row count, not just 1

    [Fact]
    public void F04_RollbackService_IntegrityFailure_FailedCountEqualsRowCount()
    {
        // Create an audit CSV with 5 valid MOVE rows
        var auditPath = Path.Combine(_tempDir, "rollback-audit.csv");
        var sb = new StringBuilder();
        sb.AppendLine("RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp");
        for (var i = 0; i < 5; i++)
        {
            sb.AppendLine($@"C:\roms,C:\roms\file_{i}.zip,C:\roms\trash\file_{i}.zip,MOVE,Game,hash_{i},dedupe,2026-04-13T10:00:00Z");
        }
        File.WriteAllText(auditPath, sb.ToString());

        // Create a tampered/invalid sidecar → integrity check fails
        var sidecarPath = auditPath + ".meta.json";
        File.WriteAllText(sidecarPath, "{\"tampered\": true}");

        // Execute rollback → Failed should be 5 (all parseable rows), not 1
        var result = RollbackService.Execute(auditPath, [@"C:\roms"]);

        // Key assertion: Failed must reflect the actual number of affected rows
        Assert.Equal(5, result.Failed);
        Assert.False(result.DryRun);
    }

    [Fact]
    public void F04_RollbackService_NoSidecar_FailedCountEqualsRowCount()
    {
        // Audit CSV with 3 rows but NO sidecar file at all
        var auditPath = Path.Combine(_tempDir, "rollback-no-sidecar.csv");
        var sb = new StringBuilder();
        sb.AppendLine("RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp");
        for (var i = 0; i < 3; i++)
        {
            sb.AppendLine($@"C:\roms,C:\roms\file_{i}.zip,C:\roms\trash\file_{i}.zip,MOVE,Game,hash_{i},dedupe,2026-04-13T10:00:00Z");
        }
        File.WriteAllText(auditPath, sb.ToString());

        // No sidecar file → rollback should still return correct count in Failed
        // The signing service will block rollback due to missing sidecar
        var result = RollbackService.Execute(auditPath, [@"C:\roms"]);

        // Without sidecar, AuditSigningService.Rollback blocks and returns Failed = row count
        Assert.True(result.Failed >= 3,
            $"Expected Failed >= 3 for 3-row audit without sidecar, got {result.Failed}");
    }

    // ═══ F-6 (P1): Set-Member Partial-Move ════
    // CUE+BIN Set, BIN-Preflight-Error → both stay in place

    [Fact]
    public void F06_SetMember_BINPreflightFail_CUEAndBINStayInPlace()
    {
        var root = Path.Combine(_tempDir, "set-f6");
        Directory.CreateDirectory(root);

        // Create CUE that references 2 BIN files
        var cue = CreateSizedFile(root, "game.cue", 64,
            "FILE \"game (track 1).bin\" BINARY\n" +
            "  TRACK 01 MODE1/2352\n" +
            "    INDEX 01 00:00:00\n" +
            "FILE \"game (track 2).bin\" BINARY\n" +
            "  TRACK 02 MODE1/2352\n" +
            "    INDEX 01 00:00:00\n");
        var bin1 = CreateSizedFile(root, "game (track 1).bin", 16);
        var bin2 = CreateSizedFile(root, "game (track 2).bin", 20);

        // Pre-create conflict for BIN1 at destination → preflight detects conflict → skip whole set
        var trashDir = Path.Combine(root, RunConstants.WellKnownFolders.TrashRegionDedupe);
        Directory.CreateDirectory(trashDir);
        File.WriteAllText(Path.Combine(trashDir, "game (track 1).bin"), "conflict");

        var fs = new Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new TrackingAuditStore();
        var options = new RunOptions
        {
            Roots = [root],
            Mode = "Move",
            ConflictPolicy = "Skip",
            AuditPath = Path.Combine(_tempDir, "audit-f6.csv")
        };

        var group = new DedupeGroup
        {
            GameKey = "set-f6",
            Winner = MakeCandidate(Path.Combine(root, "winner.zip")),
            Losers =
            [
                new RomCandidate
                {
                    MainPath = cue,
                    GameKey = "set-f6",
                    Region = "US",
                    RegionScore = 1000,
                    FormatScore = 500,
                    VersionScore = 100,
                    SizeBytes = 64,
                    Extension = ".cue",
                    ConsoleKey = "PSX",
                    Category = FileCategory.Game
                }
            ]
        };

        var result = new MovePipelinePhase().Execute(
            new MovePhaseInput([group], options),
            MakeContext(options, fs, audit),
            CancellationToken.None);

        // All files must stay in place
        Assert.Equal(0, result.MoveCount);
        Assert.Equal(1, result.FailCount);
        Assert.True(File.Exists(cue), "CUE descriptor must stay in place");
        Assert.True(File.Exists(bin1), "BIN track 1 must stay in place");
        Assert.True(File.Exists(bin2), "BIN track 2 must stay in place");

        // CUE must NOT be in trash
        Assert.False(File.Exists(Path.Combine(trashDir, "game.cue")),
            "CUE must NOT be moved to trash when set preflight fails");
    }

    // ═══ F-13 (P2): Concurrent RollbackService ════
    // Paralleler Rollback → keine Race-Condition

    [Fact]
    public async Task F13_AuditCsvStore_ConcurrentRollback_NoException()
    {
        // Setup: create audit CSV with some rows via AuditCsvStore
        var auditPath = Path.Combine(_tempDir, "concurrent-rollback.csv");
        var store = new AuditCsvStore();

        for (var i = 0; i < 5; i++)
        {
            store.AppendAuditRow(
                auditPath,
                rootPath: _tempDir,
                oldPath: Path.Combine(_tempDir, $"file_{i}.zip"),
                newPath: Path.Combine(_tempDir, "trash", $"file_{i}.zip"),
                action: "MOVE",
                category: "Game");
        }

        // Two parallel DryRun rollbacks must not throw or corrupt state
        var exceptions = new List<Exception>();
        var tasks = Enumerable.Range(0, 2).Select(_ =>
            Task.Run(() =>
            {
                try
                {
                    store.Rollback(
                        auditPath,
                        [_tempDir],
                        [_tempDir],
                        dryRun: true);
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                        exceptions.Add(ex);
                }
            })).ToArray();

        await Task.WhenAll(tasks);

        // No exceptions from parallel rollback
        Assert.Empty(exceptions);

        // Audit file must still exist and be readable
        Assert.True(File.Exists(auditPath));
        var lines = File.ReadAllLines(auditPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        Assert.True(lines.Length >= 5, "Audit file must still contain all rows after concurrent rollback");
    }

    // ═══ Helpers ════════════════════════════════════════════════════════

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

    private static RomCandidate MakeCandidate(string path) => new()
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
        Extension = Path.GetExtension(path),
        ConsoleKey = "GENERIC",
        Category = FileCategory.Game
    };

    private static PipelineContext MakeContext(RunOptions options, IFileSystem fs, IAuditStore audit)
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

    // ═══ Mock Implementations ════════════════════════════════════════

    /// <summary>Simulates partial failure: all moves succeed except FailSourcePath.</summary>
    private sealed class PartialFailureFs : IFileSystem
    {
        public required string FailSourcePath { get; init; }
        public required Dictionary<string, string> MoveResults { get; init; }

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
            => [];

        public string? MoveItemSafely(string sourcePath, string destinationPath)
        {
            if (string.Equals(sourcePath, FailSourcePath, StringComparison.OrdinalIgnoreCase))
                return null; // Simulate move failure

            return MoveResults.TryGetValue(sourcePath, out var moved) ? moved : null;
        }

        public bool MoveDirectorySafely(string sourcePath, string destinationPath) => true;

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.GetFullPath(Path.Combine(rootPath, relativePath));

        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }

    /// <summary>Audit store that tracks all appended actions for verification.</summary>
    private sealed class TrackingAuditStore : IAuditStore
    {
        public List<string> Rows { get; } = [];
        public int FlushCalls { get; private set; }
        public int SidecarCalls { get; private set; }

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
            => SidecarCalls++;

        public bool TestMetadataSidecar(string auditCsvPath) => true;

        public void Flush(string auditCsvPath) => FlushCalls++;

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
            string[] allowedCurrentRoots, bool dryRun = false)
            => [];

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "",
            string reason = "")
            => Rows.Add(action);
    }
}
