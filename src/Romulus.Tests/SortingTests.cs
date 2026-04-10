using System.IO.Compression;
using System.Text.RegularExpressions;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Sorting;
using Xunit;

namespace Romulus.Tests;

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
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
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

    private static Dictionary<string, string> EnrichedKeys(params (string Path, string ConsoleKey)[] pairs)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, consoleKey) in pairs)
            map[path] = consoleKey;
        return map;
    }

    [Fact]
    public void Sort_DryRun_DoesNotMoveFiles()
    {
        var nesFile = CreateFile("Game.nes", "nes content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".nes" },
            dryRun: true,
            enrichedConsoleKeys: EnrichedKeys((nesFile, "NES")));

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(nesFile), "File should not be moved in DryRun");
    }

    [Fact]
    public void Sort_Move_MovesToConsoleSubdir()
    {
        CreateFile("Game.nes", "nes content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var romPath = Path.Combine(_tempDir, "Game.nes");
        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".nes" },
            dryRun: false,
            enrichedConsoleKeys: EnrichedKeys((romPath, "NES")));

        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(Path.Combine(_tempDir, "NES", "Game.nes")));
    }

    [Fact]
    public void Sort_Move_OverwritePolicy_ReplacesExistingTarget()
    {
        var source = CreateFile("Collision.nes", "new-content");
        var existingTarget = CreateFile(Path.Combine("NES", "Collision.nes"), "old-content");

        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".nes" },
            dryRun: false,
            enrichedConsoleKeys: EnrichedKeys((source, "NES")),
            candidatePaths: new[] { source },
            conflictPolicy: "Overwrite");

        Assert.Equal(1, result.Moved);
        Assert.False(File.Exists(source));
        Assert.Equal("new-content", File.ReadAllText(existingTarget));
        Assert.False(File.Exists(Path.Combine(_tempDir, "NES", "Collision__DUP1.nes")));
    }

    [Fact]
    public void Sort_AlreadyInCorrectFolder_Skipped()
    {
        CreateFile("NES" + Path.DirectorySeparatorChar + "Game.nes", "nes content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var romPath = Path.Combine(_tempDir, "NES", "Game.nes");
        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".nes" },
            dryRun: false,
            enrichedConsoleKeys: EnrichedKeys((romPath, "NES")));

        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void Sort_UnknownExtension_CountedAsUnknown()
    {
        CreateFile("Game.xyz", "unknown content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var romPath = Path.Combine(_tempDir, "Game.xyz");
        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".xyz" },
            dryRun: true,
            enrichedConsoleKeys: EnrichedKeys((romPath, "UNKNOWN")));

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
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(
            new[] { root1, root2 },
            new[] { ".nes", ".sfc" },
            dryRun: false,
            enrichedConsoleKeys: EnrichedKeys(
                (Path.Combine(root1, "Game1.nes"), "NES"),
                (Path.Combine(root2, "Game2.sfc"), "SNES")));

        Assert.Equal(2, result.Moved);
        Assert.True(File.Exists(Path.Combine(root1, "NES", "Game1.nes")));
        Assert.True(File.Exists(Path.Combine(root2, "SNES", "Game2.sfc")));
    }

    [Fact]
    public void Sort_MissingEnrichedConsoleKeys_SkipsAllFiles()
    {
        CreateFile("Game.nes", "nes content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(new[] { _tempDir }, new[] { ".nes" }, dryRun: true);

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Skipped);
        Assert.True(result.UnknownReasons.ContainsKey("missing-enriched-console-keys"));
    }

    [Fact]
    public void Sort_MissingEnrichedConsoleKeys_WritesAuditWarning()
    {
        CreateFile("Game.nes", "nes content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new RecordingAuditStore();
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        var sorter = new ConsoleSorter(fs, detector, audit, auditPath);

        var result = sorter.Sort(new[] { _tempDir }, new[] { ".nes" }, dryRun: true);

        Assert.Equal(1, result.Skipped);
        Assert.Contains(audit.Rows, r => r.action == "CONSOLE_SORT" && r.reason == "missing-enriched-console-keys");
    }

    private sealed class RecordingAuditStore : IAuditStore
    {
        public List<(string action, string reason)> Rows { get; } = new();

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => true;
        public void Flush(string auditCsvPath) { }
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "")
            => Rows.Add((action, reason));
    }

    private static Dictionary<string, string> SortDecisions(params (string Path, string Decision)[] pairs)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, decision) in pairs)
            map[path] = decision;
        return map;
    }

    private static Dictionary<string, string> Categories(params (string Path, string Category)[] pairs)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, cat) in pairs)
            map[path] = cat;
        return map;
    }

    // ── SortDecision Routing Tests ──

    [Fact]
    public void Sort_ReviewDecision_MovesToReviewSubdir()
    {
        var romPath = CreateFile("Game.nes", "nes content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".nes" },
            dryRun: false,
            enrichedConsoleKeys: EnrichedKeys((romPath, "NES")),
            enrichedSortDecisions: SortDecisions((romPath, "Review")));

        Assert.Equal(1, result.Reviewed);
        Assert.Equal(0, result.Moved);
        Assert.True(File.Exists(Path.Combine(_tempDir, "_REVIEW", "NES", "Game.nes")));
    }

    [Fact]
    public void Sort_ReviewDecision_MovesCueSetAtomically()
    {
        var cuePath = CreateFile("ReviewSet.cue", "FILE \"ReviewSet.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00");
        var binPath = CreateFile("ReviewSet.bin", "binary data");
        var sorter = new ConsoleSorter(new Romulus.Infrastructure.FileSystem.FileSystemAdapter(), BuildDetector());

        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".cue" },
            dryRun: false,
            enrichedConsoleKeys: EnrichedKeys((cuePath, "NES")),
            enrichedSortDecisions: SortDecisions((cuePath, "Review")),
            candidatePaths: new[] { cuePath, binPath });

        Assert.Equal(1, result.Reviewed);
        Assert.True(result.SetMembersMoved >= 1);
        Assert.True(File.Exists(Path.Combine(_tempDir, "_REVIEW", "NES", "ReviewSet.cue")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "_REVIEW", "NES", "ReviewSet.bin")));
        Assert.Contains(result.PathMutations ?? [], mutation => mutation.SourcePath == cuePath);
        Assert.Contains(result.PathMutations ?? [], mutation => mutation.SourcePath == binPath);
    }

    [Fact]
    public void Sort_BlockedGame_NotMoved()
    {
        var romPath = CreateFile("Game.nes", "nes content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".nes" },
            dryRun: false,
            enrichedConsoleKeys: EnrichedKeys((romPath, "NES")),
            enrichedSortDecisions: SortDecisions((romPath, "Blocked")),
            enrichedCategories: Categories((romPath, "Game")));

        Assert.Equal(1, result.Blocked);
        Assert.Equal(0, result.Moved);
        // Blocked non-junk game is moved to _BLOCKED/{reason}/ staging folder
        Assert.True(File.Exists(Path.Combine(_tempDir, "_BLOCKED", "blocked", "Game.nes")),
            "Blocked game should be staged in _BLOCKED/ folder");
    }

    [Fact]
    public void Sort_BlockedJunk_MovesToTrashJunkConsoleSubdir()
    {
        var romPath = CreateFile("Junk.nes", "junk content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".nes" },
            dryRun: false,
            enrichedConsoleKeys: EnrichedKeys((romPath, "NES")),
            enrichedSortDecisions: SortDecisions((romPath, "Blocked")),
            enrichedCategories: Categories((romPath, "Junk")));

        Assert.Equal(1, result.Blocked);
        Assert.True(File.Exists(Path.Combine(_tempDir, "_TRASH_JUNK", "NES", "Junk.nes")));
    }

    [Fact]
    public void Sort_BlockedJunk_MovesCueSetAtomicallyToTrashJunk()
    {
        var cuePath = CreateFile("TrashSet.cue", "FILE \"TrashSet.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00");
        var binPath = CreateFile("TrashSet.bin", "binary data");
        var sorter = new ConsoleSorter(new Romulus.Infrastructure.FileSystem.FileSystemAdapter(), BuildDetector());

        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".cue" },
            dryRun: false,
            enrichedConsoleKeys: EnrichedKeys((cuePath, "NES")),
            enrichedSortDecisions: SortDecisions((cuePath, "Blocked")),
            enrichedCategories: Categories((cuePath, "Junk")),
            candidatePaths: new[] { cuePath, binPath });

        Assert.Equal(1, result.Blocked);
        Assert.True(result.SetMembersMoved >= 1);
        Assert.True(File.Exists(Path.Combine(_tempDir, "_TRASH_JUNK", "NES", "TrashSet.cue")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "_TRASH_JUNK", "NES", "TrashSet.bin")));
        Assert.Contains(result.PathMutations ?? [], mutation => mutation.SourcePath == cuePath);
        Assert.Contains(result.PathMutations ?? [], mutation => mutation.SourcePath == binPath);
    }

    [Fact]
    public void Sort_DatVerified_MovesToConsoleSubdir()
    {
        var romPath = CreateFile("Verified.nes", "verified content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".nes" },
            dryRun: false,
            enrichedConsoleKeys: EnrichedKeys((romPath, "NES")),
            enrichedSortDecisions: SortDecisions((romPath, "DatVerified")));

        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(Path.Combine(_tempDir, "NES", "Verified.nes")));
    }

    [Fact]
    public void Sort_NoSortDecision_DefaultsToStandardMove()
    {
        var romPath = CreateFile("Default.nes", "content");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".nes" },
            dryRun: false,
            enrichedConsoleKeys: EnrichedKeys((romPath, "NES")));

        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(Path.Combine(_tempDir, "NES", "Default.nes")));
    }

    [Fact]
    public void Sort_UsesProvidedCandidatePaths_InsteadOfRescanningRoot()
    {
        var planned = CreateFile("Planned.nes", "planned");
        CreateFile("Unplanned.nes", "unplanned");

        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".nes" },
            dryRun: true,
            enrichedConsoleKeys: EnrichedKeys((planned, "NES")),
            candidatePaths: new[] { planned });

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Moved);
        Assert.Equal(0, result.Unknown);
    }

    [Fact]
    public void Sort_SetRollback_UsesFileSystemLookupForDupDestination()
    {
        var cuePath = CreateFile("Rollback.cue", "FILE \"Rollback.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00");
        _ = CreateFile("Rollback.bin", "binary data");

        var fs = new RollbackAwareFileSystem();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".cue" },
            dryRun: false,
            enrichedConsoleKeys: EnrichedKeys((cuePath, "PS1")),
            candidatePaths: new[] { cuePath });

        Assert.Equal(2, result.Failed);
        Assert.Contains(fs.MoveLog, move =>
            move.src.EndsWith("Rollback__DUP10.cue", StringComparison.OrdinalIgnoreCase)
            && move.dst.EndsWith("Rollback.cue", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sort_CueSet_AtomicMoveWithMembers()
    {
        var cuePath = CreateFile("Game.cue", "FILE \"Game.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00");
        var binPath = CreateFile("Game.bin", "binary data for CUE");
        var detector = BuildDetector();
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, detector);

        var result = sorter.Sort(
            new[] { _tempDir },
            new[] { ".cue" },
            dryRun: false,
            enrichedConsoleKeys: EnrichedKeys((cuePath, "PS1")));

        Assert.Equal(1, result.Moved);
        Assert.True(result.SetMembersMoved >= 1, "BIN should move with CUE");
        Assert.True(File.Exists(Path.Combine(_tempDir, "PS1", "Game.cue")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "PS1", "Game.bin")));
    }

    private sealed class RollbackAwareFileSystem : IFileSystem
    {
        private readonly HashSet<string> _virtualFiles = new(StringComparer.OrdinalIgnoreCase);

        public List<(string src, string dst)> MoveLog { get; } = new();

        public bool TestPath(string literalPath, string pathType = "Any")
            => pathType != "Leaf" || _virtualFiles.Contains(literalPath) || File.Exists(literalPath);

        public string EnsureDirectory(string path) => path;

        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => Array.Empty<string>();

        public string? MoveItemSafely(string sourcePath, string destinationPath)
        {
            MoveLog.Add((sourcePath, destinationPath));

            if (sourcePath.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)
                && !sourcePath.Contains("__DUP", StringComparison.OrdinalIgnoreCase))
            {
                var actualDest = Path.Combine(
                    Path.GetDirectoryName(destinationPath) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(destinationPath) + "__DUP10" + Path.GetExtension(destinationPath));
                _virtualFiles.Add(actualDest);
                return actualDest;
            }

            if (sourcePath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                return null;

            if (_virtualFiles.Remove(sourcePath))
            {
                _virtualFiles.Add(destinationPath);
                return destinationPath;
            }

            return destinationPath;
        }

        public bool FileExists(string literalPath)
            => _virtualFiles.Contains(literalPath);

        public bool DirectoryExists(string literalPath)
            => true;

        public IReadOnlyList<string> GetDirectoryFiles(string directoryPath, string searchPattern)
            => _virtualFiles
                .Where(path => string.Equals(Path.GetDirectoryName(path), directoryPath, StringComparison.OrdinalIgnoreCase))
                .Where(path => MatchesPattern(Path.GetFileName(path), searchPattern))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);

        public bool IsReparsePoint(string path) => false;

        public void DeleteFile(string path)
        {
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
        }

        private static bool MatchesPattern(string fileName, string pattern)
        {
            if (pattern == "*")
                return true;

            var regex = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
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
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
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
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: true);

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(zip), "File should not be moved in DryRun");
    }

    [Fact]
    public void SortPS1PS2_PS1Exts_MovesToPS1()
    {
        CreateZipWithEntries("ps1game.zip", "game.ccd", "game.sub", "game.img");
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: false);

        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(Path.Combine(_tempDir, "PS1", "ps1game.zip")));
    }

    [Fact]
    public void SortPS1PS2_PS2Exts_MovesToPS2()
    {
        CreateZipWithEntries("ps2game.zip", "game.mdf", "game.mds");
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: false);

        Assert.Equal(1, result.Moved);
        Assert.True(File.Exists(Path.Combine(_tempDir, "PS2", "ps2game.zip")));
    }

    [Fact]
    public void SortPS1PS2_Ambiguous_BothPS1AndPS2_Skipped()
    {
        CreateZipWithEntries("ambiguous.zip", "game.ccd", "game.sub", "game.mdf");
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: false);

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void SortPS1PS2_NoMatchingExts_Skipped()
    {
        CreateZipWithEntries("generic.zip", "game.iso", "readme.txt");
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();

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
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: false);

        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void SortPS1PS2_Cancellation_StopsEarly()
    {
        for (int i = 0; i < 10; i++)
            CreateZipWithEntries($"game{i}.zip", "game.ccd");

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
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
        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();

        var result = ZipSorter.SortPS1PS2(new[] { _tempDir }, fs, dryRun: false);

        Assert.Equal(1, result.Skipped);
    }
}
