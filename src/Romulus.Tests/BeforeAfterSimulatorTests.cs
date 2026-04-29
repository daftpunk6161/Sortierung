using System.IO;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave 5 — T-W5-BEFORE-AFTER-SIMULATOR. Pin tests for the
/// <see cref="BeforeAfterSimulator"/>: parity with the canonical
/// <c>RunOrchestrator</c> (Single Source of Truth invariant), correct
/// projection of winners/losers/conversions/renames, and forced DryRun mode.
/// </summary>
public sealed class BeforeAfterSimulatorTests : IDisposable
{
    private readonly string _baseDir;
    private readonly string _root;

    public BeforeAfterSimulatorTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "RomulusBASim_" + Guid.NewGuid().ToString("N")[..8]);
        _root = Path.Combine(_baseDir, "lib");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_baseDir)) Directory.Delete(_baseDir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best-effort */ }
    }

    private string CreateRom(string fileName, int sizeBytes)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    private RunOptions BuildOptions(string mode) => new()
    {
        Roots = new[] { _root },
        Extensions = new[] { ".zip" },
        Mode = mode,
        PreferRegions = new[] { "US" },
        AuditPath = Path.Combine(_baseDir, "audit", "audit.csv"),
        ReportPath = Path.Combine(_baseDir, "report", "run.html"),
        TrashRoot = Path.Combine(_baseDir, "trash")
    };

    private static RunResult RunDryRun(RunOptions options)
    {
        var fs = new FileSystemAdapter();
        var auditStore = new AuditCsvStore(fs);
        var orch = new RunOrchestrator(fs, auditStore);
        return orch.Execute(options);
    }

    // ─────────────────────────────────────────────────────────────────
    // Invariant 1 (Single Source of Truth): simulator MUST drive its
    // projection from the canonical RunOrchestrator. The UnderlyingPlan
    // surfaced via the result must have identical winner/loser counts to
    // a parallel direct DryRun invocation against the same options.
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Simulate_PlanMatchesDirectDryRun_SingleSourceOfTruth()
    {
        CreateRom("Mario (USA).zip", 100);
        CreateRom("Mario (Europe).zip", 100);
        CreateRom("Mario (Japan).zip", 100);

        var options = BuildOptions(mode: RunConstants.ModeDryRun);

        var direct = RunDryRun(options);
        var simulator = new BeforeAfterSimulator((opts, ct) => RunDryRun(opts));
        var sim = simulator.Simulate(options);

        Assert.Equal(direct.GroupCount, sim.UnderlyingPlan.GroupCount);
        Assert.Equal(direct.WinnerCount, sim.UnderlyingPlan.WinnerCount);
        Assert.Equal(direct.LoserCount, sim.UnderlyingPlan.LoserCount);
        Assert.Equal(direct.TotalFilesScanned, sim.UnderlyingPlan.TotalFilesScanned);
    }

    // ─────────────────────────────────────────────────────────────────
    // Invariant 2: even if caller passes Mode=Move, the simulator MUST
    // force DryRun. ForceDryRun is the chokepoint; verify by asserting
    // no MoveResult is materialised and source files remain.
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Simulate_ForcesDryRunEvenWhenCallerPassesMoveMode()
    {
        var winner = CreateRom("Sonic (USA).zip", 100);
        var loser = CreateRom("Sonic (Europe).zip", 100);

        var moveOptions = BuildOptions(mode: RunConstants.ModeMove);

        var simulator = new BeforeAfterSimulator((opts, ct) =>
        {
            // Prove the chokepoint actually overrode the mode before calling pipeline.
            Assert.Equal(RunConstants.ModeDryRun, opts.Mode);
            return RunDryRun(opts);
        });

        var sim = simulator.Simulate(moveOptions);

        Assert.Null(sim.UnderlyingPlan.MoveResult);
        Assert.True(File.Exists(winner));
        Assert.True(File.Exists(loser));
    }

    // ─────────────────────────────────────────────────────────────────
    // Invariant 3: 1 group with 1 winner + 2 losers projects to
    // 1 Keep + 2 Remove entries; summary counts match.
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Simulate_DedupeGroup_ProjectsKeepAndRemoveEntries()
    {
        var winner = CreateRom("Tetris (USA).zip", 100);
        var loserEu = CreateRom("Tetris (Europe).zip", 100);
        var loserJp = CreateRom("Tetris (Japan).zip", 100);

        var simulator = new BeforeAfterSimulator((opts, ct) => RunDryRun(opts));
        var sim = simulator.Simulate(BuildOptions(RunConstants.ModeDryRun));

        Assert.Equal(3, sim.Summary.TotalBefore);
        Assert.Equal(1, sim.Summary.TotalAfter);
        Assert.Equal(1, sim.Summary.Kept);
        Assert.Equal(2, sim.Summary.Removed);
        Assert.Equal(0, sim.Summary.Converted);
        Assert.Equal(0, sim.Summary.Renamed);

        var keep = Assert.Single(sim.Items, i => i.Action == BeforeAfterAction.Keep);
        Assert.Equal(winner, keep.SourcePath);
        Assert.Equal(winner, keep.TargetPath);

        var removes = sim.Items.Where(i => i.Action == BeforeAfterAction.Remove).ToList();
        Assert.Equal(2, removes.Count);
        Assert.All(removes, r => Assert.Null(r.TargetPath));
        Assert.Contains(removes, r => r.SourcePath == loserEu);
        Assert.Contains(removes, r => r.SourcePath == loserJp);

        // PotentialSavedBytes must equal sum of removed source sizes.
        Assert.Equal(200L, sim.Summary.PotentialSavedBytes);
    }

    // ─────────────────────────────────────────────────────────────────
    // Invariant 4: empty library -> empty projection, no exceptions.
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Simulate_EmptyLibrary_ReturnsEmptyProjection()
    {
        var simulator = new BeforeAfterSimulator((opts, ct) => RunDryRun(opts));
        var sim = simulator.Simulate(BuildOptions(RunConstants.ModeDryRun));

        Assert.Empty(sim.Items);
        Assert.Equal(0, sim.Summary.TotalBefore);
        Assert.Equal(0, sim.Summary.TotalAfter);
        Assert.Equal(0L, sim.Summary.PotentialSavedBytes);
    }

    // ─────────────────────────────────────────────────────────────────
    // Invariant 5: ForceDryRun must copy every public init property of
    // RunOptions (drift guard). If a new property is added to RunOptions
    // and ForceDryRun forgets it, this test fails.
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void ForceDryRun_CopiesEveryRunOptionsProperty()
    {
        var source = new RunOptions
        {
            Roots = new[] { @"C:\rom" },
            Mode = RunConstants.ModeMove,
            PreferRegions = new[] { "JP", "EU" },
            Extensions = new[] { ".zip", ".7z" },
            RemoveJunk = false,
            OnlyGames = true,
            KeepUnknownWhenOnlyGames = false,
            AggressiveJunk = true,
            SortConsole = true,
            EnableDat = true,
            EnableDatAudit = true,
            EnableDatRename = true,
            DatRoot = @"C:\dat",
            HashType = "SHA256",
            ConvertFormat = "chd",
            ConvertOnly = true,
            ApproveReviews = true,
            ApproveConversionReview = true,
            TrashRoot = @"C:\trash",
            AuditPath = @"C:\audit\audit.csv",
            ReportPath = @"C:\rep\r.html",
            ConflictPolicy = "Overwrite",
            AllowHeuristicFallback = true,
            AcceptDataLossToken = "token-xyz"
        };

        var clone = BeforeAfterSimulator.ForceDryRun(source);

        // Mode must be forced; everything else must be preserved.
        Assert.Equal(RunConstants.ModeDryRun, clone.Mode);

        var props = typeof(RunOptions)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

        foreach (var prop in props)
        {
            if (prop.Name == nameof(RunOptions.Mode)) continue;
            var sourceVal = prop.GetValue(source);
            var cloneVal = prop.GetValue(clone);

            if (sourceVal is System.Collections.IEnumerable srcEnum && sourceVal is not string)
            {
                var cloneEnum = (System.Collections.IEnumerable)cloneVal!;
                Assert.Equal(srcEnum.Cast<object>().ToList(), cloneEnum.Cast<object>().ToList());
            }
            else
            {
                Assert.Equal(sourceVal, cloneVal);
            }
        }
    }
}
