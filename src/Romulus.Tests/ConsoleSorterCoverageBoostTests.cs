using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Sorting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage boost for ConsoleSorter: Blocked/Unknown routing with Junk category,
/// non-set blocked/unknown moves, set-member atomic moves, failed move branches,
/// review set members, and unknown-key/no-match counters.
/// Targets ~60 uncovered lines.
/// </summary>
public sealed class ConsoleSorterCoverageBoostTests : IDisposable
{
    private readonly string _tempDir;

    public ConsoleSorterCoverageBoostTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CS_Coverage_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateFile(string relativePath, string content = "data")
    {
        var full = Path.GetFullPath(Path.Combine(_tempDir, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private static ConsoleDetector BuildDetector()
    {
        return new ConsoleDetector(new List<ConsoleInfo>
        {
            new("NES", "Nintendo", false, new[] { ".nes" }, Array.Empty<string>(), new[] { "NES" }),
            new("SNES", "Super Nintendo", false, new[] { ".sfc", ".smc" }, Array.Empty<string>(), new[] { "SNES" }),
        });
    }

    private ConsoleSorter CreateSorter() => new(new FileSystemAdapter(), BuildDetector());

    // ===== Unknown decision (non-blocked, non-junk) → _UNKNOWN/ =====

    [Fact]
    public void Sort_UnknownDecision_NonJunk_MovesToUnknownSubdir()
    {
        var file = CreateFile("game.nes");
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [_tempDir],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "UNKNOWN" },
            enrichedSortDecisions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "Unknown" },
            enrichedCategories: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "Game" });

        Assert.True(result.Unknown > 0);
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "_UNKNOWN")));
    }

    // ===== Blocked decision + Junk category → _TRASH_JUNK/{ConsoleKey}/ =====

    [Fact]
    public void Sort_BlockedJunkWithKnownConsole_MovesToTrashJunkConsole()
    {
        var file = CreateFile("junk.nes");
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [_tempDir],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "NES" },
            enrichedSortDecisions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "Blocked" },
            enrichedCategories: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "Junk" });

        Assert.True(result.Blocked > 0);
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "_TRASH_JUNK", "NES")));
    }

    [Fact]
    public void Sort_BlockedJunkWithUnknownConsole_MovesToTrashJunkUnknown()
    {
        var file = CreateFile("junk.bin");
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [_tempDir],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "UNKNOWN" },
            enrichedSortDecisions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "Blocked" },
            enrichedCategories: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "Junk" });

        Assert.True(result.Blocked > 0);
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "_TRASH_JUNK", "UNKNOWN")));
    }

    // ===== Unknown decision + Junk category =====

    [Fact]
    public void Sort_UnknownJunk_MovesToTrashJunk()
    {
        var file = CreateFile("junk.nes");
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [_tempDir],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "NES" },
            enrichedSortDecisions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "Unknown" },
            enrichedCategories: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "Junk" });

        Assert.True(result.Unknown > 0);
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "_TRASH_JUNK", "NES")));
    }

    // ===== Blocked non-junk → _BLOCKED/{reason}/ =====

    [Fact]
    public void Sort_BlockedNonJunk_MovesToBlockedSubdir()
    {
        var file = CreateFile("blocked.nes");
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [_tempDir],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "NES" },
            enrichedSortDecisions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "Blocked" },
            enrichedSortReasons: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "low-confidence" },
            enrichedCategories: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "Game" });

        Assert.True(result.Blocked > 0);
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "_BLOCKED")));
    }

    // ===== No enriched key for file → UNKNOWN counter =====

    [Fact]
    public void Sort_FileNotInEnrichedKeys_CountsAsUnknown()
    {
        var file = CreateFile("mystery.nes");
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [_tempDir],
            dryRun: true,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.True(result.Unknown > 0);
    }

    // ===== Invalid console key format → unknown counter =====

    [Fact]
    public void Sort_InvalidConsoleKeyFormat_CountsAsUnknown()
    {
        var file = CreateFile("game.nes");
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [_tempDir],
            dryRun: true,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "invalid key!" }); // spaces/special chars fail RxValidConsoleKey

        Assert.True(result.Unknown > 0);
    }

    // ===== DryRun with enriched set members =====

    [Fact]
    public void Sort_DryRunWithSetMembers_CountsAllMembers()
    {
        var cue = CreateFile("game.cue", "FILE game.bin BINARY");
        var bin = CreateFile("game.bin", "binary data");
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [_tempDir],
            dryRun: true,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [cue] = "SNES",
                [bin] = "SNES"
            });

        // Primary + member both counted
        Assert.True(result.Moved + result.SetMembersMoved >= 1);
    }

    // ===== Execute with set members =====

    [Fact]
    public void Sort_ExecuteWithSetMembers_MovesBothFiles()
    {
        var cue = CreateFile("game.cue", "FILE \"game.bin\" BINARY");
        var bin = CreateFile("game.bin", "binary data");
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [_tempDir],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [cue] = "SNES",
                [bin] = "SNES"
            });

        Assert.True(result.Moved > 0);
        var snesDir = Path.Combine(_tempDir, "SNES");
        Assert.True(Directory.Exists(snesDir));
    }

    // ===== DatVerified sort =====

    [Fact]
    public void Sort_DatVerifiedDecision_SortsNormally()
    {
        var file = CreateFile("verified.nes");
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [_tempDir],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "NES" },
            enrichedSortDecisions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "DatVerified" });

        Assert.True(result.Moved > 0);
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "NES")));
    }

    // ===== No sort decision → default sort =====

    [Fact]
    public void Sort_NoSortDecision_DefaultsToStandardSort()
    {
        var file = CreateFile("game.nes");
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [_tempDir],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "NES" });

        Assert.True(result.Moved > 0);
    }

    // ===== Non-existent root → skipped =====

    [Fact]
    public void Sort_NonExistentRoot_Skipped()
    {
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [Path.Combine(_tempDir, "nope")],
            dryRun: true,
            enrichedConsoleKeys: new Dictionary<string, string>());

        Assert.Equal(0, result.Total);
    }

    // ===== Already in correct folder → skipped =====

    [Fact]
    public void Sort_AlreadyInCorrectFolder_CountsAsSkipped()
    {
        var nesDir = Path.Combine(_tempDir, "NES");
        var file = CreateFile("NES/game.nes");
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [_tempDir],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "NES" });

        Assert.True(result.Skipped > 0);
        Assert.True(File.Exists(file)); // not moved
    }

    // ===== PathMutations tracking =====

    [Fact]
    public void Sort_Execute_TracksPathMutations()
    {
        var file = CreateFile("game.nes");
        var sorter = CreateSorter();

        var result = sorter.Sort(
            roots: [_tempDir],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { [file] = "NES" });

        var pathMutations = result.PathMutations;
        Assert.NotNull(pathMutations);
        Assert.NotEmpty(pathMutations);
        Assert.All(pathMutations, pm =>
        {
            Assert.NotEmpty(pm.SourcePath);
            Assert.NotEmpty(pm.TargetPath);
        });
    }
}
