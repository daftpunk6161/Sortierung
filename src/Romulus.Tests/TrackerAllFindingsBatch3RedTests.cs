using System.Text.Json;
using Romulus.Core.Classification;
using Romulus.Core.Deduplication;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Sorting;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

public sealed class TrackerAllFindingsBatch3RedTests : IDisposable
{
    private readonly string _tempDir;

    public TrackerAllFindingsBatch3RedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Batch3_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Di10_RunManager_MustImplementIDisposable()
    {
        Assert.True(typeof(Romulus.Api.RunManager).GetInterfaces().Contains(typeof(IDisposable)));
    }

    [Fact]
    public void Fin01_DatCatalogState_LoadState_MustKeepCaseInsensitiveComparers()
    {
        var statePath = Path.Combine(_tempDir, "dat-catalog-state.json");
        File.WriteAllText(statePath, """
        {
          "entries": {
            "PSX": {
              "installedDate": "2026-01-01T00:00:00Z",
              "fileSha256": "abc",
              "fileSizeBytes": 1,
              "localPath": "C:\\dat\\psx.dat"
            }
          },
          "removedBuiltinIds": ["NO-INTRO-PSX"],
          "userEntries": []
        }
        """);

        var state = DatCatalogStateService.LoadState(statePath);

        Assert.True(state.Entries.ContainsKey("psx"));
        Assert.Contains("no-intro-psx", state.RemovedBuiltinIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fin04_Test03_BenchmarkFixture_MustNotUseSyncLockBlockInInitializeAsync()
    {
        var benchmarkFixtureSource = File.ReadAllText(FindRepoFile("src", "Romulus.Tests", "Benchmark", "BenchmarkFixture.cs"));
        Assert.DoesNotContain("lock (_lock)", benchmarkFixtureSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Api05_Api_MustNotServeStaticFilesByDefault()
    {
        var programSource = File.ReadAllText(FindRepoFile("src", "Romulus.Api", "Program.cs"));
        Assert.DoesNotContain("UseStaticFiles();", programSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Th11_ToolHashValidation_MustAvoidDuplicateHashChecksInProductionInvokerPath()
    {
        var supportSource = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure", "Conversion", "ToolInvokers", "ToolInvokerSupport.cs"));
        var invokerSource = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure", "Conversion", "ToolInvokers", "NkitInvoker.cs"));

        Assert.Contains("skipExpectedHashValidation", supportSource, StringComparison.Ordinal);
        Assert.Contains("ShouldSkipHashConstraintValidation", invokerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Sort02_M3uPlaylist_MustBeRewrittenAfterAtomicSetMove()
    {
        var root = Path.Combine(_tempDir, "sort-m3u-rewrite");
        var inputDir = Path.Combine(root, "Input");
        Directory.CreateDirectory(Path.Combine(inputDir, "sub"));

        var m3uPath = Path.Combine(inputDir, "Game.m3u");
        var cue1 = Path.Combine(inputDir, "disc1.cue");
        var cue2 = Path.Combine(inputDir, "sub", "disc2.cue");

        File.WriteAllText(m3uPath, "disc1.cue\r\nsub\\disc2.cue\r\n");
        File.WriteAllText(cue1, "FILE \"disc1.bin\" BINARY");
        File.WriteAllText(cue2, "FILE \"disc2.bin\" BINARY");

        var sorter = new ConsoleSorter(new FileSystemAdapter(), LoadDetector());
        var result = sorter.Sort(
            [root],
            [".m3u", ".cue"],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [m3uPath] = "PS1",
                [cue1] = "PS1",
                [cue2] = "PS1"
            },
            candidatePaths: [m3uPath, cue1, cue2]);

        var movedPlaylist = Path.Combine(root, "PS1", "Game.m3u");
        Assert.True(File.Exists(movedPlaylist));

        var content = File.ReadAllText(movedPlaylist);
        Assert.Contains("disc1.cue", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disc2.cue", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sub\\disc2.cue", content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public void Sort03_OverlappingSetMembers_MustBeAssignedDeterministicallyToSinglePrimary()
    {
        var root = Path.Combine(_tempDir, "sort-overlap");
        Directory.CreateDirectory(root);

        var sharedCue = Path.Combine(root, "shared.cue");
        var setA = Path.Combine(root, "A.m3u");
        var setB = Path.Combine(root, "B.m3u");

        File.WriteAllText(sharedCue, "FILE \"shared.bin\" BINARY");
        File.WriteAllText(setA, "shared.cue\r\n");
        File.WriteAllText(setB, "shared.cue\r\n");

        var sorter = new ConsoleSorter(new FileSystemAdapter(), LoadDetector());
        var result = sorter.Sort(
            [root],
            [".m3u", ".cue"],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [setA] = "PS1",
                [setB] = "PS1",
                [sharedCue] = "PS1"
            },
            candidatePaths: [setA, setB, sharedCue]);

        Assert.Equal(0, result.Failed);
        Assert.True(File.Exists(Path.Combine(root, "PS1", "A.m3u")));
        Assert.True(File.Exists(Path.Combine(root, "PS1", "B.m3u")));
        Assert.True(File.Exists(Path.Combine(root, "PS1", "shared.cue")));
    }

    [Fact]
    public void Core02_Deduplication_UnknownOrInvalidConsoleKeys_MustUseCanonicalUnknownGrouping()
    {
        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = Path.Combine(_tempDir, "a.rom"),
                GameKey = "zelda",
                ConsoleKey = " ??? ",
                Category = FileCategory.Game,
                CompletenessScore = 100,
                DatMatch = true
            },
            new RomCandidate
            {
                MainPath = Path.Combine(_tempDir, "b.rom"),
                GameKey = "zelda",
                ConsoleKey = "",
                Category = FileCategory.Game,
                CompletenessScore = 90,
                DatMatch = true
            }
        };

        var groups = DeduplicationEngine.Deduplicate(candidates);
        Assert.Single(groups);
    }

    [Fact]
    public void Fin06_I18n07_JsonOutput_MustUseUnsafeRelaxedJsonEscapingWhereOutputIsUserFacing()
    {
        var apiProgram = File.ReadAllText(FindRepoFile("src", "Romulus.Api", "Program.cs"));
        var cliWriter = File.ReadAllText(FindRepoFile("src", "Romulus.CLI", "CliOutputWriter.cs"));

        Assert.Contains("UnsafeRelaxedJsonEscaping", apiProgram, StringComparison.Ordinal);
        Assert.Contains("UnsafeRelaxedJsonEscaping", cliWriter, StringComparison.Ordinal);
    }

    private static ConsoleDetector LoadDetector()
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var consolesPath = Path.Combine(dataDir, "consoles.json");
        return ConsoleDetector.LoadFromJson(File.ReadAllText(consolesPath));
    }

    private static string FindRepoFile(params string[] segments)
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var repoRoot = Directory.GetParent(dataDir)?.FullName
            ?? throw new InvalidOperationException("Repository root could not be resolved.");
        return Path.Combine(new[] { repoRoot }.Concat(segments).ToArray());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
