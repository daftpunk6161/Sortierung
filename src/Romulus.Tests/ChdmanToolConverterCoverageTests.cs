using System.IO.Compression;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for ChdmanToolConverter: direct conversion, archive extraction,
/// multi-CUE, security guards, CD vs DVD heuristic, error/edge paths.
/// </summary>
public sealed class ChdmanToolConverterCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public ChdmanToolConverterCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"chdman_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    #region Direct Conversion

    [Fact]
    public void Convert_DirectIso_Success()
    {
        var source = CreateFile("game.iso", 800 * 1024 * 1024); // > 700 MB = DVD
        var target = Path.Combine(_tempDir, "game.chd");

        var stub = new TrackingToolRunner();
        stub.RegisterTool("chdman", "chdman.exe");
        stub.NextResult = new ToolResult(0, "ok", true);

        // ConversionOutputValidator expects the file to exist and have size > 0
        File.WriteAllBytes(target, new byte[1024]);

        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(source, target, "chdman.exe", "createdvd");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.Equal(target, result.TargetPath);
        Assert.Contains("createdvd", stub.LastArgs!); // Should remain DVD for large files
    }

    [Fact]
    public void Convert_SmallIso_FallsBackToCreateCd()
    {
        // Under 700MB → createcd heuristic
        var source = CreateFile("small.iso", 500 * 1024 * 1024);
        var target = Path.Combine(_tempDir, "small.chd");

        var stub = new TrackingToolRunner();
        stub.RegisterTool("chdman", "chdman.exe");
        stub.NextResult = new ToolResult(0, "ok", true);
        File.WriteAllBytes(target, new byte[1024]);

        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(source, target, "chdman.exe", "createdvd");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.Contains("createcd", stub.LastArgs!); // Downgraded from DVD to CD
    }

    [Fact]
    public void Convert_UnsupportedExtension_Skipped()
    {
        var source = CreateFile("game.nes", 100);
        var target = Path.Combine(_tempDir, "game.chd");

        var stub = new TrackingToolRunner();
        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(source, target, "chdman.exe", "createcd");

        Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
        Assert.Contains("chdman-unsupported-source", result.Reason!);
    }

    [Fact]
    public void Convert_ToolFailure_ReturnsError()
    {
        var source = CreateFile("game.cue", 100);
        var target = Path.Combine(_tempDir, "game.chd");

        var stub = new TrackingToolRunner();
        stub.NextResult = new ToolResult(1, "disc error\nsecondline", false);

        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(source, target, "chdman.exe", "createcd");

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Contains("chdman-failed", result.Reason!);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void Convert_ToolFailure_EmptyOutput_ReturnsErrorNoDetail()
    {
        var source = CreateFile("game.gdi", 100);
        var target = Path.Combine(_tempDir, "game.chd");

        var stub = new TrackingToolRunner();
        stub.NextResult = new ToolResult(2, "", false);

        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(source, target, "chdman.exe", "createcd");

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("chdman-failed", result.Reason);
    }

    [Fact]
    public void Convert_CueExtension_AcceptedDirectly()
    {
        var source = CreateFile("game.cue", 100);
        var target = Path.Combine(_tempDir, "game.chd");

        var stub = new TrackingToolRunner();
        stub.NextResult = new ToolResult(0, "", true);
        File.WriteAllBytes(target, new byte[1024]);

        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(source, target, "chdman.exe", "createcd");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
    }

    [Fact]
    public void Convert_BinExtension_AcceptedDirectly()
    {
        var source = CreateFile("game.bin", 100);
        var target = Path.Combine(_tempDir, "game.chd");

        var stub = new TrackingToolRunner();
        stub.NextResult = new ToolResult(0, "", true);
        File.WriteAllBytes(target, new byte[1024]);

        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(source, target, "chdman.exe", "createcd");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
    }

    [Fact]
    public void Convert_ImgExtension_AcceptedDirectly()
    {
        var source = CreateFile("game.img", 100);
        var target = Path.Combine(_tempDir, "game.chd");

        var stub = new TrackingToolRunner();
        stub.NextResult = new ToolResult(0, "", true);
        File.WriteAllBytes(target, new byte[1024]);

        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(source, target, "chdman.exe", "createcd");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
    }

    [Fact]
    public void Convert_SmallBin_CreatedvdBecomesCreatecd()
    {
        var source = CreateFile("small.bin", 400 * 1024 * 1024);
        var target = Path.Combine(_tempDir, "small.chd");

        var stub = new TrackingToolRunner();
        stub.NextResult = new ToolResult(0, "", true);
        File.WriteAllBytes(target, new byte[1024]);

        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(source, target, "chdman.exe", "createdvd");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.Contains("createcd", stub.LastArgs!);
    }

    [Fact]
    public void Convert_CreatecdCommand_NoHeuristicDowngrade()
    {
        // createcd should stay createcd regardless of file size
        var source = CreateFile("big.iso", 800 * 1024 * 1024);
        var target = Path.Combine(_tempDir, "big.chd");

        var stub = new TrackingToolRunner();
        stub.NextResult = new ToolResult(0, "", true);
        File.WriteAllBytes(target, new byte[1024]);

        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(source, target, "chdman.exe", "createcd");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.Contains("createcd", stub.LastArgs!);
    }

    #endregion

    #region Verify

    [Fact]
    public void Verify_ToolNotFound_ReturnsFalse()
    {
        var stub = new TrackingToolRunner();
        // No chdman registered
        var converter = new ChdmanToolConverter(stub);

        Assert.False(converter.Verify("some.chd"));
    }

    [Fact]
    public void Verify_ToolSuccess_ReturnsTrue()
    {
        var stub = new TrackingToolRunner();
        stub.RegisterTool("chdman", @"C:\tools\chdman.exe");
        stub.NextResult = new ToolResult(0, "verified", true);

        var converter = new ChdmanToolConverter(stub);

        Assert.True(converter.Verify("some.chd"));
    }

    [Fact]
    public void Verify_ToolFails_ReturnsFalse()
    {
        var stub = new TrackingToolRunner();
        stub.RegisterTool("chdman", @"C:\tools\chdman.exe");
        stub.NextResult = new ToolResult(1, "bad", false);

        var converter = new ChdmanToolConverter(stub);

        Assert.False(converter.Verify("some.chd"));
    }

    #endregion

    #region ZIP Archive Conversion

    [Fact]
    public void Convert_ZipWithSingleCue_Success()
    {
        var zipPath = CreateZipWithCue("single_cue.zip", ("game.cue", "FILE game.bin BINARY"), ("game.bin", "data"));
        var target = Path.Combine(_tempDir, "game.chd");

        var stub = new TrackingToolRunner();
        stub.NextResult = new ToolResult(0, "ok", true);

        // Create target to pass validation
        File.WriteAllBytes(target, new byte[1024]);

        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(zipPath, target, "chdman.exe", "createcd");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
    }

    [Fact]
    public void Convert_ZipWithNoDiscImage_Skipped()
    {
        var zipPath = CreateZipWithEntries("nodisk.zip", ("readme.txt", "just text"));
        var target = Path.Combine(_tempDir, "nodisk.chd");

        var stub = new TrackingToolRunner();
        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(zipPath, target, "chdman.exe", "createcd");

        Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
        Assert.Equal("archive-no-disc-image", result.Reason);
    }

    [Fact]
    public void Convert_ZipCorrupt_ReturnsError()
    {
        var zipPath = Path.Combine(_tempDir, "corrupt.zip");
        File.WriteAllBytes(zipPath, [0x50, 0x4B, 0x00, 0x00, 0xFF, 0xFF]); // Invalid ZIP

        var target = Path.Combine(_tempDir, "corrupt.chd");
        var stub = new TrackingToolRunner();
        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(zipPath, target, "chdman.exe", "createcd");

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Contains("corrupt", result.Reason!);
    }

    #endregion

    #region 7Z Archive Conversion

    [Fact]
    public void Convert_7z_NoToolFound_Skipped()
    {
        var source = CreateFile("game.7z", 100);
        var target = Path.Combine(_tempDir, "game.chd");

        var stub = new TrackingToolRunner();
        // No 7z registered
        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(source, target, "chdman.exe", "createcd");

        Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
        Assert.Contains("tool-not-found:7z", result.Reason!);
    }

    [Fact]
    public void Convert_7z_ExtractFails_Error()
    {
        var source = CreateFile("game.7z", 100);
        var target = Path.Combine(_tempDir, "game.chd");

        var stub = new TrackingToolRunner();
        stub.RegisterTool("7z", @"C:\tools\7z.exe");
        stub.NextResult = new ToolResult(1, "extraction error", false);

        var converter = new ChdmanToolConverter(stub);
        var result = converter.Convert(source, target, "chdman.exe", "createcd");

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("7z-extract-failed", result.Reason);
    }

    #endregion

    #region ExtractZipSafe (Security)

    [Fact]
    public void ExtractZipSafe_TooManyEntries_ReturnsError()
    {
        // Create ZIP with > MaxZipEntryCount entries
        var zipPath = Path.Combine(_tempDir, "bomb.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            for (int i = 0; i <= ChdmanToolConverter.MaxZipEntryCount; i++)
            {
                var entry = archive.CreateEntry($"entry_{i}.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("x");
            }
        }

        var extractDir = Path.Combine(_tempDir, "extract_bomb");
        var error = ChdmanToolConverter.ExtractZipSafe(zipPath, extractDir);

        Assert.NotNull(error);
        Assert.Contains("archive-too-many-entries", error);
    }

    [Fact]
    public void ExtractZipSafe_NormalZip_ReturnsNull()
    {
        var zipPath = CreateZipWithEntries("normal.zip", ("file1.txt", "hello"));
        var extractDir = Path.Combine(_tempDir, "extract_normal");

        var error = ChdmanToolConverter.ExtractZipSafe(zipPath, extractDir);

        Assert.Null(error);
        Assert.True(File.Exists(Path.Combine(extractDir, "file1.txt")));
    }

    #endregion

    #region ValidateExtractedContents

    [Fact]
    public void ValidateExtractedContents_CleanDir_ReturnsTrue()
    {
        var dir = Path.Combine(_tempDir, "clean_dir");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "file.txt"), "clean");

        Assert.True(ChdmanToolConverter.ValidateExtractedContents(dir));
    }

    [Fact]
    public void ValidateExtractedContents_EmptyDir_ReturnsTrue()
    {
        var dir = Path.Combine(_tempDir, "empty_dir");
        Directory.CreateDirectory(dir);

        Assert.True(ChdmanToolConverter.ValidateExtractedContents(dir));
    }

    #endregion

    #region CleanupPartialOutput

    [Fact]
    public void CleanupPartialOutput_ExistingFile_Deletes()
    {
        var path = CreateFile("partial.chd", 100);
        Assert.True(File.Exists(path));

        ChdmanToolConverter.CleanupPartialOutput(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CleanupPartialOutput_NonExistentFile_NoThrow()
    {
        ChdmanToolConverter.CleanupPartialOutput(Path.Combine(_tempDir, "doesnotexist.chd"));
        // Should not throw
    }

    #endregion

    #region Constructor Guard

    [Fact]
    public void Ctor_NullTools_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ChdmanToolConverter(null!));
    }

    #endregion

    #region Helpers

    private string CreateFile(string name, long sizeBytes)
    {
        var path = Path.Combine(_tempDir, name);
        using var fs = File.Create(path);
        fs.SetLength(sizeBytes);
        return path;
    }

    private string CreateZipWithEntries(string zipName, params (string Name, string Content)[] entries)
    {
        var zipPath = Path.Combine(_tempDir, zipName);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }
        return zipPath;
    }

    private string CreateZipWithCue(string zipName, params (string Name, string Content)[] entries)
        => CreateZipWithEntries(zipName, entries);

    /// <summary>
    /// Tracking IToolRunner that records invocations and allows configuring results.
    /// </summary>
    private sealed class TrackingToolRunner : IToolRunner
    {
        private readonly Dictionary<string, string> _tools = new(StringComparer.OrdinalIgnoreCase);
        public ToolResult NextResult { get; set; } = new(0, "", true);
        public string[]? LastArgs { get; private set; }
        public string? LastFilePath { get; private set; }

        public void RegisterTool(string name, string path) => _tools[name] = path;
        public string? FindTool(string toolName) => _tools.GetValueOrDefault(toolName);

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
        {
            LastFilePath = filePath;
            LastArgs = arguments;
            return NextResult;
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments) => NextResult;
    }

    #endregion
}
