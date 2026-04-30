using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// T-W7-HEALTH-SCORE — Per-Console HealthScore Breakdown.
/// Pin: <c>CollectionRunSnapshotWriter.CreateSnapshot</c> projiziert deterministisch
/// <see cref="ConsoleHealthBreakdown"/> aus der bestehenden Run-Wahrheit
/// (RomCandidate + DedupeGroup), persistiert ueber den vorhandenen
/// LiteDb-Pfad und vermeidet eine zweite Wahrheit fuer den HealthScore.
/// </summary>
public sealed class Wave7HealthScorePerConsoleTests : IDisposable
{
    private readonly string _tempDir;

    public Wave7HealthScorePerConsoleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_W7Health_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    private static RunOptions Options(string root) => new()
    {
        Roots = [root],
        Mode = RunConstants.ModeDryRun,
        AuditPath = Path.Combine(root, "audit", "audit-w7.csv")
    };

    private static RomCandidate Candidate(
        string path,
        string consoleKey,
        FileCategory category = FileCategory.Game,
        bool datMatch = false,
        long sizeBytes = 1024)
        => new()
        {
            MainPath = path,
            ConsoleKey = consoleKey,
            Category = category,
            DatMatch = datMatch,
            SizeBytes = sizeBytes
        };

    [Fact]
    public void CollectionRunSnapshot_PerConsoleHealth_DefaultIsEmpty()
    {
        var snapshot = new CollectionRunSnapshot();
        Assert.NotNull(snapshot.PerConsoleHealth);
        Assert.Empty(snapshot.PerConsoleHealth);
    }

    [Fact]
    public void CreateSnapshot_PerConsoleHealth_EmptyWhenNoCandidates()
    {
        var options = Options(_tempDir);
        var result = new RunResult { Status = RunConstants.StatusOk };
        var projection = RunProjectionFactory.Create(result);

        var snapshot = CollectionRunSnapshotWriter.CreateSnapshot(
            options, result, projection,
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 1, 0, 0, 1, DateTimeKind.Utc));

        Assert.Empty(snapshot.PerConsoleHealth);
    }

    [Fact]
    public void CreateSnapshot_PerConsoleHealth_SingleConsole_HealthScoreMatchesGlobal()
    {
        var options = Options(_tempDir);
        var result = new RunResult
        {
            Status = RunConstants.StatusOk,
            // Aligned totals: single-console run with 0 dupes / 0 errors so the
            // global RunProjection HealthScore (which feeds errors=failCount)
            // and the per-console projection (errors=0) must agree.
            TotalFilesScanned = 3,
            AllCandidates =
            [
                Candidate(Path.Combine(_tempDir, "g1.zip"), "snes", datMatch: true),
                Candidate(Path.Combine(_tempDir, "g2.zip"), "snes", datMatch: true),
                Candidate(Path.Combine(_tempDir, "j1.zip"), "snes", category: FileCategory.Junk)
            ]
        };
        var projection = RunProjectionFactory.Create(result);

        var snapshot = CollectionRunSnapshotWriter.CreateSnapshot(
            options, result, projection,
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 1, 0, 0, 1, DateTimeKind.Utc));

        var entry = Assert.Single(snapshot.PerConsoleHealth);
        Assert.Equal("snes", entry.ConsoleKey);
        Assert.Equal(3, entry.TotalFiles);
        Assert.Equal(2, entry.Games);
        Assert.Equal(1, entry.Junk);
        Assert.Equal(2, entry.DatMatches);
        // Single console: per-console HealthScore must equal the global HealthScore (one fachliche Wahrheit).
        Assert.Equal(snapshot.HealthScore, entry.HealthScore);
    }

    [Fact]
    public void CreateSnapshot_PerConsoleHealth_MultipleConsoles_DeterministicOrderOrdinal()
    {
        var options = Options(_tempDir);
        var result = new RunResult
        {
            Status = RunConstants.StatusOk,
            AllCandidates =
            [
                Candidate(Path.Combine(_tempDir, "z.zip"), "snes"),
                Candidate(Path.Combine(_tempDir, "a.zip"), "nes"),
                Candidate(Path.Combine(_tempDir, "m.zip"), "gb"),
                Candidate(Path.Combine(_tempDir, "x.zip"), "nes")
            ]
        };
        var projection = RunProjectionFactory.Create(result);

        var snapshot = CollectionRunSnapshotWriter.CreateSnapshot(
            options, result, projection,
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 1, 0, 0, 1, DateTimeKind.Utc));

        var keys = snapshot.PerConsoleHealth.Select(e => e.ConsoleKey).ToArray();
        Assert.Equal(new[] { "gb", "nes", "snes" }, keys);
    }

    [Fact]
    public void CreateSnapshot_PerConsoleHealth_CountsDupesPerConsoleFromDedupeGroups()
    {
        var options = Options(_tempDir);
        var snesWinner = Candidate(Path.Combine(_tempDir, "snesW.zip"), "snes");
        var snesLoser = Candidate(Path.Combine(_tempDir, "snesL.zip"), "snes");
        var nesWinner = Candidate(Path.Combine(_tempDir, "nesW.zip"), "nes");
        var nesLoser1 = Candidate(Path.Combine(_tempDir, "nesL1.zip"), "nes");
        var nesLoser2 = Candidate(Path.Combine(_tempDir, "nesL2.zip"), "nes");

        var result = new RunResult
        {
            Status = RunConstants.StatusOk,
            AllCandidates = [snesWinner, snesLoser, nesWinner, nesLoser1, nesLoser2],
            DedupeGroups =
            [
                new DedupeGroup { Winner = snesWinner, Losers = [snesLoser], GameKey = "snes:gameA" },
                new DedupeGroup { Winner = nesWinner, Losers = [nesLoser1, nesLoser2], GameKey = "nes:gameB" }
            ]
        };
        var projection = RunProjectionFactory.Create(result);

        var snapshot = CollectionRunSnapshotWriter.CreateSnapshot(
            options, result, projection,
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 1, 0, 0, 1, DateTimeKind.Utc));

        var nes = Assert.Single(snapshot.PerConsoleHealth, e => e.ConsoleKey == "nes");
        var snes = Assert.Single(snapshot.PerConsoleHealth, e => e.ConsoleKey == "snes");

        Assert.Equal(3, nes.TotalFiles);
        Assert.Equal(2, nes.Dupes);
        Assert.Equal(2, snes.TotalFiles);
        Assert.Equal(1, snes.Dupes);
    }

    [Fact]
    public void CreateSnapshot_PerConsoleHealth_HealthScoreUsesCanonicalCalculator()
    {
        var options = Options(_tempDir);
        var result = new RunResult
        {
            Status = RunConstants.StatusOk,
            AllCandidates =
            [
                // 4 files, 0 dupes, 1 junk, 2 dat-verified
                Candidate(Path.Combine(_tempDir, "g1.zip"), "snes", datMatch: true),
                Candidate(Path.Combine(_tempDir, "g2.zip"), "snes", datMatch: true),
                Candidate(Path.Combine(_tempDir, "g3.zip"), "snes"),
                Candidate(Path.Combine(_tempDir, "j1.zip"), "snes", category: FileCategory.Junk)
            ]
        };
        var projection = RunProjectionFactory.Create(result);

        var snapshot = CollectionRunSnapshotWriter.CreateSnapshot(
            options, result, projection,
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 1, 0, 0, 1, DateTimeKind.Utc));

        var entry = Assert.Single(snapshot.PerConsoleHealth);
        var expected = CollectionAnalysisService.CalculateHealthScore(
            totalFiles: 4, dupes: 0, junk: 1, verified: 2);
        Assert.Equal(expected, entry.HealthScore);
    }

    [Fact]
    public async Task TryPersistAsync_PerConsoleHealth_RoundtripsViaLiteDb()
    {
        var indexPath = Path.Combine(_tempDir, "collection.db");
        using var index = new LiteDbCollectionIndex(indexPath);

        var options = Options(_tempDir);
        var result = new RunResult
        {
            Status = RunConstants.StatusOk,
            AllCandidates =
            [
                Candidate(Path.Combine(_tempDir, "g.zip"), "nes", datMatch: true),
                Candidate(Path.Combine(_tempDir, "j.zip"), "snes", category: FileCategory.Junk)
            ]
        };

        await CollectionRunSnapshotWriter.TryPersistAsync(
            index, options, result,
            new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 1, 0, 0, 1, DateTimeKind.Utc));

        var snapshots = await index.ListRunSnapshotsAsync();
        var snap = Assert.Single(snapshots);
        Assert.Equal(2, snap.PerConsoleHealth.Count);
        var nes = Assert.Single(snap.PerConsoleHealth, e => e.ConsoleKey == "nes");
        Assert.Equal(1, nes.TotalFiles);
        Assert.Equal(1, nes.DatMatches);
    }
}
