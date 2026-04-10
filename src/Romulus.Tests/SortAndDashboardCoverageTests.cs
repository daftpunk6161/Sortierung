using Romulus.Api;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Safety;
using Romulus.Infrastructure.Sorting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for ConsoleSorter.Sort() edge cases not yet tested 
/// and DashboardDataBuilder.BuildBootstrap.
/// </summary>
public sealed class SortAndDashboardCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public SortAndDashboardCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sort-dash-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateFile(string relativePath, string content = "x")
    {
        var full = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private static ConsoleDetector BuildDetector()
    {
        var consoles = new List<ConsoleInfo>
        {
            new("NES", "Nintendo", false, [".nes"], [], ["NES"]),
            new("SNES", "Super Nintendo", false, [".sfc", ".smc"], [], ["SNES"]),
            new("PS1", "PlayStation", true, [".cue", ".bin", ".img"], [], ["PS1", "PSX"]),
            new("GBA", "Game Boy Advance", false, [".gba"], [], ["GBA"]),
        };
        return new ConsoleDetector(consoles);
    }

    private static Dictionary<string, string> Keys(params (string Path, string Key)[] pairs)
        => new(pairs.Select(p => new KeyValuePair<string, string>(p.Path, p.Key)), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> Decisions(params (string Path, string Decision)[] pairs)
        => new(pairs.Select(p => new KeyValuePair<string, string>(p.Path, p.Decision)), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> Cats(params (string Path, string Cat)[] pairs)
        => new(pairs.Select(p => new KeyValuePair<string, string>(p.Path, p.Cat)), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> Reasons(params (string Path, string Reason)[] pairs)
        => new(pairs.Select(p => new KeyValuePair<string, string>(p.Path, p.Reason)), StringComparer.OrdinalIgnoreCase);

    // ═══════════════════════════════════════════
    //  ConsoleSorter – Unknown decision routing
    // ═══════════════════════════════════════════

    [Fact]
    public void Sort_UnknownDecision_MovesToUnknownFolder()
    {
        var romPath = CreateFile("Mystery.nes", "unknown");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(
            [_tempDir], [".nes"], dryRun: false,
            enrichedConsoleKeys: Keys((romPath, "NES")),
            enrichedSortDecisions: Decisions((romPath, "Unknown")));

        Assert.Equal(1, result.Unknown);
        Assert.Equal(0, result.Moved);
        Assert.True(File.Exists(Path.Combine(_tempDir, "_UNKNOWN", "Mystery.nes")));
    }

    [Fact]
    public void Sort_UnknownDecision_DryRun_DoesNotMove()
    {
        var romPath = CreateFile("Mystery.nes", "unknown");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(
            [_tempDir], [".nes"], dryRun: true,
            enrichedConsoleKeys: Keys((romPath, "NES")),
            enrichedSortDecisions: Decisions((romPath, "Unknown")));

        Assert.Equal(1, result.Unknown);
        Assert.True(File.Exists(romPath), "File should stay in place during DryRun");
    }

    // ═══════════════════════════════════════════
    //  ConsoleSorter – Enriched sort reasons
    // ═══════════════════════════════════════════

    [Fact]
    public void Sort_WithEnrichedReasons_MovesNormally()
    {
        var romPath = CreateFile("Game.nes", "data");
        var fs = new FileSystemAdapter();
        var audit = new RecordingAuditStore();
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        var sorter = new ConsoleSorter(fs, BuildDetector(), audit, auditPath);

        var result = sorter.Sort(
            [_tempDir], [".nes"], dryRun: false,
            enrichedConsoleKeys: Keys((romPath, "NES")),
            enrichedSortReasons: Reasons((romPath, "dat-hash-match")));

        Assert.Equal(1, result.Moved);
        // Normal sort path uses consoleKey as audit reason
        Assert.Contains(audit.Rows, r => r.action == "CONSOLE_SORT");
        Assert.True(File.Exists(Path.Combine(_tempDir, "NES", "Game.nes")));
    }

    // ═══════════════════════════════════════════
    //  ConsoleSorter – Blocked with reason segment
    // ═══════════════════════════════════════════

    [Fact]
    public void Sort_BlockedWithReason_UsesReasonAsSubfolder()
    {
        var romPath = CreateFile("GameBlocked.nes", "blocked");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(
            [_tempDir], [".nes"], dryRun: false,
            enrichedConsoleKeys: Keys((romPath, "NES")),
            enrichedSortDecisions: Decisions((romPath, "Blocked")),
            enrichedSortReasons: Reasons((romPath, "low-confidence")),
            enrichedCategories: Cats((romPath, "Game")));

        Assert.Equal(1, result.Blocked);
        Assert.True(File.Exists(Path.Combine(_tempDir, "_BLOCKED", "low-confidence", "GameBlocked.nes")));
    }

    // ═══════════════════════════════════════════
    //  ConsoleSorter – Set member detection (GDI)
    // ═══════════════════════════════════════════

    [Fact]
    public void Sort_GdiSet_MovesAllTracksAtomically()
    {
        var gdiContent = "3\r\n1 0 4 2352 track01.bin 0\r\n2 100 0 2352 track02.raw 0\r\n3 200 4 2352 track03.bin 0";
        var gdiPath = CreateFile("Game.gdi", gdiContent);
        CreateFile("track01.bin", "t1");
        CreateFile("track02.raw", "t2");
        CreateFile("track03.bin", "t3");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(
            [_tempDir], [".gdi"], dryRun: false,
            enrichedConsoleKeys: Keys((gdiPath, "SNES")),
            candidatePaths: [gdiPath,
                Path.Combine(_tempDir, "track01.bin"),
                Path.Combine(_tempDir, "track02.raw"),
                Path.Combine(_tempDir, "track03.bin")]);

        Assert.Equal(1, result.Moved);
        Assert.True(result.SetMembersMoved >= 3, $"Expected 3+ set members, got {result.SetMembersMoved}");
        Assert.True(File.Exists(Path.Combine(_tempDir, "SNES", "Game.gdi")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "SNES", "track01.bin")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "SNES", "track02.raw")));
    }

    // ═══════════════════════════════════════════
    //  ConsoleSorter – M3U set detection
    // ═══════════════════════════════════════════

    [Fact]
    public void Sort_M3uSet_MovesReferencedFilesAtomically()
    {
        var m3uContent = "disc1.cue\r\ndisc2.cue";
        var m3uPath = CreateFile("Game.m3u", m3uContent);
        CreateFile("disc1.cue", "FILE disc1.bin BINARY");
        CreateFile("disc2.cue", "FILE disc2.bin BINARY");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(
            [_tempDir], [".m3u"], dryRun: false,
            enrichedConsoleKeys: Keys((m3uPath, "PS1")),
            candidatePaths: [m3uPath,
                Path.Combine(_tempDir, "disc1.cue"),
                Path.Combine(_tempDir, "disc2.cue")]);

        Assert.Equal(1, result.Moved);
        Assert.True(result.SetMembersMoved >= 2);
        Assert.True(File.Exists(Path.Combine(_tempDir, "PS1", "Game.m3u")));
    }

    // ═══════════════════════════════════════════
    //  ConsoleSorter – CCD set detection
    // ═══════════════════════════════════════════

    [Fact]
    public void Sort_CcdSet_MovesImgAndSubAtomically()
    {
        var ccdPath = CreateFile("Game.ccd", "[CloneCD]\r\n");
        CreateFile("Game.img", "img data");
        CreateFile("Game.sub", "sub data");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(
            [_tempDir], [".ccd"], dryRun: false,
            enrichedConsoleKeys: Keys((ccdPath, "PS1")),
            candidatePaths: [ccdPath,
                Path.Combine(_tempDir, "Game.img"),
                Path.Combine(_tempDir, "Game.sub")]);

        Assert.Equal(1, result.Moved);
        Assert.True(result.SetMembersMoved >= 2);
        Assert.True(File.Exists(Path.Combine(_tempDir, "PS1", "Game.ccd")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "PS1", "Game.img")));
    }

    // ═══════════════════════════════════════════
    //  ConsoleSorter – MDS set detection
    // ═══════════════════════════════════════════

    [Fact]
    public void Sort_MdsSet_MovesMdfAtomically()
    {
        var mdsPath = CreateFile("Game.mds", "mds header");
        CreateFile("Game.mdf", "mdf data");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(
            [_tempDir], [".mds"], dryRun: false,
            enrichedConsoleKeys: Keys((mdsPath, "PS1")),
            candidatePaths: [mdsPath, Path.Combine(_tempDir, "Game.mdf")]);

        Assert.Equal(1, result.Moved);
        Assert.True(result.SetMembersMoved >= 1);
        Assert.True(File.Exists(Path.Combine(_tempDir, "PS1", "Game.mds")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "PS1", "Game.mdf")));
    }

    // ═══════════════════════════════════════════
    //  ConsoleSorter – PathMutations tracking
    // ═══════════════════════════════════════════

    [Fact]
    public void Sort_Move_TracksPathMutations()
    {
        var romPath = CreateFile("Tracked.nes", "data");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(
            [_tempDir], [".nes"], dryRun: false,
            enrichedConsoleKeys: Keys((romPath, "NES")));

        Assert.NotNull(result.PathMutations);
        Assert.Contains(result.PathMutations, m => m.SourcePath == romPath);
        var mutation = result.PathMutations.First(m => m.SourcePath == romPath);
        Assert.EndsWith("NES" + Path.DirectorySeparatorChar + "Tracked.nes", mutation.TargetPath);
    }

    // ═══════════════════════════════════════════
    //  ConsoleSorter – Null enrichedConsoleKeys
    // ═══════════════════════════════════════════

    [Fact]
    public void Sort_NullEnrichedKeys_SkipsWithWarning()
    {
        CreateFile("File.nes", "data");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort([_tempDir], [".nes"], dryRun: true, enrichedConsoleKeys: null);

        Assert.True(result.Skipped >= 1);
        Assert.True(result.UnknownReasons.ContainsKey("missing-enriched-console-keys"));
    }

    // ═══════════════════════════════════════════
    //  ConsoleSorter – UNKNOWN consoleKey routing
    // ═══════════════════════════════════════════

    [Fact]
    public void Sort_UnknownConsoleKey_CountsAsUnknownNoMatch()
    {
        var romPath = CreateFile("Unknown.nes", "data");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(
            [_tempDir], [".nes"], dryRun: false,
            enrichedConsoleKeys: Keys((romPath, "UNKNOWN")));

        Assert.Equal(1, result.Unknown);
        Assert.True(result.UnknownReasons.ContainsKey("no-match"));
        // UNKNOWN consoleKey without sort decision = skip, file stays
        Assert.True(File.Exists(romPath));
    }

    // ═══════════════════════════════════════════
    //  ConsoleSorter – Empty consoleKey
    // ═══════════════════════════════════════════

    [Fact]
    public void Sort_EmptyConsoleKey_TreatedAsUnknown()
    {
        var romPath = CreateFile("Empty.nes", "data");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(
            [_tempDir], [".nes"], dryRun: false,
            enrichedConsoleKeys: Keys((romPath, "")));

        Assert.True(result.Unknown >= 1 || result.Skipped >= 1);
    }

    // ═══════════════════════════════════════════
    //  ConsoleSorter – Multiple files same console
    // ═══════════════════════════════════════════

    [Fact]
    public void Sort_MultipleFiles_SameConsole_AllMoved()
    {
        var rom1 = CreateFile("Game1.nes", "g1");
        var rom2 = CreateFile("Game2.nes", "g2");
        var rom3 = CreateFile("Game3.nes", "g3");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort(
            [_tempDir], [".nes"], dryRun: false,
            enrichedConsoleKeys: Keys((rom1, "NES"), (rom2, "NES"), (rom3, "NES")));

        Assert.Equal(3, result.Moved);
        Assert.True(File.Exists(Path.Combine(_tempDir, "NES", "Game1.nes")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "NES", "Game2.nes")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "NES", "Game3.nes")));
    }

    // ═══════════════════════════════════════════
    //  ConsoleSorter – Files in excluded folders
    // ═══════════════════════════════════════════

    [Fact]
    public void Sort_FilesInReviewFolder_Skipped()
    {
        CreateFile("_REVIEW" + Path.DirectorySeparatorChar + "Game.nes", "review");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort([_tempDir], [".nes"], dryRun: true);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void Sort_FilesInBlockedFolder_Skipped()
    {
        CreateFile("_BLOCKED" + Path.DirectorySeparatorChar + "Game.nes", "blocked");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort([_tempDir], [".nes"], dryRun: true);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void Sort_FilesInUnknownFolder_Skipped()
    {
        CreateFile("_UNKNOWN" + Path.DirectorySeparatorChar + "Game.nes", "unknown");
        var fs = new FileSystemAdapter();
        var sorter = new ConsoleSorter(fs, BuildDetector());

        var result = sorter.Sort([_tempDir], [".nes"], dryRun: true);
        Assert.Equal(0, result.Total);
    }

    // ═══════════════════════════════════════════
    //  DashboardDataBuilder.BuildBootstrap
    // ═══════════════════════════════════════════

    [Fact]
    public void BuildBootstrap_MapsAllProperties()
    {
        var options = new HeadlessApiOptions
        {
            DashboardEnabled = true,
            AllowRemoteClients = false,
            PublicBaseUrl = "https://rom.local"
        };
        var policy = new AllowedRootPathPolicy([@"C:\Roms", @"D:\Games"]);

        var result = DashboardDataBuilder.BuildBootstrap(options, policy, "1.0.0-test");

        Assert.Equal("1.0.0-test", result.Version);
        Assert.True(result.DashboardEnabled);
        Assert.False(result.AllowRemoteClients);
        Assert.True(result.AllowedRootsEnforced);
        Assert.Equal(2, result.AllowedRoots.Length);
        Assert.Equal("https://rom.local", result.PublicBaseUrl);
    }

    [Fact]
    public void BuildBootstrap_EmptyPolicy_EnforcedFalse()
    {
        var options = new HeadlessApiOptions { DashboardEnabled = false };
        var policy = new AllowedRootPathPolicy(null);

        var result = DashboardDataBuilder.BuildBootstrap(options, policy, "2.0");

        Assert.False(result.DashboardEnabled);
        Assert.False(result.AllowedRootsEnforced);
        Assert.Empty(result.AllowedRoots);
    }

    [Fact]
    public void BuildBootstrap_NullOptions_Throws()
    {
        var policy = new AllowedRootPathPolicy(null);
        Assert.Throws<ArgumentNullException>(() =>
            DashboardDataBuilder.BuildBootstrap(null!, policy, "1.0"));
    }

    [Fact]
    public void BuildBootstrap_NullPolicy_Throws()
    {
        var options = new HeadlessApiOptions();
        Assert.Throws<ArgumentNullException>(() =>
            DashboardDataBuilder.BuildBootstrap(options, null!, "1.0"));
    }

    // ═══════════════════════════════════════════
    //  Recording Audit Store for sort tests
    // ═══════════════════════════════════════════

    private sealed class RecordingAuditStore : IAuditStore
    {
        public List<(string action, string reason)> Rows { get; } = [];

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => true;
        public void Flush(string auditCsvPath) { }
        public IReadOnlyList<string> Rollback(string p, string[] a, string[] b, bool d = false) => [];

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "")
            => Rows.Add((action, reason));

        public void AppendAuditRows(string auditCsvPath, IReadOnlyList<AuditAppendRow> rows) { }
    }
}
