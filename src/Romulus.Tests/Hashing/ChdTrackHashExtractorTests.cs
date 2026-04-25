using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Hashing;
using Xunit;

namespace Romulus.Tests.Hashing;

/// <summary>
/// Unit tests for ChdTrackHashExtractor.
/// Verifies chdman info output parsing and edge-case handling.
/// </summary>
public sealed class ChdTrackHashExtractorTests
{
    private static readonly string FakeChdmanPath = "/fake/chdman";
    private const string ValidSha1 = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";

    // ── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public void ExtractDataSha1_ParsesDataSha1Line_ReturnsLowerHex()
    {
        var chdmanOutput = $"""
            chdman - MAME Compressed Hunks of Data (CHD) manager 0.263
            Input file:   game.chd
            File Version: 5
            Logical size: 733,921,280 bytes
            CHD sha1:     0000000000000000000000000000000000000001
            Data sha1:    {ValidSha1.ToUpperInvariant()}
            """;

        var runner = new StubToolRunner(FakeChdmanPath, chdmanOutput);
        var extractor = new ChdTrackHashExtractor(runner);

        using var tmp = new TempFile(".chd");
        var result = extractor.ExtractDataSha1(tmp.Path);

        Assert.Equal(ValidSha1, result);
    }

    [Fact]
    public void ExtractDataSha1_CaseInsensitiveLabel_ParsesCorrectly()
    {
        var chdmanOutput = $"data SHA1  :  {ValidSha1}\n";

        var runner = new StubToolRunner(FakeChdmanPath, chdmanOutput);
        var extractor = new ChdTrackHashExtractor(runner);

        using var tmp = new TempFile(".chd");
        var result = extractor.ExtractDataSha1(tmp.Path);

        Assert.Equal(ValidSha1, result);
    }

    // ── Guard rails ─────────────────────────────────────────────────────────

    [Fact]
    public void ExtractDataSha1_NullPath_ReturnsNull()
    {
        var extractor = new ChdTrackHashExtractor(new StubToolRunner(FakeChdmanPath, ""));
        Assert.Null(extractor.ExtractDataSha1(null!));
    }

    [Fact]
    public void ExtractDataSha1_EmptyPath_ReturnsNull()
    {
        var extractor = new ChdTrackHashExtractor(new StubToolRunner(FakeChdmanPath, ""));
        Assert.Null(extractor.ExtractDataSha1("   "));
    }

    [Fact]
    public void ExtractDataSha1_FileDoesNotExist_ReturnsNull()
    {
        var runner = new StubToolRunner(FakeChdmanPath, $"Data sha1: {ValidSha1}");
        var extractor = new ChdTrackHashExtractor(runner);

        var result = extractor.ExtractDataSha1("/nonexistent/path/game.chd");

        Assert.Null(result);
    }

    [Fact]
    public void ExtractDataSha1_ToolNotFound_ReturnsNull()
    {
        var runner = new StubToolRunner(toolPath: null, output: "");
        var extractor = new ChdTrackHashExtractor(runner);

        using var tmp = new TempFile(".chd");
        var result = extractor.ExtractDataSha1(tmp.Path);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractDataSha1_ToolFails_ReturnsNull()
    {
        var runner = new StubToolRunner(FakeChdmanPath, output: null, success: false);
        var extractor = new ChdTrackHashExtractor(runner);

        using var tmp = new TempFile(".chd");
        var result = extractor.ExtractDataSha1(tmp.Path);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractDataSha1_NoDataSha1Line_ReturnsNull()
    {
        const string chdmanOutput = """
            chdman - MAME CHD manager
            Input file:   nodatasha1.chd
            CHD sha1:     0000000000000000000000000000000000000001
            """;

        var runner = new StubToolRunner(FakeChdmanPath, chdmanOutput);
        var extractor = new ChdTrackHashExtractor(runner);

        using var tmp = new TempFile(".chd");
        var result = extractor.ExtractDataSha1(tmp.Path);

        Assert.Null(result);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private sealed class StubToolRunner(string? toolPath, string? output, bool success = true) : IToolRunner
    {
        public string? FindTool(string name) => toolPath;

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
            => success && output is not null
                ? new ToolResult(0, output, true)
                : new ToolResult(1, string.Empty, false);

        public ToolResult InvokeProcess(
            string filePath,
            string[] arguments,
            string? errorLabel,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
            => InvokeProcess(filePath, arguments, errorLabel);

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(1, string.Empty, false);
    }

    /// <summary>Creates a real, empty temp file with the given extension and deletes it on Dispose.</summary>
    private sealed class TempFile : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public TempFile(string extension)
        {
            Path += extension;
            File.WriteAllBytes(Path, []);
        }

        public void Dispose()
        {
            try { File.Delete(Path); } catch { /* best-effort */ }
        }
    }
}
