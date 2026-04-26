using Romulus.Contracts.Models;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests.Audit;

/// <summary>
/// Cancellation-Datenintegritaet.
///
/// Conversion-cancellation cleanup is covered by SourcePreservationInvariantTests.
/// This suite covers the orchestrator-level Move-mode cancellation contract:
///
///  1.  An immediately cancelled token must yield Status="cancelled" with ExitCode=2
///        and MUST NOT move any source file.
///  2.  Cancellation triggered during the scan progress callback (mid-run) must
///        leave every input file at its original location (no half-move).
///
/// Combined with conversion-cancellation coverage, this closes the C4 gap:
/// "no half-moved files, partial conversion outputs cleaned up after cancel."
/// </summary>
public sealed class CancellationDataIntegrityTests : IDisposable
{
    private readonly string _tempDir;

    public CancellationDataIntegrityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_C4_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void RunOrchestrator_MoveModeWithAlreadyCancelledToken_LeavesAllInputsInPlace()
    {
        var root = Path.Combine(_tempDir, "scan");
        Directory.CreateDirectory(root);
        var paths = new[]
        {
            Path.Combine(root, "Game (USA).zip"),
            Path.Combine(root, "Game (Europe).zip"),
            Path.Combine(root, "Other (Japan).zip")
        };
        foreach (var p in paths)
            File.WriteAllText(p, Path.GetFileName(p));

        var beforeBytes = paths.ToDictionary(p => p, File.ReadAllBytes);

        var orch = new RunOrchestrator(new FileSystemAdapter(), new AuditCsvStore());
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = orch.Execute(new RunOptions
        {
            Roots = [root],
            Extensions = [".zip"],
            Mode = "Move",
            PreferRegions = ["US"]
        }, cts.Token);

        Assert.Equal("cancelled", result.Status);
        Assert.Equal(2, result.ExitCode);

        foreach (var p in paths)
        {
            Assert.True(File.Exists(p), $"Source missing after cancel: {p}");
            Assert.Equal(beforeBytes[p], File.ReadAllBytes(p));
        }
    }

    [Fact]
    public void RunOrchestrator_MoveModeWithCancellationDuringScan_LeavesAllInputsInPlace()
    {
        var root = Path.Combine(_tempDir, "scan");
        Directory.CreateDirectory(root);
        var paths = new[]
        {
            Path.Combine(root, "Mario (USA).zip"),
            Path.Combine(root, "Mario (Europe).zip"),
            Path.Combine(root, "Zelda (Europe).zip")
        };
        foreach (var p in paths)
            File.WriteAllText(p, Path.GetFileName(p));

        var beforeBytes = paths.ToDictionary(p => p, File.ReadAllBytes);
        var cts = new CancellationTokenSource();

        var orch = new RunOrchestrator(
            new FileSystemAdapter(),
            new AuditCsvStore(),
            onProgress: msg =>
            {
                if (msg.Contains("[Scan]", StringComparison.OrdinalIgnoreCase))
                    cts.Cancel();
            });

        var result = orch.Execute(new RunOptions
        {
            Roots = [root],
            Extensions = [".zip"],
            Mode = "Move",
            PreferRegions = ["US"]
        }, cts.Token);

        Assert.Equal("cancelled", result.Status);
        Assert.Equal(2, result.ExitCode);

        foreach (var p in paths)
        {
            Assert.True(File.Exists(p), $"Source missing after mid-run cancel: {p}");
            Assert.Equal(beforeBytes[p], File.ReadAllBytes(p));
        }
    }
}
