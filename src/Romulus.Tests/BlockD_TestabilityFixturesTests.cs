using System.Diagnostics;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.FileSystem;
using Romulus.Tests.Benchmark.Infrastructure;
using Romulus.Tests.TestFixtures;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Block D - verification suite for the centralized testability fixtures
/// added in this block. These tests prove the new fixtures behave correctly
/// (Red-Green discipline) and lock in their public contracts so future
/// consumer migrations stay safe.
///
/// One test per fixture - kept small and focused on the fixture itself,
/// not on production logic (production logic stays covered by the suites
/// that will adopt these fixtures, e.g. CrossConsoleDatPolicyTests,
/// DecisionReasonParityTests, UnknownReviewBlockedRoutingTests,
/// AuditABEndToEndRedTests, ...).
/// </summary>
public sealed class BlockD_TestabilityFixturesTests : IDisposable
{
    private readonly string _tempDir;

    public BlockD_TestabilityFixturesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_D_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    // ── D1: EnrichmentTestHarness + FixedFamilyDatPolicyResolver ──────

    [Fact]
    public void D1_EnrichmentTestHarness_BuildContext_HasMetricsInitializedAndOptionsSet()
    {
        var options = EnrichmentTestHarness.DryRunOptions(_tempDir, [".zip"]);
        var ctx = EnrichmentTestHarness.BuildContext(options);

        Assert.Same(options, ctx.Options);
        Assert.NotNull(ctx.FileSystem);
        Assert.NotNull(ctx.AuditStore);
        Assert.NotNull(ctx.Metrics);
    }

    [Fact]
    public void D1_FixedFamilyDatPolicyResolver_ReturnsSamePolicyForEveryFamily()
    {
        var policy = new FamilyDatPolicy(
            PreferArchiveInnerHash: false,
            UseHeaderlessHash: false,
            UseContainerHash: true,
            AllowNameOnlyDatMatch: false,
            RequireStrictNameForNameOnly: true,
            EnableCrossConsoleLookup: false);
        var resolver = new FixedFamilyDatPolicyResolver(policy);

        Assert.Same(policy, resolver.ResolvePolicy(PlatformFamily.NoIntroCartridge, ".smc", "headerless"));
        Assert.Same(policy, resolver.ResolvePolicy(PlatformFamily.RedumpDisc, ".chd", "container"));
        Assert.Same(policy, resolver.ResolvePolicy(PlatformFamily.Arcade, ".zip", "set-archive"));
    }

    // ── D2: RootBoundaryValidator ─────────────────────────────────────

    [Fact]
    public void D2_RootBoundaryValidator_DetectsModificationOfFileOutsideRoots()
    {
        var outside = Path.Combine(_tempDir, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.bin");
        File.WriteAllBytes(sentinel, [1, 2, 3, 4, 5]);

        var validator = new RootBoundaryValidator(outside).Snapshot();

        // Simulate a buggy run that touches a file outside roots
        File.WriteAllBytes(sentinel, [9, 9, 9]);

        var violations = validator.Verify();
        Assert.Single(violations);
        Assert.Contains("modified", violations[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void D2_RootBoundaryValidator_GreenWhenNothingTouched()
    {
        var outside = Path.Combine(_tempDir, "outside-clean");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "a.bin"), [10, 20]);
        File.WriteAllBytes(Path.Combine(outside, "b.bin"), [30, 40]);

        var validator = new RootBoundaryValidator(outside).Snapshot();
        // No-op "run"
        var violations = validator.Verify();
        Assert.Empty(violations);
    }

    [Fact]
    public void D2_RootBoundaryValidator_DetectsNewFileInOutsideRoot()
    {
        var outside = Path.Combine(_tempDir, "outside-new");
        Directory.CreateDirectory(outside);
        var validator = new RootBoundaryValidator(outside).Snapshot();

        File.WriteAllBytes(Path.Combine(outside, "leaked.bin"), [1]);

        var violations = validator.Verify();
        Assert.Single(violations);
        Assert.Contains("appeared", violations[0], StringComparison.OrdinalIgnoreCase);
    }

    // ── D3: ScenarioToolRunner ────────────────────────────────────────

    [Fact]
    public void D3_ScenarioToolRunner_Crash_ThrowsInvalidOperation()
    {
        var runner = ScenarioToolRunner.ForScenario(ConversionFailureScenario.Crash);
        Assert.Throws<InvalidOperationException>(() => runner.InvokeProcess("any.exe", []));
    }

    [Fact]
    public void D3_ScenarioToolRunner_Cancellation_ThrowsOperationCanceled()
    {
        var runner = ScenarioToolRunner.ForScenario(ConversionFailureScenario.Cancellation);
        Assert.Throws<OperationCanceledException>(() => runner.InvokeProcess("any.exe", []));
    }

    [Fact]
    public void D3_ScenarioToolRunner_DiskFull_ReturnsFailureWithErrorCode112()
    {
        var runner = ScenarioToolRunner.ForScenario(ConversionFailureScenario.DiskFull);
        var result = runner.InvokeProcess("any.exe", []);
        Assert.False(result.Success);
        Assert.Equal(112, result.ExitCode);
    }

    [Fact]
    public void D3_ScenarioToolRunner_OutputTooSmall_WritesOneByteOutput()
    {
        var outPath = Path.Combine(_tempDir, "out.chd");
        var runner = ScenarioToolRunner.ForScenario(
            ConversionFailureScenario.OutputTooSmall,
            outputPathProvider: (_, _) => outPath);

        var result = runner.InvokeProcess("chdman.exe", ["createcd"]);

        Assert.True(result.Success);
        Assert.True(File.Exists(outPath));
        Assert.Equal(1, new FileInfo(outPath).Length);
    }

    [Fact]
    public void D3_ScenarioToolRunner_HashMismatch_ReturnsSuccess_CallerVerifiesHash()
    {
        var runner = ScenarioToolRunner.ForScenario(ConversionFailureScenario.HashMismatch);
        var result = runner.InvokeProcess("any.exe", []);
        Assert.True(result.Success);
    }

    // ── D4: RunResultProjection ───────────────────────────────────────

    [Fact]
    public void D4_RunResultProjection_DecisionFields_OrderedDeterministicallyAndCaseNormalized()
    {
        var c1 = new RomCandidate
        {
            GameKey = "Zelda",
            ConsoleKey = "snes",
            PlatformFamily = PlatformFamily.NoIntroCartridge,
            DecisionClass = DecisionClass.Sort,
            SortDecision = SortDecision.Sort,
            ClassificationReasonCode = "dat-exact",
            DatMatch = true
        };
        var c2 = new RomCandidate
        {
            GameKey = "ALPHA",
            ConsoleKey = "nes",
            PlatformFamily = PlatformFamily.NoIntroCartridge,
            DecisionClass = DecisionClass.Review,
            SortDecision = SortDecision.Review,
            ClassificationReasonCode = "ambig-ext",
            DatMatch = false
        };
        var projection = RunResultProjection.DecisionFields([c1, c2]);

        Assert.Equal(2, projection.Count);
        // ordinal sort + lowercased GameKey -> "alpha..." comes before "zelda..."
        Assert.StartsWith("alpha|", projection[0]);
        Assert.StartsWith("zelda|", projection[1]);
        Assert.EndsWith("|NODAT", projection[0]);
        Assert.EndsWith("|DAT", projection[1]);
    }

    [Fact]
    public void D4_RunResultProjection_RoutingTuples_IncludesCategoryButNotDatFlag()
    {
        var winner = new RomCandidate
        {
            ConsoleKey = "psx",
            PlatformFamily = PlatformFamily.RedumpDisc,
            DecisionClass = DecisionClass.Blocked,
            SortDecision = SortDecision.Blocked,
            Category = FileCategory.Junk
        };
        var projection = RunResultProjection.RoutingTuples([("Final Fantasy VII", winner)]);

        Assert.Single(projection);
        Assert.Contains("|Junk", projection[0]);
        Assert.DoesNotContain("DAT", projection[0]);
    }

    // ── D5: DatasetExpander baseline characterisation ────────────────

    [Fact]
    [Trait("Category", "DatasetGeneration")]
    public void D5_DatasetExpander_PublicSurface_StableAcrossFcBuckets()
    {
        // Characterisation test: locks the DatasetExpander public contract
        // (group keys, non-empty buckets, schema-validity) so a future
        // modularisation refactor (D5 follow-up) can be done safely.
        var expander = new DatasetExpander([]);
        var expansion = expander.GenerateExpansion();

        Assert.NotEmpty(expansion);
        // Every emitted bucket must contain at least one entry.
        Assert.All(expansion, kv => Assert.NotEmpty(kv.Value));
        // Every entry must have a non-empty Id and Expected payload.
        var allEntries = expansion.Values.SelectMany(static v => v).ToList();
        Assert.All(allEntries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Id), "Entry Id must not be empty.");
            Assert.NotNull(e.Expected);
        });
        // Distinct Ids - no duplicates across buckets.
        var ids = allEntries.Select(static e => e.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    // ── D6: TraceCapture ──────────────────────────────────────────────

    [Fact]
    public void D6_TraceCapture_ReturnsEmittedTraceLines()
    {
        var captured = TraceCapture.Capture(() =>
        {
            Trace.WriteLine("hello-from-D6");
            Trace.WriteLine("second-line");
        });

        Assert.Contains("hello-from-D6", captured, StringComparison.Ordinal);
        Assert.Contains("second-line", captured, StringComparison.Ordinal);
    }

    [Fact]
    public void D6_TraceCapture_RestoresAutoFlushAndRemovesListener_EvenOnException()
    {
        var listenerCountBefore = Trace.Listeners.Count;
        var autoFlushBefore = Trace.AutoFlush;

        Assert.Throws<InvalidOperationException>(() =>
            TraceCapture.Capture(() => throw new InvalidOperationException("boom")));

        Assert.Equal(listenerCountBefore, Trace.Listeners.Count);
        Assert.Equal(autoFlushBefore, Trace.AutoFlush);
    }

    // ── D2 integration: real FileSystemAdapter.MoveItemSafely ─────────

    [Fact]
    public void D2_RootBoundaryValidator_GreenAfterLegitMoveInsideAllowedRoot()
    {
        var allowed = Path.Combine(_tempDir, "allowed");
        var outside = Path.Combine(_tempDir, "outside");
        Directory.CreateDirectory(allowed);
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "untouched.bin"), [42]);

        var src = Path.Combine(allowed, "a.bin");
        var dest = Path.Combine(allowed, "sub", "a.bin");
        File.WriteAllBytes(src, [1, 2, 3]);

        var validator = new RootBoundaryValidator(outside).Snapshot();

        var fs = new FileSystemAdapter();
        fs.MoveItemSafely(src, dest, allowedRoot: allowed);

        var violations = validator.Verify();
        Assert.Empty(violations);
        Assert.True(File.Exists(dest));
        Assert.False(File.Exists(src));
    }
}
