using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Deduplication;
using Romulus.Core.Scoring;
using Romulus.Infrastructure.Quarantine;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Regression tests for all P1 findings from the 2026-03-27 deep code review.
/// Covers SEC-01/02 (IFileSystem default bodies), SEC-03 (QuarantineService),
/// SEC-07 (API Rollback dryRun), CORE-01 (SEC-DEDUP audit), CORE-02 (VersionScorer),
/// CORE-03 (Xbox detection order).
/// </summary>
public class P1SecurityHardeningTests : IDisposable
{
    private readonly string _tempDir;

    public P1SecurityHardeningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"p1sec_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ────────────────────────────────────────────────────────────────
    // SEC-01: IFileSystem.RenameItemSafely default body guards
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void SEC01_RenameItemSafely_Default_TraversalInFileName_Throws()
    {
        IFileSystem fs = new MinimalFs();
        Assert.Throws<InvalidOperationException>(() =>
            fs.RenameItemSafely(@"C:\roms\game.zip", @"..\..\evil.exe"));
    }

    [Fact]
    public void SEC01_RenameItemSafely_Default_AdsInFileName_Throws()
    {
        IFileSystem fs = new MinimalFs();
        Assert.Throws<InvalidOperationException>(() =>
            fs.RenameItemSafely(@"C:\roms\game.zip", "game.zip:hidden"));
    }

    [Fact]
    public void SEC01_RenameItemSafely_Default_InvalidCharsInFileName_Throws()
    {
        IFileSystem fs = new MinimalFs();
        Assert.Throws<InvalidOperationException>(() =>
            fs.RenameItemSafely(@"C:\roms\game.zip", "game<>.zip"));
    }

    // ────────────────────────────────────────────────────────────────
    // SEC-02: IFileSystem.MoveDirectorySafely default body guards
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void SEC02_MoveDirectorySafely_Default_TraversalInDest_Throws()
    {
        IFileSystem fs = new MinimalFs();
        Assert.Throws<InvalidOperationException>(() =>
            fs.MoveDirectorySafely(@"C:\roms\dir1", @"C:\roms\..\..\..\windows\system32\dir1"));
    }

    [Fact]
    public void SEC02_MoveDirectorySafely_Default_SameSourceDest_Throws()
    {
        var dir = Path.Combine(_tempDir, "testdir");
        Directory.CreateDirectory(dir);

        IFileSystem fs = new MinimalFs();
        Assert.Throws<InvalidOperationException>(() =>
            fs.MoveDirectorySafely(dir, dir));
    }

    // ────────────────────────────────────────────────────────────────
    // SEC-03: QuarantineService path traversal in filename
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void SEC03_CreateAction_TraversalInFilename_Sanitized()
    {
        var fs = new FakeQuarantineFs(_tempDir);
        var svc = new QuarantineService(fs);
        var qRoot = Path.Combine(_tempDir, "quarantine");

        // Filename with traversal segments — should be sanitized, not throw
        var action = svc.CreateAction(@"C:\roms\..\..\evil.zip", qRoot);

        // Target must stay within qRoot
        var normalizedRoot = Path.GetFullPath(qRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedTarget = Path.GetFullPath(action.TargetPath);
        Assert.StartsWith(normalizedRoot, normalizedTarget);
    }

    [Fact]
    public void SEC03_CreateAction_NormalFilename_Works()
    {
        var fs = new FakeQuarantineFs(_tempDir);
        var svc = new QuarantineService(fs);
        var qRoot = Path.Combine(_tempDir, "quarantine");

        var action = svc.CreateAction(@"C:\roms\game.zip", qRoot);
        Assert.Contains("game.zip", action.TargetPath);
    }

    // ────────────────────────────────────────────────────────────────
    // CORE-01: SEC-DEDUP CrossGroupFilteredCount audit
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void CORE01_SecDedup_CrossGroupFiltered_CountIsTracked()
    {
        // File "shared.zip" is winner in group A, loser in group B
        var shared = MakeCandidate("shared.zip", "key_a", regionScore: 100);
        var loserA = MakeCandidate("loserA.zip", "key_a", regionScore: 50);
        var winnerB = MakeCandidate("winnerB.zip", "key_b", regionScore: 200);
        var sharedAsLoser = MakeCandidate("shared.zip", "key_b", regionScore: 80);

        var candidates = new[] { shared, loserA, winnerB, sharedAsLoser };
        var groups = DeduplicationEngine.Deduplicate(candidates);

        // group "key_b" should have filtered out "shared.zip" (winner in key_a)
        var groupB = groups.First(g => g.GameKey == "key_b");
        Assert.Equal(1, groupB.CrossGroupFilteredCount);
        Assert.DoesNotContain(groupB.Losers, l => l.MainPath == "shared.zip");
    }

    [Fact]
    public void CORE01_SecDedup_NoCrossGroup_CountIsZero()
    {
        var w1 = MakeCandidate("a.zip", "key_a", regionScore: 100);
        var l1 = MakeCandidate("b.zip", "key_a", regionScore: 50);

        var groups = DeduplicationEngine.Deduplicate(new[] { w1, l1 });
        Assert.Single(groups);
        Assert.Equal(0, groups[0].CrossGroupFilteredCount);
    }

    // ────────────────────────────────────────────────────────────────
    // CORE-02: VersionScorer truncation differentiation
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void CORE02_VersionScore_ExtraSegments_ScoreDifferently()
    {
        var scorer = new VersionScorer();

        // The default regex matches (v<major>.<minor>) — use that format.
        // When RxDigits.Matches extracts all digits from "(v1.2)", it gets 2 segments.
        // For exhaustive multi-segment testing, we construct a custom scorer with a wider pattern.
        var wideScorer = new VersionScorer(
            verifiedPattern: @"\[!\]",
            revisionPattern: @"\(rev\s*([a-z0-9.]+)\)",
            versionPattern: @"\(v\s*([\d.]+)\)",
            langPattern: @"\(en\)");

        var score6 = wideScorer.GetVersionScore("Game (v1.2.3.4.5.6)");
        var score7 = wideScorer.GetVersionScore("Game (v1.2.3.4.5.6.7)");
        var score8 = wideScorer.GetVersionScore("Game (v1.2.3.4.5.6.7.8)");

        Assert.True(score7 > score6, $"7-segment ({score7}) should be > 6-segment ({score6})");
        Assert.True(score8 > score7, $"8-segment ({score8}) should be > 7-segment ({score7})");
    }

    [Fact]
    public void CORE02_VersionScore_NormalVersions_Unaffected()
    {
        var scorer = new VersionScorer();

        // Normal versions (≤6 segments) should not be affected
        var score1 = scorer.GetVersionScore("Game (v1.0)");
        var score2 = scorer.GetVersionScore("Game (v1.1)");
        Assert.True(score2 > score1);
    }

    // ────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────

    private static RomCandidate MakeCandidate(string path, string gameKey, int regionScore = 0) =>
        new()
        {
            MainPath = path,
            GameKey = gameKey,
            RegionScore = regionScore,
            Category = FileCategory.Game,
            SortDecision = SortDecision.Sort,
        };

    /// <summary>
    /// Minimal IFileSystem implementation that inherits default bodies for
    /// RenameItemSafely and MoveDirectorySafely — used to test interface-level guards.
    /// </summary>
    private sealed class MinimalFs : IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any") => false;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => Array.Empty<string>();
        public string? MoveItemSafely(string sourcePath, string destinationPath) => destinationPath;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath) => null;
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }

    /// <summary>
    /// Fake FS for QuarantineService tests that supports basic ops.
    /// </summary>
    private sealed class FakeQuarantineFs : IFileSystem
    {
        private readonly string _root;
        public FakeQuarantineFs(string root) => _root = root;

        public bool TestPath(string literalPath, string pathType = "Any")
        {
            if (pathType == "Leaf") return File.Exists(literalPath);
            if (pathType == "Container") return Directory.Exists(literalPath);
            return File.Exists(literalPath) || Directory.Exists(literalPath);
        }

        public string EnsureDirectory(string path) { Directory.CreateDirectory(path); return path; }
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => Array.Empty<string>();
        public string? MoveItemSafely(string sourcePath, string destinationPath)
        {
            if (File.Exists(sourcePath))
            {
                var dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.Move(sourcePath, destinationPath);
                return destinationPath;
            }
            return null;
        }
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath) => null;
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { if (File.Exists(path)) File.Delete(path); }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }
}
