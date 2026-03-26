using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Classification;
using RomCleanup.Core.Deduplication;
using RomCleanup.Core.GameKeys;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.Orchestration;
using System.Text;
using Xunit;

namespace RomCleanup.Tests;

/// <summary>
/// TDD Red-Phase: Invariant tests that attack the heartbeat of the system.
/// Each test targets a documented bug or missing invariant from FINAL_CONSOLIDATED_AUDIT.md.
/// Expected result: 5 RED (real bugs), 8 GREEN (regression guards).
/// No production fixes — only failing tests.
/// </summary>
public class CoreHeartbeatInvariantTests : IDisposable
{
    private readonly string _tempDir;

    public CoreHeartbeatInvariantTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HB_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* cleanup best-effort */ }
    }

    // ══════════════════════════════════════════════════════════════
    // PRIO 1: WINNER SELECTION / DEDUPE
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// P2-03 / R-01: BIOS candidate with superior scores must NOT beat a GAME candidate.
    /// SelectWinner sorts purely by numeric scores — no category awareness.
    /// A BIOS file with higher RegionScore/FormatScore/DatMatch wins over GAME.
    /// → MUST BE RED until SelectWinner is made category-aware.
    /// </summary>
    [Fact]
    public void SelectWinner_BiosWithHigherScores_MustNotBeatGame()
    {
        var bios = new RomCandidate
        {
            MainPath = "PlayStation (BIOS).bin",
            GameKey = "playstation",
            Category = FileCategory.Bios,
            RegionScore = 1000,
            FormatScore = 850,
            VersionScore = 500,
            CompletenessScore = 100,
            HeaderScore = 100,
            DatMatch = true,
            SizeTieBreakScore = 999_999
        };

        var game = new RomCandidate
        {
            MainPath = "PlayStation (Europe).bin",
            GameKey = "playstation",
            Category = FileCategory.Game,
            RegionScore = 500,
            FormatScore = 700,
            VersionScore = 0,
            CompletenessScore = 25,
            HeaderScore = 0,
            DatMatch = false,
            SizeTieBreakScore = 500_000
        };

        var winner = DeduplicationEngine.SelectWinner(new[] { bios, game });

        // INVARIANT: GAME must never lose to BIOS regardless of scores.
        Assert.Equal(FileCategory.Game, winner!.Category);
    }

    /// <summary>
    /// P2-03: In a dedupe group where BIOS and GAME share the same GameKey,
    /// the GAME candidate must always be the winner — BIOS must be the loser.
    /// → MUST BE RED until Deduplicate filters by category.
    /// </summary>
    [Fact]
    public void Deduplicate_MixedBiosAndGame_GameMustAlwaysBeWinner()
    {
        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = "bios.bin",
                GameKey = "playstation",
                Category = FileCategory.Bios,
                RegionScore = 1000,
                FormatScore = 850,
                VersionScore = 500,
                CompletenessScore = 100,
                HeaderScore = 100,
                DatMatch = true
            },
            new RomCandidate
            {
                MainPath = "game.bin",
                GameKey = "playstation",
                Category = FileCategory.Game,
                RegionScore = 500,
                FormatScore = 700,
                VersionScore = 0,
                CompletenessScore = 25,
                HeaderScore = 0,
                DatMatch = false
            }
        };

        var results = DeduplicationEngine.Deduplicate(candidates);

        Assert.Single(results);
        // INVARIANT: GAME must be winner, BIOS must be loser — never the other way around.
        Assert.Equal(FileCategory.Game, results[0].Winner.Category);
        Assert.All(results[0].Losers, loser => Assert.NotEqual(FileCategory.Game, loser.Category));
    }

    /// <summary>
    /// Summen-Invariante: Every input candidate with a non-empty GameKey must appear
    /// exactly once — either as winner or as loser — in the dedupe results.
    /// Regression guard for silent data loss.
    /// </summary>
    [Fact]
    public void Deduplicate_SumInvariant_AllNonEmptyKeyCandidatesAccountedFor()
    {
        var candidates = new[]
        {
            new RomCandidate { MainPath = "a.zip", GameKey = "mario", RegionScore = 1000 },
            new RomCandidate { MainPath = "b.zip", GameKey = "mario", RegionScore = 500 },
            new RomCandidate { MainPath = "c.zip", GameKey = "zelda", RegionScore = 800 },
            new RomCandidate { MainPath = "d.zip", GameKey = "", RegionScore = 100 },       // dropped
            new RomCandidate { MainPath = "e.zip", GameKey = "   ", RegionScore = 100 },     // dropped
            new RomCandidate { MainPath = "f.zip", GameKey = "metroid", RegionScore = 900 },
        };

        var results = DeduplicationEngine.Deduplicate(candidates);

        int validInputCount = candidates.Count(c => !string.IsNullOrWhiteSpace(c.GameKey));
        int totalAccountedFor = results.Sum(r => 1 + r.Losers.Count);

        // INVARIANT: All valid candidates appear exactly once
        Assert.Equal(validInputCount, totalAccountedFor);
    }

    /// <summary>
    /// BUG-011 Regression: SelectWinner must be deterministic across ALL 120 permutations
    /// of 5 candidates. Same inputs → same winner, regardless of input order.
    /// </summary>
    [Fact]
    public void SelectWinner_AllPermutationsOf5Items_AlwaysSameWinner()
    {
        var items = new[]
        {
            new RomCandidate { MainPath = "game_eu.chd", GameKey = "game", RegionScore = 1000, FormatScore = 850 },
            new RomCandidate { MainPath = "game_us.iso", GameKey = "game", RegionScore = 999, FormatScore = 700 },
            new RomCandidate { MainPath = "game_jp.zip", GameKey = "game", RegionScore = 998, FormatScore = 500 },
            new RomCandidate { MainPath = "game_kr.7z", GameKey = "game", RegionScore = 200, FormatScore = 480 },
            new RomCandidate { MainPath = "game_asia.rar", GameKey = "game", RegionScore = 200, FormatScore = 400 },
        };

        var referenceWinner = DeduplicationEngine.SelectWinner(items);
        Assert.NotNull(referenceWinner);

        foreach (var permutation in GetPermutations(items))
        {
            var winner = DeduplicationEngine.SelectWinner(permutation.ToArray());
            Assert.Equal(referenceWinner!.MainPath, winner!.MainPath);
        }
    }

    /// <summary>
    /// Regression guard: Deduplicate must never group candidates with empty/whitespace GameKeys.
    /// </summary>
    [Fact]
    public void Deduplicate_EmptyOrWhitespaceKey_NeverGrouped()
    {
        var candidates = new[]
        {
            new RomCandidate { MainPath = "a.zip", GameKey = "" },
            new RomCandidate { MainPath = "b.zip", GameKey = "   " },
            new RomCandidate { MainPath = "c.zip", GameKey = "\t" },
            new RomCandidate { MainPath = "d.zip", GameKey = "real_game", RegionScore = 500 },
        };

        var results = DeduplicationEngine.Deduplicate(candidates);

        Assert.Single(results);
        Assert.Equal("real_game", results[0].GameKey);
        Assert.DoesNotContain(results, r => string.IsNullOrWhiteSpace(r.GameKey));
    }

    // ══════════════════════════════════════════════════════════════
    // PRIO 2: CLASSIFICATION / GAMEKEY
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// P1-13: FileCategory.Unknown must NOT be silently mapped to "GAME".
    /// The categoryStr switch in ScanFiles uses `_ => "GAME"` which catches Unknown.
    /// → MUST BE RED because empty-basename files get classified as GAME.
    /// </summary>
    [Fact]
    public void Execute_UnknownFileCategory_MustNotBecomeGame()
    {
        // Precondition: FileClassifier returns Unknown for empty basenames
        Assert.Equal(FileCategory.Unknown, FileClassifier.Classify(""));

        // A file named ".zip" has empty basename → FileClassifier returns Unknown
        File.WriteAllBytes(Path.Combine(_tempDir, ".zip"), new byte[10]);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new InertAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
        };

        var result = orch.Execute(options);

        // INVARIANT: Files with FileCategory.Unknown must NOT appear as "GAME"
        foreach (var candidate in result.AllCandidates)
        {
            Assert.True(candidate.Category != FileCategory.Game,
                $"File '{candidate.MainPath}' with empty baseName was silently classified as GAME instead of UNKNOWN");
        }
    }

    /// <summary>
    /// P0-D Regression: Overlapping roots (parent + child directory) must NOT create
    /// duplicate candidates for the same physical file. seenCandidatePaths HashSet
    /// should prevent this — this test verifies the invariant.
    /// </summary>
    [Fact]
    public void Execute_OverlappingRoots_NoDuplicateMainPaths()
    {
        var childDir = Path.Combine(_tempDir, "SNES");
        Directory.CreateDirectory(childDir);
        File.WriteAllBytes(Path.Combine(childDir, "Mario (USA).zip"), new byte[50]);
        File.WriteAllBytes(Path.Combine(childDir, "Zelda (Europe).zip"), new byte[60]);
        File.WriteAllBytes(Path.Combine(_tempDir, "Sonic (USA).zip"), new byte[40]);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new InertAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir, childDir }, // overlapping: child is inside parent
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
        };

        var result = orch.Execute(options);

        // INVARIANT: No duplicate MainPaths in AllCandidates
        var mainPaths = result.AllCandidates.Select(c => c.MainPath).ToList();
        var distinctPaths = mainPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(distinctPaths.Count, mainPaths.Count);
        // Should find exactly 3 unique files
        Assert.Equal(3, result.TotalFilesScanned);
    }

    /// <summary>
    /// P2-03 / R-01: BIOS and GAME titles that normalize to the same base key must be
    /// separated before dedupe grouping. The current architecture does this in CandidateFactory
    /// by prefixing BIOS keys, even if GameKeyNormalizer strips the BIOS tag.
    /// </summary>
    [Fact]
    public void CandidateFactory_BiosAndGameTitle_MustNotCollide()
    {
        var biosBase = GameKeyNormalizer.Normalize("PlayStation (BIOS)");
        var gameBase = GameKeyNormalizer.Normalize("PlayStation (Europe)");

        var biosCandidate = CandidateFactory.Create(
            normalizedPath: "bios.bin",
            extension: ".bin",
            sizeBytes: 1,
            category: FileCategory.Bios,
            gameKey: biosBase,
            region: "UNKNOWN",
            regionScore: 0,
            formatScore: 0,
            versionScore: 0,
            headerScore: 0,
            completenessScore: 0,
            sizeTieBreakScore: 0,
            datMatch: false,
            consoleKey: "PSX");

        var gameCandidate = CandidateFactory.Create(
            normalizedPath: "game.bin",
            extension: ".bin",
            sizeBytes: 1,
            category: FileCategory.Game,
            gameKey: gameBase,
            region: "EU",
            regionScore: 0,
            formatScore: 0,
            versionScore: 0,
            headerScore: 0,
            completenessScore: 0,
            sizeTieBreakScore: 0,
            datMatch: false,
            consoleKey: "PSX");

        // INVARIANT: BIOS files must not share effective dedupe keys with GAME files.
        Assert.NotEqual(biosCandidate.GameKey, gameCandidate.GameKey);
    }

    // ══════════════════════════════════════════════════════════════
    // PRIO 3: MOVE / RESTORE / ROLLBACK
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// P1-12: Rollback reports paths as "restored" even when the source file at newPath
    /// doesn't exist. restoredPaths.Add(oldPath) is OUTSIDE the if(!dryRun && File.Exists)
    /// guard — so for dryRun=false with missing source, it still reports a "restore".
    /// → MUST BE RED until restoredPaths.Add is moved inside the guard.
    /// </summary>
    [Fact]
    public void Rollback_SourceFileNotExisting_MustNotReportAsRestored()
    {
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        var rootDir = Path.Combine(_tempDir, "root");
        var trashDir = Path.Combine(_tempDir, "root", "_TRASH_REGION_DEDUPE");
        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(trashDir);

        var oldPath = Path.Combine(rootDir, "game.zip");
        var newPath = Path.Combine(trashDir, "game.zip");

        // Write audit CSV with a MOVE entry — but DO NOT create the file at newPath.
        // The file was already deleted or moved elsewhere.
        File.WriteAllText(auditPath,
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n" +
            $"{rootDir},{oldPath},{newPath},Move,GAME,,region-dedupe,2026-01-01T00:00:00Z\n",
            Encoding.UTF8);

        var store = new AuditCsvStore();
        var restored = store.Rollback(
            auditPath,
            allowedRestoreRoots: new[] { rootDir },
            allowedCurrentRoots: new[] { rootDir },
            dryRun: false);

        // INVARIANT: If the source file at newPath doesn't exist, nothing was physically
        // restored. The returned list must be empty.
        Assert.Empty(restored);
    }

    /// <summary>
    /// DryRun rollback should report entries that are actually restorable,
    /// i.e. only when the current source file exists at newPath.
    /// Regression guard for the tightened P1-12 semantics.
    /// </summary>
    [Fact]
    public void Rollback_DryRun_ReportsExistingMatchingEntries()
    {
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        var rootDir = Path.Combine(_tempDir, "root");
        var trashDir = Path.Combine(_tempDir, "root", "_TRASH_REGION_DEDUPE");
        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(trashDir);

        var oldPath = Path.Combine(rootDir, "game.zip");
        var newPath = Path.Combine(trashDir, "game.zip");

        // Current source exists -> dry-run should report it as restorable.
        File.WriteAllText(newPath, "data", Encoding.UTF8);

        File.WriteAllText(auditPath,
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n" +
            $"{rootDir},{oldPath},{newPath},Move,GAME,,region-dedupe,2026-01-01T00:00:00Z\n",
            Encoding.UTF8);

        var store = new AuditCsvStore();
        var restored = store.Rollback(
            auditPath,
            allowedRestoreRoots: new[] { rootDir },
            allowedCurrentRoots: new[] { rootDir },
            dryRun: true);

        // DryRun reports what WOULD be restored, but only for existing sources.
        Assert.Single(restored);
    }

    // ══════════════════════════════════════════════════════════════
    // PRIO 4: ORCHESTRATOR / PIPELINE INVARIANTS
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Summen-Invariante: WinnerCount == GroupCount, LoserCount == Σ(group.Losers.Count),
    /// TotalFilesScanned >= WinnerCount + LoserCount.
    /// Regression guard for pipeline consistency.
    /// </summary>
    [Fact]
    public void Execute_DryRun_CountsMustBeConsistent()
    {
        CreateFile("Mario (USA).zip", 50);
        CreateFile("Mario (Europe).zip", 50);
        CreateFile("Mario (Japan).zip", 50);
        CreateFile("Zelda (USA).zip", 60);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new InertAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            PreferRegions = new[] { "US", "EU", "JP" }
        };

        var result = orch.Execute(options);

        // INVARIANT: One winner per group
        Assert.Equal(result.GroupCount, result.WinnerCount);

        // INVARIANT: LoserCount matches actual loser count from DedupeGroups
        var calculatedLosers = result.DedupeGroups.Sum(g => g.Losers.Count);
        Assert.Equal(calculatedLosers, result.LoserCount);

        // INVARIANT: TotalFilesScanned >= winners + losers
        Assert.True(result.TotalFilesScanned >= result.WinnerCount + result.LoserCount,
            $"TotalFilesScanned ({result.TotalFilesScanned}) < WinnerCount ({result.WinnerCount}) + LoserCount ({result.LoserCount})");
    }

    /// <summary>
    /// After Move phase: LoserCount is overwritten with MoveResult.MoveCount (line ~302),
    /// discarding the known-planned loser count. When move failures occur,
    /// LoserCount != MoveCount + FailCount + SkipCount.
    /// → MUST BE RED when FailCount > 0 because LoserCount = MoveCount only.
    /// </summary>
    [Fact]
    public void Execute_MoveWithFailures_LoserCountMustReflectPlannedLosers()
    {
        CreateFile("Game (USA).zip", 50);
        CreateFile("Game (Europe).zip", 50);

        // FailingMoveFileSystem always fails MoveItemSafely → FailCount = 1, MoveCount = 0
        var failingFs = new FailingMoveFileSystem(_tempDir);
        var audit = new InertAuditStore();
        var orch = new RunOrchestrator(failingFs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "US" }
        };

        var result = orch.Execute(options);

        Assert.NotNull(result.MoveResult);
        // There is 1 planned loser (Europe file)
        int plannedLosers = result.MoveResult!.MoveCount + result.MoveResult.FailCount + result.MoveResult.SkipCount;
        Assert.True(plannedLosers > 0, "There should be at least 1 planned loser");

        // INVARIANT: LoserCount must equal planned losers (MoveCount + FailCount + SkipCount),
        // not just MoveCount. The current code sets LoserCount = MoveCount, losing FailCount info.
        Assert.Equal(plannedLosers, result.LoserCount);
    }

    /// <summary>
    /// JunkMoveResult and MoveResult must be independently queryable.
    /// Junk moves must NOT be mixed into the dedupe MoveResult.
    /// Regression guard for P1-02 (Junk+Dedupe separation).
    /// </summary>
    [Fact]
    public void Execute_JunkAndDedupeMovesAreSeparate()
    {
        // Standalone JUNK file (no other candidate with same key)
        CreateFile("SomeJunk (Beta).zip", 30);
        // GAME files with same GameKey → 1 winner, 1 loser
        CreateFile("RealGame (USA).zip", 50);
        CreateFile("RealGame (Europe).zip", 50);

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new InertAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            RemoveJunk = true,
            PreferRegions = new[] { "US" }
        };

        var result = orch.Execute(options);

        // INVARIANT: JunkMoveResult and MoveResult are independently queryable
        Assert.NotNull(result.JunkMoveResult);
        Assert.NotNull(result.MoveResult);

        // Junk moves must only count junk files
        Assert.True(result.JunkMoveResult!.MoveCount >= 1,
            "At least 1 standalone junk file should have been removed");

        // Dedupe moves must only count dedupe losers
        Assert.True(result.MoveResult!.MoveCount >= 1,
            "At least 1 dedupe loser should have been moved");

        // INVARIANT: JunkRemovedCount matches JunkMoveResult.MoveCount exactly
        Assert.Equal(result.JunkRemovedCount, result.JunkMoveResult.MoveCount);
    }

    // ══════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════

    private string CreateFile(string name, int sizeBytes)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    private static IEnumerable<IEnumerable<T>> GetPermutations<T>(T[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (int i = 0; i < items.Length; i++)
        {
            var rest = items.Where((_, idx) => idx != i).ToArray();
            foreach (var perm in GetPermutations(rest))
            {
                yield return new[] { items[i] }.Concat(perm);
            }
        }
    }

    // ── Fakes ─────────────────────────────────────────────────────

    /// <summary>
    /// Minimal IAuditStore that does nothing. Used when audit behavior is not under test.
    /// </summary>
    private sealed class InertAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => false;
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
            string[] allowedCurrentRoots, bool dryRun = false) => Array.Empty<string>();
        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "") { }
        public void Flush(string auditCsvPath) { }
    }

    /// <summary>
    /// FileSystem that returns real file listings but ALWAYS fails MoveItemSafely (returns null).
    /// Used to test FailCount scenarios in the move phase.
    /// </summary>
    private sealed class FailingMoveFileSystem : IFileSystem
    {
        private readonly RomCleanup.Infrastructure.FileSystem.FileSystemAdapter _real = new();
        private readonly string _root;

        public FailingMoveFileSystem(string root) => _root = root;

        public bool TestPath(string literalPath, string pathType = "Any")
            => _real.TestPath(literalPath, pathType);
        public string EnsureDirectory(string path)
            => _real.EnsureDirectory(path);
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => _real.GetFilesSafe(root, allowedExtensions);
        public string? MoveItemSafely(string sourcePath, string destinationPath)
            => null; // always fail
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => _real.ResolveChildPathWithinRoot(rootPath, relativePath);
        public bool IsReparsePoint(string path)
            => _real.IsReparsePoint(path);
        public void DeleteFile(string path)
            => _real.DeleteFile(path);
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
            => _real.CopyFile(sourcePath, destinationPath, overwrite);
    }
}
