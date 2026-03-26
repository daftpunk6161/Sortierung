using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Metrics;
using Xunit;

namespace RomCleanup.Tests;

/// <summary>
/// Phase 5B invariant tests covering:
/// - TASK-151: hasErrors formula includes DatRename + ConsoleSort failures
/// - TASK-154: Non-cancel exceptions produce RunOutcome.Failed + ExitCode 4
/// - TASK-074/075: PhasePlanBuilder ordering and conditional phases
/// - TASK-168: Set-member integrity (CUE→BIN co-move)
/// - ActionPhaseStep generic delegate pattern
/// </summary>
public class Phase5BPipelineInvariantTests
{
    // ═══════════════════════════════════════════════════════════════════
    // TASK-151: hasErrors formula
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void HasErrors_DatRenameFailure_ProducesCompletedWithErrors()
    {
        // The hasErrors formula must include DatRenameFailedCount > 0
        var result = new RunResult
        {
            DatRenameFailedCount = 3,
            Status = "ok", // before fix this would stay "ok"
        };

        var projection = RunProjectionFactory.Create(result);

        // FailCount must include DatRename failures
        Assert.True(projection.FailCount > 0,
            "FailCount must include DatRename failures");
        Assert.Equal(3, projection.DatRenameFailedCount);
    }

    [Fact]
    public void HasErrors_ConsoleSortFailure_ProducesCompletedWithErrors()
    {
        var emptyReasons = (IReadOnlyDictionary<string, int>)new Dictionary<string, int>();
        var result = new RunResult
        {
            ConsoleSortResult = new ConsoleSortResult(
                Total: 10, Moved: 5, SetMembersMoved: 0, Skipped: 0,
                Unknown: 0, UnknownReasons: emptyReasons, Failed: 3),
            Status = "ok",
        };

        var projection = RunProjectionFactory.Create(result);

        Assert.True(projection.FailCount > 0,
            "FailCount must include ConsoleSort failures");
        Assert.Equal(3, projection.ConsoleSortFailed);
    }

    [Theory]
    [InlineData(1, 0, 0, 0, 0, true)]  // ConvertError only
    [InlineData(0, 1, 0, 0, 0, true)]  // MoveResult fail only
    [InlineData(0, 0, 1, 0, 0, true)]  // JunkMoveResult fail only
    [InlineData(0, 0, 0, 1, 0, true)]  // DatRename fail only
    [InlineData(0, 0, 0, 0, 1, true)]  // ConsoleSort fail only
    [InlineData(0, 0, 0, 0, 0, false)] // No errors at all
    public void HasErrors_AllErrorSources_ContributeToFailCount(
        int convertErrors, int moveFails, int junkFails, int datRenameFails, int consoleSortFails,
        bool expectErrors)
    {
        var emptyReasons = (IReadOnlyDictionary<string, int>)new Dictionary<string, int>();
        var result = new RunResult
        {
            ConvertErrorCount = convertErrors,
            MoveResult = moveFails > 0 ? new MovePhaseResult(0, moveFails, 0) : null,
            JunkMoveResult = junkFails > 0 ? new MovePhaseResult(0, junkFails, 0) : null,
            DatRenameFailedCount = datRenameFails,
            ConsoleSortResult = consoleSortFails > 0
                ? new ConsoleSortResult(10, 0, 0, 0, 0, emptyReasons, consoleSortFails)
                : null,
        };

        var projection = RunProjectionFactory.Create(result);

        if (expectErrors)
            Assert.True(projection.FailCount > 0, $"FailCount should be > 0 for error combination");
        else
            Assert.Equal(0, projection.FailCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TASK-154: Non-cancel exception handling
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Execute_IOException_ReturnsFailed_ExitCode4()
    {
        // ThrowingFileSystem passes Preflight (TestPath returns true)
        // but explodes on GetFilesSafe during scan phase
        var fs = new ThrowingFileSystem(new IOException("Disk full"));
        var orch = new RunOrchestrator(fs, new FakeAuditStore());

        var tempDir = Path.Combine(Path.GetTempPath(), $"P5B_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new RunOptions
            {
                Roots = [tempDir],
                Extensions = [".zip"],
                Mode = "DryRun"
            };

            var result = orch.Execute(options);

            Assert.Equal("failed", result.Status);
            Assert.Equal(4, result.ExitCode);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Execute_NullReferenceException_ReturnsFailed_NotThrown()
    {
        var fs = new ThrowingFileSystem(new NullReferenceException("Simulated bug"));
        var orch = new RunOrchestrator(fs, new FakeAuditStore());

        var tempDir = Path.Combine(Path.GetTempPath(), $"P5B_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var options = new RunOptions
            {
                Roots = [tempDir],
                Extensions = [".zip"],
                Mode = "DryRun"
            };

            var result = orch.Execute(options);

            Assert.Equal("failed", result.Status);
            Assert.Equal(4, result.ExitCode);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Execute_OperationCanceledException_ReturnsCancelled_ExitCode2()
    {
        // Ensure cancel path is still handled correctly alongside the new non-cancel handler
        var fs = new FakeFileSystem();
        var tempDir = Path.Combine(Path.GetTempPath(), $"P5B_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            fs.ExistingPaths.Add(tempDir);
            fs.FileLists[tempDir] = new List<string>();

            var cts = new CancellationTokenSource();
            cts.Cancel();

            var orch = new RunOrchestrator(fs, new FakeAuditStore());
            var options = new RunOptions
            {
                Roots = [tempDir],
                Extensions = [".zip"],
                Mode = "DryRun"
            };

            var result = orch.Execute(options, cts.Token);

            Assert.Equal("cancelled", result.Status);
            Assert.Equal(2, result.ExitCode);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TASK-074/075: PhasePlanBuilder ordering and conditional phases
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PhasePlanBuilder_MoveMode_AllEnabled_ProducesCorrectOrder()
    {
        var builder = new PhasePlanBuilder();
        var options = new RunOptions
        {
            Mode = "Move",
            EnableDatAudit = true,
            EnableDatRename = true,
            SortConsole = true,
            ConvertFormat = "chd"
        };

        var actions = CreateNoOpActions();
        var phases = builder.Build(options, actions);

        var names = phases.Select(p => p.Name).ToArray();

        Assert.Equal(
            ["DatAudit", "Deduplicate", "JunkRemoval", "DatRename", "Move", "ConsoleSort", "WinnerConversion"],
            names);
    }

    [Fact]
    public void PhasePlanBuilder_DryRunMode_ExcludesMoveSortConvert()
    {
        var builder = new PhasePlanBuilder();
        var options = new RunOptions
        {
            Mode = "DryRun",
            EnableDatAudit = true,
            EnableDatRename = true,
            SortConsole = true,
            ConvertFormat = "chd"
        };

        var actions = CreateNoOpActions();
        var phases = builder.Build(options, actions);

        var names = phases.Select(p => p.Name).ToArray();

        Assert.Equal(["DatAudit", "Deduplicate", "JunkRemoval"], names);
        Assert.DoesNotContain("Move", names);
        Assert.DoesNotContain("ConsoleSort", names);
        Assert.DoesNotContain("WinnerConversion", names);
    }

    [Fact]
    public void PhasePlanBuilder_NoDatAudit_OmitsDatPhases()
    {
        var builder = new PhasePlanBuilder();
        var options = new RunOptions
        {
            Mode = "Move",
            EnableDatAudit = false,
            EnableDatRename = false
        };

        var actions = CreateNoOpActions();
        var phases = builder.Build(options, actions);

        var names = phases.Select(p => p.Name).ToArray();

        Assert.DoesNotContain("DatAudit", names);
        Assert.DoesNotContain("DatRename", names);
        Assert.Contains("Deduplicate", names);
        Assert.Contains("Move", names);
    }

    [Fact]
    public void PhasePlanBuilder_DeduplicateAndJunkRemoval_AlwaysPresent()
    {
        var builder = new PhasePlanBuilder();
        var options = new RunOptions { Mode = "DryRun" };

        var actions = CreateNoOpActions();
        var phases = builder.Build(options, actions);

        var names = phases.Select(p => p.Name).ToArray();

        Assert.Contains("Deduplicate", names);
        Assert.Contains("JunkRemoval", names);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ActionPhaseStep: generic delegate pattern
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ActionPhaseStep_ExecutesDelegateAndReturnsResult()
    {
        var executed = false;
        var step = new ActionPhaseStep("TestPhase", (state, ct) =>
        {
            executed = true;
            return PhaseStepResult.Ok(42);
        });

        Assert.Equal("TestPhase", step.Name);

        var result = step.Execute(new PipelineState(), CancellationToken.None);

        Assert.True(executed);
        Assert.Equal("ok", result.Status);
        Assert.Equal(42, result.ItemCount);
    }

    [Fact]
    public void ActionPhaseStep_Name_MatchesConstructorArgument()
    {
        var step = new ActionPhaseStep("CustomName", (_, _) => PhaseStepResult.Skipped());

        Assert.Equal("CustomName", step.Name);

        var result = step.Execute(new PipelineState(), CancellationToken.None);
        Assert.Equal("skipped", result.Status);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TASK-168: Set-member integrity in MovePipelinePhase
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void MovePhase_CueLoser_CoMovesBinMember()
    {
        // Setup: CUE file references a BIN file
        var tempDir = Path.Combine(Path.GetTempPath(), $"P5BSet_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var cuePath = Path.Combine(tempDir, "Game (Europe).cue");
            var binPath = Path.Combine(tempDir, "Game (Europe).bin");

            File.WriteAllText(cuePath, $"FILE \"{Path.GetFileName(binPath)}\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n");
            File.WriteAllBytes(binPath, new byte[2352]);

            var fs = new FileSystemAdapter();
            var audit = new FakeAuditStore();
            var metrics = new PhaseMetricsCollector();
            metrics.Initialize();
            var context = new PipelineContext
            {
                Options = new RunOptions
                {
                    Roots = [tempDir],
                    Mode = "Move",
                    ConflictPolicy = "Rename"
                },
                FileSystem = fs,
                AuditStore = audit,
                Metrics = metrics
            };

            var group = new DedupeGroup
            {
                GameKey = "Game",
                Winner = new RomCandidate { MainPath = "dummy_winner.cue", GameKey = "Game" },
                Losers = new List<RomCandidate>
                {
                    new RomCandidate
                    {
                        MainPath = cuePath,
                        GameKey = "Game",
                        Extension = ".cue",
                        SizeBytes = 100,
                        Category = FileCategory.Game
                    }
                }
            };

            var phase = new MovePipelinePhase();
            var result = phase.Execute(
                new MovePhaseInput(new[] { group }, context.Options),
                context,
                CancellationToken.None);

            // CUE was moved
            Assert.True(result.MoveCount >= 1, "CUE loser should have been moved");

            // BIN member was co-moved
            Assert.False(File.Exists(binPath), "BIN member should have been co-moved to trash (no longer at original location)");

            // Verify trash directory was created
            var trashDir = Path.Combine(tempDir, "_TRASH_REGION_DEDUPE");
            Assert.True(Directory.Exists(trashDir), "Trash directory should exist");

            // BIN should be in trash
            var trashBin = Path.Combine(trashDir, "Game (Europe).bin");
            Assert.True(File.Exists(trashBin), "BIN member should be in trash directory");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MovePhase_NonDescriptorLoser_DoesNotTriggerSetCoMove()
    {
        // A .zip loser (not a set descriptor) should not trigger set member resolution
        var tempDir = Path.Combine(Path.GetTempPath(), $"P5BNoSet_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var zipPath = Path.Combine(tempDir, "Game (Europe).zip");
            File.WriteAllBytes(zipPath, new byte[100]);

            var fs = new FileSystemAdapter();
            var audit = new FakeAuditStore();
            var metrics2 = new PhaseMetricsCollector();
            metrics2.Initialize();
            var context = new PipelineContext
            {
                Options = new RunOptions
                {
                    Roots = [tempDir],
                    Mode = "Move",
                    ConflictPolicy = "Rename"
                },
                FileSystem = fs,
                AuditStore = audit,
                Metrics = metrics2
            };

            var group = new DedupeGroup
            {
                GameKey = "Game",
                Winner = new RomCandidate { MainPath = "winner.zip", GameKey = "Game" },
                Losers = new List<RomCandidate>
                {
                    new RomCandidate
                    {
                        MainPath = zipPath,
                        GameKey = "Game",
                        Extension = ".zip",
                        SizeBytes = 100,
                        Category = FileCategory.Game
                    }
                }
            };

            var phase = new MovePipelinePhase();
            var result = phase.Execute(
                new MovePhaseInput(new[] { group }, context.Options),
                context,
                CancellationToken.None);

            Assert.Equal(1, result.MoveCount); // Only the ZIP, no set members
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // PipelineState set-once guard invariants
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PipelineState_DoubleScanSet_Throws()
    {
        var state = new PipelineState();
        state.SetScanOutput(Array.Empty<RomCandidate>(), Array.Empty<RomCandidate>());

        Assert.Throws<InvalidOperationException>(() =>
            state.SetScanOutput(Array.Empty<RomCandidate>(), Array.Empty<RomCandidate>()));
    }

    [Fact]
    public void PipelineState_DoubleDedupeSet_Throws()
    {
        var state = new PipelineState();
        state.SetDedupeOutput(Array.Empty<DedupeGroup>(), Array.Empty<DedupeGroup>());

        Assert.Throws<InvalidOperationException>(() =>
            state.SetDedupeOutput(Array.Empty<DedupeGroup>(), Array.Empty<DedupeGroup>()));
    }

    [Fact]
    public void PipelineState_DoubleJunkSet_Throws()
    {
        var state = new PipelineState();
        state.SetJunkPaths(new HashSet<string>());

        Assert.Throws<InvalidOperationException>(() =>
            state.SetJunkPaths(new HashSet<string>()));
    }

    // ═══════════════════════════════════════════════════════════════════
    // PhaseStepResult factory methods
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PhaseStepResult_Ok_HasCorrectStatus()
    {
        var r = PhaseStepResult.Ok(5, "data");
        Assert.Equal("ok", r.Status);
        Assert.Equal(5, r.ItemCount);
        Assert.Equal("data", r.TypedResult);
    }

    [Fact]
    public void PhaseStepResult_Skipped_HasCorrectStatus()
    {
        var r = PhaseStepResult.Skipped("reason");
        Assert.Equal("skipped", r.Status);
        Assert.Equal(0, r.ItemCount);
        Assert.Equal("reason", r.TypedResult);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static StandardPhaseStepActions CreateNoOpActions() => new()
    {
        DatAudit = (_, _) => PhaseStepResult.Ok(),
        Deduplicate = (_, _) => PhaseStepResult.Ok(),
        JunkRemoval = (_, _) => PhaseStepResult.Ok(),
        DatRename = (_, _) => PhaseStepResult.Ok(),
        Move = (_, _) => PhaseStepResult.Ok(),
        ConsoleSort = (_, _) => PhaseStepResult.Ok(),
        WinnerConversion = (_, _) => PhaseStepResult.Ok()
    };

    // ── Fakes ─────────────────────────────────────────────────────

    /// <summary>
    /// FileSystem that throws on GetFilesSafe — used for TASK-154 non-cancel exception tests.
    /// </summary>
    private sealed class ThrowingFileSystem : IFileSystem
    {
        private readonly Exception _exception;

        public ThrowingFileSystem(Exception exception) => _exception = exception;

        public bool TestPath(string literalPath, string pathType = "Any") => true;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => throw _exception;
        public string? MoveItemSafely(string sourcePath, string destinationPath) => destinationPath;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }

    private sealed class FakeFileSystem : IFileSystem
    {
        public HashSet<string> ExistingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> FileLists { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool TestPath(string literalPath, string pathType = "Any")
            => ExistingPaths.Contains(literalPath);

        public string EnsureDirectory(string path)
        {
            ExistingPaths.Add(path);
            return path;
        }

        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
        {
            if (FileLists.TryGetValue(root, out var list))
                return list;
            return Array.Empty<string>();
        }

        public string? MoveItemSafely(string sourcePath, string destinationPath) => destinationPath;

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
        {
            var full = Path.Combine(rootPath, relativePath);
            return full.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }

    private sealed class FakeAuditStore : IAuditStore
    {
        public List<(string path, IDictionary<string, object> meta)> SidecarLog { get; } = new();
        public List<(string csvPath, string rootPath, string oldPath, string newPath, string action)> AuditRows { get; } = new();

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
            => SidecarLog.Add((auditCsvPath, metadata));

        public bool TestMetadataSidecar(string auditCsvPath) => false;

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
            string[] allowedCurrentRoots, bool dryRun = false) => Array.Empty<string>();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "")
            => AuditRows.Add((auditCsvPath, rootPath, oldPath, newPath, action));

        public void Flush(string auditCsvPath) { }
    }
}
