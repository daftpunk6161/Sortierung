using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Hashing;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for ArchiveHashService — specifically targeting:
/// - Hash7zEntries (entire 7z extraction + hashing path, 0 existing tests)
/// - ListArchiveEntries (7z output parsing)
/// - GetArchiveEntryNames for 7z and ZIP
/// - AreEntryPathsSafe edge cases
/// </summary>
public sealed class ArchiveHashServiceCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public ArchiveHashServiceCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ArchHashCov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    #region Fake ToolRunner for 7z simulation

    private sealed class Fake7zRunner : IToolRunner
    {
        private readonly string? _listOutput;
        private readonly int _extractExitCode;
        private readonly Action<string>? _onExtract;
        private readonly bool _toolFound;

        public Fake7zRunner(
            string? listOutput = null,
            int extractExitCode = 0,
            Action<string>? onExtract = null,
            bool toolFound = true)
        {
            _listOutput = listOutput;
            _extractExitCode = extractExitCode;
            _onExtract = onExtract;
            _toolFound = toolFound;
        }

        public string? FindTool(string toolName) =>
            toolName == "7z" && _toolFound ? "fake_7z.exe" : null;

        public ToolResult InvokeProcess(string filePath, string[] arguments,
            string? errorLabel = null)
        {
            if (arguments.Length > 0 && arguments[0] == "l")
                return new ToolResult(0, _listOutput ?? "", true);

            if (arguments.Length > 0 && arguments[0] == "x")
            {
                var outArg = arguments.FirstOrDefault(a => a.StartsWith("-o"));
                if (outArg is not null)
                    _onExtract?.Invoke(outArg[2..]);
                return new ToolResult(_extractExitCode, "", _extractExitCode == 0);
            }

            return new ToolResult(1, "", false);
        }

        public ToolResult InvokeProcess(string filePath, string[] arguments,
            ToolRequirement? requirement, string? errorLabel = null)
            => InvokeProcess(filePath, arguments, errorLabel);

        public ToolResult InvokeProcess(string filePath, string[] arguments,
            string? errorLabel, TimeSpan? timeout, CancellationToken ct)
            => InvokeProcess(filePath, arguments, errorLabel);

        public ToolResult InvokeProcess(string filePath, string[] arguments,
            ToolRequirement? requirement, string? errorLabel, TimeSpan? timeout,
            CancellationToken ct)
            => InvokeProcess(filePath, arguments, errorLabel);

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => InvokeProcess(sevenZipPath, arguments);
    }

    #endregion

    #region Helpers

    private string CreateDummy7z(string name = "test.7z")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, [0x37, 0x7A, 0xBC, 0xAF]); // 7z magic bytes
        return path;
    }

    private string CreateDummyZip(string name = "test.zip", params (string entryName, byte[] content)[] entries)
    {
        var path = Path.Combine(_tempDir, name);
        using var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var stream = entry.Open();
            stream.Write(content);
        }
        return path;
    }

    private static string Make7zListOutput(params string[] entryNames)
    {
        // Simulate 7z l -slt output format
        var lines = new List<string>
        {
            "Listing archive: test.7z",
            "",
            "----------"
        };
        foreach (var name in entryNames)
        {
            lines.Add($"Path = {name}");
            lines.Add("Size = 100");
            lines.Add("");
        }
        return string.Join('\n', lines);
    }

    #endregion

    // =================================================================
    //  7z hashing path (Hash7zEntries — entirely untested)
    // =================================================================

    [Fact]
    public void GetArchiveHashes_7z_ValidArchive_ReturnsHashes()
    {
        var archivePath = CreateDummy7z();
        var listOutput = Make7zListOutput("game.bin", "rom.nes");

        var runner = new Fake7zRunner(
            listOutput: listOutput,
            onExtract: outDir =>
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllBytes(Path.Combine(outDir, "game.bin"), [1, 2, 3, 4]);
                File.WriteAllBytes(Path.Combine(outDir, "rom.nes"), [5, 6, 7, 8]);
            });

        var svc = new ArchiveHashService(runner);
        var hashes = svc.GetArchiveHashes(archivePath, "SHA1");

        Assert.True(hashes.Length >= 2, $"Expected at least 2 hashes, got {hashes.Length}");
        Assert.All(hashes, h => Assert.False(string.IsNullOrEmpty(h)));
    }

    [Fact]
    public void GetArchiveHashes_7z_ExtractionFails_ReturnsEmpty()
    {
        var archivePath = CreateDummy7z();
        var listOutput = Make7zListOutput("game.bin");

        var runner = new Fake7zRunner(listOutput: listOutput, extractExitCode: 2);
        var svc = new ArchiveHashService(runner);

        var hashes = svc.GetArchiveHashes(archivePath, "SHA1");

        Assert.Empty(hashes);
    }

    [Fact]
    public void GetArchiveHashes_7z_UnsafeEntryPaths_ReturnsEmpty()
    {
        var archivePath = CreateDummy7z();
        // Include path traversal in the listing
        var listOutput = Make7zListOutput("../../../etc/passwd", "game.bin");

        var runner = new Fake7zRunner(listOutput: listOutput);
        var svc = new ArchiveHashService(runner);

        var hashes = svc.GetArchiveHashes(archivePath, "SHA1");

        Assert.Empty(hashes);
    }

    [Fact]
    public void GetArchiveHashes_7z_ToolNotFound_ReturnsEmpty()
    {
        var archivePath = CreateDummy7z();

        var runner = new Fake7zRunner(toolFound: false);
        var svc = new ArchiveHashService(runner);

        var hashes = svc.GetArchiveHashes(archivePath, "SHA1");

        Assert.Empty(hashes);
    }

    [Fact]
    public void GetArchiveHashes_7z_Cancellation_ThrowsOce()
    {
        var archivePath = CreateDummy7z();
        var listOutput = Make7zListOutput("game.bin");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var runner = new Fake7zRunner(listOutput: listOutput);
        var svc = new ArchiveHashService(runner);

        Assert.Throws<OperationCanceledException>(() =>
            svc.GetArchiveHashes(archivePath, "SHA1", cts.Token));
    }

    [Fact]
    public void GetArchiveHashes_7z_SHA256_ReturnsHashes()
    {
        var archivePath = CreateDummy7z();
        var listOutput = Make7zListOutput("test.rom");

        var runner = new Fake7zRunner(
            listOutput: listOutput,
            onExtract: outDir =>
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllBytes(Path.Combine(outDir, "test.rom"), [42, 42, 42]);
            });

        var svc = new ArchiveHashService(runner);
        var hashes = svc.GetArchiveHashes(archivePath, "SHA256");

        Assert.NotEmpty(hashes);
        // SHA256 produces 64 hex chars
        Assert.Contains(hashes, h => h.Length == 64);
    }

    [Fact]
    public void GetArchiveHashes_7z_CRC32_ReturnsHashes()
    {
        var archivePath = CreateDummy7z();
        var listOutput = Make7zListOutput("data.bin");

        var runner = new Fake7zRunner(
            listOutput: listOutput,
            onExtract: outDir =>
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllBytes(Path.Combine(outDir, "data.bin"), [10, 20, 30]);
            });

        var svc = new ArchiveHashService(runner);
        var hashes = svc.GetArchiveHashes(archivePath, "CRC32");

        Assert.NotEmpty(hashes);
        // CRC32 = 8 hex chars
        Assert.Contains(hashes, h => h.Length == 8);
    }

    [Fact]
    public void GetArchiveHashes_7z_NoExtractedFiles_ReturnsEmpty()
    {
        var archivePath = CreateDummy7z();
        var listOutput = Make7zListOutput("game.bin");

        // onExtract does nothing → no files in tempDir after extraction
        var runner = new Fake7zRunner(listOutput: listOutput);
        var svc = new ArchiveHashService(runner);

        var hashes = svc.GetArchiveHashes(archivePath, "SHA1");

        Assert.Empty(hashes);
    }

    [Fact]
    public void GetArchiveHashes_7z_ResultIsCached()
    {
        var archivePath = CreateDummy7z();
        var listOutput = Make7zListOutput("file.bin");
        var extractCount = 0;

        var runner = new Fake7zRunner(
            listOutput: listOutput,
            onExtract: outDir =>
            {
                extractCount++;
                Directory.CreateDirectory(outDir);
                File.WriteAllBytes(Path.Combine(outDir, "file.bin"), [99]);
            });

        var svc = new ArchiveHashService(runner);
        var first = svc.GetArchiveHashes(archivePath, "SHA1");
        var second = svc.GetArchiveHashes(archivePath, "SHA1");

        Assert.Equal(first, second);
        Assert.Equal(1, extractCount); // Only extracted once, second call from cache
    }

    // =================================================================
    //  GetArchiveEntryNames paths
    // =================================================================

    [Fact]
    public void GetArchiveEntryNames_7z_ReturnsEntryNames()
    {
        var archivePath = CreateDummy7z();
        var listOutput = Make7zListOutput("game.bin", "readme.txt");

        var runner = new Fake7zRunner(listOutput: listOutput);
        var svc = new ArchiveHashService(runner);

        var names = svc.GetArchiveEntryNames(archivePath);

        Assert.Equal(2, names.Count);
        Assert.Contains("game.bin", names);
        Assert.Contains("readme.txt", names);
    }

    [Fact]
    public void GetArchiveEntryNames_7z_NoToolRunner_ReturnsEmpty()
    {
        var archivePath = CreateDummy7z();
        var svc = new ArchiveHashService(toolRunner: null);

        var names = svc.GetArchiveEntryNames(archivePath);

        Assert.Empty(names);
    }

    [Fact]
    public void GetArchiveEntryNames_7z_ToolNotFound_ReturnsEmpty()
    {
        var archivePath = CreateDummy7z();
        var runner = new Fake7zRunner(toolFound: false);
        var svc = new ArchiveHashService(runner);

        var names = svc.GetArchiveEntryNames(archivePath);

        Assert.Empty(names);
    }

    [Fact]
    public void GetArchiveEntryNames_Zip_ReturnsEntryNames()
    {
        var archivePath = CreateDummyZip("entries.zip",
            ("game.bin", [1, 2, 3]),
            ("sub/readme.txt", [4, 5]));

        var svc = new ArchiveHashService();

        var names = svc.GetArchiveEntryNames(archivePath);

        Assert.Equal(2, names.Count);
        Assert.Contains("game.bin", names);
        Assert.Contains("sub/readme.txt", names);
    }

    [Fact]
    public void GetArchiveEntryNames_UnsupportedExt_ReturnsEmpty()
    {
        var path = Path.Combine(_tempDir, "test.rar");
        File.WriteAllBytes(path, [0]);

        var svc = new ArchiveHashService();
        var names = svc.GetArchiveEntryNames(path);

        Assert.Empty(names);
    }

    [Fact]
    public void GetArchiveEntryNames_NullOrMissing_ReturnsEmpty()
    {
        var svc = new ArchiveHashService();

        Assert.Empty(svc.GetArchiveEntryNames(null!));
        Assert.Empty(svc.GetArchiveEntryNames(""));
        Assert.Empty(svc.GetArchiveEntryNames(Path.Combine(_tempDir, "missing.7z")));
    }

    // =================================================================
    //  ListArchiveEntries parsing edge cases (via GetArchiveEntryNames)
    // =================================================================

    [Fact]
    public void GetArchiveEntryNames_7z_SkipsArchiveOwnName()
    {
        var archivePath = CreateDummy7z("myarchive.7z");
        // Include the archive's own name in the listing → should be filtered
        var listOutput = Make7zListOutput("myarchive.7z", "game.bin");

        var runner = new Fake7zRunner(listOutput: listOutput);
        var svc = new ArchiveHashService(runner);

        var names = svc.GetArchiveEntryNames(archivePath);

        Assert.Single(names);
        Assert.Equal("game.bin", names[0]);
    }

    [Fact]
    public void GetArchiveEntryNames_7z_EmptyOutput_ReturnsEmpty()
    {
        var archivePath = CreateDummy7z();
        var runner = new Fake7zRunner(listOutput: "");
        var svc = new ArchiveHashService(runner);

        var names = svc.GetArchiveEntryNames(archivePath);

        Assert.Empty(names);
    }

    [Fact]
    public void GetArchiveEntryNames_7z_PathsBeforeSeparator_Ignored()
    {
        var archivePath = CreateDummy7z();
        // Paths before the "----------" separator should be ignored
        var output = "Path = header_only.bin\n\n----------\nPath = real_entry.bin\nSize = 50\n";
        var runner = new Fake7zRunner(listOutput: output);
        var svc = new ArchiveHashService(runner);

        var names = svc.GetArchiveEntryNames(archivePath);

        Assert.Single(names);
        Assert.Equal("real_entry.bin", names[0]);
    }

    // =================================================================
    //  AreEntryPathsSafe edge cases
    // =================================================================

    [Theory]
    [InlineData("game/../../../etc/hosts")]
    [InlineData("..\\Windows\\System32")]
    [InlineData("C:\\Windows\\system.dll")]
    [InlineData("/etc/shadow")]
    public void AreEntryPathsSafe_UnsafePaths_ReturnsFalse(string unsafePath)
    {
        Assert.False(ArchiveHashService.AreEntryPathsSafe(["safe.bin", unsafePath]));
    }

    [Theory]
    [InlineData("rom/game.bin")]
    [InlineData("subfolder\\data.txt")]
    [InlineData("simple.iso")]
    public void AreEntryPathsSafe_SafePaths_ReturnsTrue(string safePath)
    {
        Assert.True(ArchiveHashService.AreEntryPathsSafe([safePath]));
    }

    [Fact]
    public void AreEntryPathsSafe_EmptyEnumerable_ReturnsTrue()
    {
        Assert.True(ArchiveHashService.AreEntryPathsSafe(Array.Empty<string>()));
    }

    [Fact]
    public void AreEntryPathsSafe_WhitespaceEntries_SkippedReturnsTrue()
    {
        Assert.True(ArchiveHashService.AreEntryPathsSafe(["", "  ", "\t"]));
    }
}
