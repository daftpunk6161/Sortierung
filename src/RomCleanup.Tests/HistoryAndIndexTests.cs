using RomCleanup.Infrastructure.History;
using Xunit;

namespace RomCleanup.Tests;

public class RunHistoryServiceTests : IDisposable
{
    private readonly string _tempDir;

    public RunHistoryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"romhist_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void GetHistory_NonexistentDir_ReturnsEmpty()
    {
        var svc = new RunHistoryService();
        var result = svc.GetHistory(Path.Combine(_tempDir, "nope"));
        Assert.Empty(result.Entries);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void GetHistory_WithPlanFiles_ParsesEntries()
    {
        var json = """{"Roots": ["C:\\Roms"], "Mode": "Move", "Status": "ok", "FileCount": 42}""";
        File.WriteAllText(Path.Combine(_tempDir, "move-plan-20240101.json"), json);

        var svc = new RunHistoryService();
        var result = svc.GetHistory(_tempDir);
        Assert.Single(result.Entries);
        Assert.Equal(1, result.Total);
        Assert.Equal("Move", result.Entries[0].Mode);
        Assert.Equal(42, result.Entries[0].FileCount);
    }

    [Fact]
    public void GetHistory_MalformedJson_Skipped()
    {
        File.WriteAllText(Path.Combine(_tempDir, "move-plan-bad.json"), "not json");
        File.WriteAllText(Path.Combine(_tempDir, "move-plan-ok.json"),
            """{"Mode": "DryRun", "TotalFiles": 10}""");

        var svc = new RunHistoryService();
        var result = svc.GetHistory(_tempDir);
        Assert.Single(result.Entries);
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public void GetHistory_MaxEntries_Limits()
    {
        for (int i = 0; i < 5; i++)
            File.WriteAllText(Path.Combine(_tempDir, $"move-plan-{i:D4}.json"),
                $"{{\"FileCount\": {i}}}");

        var svc = new RunHistoryService();
        var result = svc.GetHistory(_tempDir, maxEntries: 3);
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(5, result.Total);
    }

    [Fact]
    public void GetDetail_ValidFile_ReturnsDict()
    {
        var path = Path.Combine(_tempDir, "move-plan-test.json");
        File.WriteAllText(path, """{"Mode": "Move", "Roots": ["X:\\"]}""");

        var svc = new RunHistoryService();
        var detail = svc.GetDetail(path);
        Assert.NotNull(detail);
        Assert.True(detail.ContainsKey("Mode"));
    }

    [Fact]
    public void GetDetail_MissingFile_ReturnsNull()
    {
        var svc = new RunHistoryService();
        Assert.Null(svc.GetDetail(Path.Combine(_tempDir, "nope.json")));
    }
}

public class ScanIndexServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ScanIndexServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"romscan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var svc = new ScanIndexService();
        var index = svc.Load(Path.Combine(_tempDir, "nofile.json"));
        Assert.Empty(index);
    }

    [Fact]
    public void SaveAndLoad_Roundtrip()
    {
        var svc = new ScanIndexService();
        var path = Path.Combine(_tempDir, "scan-index.json");

        var index = new Dictionary<string, RomCleanup.Contracts.Models.ScanIndexEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\Roms\\game.zip"] = new()
            {
                Path = "C:\\Roms\\game.zip",
                Fingerprint = "game.zip|1024|637500000000",
                Hash = "abc123",
                LastScan = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        svc.Save(path, index);
        Assert.True(File.Exists(path));

        var loaded = svc.Load(path);
        Assert.Single(loaded);
        Assert.True(loaded.ContainsKey("C:\\Roms\\game.zip"));
        Assert.Equal("abc123", loaded["C:\\Roms\\game.zip"].Hash);
    }

    [Fact]
    public void Load_CaseInsensitive()
    {
        var svc = new ScanIndexService();
        var path = Path.Combine(_tempDir, "idx.json");

        var index = new Dictionary<string, RomCleanup.Contracts.Models.ScanIndexEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["C:\\ROMS\\GAME.ZIP"] = new() { Path = "C:\\ROMS\\GAME.ZIP", Fingerprint = "fp" }
        };

        svc.Save(path, index);
        var loaded = svc.Load(path);
        Assert.True(loaded.ContainsKey("c:\\roms\\game.zip"));
    }

    [Fact]
    public void GetPathFingerprint_Format()
    {
        var file = Path.Combine(_tempDir, "test.bin");
        File.WriteAllText(file, "data");
        var fp = ScanIndexService.GetPathFingerprint(file);
        Assert.Contains("|", fp);
        Assert.Contains("test.bin", fp);
    }

    [Fact]
    public void GetPathFingerprint_NonexistentFile()
    {
        var fp = ScanIndexService.GetPathFingerprint(Path.Combine(_tempDir, "nope.bin"));
        Assert.Contains("|0|0", fp);
    }
}
