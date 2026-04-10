using Romulus.Contracts.Models;
using Romulus.Infrastructure.Dat;
using Xunit;

namespace Romulus.Tests;

public class DatRepositoryAdapterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DatRepositoryAdapter _dat;

    public DatRepositoryAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_DatTest_" + Guid.NewGuid().ToString("N")[..8]);
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
        public void GetDatIndex_ParsesMachineBasedDat()
        {
                var datContent = @"<?xml version=""1.0""?>
<mame>
    <machine name=""sf2"">
        <rom name=""sf2u.30g"" size=""524288"" sha1=""mamehash1"" />
    </machine>
    <machine name=""sf2ce"" cloneof=""sf2"">
        <rom name=""sf2ce.23"" size=""524288"" sha1=""mamehash2"" />
    </machine>
</mame>";

                File.WriteAllText(Path.Combine(_tempDir, "arcade.dat"), datContent);

                var index = _dat.GetDatIndex(
                        _tempDir,
                        new Dictionary<string, string> { ["ARCADE"] = "arcade.dat" });

                Assert.Equal(2, index.TotalEntries);
                Assert.True(index.HasConsole("ARCADE"));
                Assert.Equal("sf2", index.Lookup("ARCADE", "mamehash1"));
                Assert.Equal("sf2ce", index.Lookup("ARCADE", "mamehash2"));
        }

        [Fact]
        public void GetDatIndex_ParsesDiskEntries()
        {
                var datContent = @"<?xml version=""1.0""?>
<datafile>
    <game name=""Game Disc (USA)"">
        <disk name=""gamedisc"" sha1=""diskhash1"" />
    </game>
</datafile>";

                File.WriteAllText(Path.Combine(_tempDir, "disc.dat"), datContent);

                var index = _dat.GetDatIndex(
                        _tempDir,
                        new Dictionary<string, string> { ["PSX"] = "disc.dat" });

                Assert.Equal("Game Disc (USA)", index.Lookup("PSX", "diskhash1"));
        }

        [Fact]
        public void GetDatIndex_BiosGameName_SetsIsBiosFlag()
        {
                var datContent = @"<?xml version=""1.0""?>
<datafile>
    <game name=""PlayStation BIOS"">
        <rom name=""SCPH1001.BIN"" size=""524288"" sha1=""bioshash1"" />
    </game>
</datafile>";

                File.WriteAllText(Path.Combine(_tempDir, "psx.dat"), datContent);

                var index = _dat.GetDatIndex(
                        _tempDir,
                        new Dictionary<string, string> { ["PSX"] = "psx.dat" });

        var match = index.LookupWithFilename("PSX", "bioshash1");
        Assert.NotNull(match);
        Assert.True(match.Value.IsBios);
        }

    [Fact]
    public void GetDatIndex_MachineWithIsBiosAttribute_SetsIsBiosFlag()
    {
        var datContent = @"<?xml version=""1.0""?>
<mame>
  <machine name=""neogeo"" isbios=""yes"">
    <rom name=""neogeo.zip"" size=""100"" sha1=""biosarcade1"" />
  </machine>
</mame>";

        File.WriteAllText(Path.Combine(_tempDir, "arcade-bios.dat"), datContent);

        var index = _dat.GetDatIndex(
            _tempDir,
            new Dictionary<string, string> { ["ARCADE"] = "arcade-bios.dat" });

        var match = index.LookupWithFilename("ARCADE", "biosarcade1");
        Assert.NotNull(match);
        Assert.True(match.Value.IsBios);
    }

    [Fact]
    public void GetDatIndex_MachineWithIsDeviceAttribute_SetsIsBiosFlag()
    {
        var datContent = @"<?xml version=""1.0""?>
<mame>
  <machine name=""naomi"" isdevice=""yes"">
    <rom name=""naomi.zip"" size=""100"" sha1=""devicehash1"" />
  </machine>
</mame>";

        File.WriteAllText(Path.Combine(_tempDir, "arcade-device.dat"), datContent);

        var index = _dat.GetDatIndex(
            _tempDir,
            new Dictionary<string, string> { ["ARCADE"] = "arcade-device.dat" });

        var match = index.LookupWithFilename("ARCADE", "devicehash1");
        Assert.NotNull(match);
        Assert.True(match.Value.IsBios);
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
    public void GetDatParentCloneIndex_ParsesMachineCloneRelations()
    {
        var datContent = @"<?xml version=""1.0""?>
<mame>
  <machine name=""sf2"" />
  <machine name=""sf2ce"" cloneof=""sf2"" />
</mame>";

        var datPath = Path.Combine(_tempDir, "mame-parent.dat");
        File.WriteAllText(datPath, datContent);

        var parentMap = _dat.GetDatParentCloneIndex(datPath);

        Assert.Single(parentMap);
        Assert.Equal("sf2", parentMap["sf2ce"]);
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

    // ── Hash fallback chain tests ──

    [Fact]
    public void GetDatIndex_CrcOnlyDat_FallsBackToCrc()
    {
        // FBNeo-style DAT: only crc attribute, no sha1/md5
        var datContent = @"<?xml version=""1.0""?>
<datafile>
  <game name=""mslug5"">
    <rom name=""268-p1.bin"" size=""524288"" crc=""4352ce78"" />
  </game>
</datafile>";

        File.WriteAllText(Path.Combine(_tempDir, "fbneo.dat"), datContent);

        var index = _dat.GetDatIndex(
            _tempDir,
            new Dictionary<string, string> { ["ARCADE"] = "fbneo.dat" });

        Assert.Equal(1, index.TotalEntries);
        // The fallback chain should index by CRC32 when SHA1 is absent
        Assert.Equal("mslug5", index.Lookup("ARCADE", "4352ce78"));
    }

        [Fact]
        public void GetDatIndex_PreferredHashMissing_EmitsFallbackWarning()
        {
                var datContent = @"<?xml version=""1.0""?>
<datafile>
    <game name=""FallbackGame"">
        <rom name=""fallback.bin"" size=""100"" crc=""1234abcd"" />
    </game>
</datafile>";

                File.WriteAllText(Path.Combine(_tempDir, "fallback-warning.dat"), datContent);
                var logMessages = new List<string>();
                var dat = new DatRepositoryAdapter(log: message => logMessages.Add(message));

                var index = dat.GetDatIndex(
                        _tempDir,
                        new Dictionary<string, string> { ["TEST"] = "fallback-warning.dat" },
                        hashType: "SHA256");

                Assert.Equal("FallbackGame", index.Lookup("TEST", "1234abcd"));
                Assert.Contains(logMessages, message =>
                        message.Contains("fallback", StringComparison.OrdinalIgnoreCase)
                        && message.Contains("SHA256", StringComparison.OrdinalIgnoreCase));
        }

    [Fact]
    public void GetDatIndex_Md5OnlyDat_FallsBackToMd5()
    {
        var datContent = @"<?xml version=""1.0""?>
<datafile>
  <game name=""TestGame"">
    <rom name=""test.bin"" size=""100"" md5=""d41d8cd98f00b204e9800998ecf8427e"" />
  </game>
</datafile>";

        File.WriteAllText(Path.Combine(_tempDir, "md5.dat"), datContent);

        var index = _dat.GetDatIndex(
            _tempDir,
            new Dictionary<string, string> { ["TEST"] = "md5.dat" });

        Assert.Equal(1, index.TotalEntries);
        Assert.Equal("TestGame", index.Lookup("TEST", "d41d8cd98f00b204e9800998ecf8427e"));
    }

    [Fact]
    public void GetDatIndex_NoHashAttributes_ProducesNoEntries()
    {
        // DAT with rom element but no hash attributes
        var datContent = @"<?xml version=""1.0""?>
<datafile>
  <game name=""NoHash"">
    <rom name=""none.bin"" size=""100"" />
  </game>
</datafile>";

        File.WriteAllText(Path.Combine(_tempDir, "nohash.dat"), datContent);

        var index = _dat.GetDatIndex(
            _tempDir,
            new Dictionary<string, string> { ["TEST"] = "nohash.dat" });

        // ROM without any hash should still create an entry (no hash but name persists)
        // The hash field will be null so it won't be in the hash index, but game exists
        Assert.Null(index.Lookup("TEST", "anything"));
    }

    [Fact]
    public void GetDatIndex_MixedHashes_Sha1PreferredOverFallback()
    {
        // DAT with all hash types – SHA1 should be preferred, not CRC32
        var datContent = @"<?xml version=""1.0""?>
<datafile>
  <game name=""FullHashGame"">
    <rom name=""full.bin"" size=""100"" sha1=""abc123"" md5=""def456"" crc=""aabbccdd"" />
  </game>
</datafile>";

        File.WriteAllText(Path.Combine(_tempDir, "full.dat"), datContent);

        var index = _dat.GetDatIndex(
            _tempDir,
            new Dictionary<string, string> { ["TEST"] = "full.dat" });

        Assert.Equal(1, index.TotalEntries);
        // SHA1 is the default hash type – should be indexed by SHA1
        Assert.Equal("FullHashGame", index.Lookup("TEST", "abc123"));
        // CRC32 and MD5 should NOT be used when SHA1 is present
        Assert.Null(index.Lookup("TEST", "aabbccdd"));
        Assert.Null(index.Lookup("TEST", "def456"));
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

    [Fact]
    public void GetDatIndex_7zCompressedDat_WithoutToolRunner_SkipsGracefully()
    {
        // Create a file with 7z magic bytes (not a real 7z, but triggers detection)
        var magic = new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x00 };
        File.WriteAllBytes(Path.Combine(_tempDir, "mame.dat"), magic);

        var logMessages = new List<string>();
        var dat = new DatRepositoryAdapter(log: msg => logMessages.Add(msg));

        var index = dat.GetDatIndex(
            _tempDir,
            new Dictionary<string, string> { ["MAME"] = "mame.dat" });

        // Without ToolRunner, 7z DATs should be skipped gracefully
        Assert.Equal(0, index.TotalEntries);
        Assert.Contains(logMessages, m => m.Contains("7z-compressed") && m.Contains("no ToolRunner"));
    }

    [Fact]
    public void GetDatIndex_7zCompressedDat_WithToolRunner_Decompresses()
    {
        // This test verifies the full 7z decompression path using a mock ToolRunner
        // that actually creates the expected XML file in the temp directory.
        var xmlContent = @"<?xml version=""1.0""?>
<mame>
  <machine name=""mslug"">
    <rom name=""201-p1.bin"" size=""524288"" crc=""08d8b8df"" />
  </machine>
</mame>";

        // Create a file with 7z magic bytes
        var magic = new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x00 };
        var archivePath = Path.Combine(_tempDir, "mame.dat");
        File.WriteAllBytes(archivePath, magic);

        var toolRunner = new Fake7zToolRunner(xmlContent);
        var logMessages = new List<string>();
        var dat = new DatRepositoryAdapter(log: msg => logMessages.Add(msg), toolRunner: toolRunner);

        var index = dat.GetDatIndex(
            _tempDir,
            new Dictionary<string, string> { ["MAME"] = "mame.dat" });

        Assert.Equal(1, index.TotalEntries);
        Assert.Equal("mslug", index.Lookup("MAME", "08d8b8df"));
        Assert.Contains(logMessages, m => m.Contains("Decompressed 7z DAT"));
    }

    /// <summary>Fake IToolRunner that simulates 7z extraction by writing XML to the output directory.</summary>
    private sealed class Fake7zToolRunner : Romulus.Contracts.Ports.IToolRunner
    {
        private readonly string _xmlContent;

        public Fake7zToolRunner(string xmlContent) => _xmlContent = xmlContent;

        public string? FindTool(string toolName) => toolName == "7z" ? @"C:\fake\7z.exe" : null;

        public Romulus.Contracts.Ports.ToolResult InvokeProcess(
            string filePath, string[] arguments, string? errorLabel = null)
        {
            // Parse -o<dir> from arguments and write the XML there
            var outDir = arguments.FirstOrDefault(a => a.StartsWith("-o"))?[2..];
            if (outDir is not null)
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "mame.xml"), _xmlContent);
            }
            return new Romulus.Contracts.Ports.ToolResult(0, "Everything is Ok", true);
        }

        public Romulus.Contracts.Ports.ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => InvokeProcess(sevenZipPath, arguments);
    }

    // ── DatIndex.MergeFrom ──────────────────────────────────────────────

    [Fact]
    public void DatIndex_MergeFrom_AddsEntriesFromOtherIndex()
    {
        var primary = new DatIndex();
        primary.Add("NES", "aaa111", "Super Mario Bros");

        var supplemental = new DatIndex();
        supplemental.Add("NES", "bbb222", "Contra");

        primary.MergeFrom(supplemental);

        Assert.Equal(2, primary.TotalEntries);
        Assert.Equal("Super Mario Bros", primary.Lookup("NES", "aaa111"));
        Assert.Equal("Contra", primary.Lookup("NES", "bbb222"));
    }

    [Fact]
    public void DatIndex_MergeFrom_NewConsoleKey_AddedCorrectly()
    {
        var primary = new DatIndex();
        primary.Add("NES", "aaa111", "Super Mario Bros");

        var supplemental = new DatIndex();
        supplemental.Add("SNES", "ccc333", "Zelda");

        primary.MergeFrom(supplemental);

        Assert.Equal(2, primary.ConsoleCount);
        Assert.Equal("Zelda", primary.Lookup("SNES", "ccc333"));
    }

    [Fact]
    public void DatIndex_MergeFrom_DuplicateHash_UpdatesEntry()
    {
        var primary = new DatIndex();
        primary.Add("NES", "aaa111", "Original Name");

        var supplemental = new DatIndex();
        supplemental.Add("NES", "aaa111", "Updated Name");

        primary.MergeFrom(supplemental);

        // The Add method updates existing keys — supplemental overwrites
        Assert.Equal(1, primary.TotalEntries);
        Assert.Equal("Updated Name", primary.Lookup("NES", "aaa111"));
    }

    [Fact]
    public void DatIndex_MergeFrom_EmptySource_NoChange()
    {
        var primary = new DatIndex();
        primary.Add("NES", "aaa111", "Mario");

        var empty = new DatIndex();
        primary.MergeFrom(empty);

        Assert.Equal(1, primary.TotalEntries);
        Assert.Equal("Mario", primary.Lookup("NES", "aaa111"));
    }
}
