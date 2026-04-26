using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Sorting;
using Romulus.Tests.TestFixtures;
using Romulus.Tests.TestHelpers;
using Xunit;

namespace Romulus.Tests.Sorting;

/// <summary>
/// Multi-disc / set-integrity invariants for the sorting pipeline.
///
/// Invariants:
///  1.  All-or-nothing: when any set member fails to move, the primary file
///        (.cue / .m3u) and any earlier members must be rolled back to source.
///  2.  M3U rewrite preserves every input line including '#' comments and
///        blank lines; only renamed entries are substituted.
///  3.  CUE/BIN sets co-move together; rolling-back behavior matches invariant 1.
///  4.  Two sets with identical primary file name but different content
///        do not silently overwrite each other (DUP-suffix collision policy).
/// </summary>
public sealed class MultiDiscSetIntegrityTests : IDisposable
{
    private readonly string _tempDir;

    public MultiDiscSetIntegrityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_B4_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void ConsoleSorter_M3uMemberMoveFails_RollsBackPrimaryAndPriorMembersToSource()
    {
        var root = Path.Combine(_tempDir, "root");
        var input = Path.Combine(root, "Input");
        Directory.CreateDirectory(input);

        var m3u = Path.Combine(input, "Game.m3u");
        var disc1 = Path.Combine(input, "Game (Disc 1).cue");
        var disc2Failing = Path.Combine(input, "Game (Disc 2).cue");

        File.WriteAllText(m3u, "Game (Disc 1).cue\r\nGame (Disc 2).cue\r\n");
        File.WriteAllText(disc1, "FILE \"Game (Disc 1).bin\" BINARY");
        File.WriteAllText(disc2Failing, "FILE \"Game (Disc 2).bin\" BINARY");

        // Inject failure for the second member move only.
        var fs = new FailOnSpecificMoveFileSystem(new FileSystemAdapter(), failOnSourceContaining: "Disc 2");

        var sorter = new ConsoleSorter(fs, LoadConsoleDetector());
        var result = sorter.SortWithAutoSortDecisions(
            [root],
            [".m3u", ".cue"],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [m3u] = "PS1",
                [disc1] = "PS1",
                [disc2Failing] = "PS1"
            },
            candidatePaths: [m3u, disc1, disc2Failing]);

        // Primary + first member must be rolled back to source location.
        Assert.True(File.Exists(m3u), "Primary m3u must be rolled back to source.");
        Assert.True(File.Exists(disc1), "First member must be rolled back to source.");
        Assert.True(File.Exists(disc2Failing), "Failing member must remain at source.");

        // Destination dir must not contain the partially-moved set.
        var destDir = Path.Combine(root, "PS1");
        if (Directory.Exists(destDir))
        {
            Assert.False(File.Exists(Path.Combine(destDir, "Game.m3u")));
            Assert.False(File.Exists(Path.Combine(destDir, "Game (Disc 1).cue")));
        }

        Assert.True(result.Failed >= 1, "Sorter must report at least one failure for the partial set.");
    }

    [Fact]
    public void ConsoleSorter_M3uRewriteAfterMove_PreservesCommentsBlankLinesAndEntries()
    {
        var root = Path.Combine(_tempDir, "root");
        var input = Path.Combine(root, "Input");
        Directory.CreateDirectory(input);

        var m3u = Path.Combine(input, "Game.m3u");
        var disc1 = Path.Combine(input, "disc1.cue");
        var disc2 = Path.Combine(input, "disc2.cue");

        // Mix of comments, blank lines, and entries.
        var inputLines = new[]
        {
            "# Master playlist",
            "",
            "disc1.cue",
            "# disc 2 follows",
            "",
            "disc2.cue"
        };
        File.WriteAllLines(m3u, inputLines);
        File.WriteAllText(disc1, "FILE \"disc1.bin\" BINARY");
        File.WriteAllText(disc2, "FILE \"disc2.bin\" BINARY");

        var sorter = new ConsoleSorter(new FileSystemAdapter(), LoadConsoleDetector());
        var result = sorter.SortWithAutoSortDecisions(
            [root],
            [".m3u", ".cue"],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [m3u] = "PS1",
                [disc1] = "PS1",
                [disc2] = "PS1"
            },
            candidatePaths: [m3u, disc1, disc2]);

        Assert.Equal(0, result.Failed);
        var movedM3u = Path.Combine(root, "PS1", "Game.m3u");
        Assert.True(File.Exists(movedM3u));

        var actual = File.ReadAllLines(movedM3u);
        // Every input line must still be present (count parity) - comments and blanks preserved.
        Assert.Equal(inputLines.Length, actual.Length);
        Assert.Equal("# Master playlist", actual[0]);
        Assert.Equal("", actual[1]);
        Assert.Equal("# disc 2 follows", actual[3]);
        Assert.Equal("", actual[4]);
        // Entry lines remain (no rename happened in this scenario).
        Assert.Equal("disc1.cue", actual[2]);
        Assert.Equal("disc2.cue", actual[5]);
    }

    [Fact]
    public void ConsoleSorter_CueBinSet_CoMovesMembersAtomically()
    {
        var root = Path.Combine(_tempDir, "root");
        var input = Path.Combine(root, "Input");
        Directory.CreateDirectory(input);

        var cue = Path.Combine(input, "Game.cue");
        var bin = Path.Combine(input, "Game.bin");
        File.WriteAllText(cue, "FILE \"Game.bin\" BINARY\r\n  TRACK 01 MODE2/2352");
        File.WriteAllBytes(bin, [0x42, 0x43, 0x44]);

        var sorter = new ConsoleSorter(new FileSystemAdapter(), LoadConsoleDetector());
        var result = sorter.SortWithAutoSortDecisions(
            [root],
            [".cue", ".bin"],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [cue] = "PS1",
                [bin] = "PS1"
            },
            candidatePaths: [cue, bin]);

        Assert.Equal(0, result.Failed);
        var movedCue = Path.Combine(root, "PS1", "Game.cue");
        var movedBin = Path.Combine(root, "PS1", "Game.bin");
        Assert.True(File.Exists(movedCue), "CUE primary must move.");
        Assert.True(File.Exists(movedBin), "BIN member must move with CUE.");
        Assert.False(File.Exists(cue));
        Assert.False(File.Exists(bin));
    }

    [Fact]
    public void ConsoleSorter_SameNamedSetsWithDifferentContent_PreservesBothViaDupSuffix()
    {
        var root = Path.Combine(_tempDir, "root");
        var inputA = Path.Combine(root, "RegionA");
        var inputB = Path.Combine(root, "RegionB");
        Directory.CreateDirectory(inputA);
        Directory.CreateDirectory(inputB);

        var cueA = Path.Combine(inputA, "Game.cue");
        var binA = Path.Combine(inputA, "Game.bin");
        var cueB = Path.Combine(inputB, "Game.cue");
        var binB = Path.Combine(inputB, "Game.bin");

        File.WriteAllText(cueA, "FILE \"Game.bin\" BINARY");
        File.WriteAllBytes(binA, [0xAA]);
        File.WriteAllText(cueB, "FILE \"Game.bin\" BINARY");
        File.WriteAllBytes(binB, [0xBB]);

        var sorter = new ConsoleSorter(new FileSystemAdapter(), LoadConsoleDetector());
        var result = sorter.SortWithAutoSortDecisions(
            [root],
            [".cue", ".bin"],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [cueA] = "PS1",
                [binA] = "PS1",
                [cueB] = "PS1",
                [binB] = "PS1"
            },
            candidatePaths: [cueA, binA, cueB, binB]);

        var dest = Path.Combine(root, "PS1");
        Assert.Equal(0, result.Failed);

        // First arrival keeps the canonical name; second arrival becomes Game__DUP1.
        var presentCues = Directory.GetFiles(dest, "Game*.cue", SearchOption.TopDirectoryOnly);
        var presentBins = Directory.GetFiles(dest, "Game*.bin", SearchOption.TopDirectoryOnly);
        Assert.Equal(2, presentCues.Length);
        Assert.Equal(2, presentBins.Length);

        // Distinct content of both bins must be preserved (no silent overwrite).
        var binBytes = presentBins.Select(File.ReadAllBytes).ToList();
        Assert.Contains(binBytes, b => b.Length == 1 && b[0] == 0xAA);
        Assert.Contains(binBytes, b => b.Length == 1 && b[0] == 0xBB);
    }

    // ─── helpers ────────────────────────────────────────────────────────

    private static ConsoleDetector LoadConsoleDetector()
    {
        var consolesPath = RepoPaths.RepoFile("data", "consoles.json");
        return ConsoleDetector.LoadFromJson(File.ReadAllText(consolesPath));
    }

    /// <summary>
    /// Wrapping IFileSystem that fails MoveItemSafely whenever the source path
    /// contains the configured substring. Used to drive the all-or-nothing
    /// rollback path of <see cref="ConsoleSorter.MoveSetAtomically"/>.
    /// </summary>
    private sealed class FailOnSpecificMoveFileSystem(IFileSystem inner, string failOnSourceContaining) : IFileSystem
    {
        public bool TestPath(string p, string t = "Any") => inner.TestPath(p, t);
        public string EnsureDirectory(string p) => inner.EnsureDirectory(p);
        public IReadOnlyList<string> GetFilesSafe(string r, IEnumerable<string>? e = null) => inner.GetFilesSafe(r, e);
        public IReadOnlyList<string> GetFilesSafe(string r, IEnumerable<string>? e, CancellationToken ct) => inner.GetFilesSafe(r, e, ct);
        public IReadOnlyList<string> ConsumeScanWarnings() => inner.ConsumeScanWarnings();
        public bool FileExists(string p) => inner.FileExists(p);
        public bool DirectoryExists(string p) => inner.DirectoryExists(p);
        public IReadOnlyList<string> GetDirectoryFiles(string d, string s) => inner.GetDirectoryFiles(d, s);
        public long? GetAvailableFreeSpace(string p) => inner.GetAvailableFreeSpace(p);
        public string? RenameItemSafely(string s, string n) => inner.RenameItemSafely(s, n);
        public bool IsReparsePoint(string p) => inner.IsReparsePoint(p);
        public string? ResolveChildPathWithinRoot(string r, string rel) => inner.ResolveChildPathWithinRoot(r, rel);
        public string[] ReadAllLines(string p) => inner.ReadAllLines(p);
        public void DeleteFile(string p) => inner.DeleteFile(p);
        public void CopyFile(string s, string d, bool overwrite = false) => inner.CopyFile(s, d, overwrite);
        public void WriteAllText(string p, string c) => inner.WriteAllText(p, c);
        public bool MoveDirectorySafely(string s, string d) => inner.MoveDirectorySafely(s, d);

        public string? MoveItemSafely(string source, string dest)
            => source.Contains(failOnSourceContaining, StringComparison.OrdinalIgnoreCase)
                ? null
                : inner.MoveItemSafely(source, dest);

        public string? MoveItemSafely(string source, string dest, bool overwrite)
            => source.Contains(failOnSourceContaining, StringComparison.OrdinalIgnoreCase)
                ? null
                : inner.MoveItemSafely(source, dest, overwrite);

        public string? MoveItemSafely(string source, string dest, string allowedRoot)
            => source.Contains(failOnSourceContaining, StringComparison.OrdinalIgnoreCase)
                ? null
                : inner.MoveItemSafely(source, dest, allowedRoot);
    }
}
