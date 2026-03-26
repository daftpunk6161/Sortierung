using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Dat;
using Xunit;

namespace RomCleanup.Tests;

public class DatRepositoryAdapterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DatRepositoryAdapter _dat;

    public DatRepositoryAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RomCleanup_DatTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _dat = new DatRepositoryAdapter();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void GetDatGameKey_CombinesConsoleAndName()
    {
        var key = _dat.GetDatGameKey("Super Mario Bros", "NES");
        Assert.Equal("nes|super mario bros", key);
    }

    [Fact]
    public void GetDatIndex_EmptyRoot_ReturnsEmpty()
    {
        var index = _dat.GetDatIndex(
            Path.Combine(_tempDir, "nope"),
            new Dictionary<string, string> { ["NES"] = "nes.dat" });

        Assert.Equal(0, index.TotalEntries);
    }

    [Fact]
    public void GetDatIndex_ParsesValidDat()
    {
        var datContent = @"<?xml version=""1.0""?>
<datafile>
  <game name=""Super Mario Bros (USA)"">
    <rom name=""smb.nes"" size=""40976"" sha1=""abc123"" />
  </game>
  <game name=""Zelda (USA)"">
    <rom name=""zelda.nes"" size=""131088"" sha1=""def456"" />
  </game>
</datafile>";

        File.WriteAllText(Path.Combine(_tempDir, "nes.dat"), datContent);

        var index = _dat.GetDatIndex(
            _tempDir,
            new Dictionary<string, string> { ["NES"] = "nes.dat" });

        Assert.Equal(2, index.TotalEntries);
        Assert.True(index.HasConsole("NES"));
        Assert.Equal("Super Mario Bros (USA)", index.Lookup("NES", "abc123"));
        Assert.Equal("Zelda (USA)", index.Lookup("NES", "def456"));
    }

    [Fact]
    public void GetDatParentCloneIndex_ParsesCloneRelations()
    {
        var datContent = @"<?xml version=""1.0""?>
<datafile>
  <game name=""sf2"">
    <rom name=""sf2.zip"" size=""100"" sha1=""aaa"" />
  </game>
  <game name=""sf2ce"" cloneof=""sf2"">
    <rom name=""sf2ce.zip"" size=""120"" sha1=""bbb"" />
  </game>
  <game name=""sf2hf"" cloneof=""sf2"">
    <rom name=""sf2hf.zip"" size=""130"" sha1=""ccc"" />
  </game>
</datafile>";

        var datPath = Path.Combine(_tempDir, "fbn.dat");
        File.WriteAllText(datPath, datContent);

        var parentMap = _dat.GetDatParentCloneIndex(datPath);

        Assert.Equal(2, parentMap.Count);
        Assert.Equal("sf2", parentMap["sf2ce"]);
        Assert.Equal("sf2", parentMap["sf2hf"]);
        Assert.False(parentMap.ContainsKey("sf2")); // parent itself not in map
    }

    [Fact]
    public void ResolveParentName_WalksChain()
    {
        var parentMap = new Dictionary<string, string>
        {
            ["sf2hf"] = "sf2ce",
            ["sf2ce"] = "sf2"
        };

        var parent = _dat.ResolveParentName("sf2hf", parentMap);
        Assert.Equal("sf2", parent);
    }

    [Fact]
    public void ResolveParentName_NoParent_ReturnsNull()
    {
        var parentMap = new Dictionary<string, string>();
        Assert.Null(_dat.ResolveParentName("sf2", parentMap));
    }

    [Fact]
    public void ResolveParentName_CycleDetection()
    {
        var parentMap = new Dictionary<string, string>
        {
            ["a"] = "b",
            ["b"] = "a" // cycle
        };

        // Should not infinite-loop — returns whatever it reached last
        var result = _dat.ResolveParentName("a", parentMap);
        Assert.NotNull(result);
    }

    [Fact]
    public void GetDatParentCloneIndex_NonExistentFile_ReturnsEmpty()
    {
        var map = _dat.GetDatParentCloneIndex(Path.Combine(_tempDir, "nope.dat"));
        Assert.Empty(map);
    }

    [Fact]
    public void GetDatIndex_MalformedXml_DoesNotThrow()
    {
        File.WriteAllText(Path.Combine(_tempDir, "bad.dat"), "<not valid xml");

        var index = _dat.GetDatIndex(
            _tempDir,
            new Dictionary<string, string> { ["TEST"] = "bad.dat" });

        // Should return empty or partial, not throw
        Assert.NotNull(index);
    }

        [Fact]
        public void GetDatIndex_BillionLaughsPayload_DoesNotExpandEntities_AndReturnsNoEntries_Issue9()
        {
                // Arrange: classic XXE entity expansion payload (billion laughs family)
                var datContent = """
                        <?xml version="1.0"?>
                        <!DOCTYPE datafile [
                            <!ENTITY lol "lol">
                            <!ENTITY lol1 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
                            <!ENTITY lol2 "&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;">
                        ]>
                        <datafile>
                            <game name="&lol2;">
                                <rom name="attack.bin" size="1" sha1="deadbeef" />
                            </game>
                        </datafile>
                        """;
                File.WriteAllText(Path.Combine(_tempDir, "xxe.dat"), datContent);

                // Act
                var index = _dat.GetDatIndex(
                        _tempDir,
                        new Dictionary<string, string> { ["TEST"] = "xxe.dat" });

                // Assert
                Assert.NotNull(index);
                Assert.Equal(0, index.TotalEntries);
        }
}
