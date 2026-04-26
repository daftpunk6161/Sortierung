using Romulus.Core.Classification;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Sorting;
using Romulus.Tests.TestFixtures;
using Xunit;

namespace Romulus.Tests.Sorting;

/// <summary>
/// Arcade-set integrity invariants.
///
/// Arcade titles are typically distributed as ZIP "set archives". The integrity
/// contract is therefore narrower than for disc-based sets:
///
/// 1.  An arcade-keyed ZIP must move to the resolved arcade console folder as
///       a single opaque file - it must NEVER be extracted, decomposed, or have
///       inner contents reshuffled by the sorting pipeline.
/// 2.  Arcade detection by folder/extension must surface a stable arcade
///       console key (deterministic input -> deterministic output).
/// 3.  Two distinct arcade ZIPs with the same set name (e.g. clone vs parent)
///       must both be preserved under collision resolution; no silent overwrite.
/// </summary>
public sealed class ArcadeSetIntegrityTests : IDisposable
{
    private readonly string _tempDir;

    public ArcadeSetIntegrityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_B5_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void ConsoleSorter_ArcadeZip_MovesAsSingleOpaqueUnitWithoutExtraction()
    {
        var root = Path.Combine(_tempDir, "root");
        var input = Path.Combine(root, "MAME");
        Directory.CreateDirectory(input);

        // Synthetic ZIP-shaped payload (a valid empty ZIP is irrelevant for sorting -
        // sorter must move bytes verbatim).
        var setZip = Path.Combine(input, "sf2.zip");
        var payload = new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        File.WriteAllBytes(setZip, payload);
        var originalLength = new FileInfo(setZip).Length;

        var sorter = new ConsoleSorter(new FileSystemAdapter(), LoadConsoleDetector());
        var result = sorter.Sort(
            [root],
            [".zip"],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [setZip] = "ARCADE"
            },
            candidatePaths: [setZip]);

        Assert.Equal(0, result.Failed);
        var dest = Path.Combine(root, "ARCADE", "sf2.zip");
        Assert.True(File.Exists(dest), "Arcade ZIP must be present at destination as a single file.");
        Assert.False(File.Exists(setZip));
        Assert.Equal(originalLength, new FileInfo(dest).Length);
        Assert.Equal(payload, File.ReadAllBytes(dest));

        // Sorter must NOT have extracted the ZIP into an arcade subfolder.
        var stray = Directory.GetFiles(Path.Combine(root, "ARCADE"), "*", SearchOption.AllDirectories);
        Assert.Single(stray); // only the moved ZIP itself
    }

    [Fact]
    public void ConsoleDetector_MameFolderDetection_IsDeterministic()
    {
        var detector = LoadConsoleDetector();
        var root = Path.Combine(_tempDir, "scan");
        var mameDir = Path.Combine(root, "MAME");
        Directory.CreateDirectory(mameDir);
        var f = Path.Combine(mameDir, "sf2.zip");
        File.WriteAllBytes(f, [0x50, 0x4B, 0x05, 0x06]);

        var k1 = detector.DetectByFolder(f, root);
        var k2 = detector.DetectByFolder(f, root);

        Assert.NotNull(k1);
        Assert.Equal(k1, k2);
        Assert.Equal("MAME", k1);
    }

    [Fact]
    public void ConsoleSorter_SameNamedArcadeZipsWithDifferentContent_PreservesBoth()
    {
        var root = Path.Combine(_tempDir, "root");
        var inputA = Path.Combine(root, "Set1");
        var inputB = Path.Combine(root, "Set2");
        Directory.CreateDirectory(inputA);
        Directory.CreateDirectory(inputB);

        var zipA = Path.Combine(inputA, "sf2.zip");
        var zipB = Path.Combine(inputB, "sf2.zip");
        File.WriteAllBytes(zipA, [0xA1, 0xA2, 0xA3]);
        File.WriteAllBytes(zipB, [0xB1, 0xB2, 0xB3]);

        var sorter = new ConsoleSorter(new FileSystemAdapter(), LoadConsoleDetector());
        var result = sorter.Sort(
            [root],
            [".zip"],
            dryRun: false,
            enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [zipA] = "ARCADE",
                [zipB] = "ARCADE"
            },
            candidatePaths: [zipA, zipB]);

        Assert.Equal(0, result.Failed);
        var dest = Path.Combine(root, "ARCADE");
        var present = Directory.GetFiles(dest, "sf2*.zip", SearchOption.TopDirectoryOnly);
        Assert.Equal(2, present.Length);

        var contents = present.Select(File.ReadAllBytes).ToList();
        Assert.Contains(contents, b => b.Length == 3 && b[0] == 0xA1);
        Assert.Contains(contents, b => b.Length == 3 && b[0] == 0xB1);
    }

    private static ConsoleDetector LoadConsoleDetector()
    {
        var consolesPath = RepoPaths.RepoFile("data", "consoles.json");
        return ConsoleDetector.LoadFromJson(File.ReadAllText(consolesPath));
    }
}
