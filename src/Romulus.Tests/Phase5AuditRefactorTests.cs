using Romulus.Contracts.Models;
using Romulus.Core.Scoring;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Reporting;
using Romulus.UI.Wpf.Models;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// RED-phase tests for TASK-164 (Dashboard Ist-Werte nach Cancel),
/// TASK-165 (Report MOVE-Markierung bei DryRun),
/// TASK-166 (HealthScore berücksichtigt Run-Fehler),
/// TASK-167 (HeaderRepair crash-safe temp-file pattern).
/// </summary>
public class Phase5AuditRefactorTests
{
    // ─── TASK-164: DashboardProjection nach Cancel ────────────────────

    [Fact]
    public void DashboardProjection_FromCancelledRun_ShowsActualValues()
    {
        // A cancelled run still has partial results — dashboard must show actual IS values,
        // not plan values or 0.
        var winner = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game, DatMatch = true };
        var loser = new RomCandidate { MainPath = @"C:\roms\Game (US).chd", Category = FileCategory.Game };

        var result = new RunResult
        {
            Status = "cancelled",
            TotalFilesScanned = 2,
            WinnerCount = 1,
            LoserCount = 1,
            GroupCount = 1,
            DedupeGroups = new[]
            {
                new DedupeGroup { GameKey = "game", Winner = winner, Losers = new[] { loser } }
            },
            AllCandidates = new[] { winner, loser },
            DurationMs = 500
        };

        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false);

        Assert.StartsWith("1", dashboard.Winners, StringComparison.Ordinal);
        Assert.StartsWith("1", dashboard.Dupes, StringComparison.Ordinal);
        Assert.Contains("(vorläufig)", dashboard.Winners, StringComparison.Ordinal);
        Assert.Contains("(vorläufig)", dashboard.Dupes, StringComparison.Ordinal);
        // Duration should reflect actual ms, not be empty or placeholder
        Assert.NotEqual("–", dashboard.Duration);
        Assert.False(string.IsNullOrWhiteSpace(dashboard.Duration));
        // The key invariant: values must NOT be empty or "–" for a run that produced data
        Assert.NotEqual("–", dashboard.Winners);
        Assert.NotEqual("–", dashboard.Dupes);
    }

    // ─── TASK-165: Report Entry Actions spiegeln Modus ────────────────

    [Fact]
    public void BuildEntries_DryRun_MarksDupesAsDupeNotMove()
    {
        var winner = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game, GameKey = "game" };
        var loser = new RomCandidate { MainPath = @"C:\roms\Game (US).chd", Category = FileCategory.Game, GameKey = "game" };

        var result = new RunResult
        {
            TotalFilesScanned = 2,
            WinnerCount = 1, LoserCount = 1, GroupCount = 1,
            DedupeGroups = new[]
            {
                new DedupeGroup { GameKey = "game", Winner = winner, Losers = new[] { loser } }
            },
            AllCandidates = new[] { winner, loser }
        };

        var entries = RunReportWriter.BuildEntries(result, "DryRun");

        var loserEntry = entries.Single(e => e.FilePath == loser.MainPath);
        Assert.Equal("DUPE", loserEntry.Action);
    }

    [Fact]
    public void BuildEntries_MoveMode_MarksDupesAsMove()
    {
        var winner = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game, GameKey = "game" };
        var loser = new RomCandidate { MainPath = @"C:\roms\Game (US).chd", Category = FileCategory.Game, GameKey = "game" };

        var result = new RunResult
        {
            TotalFilesScanned = 2,
            WinnerCount = 1, LoserCount = 1, GroupCount = 1,
            DedupeGroups = new[]
            {
                new DedupeGroup { GameKey = "game", Winner = winner, Losers = new[] { loser } }
            },
            AllCandidates = new[] { winner, loser }
        };

        var entries = RunReportWriter.BuildEntries(result, "Move");

        var loserEntry = entries.Single(e => e.FilePath == loser.MainPath);
        Assert.Equal("MOVE", loserEntry.Action);
    }

    [Fact]
    public void BuildEntries_DryRun_JunkStaysJunk()
    {
        var winner = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game, GameKey = "game" };
        var junkLoser = new RomCandidate { MainPath = @"C:\roms\game (Beta).zip", Category = FileCategory.Junk, GameKey = "game" };

        var result = new RunResult
        {
            TotalFilesScanned = 2,
            WinnerCount = 1, LoserCount = 1, GroupCount = 1,
            DedupeGroups = new[]
            {
                new DedupeGroup { GameKey = "game", Winner = winner, Losers = new[] { junkLoser } }
            },
            AllCandidates = new[] { winner, junkLoser }
        };

        var entries = RunReportWriter.BuildEntries(result, "DryRun");

        var junkEntry = entries.Single(e => e.FilePath == junkLoser.MainPath);
        Assert.Equal("JUNK", junkEntry.Action);
    }

    // ─── TASK-166: HealthScore berücksichtigt Fehler ──────────────────

    [Fact]
    public void Health_GetHealthScore_WithErrors_ReducesScore()
    {
        var scoreNoErrors = HealthScorer.GetHealthScore(100, dupes: 10, junk: 5, verified: 50, errors: 0);
        var scoreWithErrors = HealthScorer.GetHealthScore(100, dupes: 10, junk: 5, verified: 50, errors: 5);

        Assert.True(scoreWithErrors < scoreNoErrors, $"Score with errors ({scoreWithErrors}) should be less than without ({scoreNoErrors})");
    }

    [Fact]
    public void Health_GetHealthScore_ErrorPenaltyCappedAt20()
    {
        // Use inputs where no-error score is well below 100 to observe full penalty
        var scoreNoErrors = HealthScorer.GetHealthScore(100, dupes: 30, junk: 0, verified: 0, errors: 0);
        var scoreMaxErrors = HealthScorer.GetHealthScore(100, dupes: 30, junk: 0, verified: 0, errors: 999);

        Assert.True(scoreNoErrors - scoreMaxErrors <= 20,
            $"Error penalty of {scoreNoErrors - scoreMaxErrors} exceeds cap of 20");
        Assert.True(scoreNoErrors - scoreMaxErrors >= 19,
            $"Error penalty of {scoreNoErrors - scoreMaxErrors} should be close to cap of 20");
    }

    [Fact]
    public void Health_GetHealthScore_ZeroErrors_SameAsNoErrorParam()
    {
        var withZero = HealthScorer.GetHealthScore(100, dupes: 10, junk: 20, verified: 40, errors: 0);
        var withoutParam = HealthScorer.GetHealthScore(100, dupes: 10, junk: 20, verified: 40);

        Assert.Equal(withoutParam, withZero);
    }

    [Fact]
    public void RunProjection_HealthScore_IncludesErrors()
    {
        var winner = new RomCandidate { MainPath = @"C:\roms\Game (EU).chd", Category = FileCategory.Game };
        var loser = new RomCandidate { MainPath = @"C:\roms\Game (US).chd", Category = FileCategory.Game };

        var resultNoErrors = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 2,
            WinnerCount = 1, LoserCount = 1, GroupCount = 1,
            DedupeGroups = new[]
            {
                new DedupeGroup { GameKey = "game", Winner = winner, Losers = new[] { loser } }
            },
            AllCandidates = new[] { winner, loser },
            MoveResult = new MovePhaseResult(MoveCount: 1, FailCount: 0, SavedBytes: 0, SkipCount: 0)
        };

        var resultWithErrors = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 2,
            WinnerCount = 1, LoserCount = 1, GroupCount = 1,
            DedupeGroups = new[]
            {
                new DedupeGroup { GameKey = "game", Winner = winner, Losers = new[] { loser } }
            },
            AllCandidates = new[] { winner, loser },
            MoveResult = new MovePhaseResult(MoveCount: 0, FailCount: 5, SavedBytes: 0, SkipCount: 0)
        };

        var projNoErr = RunProjectionFactory.Create(resultNoErrors);
        var projWithErr = RunProjectionFactory.Create(resultWithErrors);

        Assert.True(projWithErr.HealthScore < projNoErr.HealthScore,
            $"Projection health with errors ({projWithErr.HealthScore}) should be < without ({projNoErr.HealthScore})");
    }

    // ─── TASK-167: HeaderRepair Temp-File Safety ──────────────────────

    [Fact]
    public void RepairNesHeader_UsesTempFileThenRename()
    {
        // The repair should write to a .tmp file, then atomically rename.
        // We verify by checking that the original file is not corrupted if
        // the repair process writes to temp first.
        var dir = Path.Combine(Path.GetTempPath(), $"romulus-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var nesPath = Path.Combine(dir, "test.nes");

            // Valid NES header with dirty bytes 12-15
            var header = new byte[16 + 8192];
            header[0] = 0x4E; header[1] = 0x45; header[2] = 0x53; header[3] = 0x1A; // NES\x1A
            header[4] = 1; // PRG ROM
            header[12] = 0xFF; header[13] = 0xFF; header[14] = 0xFF; header[15] = 0xFF; // dirty
            File.WriteAllBytes(nesPath, header);

            var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
            var sut = new Romulus.Infrastructure.Hashing.HeaderRepairService(fs);

            var result = sut.RepairNesHeader(nesPath);

            Assert.True(result);

            // Verify the file is properly repaired
            var repairedHeader = new byte[16];
            using (var stream = File.OpenRead(nesPath))
                _ = stream.ReadAtLeast(repairedHeader, 16);

            Assert.Equal(0x00, repairedHeader[12]);
            Assert.Equal(0x00, repairedHeader[13]);
            Assert.Equal(0x00, repairedHeader[14]);
            Assert.Equal(0x00, repairedHeader[15]);

            // Verify .tmp file was cleaned up (not left behind)
            Assert.False(File.Exists(nesPath + ".tmp"), "Temp file should be cleaned up after rename");

            // Verify .bak was created
            Assert.True(File.Exists(nesPath + ".bak"), "Backup file should exist");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RemoveCopierHeader_UsesTempFileThenRename()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"romulus-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var snesPath = Path.Combine(dir, "test.sfc");

            // Create a file with 512-byte copier header + 1024 bytes real data = 1536 bytes
            // 1536 % 1024 == 512 → copier header detected
            var data = new byte[1536];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i % 256);
            File.WriteAllBytes(snesPath, data);

            var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
            var sut = new Romulus.Infrastructure.Hashing.HeaderRepairService(fs);

            var result = sut.RemoveCopierHeader(snesPath);

            Assert.True(result);

            // Verify the copier header was removed (file should be 1024 bytes now)
            var repaired = File.ReadAllBytes(snesPath);
            Assert.Equal(1024, repaired.Length);

            // First byte should be what was at offset 512 prior to repair
            Assert.Equal((byte)(512 % 256), repaired[0]);

            // Verify .tmp was cleaned up
            Assert.False(File.Exists(snesPath + ".tmp"), "Temp file should be cleaned up after rename");

            // Verify .bak was created
            Assert.True(File.Exists(snesPath + ".bak"), "Backup file should exist");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
