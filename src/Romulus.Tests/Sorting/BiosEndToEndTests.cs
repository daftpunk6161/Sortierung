using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Core.Deduplication;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Sorting;
using Romulus.Tests.TestFixtures;
using Xunit;

namespace Romulus.Tests.Sorting;

/// <summary>
/// End-to-end BIOS handling chain.
///
/// The BIOS contract spans four layers; a regression in any single one breaks
/// data integrity (e.g. a BIOS file getting grouped with a normal game and
/// later moved to trash as a "duplicate"). This suite enforces the chain end
/// to end:
///
///  1.  FileClassifier.Analyze recognizes "BIOS"-tagged base names.
///  2.  CandidateFactory.Create prefixes BIOS GameKeys with "__BIOS__"
///        so they cannot share a dedupe group with a regular game.
///  3.  DeduplicationEngine.SelectWinner respects the BIOS category rank
///        (BIOS is its own pool; never compared to a Game candidate).
///  4.  ConsoleSorter routes BIOS-keyed files to the well-known _BIOS folder.
/// </summary>
public sealed class BiosEndToEndTests : IDisposable
{
    private readonly string _tempDir;

    public BiosEndToEndTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_B6_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void FileClassifier_BiosTaggedBaseName_ReturnsBiosCategory()
    {
        var d = FileClassifier.Analyze("[BIOS] PSX (USA) (v4.4)");
        Assert.Equal(FileCategory.Bios, d.Category);
        Assert.Equal("bios-tag", d.ReasonCode);
    }

    [Fact]
    public void CandidateFactory_BiosCandidate_GetsPrefixAndCannotCollideWithGame()
    {
        var bios = CandidateFactory.Create(
            normalizedPath: "scph1001.bin", extension: ".bin", sizeBytes: 524288,
            category: FileCategory.Bios, gameKey: "scph1001",
            region: "US", regionScore: 1000, formatScore: 700,
            versionScore: 0, headerScore: 0, completenessScore: 50,
            sizeTieBreakScore: 524288, datMatch: true, consoleKey: "PSX");

        var game = CandidateFactory.Create(
            normalizedPath: "scph1001.iso", extension: ".iso", sizeBytes: 700_000_000,
            category: FileCategory.Game, gameKey: "scph1001",
            region: "US", regionScore: 1000, formatScore: 850,
            versionScore: 500, headerScore: 0, completenessScore: 75,
            sizeTieBreakScore: 700_000_000, datMatch: false, consoleKey: "PSX");

        Assert.NotEqual(bios.GameKey, game.GameKey);
        Assert.StartsWith("__BIOS__", bios.GameKey);
    }

    [Fact]
    public void DeduplicationEngine_BiosAndGameCandidateInSamePool_PrefersGame()
    {
        // Even when fed into the same SelectWinner call with the same base key,
        // the BIOS prefix forces them into different group pools at the
        // CandidateFactory level. Here we assert that within a *single* mixed list,
        // the higher category rank (Game=5 > Bios=4) wins; this protects the
        // contract that BIOS is never preferred over a real Game inside a single
        // group, which means BIOS isolation MUST happen by GameKey, not by the
        // ranking inside SelectWinner.
        var bios = CandidateFactory.Create(
            normalizedPath: "shared.bin", extension: ".bin", sizeBytes: 1024,
            category: FileCategory.Bios, gameKey: "shared",
            region: "US", regionScore: 1000, formatScore: 700,
            versionScore: 0, headerScore: 0, completenessScore: 100,
            sizeTieBreakScore: 1024, datMatch: true, consoleKey: "PSX");

        var game = CandidateFactory.Create(
            normalizedPath: "shared.iso", extension: ".iso", sizeBytes: 700_000_000,
            category: FileCategory.Game, gameKey: "shared",
            region: "US", regionScore: 1000, formatScore: 850,
            versionScore: 500, headerScore: 0, completenessScore: 50,
            sizeTieBreakScore: 700_000_000, datMatch: false, consoleKey: "PSX");

        var winner = DeduplicationEngine.SelectWinner([bios, game]);
        Assert.NotNull(winner);
        Assert.Equal(FileCategory.Game, winner!.Category);
    }

    [Fact]
    public void ConsoleSorter_BiosFileAlreadyInBiosFolder_DoesNotResortIt()
    {
        var root = Path.Combine(_tempDir, "root");
        var biosFolder = Path.Combine(root, "_BIOS");
        Directory.CreateDirectory(biosFolder);

        var biosPath = Path.Combine(biosFolder, "scph1001.bin");
        File.WriteAllBytes(biosPath, new byte[1024]);

        var sorter = new ConsoleSorter(new FileSystemAdapter(), LoadConsoleDetector());
        var result = sorter.Sort(
            [root],
            [".bin"],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [biosPath] = "PSX"
            },
            enrichedCategories: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [biosPath] = nameof(FileCategory.Bios)
            },
            candidatePaths: [biosPath]);

        // Files already inside the well-known _BIOS folder must be excluded from
        // sorting (data-loss safety: BIOS files in the canonical bucket stay put).
        Assert.True(File.Exists(biosPath), "BIOS file inside _BIOS folder must remain in place.");
        Assert.False(Directory.Exists(Path.Combine(root, "PSX")), "Sorter must not have created a PSX target for excluded BIOS source.");
        Assert.Equal(0, result.Failed);
    }

    private static ConsoleDetector LoadConsoleDetector()
    {
        var consolesPath = RepoPaths.RepoFile("data", "consoles.json");
        return ConsoleDetector.LoadFromJson(File.ReadAllText(consolesPath));
    }
}
