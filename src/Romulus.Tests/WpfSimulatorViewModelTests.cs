using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// T-W5-BEFORE-AFTER-SIMULATOR pass 4 — pin tests for the WPF
/// <c>SimulatorViewModel</c>. The view-model MUST consume
/// <see cref="IBeforeAfterSimulator"/> directly (single source of truth);
/// it is forbidden to recompute summary numbers or filter logic locally
/// because that would re-introduce parallel projections forbidden by
/// project rules ("eine fachliche Wahrheit").
/// </summary>
public sealed class WpfSimulatorViewModelTests
{
    private static RunOptions BuildOptions(string mode) => new()
    {
        Roots = new[] { @"C:\fake-root" },
        Mode = mode,
        PreferRegions = new[] { "US" }
    };

    private static BeforeAfterSimulationResult MakeResult(
        IReadOnlyList<BeforeAfterEntry> items,
        BeforeAfterSummary? summary = null,
        RunResult? plan = null)
    {
        summary ??= new BeforeAfterSummary(0, 0, 0, 0, 0, 0, 0L);
        plan ??= new RunResult();
        return new BeforeAfterSimulationResult(items, summary, plan);
    }

    private sealed class StubSimulator : IBeforeAfterSimulator
    {
        public Func<RunOptions, BeforeAfterSimulationResult> Producer { get; set; } = _ => MakeResult(Array.Empty<BeforeAfterEntry>());
        public int CallCount { get; private set; }
        public RunOptions? LastOptions { get; private set; }

        public BeforeAfterSimulationResult Simulate(RunOptions options, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastOptions = options;
            return Producer(options);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Invariant 1: brand-new VM has no items and a zeroed summary.
    // Refresh on an empty-result simulator must keep the lists empty
    // and the summary zero — no synthetic placeholders.
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Refresh_EmptySimulatorResult_KeepsBeforeAndAfterListsEmpty()
    {
        var stub = new StubSimulator();
        var vm = new SimulatorViewModel(stub);

        Assert.Empty(vm.BeforeItems);
        Assert.Empty(vm.AfterItems);
        Assert.Equal(0, vm.Summary.TotalBefore);

        vm.Refresh(BuildOptions(RunConstants.ModeDryRun));

        Assert.Empty(vm.BeforeItems);
        Assert.Empty(vm.AfterItems);
        Assert.Equal(0, vm.Summary.TotalBefore);
        Assert.Equal(0, vm.Summary.TotalAfter);
        Assert.Equal(0L, vm.Summary.PotentialSavedBytes);
    }

    // ─────────────────────────────────────────────────────────────────
    // Invariant 2: After the simulator returns Keep + Remove + Convert +
    // Rename, the BeforeItems list must contain ALL source paths and the
    // AfterItems list must EXCLUDE Remove entries (those are gone in the
    // projected after-state) and KEEP everything else.
    // This is the two-list before/after diff contract.
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Refresh_TwoListProjection_BeforeContainsAllAfterExcludesRemoved()
    {
        var keep = new BeforeAfterEntry("C:/lib/keep.zip", "C:/lib/keep.zip", BeforeAfterAction.Keep, 100);
        var remove = new BeforeAfterEntry("C:/lib/dupe.zip", null, BeforeAfterAction.Remove, 50);
        var convert = new BeforeAfterEntry("C:/lib/old.cue", "C:/lib/old.chd", BeforeAfterAction.Convert, 200);
        var rename = new BeforeAfterEntry("C:/lib/wrong.zip", "C:/lib/right.zip", BeforeAfterAction.Rename, 0);

        var stub = new StubSimulator
        {
            Producer = _ => MakeResult(
                new[] { keep, remove, convert, rename },
                new BeforeAfterSummary(TotalBefore: 4, TotalAfter: 3, Kept: 1, Removed: 1, Converted: 1, Renamed: 1, PotentialSavedBytes: 50L))
        };

        var vm = new SimulatorViewModel(stub);
        vm.Refresh(BuildOptions(RunConstants.ModeDryRun));

        // BeforeItems = every source path that exists today.
        Assert.Equal(4, vm.BeforeItems.Count);
        Assert.Contains(vm.BeforeItems, e => e.SourcePath == keep.SourcePath);
        Assert.Contains(vm.BeforeItems, e => e.SourcePath == remove.SourcePath);
        Assert.Contains(vm.BeforeItems, e => e.SourcePath == convert.SourcePath);
        Assert.Contains(vm.BeforeItems, e => e.SourcePath == rename.SourcePath);

        // AfterItems = projected post-state. Remove entries are gone.
        Assert.Equal(3, vm.AfterItems.Count);
        Assert.DoesNotContain(vm.AfterItems, e => e.Action == BeforeAfterAction.Remove);
        Assert.DoesNotContain(vm.AfterItems, e => e.SourcePath == remove.SourcePath);
        // Convert/Rename appear in After at their TargetPath, not their SourcePath.
        Assert.Contains(vm.AfterItems, e => e.SourcePath == convert.SourcePath);
        Assert.Contains(vm.AfterItems, e => e.SourcePath == rename.SourcePath);
        Assert.Contains(vm.AfterItems, e => e.SourcePath == keep.SourcePath);
    }

    // ─────────────────────────────────────────────────────────────────
    // Invariant 3: Summary numbers must come straight from the simulator
    // result — never re-derived inside the VM. Pin: a deliberately
    // inconsistent summary (numbers do NOT match the items list) must
    // surface unchanged, proving the VM is a pure projection consumer.
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Refresh_SummaryComesFromSimulator_NoParallelRecalculation()
    {
        var bogus = new BeforeAfterSummary(
            TotalBefore: 999, TotalAfter: 42, Kept: 7, Removed: 6,
            Converted: 5, Renamed: 4, PotentialSavedBytes: 1234567L);
        var stub = new StubSimulator
        {
            Producer = _ => MakeResult(
                items: new[] { new BeforeAfterEntry("a.zip", "a.zip", BeforeAfterAction.Keep, 1) },
                summary: bogus)
        };

        var vm = new SimulatorViewModel(stub);
        vm.Refresh(BuildOptions(RunConstants.ModeDryRun));

        Assert.Equal(999, vm.Summary.TotalBefore);
        Assert.Equal(42, vm.Summary.TotalAfter);
        Assert.Equal(7, vm.Summary.Kept);
        Assert.Equal(6, vm.Summary.Removed);
        Assert.Equal(5, vm.Summary.Converted);
        Assert.Equal(4, vm.Summary.Renamed);
        Assert.Equal(1234567L, vm.Summary.PotentialSavedBytes);
    }

    // ─────────────────────────────────────────────────────────────────
    // Invariant 4: VM forwards the caller's RunOptions verbatim to the
    // simulator. The simulator (not the VM) owns the ForceDryRun
    // chokepoint; the VM must NOT pre-mutate the options. Pin: pass
    // Mode=Move and assert the simulator received Move (the simulator
    // will then internally force DryRun — that is its job, not the VM's).
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Refresh_ForwardsOptionsVerbatim_DoesNotPreMutateMode()
    {
        var stub = new StubSimulator();
        var vm = new SimulatorViewModel(stub);

        var moveOptions = BuildOptions(RunConstants.ModeMove);
        vm.Refresh(moveOptions);

        Assert.Equal(1, stub.CallCount);
        Assert.NotNull(stub.LastOptions);
        Assert.Equal(RunConstants.ModeMove, stub.LastOptions!.Mode);
    }

    // ─────────────────────────────────────────────────────────────────
    // Invariant 5: subsequent Refresh calls REPLACE prior state — no
    // stale items leak across simulations. Run once with 3 items, then
    // again with 1 item, and assert the lists hold exactly the new set.
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Refresh_TwoConsecutiveCalls_ReplacesPreviousProjection()
    {
        var firstBatch = new[]
        {
            new BeforeAfterEntry("a.zip", "a.zip", BeforeAfterAction.Keep, 1),
            new BeforeAfterEntry("b.zip", null, BeforeAfterAction.Remove, 1),
            new BeforeAfterEntry("c.zip", null, BeforeAfterAction.Remove, 1),
        };
        var firstSummary = new BeforeAfterSummary(3, 1, 1, 2, 0, 0, 2L);

        var secondBatch = new[]
        {
            new BeforeAfterEntry("z.zip", "z.zip", BeforeAfterAction.Keep, 9),
        };
        var secondSummary = new BeforeAfterSummary(1, 1, 1, 0, 0, 0, 0L);

        var queue = new Queue<BeforeAfterSimulationResult>(new[]
        {
            MakeResult(firstBatch, firstSummary),
            MakeResult(secondBatch, secondSummary)
        });
        var stub = new StubSimulator { Producer = _ => queue.Dequeue() };

        var vm = new SimulatorViewModel(stub);
        vm.Refresh(BuildOptions(RunConstants.ModeDryRun));
        Assert.Equal(3, vm.BeforeItems.Count);

        vm.Refresh(BuildOptions(RunConstants.ModeDryRun));

        Assert.Single(vm.BeforeItems);
        Assert.Single(vm.AfterItems);
        Assert.Equal("z.zip", vm.BeforeItems[0].SourcePath);
        Assert.Equal(1, vm.Summary.TotalBefore);
        Assert.Equal(0L, vm.Summary.PotentialSavedBytes);
    }
}
