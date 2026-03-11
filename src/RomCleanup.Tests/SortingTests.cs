using System.IO.Compression;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Classification;
using RomCleanup.Infrastructure.Sorting;
using Xunit;

namespace RomCleanup.Tests;

public class ConsoleSorterTests : IDisposable
{
    private readonly string _tempDir;

    public ConsoleSorterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ConsoleSorterTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateFile(string relativePath, string content = "")
    {
        var full = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private ConsoleDetector BuildDetector()
    {
        var consoles = new List<ConsoleInfo>
        {
            new("NES", "Nintendo", false, new[] { ".nes" }, Array.Empty<string>(), new[] { "NES", "Nintendo Entertainment System" }),
            new("SNES", "Super Nintendo", false, new[] { ".sfc", ".smc" }, Array.Empty<string>(), new[] { "SNES", "Super Nintendo" }),
            new("GBA", "Game Boy Advance", false, new[] { ".gba" }, Array.Empty<string>(), new[] { "GBA", "Game Boy Advance" }),
        };
        return new ConsoleDetector(consoles);
    }

    [Fact]
    public void Sort_DryRun_DoesNotMoveFiles()
    {
        var nesFile = CreateFile("Game.nes", "nes content");
        var detector = BuildDetector();
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(new[] { _tempDir }, new[] { ".nes" }, dryRun: true);

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(nesFile), "File should not be moved in DryRun");
    }

    [Fact]
    public void Sort_Move_MovesToConsoleSubdir()
    {
        CreateFile("Game.nes", "nes content");
        var detector = BuildDetector();
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(new[] { _tempDir }, new[] { ".nes" }, dryRun: false);

        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(Path.Combine(_tempDir, "NES", "Game.nes")));
    }

    [Fact]
    public void Sort_AlreadyInCorrectFolder_Skipped()
    {
        CreateFile("NES" + Path.DirectorySeparatorChar + "Game.nes", "nes content");
        var detector = BuildDetector();
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(new[] { _tempDir }, new[] { ".nes" }, dryRun: false);

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void Sort_UnknownExtension_CountedAsUnknown()
    {
        CreateFile("Game.xyz", "unknown content");
        var detector = BuildDetector();
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(new[] { _tempDir }, new[] { ".xyz" }, dryRun: true);

        Assert.Equal(1, result.Unknown);
        Assert.True(result.UnknownReasons.ContainsKey("no-match"));
    }

    [Fact]
    public void Sort_ExcludedFolders_Skipped()
    {
        CreateFile("_TRASH_REGION_DEDUPE" + Path.DirectorySeparatorChar + "Game.nes", "trash");
        CreateFile("_BIOS" + Path.DirectorySeparatorChar + "bios.nes", "bios");
        CreateFile("_JUNK" + Path.DirectorySeparatorChar + "junk.nes", "junk");
        var detector = BuildDetector();
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(new[] { _tempDir }, new[] { ".nes" }, dryRun: true);

        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void Sort_Cancellation_StopsEarly()
    {
        for (int i = 0; i < 20; i++)
            CreateFile($"Game{i}.nes", "data");

        var detector = BuildDetector();
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = sorter.Sort(new[] { _tempDir }, new[] { ".nes" }, dryRun: true, cts.Token);

        Assert.True(result.Total < 20, "Should stop before processing all files");
    }

    [Fact]
    public void Sort_MultipleRoots()
    {
        var root1 = Path.Combine(_tempDir, "root1");
        var root2 = Path.Combine(_tempDir, "root2");
        Directory.CreateDirectory(root1);
        Directory.CreateDirectory(root2);
        File.WriteAllText(Path.Combine(root1, "Game1.nes"), "nes1");
        File.WriteAllText(Path.Combine(root2, "Game2.sfc"), "snes1");

        var detector = BuildDetector();
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(new[] { root1, root2 }, new[] { ".nes", ".sfc" }, dryRun: false);

        Assert.Equal(2, result.Moved);
        Assert.True(File.Exists(Path.Combine(root1, "NES", "Game1.nes")));
        Assert.True(File.Exists(Path.Combine(root2, "SNES", "Game2.sfc")));
    }
}

public class ZipSorterTests : IDisposable
{
    private readonly string _tempDir;

    public ZipSorterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ZipSorterTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateZipWithEntries(string name, params string[] entryNames)
    {
        var zipPath = Path.Combine(_tempDir, name);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var entry in entryNames)
            {
                var e = archive.CreateEntry(entry);
                using var s = e.Open();
                s.WriteByte(0x00); // minimal content
            }
        }
        return zipPath;
    }

    // ── GetZipEntryExtensions ──

    [Fact]
    public void GetZipEntryExtensions_ReturnsDistinctExtensions()
    {
        var zip = CreateZipWithEntries("test.zip", "game.bin", "game.cue", "track02.bin");
        var exts = ZipSorter.GetZipEntryExtensions(zip);

        Assert.Contains(".bin", exts);
        Assert.Contains(".cue", exts);
        Assert.Equal(2, exts.Length); // .bin counted once
    }

    [Fact]
    public void GetZipEntryExtensions_MissingFile_ReturnsEmpty()
    {
        var exts = ZipSorter.GetZipEntryExtensions(Path.Combine(_tempDir, "nope.zip"));
        Assert.Empty(exts);
    }

    [Fact]
    public void GetZipEntryExtensions_Null_ReturnsEmpty()
    {
        Assert.Empty(ZipSorter.GetZipEntryExtensions(null!));
    }

    [Fact]
    public void GetZipEntryExtensions_CorruptFile_ReturnsEmpty()
    {
        var path = Path.Combine(_tempDir, "corrupt.zip");
        File.WriteAllBytes(path, new byte[] { 0xFF, 0xFE, 0x00, 0x00 });
        Assert.Empty(ZipSorter.GetZipEntryExtensions(path));
    }

    // ── SortPS1PS2 ──

    [Fact]
    public void SortPS1PS2_DryRun_DoesNotMove()
    {
        var zip = CreateZipWithEntries("game.zip", "game.ccd", "game.sub", "game.img");
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: true);

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(zip), "File should not be moved in DryRun");
    }

    [Fact]
    public void SortPS1PS2_PS1Exts_MovesToPS1()
    {
        CreateZipWithEntries("ps1game.zip", "game.ccd", "game.sub", "game.img");
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: false);

        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(Path.Combine(_tempDir, "PS1", "ps1game.zip")));
    }

    [Fact]
    public void SortPS1PS2_PS2Exts_MovesToPS2()
    {
        CreateZipWithEntries("ps2game.zip", "game.mdf", "game.mds");
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: false);

        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(Path.Combine(_tempDir, "PS2", "ps2game.zip")));
    }

    [Fact]
    public void SortPS1PS2_Ambiguous_BothPS1AndPS2_Skipped()
    {
        CreateZipWithEntries("ambiguous.zip", "game.ccd", "game.sub", "game.mdf");
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: false);

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void SortPS1PS2_NoMatchingExts_Skipped()
    {
        CreateZipWithEntries("generic.zip", "game.iso", "readme.txt");
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: false);

        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void SortPS1PS2_AlreadyInCorrectFolder_Skipped()
    {
        var ps1Dir = Path.Combine(_tempDir, "PS1");
        Directory.CreateDirectory(ps1Dir);
        var zipPath = Path.Combine(ps1Dir, "game.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("game.ccd"); using var s = e.Open(); s.WriteByte(0);
        }
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: false);

        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void SortPS1PS2_Cancellation_StopsEarly()
    {
        for (int i = 0; i < 10; i++)
            CreateZipWithEntries($"game{i}.zip", "game.ccd");

        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: true, cts.Token);

        Assert.True(result.Total < 10);
    }

    [Fact]
    public void SortPS1PS2_EmptyZip_Skipped()
    {
        var zipPath = Path.Combine(_tempDir, "empty.zip");
        using (var _ = ZipFile.Open(zipPath, ZipArchiveMode.Create)) { }
        var fs = new RomCleanup.Infrastructure.FileSystem.FileSystemAdapter();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: false);

        Assert.Equal(1, result.Skipped);
    }
}
