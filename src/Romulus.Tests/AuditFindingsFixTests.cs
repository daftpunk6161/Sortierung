using System.Text;
using Romulus.Api;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Deduplication;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Orchestration;
using Xunit;
using CliProgram = Romulus.CLI.Program;

namespace Romulus.Tests;

/// <summary>
/// Tests for audit findings F-01, F-07, F-08, F-09, F-10, F-11.
/// </summary>
public sealed class AuditFindingsFixTests : IDisposable
{
    private readonly string _tempDir;

    public AuditFindingsFixTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "audit_findings_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    // =====================================================================
    //  F-01 / F-07: NonGame in GetCategoryRank / Winner-Selection
    // =====================================================================

    private static RomCandidate MakeCandidate(
        string mainPath = "game.zip",
        string gameKey = "game",
        int regionScore = 0,
        FileCategory category = FileCategory.Game)
        => new()
        {
            MainPath = mainPath,
            GameKey = gameKey,
            RegionScore = regionScore,
            Category = category
        };

    [Fact]
    public void SelectWinner_Game_BeatsNonGame()
    {
        var game = MakeCandidate(mainPath: "a.zip", category: FileCategory.Game);
        var nonGame = MakeCandidate(mainPath: "b.zip", category: FileCategory.NonGame, regionScore: 9999);
        var winner = DeduplicationEngine.SelectWinner(new[] { nonGame, game });
        Assert.Same(game, winner);
    }

    [Fact]
    public void SelectWinner_NonGame_BeatsJunk()
    {
        var nonGame = MakeCandidate(mainPath: "a.zip", category: FileCategory.NonGame);
        var junk = MakeCandidate(mainPath: "b.zip", category: FileCategory.Junk, regionScore: 9999);
        var winner = DeduplicationEngine.SelectWinner(new[] { junk, nonGame });
        Assert.Same(nonGame, winner);
    }

    [Fact]
    public void SelectWinner_NonGame_BeatsUnknown()
    {
        var nonGame = MakeCandidate(mainPath: "a.zip", category: FileCategory.NonGame);
        var unknown = MakeCandidate(mainPath: "b.zip", category: FileCategory.Unknown, regionScore: 9999);
        var winner = DeduplicationEngine.SelectWinner(new[] { unknown, nonGame });
        Assert.Same(nonGame, winner);
    }

    [Fact]
    public void SelectWinner_Bios_BeatsNonGame()
    {
        var bios = MakeCandidate(mainPath: "a.zip", category: FileCategory.Bios);
        var nonGame = MakeCandidate(mainPath: "b.zip", category: FileCategory.NonGame, regionScore: 9999);
        var winner = DeduplicationEngine.SelectWinner(new[] { nonGame, bios });
        Assert.Same(bios, winner);
    }

    [Fact]
    public void Deduplicate_WithNonGameCategory_GroupsCorrectly()
    {
        var candidates = new[]
        {
            MakeCandidate(mainPath: "game_eu.zip", gameKey: "title", category: FileCategory.Game, regionScore: 1000),
            MakeCandidate(mainPath: "nongame_us.zip", gameKey: "title", category: FileCategory.NonGame, regionScore: 999),
            MakeCandidate(mainPath: "junk_jp.zip", gameKey: "title", category: FileCategory.Junk, regionScore: 998),
        };

        var results = DeduplicationEngine.Deduplicate(candidates);
        Assert.Single(results);
        Assert.Equal("game_eu.zip", results[0].Winner.MainPath);
        Assert.Equal(2, results[0].Losers.Count);
    }

    // =====================================================================
    //  F-08: CLI Runtime-Exit-Code-Tests
    // =====================================================================

    private static (int ExitCode, string Stdout, string Stderr) RunCli(CliRunOptions options)
    {
        lock (SharedTestLocks.ConsoleLock)
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            try
            {
                CliProgram.SetConsoleOverrides(stdout, stderr);
                var exitCode = CliProgram.RunForTests(options);
                return (exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                CliProgram.SetConsoleOverrides(null, null);
            }
        }
    }

    [Fact]
    public void Cli_DryRun_ReturnsExitCode0()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "game (EU).zip"), "test");
        File.WriteAllText(Path.Combine(root, "game (US).zip"), "test");

        var options = new CliRunOptions
        {
            Roots = new[] { root },
            Mode = "DryRun",
            PreferRegions = new[] { "EU", "US" },
            Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip" },
            ExtensionsExplicit = true
        };

        var (exitCode, stdout, _) = RunCli(options);
        Assert.Equal(0, exitCode);
        Assert.Contains("\"Status\"", stdout);
    }

    [Fact]
    public void Cli_NonExistentRoot_ReturnsExitCode3_PreflightFailed()
    {
        var nonExistent = Path.Combine(_tempDir, "does_not_exist_" + Guid.NewGuid().ToString("N"));
        var options = new CliRunOptions
        {
            Roots = new[] { nonExistent },
            Mode = "DryRun",
            Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip" },
            ExtensionsExplicit = true
        };

        var (exitCode, _, stderr) = RunCli(options);
        Assert.Equal(3, exitCode); // Preflight failed: root not found
    }

    // =====================================================================
    //  F-09: Rollback-Reihenfolge-Test (reverse order)
    // =====================================================================

    [Fact]
    public void Rollback_ProcessesRowsInReverseOrder()
    {
        // Arrange: Create 3 files that were "moved" — rows order: A→A', B→B', C→C'
        // Rollback should process C first, then B, then A
        var srcDir = Path.Combine(_tempDir, "src");
        var trashDir = Path.Combine(_tempDir, "trash");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(trashDir);

        var fileA_orig = Path.Combine(srcDir, "fileA.zip");
        var fileA_trash = Path.Combine(trashDir, "fileA.zip");
        var fileB_orig = Path.Combine(srcDir, "fileB.zip");
        var fileB_trash = Path.Combine(trashDir, "fileB.zip");
        var fileC_orig = Path.Combine(srcDir, "fileC.zip");
        var fileC_trash = Path.Combine(trashDir, "fileC.zip");

        // Files are currently at trash locations
        File.WriteAllText(fileA_trash, "A");
        File.WriteAllText(fileB_trash, "B");
        File.WriteAllText(fileC_trash, "C");

        // Build audit CSV (header + rows in forward order)
        var csvPath = Path.Combine(_tempDir, "audit.csv");
        var sb = new StringBuilder();
        sb.AppendLine("RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp");
        sb.AppendLine($"{srcDir},{fileA_orig},{fileA_trash},MOVE,Game,,dedup,2026-01-01T00:00:00Z");
        sb.AppendLine($"{srcDir},{fileB_orig},{fileB_trash},MOVE,Game,,dedup,2026-01-01T00:00:01Z");
        sb.AppendLine($"{srcDir},{fileC_orig},{fileC_trash},MOVE,Game,,dedup,2026-01-01T00:00:02Z");
        File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);

        var fs = new FileSystemAdapter();
        var service = new AuditSigningService(fs);
        // SEC-ROLLBACK-03: Execute-mode rollback requires sidecar
        service.WriteMetadataSidecar(csvPath, 3);

        // Act: Execute rollback (non-dry-run, sidecar verifies audit integrity)
        var result = service.Rollback(csvPath,
            allowedRestoreRoots: new[] { srcDir },
            allowedCurrentRoots: new[] { trashDir },
            dryRun: false);

        // Assert: All 3 files restored
        Assert.Equal(3, result.RolledBack);
        Assert.True(File.Exists(fileA_orig), "fileA should be restored");
        Assert.True(File.Exists(fileB_orig), "fileB should be restored");
        Assert.True(File.Exists(fileC_orig), "fileC should be restored");

        // Verify rollback audit trail exists
        var rollbackAuditPath = Path.ChangeExtension(csvPath, ".rollback-audit.csv");
        Assert.True(File.Exists(rollbackAuditPath), "Rollback audit CSV should be created");

        // Verify reverse order in rollback audit (C should appear before B, B before A)
        var rollbackLines = File.ReadAllLines(rollbackAuditPath);
        var dataLines = rollbackLines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.Equal(3, dataLines.Length);
        Assert.Contains("fileC", dataLines[0]);
        Assert.Contains("fileB", dataLines[1]);
        Assert.Contains("fileA", dataLines[2]);
    }

    // =====================================================================
    //  F-10: Rate-Limiter Concurrency-Test
    // =====================================================================

    [Fact]
    public async Task RateLimiter_ConcurrentAccess_RespectsLimit()
    {
        var limiter = new RateLimiter(maxRequestsPerWindow: 50, window: TimeSpan.FromSeconds(60));
        var successCount = 0;
        var failCount = 0;

        // Fire 100 concurrent requests from the same client
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            if (limiter.TryAcquire("test-client"))
                Interlocked.Increment(ref successCount);
            else
                Interlocked.Increment(ref failCount);
        })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(50, successCount);
        Assert.Equal(50, failCount);
    }

    [Fact]
    public async Task RateLimiter_MultipleClients_IndependentBuckets()
    {
        var limiter = new RateLimiter(maxRequestsPerWindow: 10, window: TimeSpan.FromSeconds(60));

        // Fire 10 requests from each of 10 clients concurrently
        var successes = new int[10];

        var tasks = new List<Task>();
        for (int clientIdx = 0; clientIdx < 10; clientIdx++)
        {
            var idx = clientIdx;
            var clientId = $"client-{idx}";
            for (int req = 0; req < 15; req++)
            {
                tasks.Add(Task.Run(() =>
                {
                    if (limiter.TryAcquire(clientId))
                        Interlocked.Increment(ref successes[idx]);
                }));
            }
        }

        await Task.WhenAll(tasks);

        // Each client should have exactly 10 successes (limit), 5 rejections
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(10, successes[i]);
        }
    }

    [Fact]
    public void RateLimiter_WindowReset_AllowsNewRequests()
    {
        // Use a very short window to test reset
        var limiter = new RateLimiter(maxRequestsPerWindow: 5, window: TimeSpan.FromMilliseconds(50));

        // Exhaust limit
        for (int i = 0; i < 5; i++)
            Assert.True(limiter.TryAcquire("client"));
        Assert.False(limiter.TryAcquire("client"));

        // Wait for window to expire
        Thread.Sleep(100);

        // Should be allowed again
        Assert.True(limiter.TryAcquire("client"));
    }

    [Fact]
    public void SidecarSigning_AfterConsoleSort_ContainsConsoleSortRows()
    {
        var root = Path.Combine(_tempDir, "sort_sidecar");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Title (USA).sfc"), "usa");
        File.WriteAllText(Path.Combine(root, "Title (Europe).sfc"), "eu");

        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var auditPath = Path.Combine(_tempDir, "audit", "sort-sidecar.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);

        var options = new RunOptions
        {
            Roots = new[] { root },
            Mode = "Move",
            PreferRegions = new[] { "US", "EU" },
            Extensions = new[] { ".sfc" },
            RemoveJunk = false,
            SortConsole = true,
            AuditPath = auditPath
        };

        var env = RunEnvironmentBuilder.Build(options, settings, dataDir);
        var orchestrator = new RunOrchestrator(env.FileSystem, env.Audit, env.ConsoleDetector, env.HashService, env.Converter, env.DatIndex);

        var result = orchestrator.Execute(options);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(auditPath));
        Assert.True(File.Exists(auditPath + ".meta.json"));
        Assert.True(env.Audit.TestMetadataSidecar(auditPath));

        var lines = File.ReadAllLines(auditPath);
        Assert.Contains(lines, l => l.Contains(",CONSOLE_SORT,", StringComparison.OrdinalIgnoreCase));

        var sidecarJson = File.ReadAllText(auditPath + ".meta.json");
        Assert.Contains("\"RowCount\"", sidecarJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"ConsoleSortMoved\"", sidecarJson, StringComparison.OrdinalIgnoreCase);
    }
}
