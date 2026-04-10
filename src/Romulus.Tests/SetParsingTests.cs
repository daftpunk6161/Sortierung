using Romulus.Core.SetParsing;
using Xunit;

namespace Romulus.Tests;

public class SetParsingTests : IDisposable
{
    private readonly string _tempDir;

    public SetParsingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"setparse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ==========  CUE  ==========

    [Fact]
    public void Cue_GetRelatedFiles_ReturnsBinFiles()
    {
        var cuePath = Path.Combine(_tempDir, "game.cue");
        File.WriteAllText(cuePath, "FILE \"game.bin\" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n");
        File.WriteAllText(Path.Combine(_tempDir, "game.bin"), "dummy");

        var related = CueSetParser.GetRelatedFiles(cuePath);
        Assert.Single(related);
        Assert.EndsWith("game.bin", related[0]);
    }

    [Fact]
    public void Cue_GetMissingFiles_ReportsMissing()
    {
        var cuePath = Path.Combine(_tempDir, "game.cue");
        File.WriteAllText(cuePath, "FILE \"track01.bin\" BINARY\nFILE \"track02.bin\" BINARY\n");
        // Only create track01
        File.WriteAllText(Path.Combine(_tempDir, "track01.bin"), "dummy");

        var missing = CueSetParser.GetMissingFiles(cuePath);
        Assert.Single(missing);
        Assert.Contains("track02.bin", missing[0]);
    }

    [Fact]
    public void Cue_MultiTrack_ReturnsSelf()
    {
        var cuePath = Path.Combine(_tempDir, "multi.cue");
        File.WriteAllText(cuePath, "FILE \"multi (Track 1).bin\" BINARY\nFILE \"multi (Track 2).bin\" BINARY\n");
        File.WriteAllText(Path.Combine(_tempDir, "multi (Track 1).bin"), "d");
        File.WriteAllText(Path.Combine(_tempDir, "multi (Track 2).bin"), "d");

        var related = CueSetParser.GetRelatedFiles(cuePath);
        Assert.Equal(2, related.Count);
    }

    [Fact]
    public void Cue_PathTraversal_Blocked()
    {
        var cuePath = Path.Combine(_tempDir, "evil.cue");
        File.WriteAllText(cuePath, "FILE \"..\\..\\etc\\passwd\" BINARY\n");

        var related = CueSetParser.GetRelatedFiles(cuePath);
        Assert.Empty(related);
    }

    // ==========  GDI  ==========

    [Fact]
    public void Gdi_GetRelatedFiles_ReturnsTrackFiles()
    {
        var gdiPath = Path.Combine(_tempDir, "game.gdi");
        File.WriteAllText(gdiPath, "3\n1 0 4 2048 track01.raw 0\n2 456 0 2352 track02.bin 0\n3 45000 4 2048 track03.bin 0\n");
        File.WriteAllText(Path.Combine(_tempDir, "track01.raw"), "d");
        File.WriteAllText(Path.Combine(_tempDir, "track02.bin"), "d");
        File.WriteAllText(Path.Combine(_tempDir, "track03.bin"), "d");

        var related = GdiSetParser.GetRelatedFiles(gdiPath);
        Assert.Equal(3, related.Count);
    }

    [Fact]
    public void Gdi_GetMissingFiles_DetectsMissing()
    {
        var gdiPath = Path.Combine(_tempDir, "game.gdi");
        File.WriteAllText(gdiPath, "2\n1 0 4 2048 track01.raw 0\n2 456 0 2352 track02.bin 0\n");
        File.WriteAllText(Path.Combine(_tempDir, "track01.raw"), "d");
        // track02.bin missing!

        var missing = GdiSetParser.GetMissingFiles(gdiPath);
        Assert.Single(missing);
        Assert.Contains("track02.bin", missing[0]);
    }

    // ==========  CCD  ==========

    [Fact]
    public void Ccd_GetRelatedFiles_FindsCompanions()
    {
        var ccdPath = Path.Combine(_tempDir, "game.ccd");
        File.WriteAllText(ccdPath, "[CloneCD]\nVersion=3\n");
        File.WriteAllText(Path.Combine(_tempDir, "game.img"), "d");
        File.WriteAllText(Path.Combine(_tempDir, "game.sub"), "d");

        var related = CcdSetParser.GetRelatedFiles(ccdPath);
        Assert.Equal(2, related.Count);
    }

    [Fact]
    public void Ccd_GetMissingFiles_DetectsMissingImg()
    {
        var ccdPath = Path.Combine(_tempDir, "game.ccd");
        File.WriteAllText(ccdPath, "[CloneCD]\nVersion=3\n");
        // No .img or .sub companion

        var missing = CcdSetParser.GetMissingFiles(ccdPath);
        Assert.Contains(missing, m => m.EndsWith(".img"));
    }

    // ==========  MDS  ==========

    [Fact]
    public void Mds_GetRelatedFiles_FindsMdf()
    {
        var mdsPath = Path.Combine(_tempDir, "game.mds");
        File.WriteAllText(mdsPath, "MDS header");
        File.WriteAllText(Path.Combine(_tempDir, "game.mdf"), "d");

        var related = MdsSetParser.GetRelatedFiles(mdsPath);
        Assert.Single(related);
        Assert.EndsWith(".mdf", related[0]);
    }

    [Fact]
    public void Mds_GetMissingFiles_DetectsMissingMdf()
    {
        var mdsPath = Path.Combine(_tempDir, "game.mds");
        File.WriteAllText(mdsPath, "MDS header");

        var missing = MdsSetParser.GetMissingFiles(mdsPath);
        Assert.Single(missing);
    }

    // ==========  M3U  ==========

    [Fact]
    public void M3u_GetRelatedFiles_ReturnsEntries()
    {
        var m3uPath = Path.Combine(_tempDir, "multi.m3u");
        File.WriteAllText(m3uPath, "disc1.cue\ndisc2.cue\n");
        File.WriteAllText(Path.Combine(_tempDir, "disc1.cue"), "d");
        File.WriteAllText(Path.Combine(_tempDir, "disc2.cue"), "d");

        var related = M3uPlaylistParser.GetRelatedFiles(m3uPath);
        Assert.Equal(2, related.Count);
    }

    [Fact]
    public void M3u_SkipsComments()
    {
        var m3uPath = Path.Combine(_tempDir, "list.m3u");
        File.WriteAllText(m3uPath, "# comment\ndisc.cue\n");
        File.WriteAllText(Path.Combine(_tempDir, "disc.cue"), "d");

        var related = M3uPlaylistParser.GetRelatedFiles(m3uPath);
        Assert.Single(related);
    }

    [Fact]
    public void M3u_PathTraversal_Blocked()
    {
        var m3uPath = Path.Combine(_tempDir, "evil.m3u");
        File.WriteAllText(m3uPath, "..\\..\\etc\\passwd\n");

        var related = M3uPlaylistParser.GetRelatedFiles(m3uPath);
        Assert.Empty(related);
    }

    [Fact]
    public void M3u_GetMissingFiles_DetectsMissing()
    {
        var m3uPath = Path.Combine(_tempDir, "multi.m3u");
        File.WriteAllText(m3uPath, "disc1.cue\ndisc2.cue\n");
        File.WriteAllText(Path.Combine(_tempDir, "disc1.cue"), "d");

        var missing = M3uPlaylistParser.GetMissingFiles(m3uPath);
        Assert.Single(missing);
        Assert.Contains("disc2.cue", missing[0]);
    }
}
