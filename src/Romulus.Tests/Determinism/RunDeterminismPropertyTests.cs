using Romulus.Contracts.Models;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests.Determinism;

/// <summary>
/// Zentraler Determinismus-Property-Test.
///
/// DeduplicationEngine has SelectWinner_IsStableAcrossPermutationsAndParallelCalls,
/// but that only covers the dedup core - not the full pipeline.
///
/// This suite verifies orchestrator-level determinism: the same input dataset
/// processed N times (here: 25 iterations of RunOrchestrator.Execute) MUST yield
/// byte-identical RunResult identity (group count, winner identity, loser identity,
/// per-group decision class, sort decision, console key).
///
/// FileSystemAdapter.GetFilesSafe enumeration order is platform-dependent, so we
/// cannot prove "permutation independence" without injecting a custom enumerator.
/// We instead pin the inputs, run the pipeline N times, and assert outputs are
/// stable across runs - which catches non-determinism inside the pipeline itself
/// (parallel race conditions, dictionary iteration leaks, time-dependent winners).
/// </summary>
public sealed class RunDeterminismPropertyTests : IDisposable
{
    private const int Iterations = 25;
    private readonly string _tempDir;

    public RunDeterminismPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_C3_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void RunOrchestratorExecute_SameInputAcrossIterations_ProducesIdenticalProjection()
    {
        var root = Path.Combine(_tempDir, "scan");
        Directory.CreateDirectory(root);
        // Mixed dataset: dup-group winners, junk, region-conflict, BIOS tag.
        var seedFiles = new (string name, string body)[]
        {
            ("Mario (USA).zip",          "us-mario"),
            ("Mario (Europe).zip",       "eu-mario"),
            ("Mario (Japan).zip",        "jp-mario"),
            ("Zelda (Europe).zip",       "eu-zelda"),
            ("Zelda (USA) (Beta).zip",   "junk-zelda"),
            ("[BIOS] System (1.0).zip",  "bios"),
            ("Castlevania (World).zip",  "world-cv"),
            ("Sonic (USA).zip",          "us-sonic"),
            ("Sonic (Europe).zip",       "eu-sonic"),
            ("Random Stub (USA).zip",    "stub")
        };
        foreach (var (name, body) in seedFiles)
            File.WriteAllText(Path.Combine(root, name), body);

        string? canonical = null;
        for (var i = 0; i < Iterations; i++)
        {
            var orch = new RunOrchestrator(new FileSystemAdapter(), new AuditCsvStore());
            var result = orch.Execute(new RunOptions
            {
                Roots = [root],
                Extensions = [".zip"],
                Mode = "DryRun",
                PreferRegions = ["EU", "US", "JP", "WORLD"],
                HashType = "SHA1"
            });

            Assert.Equal("ok", result.Status);
            var projection = ProjectIdentity(result);

            if (canonical is null)
                canonical = projection;
            else
                Assert.Equal(canonical, projection);
        }
    }

    private static string ProjectIdentity(RunResult result)
    {
        var rows = result.DedupeGroups
            .Select(g => string.Join("|",
                g.GameKey.ToLowerInvariant(),
                Path.GetFileName(g.Winner.MainPath).ToLowerInvariant(),
                g.Winner.ConsoleKey,
                g.Winner.DecisionClass,
                g.Winner.SortDecision,
                g.Winner.PlatformFamily,
                g.Winner.Category,
                string.Join(",", g.Losers
                    .Select(l => Path.GetFileName(l.MainPath).ToLowerInvariant())
                    .OrderBy(s => s, StringComparer.Ordinal))))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        return string.Join("\n",
            $"Total={result.TotalFilesScanned}",
            $"Groups={result.GroupCount}",
            $"Winners={result.WinnerCount}",
            $"Losers={result.LoserCount}",
            string.Join("\n", rows));
    }
}
