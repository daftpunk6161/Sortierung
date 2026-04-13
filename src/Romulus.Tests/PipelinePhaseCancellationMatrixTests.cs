using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Romulus.Tests.TestFixtures;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// F4: Cancellation-Phase-Matrix — verifies that each pipeline phase properly respects
/// CancellationToken and throws OperationCanceledException when pre-cancelled.
/// </summary>
public sealed class PipelinePhaseCancellationMatrixTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CancellationTokenSource _cts;
    private readonly PipelineContext _ctx;

    public PipelinePhaseCancellationMatrixTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CancelMatrix_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _cts = new CancellationTokenSource();
        _cts.Cancel(); // Pre-cancelled

        _ctx = new PipelineContext
        {
            Options = new RunOptions
            {
                Roots = [_tempDir],
                Mode = RunConstants.ModeDryRun,
                Extensions = [".zip"],
            },
            FileSystem = new InMemoryFileSystem(),
            AuditStore = new NullAuditStore(),
            Metrics = new PhaseMetricsCollector(),
        };
    }

    public void Dispose()
    {
        _cts.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ScanPhase_PreCancelled_Throws()
    {
        var phase = new ScanPipelinePhase();
        Assert.Throws<OperationCanceledException>(
            () => phase.Execute(_ctx.Options, _ctx, _cts.Token));
    }

    [Fact]
    public void DeduplicatePhase_PreCancelled_Throws()
    {
        var phase = new DeduplicatePipelinePhase();
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = Path.Combine(_tempDir, "a.zip"), GameKey = "TestKey", Region = "EU" }
        };

        Assert.Throws<OperationCanceledException>(
            () => phase.Execute(candidates, _ctx, _cts.Token));
    }

    [Fact]
    public void JunkRemovalPhase_PreCancelled_WithItems_Throws()
    {
        var phase = new JunkRemovalPipelinePhase();
        // JunkRemoval checks cancellation per-item in the junk loop.
        // Need a standalone junk winner (no losers, winner is Junk category) to enter the loop.
        var junkPath = Path.Combine(_tempDir, "junk.zip");
        File.WriteAllBytes(junkPath, [0x50, 0x4B]); // dummy file so path exists
        var groups = new List<DedupeGroup>
        {
            new()
            {
                GameKey = "JunkKey",
                Winner = new RomCandidate { MainPath = junkPath, GameKey = "JunkKey", Region = "EU", Category = FileCategory.Junk },
                Losers = [], // standalone junk: no losers
            }
        };
        var input = new JunkRemovalPhaseInput(groups, _ctx.Options);

        Assert.Throws<OperationCanceledException>(
            () => phase.Execute(input, _ctx, _cts.Token));
    }

    [Fact]
    public void MovePhase_PreCancelled_WithLosers_Throws()
    {
        var phase = new MovePipelinePhase();
        var groups = new List<DedupeGroup>
        {
            new()
            {
                GameKey = "TestKey",
                Winner = new RomCandidate { MainPath = Path.Combine(_tempDir, "w.zip"), GameKey = "TestKey", Region = "EU" },
                Losers = [new RomCandidate { MainPath = Path.Combine(_tempDir, "l.zip"), GameKey = "TestKey", Region = "JP" }],
            }
        };
        var input = new MovePhaseInput(groups, _ctx.Options);

        Assert.Throws<OperationCanceledException>(
            () => phase.Execute(input, _ctx, _cts.Token));
    }

    [Fact]
    public void DatAuditPhase_PreCancelled_Throws()
    {
        var phase = new DatAuditPipelinePhase();
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = Path.Combine(_tempDir, "a.zip"), GameKey = "TestKey", Region = "EU" }
        };
        var datIndex = new DatIndex();
        var input = new DatAuditInput(candidates, datIndex, _ctx.Options);

        Assert.Throws<OperationCanceledException>(
            () => phase.Execute(input, _ctx, _cts.Token));
    }

    [Fact]
    public void DatRenamePhase_PreCancelled_Throws()
    {
        var phase = new DatRenamePipelinePhase();
        var entries = new List<DatAuditEntry>
        {
            new(Path.Combine(_tempDir, "a.zip"), "abc123", DatAuditStatus.Have, "Game A", "a.zip", "NES", 100)
        };
        var input = new DatRenameInput(entries, _ctx.Options);

        Assert.Throws<OperationCanceledException>(
            () => phase.Execute(input, _ctx, _cts.Token));
    }

    private sealed class NullAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => true;
        public void Flush(string auditCsvPath) { }
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();
        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "") { }
    }
}
