using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Deduplication;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for FolderDeduplicator — specifically targeting:
/// - DeduplicatePs3 (0 existing tests — entire method uncovered)
/// - DeduplicateByBaseName Move-mode error paths (BLOCKED, move failure, exception)
/// - AutoDeduplicate dispatch paths (PS3 Move, both roots, error handling)
/// </summary>
public sealed class FolderDeduplicatorCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public FolderDeduplicatorCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FolderDedupCov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* cleanup best-effort */ }
    }

    #region Helpers

    private static void CreatePs3Folder(string path, byte[] content)
    {
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "PS3_DISC.SFB"), content);
        var gameDir = Path.Combine(path, "PS3_GAME");
        Directory.CreateDirectory(gameDir);
        File.WriteAllBytes(Path.Combine(gameDir, "PARAM.SFO"), content);
        var usrDir = Path.Combine(gameDir, "USRDIR");
        Directory.CreateDirectory(usrDir);
        File.WriteAllBytes(Path.Combine(usrDir, "EBOOT.BIN"), content);
    }

    private static void CreateFolderWithFiles(string path, int fileCount, DateTime? lastWrite = null)
    {
        Directory.CreateDirectory(path);
        for (int i = 0; i < fileCount; i++)
        {
            var fp = Path.Combine(path, $"file{i}.bin");
            File.WriteAllBytes(fp, [(byte)i]);
            if (lastWrite.HasValue)
                File.SetLastWriteTimeUtc(fp, lastWrite.Value);
        }
    }

    #endregion

    #region ControllableFileSystem

    private sealed class ControllableFileSystem : IFileSystem
    {
        public HashSet<string> BlockedResolveRoots { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool MoveReturnsFailure { get; set; }
        public Exception? MoveThrows { get; set; }

        public string EnsureDirectory(string path) { Directory.CreateDirectory(path); return path; }
        public bool TestPath(string literalPath, string pathType = "Any") =>
            File.Exists(literalPath) || Directory.Exists(literalPath);
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? ext = null) => [];
        public string? MoveItemSafely(string src, string dest) => dest;

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
        {
            var trimmed = rootPath.TrimEnd(Path.DirectorySeparatorChar);
            if (BlockedResolveRoots.Contains(trimmed))
                return null;
            var full = Path.Combine(rootPath, relativePath);
            return full.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        public bool MoveDirectorySafely(string src, string dest)
        {
            if (MoveThrows is not null) throw MoveThrows;
            if (MoveReturnsFailure) return false;
            if (Directory.Exists(src)) { Directory.Move(src, dest); return true; }
            return false;
        }

        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { if (File.Exists(path)) File.Delete(path); }
        public void CopyFile(string src, string dest, bool overwrite = false) =>
            File.Copy(src, dest, overwrite);
    }

    #endregion

    // =================================================================
    //  DeduplicatePs3 Tests — entire method previously untested
    // =================================================================

    [Fact]
    public void DeduplicatePs3_TwoFolders_SameHash_MovesDuplicate()
    {
        var root = Path.Combine(_tempDir, "ps3dup");
        Directory.CreateDirectory(root);
        var content = new byte[] { 1, 2, 3, 4, 5 };
        CreatePs3Folder(Path.Combine(root, "GameA"), content);
        CreatePs3Folder(Path.Combine(root, "GameB"), content);

        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicatePs3([root]);

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Dupes);
        Assert.Equal(1, result.Moved);
        // One game folder + the PS3_DUPES folder should remain
        var remaining = Directory.GetDirectories(root)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(RunConstants.WellKnownFolders.Ps3Dupes, remaining);
    }

    [Fact]
    public void DeduplicatePs3_DifferentHashes_NoDedup()
    {
        var root = Path.Combine(_tempDir, "ps3diff");
        Directory.CreateDirectory(root);
        CreatePs3Folder(Path.Combine(root, "Game1"), [1, 2, 3]);
        CreatePs3Folder(Path.Combine(root, "Game2"), [4, 5, 6]);

        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicatePs3([root]);

        Assert.Equal(2, result.Total);
        Assert.Equal(0, result.Dupes);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void DeduplicatePs3_FolderWithoutKeyFiles_Skipped()
    {
        var root = Path.Combine(_tempDir, "ps3nokey");
        Directory.CreateDirectory(root);
        CreatePs3Folder(Path.Combine(root, "ValidGame"), [10, 20]);
        CreateFolderWithFiles(Path.Combine(root, "NotPS3"), 3);

        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicatePs3([root]);

        Assert.Equal(2, result.Total);
        Assert.Equal(0, result.Dupes);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void DeduplicatePs3_EmptyRoots_ReturnsZeroes()
    {
        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicatePs3([]);

        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.Dupes);
        Assert.Equal(0, result.Moved);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void DeduplicatePs3_NonExistentRoot_ReturnsZeroes()
    {
        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicatePs3([Path.Combine(_tempDir, "nonexistent_ps3")]);

        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void DeduplicatePs3_Cancellation_ThrowsOce()
    {
        var root = Path.Combine(_tempDir, "ps3cancel");
        Directory.CreateDirectory(root);
        CreatePs3Folder(Path.Combine(root, "Game"), [1]);

        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            dedup.DeduplicatePs3([root], ct: cts.Token));
    }

    [Fact]
    public void DeduplicatePs3_LaterFolderWithMoreFiles_ReplacesWinner()
    {
        // Tests the winner-replacement path: current folder has more files than existing
        var root = Path.Combine(_tempDir, "ps3replace");
        Directory.CreateDirectory(root);
        var content = new byte[] { 42, 42, 42 };
        // GameA: PS3 key files only (3 files)
        CreatePs3Folder(Path.Combine(root, "GameA"), content);
        // GameB: PS3 key files + extras (6 files total → wins)
        CreatePs3Folder(Path.Combine(root, "GameB"), content);
        for (int i = 0; i < 3; i++)
            File.WriteAllBytes(Path.Combine(root, "GameB", $"extra{i}.dat"), [(byte)i]);

        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicatePs3([root]);

        Assert.Equal(1, result.Dupes);
        Assert.Equal(1, result.Moved);
        // GameB has more files → replaces GameA as winner
        Assert.True(Directory.Exists(Path.Combine(root, "GameB")),
            "GameB (more files) should remain in root");
        var dupeDir = Path.Combine(root, RunConstants.WellKnownFolders.Ps3Dupes);
        Assert.True(Directory.Exists(Path.Combine(dupeDir, "GameA")),
            "GameA (fewer files) should be moved to PS3_DUPES");
    }

    [Fact]
    public void DeduplicatePs3_EqualFileCount_FirstAlphabeticallyWins()
    {
        var root = Path.Combine(_tempDir, "ps3alpha");
        Directory.CreateDirectory(root);
        var content = new byte[] { 99, 99 };
        CreatePs3Folder(Path.Combine(root, "AAA_Game"), content);
        CreatePs3Folder(Path.Combine(root, "ZZZ_Game"), content);

        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicatePs3([root]);

        Assert.Equal(1, result.Dupes);
        Assert.Equal(1, result.Moved);
        // AAA_Game is alphabetically first → processed first → stays winner
        Assert.True(Directory.Exists(Path.Combine(root, "AAA_Game")),
            "Alphabetically first folder should remain as winner");
    }

    [Fact]
    public void DeduplicatePs3_CustomDupeRoot_MovesToCustomLocation()
    {
        var root = Path.Combine(_tempDir, "ps3custom");
        var customDupes = Path.Combine(_tempDir, "myps3dupes");
        Directory.CreateDirectory(root);
        var content = new byte[] { 7, 7, 7 };
        CreatePs3Folder(Path.Combine(root, "Game1"), content);
        CreatePs3Folder(Path.Combine(root, "Game2"), content);

        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicatePs3([root], dupeRoot: customDupes);

        Assert.Equal(1, result.Moved);
        // dupeBase = Path.Combine(customDupes, Path.GetFileName(root)) = customDupes\ps3custom
        var expectedDupeDir = Path.Combine(customDupes, Path.GetFileName(root));
        Assert.True(Directory.Exists(expectedDupeDir),
            $"Custom dupe subdir should be created at {expectedDupeDir}");
    }

    [Fact]
    public void DeduplicatePs3_SourceBlockedByPathValidation_NotMoved()
    {
        var root = Path.Combine(_tempDir, "ps3blocked");
        Directory.CreateDirectory(root);
        var content = new byte[] { 11, 22, 33 };
        CreatePs3Folder(Path.Combine(root, "Game1"), content);
        CreatePs3Folder(Path.Combine(root, "Game2"), content);

        var fs = new ControllableFileSystem();
        // Block source path validation → move is skipped via continue
        fs.BlockedResolveRoots.Add(root);
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicatePs3([root]);

        Assert.Equal(1, result.Dupes);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void DeduplicatePs3_DestBlockedByPathValidation_NotMoved()
    {
        var root = Path.Combine(_tempDir, "ps3dstblk");
        Directory.CreateDirectory(root);
        var content = new byte[] { 11, 22, 33 };
        CreatePs3Folder(Path.Combine(root, "Game1"), content);
        CreatePs3Folder(Path.Combine(root, "Game2"), content);

        // Block destination: dupeBase = root\PS3_DUPES (fully resolved)
        var dupeBase = Path.GetFullPath(Path.Combine(root, RunConstants.WellKnownFolders.Ps3Dupes));
        var fs = new ControllableFileSystem();
        fs.BlockedResolveRoots.Add(dupeBase);
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicatePs3([root]);

        Assert.Equal(1, result.Dupes);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void DeduplicatePs3_MoveReturnsFalse_NotMoved()
    {
        var root = Path.Combine(_tempDir, "ps3movefail");
        Directory.CreateDirectory(root);
        var content = new byte[] { 55, 66 };
        CreatePs3Folder(Path.Combine(root, "GameX"), content);
        CreatePs3Folder(Path.Combine(root, "GameY"), content);

        var fs = new ControllableFileSystem { MoveReturnsFailure = true };
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicatePs3([root]);

        Assert.Equal(1, result.Dupes);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void DeduplicatePs3_MoveThrowsException_PropagatesException()
    {
        var root = Path.Combine(_tempDir, "ps3moveex");
        Directory.CreateDirectory(root);
        var content = new byte[] { 77, 88 };
        CreatePs3Folder(Path.Combine(root, "GameM"), content);
        CreatePs3Folder(Path.Combine(root, "GameN"), content);

        var fs = new ControllableFileSystem { MoveThrows = new IOException("disk full") };
        var dedup = new FolderDeduplicator(fs);

        // DeduplicatePs3 has no try/catch around MoveDirectorySafely
        var ex = Assert.Throws<IOException>(() => dedup.DeduplicatePs3([root]));
        Assert.Equal("disk full", ex.Message);
    }

    [Fact]
    public void DeduplicatePs3_OnlyPartialKeyFiles_StillHashesAndDedupes()
    {
        // Only PS3_DISC.SFB present → partial key files still produce a hash
        var root = Path.Combine(_tempDir, "ps3partial");
        Directory.CreateDirectory(root);
        var folder1 = Path.Combine(root, "PartialGame1");
        Directory.CreateDirectory(folder1);
        File.WriteAllBytes(Path.Combine(folder1, "PS3_DISC.SFB"), [42, 43]);

        var folder2 = Path.Combine(root, "PartialGame2");
        Directory.CreateDirectory(folder2);
        File.WriteAllBytes(Path.Combine(folder2, "PS3_DISC.SFB"), [42, 43]);

        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicatePs3([root]);

        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Dupes);
    }

    [Fact]
    public void DeduplicatePs3_LogsExpectedMessages()
    {
        var root = Path.Combine(_tempDir, "ps3log");
        Directory.CreateDirectory(root);
        CreatePs3Folder(Path.Combine(root, "LogGame1"), [1, 2, 3]);
        CreatePs3Folder(Path.Combine(root, "LogGame2"), [1, 2, 3]);

        var logs = new List<string>();
        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs, log: msg => logs.Add(msg));

        dedup.DeduplicatePs3([root]);

        Assert.Contains(logs, l => l.Contains("PS3 Dedupe:"));
        Assert.Contains(logs, l => l.Contains("DUP"));
    }

    // =================================================================
    //  DeduplicateByBaseName — Move-mode error/safety paths
    // =================================================================

    [Fact]
    public void DeduplicateByBaseName_Cancellation_ThrowsOce()
    {
        var root = Path.Combine(_tempDir, "basecancel");
        Directory.CreateDirectory(root);
        CreateFolderWithFiles(Path.Combine(root, "Game"), 1);

        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            dedup.DeduplicateByBaseName([root], ct: cts.Token));
    }

    [Fact]
    public void DeduplicateByBaseName_SourceBlocked_ErrorActionWithBlocked()
    {
        var root = Path.Combine(_tempDir, "basesrcblk");
        Directory.CreateDirectory(root);
        CreateFolderWithFiles(Path.Combine(root, "MyGame"), 2);
        CreateFolderWithFiles(Path.Combine(root, "MyGame (v1)"), 1);

        var fs = new ControllableFileSystem();
        fs.BlockedResolveRoots.Add(Path.GetFullPath(root));
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicateByBaseName([root], mode: RunConstants.ModeMove);

        Assert.True(result.Errors > 0, "Source path blocked should produce errors");
        Assert.Contains(result.Actions, a => a.Action == "BLOCKED");
    }

    [Fact]
    public void DeduplicateByBaseName_DestBlocked_ErrorActionWithBlocked()
    {
        var root = Path.Combine(_tempDir, "basedstblk");
        Directory.CreateDirectory(root);
        CreateFolderWithFiles(Path.Combine(root, "TestGame"), 2);
        CreateFolderWithFiles(Path.Combine(root, "TestGame (v2)"), 1);

        // Block the dupeBase path so destination validation fails
        var dupeBase = Path.Combine(Path.GetFullPath(root), RunConstants.WellKnownFolders.FolderDupes);
        var fs = new ControllableFileSystem();
        fs.BlockedResolveRoots.Add(dupeBase);
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicateByBaseName([root], mode: RunConstants.ModeMove);

        Assert.True(result.Errors > 0, "Dest path blocked should produce errors");
        Assert.Contains(result.Actions, a =>
            a.Action == "BLOCKED" && a.Error != null && a.Error.Contains("Destination"));
    }

    [Fact]
    public void DeduplicateByBaseName_MoveReturnsFalse_ErrorAction()
    {
        var root = Path.Combine(_tempDir, "basemovefail");
        Directory.CreateDirectory(root);
        CreateFolderWithFiles(Path.Combine(root, "SomeGame"), 2);
        CreateFolderWithFiles(Path.Combine(root, "SomeGame [EUR]"), 1);

        var fs = new ControllableFileSystem { MoveReturnsFailure = true };
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicateByBaseName([root], mode: RunConstants.ModeMove);

        Assert.True(result.Errors > 0);
        Assert.Contains(result.Actions, a =>
            a.Action == "ERROR" && a.Error == "Move returned false");
    }

    [Fact]
    public void DeduplicateByBaseName_MoveThrowsException_ErrorWithMessage()
    {
        var root = Path.Combine(_tempDir, "basemoveex");
        Directory.CreateDirectory(root);
        CreateFolderWithFiles(Path.Combine(root, "Puzzle"), 3);
        CreateFolderWithFiles(Path.Combine(root, "Puzzle (Japan)"), 1);

        var fs = new ControllableFileSystem { MoveThrows = new IOException("access denied") };
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicateByBaseName([root], mode: RunConstants.ModeMove);

        Assert.True(result.Errors > 0);
        Assert.Contains(result.Actions, a =>
            a.Action == "ERROR" && a.Error == "access denied");
    }

    [Fact]
    public void DeduplicateByBaseName_CustomDupeRoot_UsesProvided()
    {
        var root = Path.Combine(_tempDir, "basecustom");
        var customDupes = Path.Combine(_tempDir, "basecustom_dupes");
        Directory.CreateDirectory(root);
        CreateFolderWithFiles(Path.Combine(root, "RPG"), 2);
        CreateFolderWithFiles(Path.Combine(root, "RPG [USA]"), 1);

        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicateByBaseName([root], dupeRoot: customDupes,
            mode: RunConstants.ModeMove);

        Assert.True(result.Moved > 0);
        Assert.True(Directory.Exists(customDupes), "Custom dupe root should be created");
    }

    [Fact]
    public void DeduplicateByBaseName_NonExistentRoot_LogsWarning()
    {
        var logs = new List<string>();
        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs, log: msg => logs.Add(msg));

        var result = dedup.DeduplicateByBaseName(
            [Path.Combine(_tempDir, "gone_folder")]);

        Assert.Equal(0, result.TotalFolders);
        Assert.Contains(logs, l => l.Contains("WARNING") || l.Contains("Root not found"));
    }

    [Fact]
    public void DeduplicateByBaseName_MultipleDupeGroups_AllReported()
    {
        var root = Path.Combine(_tempDir, "basemulti");
        Directory.CreateDirectory(root);
        // Group 1: Action
        CreateFolderWithFiles(Path.Combine(root, "Action"), 2);
        CreateFolderWithFiles(Path.Combine(root, "Action [EUR]"), 1);
        // Group 2: Racing
        CreateFolderWithFiles(Path.Combine(root, "Racing"), 3);
        CreateFolderWithFiles(Path.Combine(root, "Racing (v2)"), 1);

        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.DeduplicateByBaseName([root]);

        Assert.True(result.DupeGroups >= 2, "Should detect at least 2 dupe groups");
        Assert.True(result.Actions.Count >= 2, "Should generate at least 2 dupe actions");
    }

    // =================================================================
    //  AutoDeduplicate — dispatch and error handling paths
    // =================================================================

    [Fact]
    public void AutoDeduplicate_Ps3MoveMode_DispatchesToPs3Dedupe()
    {
        var root = Path.Combine(_tempDir, "autops3move");
        Directory.CreateDirectory(root);
        var content = new byte[] { 1, 1, 1 };
        CreatePs3Folder(Path.Combine(root, "Ps3Game1"), content);
        CreatePs3Folder(Path.Combine(root, "Ps3Game2"), content);

        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.AutoDeduplicate(
            [root],
            mode: RunConstants.ModeMove,
            consoleKeyDetector: _ => "PS3");

        Assert.Contains(root, result.Ps3Roots);
        Assert.Single(result.Results);
        Assert.Equal("PS3", result.Results[0].Type);
        var ps3Result = Assert.IsType<Ps3FolderDedupeResult>(result.Results[0].Result);
        Assert.Equal(1, ps3Result.Moved);
    }

    [Fact]
    public void AutoDeduplicate_BothPs3AndFolder_BothDispatched()
    {
        var ps3Root = Path.Combine(_tempDir, "autobothps3");
        var dosRoot = Path.Combine(_tempDir, "autobothdos");
        Directory.CreateDirectory(ps3Root);
        Directory.CreateDirectory(dosRoot);
        CreatePs3Folder(Path.Combine(ps3Root, "Ps3A"), [5, 6]);
        CreateFolderWithFiles(Path.Combine(dosRoot, "DosGame"), 1);
        CreateFolderWithFiles(Path.Combine(dosRoot, "DosGame (v2)"), 1);

        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.AutoDeduplicate(
            [ps3Root, dosRoot],
            mode: RunConstants.ModeMove,
            consoleKeyDetector: r => r == ps3Root ? "PS3" : "DOS");

        Assert.Contains(ps3Root, result.Ps3Roots);
        Assert.Contains(dosRoot, result.FolderRoots);
    }

    [Fact]
    public void AutoDeduplicate_EmptyAndInvalidRoots_AllFiltered()
    {
        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs);

        var result = dedup.AutoDeduplicate(
            ["", "  ", Path.Combine(_tempDir, "nonexist_auto")],
            consoleKeyDetector: _ => "DOS");

        Assert.Empty(result.Ps3Roots);
        Assert.Empty(result.FolderRoots);
    }

    [Fact]
    public void AutoDeduplicate_NullConsoleKeyDetector_NoRootsDispatched()
    {
        var root = Path.Combine(_tempDir, "autonull");
        Directory.CreateDirectory(root);
        CreateFolderWithFiles(Path.Combine(root, "Game"), 1);

        var logs = new List<string>();
        var fs = new ControllableFileSystem();
        var dedup = new FolderDeduplicator(fs, log: msg => logs.Add(msg));

        var result = dedup.AutoDeduplicate([root], consoleKeyDetector: null);

        Assert.Empty(result.Ps3Roots);
        Assert.Empty(result.FolderRoots);
        Assert.Contains(logs, l => l.Contains("no roots detected"));
    }
}
