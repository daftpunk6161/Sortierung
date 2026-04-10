using Romulus.Contracts.Models;
using Romulus.Infrastructure.Dat;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-04): DatIndex should store ROM filename and expose LookupWithFilename.
/// </summary>
public sealed class DatIndexRomFilenameIssue9RedTests : IDisposable
{
    private readonly string _tempDir;

    public DatIndexRomFilenameIssue9RedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus", "DatIndexRomFilenameIssue9", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Add_WithRomFileName_LookupWithFilename_ReturnsGameAndRomName_Issue9A04()
    {
        var index = new DatIndex();

        index.Add("NES", "abc123", "Super Mario Bros", "Super Mario Bros (World).nes");

        var lookup = index.LookupWithFilename("NES", "abc123");
        Assert.NotNull(lookup);
        Assert.Equal("Super Mario Bros", lookup?.GameName);
        Assert.Equal("Super Mario Bros (World).nes", lookup?.RomFileName);
    }

    [Fact]
    public void DatRepositoryAdapter_ShouldPopulateRomFileName_InDatIndex_Issue9A04()
    {
        var datPath = Path.Combine(_tempDir, "nes.dat");
        File.WriteAllText(datPath,
            "<datafile><game name='Contra'><rom name='Contra (USA).nes' sha1='aaa111' size='40976'/></game></datafile>");

        var adapter = new DatRepositoryAdapter();
        var index = adapter.GetDatIndex(_tempDir, new Dictionary<string, string> { ["NES"] = "nes.dat" }, "SHA1");

        var lookup = index.LookupWithFilename("NES", "aaa111");
        Assert.NotNull(lookup);
        Assert.Equal("Contra", lookup?.GameName);
        Assert.Equal("Contra (USA).nes", lookup?.RomFileName);
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
            // Best effort cleanup.
        }
    }
}
