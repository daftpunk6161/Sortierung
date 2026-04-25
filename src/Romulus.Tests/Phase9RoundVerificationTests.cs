using Romulus.Contracts.Models;
using Romulus.Core.SetParsing;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Behavioural R9 verification tests retained after Block A cleanup.
/// Source-mirror, meta-tests and existence-only assertions were removed in
/// test-suite-remediation-plan-2026-04-25.md.
/// </summary>
public sealed class Phase9RoundVerificationTests : IDisposable
{
    private readonly string _tempDir;

    public Phase9RoundVerificationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"r9-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── R9-032: parser tolerance for invalid filenames ───────────────

    [Fact]
    public void R9_032_CueSetParser_InvalidCharInFilename_ReturnsEmpty()
    {
        var cuePath = Path.Combine(_tempDir, "test.cue");
        File.WriteAllText(cuePath, "FILE \"game<pipe>.bin\" BINARY\n");

        var related = CueSetParser.GetRelatedFiles(cuePath);
        Assert.Empty(related);
    }

    [Fact]
    public void R9_032_CueSetParser_NullByteInFilename_ReturnsEmpty()
    {
        var cuePath = Path.Combine(_tempDir, "test.cue");
        File.WriteAllText(cuePath, "FILE \"game\0.bin\" BINARY\n");

        var related = CueSetParser.GetRelatedFiles(cuePath);
        Assert.Empty(related);
    }

    [Fact]
    public void R9_032_CueSetParser_ValidFile_StillResolves()
    {
        var cuePath = Path.Combine(_tempDir, "valid.cue");
        File.WriteAllText(cuePath, "FILE \"track01.bin\" BINARY\n");
        File.WriteAllText(Path.Combine(_tempDir, "track01.bin"), "data");

        var related = CueSetParser.GetRelatedFiles(cuePath);
        Assert.Single(related);
        Assert.EndsWith("track01.bin", related[0]);
    }

    [Fact]
    public void R9_032_M3uParser_InvalidCharInLine_ReturnsEmpty()
    {
        var m3uPath = Path.Combine(_tempDir, "test.m3u");
        File.WriteAllText(m3uPath, "game<pipe>.bin\n");

        var related = M3uPlaylistParser.GetRelatedFiles(m3uPath);
        Assert.Empty(related);
    }

    // ─── R9-033: MdsSetParser absolute-path contract ──────────────────

    [Fact]
    public void R9_033_MdsSetParser_GetMissingFiles_ReturnsAbsolutePath()
    {
        var mdsPath = Path.Combine(_tempDir, "game.mds");
        File.WriteAllText(mdsPath, "dummy");

        var missing = MdsSetParser.GetMissingFiles(mdsPath);
        Assert.Single(missing);
        Assert.True(Path.IsPathRooted(missing[0]), $"Expected absolute path but got: {missing[0]}");
        Assert.EndsWith(".mdf", missing[0]);
    }

    [Fact]
    public void R9_033_MdsSetParser_GetRelatedFiles_And_GetMissingFiles_UseSamePaths()
    {
        var mdsPath = Path.Combine(_tempDir, "game.mds");
        File.WriteAllText(mdsPath, "dummy");

        var missing = MdsSetParser.GetMissingFiles(mdsPath);
        Assert.Single(missing);

        File.WriteAllText(missing[0], "data");
        var related = MdsSetParser.GetRelatedFiles(mdsPath);
        Assert.Single(related);
        Assert.Equal(missing[0], related[0], StringComparer.OrdinalIgnoreCase);
    }

    // ─── R9-022: DedupeGroup behavior ─────────────────────────────────

    [Fact]
    public void R9_022_DedupeGroup_Winner_IsRetained()
    {
        var group = new DedupeGroup
        {
            Winner = new RomCandidate { MainPath = @"C:\test.rom", GameKey = "TEST" },
            GameKey = "TEST"
        };
        Assert.NotNull(group.Winner);
        Assert.Equal("TEST", group.Winner.GameKey);
    }

    // ─── R9-034: BOM tolerance ────────────────────────────────────────

    [Fact]
    public void R9_034_CueSetParser_BomFile_ParsesCorrectly()
    {
        var cuePath = Path.Combine(_tempDir, "bom.cue");
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var content = System.Text.Encoding.UTF8.GetBytes("FILE \"track.bin\" BINARY\n");
        File.WriteAllBytes(cuePath, bom.Concat(content).ToArray());
        File.WriteAllText(Path.Combine(_tempDir, "track.bin"), "data");

        var related = CueSetParser.GetRelatedFiles(cuePath);
        Assert.Single(related);
    }

    // ─── R9-037: GDI quoted filenames ─────────────────────────────────

    [Fact]
    public void R9_037_GdiSetParser_QuotedFilename_Works()
    {
        var gdiPath = Path.Combine(_tempDir, "game.gdi");
        var trackFile = Path.Combine(_tempDir, "track with spaces.bin");
        File.WriteAllText(gdiPath, "2\n1 0 4 2352 \"track with spaces.bin\" 0\n2 100 0 2352 track02.raw 0\n");
        File.WriteAllText(trackFile, "data");
        File.WriteAllText(Path.Combine(_tempDir, "track02.raw"), "data");

        var related = GdiSetParser.GetRelatedFiles(gdiPath);
        Assert.Equal(2, related.Count);
    }

    // ─── R9-024: data integrity in conversion-registry.json ───────────

    [Fact]
    public void R9_024_ConversionRegistry_ConsoleKeys_AreUpperCase()
    {
        var registryPath = Path.Combine(FindRepoRoot(), "data", "conversion-registry.json");
        if (!File.Exists(registryPath)) return;

        var content = File.ReadAllText(registryPath);
        var matches = System.Text.RegularExpressions.Regex.Matches(
            content, @"""applicableConsoles"":\s*\[(.*?)\]",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var keys = System.Text.RegularExpressions.Regex.Matches(match.Groups[1].Value, @"""(\w+)""");
            foreach (System.Text.RegularExpressions.Match key in keys)
            {
                var val = key.Groups[1].Value;
                Assert.Equal(val.ToUpperInvariant(), val, StringComparer.Ordinal);
            }
        }
    }

    // ─── R9-040: French localization file presence ────────────────────

    [Fact]
    public void R9_040_FrenchLocalizationFile_Exists()
    {
        var frPath = Path.Combine(FindRepoRoot(), "data", "i18n", "fr.json");
        Assert.True(File.Exists(frPath), "fr.json must exist");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "AGENTS.md")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
