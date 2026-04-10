using Romulus.Infrastructure.Deduplication;
using Xunit;

namespace Romulus.Tests;

public sealed class FolderDeduplicatorTests
{
    // =========================================================================
    //  GetFolderBaseKey Tests
    // =========================================================================

    [Fact]
    public void GetFolderBaseKey_SimpleFolder_ReturnsLowerCase()
    {
        var result = FolderDeduplicator.GetFolderBaseKey("Super Mario World");
        Assert.Equal("super mario world", result);
    }

    [Fact]
    public void GetFolderBaseKey_StripsParentheses()
    {
        var result = FolderDeduplicator.GetFolderBaseKey("Sonic (Europe) (En,Fr)");
        Assert.Equal("sonic", result);
    }

    [Fact]
    public void GetFolderBaseKey_PreservesDiscMarker()
    {
        var result = FolderDeduplicator.GetFolderBaseKey("Final Fantasy VII (Disc 1)");
        Assert.Contains("disc 1", result);
    }

    [Fact]
    public void GetFolderBaseKey_PreservesSideMarker()
    {
        var result = FolderDeduplicator.GetFolderBaseKey("Game (Side A)");
        Assert.Contains("side a", result);
    }

    [Fact]
    public void GetFolderBaseKey_PreservesAGATag()
    {
        var key1 = FolderDeduplicator.GetFolderBaseKey("Turrican (AGA)");
        var key2 = FolderDeduplicator.GetFolderBaseKey("Turrican (ECS)");
        Assert.NotEqual(key1, key2); // AGA and ECS should produce different keys
    }

    [Fact]
    public void GetFolderBaseKey_PreservesNTSCvsPAL()
    {
        var key1 = FolderDeduplicator.GetFolderBaseKey("Game (NTSC)");
        var key2 = FolderDeduplicator.GetFolderBaseKey("Game (PAL)");
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void GetFolderBaseKey_StripsBrackets()
    {
        var result = FolderDeduplicator.GetFolderBaseKey("Game [h] [t1]");
        Assert.Equal("game", result);
    }

    [Fact]
    public void GetFolderBaseKey_StripsVersionSuffix()
    {
        var result = FolderDeduplicator.GetFolderBaseKey("Game v1.2");
        Assert.Equal("game", result);
    }

    [Fact]
    public void GetFolderBaseKey_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", FolderDeduplicator.GetFolderBaseKey(""));
    }

    [Fact]
    public void GetFolderBaseKey_WhitespaceOnly_ReturnsEmpty()
    {
        Assert.Equal("", FolderDeduplicator.GetFolderBaseKey("   "));
    }

    [Fact]
    public void GetFolderBaseKey_NormalizesUnicode()
    {
        // FormC normalization should handle basic equivalences
        var result = FolderDeduplicator.GetFolderBaseKey("Pokémon");
        Assert.Equal("pokémon", result);
    }

    [Fact]
    public void GetFolderBaseKey_CollapsesSpaces()
    {
        var result = FolderDeduplicator.GetFolderBaseKey("Game   Name   Here");
        Assert.Equal("game name here", result);
    }

    [Fact]
    public void GetFolderBaseKey_StripsNonPreservedParensKeepsPreserved()
    {
        var result = FolderDeduplicator.GetFolderBaseKey("Game (Europe) (Disc 2) (En,Fr)");
        Assert.Contains("disc 2", result);
        Assert.DoesNotContain("europe", result);
        Assert.DoesNotContain("en,fr", result);
    }

    // =========================================================================
    //  PS3 Tests
    // =========================================================================

    [Fact]
    public void IsPs3MultidiscFolder_WithDiscMarker_ReturnsTrue()
    {
        Assert.True(FolderDeduplicator.IsPs3MultidiscFolder("Game Disc 1"));
        Assert.True(FolderDeduplicator.IsPs3MultidiscFolder("Game CD2"));
    }

    [Fact]
    public void IsPs3MultidiscFolder_WithoutDiscMarker_ReturnsFalse()
    {
        Assert.False(FolderDeduplicator.IsPs3MultidiscFolder("Normal Game"));
    }

    // =========================================================================
    //  Console Key Detection
    // =========================================================================

    [Fact]
    public void NeedsFolderDedupe_DOS_ReturnsTrue()
    {
        Assert.True(FolderDeduplicator.NeedsFolderDedupe("DOS"));
        Assert.True(FolderDeduplicator.NeedsFolderDedupe("AMIGA"));
        Assert.True(FolderDeduplicator.NeedsFolderDedupe("XBOX"));
    }

    [Fact]
    public void NeedsFolderDedupe_NES_ReturnsFalse()
    {
        Assert.False(FolderDeduplicator.NeedsFolderDedupe("NES"));
        Assert.False(FolderDeduplicator.NeedsFolderDedupe("SNES"));
    }

    [Fact]
    public void NeedsPs3Dedupe_PS3_ReturnsTrue()
    {
        Assert.True(FolderDeduplicator.NeedsPs3Dedupe("PS3"));
    }

    [Fact]
    public void NeedsPs3Dedupe_Other_ReturnsFalse()
    {
        Assert.False(FolderDeduplicator.NeedsPs3Dedupe("PS2"));
    }

    // =========================================================================
    //  DeduplicateByBaseName Tests (with TempDir)
    // =========================================================================

    [Fact]
    public void DeduplicateByBaseName_DryRun_ReportsActions()
    {
        using var tempDir = new TempDir();
        var root = tempDir.Path;

        // Create two "duplicate" folders
        var folder1 = Path.Combine(root, "Super Mario World (Europe)");
        var folder2 = Path.Combine(root, "Super Mario World (USA)");
        Directory.CreateDirectory(folder1);
        Directory.CreateDirectory(folder2);
        File.WriteAllText(Path.Combine(folder1, "rom.sfc"), "data1");
        File.WriteAllText(Path.Combine(folder2, "rom.sfc"), "data2");

        var fs = new FakeFileSystem(root);
        var deduplicator = new FolderDeduplicator(fs);

        var result = deduplicator.DeduplicateByBaseName([root], mode: "DryRun");

        Assert.Equal(2, result.TotalFolders);
        Assert.Equal(1, result.DupeGroups);
        Assert.Single(result.Actions);
        Assert.Equal("DRYRUN-MOVE", result.Actions[0].Action);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void DeduplicateByBaseName_Move_MovesLoserFolder()
    {
        using var tempDir = new TempDir();
        var root = tempDir.Path;

        var folder1 = Path.Combine(root, "Zelda (Europe)");
        var folder2 = Path.Combine(root, "Zelda (USA)");
        Directory.CreateDirectory(folder1);
        Directory.CreateDirectory(folder2);
        // folder1 gets newer file → should win
        File.WriteAllText(Path.Combine(folder1, "rom.sfc"), "newer data");
        File.SetLastWriteTimeUtc(Path.Combine(folder1, "rom.sfc"), DateTime.UtcNow);
        File.WriteAllText(Path.Combine(folder2, "rom.sfc"), "older data");
        File.SetLastWriteTimeUtc(Path.Combine(folder2, "rom.sfc"), DateTime.UtcNow.AddDays(-10));

        var fs = new FakeFileSystem(root);
        var deduplicator = new FolderDeduplicator(fs);

        var result = deduplicator.DeduplicateByBaseName([root], mode: "Move");

        Assert.Equal(1, result.DupeGroups);
        Assert.Equal(1, result.Moved);
        // Verify that the correct winner/loser was selected
        Assert.Single(result.Actions);
        var action = result.Actions[0];
        // folder1 has newer file → it should be the winner
        Assert.Contains("Zelda (Europe)", action.Winner);
        // folder2 has older file → it should be the loser (moved)
        Assert.Contains("Zelda (USA)", action.Source);
        Assert.Equal("MOVED", action.Action);
    }

    [Fact]
    public void DeduplicateByBaseName_EmptyRoot_NoErrors()
    {
        using var tempDir = new TempDir();
        var fs = new FakeFileSystem(tempDir.Path);
        var deduplicator = new FolderDeduplicator(fs);

        var result = deduplicator.DeduplicateByBaseName([tempDir.Path]);
        Assert.Equal(0, result.DupeGroups);
        Assert.Equal(0, result.Errors);
    }

    [Fact]
    public void DeduplicateByBaseName_SingleFolder_NoDupes()
    {
        using var tempDir = new TempDir();
        var folder = Path.Combine(tempDir.Path, "UniqueGame");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "rom.bin"), "data");

        var fs = new FakeFileSystem(tempDir.Path);
        var deduplicator = new FolderDeduplicator(fs);

        var result = deduplicator.DeduplicateByBaseName([tempDir.Path]);
        Assert.Equal(0, result.DupeGroups);
    }

    [Fact]
    public void DeduplicateByBaseName_DiscVariants_NotGrouped()
    {
        using var tempDir = new TempDir();
        var folder1 = Path.Combine(tempDir.Path, "FF7 (Disc 1)");
        var folder2 = Path.Combine(tempDir.Path, "FF7 (Disc 2)");
        Directory.CreateDirectory(folder1);
        Directory.CreateDirectory(folder2);
        File.WriteAllText(Path.Combine(folder1, "rom.bin"), "d1");
        File.WriteAllText(Path.Combine(folder2, "rom.bin"), "d2");

        var fs = new FakeFileSystem(tempDir.Path);
        var deduplicator = new FolderDeduplicator(fs);

        var result = deduplicator.DeduplicateByBaseName([tempDir.Path]);
        Assert.Equal(0, result.DupeGroups); // Disc 1 and Disc 2 should NOT be grouped
    }

    [Fact]
    public void DeduplicateByBaseName_PopulatedBeatsEmpty()
    {
        using var tempDir = new TempDir();
        var populated = Path.Combine(tempDir.Path, "Game (Ver A)");
        var empty = Path.Combine(tempDir.Path, "Game (Ver B)");
        Directory.CreateDirectory(populated);
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(populated, "rom.bin"), "data");
        // empty folder has no files

        var fs = new FakeFileSystem(tempDir.Path);
        var deduplicator = new FolderDeduplicator(fs);

        var result = deduplicator.DeduplicateByBaseName([tempDir.Path], mode: "Move");

        Assert.Equal(1, result.DupeGroups);
        Assert.Equal(1, result.Moved);
        // The populated folder should be the winner, empty one should be moved
        var action = result.Actions[0];
        Assert.Contains("Ver B", action.Source); // Ver B (empty) is the loser
        Assert.Contains("Ver A", action.Winner); // Ver A (populated) is winner
    }

    // =========================================================================
    //  AutoDeduplicate Tests
    // =========================================================================

    [Fact]
    public void AutoDeduplicate_NoFolderDedupeConsole_NoAction()
    {
        using var tempDir = new TempDir();
        var fs = new FakeFileSystem(tempDir.Path);
        var deduplicator = new FolderDeduplicator(fs);

        var result = deduplicator.AutoDeduplicate([tempDir.Path],
            consoleKeyDetector: _ => "NES");

        Assert.Empty(result.Ps3Roots);
        Assert.Empty(result.FolderRoots);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void AutoDeduplicate_DosRoot_DispatchesToBaseName()
    {
        using var tempDir = new TempDir();
        var root = tempDir.Path;
        var f1 = Path.Combine(root, "Game (v1)");
        var f2 = Path.Combine(root, "Game (v2)");
        Directory.CreateDirectory(f1);
        Directory.CreateDirectory(f2);
        File.WriteAllText(Path.Combine(f1, "game.exe"), "x");
        File.WriteAllText(Path.Combine(f2, "game.exe"), "y");

        var fs = new FakeFileSystem(root);
        var deduplicator = new FolderDeduplicator(fs);

        var result = deduplicator.AutoDeduplicate([root], mode: "DryRun",
            consoleKeyDetector: _ => "DOS");

        Assert.Single(result.FolderRoots);
        Assert.Empty(result.Ps3Roots);
        Assert.Single(result.Results);
        Assert.Equal("FolderBaseName", result.Results[0].Type);
    }

    [Fact]
    public void AutoDeduplicate_Ps3Root_DryRun_Skipped()
    {
        using var tempDir = new TempDir();
        var fs = new FakeFileSystem(tempDir.Path);
        var deduplicator = new FolderDeduplicator(fs);

        var result = deduplicator.AutoDeduplicate([tempDir.Path], mode: "DryRun",
            consoleKeyDetector: _ => "PS3");

        Assert.Single(result.Ps3Roots);
        // PS3 dedupe is skipped in DryRun mode
        Assert.Empty(result.Results);
    }

    // =========================================================================
    //  Helpers
    // =========================================================================

    /// <summary>Simple fake file system for folder dedupe tests.</summary>
    private sealed class FakeFileSystem : Romulus.Contracts.Ports.IFileSystem
    {
        private readonly string _root;
        public FakeFileSystem(string root) => _root = root;
        public bool TestPath(string literalPath, string pathType = "Any") => pathType == "Container" ? Directory.Exists(literalPath) : File.Exists(literalPath) || Directory.Exists(literalPath);
        public string EnsureDirectory(string path) { Directory.CreateDirectory(path); return path; }
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null) =>
            Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(f => extensions is null || extensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    .ToList()
                : [];
        public string? MoveItemSafely(string src, string dest)
        {
            if (Directory.Exists(src)) { Directory.Move(src, dest); return dest; }
            if (File.Exists(src)) { File.Move(src, dest); return dest; }
            return null;
        }
        public bool MoveDirectorySafely(string src, string dest)
        {
            if (Directory.Exists(src)) { Directory.Move(src, dest); return true; }
            return false;
        }
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
        {
            var full = Path.GetFullPath(Path.Combine(rootPath, relativePath));
            return full.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FolderDedupeTest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, true); } catch { }
        }
    }
}
