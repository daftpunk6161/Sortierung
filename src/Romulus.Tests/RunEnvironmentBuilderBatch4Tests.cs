using System.Text.Json;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage boost for RunEnvironmentBuilder batch 4 – targets internal/static helpers:
/// MergeDatIndices, MatchPackGlob, CompareDatCandidatePriority, FindExactStemMatch,
/// BuildConsoleMap error paths, AppendFileStamp missing file, LoadKnownBiosHashes.
/// </summary>
public sealed class RunEnvironmentBuilderBatch4Tests : IDisposable
{
    private readonly string _root;

    public RunEnvironmentBuilderBatch4Tests()
    {
        _root = Path.Combine(Path.GetTempPath(), "REB_B4_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    // ═══════ MatchPackGlob ════════════════════════════════════════

    [Fact]
    public void MatchPackGlob_WildcardSuffix_MatchesCorrectly()
    {
        var files = new[]
        {
            Path.Combine(_root, "Nintendo - NES (20240101).dat"),
            Path.Combine(_root, "Nintendo - NES (20240615).dat"),
            Path.Combine(_root, "Sega - Genesis.dat")
        };
        foreach (var f in files) File.WriteAllText(f, "dummy");

        var result = RunEnvironmentBuilder.MatchPackGlob(files, "Nintendo - NES*");

        Assert.NotNull(result);
        Assert.Contains("20240615", result); // Latest by stem sort
    }

    [Fact]
    public void MatchPackGlob_NoMatch_ReturnsNull()
    {
        var files = new[] { Path.Combine(_root, "Sega - Genesis.dat") };
        File.WriteAllText(files[0], "dummy");

        Assert.Null(RunEnvironmentBuilder.MatchPackGlob(files, "Nintendo*"));
    }

    [Fact]
    public void MatchPackGlob_EmptyPattern_ReturnsNull()
    {
        var files = new[] { Path.Combine(_root, "test.dat") };
        Assert.Null(RunEnvironmentBuilder.MatchPackGlob(files, ""));
    }

    [Fact]
    public void MatchPackGlob_EmptyFiles_ReturnsNull()
    {
        Assert.Null(RunEnvironmentBuilder.MatchPackGlob([], "test*"));
    }

    // ═══════ FindExactStemMatch ═══════════════════════════════════

    [Fact]
    public void FindExactStemMatch_MatchesById()
    {
        var datPath = Path.Combine(_root, "mame.dat");
        File.WriteAllText(datPath, "dummy");

        var result = RunEnvironmentBuilder.FindExactStemMatch([datPath], "mame", "MAME2003", "ARCADE");
        Assert.Equal(datPath, result);
    }

    [Fact]
    public void FindExactStemMatch_MatchesBySystem()
    {
        var datPath = Path.Combine(_root, "MAME2003.dat");
        File.WriteAllText(datPath, "dummy");

        var result = RunEnvironmentBuilder.FindExactStemMatch([datPath], "nonexistent-id", "MAME2003", "ARCADE");
        Assert.Equal(datPath, result);
    }

    [Fact]
    public void FindExactStemMatch_NoMatch_ReturnsNull()
    {
        var datPath = Path.Combine(_root, "something.dat");
        File.WriteAllText(datPath, "dummy");

        Assert.Null(RunEnvironmentBuilder.FindExactStemMatch([datPath], "nonexist"));
    }

    [Fact]
    public void FindExactStemMatch_EmptyStems_ReturnsNull()
    {
        var datPath = Path.Combine(_root, "test.dat");
        Assert.Null(RunEnvironmentBuilder.FindExactStemMatch([datPath], "", " ", null!));
    }

    // ═══════ BuildConsoleMap ══════════════════════════════════════

    [Fact]
    public void BuildConsoleMap_WithValidCatalog_MapsByExactFile()
    {
        var dataDir = Path.Combine(_root, "data");
        var datRoot = Path.Combine(_root, "dats");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(datRoot);

        // Create a catalog
        var catalog = new[]
        {
            new { Group = "Nintendo", System = "NES", Id = "no-intro-nes", ConsoleKey = "NES", PackMatch = "" }
        };
        File.WriteAllText(Path.Combine(dataDir, "dat-catalog.json"), JsonSerializer.Serialize(catalog));

        // Create matching DAT file
        File.WriteAllText(Path.Combine(datRoot, "no-intro-nes.dat"), "dummy");

        var map = RunEnvironmentBuilder.BuildConsoleMap(dataDir, datRoot);

        Assert.True(map.ContainsKey("NES"));
        Assert.Contains("no-intro-nes.dat", map["NES"]);
    }

    [Fact]
    public void BuildConsoleMap_MalformedCatalog_FallsThrough()
    {
        var dataDir = Path.Combine(_root, "data-bad");
        var datRoot = Path.Combine(_root, "dats-bad");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(datRoot);

        // Create malformed catalog
        File.WriteAllText(Path.Combine(dataDir, "dat-catalog.json"), "{ invalid json [");

        // Create DAT file
        File.WriteAllText(Path.Combine(datRoot, "SNES.dat"), "dummy");

        var map = RunEnvironmentBuilder.BuildConsoleMap(dataDir, datRoot);

        // Should still discover by stem scan
        Assert.True(map.ContainsKey("SNES"));
    }

    [Fact]
    public void BuildConsoleMap_NoCatalog_ScansByDirectory()
    {
        var dataDir = Path.Combine(_root, "data-none");
        var datRoot = Path.Combine(_root, "dats-none");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(datRoot);
        // no dat-catalog.json

        File.WriteAllText(Path.Combine(datRoot, "GBA.dat"), "dummy");
        File.WriteAllText(Path.Combine(datRoot, "N64.xml"), "dummy");

        var map = RunEnvironmentBuilder.BuildConsoleMap(dataDir, datRoot);

        Assert.True(map.ContainsKey("GBA"));
        Assert.True(map.ContainsKey("N64"));
    }

    [Fact]
    public void BuildConsoleMap_DatRootDoesNotExist_ReturnsEmpty()
    {
        var dataDir = Path.Combine(_root, "data-x");
        Directory.CreateDirectory(dataDir);

        var map = RunEnvironmentBuilder.BuildConsoleMap(dataDir, Path.Combine(_root, "nonexistent"));
        Assert.Empty(map);
    }

    [Fact]
    public void BuildConsoleMap_SupplementalDats_TracksDuplicateConsoleKeys()
    {
        var dataDir = Path.Combine(_root, "data-supp");
        var datRoot = Path.Combine(_root, "dats-supp");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(datRoot);

        // Two catalog entries for "NES" - one No-Intro, one FBNeo
        var catalog = new[]
        {
            new { Group = "No-Intro", System = "NES", Id = "no-intro-nes", ConsoleKey = "NES", PackMatch = "" },
            new { Group = "FBNeo", System = "FB NES", Id = "fbneo-nes", ConsoleKey = "NES", PackMatch = "" }
        };
        File.WriteAllText(Path.Combine(dataDir, "dat-catalog.json"), JsonSerializer.Serialize(catalog));

        File.WriteAllText(Path.Combine(datRoot, "no-intro-nes.dat"), "primary");
        File.WriteAllText(Path.Combine(datRoot, "fbneo-nes.dat"), "supplemental");

        var map = RunEnvironmentBuilder.BuildConsoleMap(dataDir, datRoot, out var supplementalDats);

        Assert.True(map.ContainsKey("NES"));
        Assert.Contains("no-intro-nes.dat", map["NES"]); // Primary
        Assert.True(supplementalDats.ContainsKey("NES"));
        Assert.Single(supplementalDats["NES"]);
        Assert.Contains("fbneo-nes.dat", supplementalDats["NES"][0]);
    }

    [Fact]
    public void BuildConsoleMap_CatalogWithEmptyConsoleKey_SkipsEntry()
    {
        var dataDir = Path.Combine(_root, "data-empty-key");
        var datRoot = Path.Combine(_root, "dats-empty-key");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(datRoot);

        var catalog = new[]
        {
            new { Group = "Test", System = "Test", Id = "test-id", ConsoleKey = "", PackMatch = "" }
        };
        File.WriteAllText(Path.Combine(dataDir, "dat-catalog.json"), JsonSerializer.Serialize(catalog));

        var map = RunEnvironmentBuilder.BuildConsoleMap(dataDir, datRoot);
        Assert.Empty(map);
    }

    [Fact]
    public void BuildConsoleMap_PackMatchGlob_MatchesNewestDailyPack()
    {
        var dataDir = Path.Combine(_root, "data-pack");
        var datRoot = Path.Combine(_root, "dats-pack");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(datRoot);

        var catalog = new[]
        {
            new { Group = "No-Intro", System = "NES", Id = "no-intro-nes", ConsoleKey = "NES",
                PackMatch = "Nintendo - Nintendo Entertainment System*" }
        };
        File.WriteAllText(Path.Combine(dataDir, "dat-catalog.json"), JsonSerializer.Serialize(catalog));

        // Daily pack files
        File.WriteAllText(Path.Combine(datRoot, "Nintendo - Nintendo Entertainment System (20240101).dat"), "old");
        File.WriteAllText(Path.Combine(datRoot, "Nintendo - Nintendo Entertainment System (20240615).dat"), "new");

        var map = RunEnvironmentBuilder.BuildConsoleMap(dataDir, datRoot);

        Assert.True(map.ContainsKey("NES"));
        Assert.Contains("20240615", map["NES"]); // Newest
    }

    // ═══════ ResolveDataDir / TryResolveDataDir ═══════════════════

    [Fact]
    public void TryResolveDataDir_ReturnsNonNull()
    {
        // In the test runner context, data/ should be discoverable
        var result = RunEnvironmentBuilder.TryResolveDataDir();
        Assert.NotNull(result);
        Assert.True(Directory.Exists(result));
        Assert.True(File.Exists(Path.Combine(result, "consoles.json")));
    }

    [Fact]
    public void ResolveDataDir_DoesNotThrow()
    {
        var result = RunEnvironmentBuilder.ResolveDataDir();
        Assert.True(Directory.Exists(result));
    }
}
